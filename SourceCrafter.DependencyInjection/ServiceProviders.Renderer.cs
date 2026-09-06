using Microsoft.CodeAnalysis;
using SourceCrafter.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

/// <summary>
/// Efectos que la renderizacion de cada miembro deja para el emisor del contenedor:
/// que campos hay que inicializar en el constructor y que hay que liberar al final.
/// Sustituye a la lista de parametros de salida que arrastraba <c>AppendMethod</c>.
/// </summary>
internal sealed class ContainerRenderContext
{
    /// <summary>Resolvers <see cref="Lifetime.Scoped"/>; solo se usa como recuento.</summary>
    internal readonly List<string> ScopedMembers = [];

    /// <summary>
    /// Candados de instancia de los resolvers <b>asincronos</b> cacheados, que conservan el
    /// esquema de un candado por dependencia. Ya no se asignan en el constructor: crear un
    /// ambito pagaba un candado por cada servicio scoped declarado aunque nunca se resolviera
    /// ninguno. La lista solo sirve para decidir si hay que emitir el ayudante <c>__EnsureLock</c>.
    /// </summary>
    internal readonly List<string> InstanceLockFields = [];

    /// <summary>Tipo con el que se declaran los candados de este contenedor.</summary>
    internal string InstanceLockTypeName = "object";

    /// <summary>
    /// Algun resolver sincrono cacheado de vida <see cref="Lifetime.Singleton"/> se rindio con
    /// el esquema compartido, asi que hay que emitir el candado estatico unico del contenedor.
    /// Los scoped no necesitan campo: usan <c>this</c>.
    /// </summary>
    internal bool NeedsSingletonLock;

    internal readonly List<DisposeBuilder> SingletonDisposers = [], ScopedDisposers = [];

    /// <summary>Algun resolver consume el token de vida del proveedor.</summary>
    internal bool UsesLifetimeToken;
}

/// <summary>
/// Estado <b>congelado</b> de un resolver, listo para renderizar.
///
/// <para>No contiene <see cref="ISymbol"/>, <c>SyntaxNode</c>, <c>SemanticModel</c> ni
/// <c>Compilation</c>: solo cadenas, banderas y enumeraciones. Asi los delegados que el
/// emisor cachea entre pasadas incrementales no mantienen viva la compilacion de Roslyn.</para>
///
/// <para>Todos sus miembros son de solo inicializacion: se construye de una vez en
/// <c>CommitRenderState</c> y nunca se muta despues. El analizador usa
/// <see cref="ResolverRendererRef"/> para poder tomar delegados antes de tenerlo completo.</para>
/// </summary>
internal sealed class ResolverRenderer
{
    internal const string
        LifetimeCtsFieldName = "__lifetimeCts",
        LifetimeTokenFieldName = "__lifetimeToken",
        EnsureLockMethodName = "__EnsureLock",
        SingletonLockFieldName = "__singletonLock";

    internal string typeFullName { get; init; } = string.Empty;
    internal string exportTypeFullName { get; init; } = string.Empty;
    internal string factoryProviderName { get; init; } = string.Empty;
    internal string backingFieldName { get; init; } = string.Empty;
    internal string methodName { get; init; } = string.Empty;

    internal string? factoryName { get; init; }

    internal AsyncKind AsyncKind { get; init; }
    internal AsyncKind initialAsyncType { get; init; }

    internal Lifetime lifetime { get; init; }
    internal Disposability disposability { get; init; }

    /// <summary>Enumeracion por valor: no retiene la compilacion.</summary>
    internal SymbolKind factoryKind { get; init; }

    internal bool isCached { get; init; }
    internal bool isFactory { get; init; }
    internal bool isExternal { get; init; }
    internal bool hasNoCachedDeps { get; init; }
    internal bool hasAsyncDependencies { get; init; }
    internal bool needsCancelToken { get; init; }
    internal bool isStaticFactory { get; init; }
    internal bool isFactoryFromCurrentProvider { get; init; }
    internal bool isFactoryIndexerProperty { get; init; }
    internal bool isInterfaceProvider { get; init; }
    internal bool typeIsValueType { get; init; }
    internal bool typeIsNonNullable { get; init; }
    internal bool hasFactorySymbol { get; init; }

    /// <summary>
    /// Tipo con el que se declaran los candados: <c>global::System.Threading.Lock</c>
    /// cuando el proyecto destino lo admite, o <c>object</c> en caso contrario. Lo decide
    /// el parser, que es quien puede consultar la compilacion.
    /// </summary>
    internal string lockTypeName { get; init; } = "object";

    internal IReadOnlyList<ParamBuildOptions> appendParams { get; init; } = [];

    internal IReadOnlyDictionary<DependencyKey, AsyncLocalResolver> asyncLocalResolvers { get; init; }
        = new Dictionary<DependencyKey, AsyncLocalResolver>();

    /// <summary>
    /// Nombre del candado que protege al campo de respaldo. Debe tener el mismo alcance
    /// que el campo: un campo <c>static</c> compartido por todas las instancias vigilado
    /// con <c>lock(this)</c> no ofrece exclusion alguna, porque cada instancia bloquea un
    /// objeto distinto. Ademas, <c>lock(this)</c> expone el candado a codigo ajeno.
    /// </summary>
    internal string LockFieldName => backingFieldName + "Lock";

    /// <summary>
    /// Nombre del metodo que contiene el camino lento (tomar el candado y construir).
    /// </summary>
    internal string SlowPathMethodName => "__Create" + backingFieldName;

    /// <summary>
    /// Los resolvers <b>sincronos</b> cacheados comparten un unico candado por lifetime
    /// (<c>this</c> para scoped, un estatico del contenedor para singleton) y resuelven sus
    /// dependencias cacheadas <b>antes</b> de tomarlo. Al no retener nunca un candado mientras
    /// adquieren otro, el grafo de espera queda sin aristas y no puede haber interbloqueo.
    ///
    /// <para>Los asincronos conservan el candado por dependencia: su renderizado emite locales
    /// de tarea dentro de la region protegida, asi que todavia resuelven ahi dentro. Un candado
    /// por dependencia sobre un grafo aciclico se adquiere en orden topologico, que es un orden
    /// global consistente, y tampoco se interbloquea. Mezclar ambos es seguro precisamente
    /// porque el lado sincrono iza.</para>
    /// </summary>
    internal bool UsesSharedLock => AsyncKind is 0;

    /// <summary>
    /// Expresion que se pasa a <c>lock(...)</c>. Debe tener el mismo alcance que el campo:
    /// un campo <c>static</c> vigilado con <c>lock(this)</c> no ofrece exclusion alguna,
    /// porque cada instancia bloquearia un objeto distinto.
    /// </summary>
    internal string LockExpression =>
        UsesSharedLock
            ? lifetime is Lifetime.Singleton ? SingletonLockFieldName : "this"
            : lifetime is Lifetime.Singleton
                ? LockFieldName
                : EnsureLockMethodName + "(ref " + LockFieldName + ")";

    /// <summary>
    /// Todo el proveedor comparte un unico token, copiado una sola vez en un campo de
    /// solo lectura. Ya no se aceptan tokens por parametro: un valor cacheado se entrega
    /// a todos los llamadores, asi que grabar en el el token del primero seria incorrecto.
    /// </summary>
    internal void AppendCancelToken(StringBuilder code, bool _, bool __)
    {
        code.Append(LifetimeTokenFieldName);
    }

            /// <summary>
            /// Sangria base de los parametros en formato alto. Cada nivel de anidamiento
            /// suma un tabulador, de modo que el arbol de <c>new</c> anidados se lea como
            /// el arbol que realmente es.
            /// </summary>
            internal const string ParamIndent = @"
				";

            /// <summary>
            /// Hay algun parametro que adquiere candados y por tanto debe resolverse antes
            /// de que este resolver tome el suyo.
            /// </summary>
            internal bool HasHoistedParams
            {
                get
                {
                    foreach (var item in appendParams) if (item.MustHoist) return true;
                    return false;
                }
            }

            /// <summary>
            /// Emite <c>var __aN = ...;</c> para cada parametro que adquiere candados, de modo
            /// que el <c>lock</c> posterior no encierre ninguna otra adquisicion. Es la condicion
            /// que hace seguro compartir un unico candado por lifetime.
            /// <para>
            /// Contrapartida: si dos hilos entran a la vez al camino lento, ambos izan. Los
            /// cacheados devuelven la misma instancia, pero un transient izado (porque arrastra
            /// cacheados) se construye dos veces y una se descarta. Solo puede ocurrir en la
            /// primera resolucion y los transients no se rastrean, asi que se acepta a cambio
            /// de no volver a un candado por dependencia.
            /// </para>
            /// </summary>
            internal void AppendHoistedLocals(StringBuilder code)
            {
                var any = false;
                byte pos = 0;

                foreach (var item in appendParams)
                {
                    if (item.MustHoist)
                    {
                        any = true;

                        code.Append(@"
		var __a").Append(pos).Append(" = ");

                        item.Append(code, false, false, ParamIndent);

                        code.Append(';');
                    }

                    pos++;
                }

                if (any) code.Append(@"
");
            }

            internal void AppendParams(
                StringBuilder code,
                bool completeAsyncContext,
                bool appendInterceptorProvider,
                string newIndentedLine,
                bool useHoisted = false)
            {
                var needsComma = false;
                byte pos = 0;
                var nestedIndentedLine = newIndentedLine + "\t";

                foreach (var item in appendParams)
                {
                    if (needsComma.Exchange(true)) code.Append(',');

                    code.Append(newIndentedLine);

                    if (item.StartsCollectionExpression) code.Append('[');

                    if (useHoisted && item.MustHoist)
                    {
                        code.Append("__a").Append(pos);
                    }
                    else if (asyncLocalResolvers.TryGetValue(item.Key, out var task))
                    {
                        // Se espera en el propio sitio del parametro. Antes un
                        // 'await Task.WhenAll(...)' precedia a la construccion y todos los
                        // parametros leian '.Result'; medido, WhenAll costaba ~3.8x mas
                        // tiempo y 2.5x mas memoria por el 'Task[]' de params y su promesa,
                        // sin aportar concurrencia: las tareas ya estan arrancadas.
                        if (completeAsyncContext && !task.ResolvedBefore)
                        {
                            code.Append("await ");
                            code.Append("__v").Append(pos).Append(".ConfigureAwait(false)");
                        }
                        else
                        {
                            // Ya completada por el subarbol de un parametro anterior: leerla
                            // no suspende.
                            code.Append("__v").Append(pos).Append(".Result");
                            if (completeAsyncContext && task.ResolvedBefore)
                                code.Append($" /* resolved previously by param {task.ResolvedByParamIndex} */");
                        }
                    }
                    else
                    {
                        item.Append(code, completeAsyncContext, false, nestedIndentedLine);
                    }

                    if (item.EndsCollectionExpression) code.Append(']');

                    pos++;
                }
            }

			internal void AppendMethod(StringBuilder code, ContainerRenderContext ctx)
			{
				if (needsCancelToken) ctx.UsesLifetimeToken = true;

				if (isCached)
				{
					var isSharedAcrossInstances = lifetime is Lifetime.Singleton;

					code.Append(@"
	private ");
					if (isSharedAcrossInstances) code.Append("static ");

					code.Append(GetTypeName(typeFullName)).Append("? ").Append(backingFieldName).Append(';');

					// El candado tiene el mismo alcance que el campo que protege.
					//
					// Los resolvers sincronos comparten un unico candado por lifetime: 'this'
					// para scoped y un estatico del contenedor para singleton. Es correcto
					// porque el camino lento iza fuera del 'lock' todo lo que adquiere candados,
					// asi que nadie retiene uno mientras pide otro. Medido: un ciclo completo de
					// ambito baja de 43,33 ns / 192 B a 30,63 ns / 96 B, porque cada
					// System.Threading.Lock que dejamos de asignar son 40 B.
					//
					// Los asincronos conservan el candado por dependencia porque todavia
					// resuelven dentro de la region protegida. Se crean de forma perezosa: si
					// se asignaran en el constructor, cada CreateScope() pagaria un candado por
					// servicio scoped declarado aunque el ambito no resolviera ninguno.
					ctx.InstanceLockTypeName = lockTypeName;

					if (UsesSharedLock)
					{
						if (isSharedAcrossInstances) ctx.NeedsSingletonLock = true;
					}
					else
					{
						code.Append(@"
	private ");
						if (isSharedAcrossInstances) code.Append("static readonly ");

						code.Append(lockTypeName);

						if (isSharedAcrossInstances)
						{
							code.Append(' ').Append(LockFieldName).Append(" = new()");
						}
						else
						{
							code.Append("? ").Append(LockFieldName);
							ctx.InstanceLockFields.Add(LockFieldName);
						}

						code.Append(';');
					}

					// El camino lento vive en su propio metodo. Si el 'lock' se queda en el
					// cuerpo del getter, este pasa de ser una lectura de campo a un metodo
					// con region protegida y el JIT deja de insertarlo en linea: medido,
					// 0,83 ns frente a 0,56 ns por resolucion. El atributo evita que el JIT
					// lo vuelva a fusionar.
					if (UsesSharedLock)
					{
						code.Append(@"
	[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	private ");
						// No se marca 'static' aunque el campo lo sea: la construccion puede
						// depender de resolvers de instancia (el generador permite que un
						// singleton dependa de un scoped), y eso seria CS0120.
						code.Append(GetTypeName(typeFullName)).Append(' ').Append(SlowPathMethodName).Append(@"()
	{");

						AppendHoistedLocals(code);

						code.Append(@"
		lock(").Append(LockExpression).Append(@")
		{
			return ").Append(backingFieldName).Append(@" ??= ");

						if (isFactory) AppendFactoryCaller(code, false, false, null, useHoisted: true);
						else AppendInstance(code, false, false, null, useHoisted: true);

						code.Append(@";
		}
	}
");
					}

					if (disposability is not 0)
					{
						if (lifetime is Lifetime.Scoped)
							ctx.ScopedDisposers
								.Add(disposability is Disposability.Disposable ? AppendDisposerStatement : AppendAsyncDisposerStatment);
						else if (lifetime is Lifetime.Singleton)
							ctx.SingletonDisposers
								.Add(disposability is Disposability.Disposable ? AppendDisposerStatement : AppendAsyncDisposerStatment);
					}
				}

				code.Append(@"
	public ");

				AppendSignature(code);

				if (lifetime is Lifetime.Scoped) ctx.ScopedMembers.Add(methodName);

				if (!isCached && !hasAsyncDependencies)
                {
                    code.Append(@" 
        => ");

                    if (isFactory)
                    {
                        AppendFactoryCaller(code, true);
                    }
                    else
                    {
                        AppendInstance(code, true);
                    }
                    code.Append(@";
");
                }
                else
                {
                    code.Append(@"
    {");

					if (AsyncKind is 0 || !(hasAsyncDependencies || needsCancelToken))
					{
						if (AsyncKind > 0)
						{
							var unwraps = AsyncKind is AsyncKind.ValueTask;

							// El valor cacheado es una tarea. Se comparte tal cual mientras siga
							// en vuelo (!IsCompleted) o haya terminado con exito, de modo que N
							// llamadores esperen una sola ejecucion. Si termino mal (fallada o
							// cancelada) no se devuelve: se cae a la asignacion de abajo, que la
							// sustituye por una nueva. Asi un fallo transitorio de la fabrica no
							// envenena el contenedor de por vida.
							//
							// La asignacion es '=' y no '??=' precisamente para sobrescribir la
							// tarea fallida; por eso tampoco hace falta anular el campo antes.
							code.Append(@"
		get
		{
			if(").Append(backingFieldName).Append(@" is { IsCompletedSuccessfully: true }) return ").Append(backingFieldName);

							if (unwraps) code.Append(".Value");

							code.Append(@";

			lock(").Append(LockExpression).Append(@")
			{
				if(").Append(backingFieldName).Append(@" is { } __cached
					&& (!__cached.IsCompleted || __cached.IsCompletedSuccessfully)) return __cached;

				return ");

							// El campo de una ValueTask cacheada es 'ValueTask<T>?'. '??=' se
							// evaluaba al tipo subyacente; una asignacion normal no.
							if (unwraps) code.Append('(');

							code.Append(backingFieldName).Append(" = ");

							if (isFactory)
							{
								AppendFactoryCaller(code, false);
							}
							else
							{
								AppendInstance(code, false);
							}

							if (unwraps) code.Append(").Value");

							code.Append(@";
			}
		}");
						}
						else
						{
							// Se lee el campo una sola vez, a un local. Dos lecturas separadas
							// pueden ver valores distintos; con Nullable<T> eso llega a devolver
							// un HasValue de una lectura y un Value de otra. Medido, el local no
							// cuesta nada (0,5607 ns frente a 0,5716) y ademas quita la varianza
							// que el JIT introducia al rematerializar la segunda lectura.
							code.Append(@"
		get
		{
			var __v = ").Append(backingFieldName).Append(@";

			if(__v").Append(typeIsValueType ? ".HasValue" : " is not null").Append(") return __v");

							if (typeIsValueType) code.Append(".Value");

							code.Append(@";

			return ").Append(SlowPathMethodName).Append(@"();
		}");
						}
					}
					else
					{
						var indent = isCached ? "\t" : null;

						if (isCached)
						{
							code.Append(@"
		if(").Append(backingFieldName).Append(@" is { IsCompletedSuccessfully: true }) return ").Append(backingFieldName);

							if (AsyncKind is AsyncKind.ValueTask) code.Append(".Value");

							code.Append(';');
						}

						{
							var hasAsyncLocalResolvers = asyncLocalResolvers.Count > 0;

							if (isCached)
							{
								// Recomprobacion dentro del candado.
								//
								// El camino rapido de arriba solo devuelve tareas ya completadas con
								// exito, asi que una tarea *en vuelo* llega hasta aqui: se devuelve
								// tal cual para que todos los llamadores esperen la misma y una sola
								// vez. Sin esta comprobacion se resolverian de nuevo las dependencias
								// antes de descubrir que ya habia tarea.
								//
								// Si termino mal (fallida o cancelada) no se devuelve: se cae al
								// codigo de abajo, que reasigna el campo con una tarea nueva. Asi un
								// fallo transitorio de la fabrica no envenena el contenedor para
								// siempre. No hace falta anular el campo aqui: la asignacion de abajo
								// es '=' y no '??=' precisamente para sobrescribir la fallida.
								code.Append(@"

		lock(").Append(LockExpression).Append(@")
		{
			if(").Append(backingFieldName).Append(@" is { } __cached
				&& (!__cached.IsCompleted || __cached.IsCompletedSuccessfully)) return __cached;
");
							}
							//ct = global::System.Threading.CancellationTokenSource.CreateLinkedTokenSource(ct, __scopedCancellationTokenSrc.Token).Token;

                            if (hasAsyncLocalResolvers)
                            {
                                foreach (var resolver in asyncLocalResolvers.Values)
                                {
                                    if (resolver.ParamIndex > -1) resolver.AppendAsyncLocal(code, indent);
                                }

                                code.Append(@"
");
                            }

                            code.Append(@"
		").Append(indent);

                            code.Append("return ");

                            // '=' y no '??=': tras la recomprobacion de arriba el campo es null o
                            // contiene una tarea que termino mal, y en ambos casos hay que
                            // escribirlo. Un '??=' devolveria la tarea fallida y la dejaria
                            // cacheada para siempre.
                            //
                            // El campo de una ValueTask cacheada es 'ValueTask<T>?'. '??=' se
                            // evaluaba al tipo subyacente; una asignacion normal no, asi que hay
                            // que envolverla y desempaquetarla con '.Value'.
                            var unwrapsNullableValueTask = isCached && AsyncKind is AsyncKind.ValueTask;

                            if (isCached)
                            {
                                if (unwrapsNullableValueTask) code.Append('(');

                                code.Append(backingFieldName).Append(" = ");
                            }

                            if (hasAsyncLocalResolvers)
                            {
                                var useAnd = false;

                                foreach (var param in asyncLocalResolvers.Values)
                                {
                                    if (param.ParamIndex == -1) continue;
                                    if (useAnd.Exchange(true)) code.Append(@"
				").Append(indent).Append("&& ");

                                    code.Append("__v").Append(param.ParamIndex).Append(".IsCompletedSuccessfully");
                                }

                                code.Append(@"
		    ").Append(indent).Append("? global::System.Threading.Tasks.Task.FromResult<").Append(exportTypeFullName).Append(@">(
				").Append(indent);
                            }

                            if (isFactory)
                            {
                                AppendFactoryCaller(code, false, false, @"
						");
                            }
                            else
                            {
                                AppendInstance(code, false, false, @"
						");
                            }

                            if (isCached && !hasAsyncLocalResolvers)
                            {
                                if (unwrapsNullableValueTask) code.Append(").Value");

                                code.Append(';');
                                goto exitLock;
                            }
                            else
                            {
                                code.Append(')');
                            }

                            code.Append(@"
            ").Append(indent).Append(": ResolveCoreAsync()");

                            if (unwrapsNullableValueTask) code.Append(").Value");

                            code.Append(@";

		").Append(indent).Append("async global::System.Threading.Tasks.Task<").Append(exportTypeFullName).Append(@"> ResolveCoreAsync()
		").Append(indent).Append('{');

							code.Append(@"				
			").Append(indent).Append("return ");

                            if (isFactory)
                            {
                                AppendFactoryCaller(code, true, false, @"
				" + indent);
                            }
                            else
                            {
                                AppendInstance(code, true, false, @"
				" + indent);
                            }

                            code.Append(@";
		").Append(indent).Append('}');
                            exitLock:
                            if (isCached) code.Append(@"
		}");
                        }
                    }
                    code.Append(@"
    }
");
                }
            }

            const string disposerIndent = @"
        ",
                         disposerInnerIndent = @"
            ";

            /// <summary>
            /// Cada liberador se emite en su propio bloque para poder reutilizar el
            /// nombre <c>__disposing</c>. El campo de respaldo se anula <b>antes</b> de
            /// liberar, de modo que el contenedor no pueda volver a entregar una
            /// instancia ya desechada.
            /// </summary>
            void AppendDisposerBlock(StringBuilder code, Action<StringBuilder> appendStatement)
            {
                code.Append(disposerIndent).Append('{')
                    .Append(disposerInnerIndent)
                    .Append("var __disposing = ").Append(backingFieldName).Append(';')
                    .Append(disposerInnerIndent)
                    .Append(backingFieldName).Append(" = null;")
                    .Append(disposerInnerIndent);

                appendStatement(code);

                code.Append(disposerIndent).Append('}');
            }

            internal void AppendDisposerStatement(StringBuilder code, bool awaits = true)
            {
                AppendDisposerBlock(code, c =>
                {
                    if (AsyncKind > 0)
                    {
                        // El valor cacheado vive dentro de una Task/ValueTask: hay que
                        // desenvolverlo, liberar la tarea invocaría Task.Dispose().
                        c.Append(awaits ? "await " : "return ").Append("__disposing.TryDispose();");
                    }
                    else
                    {
                        c.Append("__disposing?.Dispose();");
                    }
                });
            }

            internal void AppendAsyncDisposerStatment(StringBuilder code, bool awaits = true)
            {
                AppendDisposerBlock(code, c =>
                {
                    if (AsyncKind > 0)
                    {
                        c.Append(awaits ? "await " : "return ").Append("__disposing.TryDisposeAsync();");
                    }
                    else if (awaits)
                    {
                        c.Append("if(__disposing")
                            .Append(typeIsValueType ? ".HasValue) " : " is not null) ")
                            .Append("await __disposing");

                        if (typeIsValueType) c.Append(".Value");

                        c.Append(".DisposeAsync();");
                    }
                    else if (typeIsValueType)
                    {
                        c.Append("return __disposing.HasValue ? __disposing.Value.DisposeAsync() : default;");
                    }
                    else
                    {
                        c.Append("return __disposing?.DisposeAsync() ?? default;");
                    }
                });
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal void AppendSignature(StringBuilder code)
            {
                code.Append(GetTypeName(exportTypeFullName)).Append(' ').Append(methodName);

                // El token ya no viaja por parametro: se lee del campo del proveedor.
                if (IsMethodShaped) code.Append("()");
            }

            /// <summary>
            /// Los resolvers asincronos que componen dependencias se emiten como metodos;
            /// el resto, como propiedades.
            /// </summary>
            internal bool IsMethodShaped => AsyncKind is not 0 && (hasAsyncDependencies || needsCancelToken);

            internal string GetTypeName(string typeName)
            {
                return AsyncKind > 0
                    ? $"global::System.Threading.Tasks.{(initialAsyncType is AsyncKind.ValueTask ? "Value" : null)}Task<{exportTypeFullName}>"
                    : typeName;
            }

            internal void AppendValue(StringBuilder code, bool asyncContext = false, bool interceptorContext = false, string? newIndentedLine = null)
            {
                if (!interceptorContext && !isCached && isFactory)
                {
                    //if (asyncContext && AsyncKind is not 0) code.Append("await ");

                    AppendFactoryCaller(code, asyncContext, false, newIndentedLine);
                }
                else if (hasNoCachedDeps && !isCached && !isExternal)
                {
                    AppendInstance(code, asyncContext, interceptorContext, newIndentedLine);
                }
                else
                {
                    //if (asyncContext && (AsyncKind is not 0)) code.Append("await ");

                    AppendCachedCaller(code, interceptorContext);
                }
            }

            internal void AppendCachedCaller(StringBuilder code, bool interceptorContext = false, string? newIndentedLine = null)
            {
                if (interceptorContext) code.Append("provider.");
                code.Append(methodName);

                if (IsMethodShaped) code.Append("()");
            }

            internal void AppendInstance(StringBuilder code, bool isAsyncContext, bool appendInterceptorProvider = false, string? newIndentedLine = null, bool useHoisted = false)
            {
                code.Append("new ")
                    .Append(typeFullName)
                    .Append('(');

                AppendParams(code, isAsyncContext, appendInterceptorProvider, newIndentedLine ?? ParamIndent, useHoisted);

                code.Append(')');
            }

            internal void AppendFactoryCaller(
                StringBuilder code,
                bool allowAwait,
                bool appendInterceptorProvider = false,
                string? newIndentedLine = null,
                bool useHoisted = false)
            {
                newIndentedLine ??= ParamIndent;

                switch (factoryKind)
                {
                    case SymbolKind.Method:

                        AppendFactoryContainingType(code, appendInterceptorProvider);

                        //if (initialAsyncType > 0)
                        //{
                        //    code.Append(factoryName)
                        //        .Append('<')
                        //        .Append(exportTypeFullName)
                        //        .Append(">(");

                        //    AppendParams(code, allowAwait, newIndentedLine);

                        //    code.Append(')');
                        //}
                        //else
                        //{
                        code.Append(factoryName)
                            .Append('(');

                        AppendParams(code, allowAwait, appendInterceptorProvider, newIndentedLine, useHoisted);

                        code.Append(')');
                        //}

                        break;

                    case SymbolKind.Property:

                        AppendFactoryContainingType(code, appendInterceptorProvider);

                        if (isFactoryIndexerProperty)
                        {
                            code.Append(factoryName)
                                .Append('[');

                            AppendParams(code, allowAwait, appendInterceptorProvider, newIndentedLine, useHoisted);

                            code.Append(']');
                        }
                        else
                        {
                            code.Append(factoryName);
                        }

                        break;


                    case SymbolKind.Field:

                        AppendFactoryContainingType(code, appendInterceptorProvider);

                        code.Append(factoryName);

                        break;

                    default:

                        AppendDefault(code);

                        break;
                }
            }

            internal void AppendFactoryContainingType(StringBuilder code, bool appendInterceptorProvider)
            {
                if (isFactoryFromCurrentProvider && !isInterfaceProvider)
                    return;

                if (appendInterceptorProvider)
                    code.Append("provider.");
                if (isInterfaceProvider && !isStaticFactory)
                    code.Append("((").Append(factoryProviderName).Append(")this)").Append('.');
                else if (isStaticFactory)
                    code.Append(factoryProviderName).Append('.');
            }

            internal void AppendDefault(StringBuilder code, bool _ = false, bool __ = false)
            {
                code.Append("default");

                if (typeIsNonNullable) code.Append('!');
            }

            /// <summary>
            /// Igualdad estructural sobre el estado proyectado. Los delegados de los
            /// parametros quedan fuera a proposito: son funciones derivadas del mismo
            /// analisis, de modo que si todo lo demas coincide el texto emitido tambien.
            /// Compararlos por identidad haria que dos pasadas incrementales nunca se
            /// consideraran iguales, que es justo lo que se quiere evitar.
            /// </summary>
            public override bool Equals(object? obj)
            {
                return ReferenceEquals(this, obj)
                    || (obj is ResolverRenderer other
                        && typeFullName == other.typeFullName
                        && exportTypeFullName == other.exportTypeFullName
                        && factoryProviderName == other.factoryProviderName
                        && backingFieldName == other.backingFieldName
                        && methodName == other.methodName
                        && factoryName == other.factoryName
                        && AsyncKind == other.AsyncKind
                        && initialAsyncType == other.initialAsyncType
                        && lifetime == other.lifetime
                        && disposability == other.disposability
                        && factoryKind == other.factoryKind
                        && isCached == other.isCached
                        && isFactory == other.isFactory
                        && isExternal == other.isExternal
                        && hasNoCachedDeps == other.hasNoCachedDeps
                        && hasAsyncDependencies == other.hasAsyncDependencies
                        && needsCancelToken == other.needsCancelToken
                        && isStaticFactory == other.isStaticFactory
                        && isFactoryFromCurrentProvider == other.isFactoryFromCurrentProvider
                        && isFactoryIndexerProperty == other.isFactoryIndexerProperty
                        && isInterfaceProvider == other.isInterfaceProvider
                        && typeIsValueType == other.typeIsValueType
                        && typeIsNonNullable == other.typeIsNonNullable
                        && hasFactorySymbol == other.hasFactorySymbol
                        && lockTypeName == other.lockTypeName
                        && appendParams.Count == other.appendParams.Count
                        && asyncLocalResolvers.Count == other.asyncLocalResolvers.Count);
            }

            public override int GetHashCode()
            {
                var hash = new HashCode();

                hash.Add(typeFullName);
                hash.Add(exportTypeFullName);
                hash.Add(lockTypeName);
                hash.Add(backingFieldName);
                hash.Add(methodName);
                hash.Add(factoryName);
                hash.Add(AsyncKind);
                hash.Add(lifetime);
                hash.Add(disposability);
                hash.Add(isCached);
                hash.Add(needsCancelToken);
                hash.Add(appendParams.Count);
                hash.Add(asyncLocalResolvers.Count);

                return hash.ToHashCode();
            }
}

/// <summary>
/// Celda que permite tomar delegados del renderizador antes de que este exista.
///
/// <para>El analizador necesita referenciar <c>AppendValue</c> o <c>AppendCancelToken</c>
/// mientras aun esta descubriendo dependencias, pero el renderizador solo puede
/// construirse cuando ya se conoce todo. Esta indireccion resuelve el huevo y la gallina
/// sin renunciar a que <see cref="ResolverRenderer"/> sea inmutable: lo mutable es la
/// celda, no el modelo.</para>
/// </summary>
internal sealed class ResolverRendererRef
{
    internal ResolverRenderer Value = null!;

    internal void AppendValue(StringBuilder code, bool asyncContext, bool interceptorContext, string? newIndentedLine = null)
        => Value.AppendValue(code, asyncContext, interceptorContext, newIndentedLine);

    internal void AppendCancelToken(StringBuilder code, bool asyncContext, bool interceptorContext, string? newIndentedLine = null)
        => Value.AppendCancelToken(code, asyncContext, interceptorContext);

    internal void AppendDefault(StringBuilder code, bool asyncContext = false, bool interceptorContext = false, string? newIndentedLine = null)
        => Value.AppendDefault(code, asyncContext, interceptorContext);

    internal void AppendMethod(StringBuilder code, ContainerRenderContext ctx)
        => Value.AppendMethod(code, ctx);
}

/// <summary>Renderiza el propio contenedor (o su raiz) como argumento.</summary>
internal sealed class SelfProviderAppender(bool asksForRoot)
{
    internal void Append(StringBuilder code, bool _, bool __, string? ___ = null) => code.Append(asksForRoot ? "Root" : "this");
}

/// <summary>Renderiza el valor de un parametro adaptando el modo asincrono.</summary>
internal sealed class ParamValueAppender(
    ResolverBuilder foundService,
    AppendValue appendParam,
    string foundExportTypeFullName,
    AsyncKind paramAsyncType)
{
    internal void Append(StringBuilder code, bool asyncContext, bool _, string? newIndentedLine = null)
    {
        var awaits = asyncContext && foundService.AsyncKind is not 0 && paramAsyncType is 0;

        if (awaits)
        {
            code.Append("await ");
            appendParam(code, asyncContext, false, newIndentedLine);
        }
        else if (paramAsyncType is not 0 && foundService.AsyncKind is 0)
        {
            if (paramAsyncType is AsyncKind.Task)
            {
                code.Append("global::System.Threading.Tasks.Task.FromResult<")
                    .Append(foundExportTypeFullName)
                    .Append(">(");
                appendParam(code, asyncContext, false, newIndentedLine);
                code.Append(')');
            }
            else
            {
                code.Append("new global::System.Threading.Tasks.ValueTask<")
                    .Append(foundExportTypeFullName)
                    .Append(">(");
                appendParam(code, asyncContext, false, newIndentedLine);
                code.Append(')');
            }
        }
        else if (foundService.AsyncKind is AsyncKind.ValueTask && paramAsyncType is AsyncKind.Task)
        {
            appendParam(code, asyncContext, false, newIndentedLine);
            code.Append(".AsTask()");
        }
        else if (foundService.AsyncKind is AsyncKind.Task && paramAsyncType is AsyncKind.ValueTask)
        {
            code.Append("new global::System.Threading.Tasks.ValueTask<")
                .Append(foundExportTypeFullName)
                .Append(">(");
            appendParam(code, asyncContext, false, newIndentedLine);
            code.Append(')');
        }
        else
        {
            appendParam(code, asyncContext, false, newIndentedLine);
        }
    }
}

/// <summary>Declara la variable local <c>__vN</c> que materializa una dependencia asincrona.</summary>
internal sealed class AsyncLocalAppender(int paramIndex, AppendValue appendParam, string prefix)
{
    internal void Append(StringBuilder code, string? indent)
    {
        code.Append(prefix).Append(indent).Append("var __v").Append(paramIndex).Append(" = ");

        appendParam(code);

        code.Append(';');
    }
}
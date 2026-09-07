
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SourceCrafter.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

#pragma warning disable RS1041 // Las extensiones del compilador deben implementarse en ensamblados que tengan como destino netstandard2.0
[Generator(LanguageNames.CSharp)]
#pragma warning restore RS1041 // Las extensiones del compilador deben implementarse en ensamblados que tengan como destino netstandard2.0
#pragma warning disable CA1050 // Declarar tipos en espacios de nombres
internal sealed partial class ServiceProviders : IIncrementalGenerator
#pragma warning restore CA1050 // Declarar tipos en espacios de nombres
{
    internal const string
        BaseAttributesNS = "SourceCrafter.DependencyInjection.Attributes",
        GlobalBaseAttributeNS = $"global::{BaseAttributesNS}",
        ServiceProviderFullTypeName = $"{BaseAttributesNS}.ServiceProviderAttribute",
        CancelTokenFQMetaName = "global::System.Threading.CancellationToken",
        EnumFQMetaName = "global::System.Enum",
        KeyParamName = "key",
        NameFormatParamName = "nameFormat",
        SourceParamName = "source",
        ImplParamName = "impl",
        IfaceParamName = "iface",
        SingletonAttr = $"{GlobalBaseAttributeNS}.SingletonAttribute",
        ScopedAttr = $"{GlobalBaseAttributeNS}.ScopedAttribute",
        TransientAttr = $"{GlobalBaseAttributeNS}.TransientAttribute",
        DependencyAttr = $"{GlobalBaseAttributeNS}.DependencyAttribute",
        ServiceProviderAttr = $"global::{ServiceProviderFullTypeName}",
        DefaultEnvName = @"""DOTNET_ENVIRONMENT""";


    private const string serviceProviderFullTypeName = "SourceCrafter.DependencyInjection.Attributes.ServiceProviderAttribute";
    internal readonly static string generatedCodeAttribute = ParseToolAndVersion();
    internal readonly static Guid generatorGuid = new("31C54896-DE65-4FDC-8EBA-5A169A6E3CBB");

    // RS2008: el seguimiento de versiones de analizador (AnalyzerReleases.*.md) no se usa
    // en este proyecto; el resto de descriptores son locales y por eso no lo disparan.
#pragma warning disable RS2008
    private static readonly DiagnosticDescriptor FatalErrorRule = new(
        id: "SCDIE00",
        title: "Error at SourceCrafter.DependencyInjection generation time",
        messageFormat: "{0}",
        category: "SourceCrafter.DependencyInjection.FatalError",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
#pragma warning restore RS2008

    //static DependenciesServer? server;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
#if DEBUG_SG
        System.Diagnostics.Debugger.Launch();
#endif

        var msResolverCalls = context.SyntaxProvider
                .CreateSyntaxProvider(
                    ScanMsDIGerServiceCalls,
                    GetInvokeInfos)
                .Where(static info => info is not null)
                .Collect()
                .WithTrackingName("AssemblyExternalProvidersRegistrations");

        var servicesContainers = context.SyntaxProvider
                .ForAttributeWithMetadataName(ServiceProviderFullTypeName,
                    static (node, a) => true,
                    static (gasc, c) => TryParseContainer(gasc, c))
                .Where(e => e is not null)
                .WithComparer(EmitterEqualityComparer.Default)
                .WithTrackingName("EmitterCreation")
                .Collect()
                .Combine(msResolverCalls)
                .Select(static (result, c) =>
                {
                    var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
                    var files = ImmutableArray.CreateBuilder<(string fileName, string code)>();
                    try
                    {
                        var (emitters, msCalls) = result;

                        // Estado por pasada: no debe vivir en los objetos que Roslyn
                        // cachea entre compilaciones incrementales.
                        HashSet<InterceptableLocation> acknowledged = [];
                        List<(Emitter emitter, Dictionary<FirstLevelDependencyKey, Interceptor> interceptors)> pending = [];

                        // Solo compiten por una llamada los contenedores que realmente
                        // van a emitir interceptors. Se indexa por posicion porque dos
                        // emisores pueden ser estructuralmente iguales.
                        var claims = new List<InvokeInfo>?[emitters.Length];

                        foreach (var call in msCalls)
                        {
                            var owner = ResolveOwner(emitters, call, diagnostics);

                            if (owner < 0) continue;

                            (claims[owner] ??= []).Add(call);
                        }

                        for (var i = 0; i < emitters.Length; i++)
                        {
                            Dictionary<FirstLevelDependencyKey, Interceptor> emitterInterceptors =
                                new(EqualityComparer<FirstLevelDependencyKey>.Default);

                            if (claims[i] is { } owned)
                                InterceptorsAppender(emitters[i], owned, emitterInterceptors, acknowledged, c);

                            pending.Add((emitters[i], emitterInterceptors));
                        }

                        foreach (var call in msCalls)
                        {
                            if (call.InvalidAsyncTypeArg)
                                diagnostics.Add(
                                    ServiceContainerDiagnostics.InvalidAsyncTypeArgument(call.Location, call.AsyncKind, call.MethodName));

                            if (!acknowledged.Contains(call.Interceptor))
                                diagnostics.Add(
                                    ServiceContainerDiagnostics.UncoveredGenericResolver(call.Location, call.ReturnType, call.ContainerTypeFullName, call.IsScopedCall));
                        }

                        var requiresTaskExtensions = false;
                        string? ensureLockType = null;
                        var interceptorsCount = 0;
                        Dictionary<string, byte> countedNames = [];

                        foreach (var (emitter, emitterInterceptors) in pending)
                        {
                            // Un emisor sin servicios solo esta aqui para transportar sus
                            // diagnosticos: emitir su archivo produciria un contenedor vacio.
                            if (emitter.HasServices)
                            {
                                emitter.Emit(countedNames, emitterInterceptors, ref requiresTaskExtensions, ref ensureLockType, ref interceptorsCount, out var file, out var code);
                                files.Add((file + ".g", code));
                            }

                            foreach (var item in emitter.Diagnostics)
                            {
                                diagnostics.Add(item);
                            }
                        }

                        if (interceptorsCount > 0)
                            files.Add(("Utils.g", UtilsFileContent));

                        if (ensureLockType is { } lockType)
                            files.Add(("Locks.g", BuildLocksFileContent(lockType)));

                        if (requiresTaskExtensions)
                            files.Add(("TaskExtensions.g", TaskExtensionsFileContent));
                    }
                    catch (Exception e)
                    {
                        diagnostics.Add(Diagnostic.Create(FatalErrorRule, null, e.ToString()));
                    }
                    
                    return (diagnostics.ToImmutable(), files.ToImmutable());
                })
                .WithTrackingName("SourceCrafterEmitters");

        context.RegisterSourceOutput(
            servicesContainers,
            static (context, artifacts) =>
                {
                    var (diagnostics, files) = artifacts;

                    foreach (var diagnostic in diagnostics)
                    {
                        context.ReportDiagnostic(diagnostic);
                    }

                    foreach (var (fileName, code) in files)
                    {
                        context.AddSource(fileName, code);
                    }
                });
    }

    private const string UtilsFileContent = @"// <auto-generated/>
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class InterceptsLocationAttribute(int version, string data) : global::System.Attribute;
}";

    /// <summary>
    /// Ayudante compartido que crea un candado de instancia la primera vez que se necesita.
    ///
    /// <para>Se usa <c>Interlocked.CompareExchange</c> y no <c>??=</c>: este ultimo se expande a
    /// leer-comprobar-escribir, que no es atomico, y dos hilos pueden acabar con candados
    /// distintos y por tanto sin exclusion alguna.</para>
    ///
    /// <para>El camino rapido de cada resolver (<c>if (_x is not null) return _x;</c>) no llega
    /// aqui, asi que el coste en caliente es cero; a cambio, crear un ambito deja de pagar un
    /// candado por cada servicio scoped declarado.</para>
    ///
    /// <para>Vive en un unico archivo por compilacion y no dentro de cada contenedor: el cuerpo
    /// era identico en todos -el tipo del candado lo decide la compilacion, no el contenedor- y
    /// ademas encabezaba cada archivo generado, que es lo que de verdad estorbaba al leerlos.</para>
    ///
    /// <para>No se declara generico. Un generico solo produce una copia por tipo con tipos de
    /// valor; con tipos de referencia el runtime comparte un cuerpo canonico que, al no conocer
    /// el tipo concreto, traduce <c>new T()</c> a <c>Activator.CreateInstance&lt;T&gt;()</c> en vez
    /// de <c>newobj</c>. Medido sobre dos millones de creaciones: 18-20 ms frente a 13-14 ms. Y no
    /// comprarian nada, porque <c>object</c> y <c>Lock</c> nunca conviven en una misma
    /// compilacion.</para>
    /// </summary>
    private static string BuildLocksFileContent(string lockType) => @"// <auto-generated/>
#nullable enable

namespace SourceCrafter.DependencyInjection.Extensions;

internal static class Locks
{
	internal static " + lockType + " " + ResolverRenderer.EnsureLockMethodName + "(ref " + lockType + @"? location)
	{
		var current = global::System.Threading.Volatile.Read(ref location);

		if (current is not null) return current;

		var created = new " + lockType + @"();

		return global::System.Threading.Interlocked.CompareExchange(ref location, created, null) ?? created;
	}
}
";

	// Todo va cualificado con 'global::'. Este archivo se compila dentro del proyecto del
	// consumidor, que puede tener 'ImplicitUsings' desactivado (lo habitual en proyectos de
	// estilo antiguo y en netstandard2.0): sin el 'using System' implicito, 'IDisposable' e
	// 'IAsyncDisposable' no resolvian y el archivo no compilaba.
	private const string TaskExtensionsFileContent = @"// <auto-generated/>
#nullable enable
namespace SourceCrafter.DependencyInjection.Extensions;

internal static class Extensions
{
	internal static async global::System.Threading.Tasks.ValueTask TryDisposeAsync<T>(this global::System.Threading.Tasks.ValueTask<T>? vTask) where T : global::System.IAsyncDisposable
	{
		if (vTask is not { } task || !TryUnwrap(await Observe(task).ConfigureAwait(false), out var value)) return;

		if (value is global::System.IAsyncDisposable result) await result.DisposeAsync().ConfigureAwait(false);
	}
	internal static async global::System.Threading.Tasks.ValueTask TryDispose<T>(this global::System.Threading.Tasks.ValueTask<T>? vTask) where T : global::System.IDisposable
	{
		if (vTask is not { } task || !TryUnwrap(await Observe(task).ConfigureAwait(false), out var value)) return;

		if (value is global::System.IDisposable result) result.Dispose();
	}
	internal static async global::System.Threading.Tasks.ValueTask TryDisposeAsync<T>(this global::System.Threading.Tasks.Task<T>? task) where T : global::System.IAsyncDisposable
	{
		if (task is null || !TryUnwrap(await Observe(task).ConfigureAwait(false), out var value)) return;

		if (value is global::System.IAsyncDisposable result) await result.DisposeAsync().ConfigureAwait(false);
	}
	internal static async global::System.Threading.Tasks.ValueTask TryDispose<T>(this global::System.Threading.Tasks.Task<T>? task) where T : global::System.IDisposable
	{
		if (task is null || !TryUnwrap(await Observe(task).ConfigureAwait(false), out var value)) return;

		if (value is global::System.IDisposable result) result.Dispose();
	}

	// Espera a que la tarea termine sin relanzar. Liberar es limpieza: si la construccion
	// del servicio fallo no hay instancia que liberar, y propagar ese fallo desde
	// Dispose/DisposeAsync tapa la excepcion real del bloque 'using'. Devolver la tarea
	// terminada tambien *observa* su excepcion, asi que no queda sin observar.
	private static async global::System.Threading.Tasks.ValueTask<global::System.Threading.Tasks.Task<T>> Observe<T>(global::System.Threading.Tasks.Task<T> task)
	{
		if (!task.IsCompleted)
		{
			try { await task.ConfigureAwait(false); } catch { }
		}
		else if (!task.IsCompletedSuccessfully)
		{
			_ = task.Exception;
		}

		return task;
	}

	private static async global::System.Threading.Tasks.ValueTask<global::System.Threading.Tasks.Task<T>> Observe<T>(global::System.Threading.Tasks.ValueTask<T> vTask)
		=> await Observe(vTask.AsTask()).ConfigureAwait(false);

	private static bool TryUnwrap<T>(global::System.Threading.Tasks.Task<T> task, out T value)
	{
		if (task.IsCompletedSuccessfully)
		{
			value = task.Result;
			return true;
		}

		value = default!;
		return false;
	}
}";

	private const string ServiceScopeFullTypeName = "global::Microsoft.Extensions.DependencyInjection.IServiceScope";

	/// <summary>
	/// Decide que contenedor reclama una llamada interceptable. Primero se busca una
	/// coincidencia exacta por tipo; solo si no hay ninguna se recurre a las laxas
	/// (mismo nombre simple o inferencia por <c>IServiceScope</c>). Antes bastaba con
	/// que la llamada fuese scoped para que <b>cualquier</b> contenedor la reclamase, lo
	/// que con dos contenedores en la misma compilacion emitia dos
	/// <c>[InterceptsLocation]</c> para la misma ubicacion.
	/// </summary>
	private static int ResolveOwner(ImmutableArray<Emitter> emitters, InvokeInfo call, ImmutableArray<Diagnostic>.Builder diagnostics)
	{
		int exact = -1, loose = -1, exactCount = 0, looseCount = 0;

		for (var i = 0; i < emitters.Length; i++)
		{
			var emitter = emitters[i];

			if (!emitter.EmitsInterceptors) continue;

			if (call.ContainerTypeFullName == emitter.ContainerFullTypeName)
			{
				exact = i;
				exactCount++;
			}
			else if (call.ContainerTypeNameOnly == emitter.ClassName
				|| (call.IsScopedCall && call.ContainerTypeFullName is ServiceScopeFullTypeName))
			{
				loose = i;
				looseCount++;
			}
		}

		if (exactCount == 1) return exact;
		if (exactCount == 0 && looseCount == 1) return loose;
		if (exactCount == 0 && looseCount == 0) return -1;

		diagnostics.Add(
			ServiceContainerDiagnostics.AmbiguousContainerForCall(
				call.Location,
				call.MethodName,
				call.ContainerTypeFullName));

		return -1;
	}

	private static void InterceptorsAppender(
		Emitter emitter,
		List<InvokeInfo> msCalls,
		Dictionary<FirstLevelDependencyKey, Interceptor> interceptors,
		HashSet<InterceptableLocation> acknowledged,
		CancellationToken c)
	{
		foreach (var serviceCall in msCalls)
			if (!acknowledged.Contains(serviceCall.Interceptor))
			{
				if (emitter.DependencyValueBuilders.TryGetValue((serviceCall.ReturnType, serviceCall.Key), out var resolvers))
				{
					if (serviceCall.IsMultiple)
					{
						foreach (var resolver in resolvers.Values)
						{
							Emitter.AddOrUpdateIntercerceptor(
								interceptors,
								acknowledged,
								serviceCall,
								true,
								resolver);
						}
					}
					else if (resolvers.Values.LastOrDefault(e => e.Key.key == serviceCall.Key) is { } resolver2)
					{
						Emitter.AddOrUpdateIntercerceptor(
							interceptors,
							acknowledged,
							serviceCall,
							false,
							resolver2);
					}
				}
				else if (serviceCall.Key == "" && serviceCall.IsMultiple 
					&& emitter.DependencyValueBuilders.Where(dvb => dvb.Key.type == serviceCall.ReturnType).SelectMany(dvb => dvb.Value.Select(e => e.Value)).ToArray() is { Length: > 0 } items)
				{
					foreach (var resolver in items)
					{
						Emitter.AddOrUpdateIntercerceptor(
							interceptors,
							acknowledged,
							serviceCall,
							true,
							resolver);
					}
				}
			}
	}

    [GeneratedRegex("^GetRequired(?:Keyed)?(?:Value)?Services?(?:Async)?$")]
    internal static partial Regex MEDI_MethodDetector { get; }

    private static bool ScanMsDIGerServiceCalls(SyntaxNode syntax, CancellationToken _) =>
        syntax is MemberAccessExpressionSyntax
        {
            Parent: InvocationExpressionSyntax,
            Name.Identifier.ValueText: string name
        } && MEDI_MethodDetector.IsMatch(name);

    readonly static ImmutableArray<Lifetime> lifeTimes = [Lifetime.Singleton, Lifetime.Scoped, Lifetime.Transient];

    static bool TryGetLifetime(AttributeSyntax attrSyntax, ref INamedTypeSymbol attrClass, ref bool isExternal, out Lifetime lifetime)
    {
        if (GetLifetimeFromSyntax(attrSyntax, out lifetime)) return true;

        bool found;
        do
        {
            (isExternal, (found, lifetime)) = attrClass.FullGlobalQualifiedNonGenericName switch
            {
                SingletonAttr => (isExternal, (true, Lifetime.Singleton)),
                ScopedAttr => (isExternal, (true, Lifetime.Scoped)),
                TransientAttr => (isExternal, (true, Lifetime.Transient)),
                { } val => (val is not DependencyAttr, GetFromCtorSymbol(attrClass))
            };

            if (found) return true;

            isExternal = true;
        }
        while ((attrClass = attrClass?.BaseType!) is not null);

        return false;

        static (bool, Lifetime) GetFromCtorSymbol(INamedTypeSymbol attrClass)
        {
            foreach (var ctor in attrClass.Constructors)
                foreach (var param in ctor.Parameters)
                    if (param.Name.ToLower() is "lifetime" && param.HasExplicitDefaultValue)
                        return (true, (Lifetime)(byte)param.ExplicitDefaultValue!);

            return (false, default);
        }

        static bool GetLifetimeFromSyntax(AttributeSyntax attribute, out Lifetime lifetime)
        {
            foreach (var arg in attribute.ArgumentList?.Arguments ?? [])
            {
                if (arg is { NameColon.Name.Identifier.ValueText: "lifetime", Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: { } memberName } }
                    && Enum.TryParse(memberName, out lifetime))
                {
                    return true;
                }
            }

            lifetime = default;
            return false;
        }
    }

    private static InvokeInfo GetInvokeInfos(GeneratorSyntaxContext gsc, CancellationToken cancellationToken)
    {
        if (gsc.Node is not MemberAccessExpressionSyntax
            {
                Parent: InvocationExpressionSyntax invocation,
                Name: GenericNameSyntax { TypeArgumentList.Arguments: [{ } typeArgSyntax], Identifier.ValueText: { } methodName } method,
                Expression: { } receiver
            } memberAccess
        ) 
            
            return null!;

        var model = gsc.SemanticModel;

        if (model.GetInterceptableLocation(invocation, cancellationToken) is not { } interceptor
            || model.GetTypeInfo(typeArgSyntax, cancellationToken).Type is not ITypeSymbol { } depTypeResult)

            return null!;

        // El interceptor se emite como metodo de extension, asi que su 'this' admite cualquier
        // expresion. Lo que decide si el sitio es interceptable es el **tipo** del receptor, no
        // su forma sintactica: exigir un identificador simple dejaba fuera
        // 'new Container().GetX()', 'Factory().GetX()' o 'this.field.GetX()', que caian en el
        // stub que lanza en ejecucion.
        ITypeSymbol type;
        ISymbol? declaringRef = null;
        bool isCtor;

        if (receiver is IdentifierNameSyntax refVar
            && model.GetSymbolInfo(refVar, cancellationToken).Symbol switch
            {
                ILocalSymbol local => (local, local.Type),
                IParameterSymbol parameter => (parameter, parameter.Type),
                IFieldSymbol field => (field, field.Type),
                IPropertySymbol property => (property, property.Type),
                _ => (default(ISymbol)!, default(ITypeSymbol)!)
            } is ({ } _ref, { } declaredType))
        {
            // Un identificador declarado con 'var' sobre CreateScope(), o tipado como
            // IServiceScope, no revela por si solo de que contenedor viene: hay que mirar su
            // inicializador.
            (declaringRef, type) = (_ref, declaredType);
        }
        else if (ReceiverType(model, receiver, cancellationToken) is { } receiverType)
        {
            type = receiverType;
        }
        else return null!;

        bool scopedInference = type.AsNonNullable().ToDisplayString().Equals("Microsoft.Extensions.DependencyInjection.IServiceScope");

        if (declaringRef is not null)
        {
            (isCtor, type) = GetContainerSource(model, declaringRef, type, cancellationToken);
        }
        else
        {
            // Una expresion se explica sola: si es una construccion, el receptor es la raiz
            // recien creada y no puede venir de un ambito.
            var bare = receiver;

            while (bare is ParenthesizedExpressionSyntax { Expression: { } inner }) bare = inner;

            isCtor = bare is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax;
        }

        var callContainerType = (type.Name is "Scoped" && type.ContainingType is not null
            ? type.ContainingType
            : type).AsNonNullable();

        var keyHash = invocation.ArgumentList.Arguments switch
        {
            [{ Expression: LiteralExpressionSyntax { Token.RawKind: (int)SyntaxKind.StringLiteralToken, Token.ValueText: string text } }, ..]
                => text,

            [{ Expression: IdentifierNameSyntax id }, ..]
                when TryGetIdentifier(model, id, cancellationToken, out string text) => text,

            [{ Expression: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax id } }, ..]
                when TryGetIdentifier(model, id, cancellationToken, out string text) => text,

            _ => ""
        };


        var AsyncKind = depTypeResult.TryGetAsyncType(out depTypeResult);
        var methodWithTaskTypeArg = AsyncKind > 0;

        if (AsyncKind is 0 && methodName.EndsWith("Async")) AsyncKind = AsyncKind.Task;

        return new(
            callContainerType.AllInterfaces.Any(i => i.GetAttributes().Any(IsGeneratedServiceProvider)),
            callContainerType.NameOnly,
            callContainerType.FullGlobalQualifiedName,
            methodName,
            scopedInference || (callContainerType.Name is "Scoped" && callContainerType.ContainingType is not null),
            AsyncKind,
            depTypeResult.FullGlobalQualifiedName,
            interceptor,
            keyHash,
            isCtor,
            methodName.Contains("Keyed", StringComparison.Ordinal),
            methodName.Contains("Services", StringComparison.Ordinal))
        {
            InvalidAsyncTypeArg = methodWithTaskTypeArg,
            Location = memberAccess.GetLocation()
        };
    }

    /// <summary>
    /// Tipo del receptor de una llamada interceptable.
    ///
    /// <para>Durante el escaneo, los miembros que este mismo generador va a emitir todavia no
    /// existen: <c>new Container().CreateScope()</c> no tiene tipo resoluble. El contenedor de
    /// origen si lo tiene, asi que se retrocede al receptor de esa invocacion. Es la misma
    /// razon por la que <see cref="GetContainerSource"/> mira el inicializador de una variable
    /// en vez de su tipo declarado.</para>
    /// </summary>
    private static ITypeSymbol? ReceiverType(SemanticModel model, ExpressionSyntax expression, CancellationToken token)
    {
        while (expression is ParenthesizedExpressionSyntax { Expression: { } inner }) expression = inner;

        if (model.GetTypeInfo(expression, token).Type is { TypeKind: not TypeKind.Error } resolved) return resolved;

        return expression is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Expression: { } source } }
            ? ReceiverType(model, source, token)
            : null;
    }

    private static (bool isCtor, ITypeSymbol finalType) GetContainerSource(SemanticModel model, ISymbol _ref, ITypeSymbol type, CancellationToken token)
    {
        foreach (var declaration in _ref.DeclaringSyntaxReferences.Reverse())
        {
            switch ((declaration.GetSyntax(token) as VariableDeclaratorSyntax)?.Initializer?.Value)
            {
                case ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax:
                    return (true, type);

                case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Expression: { } source } }:
                    // El receptor de la invocacion que produjo la variable puede ser a su vez
                    // cualquier expresion ('new Container().CreateScope()'), no solo un
                    // identificador.
                    return (false, ReceiverType(model, source, token) ?? type);
            }
        }
        return (false, type);
    }

    private static bool TryGetIdentifier(SemanticModel model, IdentifierNameSyntax id, CancellationToken cancellationToken, out string text)
    {
        if (model.GetSymbolInfo(id, cancellationToken).Symbol is IFieldSymbol { IsConst: true, HasConstantValue: true, ConstantValue: string _text })
        {
            text = _text;
            return true;
        }
        text = null!;
        return false;
    }

    static bool IsGeneratedServiceProvider(AttributeData attrData) =>
        attrData.AttributeClass?.FullGlobalQualifiedName.EndsWith(ServiceProviderFullTypeName) ?? false;

    private static string ParseToolAndVersion()
    {
        string name = "SourceCrafter.DependencyInjection";

        int i = 0;

        if (Assembly.GetExecutingAssembly().FullName?.Trim() is not { Length: > 0 } fullName) return "";

        foreach (var item in fullName.Split(','))
        {
            switch (item.Split('='))
            {
                case [{ } _name] when i <= 1: name = _name; break;
                case [" Version", { } version]: return $@"[global::System.CodeDom.Compiler.GeneratedCode(""{name}"", ""{version}"")]";
            }
            i++;
        }

        return $@"[global::System.CodeDom.Compiler.GeneratedCode(""{name}"", ""1.0.0"")]";
    }
}
using System.Collections.Generic;
using System.Linq;
using System.Text;

#pragma warning disable CA1050 // Declarar tipos en espacios de nombres
internal partial class ServiceProviders
#pragma warning restore CA1050 // Declarar tipos en espacios de nombres
{
    /// <summary>
    /// Emite la API generica de compatibilidad como un despachador real.
    ///
    /// <para>Es el <b>fallback</b> de los interceptores. Un interceptor reemplaza la llamada
    /// en el sitio, asi que resuelve sin ninguna comparacion; pero solo puede hacerlo si el
    /// compilador logra enlazar el contenedor concreto en ese sitio. Cuando no lo logra
    /// (el contenedor llega por una variable de tipo interfaz, o la llamada esta en otro
    /// ensamblado) la llamada sobrevive y aterriza en estos miembros, que discriminan por
    /// tipo en ejecucion. Por eso conviven: el interceptor es mas granular, este es el
    /// que garantiza que la llamada siempre resuelve.</para>
    /// </summary>
    internal static class GenericApiEmitter
    {
        /// <summary>
        /// Un servicio tal y como lo ve el despachador, ya proyectado a cadenas.
        /// </summary>
        /// <param name="RuntimeTypeName">
        /// <c>typeof(T).FullName</c> del tipo expuesto, o <c>null</c> si es un generico
        /// construido (se discrimina por <c>typeof</c>, no por cadena).
        /// </param>
        internal readonly record struct Entry(
            string? RuntimeTypeName,
            string ExportTypeFullName,
            string Key,
            AsyncKind AsyncKind,
            string MemberName,
            bool IsMethodShaped);

        /// <summary>
        /// Proyecta los resolvedores registrados a las entradas que el despachador puede
        /// atender. Se descartan los que no exponen miembro: un transient inlineado sin
        /// <c>exportTransients</c> no tiene nombre al que reenviar.
        ///
        /// <para>Se conservan <b>todos</b> los registros, en orden: los miembros plurales
        /// devuelven cada uno de ellos. Son los singulares los que se quedan con uno solo,
        /// via <see cref="LastPerIdentity"/>.</para>
        /// </summary>
        internal static List<Entry> Collect(DependencyDictionary services)
        {
            List<Entry> entries = [];

            foreach (var group in services.Values)
            {
                foreach (var resolver in group.Values)
                {
                    if (resolver.MemberName is not { Length: > 0 } member) continue;

                    entries.Add(new(
                        resolver.RuntimeTypeName,
                        resolver.ExportTypeFullName,
                        resolver.Key.key,
                        resolver.AsyncKind,
                        member,
                        resolver.MemberIsMethodShaped));
                }
            }

            return entries;
        }

        /// <summary>
        /// Reduce las entradas a una por tipo expuesto y clave, quedandose con la
        /// <b>ultima</b> registrada.
        ///
        /// <para>El tipo expuesto es el unico criterio: si es una interfaz o una clase
        /// abstracta da igual, gana el ultimo registro de ese tipo. Ademas de ser la regla
        /// pedida, evita emitir dos <c>case</c> con la misma etiqueta, que no compilaria.</para>
        /// </summary>
        static List<Entry> LastPerIdentity(IEnumerable<Entry> entries)
        {
            Dictionary<(string, string), Entry> byIdentity = [];
            List<(string, string)> order = [];

            foreach (var entry in entries)
            {
                var identity = (entry.ExportTypeFullName, entry.Key);

                if (!byIdentity.ContainsKey(identity)) order.Add(identity);

                byIdentity[identity] = entry;
            }

            return [.. order.Select(id => byIdentity[id])];
        }

        /// <summary>
        /// Emite los ocho miembros de la API generica sobre <paramref name="entries"/>.
        /// </summary>
        internal static void Emit(StringBuilder code, List<Entry> entries)
        {
            if (entries.Count == 0) return;

            // --- Sincronos sin clave -------------------------------------------------
            var sync = LastPerIdentity(entries.Where(e => e.Key is "" && e.AsyncKind is AsyncKind.None));

            AppendSingle(code, "GetRequiredService", sync, keyed: false, asyncKind: AsyncKind.None);

            // GetService<T>() es exactamente GetRequiredService<T>() salvo en que no lanza:
            // devuelve null, que es la semantica que MS DI documenta. No resuelve keyed ni
            // async, asi que comparte la misma lista.
            AppendSingle(code, "GetService", sync, keyed: false, asyncKind: AsyncKind.None, nullWhenMissing: true);

            AppendMultiple(code, "GetRequiredServices", entries.Where(e => e.AsyncKind is AsyncKind.None).ToList(), keyed: false, asyncKind: AsyncKind.None);

            // --- Sincronos con clave -------------------------------------------------
            var keyedSync = entries.Where(e => e.Key is not "" && e.AsyncKind is AsyncKind.None).ToList();

            AppendSingle(code, "GetRequiredKeyedService", LastPerIdentity(keyedSync), keyed: true, asyncKind: AsyncKind.None);
            AppendMultiple(code, "GetRequiredKeyedServices", keyedSync, keyed: true, asyncKind: AsyncKind.None);

            // --- Asincronos ----------------------------------------------------------
            // Un servicio sincrono tambien se puede pedir de forma asincrona, asi que las
            // listas async incluyen a todos: el miembro es 'async' y devuelve el valor ya
            // materializado cuando no hay nada que esperar.
            var unkeyed = LastPerIdentity(entries.Where(e => e.Key is ""));
            var keyedAll = entries.Where(e => e.Key is not "").ToList();
            var keyedLast = LastPerIdentity(keyedAll);

            AppendSingle(code, "GetRequiredServiceAsync", unkeyed, keyed: false, asyncKind: AsyncKind.Task);
            AppendMultiple(code, "GetRequiredServicesAsync", entries, keyed: false, asyncKind: AsyncKind.Task);

            AppendSingle(code, "GetRequiredKeyedServiceAsync", keyedLast, keyed: true, asyncKind: AsyncKind.Task);
            AppendMultiple(code, "GetRequiredKeyedServicesAsync", keyedAll, keyed: true, asyncKind: AsyncKind.Task);

            // Las variantes 'Value' devuelven ValueTask<T>. Se emiten siempre junto a las de
            // Task: un sitio de llamada elige una u otra por el tipo que espera, y omitirlas
            // dejaba sin compilar a quien resolvia una factory ValueTask.
            AppendSingle(code, "GetRequiredValueServiceAsync", unkeyed, keyed: false, asyncKind: AsyncKind.ValueTask);
            AppendMultiple(code, "GetRequiredValueServicesAsync", entries, keyed: false, asyncKind: AsyncKind.ValueTask);

            AppendSingle(code, "GetRequiredKeyedValueServiceAsync", keyedLast, keyed: true, asyncKind: AsyncKind.ValueTask);
            AppendMultiple(code, "GetRequiredKeyedValueServicesAsync", keyedAll, keyed: true, asyncKind: AsyncKind.ValueTask);
        }

        /// <summary>
        /// Emite un miembro que devuelve <b>un</b> servicio.
        /// </summary>
        static void AppendSingle(
            StringBuilder code,
            string methodName,
            List<Entry> entries,
            bool keyed,
            AsyncKind asyncKind,
            bool nullWhenMissing = false)
        {
            var isAsync = asyncKind is not AsyncKind.None;

            code.Append(@"

    public ");

            if (isAsync) code.Append("async ");

            code.Append(asyncKind switch
            {
                AsyncKind.ValueTask => "global::System.Threading.Tasks.ValueTask<TOut>",
                AsyncKind.Task => "global::System.Threading.Tasks.Task<TOut>",
                _ => "TOut"
            })
                .Append(' ')
                .Append(methodName)
                .Append("<TOut>(")
                .Append(keyed ? "string key" : null)
                .Append(@") where TOut : notnull
    {");

            // Los genericos construidos no tienen FullName constante: se comparan por
            // typeof, que el JIT pliega. El resto entra al switch sobre cadena.
            var byType = entries.Where(e => e.RuntimeTypeName is null).ToList();
            var byName = entries.Where(e => e.RuntimeTypeName is not null).ToList();

            foreach (var entry in byType)
            {
                code.Append(@"
        if (typeof(TOut) == typeof(").Append(entry.ExportTypeFullName).Append(')');

                if (keyed) code.Append(" && key is ").Append('"').Append(entry.Key).Append('"');

                code.Append(@") return ");

                AppendReturnValue(code, entry, isAsync);

                code.Append(';');
            }

            if (byName.Count > 0)
            {
                AppendSwitchHead(code, keyed);

                foreach (var entry in byName)
                {
                    AppendCaseLabel(code, entry, keyed);

                    code.Append(@"
                return ");

                    AppendReturnValue(code, entry, isAsync);

                    code.Append(';');
                }

                code.Append(@"
        }");
            }

            code.Append(@"

        ");

            if (nullWhenMissing)
            {
                // 'default!' y no 'null': TOut es 'notnull' pero puede ser un struct, para el
                // que null no es un valor representable.
                code.Append("return default!;");
            }
            else
            {
                code.Append("throw new global::System.InvalidOperationException($\"No service of type '{typeof(TOut)}'")
                    .Append(keyed ? " with key '{key}'" : null)
                    .Append(" is registered.\");");
            }

            code.Append(@"
    }
");
        }

        /// <summary>
        /// Emite un miembro que devuelve <b>todos</b> los servicios de un tipo.
        ///
        /// <para>Un sitio sin clave recoge tambien los registros con clave: es lo que hace el
        /// fallback de los interceptores y lo que MS DI documenta para las variantes
        /// plurales sin clave.</para>
        /// </summary>
        static void AppendMultiple(
            StringBuilder code,
            string methodName,
            List<Entry> entries,
            bool keyed,
            AsyncKind asyncKind)
        {
            var isAsync = asyncKind is not AsyncKind.None;

            code.Append(@"

    public ");

            if (isAsync) code.Append("async ");

            code.Append(asyncKind switch
            {
                AsyncKind.ValueTask => "global::System.Threading.Tasks.ValueTask<TOut[]>",
                AsyncKind.Task => "global::System.Threading.Tasks.Task<TOut[]>",
                _ => "TOut[]"
            })
                .Append(' ')
                .Append(methodName)
                .Append("<TOut>(")
                .Append(keyed ? "string key" : null)
                .Append(@") where TOut : notnull
    {");

            // Se agrupa por tipo expuesto: todos los registros de un mismo tipo forman un
            // unico array, sin importar su lifetime. Con clave el grupo incluye tambien la
            // clave, para no mezclar registros de claves distintas en la misma respuesta.
            // Los genericos construidos no tienen FullName constante y salen como 'if'; el
            // resto entra al switch, igual que en los miembros singulares.
            var groups = entries
                .GroupBy(e => (e.RuntimeTypeName, e.ExportTypeFullName, Key: keyed ? e.Key : ""))
                .ToList();

            foreach (var group in groups.Where(g => g.Key.RuntimeTypeName is null))
            {
                code.Append(@"
        if (typeof(TOut) == typeof(").Append(group.Key.ExportTypeFullName).Append(')');

                if (keyed) code.Append(" && key is ").Append('"').Append(group.Key.Key).Append('"');

                code.Append(@") return ");

                AppendArray(code, group, isAsync);

                code.Append(';');
            }

            var byName = groups.Where(g => g.Key.RuntimeTypeName is not null).ToList();

            if (byName.Count > 0)
            {
                AppendSwitchHead(code, keyed);

                foreach (var group in byName)
                {
                    code.Append(@"
            case """).Append(group.Key.RuntimeTypeName);

                    if (keyed) code.Append('|').Append(group.Key.Key);

                    code.Append(@""":
                return ");

                    AppendArray(code, group, isAsync);

                    code.Append(';');
                }

                code.Append(@"
        }");
            }

            code.Append(@"

        return [];
    }
");
        }

        /// <summary>
        /// Emite el array con todos los registros de un grupo, ya convertido a <c>TOut[]</c>.
        /// </summary>
        static void AppendArray(StringBuilder code, IEnumerable<Entry> group, bool isAsync)
        {
            var exportType = group.First().ExportTypeFullName;

            code.Append("(TOut[])(object)new ").Append(exportType).Append("[] { ");

            var isFirst = true;

            foreach (var entry in group)
            {
                if (!isFirst) code.Append(", ");

                AppendMemberAccess(code, entry, isAsync, cast: false);

                isFirst = false;
            }

            code.Append(" }");
        }

        /// <summary>
        /// Abre el <c>switch</c> de discriminacion. Con clave se discrimina sobre
        /// <c>$"{typeof(T).FullName}|{key}"</c>, que resuelve tipo y clave en una sola
        /// comparacion en vez de anidar dos switches.
        /// </summary>
        static void AppendSwitchHead(StringBuilder code, bool keyed)
        {
            code.Append(keyed
                ? @"

        switch ($""{typeof(TOut).FullName}|{key}"")
        {"
                : @"

        switch (typeof(TOut).FullName)
        {");
        }

        static void AppendCaseLabel(StringBuilder code, Entry entry, bool keyed)
        {
            code.Append(@"
            case """).Append(entry.RuntimeTypeName);

            if (keyed) code.Append('|').Append(entry.Key);

            code.Append(@""":");
        }

        /// <summary>
        /// Emite el valor de retorno de un miembro singular, ya convertido a <c>TOut</c>.
        /// </summary>
        static void AppendReturnValue(StringBuilder code, Entry entry, bool isAsync)
        {
            // La conversion pasa por 'object' porque el compilador no puede saber que TOut
            // es el tipo del resolver. Es el precio de la compatibilidad con IServiceProvider
            // y no cuesta nada en ejecucion: sobre una referencia es una conversion de
            // identidad, y sobre un struct el JIT especializa el metodo por tipo y colapsa
            // el par box/unbox al mismo tipo.
            code.Append("(TOut)(object)");

            AppendMemberAccess(code, entry, isAsync, cast: true);
        }

        /// <summary>
        /// Emite el acceso al miembro que resuelve el servicio, esperandolo si hace falta.
        /// </summary>
        static void AppendMemberAccess(StringBuilder code, Entry entry, bool isAsync, bool cast)
        {
            var mustAwait = entry.AsyncKind is not AsyncKind.None;

            if (mustAwait) code.Append("await ");

            code.Append(entry.MemberName);

            if (entry.IsMethodShaped) code.Append("()");
        }
    }
}

using System;
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
    /// ensamblado) la llamada sobrevive y aterriza en estos miembros.</para>
    ///
    /// <para>La discriminacion <b>no</b> se hace sobre cadenas. Cada contenedor implementa,
    /// de forma explicita, una interfaz generica por servicio expuesto
    /// (<c>IProvider&lt;T&gt;</c> y companeras) y el despachador se limita a una prueba de
    /// tipo <c>this is IProvider&lt;TOut&gt;</c>: el runtime resuelve por tabla de
    /// interfaces en vez de comparar <c>typeof(TOut).FullName</c> contra una lista de
    /// literales. Solo sobrevive el <c>switch</c> sobre la clave, porque la clave si es un
    /// dato de ejecucion.</para>
    /// </summary>
    internal static class GenericApiEmitter
    {
        /// <summary>
        /// Espacio de nombres de las interfaces compartidas, emitidas una sola vez por
        /// compilacion en <c>Utils.g.cs</c>.
        /// </summary>
        internal const string SharedNamespace = "global::SourceCrafter.DependencyInjection.";

        /// <summary>
        /// Las interfaces de resolucion hablan siempre en <c>Task&lt;T&gt;</c>. Un
        /// <c>ValueTask&lt;T&gt;</c> intermedio no ahorra nada aqui -el valor se consume una
        /// sola vez y detras hay una tarea de todos modos- y obligaba a adaptar en cada
        /// consumidor.
        /// </summary>
        const string TaskOf = "global::System.Threading.Tasks.Task<";

        /// <summary>
        /// Un servicio tal y como lo ve el despachador, ya proyectado a cadenas.
        /// </summary>
        /// <param name="MemberName">
        /// Miembro del contenedor al que reenviar, o <c>null</c> si el servicio es un
        /// transient inlineado: entonces el valor se reconstruye con <paramref name="Append"/>.
        /// </param>
        internal readonly record struct Entry(
            string ExportTypeFullName,
            string Key,
            AsyncKind AsyncKind,
            string? MemberName,
            bool IsMethodShaped,
            AppendValue Append);

        internal enum ImplKind { Single, SingleAsync, Multiple, MultipleAsync }

        /// <summary>
        /// Una implementacion explicita de interfaz a emitir dentro del contenedor.
        /// </summary>
        internal sealed class Implementation(string interfaceName, string exportTypeFullName, ImplKind kind, List<Entry> entries)
        {
            internal readonly string InterfaceName = interfaceName;
            internal readonly string ExportTypeFullName = exportTypeFullName;
            internal readonly ImplKind Kind = kind;
            internal readonly List<Entry> Entries = entries;
        }

        /// <summary>
        /// Las interfaces declaradas para una clave concreta. Solo se declara la que
        /// realmente tiene implementacion.
        /// </summary>
        internal sealed class KeyDispatch(string key)
        {
            internal readonly string Key = key;
            internal string? SyncInterface, AsyncInterface, MultipleInterface, MultipleAsyncInterface;
        }

        /// <summary>
        /// Todo lo que un contenedor necesita para atender la API generica: su lista de
        /// bases, las interfaces con clave que le son propias, las implementaciones
        /// explicitas y el mapa de claves del despachador.
        /// </summary>
        internal sealed class ProviderPlan
        {
            internal readonly List<string> BaseInterfaces = [];
            internal readonly List<Implementation> Implementations = [];
            internal readonly List<KeyDispatch> Keys = [];
            internal readonly List<(string Name, ImplKind Kind)> KeyedInterfaces = [];

            internal bool HasSync, HasAsync, HasMultiple, HasMultipleAsync;
        }

        /// <summary>
        /// Proyecta los resolvedores registrados a las entradas que el despachador puede
        /// atender.
        ///
        /// <para>Un transient inlineado no tiene miembro al que reenviar, pero si un valor
        /// que reconstruir en el sitio, asi que <b>tambien</b> entra: descartarlo hacia
        /// desaparecer una registracion entera de los miembros plurales.</para>
        ///
        /// <para>Se conservan <b>todos</b> los registros, en orden: los miembros plurales
        /// devuelven cada uno de ellos. Son los singulares los que se quedan con uno solo,
        /// el ultimo registrado.</para>
        /// </summary>
        internal static List<Entry> Collect(DependencyDictionary services)
        {
            List<Entry> entries = [];

            foreach (var group in services.Values)
            {
                foreach (var resolver in group.Values)
                {
                    var member = resolver.MemberName is { Length: > 0 } name ? name : null;

                    // Sin miembro y sin valor inlineable (un servicio externo) no hay nada
                    // que emitir.
                    if (member is null && !resolver.IsInlineable) continue;

                    entries.Add(new(
                        resolver.ExportTypeFullName,
                        resolver.Key.key,
                        resolver.AsyncKind,
                        member,
                        resolver.MemberIsMethodShaped,
                        resolver.AppendValue));
                }
            }

            return entries;
        }

        /// <summary>
        /// Construye el plan de interfaces de un contenedor. Devuelve <c>null</c> cuando no
        /// hay nada que despachar.
        /// </summary>
        /// <param name="className">
        /// Discrimina las interfaces con clave entre contenedores: se declaran a nivel de
        /// espacio de nombres -una clase no puede derivar de un tipo <c>file</c>-local
        /// (CS9053)- y por tanto sus nombres tienen que ser unicos en el ensamblado.
        /// </param>
        internal static ProviderPlan? Build(DependencyDictionary services, string className)
        {
            var entries = Collect(services);

            if (entries.Count == 0) return null;

            ProviderPlan plan = new();

            // --- Sin clave -----------------------------------------------------------
            // El singular se queda con el ultimo registro. Se mantienen separados el ultimo
            // sincrono y el ultimo de todos para que una peticion sincrona siga alcanzando
            // un registro sincrono anterior a uno asincrono.
            foreach (var group in GroupBy(entries.Where(static e => e.Key is ""), static e => e.ExportTypeFullName))
            {
                if (Last(group.Where(static e => e.AsyncKind is AsyncKind.None)) is { } lastSync)
                {
                    Add(plan, SharedNamespace + "IProvider", group.Key, ImplKind.Single, [lastSync]);
                    plan.HasSync = true;
                }

                if (group[group.Count - 1] is { AsyncKind: not AsyncKind.None } lastAsync)
                {
                    Add(plan, SharedNamespace + "IAsyncProvider", group.Key, ImplKind.SingleAsync, [lastAsync]);
                    plan.HasAsync = true;
                }
            }

            // Un sitio sin clave recoge tambien los registros con clave: es lo que hace el
            // fallback de los interceptores y lo que MS DI documenta para las variantes
            // plurales sin clave.
            foreach (var group in GroupBy(entries.Where(static e => e.AsyncKind is AsyncKind.None), static e => e.ExportTypeFullName))
            {
                Add(plan, SharedNamespace + "IMultipleProvider", group.Key, ImplKind.Multiple, group);
                plan.HasMultiple = true;
            }

            // La variante plural asincrona agrupa unicamente los registros asincronos, igual
            // que la singular se queda con el ultimo asincrono. Un registro sincrono ya lo
            // devuelve la interfaz plural sincrona, y colarlo aqui obligaba a envolver en una
            // tarea valores que el llamante podia obtener sin esperar nada. Si el grupo no
            // tiene ninguno asincrono, no hay interfaz plural asincrona que declarar.
            foreach (var group in GroupBy(entries.Where(static e => e.AsyncKind is not AsyncKind.None), static e => e.ExportTypeFullName))
            {
                Add(plan, SharedNamespace + "IMultipleAsyncProvider", group.Key, ImplKind.MultipleAsync, group);
                plan.HasMultipleAsync = true;
            }

            // --- Con clave -----------------------------------------------------------
            Dictionary<string, byte> usedNames = [];

            foreach (var keyed in GroupBy(entries.Where(static e => e.Key is not ""), static e => e.Key))
            {
                KeyDispatch dispatch = new(keyed.Key);

                var prefix = "I" + className + PascalCase(keyed.Key, usedNames);

                foreach (var group in GroupBy(keyed, static e => e.ExportTypeFullName))
                {
                    if (Last(group.Where(static e => e.AsyncKind is AsyncKind.None)) is { } lastSync)
                    {
                        dispatch.SyncInterface = Declare(plan, dispatch.SyncInterface, prefix + "Provider", ImplKind.Single);
                        Add(plan, dispatch.SyncInterface, group.Key, ImplKind.Single, [lastSync]);
                    }

                    if (group[group.Count - 1] is { AsyncKind: not AsyncKind.None } lastAsync)
                    {
                        dispatch.AsyncInterface = Declare(plan, dispatch.AsyncInterface, prefix + "AsyncProvider", ImplKind.SingleAsync);
                        Add(plan, dispatch.AsyncInterface, group.Key, ImplKind.SingleAsync, [lastAsync]);
                    }

                    if (group.Where(static e => e.AsyncKind is AsyncKind.None).ToList() is { Count: > 0 } allSync)
                    {
                        dispatch.MultipleInterface = Declare(plan, dispatch.MultipleInterface, prefix + "MultipleProvider", ImplKind.Multiple);
                        Add(plan, dispatch.MultipleInterface, group.Key, ImplKind.Multiple, allSync);
                    }

                    if (group.Where(static e => e.AsyncKind is not AsyncKind.None).ToList() is { Count: > 0 } allAsync)
                    {
                        dispatch.MultipleAsyncInterface = Declare(plan, dispatch.MultipleAsyncInterface, prefix + "MultipleAsyncProvider", ImplKind.MultipleAsync);
                        Add(plan, dispatch.MultipleAsyncInterface, group.Key, ImplKind.MultipleAsync, allAsync);
                    }
                }

                plan.Keys.Add(dispatch);
            }

            return plan;

            static string Declare(ProviderPlan plan, string? existing, string name, ImplKind kind)
            {
                if (existing is null) plan.KeyedInterfaces.Add((name, kind));

                return name;
            }

            static void Add(ProviderPlan plan, string interfaceName, string exportType, ImplKind kind, List<Entry> entries)
            {
                plan.Implementations.Add(new(interfaceName, exportType, kind, entries));
                plan.BaseInterfaces.Add(interfaceName + "<" + exportType + ">");
            }
        }

        static Entry? Last(IEnumerable<Entry> entries)
        {
            Entry? last = null;

            foreach (var entry in entries) last = entry;

            return last;
        }

        /// <summary>
        /// Agrupa conservando el orden de aparicion: el orden de los registros es
        /// observable en los miembros plurales.
        /// </summary>
        static List<Group> GroupBy(IEnumerable<Entry> entries, Func<Entry, string> keySelector)
        {
            Dictionary<string, Group> groups = [];
            List<Group> order = [];

            foreach (var entry in entries)
            {
                var key = keySelector(entry);

                if (!groups.TryGetValue(key, out var group))
                {
                    groups[key] = group = new(key);
                    order.Add(group);
                }

                group.Add(entry);
            }

            return order;
        }

        internal sealed class Group(string key) : List<Entry>
        {
            internal readonly string Key = key;
        }

        /// <summary>
        /// Convierte una clave arbitraria en un identificador Pascal. Dos claves distintas
        /// pueden normalizar al mismo nombre (p. ej. <c>"a-b"</c> y <c>"a_b"</c>), asi que
        /// se desambigua con un sufijo.
        /// </summary>
        static string PascalCase(string key, Dictionary<string, byte> used)
        {
            StringBuilder name = new();
            var upper = true;

            foreach (var ch in key)
            {
                if (char.IsLetterOrDigit(ch) || ch is '_')
                {
                    name.Append(upper ? char.ToUpperInvariant(ch) : ch);
                    upper = false;
                }
                else
                {
                    upper = true;
                }
            }

            if (name.Length == 0 || char.IsDigit(name[0])) name.Insert(0, '_');

            var result = name.ToString();

            if (used.TryGetValue(result, out var count))
            {
                used[result] = ++count;
                return result + count;
            }

            used[result] = 0;

            return result;
        }

        /// <summary>
        /// Declara, antes del contenedor, las interfaces con clave que le pertenecen.
        /// </summary>
        internal static void AppendKeyedInterfaces(StringBuilder code, ProviderPlan plan)
        {
            foreach (var (name, kind) in plan.KeyedInterfaces)
            {
                code.Append("internal interface ").Append(name).Append(@"<T> where T : notnull
{
    ").Append(kind switch
                {
                    ImplKind.Single => "T GetService();",
                    ImplKind.SingleAsync => TaskOf + "T> GetServiceAsync();",
                    ImplKind.Multiple => "T[] GetServices();",
                    _ => TaskOf + "T[]> GetServicesAsync();"
                }).Append(@"
}

");
            }
        }

        /// <summary>
        /// Emite las implementaciones explicitas. Son explicitas para no ensuciar la
        /// superficie publica del contenedor con un miembro por servicio expuesto.
        /// </summary>
        internal static void AppendImplementations(StringBuilder code, ProviderPlan plan)
        {
            foreach (var impl in plan.Implementations)
            {
                var export = impl.ExportTypeFullName;

                code.Append(@"
    ");

                switch (impl.Kind)
                {
                    case ImplKind.Single:

                        code.Append(export).Append(' ').Append(impl.InterfaceName)
                            .Append('<').Append(export).Append(">.GetService() => ");

                        AppendMember(code, impl.Entries[0], awaited: false);

                        break;

                    case ImplKind.SingleAsync:

                        var entry = impl.Entries[0];

                        code.Append(TaskOf).Append(export).Append("> ").Append(impl.InterfaceName)
                            .Append('<').Append(export).Append(">.GetServiceAsync() => ");

                        AppendMember(code, entry, awaited: false);

                        // Una factoria ValueTask<T> se adapta una sola vez, aqui.
                        if (entry.AsyncKind is AsyncKind.ValueTask) code.Append(".AsTask()");

                        break;

                    case ImplKind.Multiple:

                        code.Append(export).Append("[] ").Append(impl.InterfaceName)
                            .Append('<').Append(export).Append(">.GetServices() =>");

                        AppendArray(code, impl, awaited: false);

                        break;

                    default:

                        // Todos los elementos son asincronos -el plan ya excluye los
                        // sincronos-, asi que el miembro siempre espera algo.
                        code.Append("async ").Append(TaskOf).Append(export).Append("[]> ").Append(impl.InterfaceName)
                            .Append('<').Append(export).Append(">.GetServicesAsync() =>");

                        AppendArray(code, impl, awaited: true);

                        break;
                }

                code.Append(';');
            }

            code.Append('\n');
        }

        /// <summary>
        /// Los elementos van uno por linea: un contenedor real acumula decenas de registros
        /// del mismo tipo y en una sola linea el miembro era ilegible.
        /// </summary>
        static void AppendArray(StringBuilder code, Implementation impl, bool awaited)
        {
            code.Append(@"
        [");

            var isFirst = true;

            foreach (var entry in impl.Entries)
            {
                if (!isFirst) code.Append(',');

                code.Append(@"
            ");

                AppendMember(code, entry, awaited);

                isFirst = false;
            }

            code.Append(@"
        ]");
        }

        static void AppendMember(StringBuilder code, Entry entry, bool awaited)
        {
            if (awaited && entry.AsyncKind is not AsyncKind.None) code.Append("await ");

            // El transient inlineado reconstruye su valor aqui mismo; el resto reenvia a su
            // miembro. Se emite fuera de contexto de interceptor porque el codigo vive
            // dentro del propio contenedor y no detras de un 'provider.'.
            if (entry.MemberName is null)
            {
                entry.Append(code, false, false, null);

                return;
            }

            code.Append(entry.MemberName);

            if (entry.IsMethodShaped) code.Append("()");
        }

        /// <summary>
        /// Emite los miembros de la API generica. Ninguno compara cadenas de tipo: todos
        /// preguntan por la interfaz que corresponde.
        /// </summary>
        internal static void Emit(StringBuilder code, ProviderPlan plan)
        {
            AppendSingle(code, plan, "GetRequiredService", AsyncKind.None, keyed: false);

            // GetService<T>() es exactamente GetRequiredService<T>() salvo en que no lanza:
            // devuelve null, que es la semantica que MS DI documenta.
            AppendSingle(code, plan, "GetService", AsyncKind.None, keyed: false, nullWhenMissing: true);

            AppendMultiple(code, plan, "GetRequiredServices", AsyncKind.None, keyed: false);

            AppendSingle(code, plan, "GetRequiredKeyedService", AsyncKind.None, keyed: true);
            AppendMultiple(code, plan, "GetRequiredKeyedServices", AsyncKind.None, keyed: true);

            AppendSingle(code, plan, "GetRequiredServiceAsync", AsyncKind.Task, keyed: false);
            AppendMultiple(code, plan, "GetRequiredServicesAsync", AsyncKind.Task, keyed: false);

            AppendSingle(code, plan, "GetRequiredKeyedServiceAsync", AsyncKind.Task, keyed: true);
            AppendMultiple(code, plan, "GetRequiredKeyedServicesAsync", AsyncKind.Task, keyed: true);
        }

        /// <summary>
        /// Emite un miembro que devuelve <b>un</b> servicio.
        /// </summary>
        static void AppendSingle(
            StringBuilder code,
            ProviderPlan plan,
            string methodName,
            AsyncKind asyncKind,
            bool keyed,
            bool nullWhenMissing = false)
        {
            var isAsync = asyncKind is not AsyncKind.None;

            AppendSignature(code, methodName, asyncKind, keyed, plural: false);

            if (keyed)
            {
                AppendKeyedSwitch(
                    code,
                    plan,
                    isAsync
                        ? static d => d.AsyncInterface is not null
                        : static d => d.SyncInterface is not null,
                    (body, dispatch, slot) =>
                    {
                        // Cada superficie resuelve unicamente sus propios registros: un
                        // proveedor sincrono no participa de la API asincrona.
                        if (isAsync)
                        {
                            if (dispatch.AsyncInterface is { } async)
                                AppendTest(body, async, "GetServiceAsync", slot, awaited: true);
                        }
                        else if (dispatch.SyncInterface is { } sync)
                        {
                            AppendTest(body, sync, "GetService", slot, awaited: false);
                        }
                    });
            }
            else
            {
                if (isAsync)
                {
                    if (plan.HasAsync)
                        AppendTest(code, SharedNamespace + "IAsyncProvider", "GetServiceAsync", 0, awaited: true);
                }
                else if (plan.HasSync)
                {
                    AppendTest(code, SharedNamespace + "IProvider", "GetService", 0, awaited: false);
                }
            }

            AppendTail(code, keyed, nullWhenMissing ? "return default!;" : null);
        }

        /// <summary>
        /// Emite un miembro que devuelve <b>todos</b> los servicios de un tipo.
        /// </summary>
        static void AppendMultiple(
            StringBuilder code,
            ProviderPlan plan,
            string methodName,
            AsyncKind asyncKind,
            bool keyed)
        {
            var isAsync = asyncKind is not AsyncKind.None;

            AppendSignature(code, methodName, asyncKind, keyed, plural: true);

            if (keyed)
            {
                AppendKeyedSwitch(
                    code,
                    plan,
                    isAsync
                        ? static d => d.MultipleAsyncInterface is not null
                        : static d => d.MultipleInterface is not null,
                    (body, dispatch, slot) =>
                    {
                        if (isAsync)
                        {
                            if (dispatch.MultipleAsyncInterface is { } async)
                                AppendTest(body, async, "GetServicesAsync", slot, awaited: true);
                        }
                        else if (dispatch.MultipleInterface is { } sync)
                        {
                            AppendTest(body, sync, "GetServices", slot, awaited: false);
                        }
                    });
            }
            else if (isAsync)
            {
                // Un grupo integramente sincrono no declara interfaz plural asincrona y
                // tampoco se alcanza desde aqui: la API asincrona no reparte proveedores
                // sincronos, que ya tienen su propia superficie.
                if (plan.HasMultipleAsync)
                    AppendTest(code, SharedNamespace + "IMultipleAsyncProvider", "GetServicesAsync", 0, awaited: true);
            }
            else if (plan.HasMultiple)
            {
                AppendTest(code, SharedNamespace + "IMultipleProvider", "GetServices", 0, awaited: false);
            }

            AppendTail(code, keyed, "return [];");
        }

        static void AppendSignature(StringBuilder code, string methodName, AsyncKind asyncKind, bool keyed, bool plural)
        {
            var result = plural ? "TOut[]" : "TOut";

            code.Append(@"

    public ");

            if (asyncKind is not AsyncKind.None) code.Append("async ");

            code.Append(asyncKind is AsyncKind.None ? result : TaskOf + result + ">")
                .Append(' ')
                .Append(methodName)
                .Append("<TOut>(")
                .Append(keyed ? "string key" : null)
                .Append(@") where TOut : notnull
    {");
        }

        /// <summary>
        /// La clave si es un dato de ejecucion, asi que conserva su <c>switch</c>. Dentro de
        /// cada rama la resolucion vuelve a ser una prueba de tipo.
        /// </summary>
        static void AppendKeyedSwitch(
            StringBuilder code,
            ProviderPlan plan,
            Func<KeyDispatch, bool> emits,
            Action<StringBuilder, KeyDispatch, int> appendBody)
        {
            var dispatches = plan.Keys.Where(emits).ToList();

            if (dispatches.Count == 0) return;

            code.Append(@"

        switch (key)
        {");

            var slot = 0;

            foreach (var dispatch in dispatches)
            {
                code.Append(@"
            case """).Append(dispatch.Key).Append(@""":");

                appendBody(code, dispatch, slot);

                // Dos ranuras por rama: una prueba asincrona y su respaldo sincrono.
                slot += 2;

                code.Append(@"
                break;");
            }

            code.Append(@"
        }");
        }

        /// <summary>
        /// Los <c>case</c> de un <c>switch</c> comparten ambito, de ahi el sufijo por rama.
        /// </summary>
        static void AppendTest(StringBuilder code, string interfaceName, string member, int slot, bool awaited)
        {
            code.Append(@"
        if (this is ").Append(interfaceName).Append("<TOut> __p").Append(slot).Append(") return ");

            if (awaited) code.Append("await ");

            code.Append("__p").Append(slot).Append('.').Append(member).Append("();");
        }

        static void AppendTail(StringBuilder code, bool keyed, string? fallback)
        {
            code.Append(@"

        ");

            if (fallback is not null)
            {
                // 'default!' y no 'null': TOut es 'notnull' pero puede ser un struct, para el
                // que null no es un valor representable.
                code.Append(fallback);
            }
            else
            {
                code.Append("throw new global::System.InvalidOperationException($\"No service of type '{typeof(TOut).FullName}'")
                    .Append(keyed ? " with key '{key}'" : null)
                    .Append(" is registered.\");");
            }

            code.Append(@"
    }
");
        }
    }
}

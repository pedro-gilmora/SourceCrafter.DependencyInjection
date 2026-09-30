using Microsoft.CodeAnalysis;
using SourceCrafter.DependencyInjection.Generation;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading;

/// <summary>
/// Registro de los generadores parciales descubiertos en los ensamblados cuyo nombre empieza por
/// <c>SourceCrafter.DependencyInjection.Partial</c>.
///
/// <para>
/// No se sondea el disco a proposito: <c>EnforceExtendedAnalyzerRules</c> prohibe la E/S de
/// archivos dentro de un analizador (RS1035) y Roslyn hace copia en sombra, asi que el directorio
/// del <c>.dll</c> tampoco seria una fuente fiable. Solo se mira lo que el anfitrion ya cargo.
/// </para>
/// </summary>
internal static class PartialGeneratorRegistry
{
    internal const string PartialAssemblyPrefix = "SourceCrafter.DependencyInjection.Partial";

    private static readonly object gate = new();
    private static PartialGeneratorHandle[] partials = [];
    private static int scannedAssemblies = -1;

    /// <summary>
    /// Parciales registrados, en orden de ejecucion.
    ///
    /// <para>
    /// El barrido se repite mientras sigan apareciendo ensamblados candidatos: Roslyn carga los
    /// analizadores de forma perezosa y sin orden garantizado, asi que en el instante del
    /// <see cref="ModuleInitializerAttribute"/> el parcial puede no estar todavia ahi. Al primer
    /// acceso -ya dentro de una pasada de generacion- si lo esta, y a partir de entonces el
    /// conteo se estabiliza y no se vuelve a barrer.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<PartialGeneratorHandle> Partials
    {
        get
        {
            lock (gate)
            {
                var loaded = EnumerateCandidates().ToArray();

                if (loaded.Length != scannedAssemblies)
                {
                    scannedAssemblies = loaded.Length;
                    partials = Discover(loaded);
                }

                return partials;
            }
        }
    }

    // CA2255: el escenario que la regla exceptua es justo este -codigo de generador que
    // necesita inicializarse al cargarse el ensamblado-, y no hay otro punto de enganche:
    // Initialize() del generador corre por compilacion, no por carga.
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Initialize()
    {
        // Una excepcion aqui tumbaria la carga del analizador entero -sin diagnostico y sin
        // pila util-, asi que un descubrimiento fallido degrada a "no hay parciales".
        try
        {
            _ = Partials;
        }
        catch
        {
            partials = [];
            scannedAssemblies = -1;
        }
    }

    /// <summary>
    /// Se recorren <b>todos</b> los contextos de carga, no solo el propio.
    ///
    /// <para>
    /// Roslyn en .NET Core reparte los analizadores en un <c>DirectoryLoadContext</c> por
    /// directorio, asi que un parcial que venga de otra carpeta -el caso normal: otro paquete-
    /// cae en un contexto distinto y <c>Assemblies</c> del propio no lo veria nunca.
    /// </para>
    /// </summary>
    private static IEnumerable<Assembly> EnumerateCandidates()
    {
        foreach (var context in AssemblyLoadContext.All)
        {
            foreach (var assembly in context.Assemblies)
            {
                if (assembly.GetName().Name is { } name
                    && name.StartsWith(PartialAssemblyPrefix, StringComparison.Ordinal))
                {
                    yield return assembly;
                }
            }
        }
    }

    private static PartialGeneratorHandle[] Discover(Assembly[] loaded)
    {
        // El desempate por nombre de tipo evita que el orden de carga de los ensamblados
        // -que Roslyn no garantiza- se cuele en el codigo emitido.
        PriorityQueue<PartialGeneratorHandle, (int Priority, string TypeName)> queue = new();

        // El mismo parcial puede estar cargado varias veces: Roslyn hace copia en sombra por
        // compilacion y crea un DirectoryLoadContext por directorio, asi que en un proceso
        // longevo -Visual Studio- el mismo ensamblado aparece en varios contextos. Sin este
        // desduplicado por nombre de tipo el parcial se ejecutaria una vez por copia y emitiria
        // los mismos miembros repetidos (CS0102/CS0111).
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (var assembly in loaded)
        {
            foreach (var type in GetLoadableTypes(assembly))
            {
                if (type is not { IsClass: true, IsAbstract: false, IsPublic: true }
                    || type.GetConstructor(Type.EmptyTypes) is null) continue;

                var typeName = type.FullName ?? type.Name;

                if (!seen.Add(typeName)) continue;

                try
                {
                    if (PartialGeneratorHandle.TryBind(type) is { } handle)
                        queue.Enqueue(handle, (handle.Priority, typeName));
                }
                catch
                {
                    // Un parcial que no se puede construir o enlazar se ignora: no debe impedir
                    // que corran los demas.
                }
            }
        }

        var result = new PartialGeneratorHandle[queue.Count];

        for (var i = 0; i < result.Length; i++) result[i] = queue.Dequeue();

        return result;
    }

    /// <summary>
    /// Un ensamblado con dependencias a medias no debe impedir que se descubran los demas: se
    /// conservan los tipos que si resolvieron.
    /// </summary>
    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.OfType<Type>();
        }
        catch
        {
            return [];
        }
    }
}

/// <summary>
/// Adaptador a un generador parcial que vive en otro <c>AssemblyLoadContext</c>.
///
/// <para>
/// El enlace es <b>por nombre completo y reflexion</b>, no por identidad de tipo: el
/// <c>IIncrementalGeneratorPartial</c> que ve el parcial no es el mismo <see cref="Type"/> que el
/// nuestro aunque ambos salgan del mismo archivo enlazado, porque cada directorio de analizadores
/// tiene su propio contexto de carga.
/// </para>
///
/// <para>
/// Los argumentos <b>si</b> pueden ser tipos de Roslyn: el cargador de analizadores delega
/// <c>Microsoft.CodeAnalysis*</c> al contexto por defecto, asi que el ensamblado se carga una
/// sola vez en el proceso y <c>typeof(Compilation)</c> es identico a ambos lados. Se comprobo
/// midiendo: mismo <c>Location</c> (<c>Roslyn\bincore</c>), mismo <c>Assembly</c> por referencia.
/// </para>
/// </summary>
internal sealed class PartialGeneratorHandle(object instance, MethodInfo analyze, int priority)
{
    internal int Priority { get; } = priority;

    internal static PartialGeneratorHandle? TryBind(Type type)
    {
        var contractName = typeof(IIncrementalGeneratorPartial).FullName;

        foreach (var iface in type.GetInterfaces())
        {
            if (iface.FullName != contractName) continue;

            // Mapa explicito de la implementacion: la interfaz puede estar implementada de forma
            // explicita, y en ese caso el metodo no aparece en la superficie publica del tipo.
            var map = type.GetInterfaceMap(iface);
            MethodInfo? analyze = null;

            for (var i = 0; i < map.InterfaceMethods.Length; i++)
            {
                if (map.InterfaceMethods[i].Name == nameof(IIncrementalGeneratorPartial.Analyze))
                {
                    analyze = map.TargetMethods[i];
                    break;
                }
            }

            if (analyze is null || Activator.CreateInstance(type) is not { } instance) return null;

            var priority = iface.GetProperty(nameof(IIncrementalGeneratorPartial.Priority))
                ?.GetValue(instance) as int? ?? 0;

            return new PartialGeneratorHandle(instance, analyze, priority);
        }

        return null;
    }

    /// <summary>
    /// Ejecuta el parcial. Un fallo suyo no puede tumbar la generacion del contenedor, asi que
    /// se degrada a "no aporto nada".
    ///
    /// <para>
    /// La tupla de retorno se lee por reflexion sobre sus campos <c>Item1</c>/<c>Item2</c>:
    /// <c>ValueTuple</c> viene de la BCL y tiene identidad unica, pero el tipo estatico de
    /// nuestro lado no es visible desde el <c>MethodInfo</c> del otro.
    /// </para>
    /// </summary>
    internal (string[][] Files, Diagnostic[] Diagnostics) Analyze(
        INamedTypeSymbol containerType,
        SemanticModel semanticModel,
        SyntaxNode? declaration,
        string[] containerData,
        string[][] serviceData,
        object?[] serviceSymbols,
        CancellationToken cancelToken)
    {
        try
        {
            var result = analyze.Invoke(
                instance,
                [containerType, semanticModel, declaration, containerData, serviceData, serviceSymbols, cancelToken]);

            if (result is null) return ([], []);

            var type = result.GetType();
            var files = type.GetField("Item1")?.GetValue(result) as string[][] ?? [];
            var raw = type.GetField("Item2")?.GetValue(result) as object[] ?? [];

            var diagnostics = new List<Diagnostic>(raw.Length);

            foreach (var item in raw)
            {
                if (item is Diagnostic diagnostic) diagnostics.Add(diagnostic);
            }

            return (files, [.. diagnostics]);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return ([], []);
        }
    }
}

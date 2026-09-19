using System.Reflection;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SourceCrafter.DependencyInjection.Tests;

/// <summary>
/// Ejecuta el generador en memoria sobre un fragmento de codigo y devuelve lo que emite.
///
/// <para>El generador se referencia como analizador (<c>ReferenceOutputAssembly=false</c>),
/// asi que no hay una referencia de compilacion a el; se localiza su ensamblado en disco y
/// se carga por reflexion. De ese modo los tests no alteran el cableado del proyecto.</para>
/// </summary>
static class GeneratorHarness
{
    /// <summary>Resultado de una ejecucion del generador.</summary>
    internal sealed record Result(
        IReadOnlyDictionary<string, string> Sources,
        IReadOnlyList<Diagnostic> GeneratorDiagnostics,
        IReadOnlyList<Diagnostic> CompilationDiagnostics)
    {
        /// <summary>Devuelve el archivo generado cuyo nombre contiene <paramref name="hint"/>.</summary>
        internal string Source(string hint)
        {
            foreach (var (name, text) in Sources)
                if (name.Contains(hint, StringComparison.OrdinalIgnoreCase)) return text;

            throw new InvalidOperationException(
                $"No se genero ningun archivo que contenga '{hint}'. Generados: "
                + string.Join(", ", Sources.Keys));
        }

        internal IReadOnlyList<Diagnostic> Errors =>
            [.. GeneratorDiagnostics.Concat(CompilationDiagnostics)
                .Where(d => d.Severity == DiagnosticSeverity.Error)];

        internal bool HasDiagnostic(string id) =>
            GeneratorDiagnostics.Concat(CompilationDiagnostics).Any(d => d.Id == id);
    }

    static readonly Lazy<DirectoryInfo> RepoRoot = new(() =>
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SourceCrafter.DependencyInjection.slnx")))
            dir = dir.Parent;

        return dir ?? throw new InvalidOperationException("No se encontro la raiz del repositorio.");
    });

    static readonly Lazy<IIncrementalGenerator> Generator = new(() => Load("SourceCrafter.DependencyInjection"));

    /// <summary>
    /// La extension de configuracion vive en su propio ensamblado y emite un
    /// <c>partial</c> aparte del contenedor. Roslyn ejecuta ambos generadores sobre la
    /// <b>misma</b> compilacion de entrada, asi que ninguno ve la salida del otro: el
    /// contrato entre ellos es el nombre del miembro. Por eso hay que ejecutarlos juntos
    /// para comprobar que ese contrato se cumple.
    /// </summary>
    static readonly Lazy<IIncrementalGenerator> ConfigurationGenerator =
        new(() => Load("SourceCrafter.DependencyInjection.MsConfiguration"));

    static IIncrementalGenerator Load(string project)
    {
        var type = Assembly.LoadFrom(NewestAssembly(project))
            .GetTypes()
            .First(t => typeof(IIncrementalGenerator).IsAssignableFrom(t) && !t.IsAbstract);

        return (IIncrementalGenerator)Activator.CreateInstance(type)!;
    }

    static readonly Lazy<IReadOnlyList<MetadataReference>> References = new(() =>
    {
        List<MetadataReference> refs = [];

        foreach (var dll in Directory.EnumerateFiles(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "*.dll"))
        {
            // El directorio compartido mezcla ensamblados nativos sin metadatos.
            try { refs.Add(MetadataReference.CreateFromFile(dll)); } catch (Exception) { }
        }

        refs.Add(MetadataReference.CreateFromFile(NewestAssembly("SourceCrafter.DependencyInjection.Metadata")));

        return refs;
    });

    /// <summary>
    /// Las de siempre mas lo que necesita la extension de configuracion: sus atributos y
    /// los ensamblados de <c>Microsoft.Extensions.Configuration</c>. Sin estos ultimos la
    /// extension se desactiva a proposito, asi que omitirlos daria un falso verde.
    /// </summary>
    static readonly Lazy<IReadOnlyList<MetadataReference>> ConfigurationReferences = new(() =>
    {
        List<MetadataReference> refs = [.. References.Value];

        refs.Add(MetadataReference.CreateFromFile(
            NewestAssembly("SourceCrafter.DependencyInjection.MsConfiguration.Metadata")));

        foreach (var dll in Directory.EnumerateFiles(AppContext.BaseDirectory, "Microsoft.Extensions.Configuration*.dll"))
        {
            try { refs.Add(MetadataReference.CreateFromFile(dll)); } catch (Exception) { }
        }

        return refs;
    });

    static string NewestAssembly(string project)
    {
        var binDir = Path.Combine(RepoRoot.Value.FullName, project, "bin");

        return Directory
            .EnumerateFiles(binDir, project + ".dll", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"No se encontro el ensamblado de '{project}'.");
    }

    static CSharpParseOptions OptionsFor(LanguageVersion version) =>
        new CSharpParseOptions(version)
            .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "Probe")]);

    static readonly CSharpParseOptions ParseOptions = OptionsFor(LanguageVersion.Preview);

    /// <summary>
    /// Compila <paramref name="source"/> con el generador y devuelve lo emitido.
    /// </summary>
    /// <param name="languageVersion">
    /// Permite comprobar lo que el generador emite para lenguajes anteriores; algunas
    /// decisiones (como usar <c>System.Threading.Lock</c>) dependen de ello.
    /// </param>
    internal static Result Run(string source, LanguageVersion languageVersion = LanguageVersion.Preview)
    {
        var options = OptionsFor(languageVersion);

        var driver = CreateDriver(options)
            .RunGeneratorsAndUpdateCompilation(CreateCompilation(source, options), out var output, out _);

        return Collect(driver, output);
    }

    /// <summary>
    /// Compila <paramref name="source"/> con el generador de contenedores <b>y</b> el de
    /// configuracion, que es como se usan en un proyecto real.
    /// </summary>
    internal static Result RunWithConfiguration(string source)
    {
        var compilation = CSharpCompilation.Create(
            "Probe",
            [CSharpSyntaxTree.ParseText(source, ParseOptions, "Probe.cs")],
            ConfigurationReferences.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver
            .Create([Generator.Value.AsSourceGenerator(), ConfigurationGenerator.Value.AsSourceGenerator()], parseOptions: ParseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        return Collect(driver, output);
    }

    /// <summary>
    /// Ejecuta el generador varias veces sobre el mismo driver, tal y como hace el IDE al
    /// reevaluar una compilacion sin cambios. Devuelve la salida de cada pasada.
    /// </summary>
    internal static IReadOnlyList<Result> RunRepeatedly(string source, int passes)
    {
        var compilation = CreateCompilation(source);
        var driver = CreateDriver();

        List<Result> results = [];

        for (var i = 0; i < passes; i++)
        {
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
            results.Add(Collect(driver, output));
        }

        return results;
    }

    static GeneratorDriver CreateDriver(CSharpParseOptions? options = null) =>
        CSharpGeneratorDriver.Create([Generator.Value.AsSourceGenerator()], parseOptions: options ?? ParseOptions);

    static CSharpCompilation CreateCompilation(string source, CSharpParseOptions? options = null) =>
        CSharpCompilation.Create(
            "Probe",
            [CSharpSyntaxTree.ParseText(source, options ?? ParseOptions, "Probe.cs")],
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

    static Result Collect(GeneratorDriver driver, Compilation output)
    {
        var run = driver.GetRunResult();

        Dictionary<string, string> sources = [];

        foreach (var tree in run.GeneratedTrees)
            sources[Path.GetFileName(tree.FilePath)] = tree.GetText().ToString();

        // CS0009 lo provocan los ensamblados nativos del directorio compartido, no el
        // codigo generado.
        var compilationDiagnostics = output.GetDiagnostics().Where(d => d.Id != "CS0009").ToArray();

        return new(sources, [.. run.Diagnostics], compilationDiagnostics);
    }
}

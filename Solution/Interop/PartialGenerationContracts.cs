using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Generic;
using System.Threading;

namespace SourceCrafter.DependencyInjection.Generation;

/// <summary>
/// Ciclo de vida de un servicio, tal y como lo ve un generador parcial. Replica los valores de
/// <c>Lifetime</c>, que es <c>internal</c> y vive en el espacio de nombres global.
/// </summary>
public enum PartialLifetime : byte { Singleton, Scoped, Transient }

/// <summary>
/// Forma asincrona de la resolucion de un servicio. Replica <c>AsyncKind</c>.
/// </summary>
public enum PartialAsyncKind : byte { None, ValueTask, Task }

/// <summary>
/// Forma de liberacion de un servicio. Replica <c>Disposability</c>.
/// </summary>
public enum PartialDisposability : byte { None, Disposable, AsyncDisposable }

/// <summary>
/// Archivo producido por un generador parcial.
/// </summary>
/// <param name="FileName">
/// Nombre del archivo <b>sin</b> extension. El generador le anade <c>.g</c> y resuelve las
/// colisiones con un sufijo numerico, igual que hace con los contenedores.
/// </param>
/// <param name="Code">Contenido C# completo, incluida su propia cabecera.</param>
public readonly record struct PartialFile(string FileName, string Code);

/// <summary>
/// Un servicio registrado en el contenedor, con su simbolo asociado.
/// </summary>
public sealed class ServiceInfo(
    string exportTypeFullName,
    string key,
    PartialLifetime lifetime,
    PartialAsyncKind asyncKind,
    string? memberName,
    bool memberIsMethodShaped,
    bool isInlineable,
    string implTypeFullName,
    bool isCached,
    bool isFactory,
    bool isExternal,
    PartialDisposability disposability,
    ITypeSymbol? exportType,
    ITypeSymbol? implType)
{
    /// <summary>Tipo expuesto, cualificado con <c>global::</c>.</summary>
    public string ExportTypeFullName { get; } = exportTypeFullName;

    /// <summary>Clave de registro, o cadena vacia si no tiene.</summary>
    public string Key { get; } = key;

    public PartialLifetime Lifetime { get; } = lifetime;

    public PartialAsyncKind AsyncKind { get; } = asyncKind;

    /// <summary>
    /// Miembro del contenedor que lo resuelve, o <c>null</c> si no llego a exponerse
    /// (un transient inlineado sin <c>exportTransients</c>).
    /// </summary>
    public string? MemberName { get; } = memberName;

    /// <summary>Cierto si <see cref="MemberName"/> se emite como metodo y hay que invocarlo.</summary>
    public bool MemberIsMethodShaped { get; } = memberIsMethodShaped;

    /// <summary>Cierto para un transient simple que se reconstruye en cada sitio de uso.</summary>
    public bool IsInlineable { get; } = isInlineable;

    /// <summary>Tipo de la implementacion, cualificado con <c>global::</c>.</summary>
    public string ImplTypeFullName { get; } = implTypeFullName;

    /// <summary>
    /// Cierto si el valor se guarda en un campo de respaldo. Equivale a un lifetime distinto
    /// de <see cref="PartialLifetime.Transient"/>: quien emita un <c>using</c> sobre el valor
    /// debe consultarlo, porque liberar un cacheado lo destruye para el resto del contenedor.
    /// </summary>
    public bool IsCached { get; } = isCached;

    /// <summary>Cierto si el valor lo produce una fabrica declarada por el autor.</summary>
    public bool IsFactory { get; } = isFactory;

    /// <summary>Cierto si el registro proviene de un ensamblado externo.</summary>
    public bool IsExternal { get; } = isExternal;

    /// <summary>Forma de liberacion del tipo de la implementacion.</summary>
    public PartialDisposability Disposability { get; } = disposability;

    /// <summary>
    /// Simbolo del tipo expuesto. Solo es valido durante
    /// <see cref="ServiceProviderPartial.AnalyzeContainer"/>; ver la nota de vigencia en
    /// <see cref="ServiceProviderInfo"/>.
    /// </summary>
    /// <para>
    /// Es <c>null</c> si el tipo no se puede volver a resolver por nombre de metadatos
    /// (tuplas, genericos construidos, tipos de archivo). El nombre completo siempre esta en
    /// <see cref="ExportTypeFullName"/>; para el resto del grafo esta
    /// <see cref="ServiceProviderInfo.Compilation"/>.
    /// </para>
    public ITypeSymbol? ExportType { get; } = exportType;

    /// <summary>
    /// Simbolo del tipo de la implementacion, con la misma vigencia y las mismas limitaciones
    /// que <see cref="ExportType"/>. Coincide con el si el servicio se registro sin interfaz.
    /// </summary>
    public ITypeSymbol? ImplType { get; } = implType;
}

/// <summary>
/// Vista de un contenedor ya analizado, con acceso completo al modelo de Roslyn.
///
/// <para>
/// <b>Vigencia.</b> <see cref="Compilation"/>, <see cref="SemanticModel"/> y todos los
/// <c>ISymbol</c> de aqui solo son validos <i>dentro</i> de la llamada a
/// <see cref="ServiceProviderPartial.AnalyzeContainer"/>. Retenerlos mas alla ancla la
/// <c>Compilation</c> entera en la cache incremental de Roslyn -decenas de MB por compilacion
/// viva- y hace que el generador recalcule en cada pulsacion de tecla. Lo que sobreviva a la
/// llamada debe ser ya cadena, bool o enum.
/// </para>
/// </summary>
public sealed class ServiceProviderInfo(
    string? nameSpace,
    string className,
    string containerFullTypeName,
    string typeName,
    string modifiers,
    bool isInterfaceProvider,
    bool implementsServiceProvider,
    bool genericApi,
    PartialDisposability containerDisposability,
    PartialDisposability scopedDisposability,
    IReadOnlyList<ServiceInfo> services,
    INamedTypeSymbol containerType,
    SemanticModel semanticModel,
    SyntaxNode? declaration,
    AnalyzerConfigOptions globalOptions)
{
    /// <summary>Espacio de nombres del contenedor, o <c>null</c> si esta en el global.</summary>
    public string? Namespace { get; } = nameSpace;

    /// <summary>Nombre simple del contenedor.</summary>
    public string ClassName { get; } = className;

    /// <summary>Nombre completo del contenedor, cualificado con <c>global::</c>.</summary>
    public string ContainerFullTypeName { get; } = containerFullTypeName;

    /// <summary>Declaracion del tipo tal y como la emite el contenedor (incluye genericos).</summary>
    public string TypeName { get; } = typeName;

    /// <summary>Modificadores de la declaracion (<c>public partial class</c>, ...).</summary>
    public string Modifiers { get; } = modifiers;

    public bool IsInterfaceProvider { get; } = isInterfaceProvider;

    public bool ImplementsServiceProvider { get; } = implementsServiceProvider;

    public bool GenericApi { get; } = genericApi;

    /// <summary>Forma de liberacion del contenedor raiz.</summary>
    public PartialDisposability ContainerDisposability { get; } = containerDisposability;

    /// <summary>Forma de liberacion de un ambito (<c>Scoped</c>).</summary>
    public PartialDisposability ScopedDisposability { get; } = scopedDisposability;

    /// <summary>Todos los registros del contenedor, en orden de declaracion.</summary>
    public IReadOnlyList<ServiceInfo> Services { get; } = services;

    /// <summary>Simbolo del tipo marcado con <c>[ServiceProvider]</c>.</summary>
    public INamedTypeSymbol ContainerType { get; } = containerType;

    /// <summary>Modelo semantico del archivo que declara el contenedor.</summary>
    public SemanticModel SemanticModel { get; } = semanticModel;

    /// <summary>Compilacion completa: da acceso a cualquier tipo del proyecto y sus referencias.</summary>
    public Compilation Compilation { get; } = semanticModel.Compilation;

    /// <summary>Sintaxis de la declaracion del contenedor, si esta disponible.</summary>
    public SyntaxNode? Declaration { get; } = declaration;

    /// <summary>
    /// Opciones globales del analizador (<c>build_property.*</c>: <c>TargetFramework</c>,
    /// <c>RuntimeIdentifier</c> si es <c>CompilerVisibleProperty</c>, ...).
    /// </summary>
    public AnalyzerConfigOptions GlobalOptions { get; } = globalOptions;
}

/// <summary>
/// Lo que un parcial produce durante el analisis de un contenedor.
/// </summary>
public sealed class PartialContribution
{
    private readonly List<PartialFile> files = [];
    private readonly List<Diagnostic> diagnostics = [];

    /// <summary>
    /// Anade un archivo. El codigo debe estar <b>ya renderizado</b>: no se puede diferir a un
    /// callback que corra fuera de la llamada, porque para entonces los simbolos ya no valen.
    /// </summary>
    public void AddSource(string fileName, string code)
    {
        if (fileName is { Length: > 0 } && code is not null) files.Add(new(fileName, code));
    }

    /// <summary>Anade un diagnostico, que el generador reportara como propio.</summary>
    public void ReportDiagnostic(Diagnostic diagnostic)
    {
        if (diagnostic is not null) diagnostics.Add(diagnostic);
    }

    /// <summary>
    /// Incluye <paramref name="member"/> -un miembro de instancia que el parcial declara en el
    /// contenedor- en el <c>Dispose</c>/<c>DisposeAsync</c> raiz que emite el generador. Se libera
    /// despues de los servicios (se considera creado antes que ellos) y nunca desde un ambito.
    /// </summary>
    public void AddDisposer(string member, PartialDisposability disposability)
    {
        if (member is { Length: > 0 } && disposability is not PartialDisposability.None)
            disposers.Add([member, ((byte)disposability).ToString()]);
    }

    private readonly List<string[]> disposers = [];

    internal IReadOnlyList<PartialFile> Files => files;
    internal IReadOnlyList<Diagnostic> Diagnostics => diagnostics;
    internal IReadOnlyList<string[]> Disposers => disposers;
}

/// <summary>
/// Punto de extension que descubre el generador en los ensamblados cuyo nombre empieza por
/// <c>SourceCrafter.DependencyInjection.Partial</c>. El tipo debe ser publico, no abstracto y
/// tener constructor sin parametros.
///
/// <para>
/// <b>Identidad de tipo.</b> Roslyn carga cada directorio de analizadores en su propio
/// <c>AssemblyLoadContext</c>, asi que <i>esta misma interfaz</i> compilada en el generador y en
/// el parcial son dos <c>Type</c> distintos: el enlace se hace por nombre completo y reflexion,
/// nunca con <c>is</c> ni <c>IsAssignableFrom</c>.
/// </para>
///
/// <para>
/// Los tipos de <c>Microsoft.CodeAnalysis</c> <b>si</b> cruzan la frontera: el cargador de
/// analizadores delega <c>Microsoft.CodeAnalysis*</c> al contexto por defecto, asi que hay una
/// sola instancia del ensamblado en el proceso y <c>typeof(Compilation)</c> es identico en ambos
/// lados. Por eso la firma puede declarar <c>object</c> y castearse sin riesgo.
/// </para>
///
/// <para>No se implementa a mano: se deriva de <see cref="ServiceProviderPartial"/>.</para>
/// </summary>
public interface IIncrementalGeneratorPartial
{
    /// <summary>
    /// Orden de ejecucion, ascendente. Los empates se rompen por el nombre completo del tipo,
    /// para que el resultado no dependa del orden de carga de los ensamblados.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Punto de entrada real. Se declara con <c>object</c> porque el tipo
    /// <see cref="ServiceProviderInfo"/> lo define cada lado por separado y no tiene identidad
    /// compartida; el receptor lo reconstruye desde los componentes de Roslyn, que si la tienen.
    /// </summary>
    /// <param name="containerType">El <c>INamedTypeSymbol</c> del contenedor.</param>
    /// <param name="semanticModel">El <c>SemanticModel</c> de su declaracion.</param>
    /// <param name="declaration">El <c>SyntaxNode</c> de la declaracion, o <c>null</c>.</param>
    /// <param name="containerData">Metadatos planos del contenedor, via <c>PartialCodec</c>.</param>
    /// <param name="serviceData">Una fila por servicio, via <c>PartialCodec</c>.</param>
    /// <param name="serviceSymbols">
    /// Dos entradas por servicio, intercaladas: el <c>ITypeSymbol</c> del tipo expuesto y el del
    /// tipo de la implementacion. Cualquier elemento puede ser <c>null</c>.
    /// </param>
    /// <param name="globalOptions">El <c>AnalyzerConfigOptions</c> global de la compilacion.</param>
    /// <param name="cancelToken">Cancelacion de la pasada de generacion.</param>
    /// <returns>
    /// <c>{ archivos, diagnosticos, liberadores }</c>: los archivos como filas
    /// <c>{ nombre, codigo }</c>, los diagnosticos como <c>Diagnostic</c> y los liberadores como
    /// filas <c>{ miembro, disposability }</c>. El receptor lee <c>Item3</c> solo si existe, asi que
    /// un parcial anterior (tupla de dos) sigue enlazando.
    /// </returns>
    (string[][] Files, object[] Diagnostics, string[][] Disposers) Analyze(
        object containerType,
        object semanticModel,
        object? declaration,
        string[] containerData,
        string[][] serviceData,
        object?[] serviceSymbols,
        object globalOptions,
        CancellationToken cancelToken);
}

/// <summary>
/// Traduccion de los metadatos planos del contenedor. Los <c>string[]</c> se usan solo para lo
/// que no es un tipo de Roslyn: cada lado compila su propia copia de esta clase, asi que sus
/// tipos no cruzan la frontera.
/// </summary>
public static class PartialCodec
{
    public static string[] EncodeContainer(
        string? nameSpace,
        string className,
        string containerFullTypeName,
        string typeName,
        string modifiers,
        bool isInterfaceProvider,
        bool implementsServiceProvider,
        bool genericApi,
        byte containerDisposability,
        byte scopedDisposability) =>
        [
            nameSpace ?? "",
            className,
            containerFullTypeName,
            typeName,
            modifiers,
            isInterfaceProvider ? "1" : "",
            implementsServiceProvider ? "1" : "",
            genericApi ? "1" : "",
            containerDisposability.ToString(),
            scopedDisposability.ToString(),
        ];

    public static string[] EncodeService(
        string exportTypeFullName,
        string key,
        byte lifetime,
        byte asyncKind,
        string? memberName,
        bool memberIsMethodShaped,
        bool isInlineable,
        string implTypeFullName,
        bool isCached,
        bool isFactory,
        bool isExternal,
        byte disposability) =>
        [
            exportTypeFullName,
            key,
            lifetime.ToString(),
            asyncKind.ToString(),
            // La cadena vacia no vale como marca: un miembro puede no existir, y eso es
            // distinto de existir con nombre vacio.
            memberName ?? "\0",
            memberIsMethodShaped ? "1" : "",
            isInlineable ? "1" : "",
            implTypeFullName,
            isCached ? "1" : "",
            isFactory ? "1" : "",
            isExternal ? "1" : "",
            disposability.ToString(),
        ];

    /// <param name="serviceSymbols">
    /// Dos entradas por servicio -tipo expuesto y tipo de implementacion-, intercaladas. Se
    /// mantiene un solo array para no cambiar la aridad de
    /// <see cref="IIncrementalGeneratorPartial.Analyze"/>, que se enlaza por reflexion.
    /// </param>
    internal static ServiceProviderInfo Decode(
        INamedTypeSymbol containerType,
        SemanticModel semanticModel,
        SyntaxNode? declaration,
        string[] container,
        string[][] services,
        object?[] serviceSymbols,
        AnalyzerConfigOptions globalOptions)
    {
        var decoded = new ServiceInfo[services.Length];

        for (var i = 0; i < services.Length; i++)
        {
            var row = services[i];
            var exportIndex = i * 2;
            var implIndex = exportIndex + 1;

            decoded[i] = new ServiceInfo(
                row[0],
                row[1],
                (PartialLifetime)byte.Parse(row[2]),
                (PartialAsyncKind)byte.Parse(row[3]),
                row[4] == "\0" ? null : row[4],
                row[5].Length > 0,
                row[6].Length > 0,
                row[7],
                row[8].Length > 0,
                row[9].Length > 0,
                row[10].Length > 0,
                (PartialDisposability)byte.Parse(row[11]),
                exportIndex < serviceSymbols.Length ? serviceSymbols[exportIndex] as ITypeSymbol : null,
                implIndex < serviceSymbols.Length ? serviceSymbols[implIndex] as ITypeSymbol : null);
        }

        return new ServiceProviderInfo(
            container[0].Length > 0 ? container[0] : null,
            container[1],
            container[2],
            container[3],
            container[4],
            container[5].Length > 0,
            container[6].Length > 0,
            container[7].Length > 0,
            (PartialDisposability)byte.Parse(container[8]),
            (PartialDisposability)byte.Parse(container[9]),
            decoded,
            containerType,
            semanticModel,
            declaration,
            globalOptions);
    }
}

/// <summary>
/// Base recomendada para un generador parcial: traduce la frontera y deja al autor trabajar con
/// <see cref="ServiceProviderInfo"/>, con acceso pleno a <c>Compilation</c> y simbolos.
/// </summary>
public abstract class ServiceProviderPartial : IIncrementalGeneratorPartial
{
    public virtual int Priority => 0;

    /// <summary>
    /// Inspecciona un contenedor y aporta archivos o diagnosticos.
    ///
    /// <para>
    /// Los simbolos de <paramref name="container"/> <b>no deben sobrevivir</b> a esta llamada:
    /// todo lo que se anada a <paramref name="contribution"/> tiene que estar ya renderizado a
    /// texto. Ver la nota de vigencia en <see cref="ServiceProviderInfo"/>.
    /// </para>
    /// </summary>
    public abstract void AnalyzeContainer(
        ServiceProviderInfo container,
        PartialContribution contribution,
        CancellationToken cancelToken);

    (string[][] Files, object[] Diagnostics, string[][] Disposers) IIncrementalGeneratorPartial.Analyze(
        object containerType,
        object semanticModel,
        object? declaration,
        string[] containerData,
        string[][] serviceData,
        object?[] serviceSymbols,
        object globalOptions,
        CancellationToken cancelToken)
    {
        PartialContribution contribution = new();

        AnalyzeContainer(
            PartialCodec.Decode(
                (INamedTypeSymbol)containerType,
                (SemanticModel)semanticModel,
                declaration as SyntaxNode,
                containerData,
                serviceData,
                serviceSymbols,
                (AnalyzerConfigOptions)globalOptions),
            contribution,
            cancelToken);

        var files = new string[contribution.Files.Count][];

        for (var i = 0; i < files.Length; i++)
        {
            var (fileName, code) = contribution.Files[i];
            files[i] = [fileName, code];
        }

        var diagnostics = new object[contribution.Diagnostics.Count];

        for (var i = 0; i < diagnostics.Length; i++) diagnostics[i] = contribution.Diagnostics[i];

        return (files, diagnostics, [.. contribution.Disposers]);
    }
}


/// <summary>
/// Solo existe para que Roslyn cargue el ensamblado parcial: sin algun <c>[Generator]</c> o
/// <c>[DiagnosticAnalyzer]</c> el cargador descarta el <c>.dll</c> y el registro de parciales
/// nunca lo veria. Es un analizador sin reglas para no aparecer en el arbol de analizadores.
/// </summary>
#pragma warning disable RS1041 // Las extensiones del compilador deben implementarse en ensamblados que tengan como destino netstandard2.0
[Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer(LanguageNames.CSharp)]
#pragma warning restore RS1041 // Las extensiones del compilador deben implementarse en ensamblados que tengan como destino netstandard2.0
sealed class PartialLoadTrigger : Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer
{
    public override System.Collections.Immutable.ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [];

    public override void Initialize(Microsoft.CodeAnalysis.Diagnostics.AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(Microsoft.CodeAnalysis.Diagnostics.GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
    }
}
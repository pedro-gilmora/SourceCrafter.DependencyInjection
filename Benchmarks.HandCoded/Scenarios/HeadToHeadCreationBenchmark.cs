using BenchmarkDotNet.Attributes;

using Benchmarks.HandCoded.Rivals;

namespace Benchmarks.HandCoded.Scenarios;

/// <summary>
/// Head-to-head: <b>creacion del contenedor</b>.
/// <para>
/// Se miden dos cosas y no una, porque la fila de "crear contenedor" a secas es donde mas facil
/// es publicar una victoria falsa:
/// <list type="number">
///   <item><b>Crear sin resolver</b> favorece a los perezosos, que no construyen nada.</item>
///   <item><b>Crear y resolver</b> favorece a los eager, que ya lo tenian hecho.</item>
/// </list>
/// Publicar solo una de las dos es elegir el ganador antes de medir. En este mismo repositorio
/// hubo un banco que resolvia un servicio dentro del escenario de creacion, lo que obligaba al
/// contenedor perezoso a construirlo igual y le impedia enseñar su unica ventaja; el resultado
/// parecia limpio y estaba sesgado.
/// </para>
/// <para>
/// <b>Aviso de semantica.</b> SourceCrafter guarda sus singletons en campos <c>static</c>, asi que
/// un contenedor nuevo <i>reutiliza</i> la instancia que ya existia mientras Jab, CircleDI y
/// Pure.DI la reconstruyen. Su cifra aqui no mide lo mismo que las demas, y por eso no debe
/// leerse como una victoria: es una decision de diseño distinta con una consecuencia visible.
/// </para>
/// <para>
/// <b>Las filas escritas a mano usan el contenedor lean</b>, con exactamente los tres servicios que
/// registran los rivales. Un contenedor con nueve campos de servicio pesa mas que uno con tres y
/// perderia esta tabla por su tamaño, no por su codigo.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class HeadToHeadCreationBenchmark
{
    // ----- crear sin resolver -----

    [Benchmark(Baseline = true, Description = "crear | Hand-coded lazy")]
    public object CreateLazy() => new LeanLazyContainer();

    [Benchmark(Description = "crear | Hand-coded eager")]
    public object CreateEager() => new EagerContainer();

    [Benchmark(Description = "crear | SourceCrafter")]
    public object CreateSourceCrafter() => new ScSingletonContainer();

    [Benchmark(Description = "crear | CircleDI")]
    public object CreateCircleDi() => new CircleSingletonContainer();

    [Benchmark(Description = "crear | Pure.DI")]
    public object CreatePureDi() => new PureDiSingletonContainer();

    [Benchmark(Description = "crear | Jab")]
    public object CreateJab() => new JabSingletonContainer();

    // ----- crear y resolver -----

    [Benchmark(Description = "crear+resolver | Hand-coded lazy")]
    public object CreateResolveLazy() => new LeanLazyContainer().SyncPlain;

    [Benchmark(Description = "crear+resolver | Hand-coded eager")]
    public object CreateResolveEager() => new EagerContainer().SingletonSyncPlain;

    [Benchmark(Description = "crear+resolver | SourceCrafter")]
    public object CreateResolveSourceCrafter() => new ScSingletonContainer().SyncPlain;

    [Benchmark(Description = "crear+resolver | CircleDI")]
    public object CreateResolveCircleDi() => new CircleSingletonContainer().SyncPlain;

    [Benchmark(Description = "crear+resolver | Pure.DI")]
    public object CreateResolvePureDi() => new PureDiSingletonContainer().Plain;

    [Benchmark(Description = "crear+resolver | Jab")]
    public object CreateResolveJab() => new JabSingletonContainer().GetService<SyncPlain>();
}

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

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
/// <para>
/// <b>CircleDI va en su propio grupo, con el eager escrito a mano como baseline.</b> Es la tabla
/// donde mezclarlo con los perezosos hace mas daño, porque aqui la diferencia <i>no es de
/// implementacion en absoluto</i>: CircleDI construye el grafo de singletons en el constructor, asi
/// que en "crear" hace un trabajo que los perezosos aplazan, y en "crear+resolver" ya lo tiene hecho.
/// Cada mitad de la tabla le daria un resultado opuesto y ninguno de los dos seria un juicio sobre su
/// calidad. Enfrentado al eager escrito a mano, que tiene su mismo contrato, la comparacion vuelve a
/// medir codigo.
/// </para>
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class HeadToHeadCreationBenchmark
{
    // ===== crear sin resolver =====

    [BenchmarkCategory("crear | eager")]
    [Benchmark(Baseline = true, Description = "Hand-coded eager")]
    public object CreateEager() => new EagerContainer();

    [BenchmarkCategory("crear | eager")]
    [Benchmark(Description = "CircleDI")]
    public object CreateCircleDi() => new CircleSingletonContainer();

    [BenchmarkCategory("crear | perezosos")]
    [Benchmark(Baseline = true, Description = "Hand-coded lazy")]
    public object CreateLazy() => new LeanLazyContainer();

    [BenchmarkCategory("crear | perezosos")]
    [Benchmark(Description = "SourceCrafter")]
    public object CreateSourceCrafter() => new ScSingletonContainer();

    [BenchmarkCategory("crear | perezosos")]
    [Benchmark(Description = "Pure.DI")]
    public object CreatePureDi() => new PureDiSingletonContainer();

    [BenchmarkCategory("crear | perezosos")]
    [Benchmark(Description = "Jab")]
    public object CreateJab() => new JabSingletonContainer();

    // ===== crear y resolver =====

    [BenchmarkCategory("crear+resolver | eager")]
    [Benchmark(Baseline = true, Description = "Hand-coded eager")]
    public object CreateResolveEager() => new EagerContainer().SingletonSyncPlain;

    [BenchmarkCategory("crear+resolver | eager")]
    [Benchmark(Description = "CircleDI")]
    public object CreateResolveCircleDi() => new CircleSingletonContainer().SyncPlain;

    [BenchmarkCategory("crear+resolver | perezosos")]
    [Benchmark(Baseline = true, Description = "Hand-coded lazy")]
    public object CreateResolveLazy() => new LeanLazyContainer().SyncPlain;

    [BenchmarkCategory("crear+resolver | perezosos")]
    [Benchmark(Description = "SourceCrafter")]
    public object CreateResolveSourceCrafter() => new ScSingletonContainer().SyncPlain;

    [BenchmarkCategory("crear+resolver | perezosos")]
    [Benchmark(Description = "Pure.DI")]
    public object CreateResolvePureDi() => new PureDiSingletonContainer().Plain;

    [BenchmarkCategory("crear+resolver | perezosos")]
    [Benchmark(Description = "Jab")]
    public object CreateResolveJab() => new JabSingletonContainer().GetService<SyncPlain>();
}

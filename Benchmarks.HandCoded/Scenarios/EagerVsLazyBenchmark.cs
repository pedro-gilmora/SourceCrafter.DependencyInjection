using BenchmarkDotNet.Attributes;

namespace Benchmarks.HandCoded.Scenarios;

/// <summary>
/// Eje aislado: <b>resolucion eager frente a resolucion lazy</b>.
/// <para>
/// Este eje no tiene ganador, y esa es la conclusion. Tiene <b>dos escenarios opuestos</b>, asi
/// que medir uno solo produce una victoria falsa para cualquiera de los dos. Por eso estan los
/// tres:
/// <list type="number">
///   <item><b>Crear sin resolver.</b> Favorece a lazy por construccion: eager materializa todo
///   el grafo de singletons en el constructor aunque la aplicacion no llegue a pedir ninguno.
///   Es el caso de una herramienta de linea de comandos que arranca, mira los argumentos y
///   sale.</item>
///   <item><b>Crear y resolver.</b> Favorece a eager: el trabajo hay que hacerlo igual, y eager
///   lo hace sin publicar nada, sin candado y sin prueba de nulo.</item>
///   <item><b>Camino caliente.</b> Es donde se pasa la vida un proceso de larga duracion y
///   donde eager toca el suelo teorico: un campo <c>readonly</c> leido desde una propiedad que
///   el JIT alinea se reduce a un <c>mov</c>. Lazy paga como minimo un <c>test</c> y un salto
///   sobre eso.</item>
/// </list>
/// </para>
/// <para>
/// <b>Lo que hay que mirar en el punto 3.</b> El salto que paga lazy lo acierta el predictor
/// siempre despues de la primera resolucion, asi que su coste esperado es practicamente cero.
/// Si las dos filas empatan, la lectura correcta no es "lazy es igual de rapido por suerte":
/// es que <i>el camino caliente de un singleton tiene un suelo duro que ambos alcanzan</i>, y
/// que optimizarlo mas no es posible. Es la conclusion mas util de todo el banco, porque dice
/// donde <b>no</b> merece la pena seguir trabajando.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class EagerVsLazyBenchmark
{
    private EagerContainer _eagerWarm = null!;
    private LockedContainer _lazyWarm = null!;

    [GlobalSetup]
    public void Setup()
    {
        _eagerWarm = new EagerContainer();
        _lazyWarm = new LockedContainer();
        _ = _lazyWarm.SingletonSyncPlain;
    }

    // ----- 1. crear sin resolver -----

    [Benchmark(Baseline = true, Description = "eager | crear sin resolver")]
    public EagerContainer EagerCreateOnly() => new();

    [Benchmark(Description = "lazy | crear sin resolver")]
    public LockedContainer LazyCreateOnly() => new();

    // ----- 2. crear y resolver -----

    [Benchmark(Description = "eager | crear y resolver")]
    public object EagerCreateAndResolve() => new EagerContainer().SingletonSyncPlain;

    [Benchmark(Description = "lazy | crear y resolver")]
    public object LazyCreateAndResolve() => new LockedContainer().SingletonSyncPlain;

    // ----- 3. camino caliente -----

    [Benchmark(Description = "eager | camino caliente")]
    public object EagerHot() => _eagerWarm.SingletonSyncPlain;

    [Benchmark(Description = "lazy | camino caliente")]
    public object LazyHot() => _lazyWarm.SingletonSyncPlain;
}

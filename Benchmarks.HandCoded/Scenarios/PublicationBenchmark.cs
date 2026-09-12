using BenchmarkDotNet.Attributes;

namespace Benchmarks.HandCoded.Scenarios;

/// <summary>
/// Eje aislado: <b>como se publica un servicio perezoso</b>.
/// <para>
/// Compara <c>lock(this)</c> (lo que corresponde a un scoped), <c>lock</c> sobre un candado estatico
/// (lo que corresponde a un singleton), <see cref="Interlocked.CompareExchange{T}"/>,
/// <see cref="Interlocked.Exchange{T}"/> y una lectura no volatil en el camino caliente.
/// </para>
/// <para>
/// <b>Las dos mitades de esta tabla miden cosas de naturaleza distinta y no se deben mezclar.</b>
/// El camino caliente corre millones de veces y ahi las cinco estrategias ejecutan <i>el mismo
/// codigo</i>: una lectura, una prueba de nulo y un salto. La publicacion corre <b>una vez por
/// servicio en toda la vida del proceso</b>, y es la unica mitad donde la eleccion se nota.
/// </para>
/// <para>
/// De ahi la conclusion que la tabla debe hacer evidente: <b>optimizar la publicacion es optimizar
/// algo que pasa una vez</b>. Aunque una estrategia fuese el doble de rapida publicando, el ahorro
/// total en la vida del proceso son unos pocos nanosegundos por servicio. Lo que si es permanente es
/// la correccion que se pierde por el camino, y eso no aparece en ninguna columna de tiempo.
/// </para>
/// <para>
/// <b>Aviso de lectura sobre <c>Exchange</c>.</b> Va a salir de los mas rapidos publicando y
/// <b>rompe la garantia del singleton</b>: escribe siempre, asi que dos llamadores simultaneos pueden
/// acabar con dos instancias distintas vivas a la vez. La sonda de <c>--check</c> lo cuenta. Su fila
/// esta aqui como advertencia, no como opcion.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class PublicationBenchmark
{
    private ScopedLockHolder _scopedLock = null!;
    private SingletonLockHolder _singletonLock = null!;
    private CompareExchangeHolder _cas = null!;
    private ExchangeHolder _exchange = null!;
    private PlainReadHolder _plain = null!;

    [GlobalSetup]
    public void Setup()
    {
        _scopedLock = new ScopedLockHolder();
        _singletonLock = new SingletonLockHolder();
        _cas = new CompareExchangeHolder();
        _exchange = new ExchangeHolder();
        _plain = new PlainReadHolder();

        // Publicar una vez: a partir de aqui los cinco estan en camino caliente.
        _ = _scopedLock.Value;
        _ = _singletonLock.Value;
        _ = _cas.Value;
        _ = _exchange.Value;
        _ = _plain.Value;
    }

    // ===== camino caliente: ya publicado =====
    //
    // Expectativa correcta: empate exacto en las cinco filas. Las cinco ejecutan una lectura, una
    // prueba de nulo y un salto que el predictor acierta siempre. Si alguna se separa, la causa esta
    // en la alineacion del codigo o en la resta de sobrecarga de BenchmarkDotNet, no en la
    // estrategia de publicacion, que en este camino ni siquiera se ejecuta.

    [Benchmark(Baseline = true, Description = "caliente | lock(this)")]
    public SyncPlain HotScopedLock() => _scopedLock.Value;

    [Benchmark(Description = "caliente | lock(estatico)")]
    public SyncPlain HotSingletonLock() => _singletonLock.Value;

    [Benchmark(Description = "caliente | CompareExchange")]
    public SyncPlain HotCas() => _cas.Value;

    [Benchmark(Description = "caliente | Exchange")]
    public SyncPlain HotExchange() => _exchange.Value;

    [Benchmark(Description = "caliente | lectura no volatil")]
    public SyncPlain HotPlainRead() => _plain.Value;

    // ===== publicacion: primera resolucion, sin contienda =====
    //
    // Un titular nuevo por invocacion, asi que cada operacion paga la construccion del titular mas
    // la publicacion. La construccion del titular es identica en las cinco filas y se resta sola al
    // comparar entre ellas.
    //
    // Sin contienda, un candado libre se adquiere con una operacion atomica igual que el CAS. La
    // diferencia esperada es pequeña, y ese es justo el resultado util: NO hay que elegir el metodo
    // de publicacion por su velocidad.

    [Benchmark(Description = "publicar | lock(this)")]
    public SyncPlain PublishScopedLock() => new ScopedLockHolder().Value;

    [Benchmark(Description = "publicar | lock(estatico)")]
    public SyncPlain PublishSingletonLock() => new SingletonLockHolder().Value;

    [Benchmark(Description = "publicar | CompareExchange")]
    public SyncPlain PublishCas() => new CompareExchangeHolder().Value;

    [Benchmark(Description = "publicar | Exchange")]
    public SyncPlain PublishExchange() => new ExchangeHolder().Value;

    [Benchmark(Description = "publicar | lectura no volatil")]
    public SyncPlain PublishPlainRead() => new PlainReadHolder().Value;
}

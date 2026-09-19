using BenchmarkDotNet.Attributes;

using Benchmarks.HandCoded.Rivals;

namespace Benchmarks.HandCoded.Scenarios;

/// <summary>
/// Head-to-head: <b>transitorio no desechable</b>.
/// <para>
/// Solo se mide la rama <b>no desechable</b>, y hay que decir por que en vez de dejar el hueco.
/// </para>
/// <para>
/// <b>Un transitorio desechable no es comparable entre estas librerias, porque no hacen el mismo
/// trabajo.</b> Rastrear cada instancia entregada para poder desecharla despues cuesta una lista
/// que crece sin limite; no rastrearla es gratis y filtra el recurso. Verificado en el codigo
/// generado: el contenedor de transitorios de SourceCrafter <i>no implementa ninguna interfaz de
/// desecho</i>, porque en este repositorio la decision firme es que el desecho de un transitorio
/// pertenece al sitio de llamada y no al contenedor. La version escrita a mano si los rastrea.
/// </para>
/// <para>
/// Enfrentar esas dos filas daria un ganador claro y sin sentido: gana quien menos trabajo hace.
/// La diferencia es de <b>semantica</b>, no de rendimiento, y el sitio de medirla es
/// <see cref="MatrixTransientDisposableBenchmark"/>, donde se compara contra si misma y no contra
/// quien no lo implementa.
/// </para>
/// <para>
/// Lo que queda -- construir un objeto y devolverlo -- es un <c>new</c>. La expectativa correcta
/// es un empate en tiempo y una <b>coincidencia exacta en la columna de asignacion</b>: si alguna
/// fila asigna mas que las otras, ese exceso es sobrecoste puro del contenedor, y esa columna si
/// es reproducible byte a byte aunque los tiempos no lo sean.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class HeadToHeadTransientBenchmark
{
    private LockedContainer _handCoded = null!;
    private JabTransientContainer _jab = null!;
    private CircleTransientContainer _circle = null!;
    private PureDiTransientContainer _pureDi = null!;
    private ScTransientContainer _sc = null!;

    [GlobalSetup]
    public void Setup()
    {
        _handCoded = new LockedContainer();
        _jab = new JabTransientContainer();
        _circle = new CircleTransientContainer();
        _pureDi = new PureDiTransientContainer();
        _sc = new ScTransientContainer();
    }

    [Benchmark(Baseline = true, Description = "Hand-coded")]
    public SyncPlain HandCoded() => _handCoded.TransientSyncPlain();

    [Benchmark(Description = "SourceCrafter")]
    public SyncPlain SourceCrafter() => _sc.SyncPlain;

    [Benchmark(Description = "CircleDI")]
    public SyncPlain CircleDi() => _circle.SyncPlain;

    [Benchmark(Description = "Pure.DI")]
    public SyncPlain PureDi() => _pureDi.Plain;

    [Benchmark(Description = "Jab")]
    public SyncPlain Jab() => _jab.GetService<SyncPlain>();
}

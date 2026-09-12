using BenchmarkDotNet.Attributes;

using Benchmarks.HandCoded.Rivals;

namespace Benchmarks.HandCoded.Scenarios;

/// <summary>
/// Head-to-head: <b>camino caliente de un singleton</b>.
/// <para>
/// Es el escenario que mas se cita y el que menos discrimina. Todas las implementaciones
/// convergen a lo mismo -- leer un campo -- asi que hay un <b>suelo duro</b> que cualquiera
/// alcanza. Se mide igualmente porque un contenedor que <i>no</i> lo alcanzara tendria un
/// defecto, pero la expectativa correcta es un empate, no una victoria.
/// </para>
/// <para>
/// <b>Aviso sobre la fila de Jab.</b> Jab no expone miembros con nombre: su superficie publica es
/// <c>GetService&lt;T&gt;()</c>, que hace una prueba de tipo en cada llamada. Su cifra incluye ese
/// despacho porque es lo unico que un usuario de Jab puede escribir, no porque el banco le añada
/// trabajo. Comparar su numero con una lectura de campo compara dos APIs, no dos motores.
/// </para>
/// <para>
/// <b>Y un aviso mas serio, que aprendimos por las malas en este repositorio:</b> por debajo del
/// nanosegundo, BenchmarkDotNet resta la sobrecarga de una llamada vacia. A un captador que el JIT
/// alinea por completo no se le resta nada; a uno demasiado grande para alinearse se le resta una
/// llamada entera. El resultado es que <i>un camino caliente mas gordo puede sacar el numero mas
/// bajo</i>. Una diferencia en esta tabla no significa nada sin mirar el desensamblado.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class HeadToHeadSingletonBenchmark
{
    private EagerContainer _eager = null!;
    private LeanLazyContainer _lazy = null!;
    private JabSingletonContainer _jab = null!;
    private CircleSingletonContainer _circle = null!;
    private PureDiSingletonContainer _pureDi = null!;
    private ScSingletonContainer _sc = null!;

    [GlobalSetup]
    public void Setup()
    {
        _eager = new EagerContainer();
        _lazy = new LeanLazyContainer();
        _jab = new JabSingletonContainer();
        _circle = new CircleSingletonContainer();
        _pureDi = new PureDiSingletonContainer();
        _sc = new ScSingletonContainer();

        _ = _lazy.SyncPlain;
        _ = _jab.GetService<SyncPlain>();
        _ = _pureDi.Plain;
        _ = _sc.SyncPlain;
    }

    [Benchmark(Baseline = true, Description = "Hand-coded eager")]
    public SyncPlain Eager() => _eager.SingletonSyncPlain;

    [Benchmark(Description = "Hand-coded lazy")]
    public SyncPlain Lazy() => _lazy.SyncPlain;

    [Benchmark(Description = "SourceCrafter")]
    public SyncPlain SourceCrafter() => _sc.SyncPlain;

    [Benchmark(Description = "CircleDI")]
    public SyncPlain CircleDi() => _circle.SyncPlain;

    [Benchmark(Description = "Pure.DI")]
    public SyncPlain PureDi() => _pureDi.Plain;

    [Benchmark(Description = "Jab")]
    public SyncPlain Jab() => _jab.GetService<SyncPlain>();
}

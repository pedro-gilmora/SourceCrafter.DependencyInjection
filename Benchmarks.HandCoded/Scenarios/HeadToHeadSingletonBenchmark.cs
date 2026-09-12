using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

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
/// <para>
/// <b>La tabla va partida en dos grupos.</b> CircleDI construye en el constructor y no usa ningun
/// primitivo de sincronizacion: su captador es una lectura de campo de solo lectura, sin prueba de
/// nulo y sin barrera. Los tres perezosos pagan por contrato una lectura volatil y un salto. Meter a
/// los cinco en una sola tabla con un solo baseline presenta como diferencia de calidad lo que es
/// una diferencia de semantica. De ahi el grupo "sin candados" (hand-coded eager frente a CircleDI,
/// que es la unica comparacion justa para CircleDI) y el grupo "perezosos" (hand-coded lazy frente a
/// Jab, Pure.DI y SourceCrafter).
/// </para>
/// <para>
/// <b>Expectativa declarada de antemano, para no interpretar ruido despues:</b> la corrida anterior
/// dio a esta tabla <c>RatioSD</c> entre 0,67 y 0,84 sobre diferencias de una decima de nanosegundo.
/// Eso es un instrumento que no distingue nada. Antes de leer una sola fila de aqui hay que mirar el
/// grupo de ~0,5 ns de <see cref="ControlBenchmark"/>: si sus tres filas identicas no quedan juntas,
/// esta tabla no es publicable por muy estrecha que parezca cada barra de error por separado.
/// </para>
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
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

    [BenchmarkCategory("sin candados")]
    [Benchmark(Baseline = true, Description = "Hand-coded eager")]
    public SyncPlain Eager() => _eager.SingletonSyncPlain;

    [BenchmarkCategory("sin candados")]
    [Benchmark(Description = "CircleDI")]
    public SyncPlain CircleDi() => _circle.SyncPlain;

    [BenchmarkCategory("perezosos")]
    [Benchmark(Baseline = true, Description = "Hand-coded lazy")]
    public SyncPlain Lazy() => _lazy.SyncPlain;

    [BenchmarkCategory("perezosos")]
    [Benchmark(Description = "SourceCrafter")]
    public SyncPlain SourceCrafter() => _sc.SyncPlain;

    [BenchmarkCategory("perezosos")]
    [Benchmark(Description = "Pure.DI")]
    public SyncPlain PureDi() => _pureDi.Plain;

    [BenchmarkCategory("perezosos")]
    [Benchmark(Description = "Jab")]
    public SyncPlain Jab() => _jab.GetService<SyncPlain>();
}

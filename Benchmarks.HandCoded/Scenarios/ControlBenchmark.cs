using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace Benchmarks.HandCoded.Scenarios;

/// <summary>
/// Grupo de control: metodos identicos <b>a tres magnitudes distintas</b>.
/// <para>
/// No mide ningun contenedor. Mide el <i>instrumento</i>. Como los metodos de cada grupo hacen
/// exactamente lo mismo, la unica lectura correcta es que las tres filas de cada grupo queden
/// juntas. Lo que se separen es el ruido del banco a esa magnitud, y por debajo de ese margen
/// ninguna otra tabla de la corrida significa nada.
/// </para>
/// <para>
/// <b>La version anterior de este control media una sola magnitud, y eso lo hacia inutil justo
/// donde mas falta hacia.</b> Media el grafo profundo, unos 33 ns, y pasaba con una banda del 9%.
/// Con ese visto bueno se publicaba luego una tabla de camino caliente a 0,5 ns... cuyas filas
/// traian <c>RatioSD</c> de 0,67 a 0,84. Es decir: el control daba por buena una corrida en la que
/// la tabla que de verdad importaba no distinguia nada. <b>Un control a 33 ns no dice absolutamente
/// nada sobre el ruido a 0,5 ns</b>, porque a esa escala manda la resta de sobrecarga que hace
/// BenchmarkDotNet y la alineacion del codigo, no el planificador.
/// </para>
/// <para>
/// De ahi los tres grupos, elegidos para cubrir el rango real de este banco:
/// <list type="table">
///   <item>
///     <term>~0,5 ns</term>
///     <description>Lectura de campo. Es la magnitud de las tablas de camino caliente y de la
///     matriz de singleton y scoped. <b>Es el grupo que hay que mirar primero</b>, porque es donde
///     el instrumento peor discrimina y donde mas facil es publicar una victoria inventada.</description>
///   </item>
///   <item>
///     <term>~3 ns</term>
///     <description>Una asignacion pequeña. Es la magnitud de los transitorios y del ambito
///     vacio.</description>
///   </item>
///   <item>
///     <term>~33 ns</term>
///     <description>Grafo de quince nodos. Es la magnitud del ciclo completo de ambito y de los
///     escenarios de creacion.</description>
///   </item>
/// </list>
/// </para>
/// <para>
/// <b>Esto no es celo metodologico: es la leccion de un informe equivocado.</b> En este repositorio
/// se publico que un escenario iba 2,3 veces por detras de un competidor. Al montar el primer
/// control, seis metodos identicos dieron entre 32,57 y 44,80 ns y uno marco <c>Ratio</c> 1,35
/// contra un baseline que ejecutaba su mismo codigo. La brecha no existia. Sin control no habia
/// forma de saberlo, y el informe se dio por bueno durante una sesion entera.
/// </para>
/// <para>
/// Tres metodos por grupo y no seis: con la afinidad fijada a los P-cores, seis no aportaban mas
/// informacion que tres y costaban el doble de tiempo en cada corrida.
/// </para>
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ControlBenchmark
{
    private readonly SyncPlain _service = new();
    private readonly SyncPlain _a = new();
    private readonly SyncPlain _b = new();
    private readonly SyncPlain _c = new();

    // ===== ~0,5 ns: lectura de campo =====
    //
    // La magnitud de las tablas de camino caliente. Si estas tres filas no quedan juntas, las
    // tablas de singleton y scoped de esta corrida NO son publicables por muy estrecha que sea la
    // barra de error de cada fila por separado.

    [BenchmarkCategory("~0.5ns lectura")]
    [Benchmark(Baseline = true, Description = "campo A")]
    public SyncPlain FieldA() => _a;

    [BenchmarkCategory("~0.5ns lectura")]
    [Benchmark(Description = "campo B")]
    public SyncPlain FieldB() => _b;

    [BenchmarkCategory("~0.5ns lectura")]
    [Benchmark(Description = "campo C")]
    public SyncPlain FieldC() => _c;

    // ===== ~3 ns: una asignacion pequeña =====
    //
    // La magnitud de los transitorios y del ambito vacio.

    [BenchmarkCategory("~3ns asignacion")]
    [Benchmark(Baseline = true, Description = "asignar A")]
    public SyncPlain AllocA() => new();

    [BenchmarkCategory("~3ns asignacion")]
    [Benchmark(Description = "asignar B")]
    public SyncPlain AllocB() => new();

    [BenchmarkCategory("~3ns asignacion")]
    [Benchmark(Description = "asignar C")]
    public SyncPlain AllocC() => new();

    // ===== ~33 ns: grafo de quince nodos =====
    //
    // La magnitud del ciclo completo de ambito y de la creacion de contenedores.

    private Level1 Build() => new(
        new Level2(new Level3(new Leaf(), new Leaf()), new Level3(new Leaf(), new Leaf())),
        new Level2(new Level3(new Leaf(), new Leaf()), new Level3(new Leaf(), new Leaf())),
        _service);

    [BenchmarkCategory("~33ns grafo")]
    [Benchmark(Baseline = true, Description = "grafo A")]
    public Level1 GraphA() => Build();

    [BenchmarkCategory("~33ns grafo")]
    [Benchmark(Description = "grafo B")]
    public Level1 GraphB() => Build();

    [BenchmarkCategory("~33ns grafo")]
    [Benchmark(Description = "grafo C")]
    public Level1 GraphC() => Build();
}

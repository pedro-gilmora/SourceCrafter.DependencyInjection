using BenchmarkDotNet.Attributes;

namespace Benchmarks.HandCoded.Scenarios;

/// <summary>
/// Grupo de control: <b>seis metodos que ejecutan exactamente el mismo codigo</b>.
/// <para>
/// No mide ningun contenedor. Mide el <i>instrumento</i>. Como los seis hacen lo mismo, la
/// unica lectura correcta de su tabla es que las seis filas queden agrupadas dentro de su
/// propio error. Cualquier separacion es ruido de la maquina, y esa magnitud es el suelo por
/// debajo del cual ninguna otra tabla de la misma corrida significa nada.
/// </para>
/// <para>
/// <b>Esto no es celo metodologico: es la leccion de un informe equivocado.</b> En este repo se
/// publico que un escenario iba 2,3 veces por detras de un competidor. Al montar este control,
/// seis metodos identicos dieron entre 32,57 y 44,80 ns y uno de ellos marco <c>Ratio</c> 1,35
/// contra un baseline que ejecutaba su mismo codigo. La brecha no existia. Sin un control no
/// habia forma de saberlo, y el informe se dio por bueno durante toda una sesion.
/// </para>
/// <para>
/// Se mide el grafo profundo y no una celda de la matriz porque hace falta un escenario con
/// suficiente trabajo para que el ruido se vea: una celda de 0,5 ns no distingue entre estar
/// bien medida y estar mal medida.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class ControlBenchmark
{
    private readonly SyncPlain _service = new();

    private Level1 Build() => new(
        new Level2(new Level3(new Leaf(), new Leaf()), new Level3(new Leaf(), new Leaf())),
        new Level2(new Level3(new Leaf(), new Leaf()), new Level3(new Leaf(), new Leaf())),
        _service);

    [Benchmark(Baseline = true, Description = "Identico A")]
    public Level1 A() => Build();

    [Benchmark(Description = "Identico B")]
    public Level1 B() => Build();

    [Benchmark(Description = "Identico C")]
    public Level1 C() => Build();

    [Benchmark(Description = "Identico D")]
    public Level1 D() => Build();

    [Benchmark(Description = "Identico E")]
    public Level1 E() => Build();

    [Benchmark(Description = "Identico F")]
    public Level1 F() => Build();
}

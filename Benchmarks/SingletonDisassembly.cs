using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;

namespace Benchmarks;

/// <summary>
/// El mismo escenario <i>Singleton</i>, pero volcando el <b>codigo maquina</b> de cada rama.
/// <para>
/// La fila de tiempos no sirve para esto: a 0,2-0,6 ns se esta midiendo un ciclo de reloj y
/// el bucle de BenchmarkDotNet, no el contenedor. La pregunta "¿por que Jab marca la mitad?"
/// es mecanica, no estadistica, y el desensamblado la contesta sin margen de error: o el
/// captador se reduce a una carga, o no.
/// </para>
/// <para>
/// Lo que hay que buscar en la salida: en el camino de SourceCrafter, una llamada al ayudante
/// de base estatica (<c>CORINFO_HELP_GET_GCSTATIC_BASE</c> y similares) antes de la carga del
/// campo. Los singletons se guardan en campos <c>static</c>, mientras que Jab y Pure.DI los
/// guardan en campos de instancia alcanzados por <c>_root</c>.
/// </para>
/// </summary>
[MemoryDiagnoser]
[DisassemblyDiagnoser(maxDepth: 3, printSource: true)]
public class SingletonDisassemblyBenchmark : ContainerBenchmark
{
	private static readonly IDatabase HandCodedInstance = new Database(new Settings());

	[Benchmark(Baseline = true, Description = "Hand Coded")]
	public IDatabase HandCoded() => HandCodedInstance;

	[Benchmark(Description = "SourceCrafter")]
	public IDatabase SourceCrafterDi() => SourceCrafter.Database;

	[Benchmark(Description = "Jab")]
	public IDatabase JabDi() => Jab.GetService<IDatabase>();

	[Benchmark(Description = "Pure.DI")]
	public IDatabase PureDiDi() => PureDi.Database;
}

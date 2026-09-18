using BenchmarkDotNet.Attributes;

namespace Benchmarks.HandCoded.Scenarios;

/// <summary>
/// Matriz atomica, lifetime <b>transient</b>, rama <b>no desechable</b>: locking x async-kind.
/// <para>
/// Seis celdas. Un transitorio no cachea nada, asi que <b>no hay estado compartido y el eje de
/// locking no aplica</b>. Se mide igualmente con las dos estrategias, y eso es deliberado: las
/// seis celdas son <i>literalmente el mismo codigo</i> en <see cref="LockedContainer"/> y en
/// <see cref="LockFreeContainer"/>.
/// </para>
/// <para>
/// Es decir, esta tabla es un <b>tercer grupo de control, gratis</b>. Si las columnas con y sin
/// candado no empatan aqui, no hay ninguna explicacion posible en el codigo y la corrida entera
/// queda invalidada. Un control que sale del propio diseño de la matriz, sin escribir un solo
/// benchmark de mas.
/// </para>
/// <para>
/// La rama desechable esta en <see cref="MatrixTransientDisposableBenchmark"/> y no aqui,
/// porque <b>no se puede medir con la misma unidad</b>. Ver alli el porque.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class MatrixTransientPlainBenchmark
{
    private readonly LockedContainer _locked = new();
    private readonly LockFreeContainer _lockFree = new();

    [Benchmark(Baseline = true, Description = "lock | sync | -")]
    public SyncPlain LockedSync() => _locked.TransientSyncPlain();

    [Benchmark(Description = "lock | ValueTask | -")]
    public ValueTask<VtPlain> LockedVt() => _locked.TransientVtPlainAsync();

    [Benchmark(Description = "lock | Task | -")]
    public ValueTask<TaskPlain> LockedTask() => _locked.TransientTaskPlainAsync();

    [Benchmark(Description = "cas | sync | -")]
    public SyncPlain FreeSync() => _lockFree.TransientSyncPlain();

    [Benchmark(Description = "cas | ValueTask | -")]
    public ValueTask<VtPlain> FreeVt() => _lockFree.TransientVtPlainAsync();

    [Benchmark(Description = "cas | Task | -")]
    public ValueTask<TaskPlain> FreeTask() => _lockFree.TransientTaskPlainAsync();
}

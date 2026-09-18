using BenchmarkDotNet.Attributes;

namespace Benchmarks.HandCoded.Scenarios;

/// <summary>
/// Matriz atomica, lifetime <b>scoped</b>: locking x async-kind x disposability.
/// <para>
/// Mismas dieciocho celdas que <see cref="MatrixSingletonBenchmark"/>, y a proposito: lo que
/// interesa es que <b>las dos tablas coincidan</b>. Una vez publicada la instancia, un scoped y
/// un singleton ejecutan el mismo camino caliente; lo unico que cambia es donde vive el campo.
/// Si el scoped saliera sistematicamente mas caro, seria una señal de que el ambito añade algo
/// al camino caliente, y no deberia.
/// </para>
/// <para>
/// El ambito se crea y se calienta una sola vez en <see cref="Setup"/>. <b>Esto separa dos
/// costes que es facil confundir:</b> lo que cuesta <i>resolver</i> desde un ambito, que es lo
/// que mide esta tabla, y lo que cuesta <i>crear y desechar</i> el ambito, que se mide aparte
/// en <see cref="ScopeLifecycleBenchmark"/>. Mezclarlos es el error clasico que hace parecer
/// caro el lifetime scoped cuando lo caro era el ciclo del ambito.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class MatrixScopedBenchmark
{
    private LockedContainer _lockedRoot = null!;
    private LockFreeContainer _lockFreeRoot = null!;
    private LockedScope _locked = null!;
    private LockFreeScope _lockFree = null!;

    [GlobalSetup]
    public void Setup()
    {
        _lockedRoot = new LockedContainer();
        _lockFreeRoot = new LockFreeContainer();
        _locked = _lockedRoot.CreateScope();
        _lockFree = _lockFreeRoot.CreateScope();

        _ = _locked.ScopedSyncPlain;
        _ = _locked.ScopedSyncDisp;
        _ = _locked.ScopedSyncAsyncDisp;
        _locked.GetScopedVtPlainAsync().AsTask().GetAwaiter().GetResult();
        _locked.GetScopedVtDispAsync().AsTask().GetAwaiter().GetResult();
        _locked.GetScopedVtAsyncDispAsync().AsTask().GetAwaiter().GetResult();
        _locked.GetScopedTaskPlainAsync().AsTask().GetAwaiter().GetResult();
        _locked.GetScopedTaskDispAsync().AsTask().GetAwaiter().GetResult();
        _locked.GetScopedTaskAsyncDispAsync().AsTask().GetAwaiter().GetResult();

        _ = _lockFree.ScopedSyncPlain;
        _ = _lockFree.ScopedSyncDisp;
        _ = _lockFree.ScopedSyncAsyncDisp;
        _lockFree.GetScopedVtPlainAsync().AsTask().GetAwaiter().GetResult();
        _lockFree.GetScopedVtDispAsync().AsTask().GetAwaiter().GetResult();
        _lockFree.GetScopedVtAsyncDispAsync().AsTask().GetAwaiter().GetResult();
        _lockFree.GetScopedTaskPlainAsync().AsTask().GetAwaiter().GetResult();
        _lockFree.GetScopedTaskDispAsync().AsTask().GetAwaiter().GetResult();
        _lockFree.GetScopedTaskAsyncDispAsync().AsTask().GetAwaiter().GetResult();
    }

    // ===== con candado =====

    [Benchmark(Baseline = true, Description = "lock | sync | -")]
    public object LockedSyncPlain() => _locked.ScopedSyncPlain;

    [Benchmark(Description = "lock | sync | IDisposable")]
    public object LockedSyncDisp() => _locked.ScopedSyncDisp;

    [Benchmark(Description = "lock | sync | IAsyncDisposable")]
    public object LockedSyncAsyncDisp() => _locked.ScopedSyncAsyncDisp;

    [Benchmark(Description = "lock | ValueTask | -")]
    public ValueTask<VtPlain> LockedVtPlain() => _locked.GetScopedVtPlainAsync();

    [Benchmark(Description = "lock | ValueTask | IDisposable")]
    public ValueTask<VtDisp> LockedVtDisp() => _locked.GetScopedVtDispAsync();

    [Benchmark(Description = "lock | ValueTask | IAsyncDisposable")]
    public ValueTask<VtAsyncDisp> LockedVtAsyncDisp() => _locked.GetScopedVtAsyncDispAsync();

    [Benchmark(Description = "lock | Task | -")]
    public ValueTask<TaskPlain> LockedTaskPlain() => _locked.GetScopedTaskPlainAsync();

    [Benchmark(Description = "lock | Task | IDisposable")]
    public ValueTask<TaskDisp> LockedTaskDisp() => _locked.GetScopedTaskDispAsync();

    [Benchmark(Description = "lock | Task | IAsyncDisposable")]
    public ValueTask<TaskAsyncDisp> LockedTaskAsyncDisp() => _locked.GetScopedTaskAsyncDispAsync();

    // ===== sin candado =====

    [Benchmark(Description = "cas | sync | -")]
    public object FreeSyncPlain() => _lockFree.ScopedSyncPlain;

    [Benchmark(Description = "cas | sync | IDisposable")]
    public object FreeSyncDisp() => _lockFree.ScopedSyncDisp;

    [Benchmark(Description = "cas | sync | IAsyncDisposable")]
    public object FreeSyncAsyncDisp() => _lockFree.ScopedSyncAsyncDisp;

    [Benchmark(Description = "cas | ValueTask | -")]
    public ValueTask<VtPlain> FreeVtPlain() => _lockFree.GetScopedVtPlainAsync();

    [Benchmark(Description = "cas | ValueTask | IDisposable")]
    public ValueTask<VtDisp> FreeVtDisp() => _lockFree.GetScopedVtDispAsync();

    [Benchmark(Description = "cas | ValueTask | IAsyncDisposable")]
    public ValueTask<VtAsyncDisp> FreeVtAsyncDisp() => _lockFree.GetScopedVtAsyncDispAsync();

    [Benchmark(Description = "cas | Task | -")]
    public ValueTask<TaskPlain> FreeTaskPlain() => _lockFree.GetScopedTaskPlainAsync();

    [Benchmark(Description = "cas | Task | IDisposable")]
    public ValueTask<TaskDisp> FreeTaskDisp() => _lockFree.GetScopedTaskDispAsync();

    [Benchmark(Description = "cas | Task | IAsyncDisposable")]
    public ValueTask<TaskAsyncDisp> FreeTaskAsyncDisp() => _lockFree.GetScopedTaskAsyncDispAsync();
}

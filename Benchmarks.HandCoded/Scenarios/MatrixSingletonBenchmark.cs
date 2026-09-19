using BenchmarkDotNet.Attributes;

namespace Benchmarks.HandCoded.Scenarios;

/// <summary>
/// Matriz atomica, lifetime <b>singleton</b>: locking x async-kind x disposability.
/// <para>
/// Dieciocho celdas. Lo que se mide es el <b>camino caliente</b>: el contenedor se crea y se
/// calienta en <see cref="Setup"/>, asi que cada invocacion encuentra el servicio ya publicado.
/// Es lo que corresponde a un singleton, que por definicion se construye una vez en la vida del
/// proceso y se lee millones de veces.
/// </para>
/// <para>
/// <b>Lo que esta tabla debe demostrar es un empate.</b> Con el servicio ya publicado, las dos
/// estrategias de locking ejecutan literalmente el mismo codigo: una lectura volatil, una
/// prueba de nulo y un salto. Si las columnas con y sin candado no empatan, la conclusion no es
/// que una sea mejor, sino que la corrida esta mal medida. Es un segundo grupo de control
/// escondido dentro de la propia matriz.
/// </para>
/// <para>
/// La disposability tampoco deberia mover nada aqui: desechar ocurre al final de la vida del
/// contenedor, no en la resolucion. Si moviera, seria por el tamaño del objeto o por su
/// alineacion, no por la capacidad de desecharse.
/// </para>
/// <para>
/// El unico eje con derecho a moverse es async-kind, y solo para enseñar si el camino rapido
/// evita de verdad la maquina de estados cuando el valor ya esta disponible.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class MatrixSingletonBenchmark
{
    private LockedContainer _locked = null!;
    private LockFreeContainer _lockFree = null!;

    [GlobalSetup]
    public void Setup()
    {
        _locked = new LockedContainer();
        _lockFree = new LockFreeContainer();

        // Calentamiento explicito: se fuerza la publicacion de las nueve celdas de cada
        // estrategia para que ninguna invocacion medida caiga en el camino frio.
        _ = _locked.SingletonSyncPlain;
        _ = _locked.SingletonSyncDisp;
        _ = _locked.SingletonSyncAsyncDisp;
        _locked.GetSingletonVtPlainAsync().AsTask().GetAwaiter().GetResult();
        _locked.GetSingletonVtDispAsync().AsTask().GetAwaiter().GetResult();
        _locked.GetSingletonVtAsyncDispAsync().AsTask().GetAwaiter().GetResult();
        _locked.GetSingletonTaskPlainAsync().AsTask().GetAwaiter().GetResult();
        _locked.GetSingletonTaskDispAsync().AsTask().GetAwaiter().GetResult();
        _locked.GetSingletonTaskAsyncDispAsync().AsTask().GetAwaiter().GetResult();

        _ = _lockFree.SingletonSyncPlain;
        _ = _lockFree.SingletonSyncDisp;
        _ = _lockFree.SingletonSyncAsyncDisp;
        _lockFree.GetSingletonVtPlainAsync().AsTask().GetAwaiter().GetResult();
        _lockFree.GetSingletonVtDispAsync().AsTask().GetAwaiter().GetResult();
        _lockFree.GetSingletonVtAsyncDispAsync().AsTask().GetAwaiter().GetResult();
        _lockFree.GetSingletonTaskPlainAsync().AsTask().GetAwaiter().GetResult();
        _lockFree.GetSingletonTaskDispAsync().AsTask().GetAwaiter().GetResult();
        _lockFree.GetSingletonTaskAsyncDispAsync().AsTask().GetAwaiter().GetResult();
    }

    // ===== con candado =====

    [Benchmark(Baseline = true, Description = "lock | sync | -")]
    public object LockedSyncPlain() => _locked.SingletonSyncPlain;

    [Benchmark(Description = "lock | sync | IDisposable")]
    public object LockedSyncDisp() => _locked.SingletonSyncDisp;

    [Benchmark(Description = "lock | sync | IAsyncDisposable")]
    public object LockedSyncAsyncDisp() => _locked.SingletonSyncAsyncDisp;

    [Benchmark(Description = "lock | ValueTask | -")]
    public ValueTask<VtPlain> LockedVtPlain() => _locked.GetSingletonVtPlainAsync();

    [Benchmark(Description = "lock | ValueTask | IDisposable")]
    public ValueTask<VtDisp> LockedVtDisp() => _locked.GetSingletonVtDispAsync();

    [Benchmark(Description = "lock | ValueTask | IAsyncDisposable")]
    public ValueTask<VtAsyncDisp> LockedVtAsyncDisp() => _locked.GetSingletonVtAsyncDispAsync();

    [Benchmark(Description = "lock | Task | -")]
    public ValueTask<TaskPlain> LockedTaskPlain() => _locked.GetSingletonTaskPlainAsync();

    [Benchmark(Description = "lock | Task | IDisposable")]
    public ValueTask<TaskDisp> LockedTaskDisp() => _locked.GetSingletonTaskDispAsync();

    [Benchmark(Description = "lock | Task | IAsyncDisposable")]
    public ValueTask<TaskAsyncDisp> LockedTaskAsyncDisp() => _locked.GetSingletonTaskAsyncDispAsync();

    // ===== sin candado =====

    [Benchmark(Description = "cas | sync | -")]
    public object FreeSyncPlain() => _lockFree.SingletonSyncPlain;

    [Benchmark(Description = "cas | sync | IDisposable")]
    public object FreeSyncDisp() => _lockFree.SingletonSyncDisp;

    [Benchmark(Description = "cas | sync | IAsyncDisposable")]
    public object FreeSyncAsyncDisp() => _lockFree.SingletonSyncAsyncDisp;

    [Benchmark(Description = "cas | ValueTask | -")]
    public ValueTask<VtPlain> FreeVtPlain() => _lockFree.GetSingletonVtPlainAsync();

    [Benchmark(Description = "cas | ValueTask | IDisposable")]
    public ValueTask<VtDisp> FreeVtDisp() => _lockFree.GetSingletonVtDispAsync();

    [Benchmark(Description = "cas | ValueTask | IAsyncDisposable")]
    public ValueTask<VtAsyncDisp> FreeVtAsyncDisp() => _lockFree.GetSingletonVtAsyncDispAsync();

    [Benchmark(Description = "cas | Task | -")]
    public ValueTask<TaskPlain> FreeTaskPlain() => _lockFree.GetSingletonTaskPlainAsync();

    [Benchmark(Description = "cas | Task | IDisposable")]
    public ValueTask<TaskDisp> FreeTaskDisp() => _lockFree.GetSingletonTaskDispAsync();

    [Benchmark(Description = "cas | Task | IAsyncDisposable")]
    public ValueTask<TaskAsyncDisp> FreeTaskAsyncDisp() => _lockFree.GetSingletonTaskAsyncDispAsync();
}

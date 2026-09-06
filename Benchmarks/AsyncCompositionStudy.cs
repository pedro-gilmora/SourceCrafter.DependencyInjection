using System.Runtime.CompilerServices;

using BenchmarkDotNet.Attributes;

namespace Benchmarks;

// ---------------------------------------------------------------------------
// Estudio: como componer N tareas ya arrancadas.
//
// El generador usa hoy DOS formas distintas para el mismo problema:
//
//   InterceptorCall4  ->  arrancar todas, luego [await __t0, await __t1]
//   ResolveCoreAsync  ->  arrancar todas, luego await Task.WhenAll(...) + .Result
//
// La medicion previa fue un bucle con Stopwatch, que no separa el coste real del
// ruido de alineacion ni del contexto de asignacion del GC. Este banco la repite
// con el arnes endurecido (3 lanzamientos, memoria aleatorizada) para decidir con
// datos si WhenAll paga lo que cuesta.
//
// WhenAll aporta una cosa que las otras formas no: observa TODAS las tareas antes
// de lanzar. Con awaits secuenciales, si fallan dos, la segunda queda sin observar.
// La pregunta es cuanto cuesta esa garantia en el camino que se recorre siempre.
// ---------------------------------------------------------------------------

public sealed class Composed(int a, int b)
{
    public int A { get; } = a;
    public int B { get; } = b;
}

public static class AsyncShapes
{
    // ---- Forma de coleccion (InterceptorCall4): N servicios independientes ----

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<int[]> ArraySequential(Task<int> t0, Task<int> t1)
        => [await t0, await t1];

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<int[]> ArrayWhenAll(Task<int> t0, Task<int> t1)
    {
        await Task.WhenAll(t0, t1);
        return [t0.Result, t1.Result];
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<int[]> ArraySequentialObserving(Task<int> t0, Task<int> t1)
    {
        try
        {
            return [await t0, await t1];
        }
        catch
        {
            _ = t0.Exception;
            _ = t1.Exception;
            throw;
        }
    }

    // ---- Forma de constructor (ResolveCoreAsync): N parametros ----

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<Composed> CtorWhenAll(Task<int> t0, Task<int> t1)
    {
        await Task.WhenAll(t0, t1);
        return new Composed(t0.Result, t1.Result);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<Composed> CtorInlineAwaits(Task<int> t0, Task<int> t1)
        => new(await t0.ConfigureAwait(false), await t1.ConfigureAwait(false));

    /// <summary>
    /// Deduplicado: solo se espera la tarea que hace falta; la otra ya quedo completada
    /// por el subarbol de la primera, asi que se lee su <c>.Result</c> sin suspenderse.
    /// Es la forma que ya emite <c>GetEmployeeControllerAsync</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<Composed> CtorInlineAwaitsDeduped(Task<int> t0, Task<int> t1)
        => new(await t0.ConfigureAwait(false), t1.Result);
}

/// <summary>
/// Tareas <b>ya completadas</b>: es el estado normal de un contenedor DI tras la primera
/// resolucion, porque el valor se cachea. Es el camino que se recorre casi siempre.
/// </summary>
[MemoryDiagnoser]
public class AsyncCompositionCompletedBenchmark
{
    private Task<int> _t0 = null!;
    private Task<int> _t1 = null!;

    [GlobalSetup]
    public void Setup()
    {
        _t0 = Task.FromResult(1);
        _t1 = Task.FromResult(2);
    }

    [Benchmark(Baseline = true, Description = "array: [await t0, await t1]")]
    public Task<int[]> ArraySequential() => AsyncShapes.ArraySequential(_t0, _t1);

    [Benchmark(Description = "array: seq + observe on catch")]
    public Task<int[]> ArraySequentialObserving() => AsyncShapes.ArraySequentialObserving(_t0, _t1);

    [Benchmark(Description = "array: WhenAll + .Result")]
    public Task<int[]> ArrayWhenAll() => AsyncShapes.ArrayWhenAll(_t0, _t1);

    [Benchmark(Description = "ctor: new(await t0, await t1)")]
    public Task<Composed> CtorInlineAwaits() => AsyncShapes.CtorInlineAwaits(_t0, _t1);

    [Benchmark(Description = "ctor: new(await t0, t1.Result)")]
    public Task<Composed> CtorInlineAwaitsDeduped() => AsyncShapes.CtorInlineAwaitsDeduped(_t0, _t1);

    [Benchmark(Description = "ctor: WhenAll + .Result")]
    public Task<Composed> CtorWhenAll() => AsyncShapes.CtorWhenAll(_t0, _t1);
}

/// <summary>
/// Tareas <b>pendientes</b> al invocar la forma, completadas justo despues desde el mismo
/// hilo. Mide la suspension real del state machine sin el ruido de planificar en el pool
/// de hilos, que en una medicion ingenua se come la diferencia entre formas.
/// </summary>
[MemoryDiagnoser]
public class AsyncCompositionPendingBenchmark
{
    [Benchmark(Baseline = true, Description = "array: [await t0, await t1]")]
    public async Task<int[]> ArraySequential()
    {
        var (a, b) = Pair();
        var t = AsyncShapes.ArraySequential(a.Task, b.Task);
        a.SetResult(1);
        b.SetResult(2);
        return await t;
    }

    [Benchmark(Description = "array: seq + observe on catch")]
    public async Task<int[]> ArraySequentialObserving()
    {
        var (a, b) = Pair();
        var t = AsyncShapes.ArraySequentialObserving(a.Task, b.Task);
        a.SetResult(1);
        b.SetResult(2);
        return await t;
    }

    [Benchmark(Description = "array: WhenAll + .Result")]
    public async Task<int[]> ArrayWhenAll()
    {
        var (a, b) = Pair();
        var t = AsyncShapes.ArrayWhenAll(a.Task, b.Task);
        a.SetResult(1);
        b.SetResult(2);
        return await t;
    }

    [Benchmark(Description = "ctor: new(await t0, await t1)")]
    public async Task<Composed> CtorInlineAwaits()
    {
        var (a, b) = Pair();
        var t = AsyncShapes.CtorInlineAwaits(a.Task, b.Task);
        a.SetResult(1);
        b.SetResult(2);
        return await t;
    }

    [Benchmark(Description = "ctor: WhenAll + .Result")]
    public async Task<Composed> CtorWhenAll()
    {
        var (a, b) = Pair();
        var t = AsyncShapes.CtorWhenAll(a.Task, b.Task);
        a.SetResult(1);
        b.SetResult(2);
        return await t;
    }

    private static (TaskCompletionSource<int>, TaskCompletionSource<int>) Pair() => (new(), new());
}

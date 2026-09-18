using BenchmarkDotNet.Attributes;

namespace Benchmarks.HandCoded.Scenarios;

/// <summary>
/// Matriz atomica, lifetime <b>transient</b>, rama <b>desechable</b>: locking x async-kind x
/// disposability. Doce celdas.
/// <para>
/// <b>La unidad de medida de esta tabla NO es una resolucion. Es un ciclo completo:</b> crear
/// contenedor, resolver una vez, desechar. Y no es una preferencia, es una imposibilidad.
/// </para>
/// <para>
/// Un transitorio desechable obliga al contenedor a <i>recordar</i> cada instancia que entrega,
/// porque es el unico que puede desecharlas. Esa lista crece con cada resolucion y no se vacia
/// nunca hasta el final de la vida del contenedor. Medirlo como resolucion repetida no daria el
/// coste de resolver: daria el coste de una lista de varios millones de elementos creciendo
/// durante la corrida, con sus redimensionados y su presion sobre el GC. La cifra dependeria
/// del numero de iteraciones que BenchmarkDotNet decida hacer, que es justo lo que una medida
/// no puede permitirse.
/// </para>
/// <para>
/// <b>El coste acumulativo no es un defecto de esta implementacion: es la semantica.</b> Es la
/// razon por la que un contenedor que no rastrea sus transitorios desechables es mas rapido
/// <i>y</i> pierde recursos, y por la que varios contenedores de la competencia eligen no
/// rastrearlos. No hay una tercera opcion.
/// </para>
/// <para>
/// El ciclo completo si es una unidad honesta, acotada y comparable: es exactamente lo que hace
/// una peticion web. Lo que no se puede hacer es compararla con las cifras de
/// <see cref="MatrixTransientPlainBenchmark"/>, que miden otra cosa. Las dos tablas van
/// separadas precisamente para que nadie las lea como una sola.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class MatrixTransientDisposableBenchmark
{
    // ===== con candado, IDisposable =====

    [Benchmark(Baseline = true, Description = "lock | sync | IDisposable")]
    public void LockedSyncDisp()
    {
        var c = new LockedContainer();
        _ = c.TransientSyncDisp();
        c.Dispose();
    }

    [Benchmark(Description = "lock | ValueTask | IDisposable")]
    public async Task LockedVtDisp()
    {
        var c = new LockedContainer();
        _ = await c.TransientVtDispAsync();
        c.Dispose();
    }

    [Benchmark(Description = "lock | Task | IDisposable")]
    public async Task LockedTaskDisp()
    {
        var c = new LockedContainer();
        _ = await c.TransientTaskDispAsync();
        c.Dispose();
    }

    // ===== con candado, IAsyncDisposable =====

    [Benchmark(Description = "lock | sync | IAsyncDisposable")]
    public async Task LockedSyncAsyncDisp()
    {
        var c = new LockedContainer();
        _ = c.TransientSyncAsyncDisp();
        await c.DisposeAsync();
    }

    [Benchmark(Description = "lock | ValueTask | IAsyncDisposable")]
    public async Task LockedVtAsyncDisp()
    {
        var c = new LockedContainer();
        _ = await c.TransientVtAsyncDispAsync();
        await c.DisposeAsync();
    }

    [Benchmark(Description = "lock | Task | IAsyncDisposable")]
    public async Task LockedTaskAsyncDisp()
    {
        var c = new LockedContainer();
        _ = await c.TransientTaskAsyncDispAsync();
        await c.DisposeAsync();
    }

    // ===== sin candado, IDisposable =====

    [Benchmark(Description = "cas | sync | IDisposable")]
    public void FreeSyncDisp()
    {
        var c = new LockFreeContainer();
        _ = c.TransientSyncDisp();
        c.Dispose();
    }

    [Benchmark(Description = "cas | ValueTask | IDisposable")]
    public async Task FreeVtDisp()
    {
        var c = new LockFreeContainer();
        _ = await c.TransientVtDispAsync();
        c.Dispose();
    }

    [Benchmark(Description = "cas | Task | IDisposable")]
    public async Task FreeTaskDisp()
    {
        var c = new LockFreeContainer();
        _ = await c.TransientTaskDispAsync();
        c.Dispose();
    }

    // ===== sin candado, IAsyncDisposable =====

    [Benchmark(Description = "cas | sync | IAsyncDisposable")]
    public async Task FreeSyncAsyncDisp()
    {
        var c = new LockFreeContainer();
        _ = c.TransientSyncAsyncDisp();
        await c.DisposeAsync();
    }

    [Benchmark(Description = "cas | ValueTask | IAsyncDisposable")]
    public async Task FreeVtAsyncDisp()
    {
        var c = new LockFreeContainer();
        _ = await c.TransientVtAsyncDispAsync();
        await c.DisposeAsync();
    }

    [Benchmark(Description = "cas | Task | IAsyncDisposable")]
    public async Task FreeTaskAsyncDisp()
    {
        var c = new LockFreeContainer();
        _ = await c.TransientTaskAsyncDispAsync();
        await c.DisposeAsync();
    }
}

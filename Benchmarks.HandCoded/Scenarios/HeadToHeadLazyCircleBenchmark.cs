using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

using Benchmarks.HandCoded.Rivals;

namespace Benchmarks.HandCoded.Scenarios;

/// <summary>
/// Head-to-head: <b>CircleDI perezoso</b>. La tabla que faltaba.
/// <para>
/// El resto del banco medía a CircleDI solo en su modo por defecto, que es <i>eager</i>: construye
/// en el constructor y deja campos de solo lectura, asi que resolver es leer un campo y no
/// sincroniza nada. Comparar eso contra un contenedor perezoso no compara dos implementaciones,
/// compara dos contratos, y por eso <see cref="HeadToHeadScopeBenchmark"/> los separa en grupos.
/// </para>
/// <para>
/// <b>Esta tabla cierra el hueco:</b> pone a CircleDI en <c>CreationTime = CreationTiming.Lazy</c>,
/// que es el mismo contrato que cumplen SourceCrafter, Jab y Pure.DI, y lo enfrenta a SourceCrafter
/// directamente. Se ha verificado sobre el codigo generado que en este modo CircleDI emite el doble
/// chequeo clasico -- lectura no volatil fuera, <c>lock (_lock)</c> sobre un candado dedicado,
/// segunda prueba de nulo dentro -- por servicio perezoso.
/// </para>
/// <para>
/// <b>Lo que esta tabla puede y no puede decidir.</b> Si CircleDI perezoso se acerca a las cifras de
/// SourceCrafter, la brecha del banco principal era una diferencia de <i>contrato</i> y no de
/// calidad de generacion. Si aun asi queda por delante, la diferencia es de implementacion y hay
/// margen real que perseguir. Cualquiera de los dos resultados es util; lo que no era util era no
/// haber medido nunca este eje.
/// </para>
/// <para>
/// Las dos filas de cada grupo llevan los <b>mismos tres servicios</b> y desechan de forma
/// asincrona, por la misma razon que el resto del banco: el contenedor de SourceCrafter, al
/// registrarsele un <see cref="IAsyncDisposable"/>, deja de implementar <see cref="IDisposable"/> y
/// no tiene un <c>Dispose()</c> sincrono que medir.
/// </para>
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class HeadToHeadLazyCircleBenchmark
{
    private CircleLazyScopedContainer _circleLazy = null!;
    private CircleScopedContainer _circleEager = null!;
    private ScScopedContainer _sc = null!;

    private CircleLazySingletonContainer _circleLazySingleton = null!;
    private CircleSingletonContainer _circleEagerSingleton = null!;
    private ScSingletonContainer _scSingleton = null!;

    [GlobalSetup]
    public void Setup()
    {
        _circleLazy = new CircleLazyScopedContainer();
        _circleEager = new CircleScopedContainer();
        _sc = new ScScopedContainer();

        _circleLazySingleton = new CircleLazySingletonContainer();
        _circleEagerSingleton = new CircleSingletonContainer();
        _scSingleton = new ScSingletonContainer();

        // El camino caliente de un singleton solo tiene sentido ya calentado: la primera
        // resolucion de un perezoso paga el camino frio una unica vez en toda la vida del
        // proceso, y medirla mezclada con las siguientes no describe ningun regimen real.
        _ = _circleLazySingleton.SyncDisp;
        _ = _circleEagerSingleton.SyncDisp;
        _ = _scSingleton.SyncDisp;
    }

    // ===== camino caliente de singleton: los tres ya calentados =====

    [BenchmarkCategory("singleton caliente")]
    [Benchmark(Baseline = true, Description = "CircleDI eager")]
    public SyncDisp HotCircleEager() => _circleEagerSingleton.SyncDisp;

    [BenchmarkCategory("singleton caliente")]
    [Benchmark(Description = "CircleDI lazy")]
    public SyncDisp HotCircleLazy() => _circleLazySingleton.SyncDisp;

    [BenchmarkCategory("singleton caliente")]
    [Benchmark(Description = "SourceCrafter")]
    public SyncDisp HotSourceCrafter() => _scSingleton.SyncDisp;

    // ===== ambito vacio: crear y desechar sin resolver nada =====

    [BenchmarkCategory("ambito vacio")]
    [Benchmark(Baseline = true, Description = "CircleDI eager")]
    public async Task EmptyCircleEager()
    {
        var scope = _circleEager.CreateScope();
        await scope.DisposeAsync();
    }

    [BenchmarkCategory("ambito vacio")]
    [Benchmark(Description = "CircleDI lazy")]
    public async Task EmptyCircleLazy()
    {
        var scope = _circleLazy.CreateScope();
        await scope.DisposeAsync();
    }

    [BenchmarkCategory("ambito vacio")]
    [Benchmark(Description = "SourceCrafter")]
    public async Task EmptySourceCrafter()
    {
        var scope = _sc.CreateScope();
        await scope.DisposeAsync();
    }

    // ===== ciclo completo: crear, resolver un scoped y desechar =====
    //
    // Este es el grupo decisivo. Cada iteracion abre un ambito nuevo, asi que la resolucion cae
    // siempre en el camino frio: es exactamente donde el perezoso paga su candado y donde el eager
    // no tiene nada que pagar porque ya construyo en el constructor.

    [BenchmarkCategory("ciclo completo")]
    [Benchmark(Baseline = true, Description = "CircleDI eager")]
    public async Task FullCircleEager()
    {
        var scope = _circleEager.CreateScope();
        _ = scope.SyncDisp;
        await scope.DisposeAsync();
    }

    [BenchmarkCategory("ciclo completo")]
    [Benchmark(Description = "CircleDI lazy")]
    public async Task FullCircleLazy()
    {
        var scope = _circleLazy.CreateScope();
        _ = scope.SyncDisp;
        await scope.DisposeAsync();
    }

    [BenchmarkCategory("ciclo completo")]
    [Benchmark(Description = "SourceCrafter")]
    public async Task FullSourceCrafter()
    {
        var scope = _sc.CreateScope();
        _ = scope.SyncDisp;
        await scope.DisposeAsync();
    }
}

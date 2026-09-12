using BenchmarkDotNet.Attributes;

using Benchmarks.HandCoded.Rivals;

namespace Benchmarks.HandCoded.Scenarios;

/// <summary>
/// Head-to-head: <b>ciclo de vida del ambito</b>. Es el escenario con margen real.
/// <para>
/// Mientras el camino caliente de un singleton tiene un suelo duro que todos alcanzan, el ciclo
/// del ambito no lo tiene: lo que cuesta es lo que el ambito <b>asigne</b>, y eso son tres
/// decisiones de diseño evitables -- el objeto del ambito, su candado si lo tiene, y su lista de
/// desecho si la tiene. Aqui es donde una tabla discrimina de verdad.
/// </para>
/// <para>
/// <b>Se desecha de forma asincrona en todas las filas, a proposito.</b> No es la forma mas
/// barata; es la unica <i>uniforme</i>. El contenedor de SourceCrafter, al registrarse un servicio
/// <see cref="IAsyncDisposable"/>, deja de implementar <see cref="IDisposable"/> por completo, asi
/// que no tiene un <c>Dispose()</c> que medir. Darle a cada fila la forma de desecho que mas le
/// conviene mediria seis cosas distintas; usar la unica que todos tienen mide una.
/// </para>
/// <para>
/// <b>Las filas escritas a mano son la variante lean y la eager, ambas con exactamente los tres
/// servicios que registran los rivales.</b> La primera version de esta tabla enfrentaba los
/// contenedores de la matriz completa, que llevan nueve campos de servicio, contra los tres de Jab,
/// CircleDI, Pure.DI y SourceCrafter: su ambito pesaba 144 B frente a 48 B y perdia, pero perdia
/// por llevar tres veces mas servicios, no por estar peor escrito. Comparar tamaños de objeto
/// exige que los objetos contengan lo mismo.
/// </para>
/// <para>
/// El ambito vacio no es un caso de laboratorio: es el <b>mayoritario</b>. Una peticion servida
/// desde cache, un chequeo de salud o una ruta estatica abren su ambito y no resuelven nada
/// scoped. Un contenedor que asigna el candado del ambito por adelantado paga en todas ellas por
/// un servicio perezoso que nadie pide.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class HeadToHeadScopeBenchmark
{
    private LeanLazyContainer _lean = null!;
    private EagerContainer _eager = null!;
    private JabScopedContainer _jab = null!;
    private CircleScopedContainer _circle = null!;
    private PureDiScopedContainer _pureDi = null!;
    private ScScopedContainer _sc = null!;

    [GlobalSetup]
    public void Setup()
    {
        _lean = new LeanLazyContainer();
        _eager = new EagerContainer();
        _jab = new JabScopedContainer();
        _circle = new CircleScopedContainer();
        _pureDi = new PureDiScopedContainer();
        _sc = new ScScopedContainer();
    }

    // ===== ambito vacio: abrir y cerrar sin resolver nada =====

    [Benchmark(Baseline = true, Description = "vacio | Hand-coded lazy")]
    public async Task EmptyLean()
    {
        var scope = _lean.CreateScope();
        await scope.DisposeAsync();
    }

    [Benchmark(Description = "vacio | Hand-coded eager")]
    public async Task EmptyEager()
    {
        var scope = _eager.CreateScope();
        await scope.DisposeAsync();
    }

    [Benchmark(Description = "vacio | SourceCrafter")]
    public async Task EmptySourceCrafter()
    {
        var scope = _sc.CreateScope();
        await scope.DisposeAsync();
    }

    [Benchmark(Description = "vacio | CircleDI")]
    public async Task EmptyCircleDi()
    {
        var scope = _circle.CreateScope();
        await scope.DisposeAsync();
    }

    [Benchmark(Description = "vacio | Pure.DI")]
    public async Task EmptyPureDi()
    {
        var scope = new PureDiScopedContainer(_pureDi);
        await scope.DisposeAsync();
    }

    [Benchmark(Description = "vacio | Jab")]
    public async Task EmptyJab()
    {
        var scope = _jab.CreateScope();
        await scope.DisposeAsync();
    }

    // ===== ciclo completo: abrir, resolver un scoped desechable, cerrar =====

    [Benchmark(Description = "completo | Hand-coded lazy")]
    public async Task FullLean()
    {
        var scope = _lean.CreateScope();
        _ = scope.SyncDisp;
        await scope.DisposeAsync();
    }

    [Benchmark(Description = "completo | Hand-coded eager")]
    public async Task FullEager()
    {
        var scope = _eager.CreateScope();
        _ = scope.ScopedSyncDisp;
        await scope.DisposeAsync();
    }

    [Benchmark(Description = "completo | SourceCrafter")]
    public async Task FullSourceCrafter()
    {
        var scope = _sc.CreateScope();
        _ = scope.SyncDisp;
        await scope.DisposeAsync();
    }

    [Benchmark(Description = "completo | CircleDI")]
    public async Task FullCircleDi()
    {
        var scope = _circle.CreateScope();
        _ = scope.SyncDisp;
        await scope.DisposeAsync();
    }

    [Benchmark(Description = "completo | Pure.DI")]
    public async Task FullPureDi()
    {
        var scope = new PureDiScopedContainer(_pureDi);
        _ = scope.Disp;
        await scope.DisposeAsync();
    }

    [Benchmark(Description = "completo | Jab")]
    public async Task FullJab()
    {
        var scope = _jab.CreateScope();
        _ = scope.GetService<SyncDisp>();
        await scope.DisposeAsync();
    }
}

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

using Benchmarks.HandCoded.Rivals;

namespace Benchmarks.HandCoded.Scenarios;

/// <summary>
/// Head-to-head: <b>ciclo de vida del ambito</b>. Es el escenario con margen real.
/// <para>
/// Mientras el camino caliente de un singleton tiene un suelo duro que todos alcanzan, el ciclo del
/// ambito no lo tiene: lo que cuesta es lo que el ambito <b>asigne</b>, y eso son tres decisiones de
/// diseño evitables -- el objeto del ambito, su candado si lo tiene, y su lista de desecho si la
/// tiene.
/// </para>
/// <para>
/// <b>La tabla esta partida en dos grupos que no se comparan entre si, y esto es lo mas importante
/// de todo el fichero.</b>
/// </para>
/// <para>
/// <b>CircleDI no resuelve perezosamente por defecto.</b> Construye todo en el constructor y lo deja
/// en campos de solo lectura: en esta configuracion es seguro entre hilos por inmutabilidad, sin
/// sincronizar al resolver. Enfrentarlo a un contenedor perezoso no compara dos implementaciones,
/// compara <i>dos semanticas</i>, y el perezoso pierde por algo que no es un defecto suyo -- la
/// primera resolucion de cada ambito cae por fuerza en un camino frio con exclusion mutua. Una tabla
/// que mezcle los dos grupos convierte una diferencia de contrato en una diferencia de calidad, que
/// es exactamente la clase de conclusion que este banco existe para no publicar.
/// </para>
/// <para>
/// <b>Lo que NO se debe concluir de lo anterior es que CircleDI carezca de candados.</b> Con
/// <c>CreationTiming.Lazy</c> emite el mismo doble chequeo con <c>lock</c> que este banco escribe a
/// mano, y bloquea para rastrear transitorios desechables incluso en modo eager. La ventaja que se
/// mide aqui viene de no ser perezoso, no de no sincronizar.
/// </para>
/// <para>
/// Asi que hay dos comparaciones, cada una con su propio baseline:
/// <list type="bullet">
///   <item><b>eager</b>: la variante eager escrita a mano frente a CircleDI. Ninguno de los dos
///   sincroniza al resolver. Es la unica comparacion honesta para CircleDI.</item>
///   <item><b>lazy</b>: la variante lean perezosa frente a Jab, Pure.DI y SourceCrafter, que son los
///   tres perezosos. Aqui si se comparan implementaciones del mismo contrato.</item>
/// </list>
/// </para>
/// <para>
/// Todas las filas llevan los <b>mismos tres servicios</b>. Y todas desechan de forma asincrona, que
/// no es la forma mas barata sino la unica <i>uniforme</i>: el contenedor de SourceCrafter, al
/// registrarsele un <see cref="IAsyncDisposable"/>, deja de implementar <see cref="IDisposable"/> por
/// completo y no tiene un <c>Dispose()</c> que medir.
/// </para>
/// <para>
/// El ambito vacio no es un caso de laboratorio: es el <b>mayoritario</b>. Una peticion servida desde
/// cache, un chequeo de salud o una ruta estatica abren su ambito y no resuelven nada scoped.
/// </para>
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
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

    // ===== ambito vacio, eager: hand-coded eager vs CircleDI =====

    [BenchmarkCategory("vacio | eager")]
    [Benchmark(Baseline = true, Description = "Hand-coded eager")]
    public async Task EmptyEager()
    {
        var scope = _eager.CreateScope();
        await scope.DisposeAsync();
    }

    [BenchmarkCategory("vacio | eager")]
    [Benchmark(Description = "CircleDI")]
    public async Task EmptyCircleDi()
    {
        var scope = _circle.CreateScope();
        await scope.DisposeAsync();
    }

    // ===== ambito vacio, perezosos =====

    [BenchmarkCategory("vacio | perezosos")]
    [Benchmark(Baseline = true, Description = "Hand-coded lazy")]
    public async Task EmptyLean()
    {
        var scope = _lean.CreateScope();
        await scope.DisposeAsync();
    }

    [BenchmarkCategory("vacio | perezosos")]
    [Benchmark(Description = "SourceCrafter")]
    public async Task EmptySourceCrafter()
    {
        var scope = _sc.CreateScope();
        await scope.DisposeAsync();
    }

    [BenchmarkCategory("vacio | perezosos")]
    [Benchmark(Description = "Pure.DI")]
    public async Task EmptyPureDi()
    {
        var scope = new PureDiScopedContainer(_pureDi);
        await scope.DisposeAsync();
    }

    [BenchmarkCategory("vacio | perezosos")]
    [Benchmark(Description = "Jab")]
    public async Task EmptyJab()
    {
        var scope = _jab.CreateScope();
        await scope.DisposeAsync();
    }

    // ===== ciclo completo, eager =====

    [BenchmarkCategory("completo | eager")]
    [Benchmark(Baseline = true, Description = "Hand-coded eager")]
    public async Task FullEager()
    {
        var scope = _eager.CreateScope();
        _ = scope.ScopedSyncDisp;
        await scope.DisposeAsync();
    }

    [BenchmarkCategory("completo | eager")]
    [Benchmark(Description = "CircleDI")]
    public async Task FullCircleDi()
    {
        var scope = _circle.CreateScope();
        _ = scope.SyncDisp;
        await scope.DisposeAsync();
    }

    // ===== ciclo completo, perezosos =====

    [BenchmarkCategory("completo | perezosos")]
    [Benchmark(Baseline = true, Description = "Hand-coded lazy")]
    public async Task FullLean()
    {
        var scope = _lean.CreateScope();
        _ = scope.SyncDisp;
        await scope.DisposeAsync();
    }

    [BenchmarkCategory("completo | perezosos")]
    [Benchmark(Description = "SourceCrafter")]
    public async Task FullSourceCrafter()
    {
        var scope = _sc.CreateScope();
        _ = scope.SyncDisp;
        await scope.DisposeAsync();
    }

    [BenchmarkCategory("completo | perezosos")]
    [Benchmark(Description = "Pure.DI")]
    public async Task FullPureDi()
    {
        var scope = new PureDiScopedContainer(_pureDi);
        _ = scope.Disp;
        await scope.DisposeAsync();
    }

    [BenchmarkCategory("completo | perezosos")]
    [Benchmark(Description = "Jab")]
    public async Task FullJab()
    {
        var scope = _jab.CreateScope();
        _ = scope.GetService<SyncDisp>();
        await scope.DisposeAsync();
    }
}

using BenchmarkDotNet.Attributes;

namespace Benchmarks.HandCoded.Scenarios;

/// <summary>
/// Eje aislado: <b>ciclo de vida del ambito</b>, separado de la resolucion.
/// <para>
/// <see cref="MatrixScopedBenchmark"/> mide lo que cuesta <i>resolver</i> desde un ambito ya
/// creado. Esta tabla mide lo otro: lo que cuesta <b>crear y desechar el ambito</b>. Estan
/// separadas porque mezclarlas es el error clasico que hace parecer caro el lifetime scoped
/// cuando lo caro era el ciclo.
/// </para>
/// <para>
/// <b>Todos los metodos devuelven el ambito, y no es decoracion.</b> La primera version de esta
/// tabla no lo hacia y midio <c>0,0097 ns con cero asignacion</c> para el ambito vacio: el
/// analisis de escape de .NET 10 vio que el ambito no salia del metodo y que desecharlo no tenia
/// efecto observable, asi que <b>elimino el escenario entero</b>. Una centesima de nanosegundo es
/// la trescentesima parte de un ciclo de reloj; no es un ambito rapido, es un ambito que no
/// existio.
/// </para>
/// <para>
/// Es la trampa mas peligrosa de este banco porque <i>parece un exito</i>: la fila sale imbatible
/// y la columna de asignacion, que normalmente es la de fiar, tambien marca cero. Devolver el
/// objeto lo obliga a escapar y a asignarse de verdad. La regla que queda: <b>en .NET 10, un
/// benchmark cuyo resultado no se consume no mide nada</b>, y hay que sospechar de cualquier cifra
/// por debajo de la decima de nanosegundo.
/// </para>
/// <para>
/// Dicho lo cual, el efecto es real y aprovechable: si un ambito no escapa del metodo que lo abre,
/// el JIT puede colocarlo en la pila y ahorrarse la asignacion entera. Eso premia a los ambitos
/// pequeños y sin lista de desecho, que son justo los que este banco defiende.
/// </para>
/// <para>
/// Las filas marcadas <b>9 servicios</b> llevan la matriz completa (nueve campos de servicio mas
/// los auxiliares) frente a los tres de las otras. Se incluyen para enseñar cuanto cuesta cada
/// servicio registrado en el tamaño del ambito, <b>no</b> para compararlas de igual a igual.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class ScopeLifecycleBenchmark
{
    private readonly LockedContainer _locked = new();
    private readonly LockFreeContainer _lockFree = new();
    private readonly EagerContainer _eager = new();
    private readonly LeanLazyContainer _lean = new();

    // ----- ambito vacio: crear y desechar, sin resolver nada -----

    [Benchmark(Baseline = true, Description = "lean lazy | ambito vacio")]
    public object LeanEmpty()
    {
        var scope = _lean.CreateScope();
        scope.Dispose();
        return scope;
    }

    [Benchmark(Description = "eager | ambito vacio")]
    public object EagerEmpty()
    {
        var scope = _eager.CreateScope();
        scope.Dispose();
        return scope;
    }

    [Benchmark(Description = "lock 9 servicios | ambito vacio")]
    public object LockedEmpty()
    {
        var scope = _locked.CreateScope();
        scope.Dispose();
        return scope;
    }

    [Benchmark(Description = "cas 9 servicios | ambito vacio")]
    public object FreeEmpty()
    {
        var scope = _lockFree.CreateScope();
        scope.Dispose();
        return scope;
    }

    // ----- ciclo completo: crear, resolver un scoped desechable, desechar -----

    [Benchmark(Description = "lean lazy | ciclo completo")]
    public object LeanFull()
    {
        var scope = _lean.CreateScope();
        _ = scope.SyncDisp;
        scope.Dispose();
        return scope;
    }

    [Benchmark(Description = "eager | ciclo completo")]
    public object EagerFull()
    {
        var scope = _eager.CreateScope();
        _ = scope.ScopedSyncDisp;
        scope.Dispose();
        return scope;
    }

    [Benchmark(Description = "lock 9 servicios | ciclo completo")]
    public object LockedFull()
    {
        var scope = _locked.CreateScope();
        _ = scope.ScopedSyncDisp;
        scope.Dispose();
        return scope;
    }

    [Benchmark(Description = "cas 9 servicios | ciclo completo")]
    public object FreeFull()
    {
        var scope = _lockFree.CreateScope();
        _ = scope.ScopedSyncDisp;
        scope.Dispose();
        return scope;
    }
}

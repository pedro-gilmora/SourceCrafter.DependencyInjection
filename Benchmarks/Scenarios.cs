using BenchmarkDotNet.Attributes;

using StrongInject;

namespace Benchmarks;

/// <summary>
/// Base comun a todos los escenarios.
/// <para>
/// La regla que gobierna este banco de pruebas: <b>toda rama debe hacer exactamente el
/// mismo trabajo</b>. La version anterior no la cumplia (el metodo de SourceCrafter solo
/// construia el contenedor mientras los demas resolvian servicios), de modo que las cifras
/// publicadas no comparaban nada.
/// </para>
/// </summary>
[MemoryDiagnoser]
public abstract class ContainerBenchmark
{
    protected SourceCrafterContainer SourceCrafter = null!;
    protected JabContainer Jab = null!;
    protected PureDiContainer PureDi = null!;
    protected StrongInjectContainer StrongInject = null!;
    protected DieContainer Die = null!;

    /// <summary>
    /// StrongInject expone sus raices como implementaciones explicitas de interfaz, asi que
    /// hay que llamarlas a traves de <see cref="IContainer{T}"/>.
    /// </summary>
    protected IContainer<T> Si<T>() => (IContainer<T>)(object)StrongInject;

    [GlobalSetup]
    public void GlobalSetup()
    {
        SourceCrafter = new SourceCrafterContainer();
        Jab = new JabContainer();
        PureDi = new PureDiContainer();
        StrongInject = new StrongInjectContainer();
        Die = DieContainer.DIE_CreateContainer();

        // Se calienta cada contenedor para que el escenario mida resolucion en caliente y
        // no la construccion perezosa de los singletons, que tiene su propio benchmark.
        _ = SourceCrafter.Database;
        _ = Jab.GetService<IDatabase>();
        _ = PureDi.Database;
        _ = Si<IDatabase>().Run(static (x, _) => x, 0);
        _ = Die.GetDatabase();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        SourceCrafter.Dispose();
        Jab.Dispose();
        PureDi.Dispose();
        StrongInject.Dispose();
        Die.Dispose();
    }
}

/// <summary>
/// Escenario <i>Singleton</i> de .NET Matrix: resolver repetidamente un servicio ya
/// construido. Mide el coste del camino rapido, que es el que domina en produccion.
/// </summary>
public class SingletonBenchmark : ContainerBenchmark
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

    // StrongInject entrega un Owned<T> o ejecuta el consumo dentro de Run: en ambos casos
    // hace mas trabajo que los demas porque rastrea la propiedad para el desecho.
    [Benchmark(Description = "StrongInject")]
    public IDatabase StrongInjectDi() => Si<IDatabase>().Run(static (x, _) => x, 0);

    [Benchmark(Description = "MrMeeseeks.DIE")]
    public IDatabase DieDi() => Die.GetDatabase();
}

/// <summary>
/// Escenario <i>Transient</i>: cada resolucion debe construir una instancia nueva.
/// Se usa <see cref="Level3"/> y no <see cref="Settings"/> porque un transient sin
/// dependencias se puede reducir a un <c>new</c> y dejaria de medir el contenedor.
/// </summary>
public class TransientBenchmark : ContainerBenchmark
{
    [Benchmark(Baseline = true, Description = "Hand Coded")]
    public Level3 HandCoded() => new(new Leaf(), new Leaf());

    [Benchmark(Description = "SourceCrafter")]
    public Level3 SourceCrafterDi() => SourceCrafter.Level3;

    [Benchmark(Description = "Jab")]
    public Level3 JabDi() => Jab.GetService<Level3>();

    [Benchmark(Description = "Pure.DI")]
    public Level3 PureDiDi() => PureDi.Transient;

    [Benchmark(Description = "StrongInject")]
    public Level3 StrongInjectDi() => Si<Level3>().Run(static (x, _) => x, 0);

    [Benchmark(Description = "MrMeeseeks.DIE")]
    public Level3 DieDi() => Die.GetTransient();
}

/// <summary>
/// Escenario <i>Complex</i>: grafo de cuatro niveles y quince nodos, con un singleton
/// enganchado en la raiz. Es la forma que usan los benchmarks publicados de Pure.DI.
/// </summary>
public class ComplexGraphBenchmark : ContainerBenchmark
{
    [Benchmark(Baseline = true, Description = "Hand Coded")]
    public Level1 HandCoded() =>
        new(new Level2(new Level3(new Leaf(), new Leaf()), new Level3(new Leaf(), new Leaf())),
            new Level2(new Level3(new Leaf(), new Leaf()), new Level3(new Leaf(), new Leaf())),
            HandCodedDatabase);

    private static readonly IDatabase HandCodedDatabase = new Database(new Settings());

    [Benchmark(Description = "SourceCrafter")]
    public Level1 SourceCrafterDi() => SourceCrafter.Level1;

    [Benchmark(Description = "Jab")]
    public Level1 JabDi() => Jab.GetService<Level1>();

    [Benchmark(Description = "Pure.DI")]
    public Level1 PureDiDi() => PureDi.Complex;

    [Benchmark(Description = "StrongInject")]
    public Level1 StrongInjectDi() => Si<Level1>().Run(static (x, _) => x, 0);

    [Benchmark(Description = "MrMeeseeks.DIE")]
    public Level1 DieDi() => Die.GetComplex();
}

/// <summary>
/// Escenario <i>Scoped</i>: crear un ambito, resolver dentro de el y liberarlo.
/// <para>
/// Es el escenario que responde a la objecion de memoria: aqui se ve el coste real de
/// crear un ambito, incluidos los candados por dependencia que el contenedor asigna en su
/// constructor.
/// </para>
/// <para>
/// StrongInject y MrMeeseeks.DIE no aparecen aqui porque no tienen ambito de peticion:
/// StrongInject modela la vida con propiedad (<c>Owned&lt;T&gt;</c>) y DIE con "transient
/// scopes" atados a una funcion de creacion. Inventarles un equivalente daria una cifra que
/// no corresponde a nada que un usuario suyo pueda escribir.
/// </para>
/// </summary>
public class ScopeBenchmark : ContainerBenchmark
{
    [Benchmark(Baseline = true, Description = "Hand Coded")]
    public ISession HandCoded()
    {
        var session = new Session(HandCodedDatabase);
        session.Dispose();
        return session;
    }

    private static readonly IDatabase HandCodedDatabase = new Database(new Settings());

    [Benchmark(Description = "SourceCrafter")]
    public ISession SourceCrafterDi()
    {
        var scope = SourceCrafter.CreateScope();
        var session = scope.Session;
        scope.Dispose();
        return session;
    }

    [Benchmark(Description = "Jab")]
    public ISession JabDi()
    {
        var scope = Jab.CreateScope();
        var session = scope.GetService<ISession>();
        scope.Dispose();
        return session;
    }

    [Benchmark(Description = "Pure.DI")]
    public ISession PureDiDi()
    {
        var scope = new PureDiContainer(PureDi);
        var session = scope.Session;
        scope.Dispose();
        return session;
    }
}

/// <summary>
/// Crear y liberar un ambito <b>sin resolver nada</b>.
/// <para>
/// Es el escenario que aisla el coste fijo de abrir un ambito, que en una aplicacion real
/// se paga en cada peticion aunque esa peticion solo toque una parte de los servicios
/// scoped registrados. Antes de la asignacion perezosa, este contenedor pagaba aqui un
/// candado (40 B) por <b>cada</b> servicio scoped declarado, resolviera o no alguno.
/// </para>
/// <para>Sin StrongInject ni DIE, por el mismo motivo que el escenario anterior.</para>
/// </summary>
public class EmptyScopeBenchmark : ContainerBenchmark
{
    [Benchmark(Baseline = true, Description = "Hand Coded")]
    public object HandCoded() => new object();

    [Benchmark(Description = "SourceCrafter")]
    public object SourceCrafterDi()
    {
        var scope = SourceCrafter.CreateScope();
        scope.Dispose();
        return scope;
    }

    [Benchmark(Description = "Jab")]
    public object JabDi()
    {
        var scope = Jab.CreateScope();
        scope.Dispose();
        return scope;
    }

    [Benchmark(Description = "Pure.DI")]
    public object PureDiDi()
    {
        var scope = new PureDiContainer(PureDi);
        scope.Dispose();
        return scope;
    }
}

/// <summary>
/// Escenarios <i>Prepare And Register</i> y <i>Prepare And Register And Simple Resolve</i>.
/// Aqui es donde un contenedor compile-time deberia acercarse a cero: no hay registro en
/// tiempo de ejecucion que pagar. Lo unico que se mide es la asignacion del propio objeto
/// contenedor y de los campos que inicializa su constructor.
/// <para>
/// Diferencia semantica que hay que tener presente al leer la tabla: en SourceCrafter los
/// singletons son <c>static</c> (decision de diseno deliberada), asi que un contenedor
/// nuevo <b>reutiliza</b> el singleton ya construido, mientras Jab y Pure.DI lo construyen
/// por contenedor. No es la misma semantica, y por eso la cifra no es directamente
/// comparable con las otras dos.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class ContainerCreationBenchmark
{
    [Benchmark(Baseline = true, Description = "Hand Coded")]
    public object HandCoded() => new Database(new Settings());

    [Benchmark(Description = "SourceCrafter")]
    public object SourceCrafterDi()
    {
        using var container = new SourceCrafterContainer();
        return container.Database;
    }

    [Benchmark(Description = "Jab")]
    public object JabDi()
    {
        using var container = new JabContainer();
        return container.GetService<IDatabase>();
    }

    [Benchmark(Description = "Pure.DI")]
    public object PureDiDi()
    {
        using var container = new PureDiContainer();
        return container.Database;
    }

    [Benchmark(Description = "StrongInject")]
    public object StrongInjectDi()
    {
        using var container = new StrongInjectContainer();
        return ((IContainer<IDatabase>)container).Run(static (x, _) => x, 0);
    }

    [Benchmark(Description = "MrMeeseeks.DIE")]
    public object DieDi()
    {
        using var container = DieContainer.DIE_CreateContainer();
        return container.GetDatabase();
    }
}

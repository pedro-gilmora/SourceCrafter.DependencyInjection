using System.Runtime.CompilerServices;
using System.Threading;

using BenchmarkDotNet.Attributes;

namespace Benchmarks;

// ---------------------------------------------------------------------------
// Grafo comun del estudio: un singleton y dos scoped encadenados, que es la
// forma minima en la que la granularidad del candado puede notarse.
//   Cfg (transient) -> Db (singleton) -> Sess (scoped) -> Repo (scoped)
// ---------------------------------------------------------------------------

public sealed class Cfg;

public sealed class Db(Cfg cfg)
{
    public Cfg Cfg { get; } = cfg;
}

public sealed class Sess(Db db) : IDisposable
{
    public Db Db { get; } = db;
    public void Dispose() { }
}

public sealed class Repo(Sess sess) : IDisposable
{
    public Sess Sess { get; } = sess;
    public void Dispose() { }
}

/// <summary>
/// <b>Variante A - un candado por dependencia</b> (lo que se emite hoy).
/// Cada servicio cacheado lleva su propio campo <c>Lock?</c> perezoso, y las dependencias
/// se resuelven <i>dentro</i> del candado.
/// </summary>
public class ContainerA
{
    protected bool _disposed;
    protected ContainerA _root = default!;

    private static Db? _db;
    private static readonly Lock _dbLock = new();

    private Sess? _sess;
    private Lock? _sessLock;

    private Repo? _repo;
    private Lock? _repoLock;

    protected static Lock EnsureLock(ref Lock? location)
    {
        var current = Volatile.Read(ref location);

        if (current is not null) return current;

        var created = new Lock();

        return Interlocked.CompareExchange(ref location, created, null) ?? created;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Db CreateDb() { lock (_dbLock) return _db ??= new Db(new Cfg()); }

    public Db Database { get { if (_db is not null) return _db; return CreateDb(); } }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Sess CreateSess() { lock (EnsureLock(ref _sessLock)) return _sess ??= new Sess(Database); }

    public Sess Session { get { if (_sess is not null) return _sess; return CreateSess(); } }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Repo CreateRepo() { lock (EnsureLock(ref _repoLock)) return _repo ??= new Repo(Session); }

    public Repo Repository { get { if (_repo is not null) return _repo; return CreateRepo(); } }

    public virtual ContainerA CreateScope() => new ScopedA { _root = this };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var r = _repo; _repo = null; r?.Dispose();
        var s = _sess; _sess = null; s?.Dispose();
    }
}

public sealed class ScopedA : ContainerA;

/// <summary>
/// <b>Variante B - un candado perezoso por lifetime</b>. Un unico <c>Lock?</c> para todo lo
/// scoped. Las dependencias cacheadas se resuelven <i>fuera</i> del candado: sin eso, dos
/// candados tomados en ordenes opuestos volverian a poder interbloquearse.
/// </summary>
public class ContainerB
{
    protected bool _disposed;
    protected ContainerB _root = default!;

    private static Db? _db;
    private static readonly Lock _dbLock = new();

    private Sess? _sess;
    private Repo? _repo;
    private Lock? _scopedLock;

    protected static Lock EnsureLock(ref Lock? location)
    {
        var current = Volatile.Read(ref location);

        if (current is not null) return current;

        var created = new Lock();

        return Interlocked.CompareExchange(ref location, created, null) ?? created;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Db CreateDb() { lock (_dbLock) return _db ??= new Db(new Cfg()); }

    public Db Database { get { if (_db is not null) return _db; return CreateDb(); } }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Sess CreateSess()
    {
        var db = Database;                                  // izada
        lock (EnsureLock(ref _scopedLock)) return _sess ??= new Sess(db);
    }

    public Sess Session { get { if (_sess is not null) return _sess; return CreateSess(); } }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Repo CreateRepo()
    {
        var sess = Session;                                 // izada
        lock (EnsureLock(ref _scopedLock)) return _repo ??= new Repo(sess);
    }

    public Repo Repository { get { if (_repo is not null) return _repo; return CreateRepo(); } }

    public virtual ContainerB CreateScope() => new ScopedB { _root = this };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var r = _repo; _repo = null; r?.Dispose();
        var s = _sess; _sess = null; s?.Dispose();
    }
}

public sealed class ScopedB : ContainerB;

/// <summary>
/// <b>Variante C - <c>lock(this)</c> para scoped y estatico para singleton</b>. No asigna
/// ningun objeto de bloqueo: el ambito <i>es</i> el candado. Dependencias cacheadas izadas,
/// igual que en B.
/// </summary>
public class ContainerC
{
    protected bool _disposed;
    protected ContainerC _root = default!;

    private static Db? _db;
    private static readonly Lock _dbLock = new();

    private Sess? _sess;
    private Repo? _repo;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Db CreateDb() { lock (_dbLock) return _db ??= new Db(new Cfg()); }

    public Db Database { get { if (_db is not null) return _db; return CreateDb(); } }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Sess CreateSess()
    {
        var db = Database;
        lock (this) return _sess ??= new Sess(db);
    }

    /// <summary>Camino caliente <b>sin</b> local: dos lecturas del campo (lo que se emite hoy).</summary>
    public Sess Session { get { if (_sess is not null) return _sess; return CreateSess(); } }

    /// <summary>Camino caliente <b>con</b> local: una sola lectura del campo.</summary>
    public Sess SessionViaLocal
    {
        get
        {
            var current = _sess;

            if (current is not null) return current;

            return CreateSess();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Repo CreateRepo()
    {
        var sess = Session;
        lock (this) return _repo ??= new Repo(sess);
    }

    public Repo Repository { get { if (_repo is not null) return _repo; return CreateRepo(); } }

    public virtual ContainerC CreateScope() => new ScopedC { _root = this };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var r = _repo; _repo = null; r?.Dispose();
        var s = _sess; _sess = null; s?.Dispose();
    }
}

public sealed class ScopedC : ContainerC;

/// <summary>
/// Ciclo completo de ambito: crear, resolver la cadena scoped y liberar. Es donde la
/// granularidad del candado se paga, porque los objetos de bloqueo se asignan aqui.
/// </summary>
[MemoryDiagnoser]
public class LockGranularityScopeBenchmark
{
    private readonly ContainerA _a = new();
    private readonly ContainerB _b = new();
    private readonly ContainerC _c = new();

    [GlobalSetup]
    public void Setup()
    {
        // Deja los singletons construidos: se mide el ambito, no el arranque.
        _ = _a.Database; _ = _b.Database; _ = _c.Database;
    }

    [Benchmark(Baseline = true, Description = "A: Lock por dependencia")]
    public Repo PerDependency()
    {
        var scope = _a.CreateScope();
        var repo = scope.Repository;
        scope.Dispose();
        return repo;
    }

    [Benchmark(Description = "B: Lock por lifetime")]
    public Repo PerLifetime()
    {
        var scope = _b.CreateScope();
        var repo = scope.Repository;
        scope.Dispose();
        return repo;
    }

    [Benchmark(Description = "C: lock(this)")]
    public Repo LockOnThis()
    {
        var scope = _c.CreateScope();
        var repo = scope.Repository;
        scope.Dispose();
        return repo;
    }
}

/// <summary>Ambito que no resuelve nada: aisla el tamano del objeto de ambito.</summary>
[MemoryDiagnoser]
public class LockGranularityEmptyScopeBenchmark
{
    private readonly ContainerA _a = new();
    private readonly ContainerB _b = new();
    private readonly ContainerC _c = new();

    [Benchmark(Baseline = true, Description = "A: Lock por dependencia")]
    public ContainerA PerDependency()
    {
        var scope = _a.CreateScope();
        scope.Dispose();
        return scope;
    }

    [Benchmark(Description = "B: Lock por lifetime")]
    public ContainerB PerLifetime()
    {
        var scope = _b.CreateScope();
        scope.Dispose();
        return scope;
    }

    [Benchmark(Description = "C: lock(this)")]
    public ContainerC LockOnThis()
    {
        var scope = _c.CreateScope();
        scope.Dispose();
        return scope;
    }
}

/// <summary>
/// Camino caliente sobre un servicio ya construido: la unica pregunta aqui es si leer el
/// campo a un local cuesta algo frente a leerlo dos veces.
/// </summary>
[MemoryDiagnoser]
public class HotPathLocalBenchmark
{
    private readonly ContainerC _scope = (ContainerC)new ContainerC().CreateScope();

    [GlobalSetup]
    public void Setup() => _ = _scope.Session;

    [Benchmark(Baseline = true, Description = "Sin local (dos lecturas)")]
    public Sess WithoutLocal() => _scope.Session;

    [Benchmark(Description = "Con local (una lectura)")]
    public Sess WithLocal() => _scope.SessionViaLocal;
}

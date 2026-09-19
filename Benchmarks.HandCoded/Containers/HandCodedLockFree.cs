using System.Runtime.CompilerServices;

namespace Benchmarks.HandCoded;

/// <summary>
/// Contenedor escrito a mano, estrategia <b>sin candado</b>: se publica con
/// <see cref="Interlocked.CompareExchange{T}(ref T, T, T)"/>.
/// <para>
/// El camino caliente es identico al de <see cref="LockedContainer"/> y por eso las dos
/// columnas del eje de locking deben empatar en cuanto el servicio esta publicado. Que empaten
/// no es un resultado pobre: es la comprobacion de que el candado <b>no se paga por
/// resolucion</b>, que es lo que suele darse por supuesto sin medirlo. Toda la diferencia
/// entre las dos estrategias vive en el camino frio y en la correccion.
/// </para>
/// <para>
/// <b>Esta variante no es intercambiable con la otra, y la matriz existe para enseñar
/// exactamente donde deja de serlo.</b> El CAS no impide que dos hilos construyan a la vez:
/// impide que los dos <i>publiquen</i>. El perdedor se queda con una instancia que tira. Una
/// sonda de este repo lo midio bajo llegada simultanea con barrera: entre un 27,5% y un 65,5%
/// de las instancias construidas se descartan, con el peor caso en cuatro hilos.
/// </para>
/// <para>
/// Para un servicio sin recursos eso solo es basura que el GC recoge. Para un
/// <see cref="IDisposable"/> o un <see cref="IAsyncDisposable"/> es una fuga: la instancia
/// descartada nunca se publico, asi que el contenedor no la conoce y nadie va a desecharla.
/// Por eso las celdas <c>sin candado + desechable</c> de la matriz estan marcadas como
/// <b>incorrectas</b> en <see cref="SemanticCheck"/> aunque sus cifras se midan igual: medirlas
/// y callar que estan mal seria publicar un ganador que pierde recursos.
/// </para>
/// </summary>
public sealed class LockFreeContainer : IDisposable, IAsyncDisposable
{
    // ===== Singleton, async-kind sincrono =====

    private SyncPlain? _syncPlain;
    private SyncDisp? _syncDisp;
    private SyncAsyncDisp? _syncAsyncDisp;

    public SyncPlain SingletonSyncPlain
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref _syncPlain) ?? SlowSyncPlain();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private SyncPlain SlowSyncPlain()
    {
        var created = new SyncPlain();
        return Interlocked.CompareExchange(ref _syncPlain, created, null) ?? created;
    }

    public SyncDisp SingletonSyncDisp
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref _syncDisp) ?? SlowSyncDisp();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private SyncDisp SlowSyncDisp()
    {
        var created = new SyncDisp();
        return Interlocked.CompareExchange(ref _syncDisp, created, null) ?? created;
    }

    public SyncAsyncDisp SingletonSyncAsyncDisp
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref _syncAsyncDisp) ?? SlowSyncAsyncDisp();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private SyncAsyncDisp SlowSyncAsyncDisp()
    {
        var created = new SyncAsyncDisp();
        return Interlocked.CompareExchange(ref _syncAsyncDisp, created, null) ?? created;
    }

    // ===== Singleton, async-kind ValueTask y Task =====
    //
    // Aqui el descarte es peor que en el lado sincrono, porque no hay forma de estrecharlo.
    // Sin candado no se puede publicar una tarea en vuelo, asi que cada llamador que llega
    // antes de la publicacion ejecuta la fabrica entera. Con una fabrica que hace E/S real eso
    // no es una instancia de mas: son N conexiones abiertas de las que se conserva una.

    private VtPlain? _vtPlain;

    public ValueTask<VtPlain> GetSingletonVtPlainAsync()
    {
        var value = Volatile.Read(ref _vtPlain);
        return value is not null ? new ValueTask<VtPlain>(value) : SlowVtPlainAsync();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async ValueTask<VtPlain> SlowVtPlainAsync()
    {
        var created = await VtPlain.CreateAsync().ConfigureAwait(false);
        return Interlocked.CompareExchange(ref _vtPlain, created, null) ?? created;
    }

    private VtDisp? _vtDisp;

    public ValueTask<VtDisp> GetSingletonVtDispAsync()
    {
        var value = Volatile.Read(ref _vtDisp);
        return value is not null ? new ValueTask<VtDisp>(value) : SlowVtDispAsync();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async ValueTask<VtDisp> SlowVtDispAsync()
    {
        var created = await VtDisp.CreateAsync().ConfigureAwait(false);
        return Interlocked.CompareExchange(ref _vtDisp, created, null) ?? created;
    }

    private VtAsyncDisp? _vtAsyncDisp;

    public ValueTask<VtAsyncDisp> GetSingletonVtAsyncDispAsync()
    {
        var value = Volatile.Read(ref _vtAsyncDisp);
        return value is not null ? new ValueTask<VtAsyncDisp>(value) : SlowVtAsyncDispAsync();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async ValueTask<VtAsyncDisp> SlowVtAsyncDispAsync()
    {
        var created = await VtAsyncDisp.CreateAsync().ConfigureAwait(false);
        return Interlocked.CompareExchange(ref _vtAsyncDisp, created, null) ?? created;
    }

    private TaskPlain? _taskPlain;

    public ValueTask<TaskPlain> GetSingletonTaskPlainAsync()
    {
        var value = Volatile.Read(ref _taskPlain);
        return value is not null ? new ValueTask<TaskPlain>(value) : SlowTaskPlainAsync();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async ValueTask<TaskPlain> SlowTaskPlainAsync()
    {
        var created = await TaskPlain.CreateAsync().ConfigureAwait(false);
        return Interlocked.CompareExchange(ref _taskPlain, created, null) ?? created;
    }

    private TaskDisp? _taskDisp;

    public ValueTask<TaskDisp> GetSingletonTaskDispAsync()
    {
        var value = Volatile.Read(ref _taskDisp);
        return value is not null ? new ValueTask<TaskDisp>(value) : SlowTaskDispAsync();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async ValueTask<TaskDisp> SlowTaskDispAsync()
    {
        var created = await TaskDisp.CreateAsync().ConfigureAwait(false);
        return Interlocked.CompareExchange(ref _taskDisp, created, null) ?? created;
    }

    private TaskAsyncDisp? _taskAsyncDisp;

    public ValueTask<TaskAsyncDisp> GetSingletonTaskAsyncDispAsync()
    {
        var value = Volatile.Read(ref _taskAsyncDisp);
        return value is not null ? new ValueTask<TaskAsyncDisp>(value) : SlowTaskAsyncDispAsync();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async ValueTask<TaskAsyncDisp> SlowTaskAsyncDispAsync()
    {
        var created = await TaskAsyncDisp.CreateAsync().ConfigureAwait(false);
        return Interlocked.CompareExchange(ref _taskAsyncDisp, created, null) ?? created;
    }

    // ===== Transient =====
    //
    // Las tres celdas sin desecho son identicas a las de LockedContainer, literalmente el mismo
    // codigo, y el banco las mide igual. Funcionan como grupo de control dentro de la propia
    // matriz: si dos celdas que ejecutan el mismo codigo no empatan, la corrida no es publicable.
    //
    // Las celdas con desecho NO son identicas. Sin candado hay que publicar la lista con CAS y
    // luego protegerla igualmente, porque List<T> no es segura entre hilos. Es el sitio donde
    // "sin candado" deja de ahorrar nada: se acaba necesitando exclusion de todas formas.

    private List<IDisposable>? _transientDisposables;
    private List<IAsyncDisposable>? _transientAsyncDisposables;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SyncPlain TransientSyncPlain() => new();

    public SyncDisp TransientSyncDisp()
    {
        var created = new SyncDisp();
        var list = Volatile.Read(ref _transientDisposables) ?? EnsureDisposables();
        lock (list) list.Add(created);
        return created;
    }

    public SyncAsyncDisp TransientSyncAsyncDisp()
    {
        var created = new SyncAsyncDisp();
        var list = Volatile.Read(ref _transientAsyncDisposables) ?? EnsureAsyncDisposables();
        lock (list) list.Add(created);
        return created;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private List<IDisposable> EnsureDisposables()
    {
        var created = new List<IDisposable>();
        return Interlocked.CompareExchange(ref _transientDisposables, created, null) ?? created;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private List<IAsyncDisposable> EnsureAsyncDisposables()
    {
        var created = new List<IAsyncDisposable>();
        return Interlocked.CompareExchange(ref _transientAsyncDisposables, created, null) ?? created;
    }

    public ValueTask<VtPlain> TransientVtPlainAsync() => VtPlain.CreateAsync();

    public async ValueTask<VtDisp> TransientVtDispAsync()
    {
        var created = await VtDisp.CreateAsync().ConfigureAwait(false);
        var list = Volatile.Read(ref _transientDisposables) ?? EnsureDisposables();
        lock (list) list.Add(created);
        return created;
    }

    public async ValueTask<VtAsyncDisp> TransientVtAsyncDispAsync()
    {
        var created = await VtAsyncDisp.CreateAsync().ConfigureAwait(false);
        var list = Volatile.Read(ref _transientAsyncDisposables) ?? EnsureAsyncDisposables();
        lock (list) list.Add(created);
        return created;
    }

    public ValueTask<TaskPlain> TransientTaskPlainAsync() => new(TaskPlain.CreateAsync());

    public async ValueTask<TaskDisp> TransientTaskDispAsync()
    {
        var created = await TaskDisp.CreateAsync().ConfigureAwait(false);
        var list = Volatile.Read(ref _transientDisposables) ?? EnsureDisposables();
        lock (list) list.Add(created);
        return created;
    }

    public async ValueTask<TaskAsyncDisp> TransientTaskAsyncDispAsync()
    {
        var created = await TaskAsyncDisp.CreateAsync().ConfigureAwait(false);
        var list = Volatile.Read(ref _transientAsyncDisposables) ?? EnsureAsyncDisposables();
        lock (list) list.Add(created);
        return created;
    }

    // ===== Ambitos =====

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LockFreeScope CreateScope() => new(this);

    public void Dispose()
    {
        _syncDisp?.Dispose();
        _vtDisp?.Dispose();
        _taskDisp?.Dispose();

        if (_transientDisposables is { } disposables)
        {
            foreach (var disposable in disposables) disposable.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();

        if (_syncAsyncDisp is { } syncAsyncDisp) await syncAsyncDisp.DisposeAsync().ConfigureAwait(false);
        if (_vtAsyncDisp is { } vtAsyncDisp) await vtAsyncDisp.DisposeAsync().ConfigureAwait(false);
        if (_taskAsyncDisp is { } taskAsyncDisp) await taskAsyncDisp.DisposeAsync().ConfigureAwait(false);

        if (_transientAsyncDisposables is { } asyncDisposables)
        {
            foreach (var asyncDisposable in asyncDisposables) await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
    }
}

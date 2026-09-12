using System.Runtime.CompilerServices;

namespace Benchmarks.HandCoded;

/// <summary>
/// Ambito de <see cref="LockedContainer"/>: los nueve servicios scoped, estrategia con candado.
/// <para>
/// Un ambito es una asignacion y nada mas. No lleva candado propio (bloquea sobre <c>this</c>,
/// que no asigna), no lleva lista de desecho para lo cacheado (desechar es probar tres campos)
/// y no copia nada de la raiz: guarda la referencia y delega.
/// </para>
/// <para>
/// Ese es el punto donde un contenedor se gana o se pierde de verdad. El camino caliente de un
/// singleton esta acotado por debajo y todo el mundo lo alcanza; en cambio un ambito se crea y
/// se destruye una vez por peticion, asi que todo lo que se le cuelgue se paga tantas veces
/// como peticiones haya.
/// </para>
/// </summary>
public sealed class LockedScope(LockedContainer root) : IDisposable, IAsyncDisposable
{
    private readonly LockedContainer _root = root;

    // ===== Scoped, async-kind sincrono =====

    private SyncPlain? _syncPlain;
    private SyncDisp? _syncDisp;
    private SyncAsyncDisp? _syncAsyncDisp;

    public SyncPlain ScopedSyncPlain
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref _syncPlain) ?? SlowSyncPlain();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private SyncPlain SlowSyncPlain()
    {
        lock (this)
        {
            var value = _syncPlain;
            if (value is null) Volatile.Write(ref _syncPlain, value = new SyncPlain());
            return value;
        }
    }

    public SyncDisp ScopedSyncDisp
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref _syncDisp) ?? SlowSyncDisp();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private SyncDisp SlowSyncDisp()
    {
        lock (this)
        {
            var value = _syncDisp;
            if (value is null) Volatile.Write(ref _syncDisp, value = new SyncDisp());
            return value;
        }
    }

    public SyncAsyncDisp ScopedSyncAsyncDisp
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref _syncAsyncDisp) ?? SlowSyncAsyncDisp();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private SyncAsyncDisp SlowSyncAsyncDisp()
    {
        lock (this)
        {
            var value = _syncAsyncDisp;
            if (value is null) Volatile.Write(ref _syncAsyncDisp, value = new SyncAsyncDisp());
            return value;
        }
    }

    // ===== Scoped, async-kind ValueTask y Task =====

    private VtPlain? _vtPlain;
    private Task<VtPlain>? _vtPlainInFlight;

    public ValueTask<VtPlain> GetScopedVtPlainAsync()
    {
        var value = Volatile.Read(ref _vtPlain);
        return value is not null ? new ValueTask<VtPlain>(value) : new(SlowVtPlainAsync());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Task<VtPlain> SlowVtPlainAsync()
    {
        lock (this)
        {
            var value = _vtPlain;
            return value is not null ? Task.FromResult(value) : _vtPlainInFlight ??= PublishVtPlainAsync();
        }
    }

    private async Task<VtPlain> PublishVtPlainAsync()
    {
        var created = await VtPlain.CreateAsync().ConfigureAwait(false);
        Volatile.Write(ref _vtPlain, created);
        return created;
    }

    private VtDisp? _vtDisp;
    private Task<VtDisp>? _vtDispInFlight;

    public ValueTask<VtDisp> GetScopedVtDispAsync()
    {
        var value = Volatile.Read(ref _vtDisp);
        return value is not null ? new ValueTask<VtDisp>(value) : new(SlowVtDispAsync());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Task<VtDisp> SlowVtDispAsync()
    {
        lock (this)
        {
            var value = _vtDisp;
            return value is not null ? Task.FromResult(value) : _vtDispInFlight ??= PublishVtDispAsync();
        }
    }

    private async Task<VtDisp> PublishVtDispAsync()
    {
        var created = await VtDisp.CreateAsync().ConfigureAwait(false);
        Volatile.Write(ref _vtDisp, created);
        return created;
    }

    private VtAsyncDisp? _vtAsyncDisp;
    private Task<VtAsyncDisp>? _vtAsyncDispInFlight;

    public ValueTask<VtAsyncDisp> GetScopedVtAsyncDispAsync()
    {
        var value = Volatile.Read(ref _vtAsyncDisp);
        return value is not null ? new ValueTask<VtAsyncDisp>(value) : new(SlowVtAsyncDispAsync());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Task<VtAsyncDisp> SlowVtAsyncDispAsync()
    {
        lock (this)
        {
            var value = _vtAsyncDisp;
            return value is not null ? Task.FromResult(value) : _vtAsyncDispInFlight ??= PublishVtAsyncDispAsync();
        }
    }

    private async Task<VtAsyncDisp> PublishVtAsyncDispAsync()
    {
        var created = await VtAsyncDisp.CreateAsync().ConfigureAwait(false);
        Volatile.Write(ref _vtAsyncDisp, created);
        return created;
    }

    private TaskPlain? _taskPlain;
    private Task<TaskPlain>? _taskPlainInFlight;

    public ValueTask<TaskPlain> GetScopedTaskPlainAsync()
    {
        var value = Volatile.Read(ref _taskPlain);
        return value is not null ? new ValueTask<TaskPlain>(value) : new(SlowTaskPlainAsync());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Task<TaskPlain> SlowTaskPlainAsync()
    {
        lock (this)
        {
            var value = _taskPlain;
            return value is not null ? Task.FromResult(value) : _taskPlainInFlight ??= PublishTaskPlainAsync();
        }
    }

    private async Task<TaskPlain> PublishTaskPlainAsync()
    {
        var created = await TaskPlain.CreateAsync().ConfigureAwait(false);
        Volatile.Write(ref _taskPlain, created);
        return created;
    }

    private TaskDisp? _taskDisp;
    private Task<TaskDisp>? _taskDispInFlight;

    public ValueTask<TaskDisp> GetScopedTaskDispAsync()
    {
        var value = Volatile.Read(ref _taskDisp);
        return value is not null ? new ValueTask<TaskDisp>(value) : new(SlowTaskDispAsync());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Task<TaskDisp> SlowTaskDispAsync()
    {
        lock (this)
        {
            var value = _taskDisp;
            return value is not null ? Task.FromResult(value) : _taskDispInFlight ??= PublishTaskDispAsync();
        }
    }

    private async Task<TaskDisp> PublishTaskDispAsync()
    {
        var created = await TaskDisp.CreateAsync().ConfigureAwait(false);
        Volatile.Write(ref _taskDisp, created);
        return created;
    }

    private TaskAsyncDisp? _taskAsyncDisp;
    private Task<TaskAsyncDisp>? _taskAsyncDispInFlight;

    public ValueTask<TaskAsyncDisp> GetScopedTaskAsyncDispAsync()
    {
        var value = Volatile.Read(ref _taskAsyncDisp);
        return value is not null ? new ValueTask<TaskAsyncDisp>(value) : new(SlowTaskAsyncDispAsync());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Task<TaskAsyncDisp> SlowTaskAsyncDispAsync()
    {
        lock (this)
        {
            var value = _taskAsyncDisp;
            return value is not null ? Task.FromResult(value) : _taskAsyncDispInFlight ??= PublishTaskAsyncDispAsync();
        }
    }

    private async Task<TaskAsyncDisp> PublishTaskAsyncDispAsync()
    {
        var created = await TaskAsyncDisp.CreateAsync().ConfigureAwait(false);
        Volatile.Write(ref _taskAsyncDisp, created);
        return created;
    }

    // ===== Delegacion a la raiz =====

    public SyncPlain SingletonSyncPlain
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _root.SingletonSyncPlain;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SyncPlain TransientSyncPlain() => _root.TransientSyncPlain();

    // ===== Desecho =====

    public void Dispose()
    {
        _syncDisp?.Dispose();
        _vtDisp?.Dispose();
        _taskDisp?.Dispose();
    }

    /// <summary>
    /// Devuelve <c>default</c> cuando no hay nada asincrono que desechar, que es el caso normal.
    /// Ese <c>default</c> es un <see cref="ValueTask"/> ya completado y no asigna, asi que un
    /// ambito que no toco ningun <see cref="IAsyncDisposable"/> no paga por tener la capacidad.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        Dispose();

        if (_syncAsyncDisp is null && _vtAsyncDisp is null && _taskAsyncDisp is null) return default;

        return SlowDisposeAsync();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async ValueTask SlowDisposeAsync()
    {
        if (_syncAsyncDisp is { } syncAsyncDisp) await syncAsyncDisp.DisposeAsync().ConfigureAwait(false);
        if (_vtAsyncDisp is { } vtAsyncDisp) await vtAsyncDisp.DisposeAsync().ConfigureAwait(false);
        if (_taskAsyncDisp is { } taskAsyncDisp) await taskAsyncDisp.DisposeAsync().ConfigureAwait(false);
    }
}

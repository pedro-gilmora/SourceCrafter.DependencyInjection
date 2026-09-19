using System.Runtime.CompilerServices;

namespace Benchmarks.HandCoded;

/// <summary>
/// Ambito de <see cref="LockFreeContainer"/>: los nueve servicios scoped, estrategia sin candado.
/// <para>
/// En un ambito el CAS tiene mejor defensa que en la raiz. Un ambito suele pertenecer a una
/// sola peticion y a un solo hilo, asi que la carrera que hace descartar instancias casi nunca
/// ocurre. <b>Casi</b> no es <b>nunca</b>: en cuanto la peticion abre trabajo en paralelo
/// (Task.WhenAll sobre el mismo ambito, que es corriente) vuelve a ser posible, y entonces
/// falla igual que en la raiz.
/// </para>
/// <para>
/// La diferencia practica es que en la raiz la carrera se paga una vez en la vida del proceso,
/// mientras que aqui se puede pagar una vez por peticion.
/// </para>
/// </summary>
public sealed class LockFreeScope(LockFreeContainer root) : IDisposable, IAsyncDisposable
{
    private readonly LockFreeContainer _root = root;

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
        var created = new SyncPlain();
        return Interlocked.CompareExchange(ref _syncPlain, created, null) ?? created;
    }

    public SyncDisp ScopedSyncDisp
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

    public SyncAsyncDisp ScopedSyncAsyncDisp
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

    // ===== Scoped, async-kind ValueTask y Task =====

    private VtPlain? _vtPlain;

    public ValueTask<VtPlain> GetScopedVtPlainAsync()
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

    public ValueTask<VtDisp> GetScopedVtDispAsync()
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

    public ValueTask<VtAsyncDisp> GetScopedVtAsyncDispAsync()
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

    public ValueTask<TaskPlain> GetScopedTaskPlainAsync()
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

    public ValueTask<TaskDisp> GetScopedTaskDispAsync()
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

    public ValueTask<TaskAsyncDisp> GetScopedTaskAsyncDispAsync()
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

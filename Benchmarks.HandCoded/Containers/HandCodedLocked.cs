using System.Runtime.CompilerServices;

namespace Benchmarks.HandCoded;

/// <summary>
/// Contenedor escrito a mano, estrategia <b>con candado</b>.
/// <para>
/// Replica la forma que emite SourceCrafter, que es el punto de partida de todo este banco:
/// el captador caliente es <c>Volatile.Read</c> + prueba de nulo, y el camino frio vive en un
/// metodo aparte marcado <see cref="MethodImplOptions.NoInlining"/>. Si el cuerpo del candado
/// se quedara en el captador, este dejaria de ser una lectura de campo y el JIT no lo
/// insertaria en linea en sus llamadores, que es como se pierde el camino rapido sin notarlo.
/// </para>
/// <para>
/// <b>Se bloquea sobre <c>this</c> y no sobre un <see cref="Lock"/> propio.</b> Tambien es lo
/// que hace SourceCrafter en los resolvers scoped, y la razon es que no asigna nada: un
/// <see cref="Lock"/> son 40 B por contenedor que solo protegen caminos frios. El tipo
/// <see cref="Lock"/> entra mas rapido que el monitor, pero eso solo importaria si hubiera
/// contencion en el camino caliente, y aqui el camino caliente no toca el candado.
/// </para>
/// <para>
/// <b>Divergencia deliberada respecto al generador:</b> aqui los singletons son campos de
/// instancia, no <c>static</c>. SourceCrafter los emite estaticos, lo que le da una ventaja
/// real al crear el contenedor (reutiliza lo ya construido) pero le impide desecharlos: un
/// singleton estatico no pertenece a ningun contenedor, asi que nadie puede desecharlo. Como
/// la disposability es uno de los cuatro ejes que este banco mide, copiar esa forma dejaria un
/// tercio de la matriz sin poder medirse.
/// </para>
/// </summary>
public sealed class LockedContainer : IDisposable, IAsyncDisposable
{
    // ===== Regla de escritura dentro del candado =====
    //
    // Los caminos frios de este fichero publican con una asignacion normal, NO con Volatile.Write.
    // Salir de un 'lock' ya es una barrera de liberacion: garantiza que todo lo escrito dentro --
    // el constructor del servicio incluido -- es visible para cualquiera que despues adquiera ese
    // mismo candado. Añadir Volatile.Write dentro del candado no aporta ninguna garantia; solo
    // repite una barrera que el monitor ya emite.
    //
    // El Volatile.READ del camino caliente si es imprescindible, y esa asimetria es la clave: el
    // lector rapido NO toma el candado, asi que no hereda su barrera y necesita la suya. Lo unico
    // que la lectura volatil impide es que el compilador o el procesador saquen la carga del campo
    // fuera del bucle o la reordenen con las cargas del objeto al que apunta.
    //
    // Hay una excepcion en este fichero y esta marcada donde toca: las escrituras de los caminos
    // asincronos ocurren DESPUES del await, fuera del candado, y por eso siguen siendo volatiles.

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
        lock (this)
        {
            var value = _syncPlain;
            if (value is null) _syncPlain = value = new SyncPlain();
            return value;
        }
    }

    public SyncDisp SingletonSyncDisp
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
            if (value is null) _syncDisp = value = new SyncDisp();
            return value;
        }
    }

    public SyncAsyncDisp SingletonSyncAsyncDisp
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
            if (value is null) _syncAsyncDisp = value = new SyncAsyncDisp();
            return value;
        }
    }

    // ===== Singleton, async-kind ValueTask y Task =====
    //
    // El camino caliente devuelve la instancia ya publicada envuelta por valor: sin Task y sin
    // maquina de estados, cero asignaciones. El 'async' vive solo en el camino frio.
    //
    // El camino frio publica la TAREA EN VUELO dentro del candado antes de esperarla, de modo
    // que dos llamadores simultaneos esperan la misma tarea y la fabrica corre exactamente una
    // vez. No es una optimizacion sino una cuestion de correccion: la alternativa natural
    // (esperar primero y publicar despues) construye una instancia por llamador y tira todas
    // menos una. Para un servicio desechable eso significa que nadie desecha las descartadas.
    // Ver LockFreeContainer, que es justo donde eso pasa y donde se mide lo que cuesta.

    private VtPlain? _vtPlain;
    private Task<VtPlain>? _vtPlainInFlight;

    public ValueTask<VtPlain> GetSingletonVtPlainAsync()
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
        // Volatile aqui SI: esta escritura ocurre despues del await, fuera del candado, y es la
        // que empareja con el Volatile.Read del camino caliente. Es la excepcion a la regla de
        // arriba, no un descuido.
        Volatile.Write(ref _vtPlain, created);
        return created;
    }

    private VtDisp? _vtDisp;
    private Task<VtDisp>? _vtDispInFlight;

    public ValueTask<VtDisp> GetSingletonVtDispAsync()
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

    public ValueTask<VtAsyncDisp> GetSingletonVtAsyncDispAsync()
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

    public ValueTask<TaskPlain> GetSingletonTaskPlainAsync()
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

    public ValueTask<TaskDisp> GetSingletonTaskDispAsync()
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

    public ValueTask<TaskAsyncDisp> GetSingletonTaskAsyncDispAsync()
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

    // ===== Transient =====
    //
    // No hay candado que poner: no se cachea nada, asi que no hay estado compartido que
    // proteger. Por eso el eje de locking no aplica aqui, y el banco lo dice en vez de
    // omitir la celda.
    //
    // Lo que si cambia radicalmente es la disposability. Un transitorio sin desechar es un
    // 'new' y se acabo. Uno desechable obliga al contenedor a RECORDAR cada instancia que
    // entrega, porque es el unico que puede desecharlas: ahi aparece una lista que crece con
    // cada resolucion. Es el coste mas alto de toda la matriz y no tiene nada que ver con el
    // locking ni con el lifetime.

    private List<IDisposable>? _transientDisposables;
    private List<IAsyncDisposable>? _transientAsyncDisposables;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SyncPlain TransientSyncPlain() => new();

    public SyncDisp TransientSyncDisp()
    {
        var created = new SyncDisp();
        lock (this)
        {
            (_transientDisposables ??= []).Add(created);
        }

        return created;
    }

    public SyncAsyncDisp TransientSyncAsyncDisp()
    {
        var created = new SyncAsyncDisp();
        lock (this)
        {
            (_transientAsyncDisposables ??= []).Add(created);
        }

        return created;
    }

    public ValueTask<VtPlain> TransientVtPlainAsync() => VtPlain.CreateAsync();

    public async ValueTask<VtDisp> TransientVtDispAsync()
    {
        var created = await VtDisp.CreateAsync().ConfigureAwait(false);
        lock (this)
        {
            (_transientDisposables ??= []).Add(created);
        }

        return created;
    }

    public async ValueTask<VtAsyncDisp> TransientVtAsyncDispAsync()
    {
        var created = await VtAsyncDisp.CreateAsync().ConfigureAwait(false);
        lock (this)
        {
            (_transientAsyncDisposables ??= []).Add(created);
        }

        return created;
    }

    public ValueTask<TaskPlain> TransientTaskPlainAsync() => new(TaskPlain.CreateAsync());

    public async ValueTask<TaskDisp> TransientTaskDispAsync()
    {
        var created = await TaskDisp.CreateAsync().ConfigureAwait(false);
        lock (this)
        {
            (_transientDisposables ??= []).Add(created);
        }

        return created;
    }

    public async ValueTask<TaskAsyncDisp> TransientTaskAsyncDispAsync()
    {
        var created = await TaskAsyncDisp.CreateAsync().ConfigureAwait(false);
        lock (this)
        {
            (_transientAsyncDisposables ??= []).Add(created);
        }

        return created;
    }

    // ===== Ambitos =====

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LockedScope CreateScope() => new(this);

    /// <summary>
    /// Desechar es probar campos, no recorrer una lista, <b>salvo</b> por los transitorios
    /// desechables, que son los unicos que obligan a llevar lista. Esa asimetria es una de las
    /// cosas que la matriz esta hecha para enseñar.
    /// </summary>
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

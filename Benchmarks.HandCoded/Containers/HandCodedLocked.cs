using System.Runtime.CompilerServices;

namespace Benchmarks.HandCoded;

/// <summary>
/// Contenedor escrito a mano, estrategia <b>con candado</b>.
/// <para>
/// Replica la forma que emite SourceCrafter, que es el punto de partida de todo este banco:
/// el captador caliente es una lectura de campo + prueba de nulo, y el camino frio vive en un
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
/// <b>Los singletons son campos <c>static</c>, y su candado tambien.</b> Es la forma que emite
/// SourceCrafter, y copiarla es el proposito de este contenedor: un campo estatico se lee sin
/// desreferenciar <c>this</c>, porque el JIT puede hornear la direccion como constante. Esa es la
/// unica diferencia de codigo entre un singleton y un scoped en todo el fichero, y es justo lo que
/// la matriz existe para medir.
/// </para>
/// <para>
/// <b>El precio de esa forma, que el banco no puede esconder:</b> un singleton estatico no
/// pertenece a ningun contenedor, asi que "desecharlo" no es una operacion bien definida. Aqui se
/// resuelve desechando la instancia estatica y <b>poniendo el campo a null</b> bajo el candado, de
/// modo que el siguiente contenedor la reconstruye. Eso mantiene medible el eje de disposability,
/// pero conviene ver lo que implica: <i>disponer un contenedor tiene efecto sobre todos los demas
/// del proceso</i>. Es correcto en el caso real -- un proceso tiene un contenedor raiz y se dispone
/// al terminar -- y es una bomba en cualquier escenario que cree varios a la vez. Los campos de
/// ambito, por contraste, siguen siendo de instancia: ahi <c>static</c> no seria una forma
/// discutible sino simplemente un error.
/// </para>
/// </summary>
public sealed class LockedContainer : IDisposable, IAsyncDisposable
{
    // ===== Regla de acceso: aqui solo hay 'lock' =====
    //
    // Este contenedor es el titular del eje "con candado", asi que no usa Volatile ni Interlocked
    // en ninguna parte. Mezclarlos confundiria los dos ejes que la matriz separa: una celda que
    // dijera "con candado" y por dentro publicase con un CAS no mediria el candado, mediria una
    // mezcla. La variante sin candado vive entera en LockFreeContainer, que es donde el CAS es lo
    // que se mide.
    //
    // Consecuencias, por si alguien las echa de menos:
    //
    // - PUBLICAR es una asignacion normal dentro del candado. Salir de un 'lock' ya es una barrera
    //   de liberacion: todo lo escrito dentro, constructor del servicio incluido, es visible para
    //   quien despues adquiera ese mismo candado. Un Volatile.Write ahi no añade garantia alguna.
    //
    // - LEER en el camino caliente es una lectura normal, sin Volatile.Read. Esto SI es una
    //   decision con letra pequeña, y conviene tenerla presente: el lector rapido no toma el
    //   candado, asi que no hereda su barrera. Lo que lo salva es que el runtime de .NET da
    //   semantica de liberacion a TODA escritura de referencia, no solo a las volatiles, asi que la
    //   instancia nunca se publica a medio construir. Lo que se pierde es la barrera de compilador:
    //   el JIT puede cachear la lectura del campo. Aqui da igual porque cada resolucion vuelve a
    //   entrar al captador, pero el mismo patron dentro de un bucle de espera girarian para siempre.
    //   ECMA-335 no lo garantiza; el runtime, si.
    //
    // - La SEGUNDA PRUEBA DE NULO dentro del candado no es ceremonia y no se toca. Va escrita como
    //   '??=' en lugar de un if explicito: compila a lo mismo y deja el cuerpo del candado en una
    //   linea, pero sobre todo hace que la version rota se distinga por UN caracter ('=' en vez de
    //   '??='), que es justo lo que conviene tener presente al leerla.
    //
    //   Medido con la sonda de --check: con '=' a 8 hilos se construye un 168,8% de mas y 11.630 de
    //   20.000 rondas acaban con dos llamadores sosteniendo instancias distintas. Es peor que no
    //   tener candado, porque el candado serializa a los hilos y luego los deja pisarse en fila.

    // ===== Candado y campos de los singletons =====
    //
    // El candado de los singletons es estatico porque lo que protege lo es. Que serialice la
    // primera resolucion de todos los contenedores del proceso no importa: solo cubre caminos
    // frios, uno por servicio en toda la vida del proceso.

    private static readonly Lock SingletonGate = new();

    // ===== Singleton, async-kind sincrono =====

    private static SyncPlain? _syncPlain;
    private static SyncDisp? _syncDisp;
    private static SyncAsyncDisp? _syncAsyncDisp;

    public SyncPlain SingletonSyncPlain
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _syncPlain ?? SlowSyncPlain();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static SyncPlain SlowSyncPlain()
    {
        lock (SingletonGate)
        {
            return _syncPlain ??= new SyncPlain();
        }
    }

    public SyncDisp SingletonSyncDisp
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _syncDisp ?? SlowSyncDisp();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static SyncDisp SlowSyncDisp()
    {
        lock (SingletonGate)
        {
            return _syncDisp ??= new SyncDisp();
        }
    }

    public SyncAsyncDisp SingletonSyncAsyncDisp
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _syncAsyncDisp ?? SlowSyncAsyncDisp();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static SyncAsyncDisp SlowSyncAsyncDisp()
    {
        lock (SingletonGate)
        {
            return _syncAsyncDisp ??= new SyncAsyncDisp();
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

    private static VtPlain? _vtPlain;
    private static Task<VtPlain>? _vtPlainInFlight;

    public ValueTask<VtPlain> GetSingletonVtPlainAsync()
    {
        var value = _vtPlain;
        return value is not null ? new ValueTask<VtPlain>(value) : new(SlowVtPlainAsync());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<VtPlain> SlowVtPlainAsync()
    {
        lock (SingletonGate)
        {
            var value = _vtPlain;
            return value is not null ? Task.FromResult(value) : _vtPlainInFlight ??= PublishVtPlainAsync();
        }
    }

    private static async Task<VtPlain> PublishVtPlainAsync()
    {
        var created = await VtPlain.CreateAsync().ConfigureAwait(false);

        // La publicacion va dentro del candado aunque ocurra despues del await. Antes era un
        // Volatile.Write suelto, que tambien publicaba bien, pero dejaba a este contenedor usando
        // dos mecanismos a la vez y por tanto sin poder titular el eje "con candado".
        lock (SingletonGate)
        {
            _vtPlain = created;
        }

        return created;
    }

    private static VtDisp? _vtDisp;
    private static Task<VtDisp>? _vtDispInFlight;

    public ValueTask<VtDisp> GetSingletonVtDispAsync()
    {
        var value = _vtDisp;
        return value is not null ? new ValueTask<VtDisp>(value) : new(SlowVtDispAsync());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<VtDisp> SlowVtDispAsync()
    {
        lock (SingletonGate)
        {
            var value = _vtDisp;
            return value is not null ? Task.FromResult(value) : _vtDispInFlight ??= PublishVtDispAsync();
        }
    }

    private static async Task<VtDisp> PublishVtDispAsync()
    {
        var created = await VtDisp.CreateAsync().ConfigureAwait(false);
        lock (SingletonGate) _vtDisp = created;
        return created;
    }

    private static VtAsyncDisp? _vtAsyncDisp;
    private static Task<VtAsyncDisp>? _vtAsyncDispInFlight;

    public ValueTask<VtAsyncDisp> GetSingletonVtAsyncDispAsync()
    {
        var value = _vtAsyncDisp;
        return value is not null ? new ValueTask<VtAsyncDisp>(value) : new(SlowVtAsyncDispAsync());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<VtAsyncDisp> SlowVtAsyncDispAsync()
    {
        lock (SingletonGate)
        {
            var value = _vtAsyncDisp;
            return value is not null ? Task.FromResult(value) : _vtAsyncDispInFlight ??= PublishVtAsyncDispAsync();
        }
    }

    private static async Task<VtAsyncDisp> PublishVtAsyncDispAsync()
    {
        var created = await VtAsyncDisp.CreateAsync().ConfigureAwait(false);
        lock (SingletonGate) _vtAsyncDisp = created;
        return created;
    }

    private static TaskPlain? _taskPlain;
    private static Task<TaskPlain>? _taskPlainInFlight;

    public ValueTask<TaskPlain> GetSingletonTaskPlainAsync()
    {
        var value = _taskPlain;
        return value is not null ? new ValueTask<TaskPlain>(value) : new(SlowTaskPlainAsync());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<TaskPlain> SlowTaskPlainAsync()
    {
        lock (SingletonGate)
        {
            var value = _taskPlain;
            return value is not null ? Task.FromResult(value) : _taskPlainInFlight ??= PublishTaskPlainAsync();
        }
    }

    private static async Task<TaskPlain> PublishTaskPlainAsync()
    {
        var created = await TaskPlain.CreateAsync().ConfigureAwait(false);
        lock (SingletonGate) _taskPlain = created;
        return created;
    }

    private static TaskDisp? _taskDisp;
    private static Task<TaskDisp>? _taskDispInFlight;

    public ValueTask<TaskDisp> GetSingletonTaskDispAsync()
    {
        var value = _taskDisp;
        return value is not null ? new ValueTask<TaskDisp>(value) : new(SlowTaskDispAsync());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<TaskDisp> SlowTaskDispAsync()
    {
        lock (SingletonGate)
        {
            var value = _taskDisp;
            return value is not null ? Task.FromResult(value) : _taskDispInFlight ??= PublishTaskDispAsync();
        }
    }

    private static async Task<TaskDisp> PublishTaskDispAsync()
    {
        var created = await TaskDisp.CreateAsync().ConfigureAwait(false);
        lock (SingletonGate) _taskDisp = created;
        return created;
    }

    private static TaskAsyncDisp? _taskAsyncDisp;
    private static Task<TaskAsyncDisp>? _taskAsyncDispInFlight;

    public ValueTask<TaskAsyncDisp> GetSingletonTaskAsyncDispAsync()
    {
        var value = _taskAsyncDisp;
        return value is not null ? new ValueTask<TaskAsyncDisp>(value) : new(SlowTaskAsyncDispAsync());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<TaskAsyncDisp> SlowTaskAsyncDispAsync()
    {
        lock (SingletonGate)
        {
            var value = _taskAsyncDisp;
            return value is not null ? Task.FromResult(value) : _taskAsyncDispInFlight ??= PublishTaskAsyncDispAsync();
        }
    }

    private static async Task<TaskAsyncDisp> PublishTaskAsyncDispAsync()
    {
        var created = await TaskAsyncDisp.CreateAsync().ConfigureAwait(false);
        lock (SingletonGate) _taskAsyncDisp = created;
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
    /// <para>
    /// <b>Los singletons son estaticos, asi que hay que devolverlos a null.</b> Sin eso, el
    /// siguiente contenedor del proceso heredaria instancias ya desechadas y la matriz mediria
    /// objetos muertos. Con eso, la consecuencia es la contraria y tampoco es gratis: disponer un
    /// contenedor afecta a todos los demas del proceso. No es una peculiaridad del banco sino el
    /// precio real de emitir singletons estaticos, y aqui esta a la vista en lugar de escondido.
    /// </para>
    /// <para>
    /// Las tareas en vuelo se limpian con ellos. Si no, un contenedor nuevo encontraria una tarea
    /// ya completada apuntando a una instancia desechada y la entregaria como buena.
    /// </para>
    /// </summary>
    public void Dispose()
    {
        SyncDisp? syncDisp;
        VtDisp? vtDisp;
        TaskDisp? taskDisp;

        lock (SingletonGate)
        {
            (syncDisp, _syncDisp) = (_syncDisp, null);
            (vtDisp, _vtDisp) = (_vtDisp, null);
            (taskDisp, _taskDisp) = (_taskDisp, null);
            _vtDispInFlight = null;
            _taskDispInFlight = null;
        }

        syncDisp?.Dispose();
        vtDisp?.Dispose();
        taskDisp?.Dispose();

        if (_transientDisposables is { } disposables)
        {
            foreach (var disposable in disposables) disposable.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();

        SyncAsyncDisp? syncAsyncDisp;
        VtAsyncDisp? vtAsyncDisp;
        TaskAsyncDisp? taskAsyncDisp;

        lock (SingletonGate)
        {
            (syncAsyncDisp, _syncAsyncDisp) = (_syncAsyncDisp, null);
            (vtAsyncDisp, _vtAsyncDisp) = (_vtAsyncDisp, null);
            (taskAsyncDisp, _taskAsyncDisp) = (_taskAsyncDisp, null);
            _vtAsyncDispInFlight = null;
            _taskAsyncDispInFlight = null;
        }

        if (syncAsyncDisp is not null) await syncAsyncDisp.DisposeAsync().ConfigureAwait(false);
        if (vtAsyncDisp is not null) await vtAsyncDisp.DisposeAsync().ConfigureAwait(false);
        if (taskAsyncDisp is not null) await taskAsyncDisp.DisposeAsync().ConfigureAwait(false);

        if (_transientAsyncDisposables is { } asyncDisposables)
        {
            foreach (var asyncDisposable in asyncDisposables) await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Devuelve los singletons estaticos que <b>no</b> son desechables a su estado inicial.
    /// <para>
    /// No forma parte de la semantica de un contenedor: existe porque los campos estaticos
    /// sobreviven entre casos de prueba, y un caso que herede el singleton construido por el
    /// anterior no esta midiendo ni comprobando lo que cree. <see cref="Dispose"/> ya limpia los
    /// desechables porque tiene que hacerlo; esto limpia el resto.
    /// </para>
    /// </summary>
    internal static void ResetSingletons()
    {
        lock (SingletonGate)
        {
            _syncPlain = null;
            _syncDisp = null;
            _syncAsyncDisp = null;
            _vtPlain = null;
            _vtDisp = null;
            _vtAsyncDisp = null;
            _taskPlain = null;
            _taskDisp = null;
            _taskAsyncDisp = null;
            _vtPlainInFlight = null;
            _vtDispInFlight = null;
            _vtAsyncDispInFlight = null;
            _taskPlainInFlight = null;
            _taskDispInFlight = null;
            _taskAsyncDispInFlight = null;
        }
    }
}

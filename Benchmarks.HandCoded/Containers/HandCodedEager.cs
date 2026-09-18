using System.Runtime.CompilerServices;

namespace Benchmarks.HandCoded;

/// <summary>
/// Contenedor escrito a mano, estrategia <b>eager</b>: los singletons se construyen en el
/// constructor y viven en campos <c>readonly</c>.
/// <para>
/// Es el suelo teorico del camino caliente. Un campo <c>readonly</c> leido desde una propiedad
/// que el JIT inserta en linea se reduce a un <c>mov</c>: sin prueba de nulo, sin salto y sin
/// barrera. Cualquier variante perezosa paga como minimo una lectura volatil, una prueba y un
/// salto sobre eso, por mucho que el predictor lo acierte siempre.
/// </para>
/// <para>
/// <b>Y no hay candado, porque no hay nada que proteger.</b> Un contenedor cuyos campos son
/// todos <c>readonly</c> y se asignan en el constructor es seguro entre hilos por
/// <i>inmutabilidad</i>, no por sincronizacion. Es la razon por la que el eje de locking no se
/// cruza con el eager: no es que la celda "eager sin candado" gane, es que la celda "eager con
/// candado" <b>no existe</b>.
/// </para>
/// <para>
/// <b>El eager no puede ser asincrono.</b> Un constructor no puede esperar, asi que construir
/// una fabrica asincrona de forma adelantada obligaria a un <c>.Result</c>, que es justo el
/// patron que agarrota el grupo de hilos bajo carga. Las seis celdas
/// <c>eager x (ValueTask|Task)</c> no estan omitidas por descuido: son <b>imposibles</b>, y el
/// banco prefiere decirlo a dejar huecos sin explicar.
/// </para>
/// <para>
/// Lo que se paga a cambio esta en la creacion del contenedor: el constructor materializa todo
/// el grafo de singletons aunque la aplicacion no llegue a pedir ninguno. Esa es la comparacion
/// que hace <c>CreationBenchmark</c>, y la que decide cual de las dos estrategias conviene
/// segun si la peticion tipica resuelve algo o no.
/// </para>
/// </summary>
public sealed class EagerContainer : IDisposable, IAsyncDisposable
{
    private readonly SyncPlain _syncPlain;
    private readonly SyncDisp _syncDisp;
    private readonly SyncAsyncDisp _syncAsyncDisp;

    public EagerContainer()
    {
        _syncPlain = new SyncPlain();
        _syncDisp = new SyncDisp();
        _syncAsyncDisp = new SyncAsyncDisp();
    }

    public SyncPlain SingletonSyncPlain
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _syncPlain;
    }

    public SyncDisp SingletonSyncDisp
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _syncDisp;
    }

    public SyncAsyncDisp SingletonSyncAsyncDisp
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _syncAsyncDisp;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SyncPlain TransientSyncPlain() => new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public EagerScope CreateScope() => new(this);

    public void Dispose() => _syncDisp.Dispose();

    public ValueTask DisposeAsync()
    {
        _syncDisp.Dispose();
        return _syncAsyncDisp.DisposeAsync();
    }
}

/// <summary>
/// Ambito eager. Construye sus scoped en el constructor del ambito, que es lo que hace CircleDI
/// y la razon por la que llega al mismo suelo que esta clase en el camino caliente.
/// <para>
/// Tiene una ventaja que no se ve en las tablas de resolucion y si en las de ciclo de ambito:
/// al no tener camino frio, no hay ningun metodo que reciba <c>this</c> para construir en
/// diferido, y por tanto el ambito <b>no escapa</b>. El analisis de escape de .NET 10 puede
/// entonces colocarlo en la pila. Una variante perezosa con camino frio marcado
/// <see cref="MethodImplOptions.NoInlining"/> pierde ese analisis, porque esa llamada hace
/// escapar el ambito: la decision tomada para proteger el camino caliente acaba costando una
/// asignacion por peticion.
/// </para>
/// </summary>
public sealed class EagerScope : IDisposable, IAsyncDisposable
{
    private readonly EagerContainer _root;
    private readonly SyncPlain _syncPlain;
    private readonly SyncDisp _syncDisp;
    private readonly SyncAsyncDisp _syncAsyncDisp;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public EagerScope(EagerContainer root)
    {
        _root = root;
        _syncPlain = new SyncPlain();
        _syncDisp = new SyncDisp();
        _syncAsyncDisp = new SyncAsyncDisp();
    }

    public SyncPlain ScopedSyncPlain
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _syncPlain;
    }

    public SyncDisp ScopedSyncDisp
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _syncDisp;
    }

    public SyncAsyncDisp ScopedSyncAsyncDisp
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _syncAsyncDisp;
    }

    public SyncPlain SingletonSyncPlain
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _root.SingletonSyncPlain;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SyncPlain TransientSyncPlain() => _root.TransientSyncPlain();

    public void Dispose() => _syncDisp.Dispose();

    public ValueTask DisposeAsync()
    {
        _syncDisp.Dispose();
        return _syncAsyncDisp.DisposeAsync();
    }
}

using System.Runtime.CompilerServices;

namespace Benchmarks.HandCoded;

/// <summary>
/// Contenedor perezoso <b>lean</b>: exactamente los tres servicios sincronos que registran los
/// rivales, ni uno mas.
/// <para>
/// <b>Existe para corregir una comparacion injusta que hacia este mismo banco.</b>
/// <see cref="LockedContainer"/> implementa las nueve celdas de la matriz (tres async-kind por
/// tres disposability), asi que su objeto tiene nueve campos de servicio mas los auxiliares. Los
/// contenedores de Jab, CircleDI, Pure.DI y SourceCrafter registran tres. Enfrentarlos en una
/// tabla de creacion o de asignacion de ambito no compara dos implementaciones: compara un objeto
/// de nueve campos contra uno de tres, y el de nueve pierde por definicion.
/// </para>
/// <para>
/// La variante eager si era comparable porque solo tiene los tres sincronos, y por eso era la
/// unica fila del head-to-head que se podia leer. Esta clase da la misma equidad al lado
/// perezoso, que es el que comparten Jab, Pure.DI y SourceCrafter.
/// </para>
/// <para>
/// <b>El ambito desecha por campo y no por lista, y ahi esta el hallazgo.</b> Un ambito que lleva
/// una <see cref="List{T}"/> de desechables paga el objeto de la lista mas su array interno en
/// cuanto resuelve el primer servicio. Pero el contenedor <i>sabe en tiempo de compilacion</i>
/// cuales de sus servicios son desechables: puede desecharlos leyendo sus propios campos y
/// saltando los nulos. Es la razon por la que el ambito de CircleDI ocupa 48 B -- cabecera, tres
/// referencias de servicio y la raiz -- y ni un byte mas. Un generador que emite una lista esta
/// pagando en tiempo de ejecucion por una informacion que ya tenia al generar.
/// </para>
/// </summary>
public sealed class LeanLazyContainer : IDisposable, IAsyncDisposable
{
    private SyncPlain? _syncPlain;
    private SyncDisp? _syncDisp;
    private SyncAsyncDisp? _syncAsyncDisp;

    internal readonly Lock Gate = new();

    public SyncPlain SyncPlain
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref _syncPlain) ?? CreateSyncPlain();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private SyncPlain CreateSyncPlain()
    {
        lock (Gate)
        {
            return _syncPlain ??= new SyncPlain();
        }
    }

    public SyncDisp SyncDisp
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref _syncDisp) ?? CreateSyncDisp();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private SyncDisp CreateSyncDisp()
    {
        lock (Gate)
        {
            return _syncDisp ??= new SyncDisp();
        }
    }

    public SyncAsyncDisp SyncAsyncDisp
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref _syncAsyncDisp) ?? CreateSyncAsyncDisp();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private SyncAsyncDisp CreateSyncAsyncDisp()
    {
        lock (Gate)
        {
            return _syncAsyncDisp ??= new SyncAsyncDisp();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LeanLazyScope CreateScope() => new(this);

    public void Dispose() => _syncDisp?.Dispose();

    public ValueTask DisposeAsync()
    {
        _syncDisp?.Dispose();
        return _syncAsyncDisp?.DisposeAsync() ?? default;
    }
}

/// <summary>
/// Ambito perezoso lean. <b>Cuatro referencias y una cabecera: 48 B, el mismo suelo que
/// CircleDI</b>, pero con semantica perezosa.
/// <para>
/// No tiene candado propio: usa el de la raiz. Un candado por ambito son 40 B mas por peticion
/// para proteger unicamente el camino frio de la primera resolucion, donde la contienda entre
/// ambitos distintos es irrelevante porque cada uno escribe en sus propios campos.
/// </para>
/// <para>
/// No tiene lista de desecho: <see cref="DisposeAsync"/> lee los dos campos que el compilador ya
/// sabe que son desechables y salta los nulos. Un ambito que no resolvio nada no desecha nada y
/// no asigno nada para averiguarlo.
/// </para>
/// </summary>
public sealed class LeanLazyScope(LeanLazyContainer root) : IDisposable, IAsyncDisposable
{
    private readonly LeanLazyContainer _root = root;

    private SyncPlain? _syncPlain;
    private SyncDisp? _syncDisp;
    private SyncAsyncDisp? _syncAsyncDisp;

    public SyncPlain SyncPlain
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref _syncPlain) ?? CreateSyncPlain();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private SyncPlain CreateSyncPlain()
    {
        lock (_root.Gate)
        {
            return _syncPlain ??= new SyncPlain();
        }
    }

    public SyncDisp SyncDisp
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref _syncDisp) ?? CreateSyncDisp();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private SyncDisp CreateSyncDisp()
    {
        lock (_root.Gate)
        {
            return _syncDisp ??= new SyncDisp();
        }
    }

    public SyncAsyncDisp SyncAsyncDisp
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref _syncAsyncDisp) ?? CreateSyncAsyncDisp();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private SyncAsyncDisp CreateSyncAsyncDisp()
    {
        lock (_root.Gate)
        {
            return _syncAsyncDisp ??= new SyncAsyncDisp();
        }
    }

    public void Dispose() => _syncDisp?.Dispose();

    /// <summary>
    /// Camino rapido explicito: si no hay nada asincrono que desechar se devuelve un
    /// <see cref="ValueTask"/> por valor y <b>no se crea maquina de estados</b>. Marcar el metodo
    /// como <c>async</c> por comodidad costaria una asignacion en el caso mayoritario, que es el
    /// ambito que no resolvio nada desechable.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        _syncDisp?.Dispose();
        return _syncAsyncDisp?.DisposeAsync() ?? default;
    }
}

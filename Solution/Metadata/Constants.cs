namespace SourceCrafter.DependencyInjection.Constants;

#if DISG_META
    public
#else
    internal
#endif
    enum Lifetime : byte { Singleton, Scoped, Transient }


#if DISG_META
    public
#else
    internal
#endif
    enum Disposability : byte { None, Disposable, AsyncDisposable }


/// <summary>
/// Alcance del candado que protege la construccion de una dependencia cacheada.
/// </summary>
#if DISG_META
    public
#else
    internal
#endif
    enum LockOptions : byte
{
    /// <summary>Global para <c>Singleton</c>, Instance para <c>Scoped</c>.</summary>
    Default,
    /// <summary>Candado estatico unico del contenedor, compartido por todas sus instancias.</summary>
    Global,
    /// <summary><c>lock(this)</c>: un candado por instancia del contenedor.</summary>
    Instance,
    /// <summary>Un candado propio por dependencia, creado de forma perezosa.</summary>
    Dedicated,
    /// <summary>Sin candado: dos hilos pueden construir a la vez y uno de los valores se descarta.</summary>
    None
}
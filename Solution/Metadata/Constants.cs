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
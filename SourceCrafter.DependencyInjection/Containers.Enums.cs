internal enum Lifetime : byte { Singleton, Scoped, Transient }

internal enum AsyncType : byte { None, ValueTask, Task }

internal enum Disposability : byte { None, Disposable, AsyncDisposable }

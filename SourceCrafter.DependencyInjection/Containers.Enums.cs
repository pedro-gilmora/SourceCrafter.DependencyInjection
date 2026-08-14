internal enum Lifetime : byte { Singleton, Scoped, Transient }

internal enum AsyncKind : byte { None, ValueTask, Task }

internal enum Disposability : byte { None, Disposable, AsyncDisposable }

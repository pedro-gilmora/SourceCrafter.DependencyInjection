
namespace SourceCrafter.DependencyInjection.Interop
{
    public enum Lifetime : byte { Singleton, Scoped, Transient }
    public enum Disposability : byte { None, Disposable, AsyncDisposable }
}

using Pure.DI;

using static Pure.DI.Lifetime;

namespace Benchmarks.HandCoded.Rivals;

/// <summary>
/// Composiciones de <b>Pure.DI</b> sobre el subconjunto sincrono del grafo comun.
/// <para>
/// Pure.DI si permitiria varios lifetimes del mismo tipo usando etiquetas, pero aqui se separan
/// en tres composiciones igual que en las demas librerias. Darle una forma de registro distinta
/// a un solo competidor mediria su modelo de etiquetas, no su rendimiento.
/// </para>
/// </summary>
public partial class PureDiSingletonContainer
{
    private static void Setup() =>
        DI.Setup(nameof(PureDiSingletonContainer))
            .Bind<SyncPlain>().As(Singleton).To<SyncPlain>()
            .Bind<SyncDisp>().As(Singleton).To<SyncDisp>()
            .Bind<SyncAsyncDisp>().As(Singleton).To<SyncAsyncDisp>()
            .Root<SyncPlain>("Plain")
            .Root<SyncDisp>("Disp")
            .Root<SyncAsyncDisp>("AsyncDisp");
}

public partial class PureDiScopedContainer
{
    private static void Setup() =>
        DI.Setup(nameof(PureDiScopedContainer))
            .Bind<SyncPlain>().As(Scoped).To<SyncPlain>()
            .Bind<SyncDisp>().As(Scoped).To<SyncDisp>()
            .Bind<SyncAsyncDisp>().As(Scoped).To<SyncAsyncDisp>()
            .Root<SyncPlain>("Plain")
            .Root<SyncDisp>("Disp")
            .Root<SyncAsyncDisp>("AsyncDisp");
}

public partial class PureDiTransientContainer
{
    private static void Setup() =>
        DI.Setup(nameof(PureDiTransientContainer))
            .Bind<SyncPlain>().To<SyncPlain>()
            .Bind<SyncDisp>().To<SyncDisp>()
            .Bind<SyncAsyncDisp>().To<SyncAsyncDisp>()
            .Root<SyncPlain>("Plain")
            .Root<SyncDisp>("Disp")
            .Root<SyncAsyncDisp>("AsyncDisp");
}

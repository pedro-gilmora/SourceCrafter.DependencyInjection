using Pure.DI;

using static Pure.DI.Lifetime;

namespace Benchmarks;

/// <summary>
/// Composicion de Pure.DI sobre el grafo comun de <c>Services.cs</c>.
/// </summary>
public partial class PureDiContainer
{
    private static void Setup() =>
        DI.Setup(nameof(PureDiContainer))
            .Bind<ISettings>().To<Settings>()
            .Bind<IDatabase>().As(Singleton).To<Database>()
            .Bind<ISession>().As(Scoped).To<Session>()
            .Bind<Leaf>().To<Leaf>()
            .Bind<Level3>().To<Level3>()
            .Bind<Level2>().To<Level2>()
            .Bind<Level1>().To<Level1>()
            .Root<ISettings>("Settings")
            .Root<IDatabase>("Database")
            .Root<ISession>("Session")
            .Root<Level3>("Transient")
            .Root<Level1>("Complex");
}

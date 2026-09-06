using SourceCrafter.DependencyInjection.Attributes;

namespace Benchmarks;

/// <summary>
/// Contenedor de SourceCrafter.DependencyInjection sobre el grafo comun de <c>Services.cs</c>.
/// </summary>
[ServiceContainer]
[Transient<ISettings, Settings>]
[Singleton<IDatabase, Database>]
[Scoped<ISession, Session>]
[Transient<Leaf>]
[Transient<Level3>]
[Transient<Level2>]
[Transient<Level1>]
public partial class SourceCrafterContainer;

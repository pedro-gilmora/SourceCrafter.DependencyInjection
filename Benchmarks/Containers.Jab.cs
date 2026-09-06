using Jab;

namespace Benchmarks;

/// <summary>
/// Contenedor de Jab sobre el grafo comun de <c>Services.cs</c>.
/// </summary>
[ServiceProvider]
[Transient<ISettings, Settings>]
[Singleton<IDatabase, Database>]
[Scoped<ISession, Session>]
[Transient<Leaf>]
[Transient<Level3>]
[Transient<Level2>]
[Transient<Level1>]
public sealed partial class JabContainer;

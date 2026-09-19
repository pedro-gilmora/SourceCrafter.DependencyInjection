namespace Benchmarks;

/// <summary>
/// Contenedor de CircleDI sobre el grafo comun de <c>Services.cs</c>.
/// <para>
/// Los atributos van completamente cualificados porque Jab, CircleDI y el generador propio
/// declaran todos un <c>ServiceProvider</c> y un <c>Singleton&lt;,&gt;</c>: sin cualificar,
/// el que gana depende del orden de los <c>using</c>.
/// </para>
/// </summary>
[CircleDIAttributes.ServiceProvider]
[CircleDIAttributes.Transient<ISettings, Settings>]
[CircleDIAttributes.Singleton<IDatabase, Database>]
[CircleDIAttributes.Scoped<ISession, Session>]
[CircleDIAttributes.Transient<Leaf>]
[CircleDIAttributes.Transient<Level3>]
[CircleDIAttributes.Transient<Level2>]
[CircleDIAttributes.Transient<Level1>]
public sealed partial class CircleDiContainer;

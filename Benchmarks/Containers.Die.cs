using MrMeeseeks.DIE.Configuration.Attributes;

namespace Benchmarks;

/// <summary>
/// Contenedor de MrMeeseeks.DIE sobre el grafo comun de <c>Services.cs</c>.
/// </summary>
[ImplementationAggregation(typeof(Settings))]
[ImplementationAggregation(typeof(Database))]
[ImplementationAggregation(typeof(Session))]
[ImplementationAggregation(typeof(Leaf))]
[ImplementationAggregation(typeof(Level3))]
[ImplementationAggregation(typeof(Level2))]
[ImplementationAggregation(typeof(Level1))]
[CreateFunction(typeof(IDatabase), "GetDatabase")]
[CreateFunction(typeof(Level3), "GetTransient")]
[CreateFunction(typeof(Level1), "GetComplex")]
public sealed partial class DieContainer;

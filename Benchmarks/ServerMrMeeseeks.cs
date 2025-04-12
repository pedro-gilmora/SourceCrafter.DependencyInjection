using Benchmarks;

using MrMeeseeks.DIE.Configuration.Attributes;

namespace Benchmarks;

[ImplementationAggregation(typeof(AppSettings))]
[ImplementationAggregation(typeof(Database))]
[ImplementationAggregation(typeof(AuthService))]
[CreateFunction(typeof(AuthService), "GetAuthService")]
public sealed partial class ServerMrMeeseeks
{
    internal static ValueTask<int> ResolveAsync(CancellationToken _)
    {
        return ValueTask.FromResult(1);
    }
}
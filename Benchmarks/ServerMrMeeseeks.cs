using Benchmarks;

using Microsoft.Extensions.Logging;

using MrMeeseeks.DIE.Configuration.Attributes;

namespace GettingStarted
{
    [TransientImplementationAggregation(typeof(AppSettings))]
    [ScopeInstanceImplementationAggregation(typeof(Database))]
    [ImplementationAggregation(typeof(AuthService))]
    [ImplementationAggregation(typeof(ServerMrMeeseeks))]
    [CreateFunction(typeof(ServerMrMeeseeks), "Create")]

    public sealed partial class ServerMrMeeseeks
    {
        internal static ValueTask<int> ResolveAsync(CancellationToken _)
        {
            return ValueTask.FromResult(1);
        }
    }
}
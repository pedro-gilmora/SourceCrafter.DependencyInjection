using Benchmarks;

using SourceCrafter.DependencyInjection.Attributes;

namespace SourceCrafter.DependencyInjection.Tests
{
    [ServiceContainer]
    [Transient<AppSettings>]
    [Singleton<Database>]
    [Scoped<AuthService>]
    public partial class ServerSCDI
    {
        internal static ValueTask<int> ResolveAsync(CancellationToken _)
        {
            return ValueTask.FromResult(1);
        }
    }
}

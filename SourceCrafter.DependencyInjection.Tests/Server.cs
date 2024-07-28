using SourceCrafter.DependencyInjection.Attributes;
using SourceCrafter.DependencyInjection.MsConfiguration.Metadata;

namespace SourceCrafter.DependencyInjection.Tests
{
    public enum GlobalScope { Identity, Application }

    [ServiceContainer]
    [Singleton<Configuration>(factoryOrInstance: nameof(BuildConfiguration))]
    [Singleton<IDatabase, Database>(GlobalScope.Identity)]
    [Scoped<IDatabase, Database>(GlobalScope.Application)]
    [Scoped<AuthService>]
    [Transient<int>(GlobalScope.Application, nameof(ResolveAsync))]
    [Transient<string>(GlobalScope.Application, nameof(Name))]
    public sealed partial class Server
    {
        const string Name = "Server::Name";

        static Configuration BuildConfiguration()
        {
            return default!;
        }
        static ValueTask<int> ResolveAsync(CancellationToken _)
        {
            return ValueTask.FromResult(1);
        }
    }

    public class AuthService([Singleton(GlobalScope.Identity)] IDatabase application, [Transient(GlobalScope.Application)] int o) : global::System.IDisposable
    {
        public IDatabase Database { get; } = application;

        public void Dispose()
        {

        }
    }

    public class Database([Setting("AppSettings")] AppSettings config) : IDatabase, global::System.IAsyncDisposable
    {
        AppSettings config = config;

        public void SaveFrom(out string setting1)
        {
            setting1 = config.Setting1;
        }

        public ValueTask DisposeAsync()
        {
            return default;
        }
    }

    public interface IDatabase
    {
        void SaveFrom(out string setting1);
    }

    public class Configuration
    {

    }
}

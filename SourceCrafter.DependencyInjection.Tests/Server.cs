using SourceCrafter.DependencyInjection.Attributes;
using SourceCrafter.DependencyInjection.MsConfiguration.Metadata;

namespace SourceCrafter.DependencyInjection.Tests
{
    [ServiceContainer]
    [Transient<Configuration>]
    [Singleton<IDatabase, Database>(name: "identity")]
    [Scoped<IDatabase, Database>(name: "company")]
    [Scoped(typeof(AuthService))]
    public sealed partial class Server
    {
        static Configuration BuildConfiguration()
        {
            return default!;
        }
    }

    public class AuthService([NamedService("identity")] IDatabase db) : global::System.IDisposable
    {
        public IDatabase Database { get; } = db;

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

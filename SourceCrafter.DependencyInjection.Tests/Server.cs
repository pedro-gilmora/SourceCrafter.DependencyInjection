using SourceCrafter.DependencyInjection.Attributes;
using SourceCrafter.DependencyInjection.MsConfiguration.Metadata;

namespace SourceCrafter.DependencyInjection.Tests
{
    public enum Main { Identity, App }

    [ServiceContainer]
    [Singleton<Configuration>(factoryOrInstance: nameof(BuildConfiguration))]
    [Singleton<IDatabase, Database>]
    [Scoped<IDatabase, Database>(Main.App)]
    [Scoped<AuthService>]
    [Transient<int>(Main.App, nameof(ResolveAsync))]
    [Transient<string>(Main.App, nameof(Name))]
    public partial class Server
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

    public class AuthService([Singleton] IDatabase application, [Transient(Main.App)] int o) : global::System.IDisposable
    {
        public IDatabase Database { get; } = application;

        public void Dispose()
        {

        }
    }

    public class Database([JsonSetting("AppSettings")] AppSettings config) : IDatabase, global::System.IAsyncDisposable
    {
        //AppSettings config = config;

        public void TrySave(out string setting1)
        {
            setting1 = config?.Setting1 ?? "Value3"/*config.Setting1*/;
        }

        public ValueTask DisposeAsync()
        {
            return default;
        }
    }

    public interface IDatabase
    {
        void TrySave(out string setting1);
    }

    public class Configuration
    {

    }

    public class AppSettings
    {
        public string Setting1 { get; set; }
        public string Setting2 { get; set; }
    }
}

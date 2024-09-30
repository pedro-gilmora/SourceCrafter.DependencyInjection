using Microsoft.Extensions.Configuration;

using SourceCrafter.DependencyInjection.Attributes;
using SourceCrafter.DependencyInjection.MsConfiguration.Metadata;

using System.Text.RegularExpressions;

[assembly: JsonConfiguration]

namespace SourceCrafter.DependencyInjection.Tests
{
    [ServiceContainer]
    [JsonConfiguration]
    //[Singleton<Configuration>(factoryOrInstance: nameof(BuildConfiguration))]
    [Singleton<IDatabase, Database>]
    //[Scoped<IDatabase, Database>(Main.App)]
    [Scoped<AuthService>]
    [Transient<int>("Count", nameof(ResolveAsync))]
    //[Transient<string>(Main.App, nameof(Name))]
    public partial class Server
    {
        //const string Name = "Server::Name";

        //static Configuration BuildConfiguration()
        //{
        //    return default!;
        //}
        static ValueTask<int> ResolveAsync(CancellationToken _)
        {
            return ValueTask.FromResult(1);
        }
    }

    public class AuthService([Singleton] IDatabase application, [Transient("Count")] int o) :IDisposable
    {
        public int O => o;
        public IDatabase Database { get; } = application;

        public void Dispose()
        {
            //Continue with HostEnvironment
        }
    }

    public class Database([JsonSetting("AppSettings")] AppSettings config) : IDatabase, IAsyncDisposable
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

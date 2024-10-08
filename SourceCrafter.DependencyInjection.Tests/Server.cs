using Microsoft.Extensions.Configuration;

using SourceCrafter.DependencyInjection.Attributes;
using SourceCrafter.DependencyInjection.MsConfiguration.Metadata;

using System.Text.RegularExpressions;

[assembly: JsonConfiguration]

namespace SourceCrafter.DependencyInjection.Tests
{
    [ServiceContainer]
    [JsonSetting<AppSettings>("AppSettings")]
    [JsonSetting<string>("ConnectionStrings::DefaultConnection", nameFormat: "GetConnectionString")]
    //[JsonConfiguration]
    ////[Singleton<Configuration>(factoryOrInstance: nameof(BuildConfiguration))]
    [Transient<int>("count", nameof(ResolveAsync))]
    [Singleton<IDatabase, Database>]
    //[Scoped<IDatabase, Database>(Main.App)]
    [Scoped<IAuthService, AuthService>]
    //[Transient<string>(Main.App, nameof(Name))]
    public partial class Server
    {
        //const string Name = "Server::Name";

        //static Configuration BuildConfiguration()
        //{
        //    return default!;
        //}
        internal static ValueTask<int> ResolveAsync(CancellationToken _)
        {
            return ValueTask.FromResult(1);
        }
    }

    public class AuthService(IDatabase application, int count) : IAuthService//, IDisposable
    {
        public int O => count;
        public IDatabase Database { get; } = application;

        public void Dispose()
        {
            //Continue with HostEnvironment
        }
    }

    public interface IAuthService
    {
        IDatabase Database { get; }
    }

    public class Database(AppSettings settings,string connection) : IDatabase//, IAsyncDisposable
    {
        //AppSettings config = config;

        public void TrySave(out string setting1)
        {
            setting1 = settings?.Setting1 ?? "Value3"/*config.Setting1*/;
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

using SourceCrafter.DependencyInjection.Attributes;
using SourceCrafter.DependencyInjection.MsConfiguration.Metadata;

[assembly: JsonConfiguration]

namespace SourceCrafter.DependencyInjection.Tests
{
    [ServiceContainer(EnvName)]
    [JsonSetting<AppSettings>("AppSettings")]
    [Transient("count", source: nameof(CountAsync))]
    [Scoped("reqId", source: nameof(ResolveRequestIdTask))]
    [Scoped("finalCount", source: nameof(GetCount))]
    [Singleton<IDatabase, Database>]
    [Scoped<IAuthService, AuthService>]
    [Scoped<EmployeeController>]
    public interface IServer : IServiceProvider
    {
        internal const string EnvName = "ASPNETCORE_ENVIRONMENT";
        static Task<int> CountAsync(Server _) => Task.FromResult(1);

        static ValueTask<Guid> ResolveRequestIdTask => new(Guid.NewGuid());

        static int GetCount(int count, [Root] Server _) => count;
    }

    #region TestType

    public class AuthService(IDatabase application, int count) : IAuthService
    {
        public IDatabase Database { get; } = application;

        public ValueTask DisposeAsync()
        {
            return default;
        }

        public void Dispose()
        {

        }
    }

    public interface IAuthService : IAsyncDisposable
    {
        IDatabase Database { get; }
    }

    public class Database(AppSettings settings, Guid reqId) : IDatabase
    {
        public void TrySave(out string setting1)
        {
            setting1 = "Value3"/*config.Setting1*/;
        }

        public ValueTask DisposeAsync()
        {
            return default;
        }

        public void Dispose()
        {

        }
    }

    public interface IDatabase// : IAsyncDisposable
    {
        void TrySave(out string setting1);
    }

    public class EmployeeController(IAuthService authService, IDatabase application, int count, Guid reqId);

    public class AppSettings

    {
        public string Setting1 { get; set; }
        public string Setting2 { get; set; }
    }


    #endregion
}

using SourceCrafter.DependencyInjection.Attributes;
using SourceCrafter.DependencyInjection.MsConfiguration.Metadata;


namespace SourceCrafter.DependencyInjection.Tests
{
    [ServiceContainer("ASPNETCORE_ENVIRONMENT")]
    [JsonConfiguration]
    [Transient<IA, A>]
    [Singleton<IA, AA>]
    [Singleton<B>]
    [JsonSetting<AppSettings>("AppSettings")]
    [Scoped("count", source: nameof(GetCountAsync))]
    [Singleton("reqId", source: nameof(ResolveRequestIdTask2))]
    [Scoped("finalCount", source: nameof(GetCount))]
    [Singleton<IDatabase, Database>]
    [Scoped<IAuthService, AuthService>]
    [Transient(impl:typeof(EmployeeController))]
    public partial class Server : IServiceProvider
    {
        static Task<int> GetCountAsync(Server _, CancellationToken token) => Task.FromResult(1);

        static ValueTask<Guid> ResolveRequestIdTask2 => new(Guid.NewGuid());

        static int GetCount(int count, [Root] Server _) => count;
    }

    #region TestType

    public interface IA;
    public record A : IA;
    public record AA : IA;
    public record B(IA[] IAs);

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
            setting1 = settings.Setting1;
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



namespace SourceCrafter.DependencyInjection.Tests.Sub
{
    [ServiceContainer("DOTNET_ENVIRONMENT")]
    [JsonConfiguration]
    [JsonSetting<AppSettings>("AppConfig")]
    [Scoped("times", source: nameof(IConfigModule.GetCountAsync))]
    [Singleton("serverId", source: nameof(IConfigModule.ServerRequestId))]
    [Scoped("counter", source: nameof(IConfigModule.GetCount))]
    //[Singleton(iface:typeof(IList<>), impl: typeof(List<>))]
    [Singleton<IDatabase, Database>]
    [Scoped<IAuthService, AuthService>]
    [Transient<EmployeeController>]
    public interface IServer;

    public interface IConfigModule
    {
        static ValueTask<int> GetCountAsync(IServer _, CancellationToken token) => new(1);

        static Guid ServerRequestId => Guid.NewGuid();

        static int GetCount(int times, [Root] IServer _) => times;
    }
    public class AuthService(IDatabase application, int times) : IAuthService
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

    public class Database(AppSettings settings, Guid serverId) : IDatabase
    {
        public void TrySave(out string setting1)
        {
            setting1 = settings.Setting1;
        }

        public ValueTask DisposeAsync()
        {
            return default;
        }

        public void Dispose()
        {

        }
    }

    public interface IDatabase : IAsyncDisposable
    {
        void TrySave(out string setting1);
    }

    public class EmployeeController(IAuthService authService, IDatabase application, int counter, Guid serverId);

    public class AppSettings

    {
        public string Setting1 { get; set; }
        public string Setting2 { get; set; }
    }
}

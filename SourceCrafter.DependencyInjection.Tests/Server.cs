using SourceCrafter.DependencyInjection.Attributes;
using SourceCrafter.DependencyInjection.MsConfiguration.Metadata;


namespace SourceCrafter.DependencyInjection.Tests
{
    [ServiceProvider("ASPNETCORE_ENVIRONMENT", genericApi: true)]
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
    [Transient(source: nameof(_CreateLogger))]
    [Transient<AuditLog>]
    public partial class Server : IServiceProvider
    {
        static Task<int> GetCountAsync(Server _, CancellationToken token) => Task.FromResult(1);

        static ValueTask<Guid> ResolveRequestIdTask2 => new(Guid.NewGuid());

        static int GetCount(int count, [Root] Server _) => count;

        private static ILogger<T> _CreateLogger<T>() where T : class => new Logger<T>();
    }

    #region TestType

    public interface IA;
    public record A : IA;
    public record AA : IA;
    public record B(IA[] IAs);

    public class AuthService(IDatabase application, int count) : IAuthService
    {
        public IDatabase Database { get; } = application;

        public int Count { get; } = count;

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
        public Guid RequestId { get; } = reqId;

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

    public class EmployeeController(IAuthService authService, IDatabase application, int count, Guid reqId)
    {
        public IAuthService AuthService { get; } = authService;
        public IDatabase Database { get; } = application;
        public int Count { get; } = count;
        public Guid RequestId { get; } = reqId;
    }

    public interface ILogger<T>
    {
        void Log(string message);
    }

    public sealed class Logger<T> : ILogger<T>
    {
        public void Log(string message) { }
    }

    public class AuditLog(ILogger<AuditLog> logger)
    {
        public ILogger<AuditLog> Logger { get; } = logger;
    }

    public class AppSettings

    {
        public string Setting1 { get; set; } = string.Empty;
        public string Setting2 { get; set; } = string.Empty;
    }


    #endregion
}



namespace SourceCrafter.DependencyInjection.Tests.Sub
{
    [ServiceProvider("DOTNET_ENVIRONMENT")]
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

        public int Times { get; } = times;

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
        public Guid ServerId { get; } = serverId;

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

    public class EmployeeController(IAuthService authService, IDatabase application, int counter, Guid serverId)
    {
        public IAuthService AuthService { get; } = authService;
        public IDatabase Database { get; } = application;
        public int Counter { get; } = counter;
        public Guid ServerId { get; } = serverId;
    }

    public class AppSettings

    {
        public string Setting1 { get; set; } = string.Empty;
        public string Setting2 { get; set; } = string.Empty;
    }
}

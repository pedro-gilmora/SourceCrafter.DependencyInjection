using SourceCrafter.DependencyInjection.Attributes;
using SourceCrafter.DependencyInjection.MsConfiguration.Metadata;

[assembly: JsonConfiguration]

namespace SourceCrafter.DependencyInjection.Tests
{
    [ServiceContainer]
    [Transient<AppSettings>]
    [Scoped("count", source: nameof(CountAsync))]
    [Scoped("reqId", source: nameof(ResolveRequestIdTask))]
    [Singleton<IDatabase, Database>]
    [Scoped<IAuthService, AuthService>]
    [Scoped<EmployeeController>]
    public partial class Server
    {
        static Task<int> CountAsync() => Task.FromResult(1);
        static ValueTask<Guid> ResolveRequestIdTask => new(Guid.NewGuid());
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

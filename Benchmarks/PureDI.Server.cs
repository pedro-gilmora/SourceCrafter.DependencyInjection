using Pure.DI;

namespace PureDI.Tests
{
    
    public sealed partial class Server();

    public class AuthService(Database application) : IAuthService//, IDisposable
    {
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

    public class Database(AppSettings settings) : IDatabase, IAsyncDisposable
    {
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

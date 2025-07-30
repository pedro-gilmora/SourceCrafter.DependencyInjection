using SourceCrafter.DependencyInjection.Attributes;
using SourceCrafter.DependencyInjection.MsConfiguration.Metadata;

[assembly: JsonConfiguration]

namespace SourceCrafter.DependencyInjection.Tests
{
    [ServiceContainer]
    [JsonSetting<AppSettings>("AppSettings")]
    [JsonSetting<string>("ConnectionStrings::DefaultConnection", nameFormat: "GetConnectionString")]
    [Transient("count", source: nameof(ResolveAsync))]
    [Scoped<IAuthService, AuthService>]
    [Transient<EmployeeService>]
    public sealed partial class Server
    {
        private static ValueTask<int> ResolveAsync(CancellationToken _)
        {
            return ValueTask.FromResult(1);
        }
    }

    public class AuthService([Singleton<Database>] IDatabase appDb, int count) : IAuthService
    {
        public int O => count;
        public IDatabase Database { get; } = appDb;

        public virtual void Dispose()
        {
            //Continue with HostEnvironment
        }
    }

    public interface IAuthService : IDisposable
    {
        IDatabase Database { get; }
    }

#pragma warning disable CS9113 // Parameter is unread.
    public class EmployeeService(IAuthService authService, IDatabase employeesDb) : IDisposable
#pragma warning restore CS9113 // Parameter is unread.
    {
        private bool disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~EmployeeService()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            authService.Dispose(); 
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

#pragma warning disable CS9113 // Parameter is unread.
    public class Database(AppSettings settings, string connection) : IDatabase
#pragma warning restore CS9113 // Parameter is unread.
    {
        //AppSettings config = config;

        public void TrySave(out string setting1)
        {
            setting1 = settings?.Setting1 ?? "Value3"/*config.Setting1*/;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    public interface IDatabase : IAsyncDisposable
    {
        void TrySave(out string setting1);
    }

    public class Configuration
    {

    }

    public class AppSettings
    {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        public string Setting1 { get; set; }
        public string Setting2 { get; set; }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    }
}

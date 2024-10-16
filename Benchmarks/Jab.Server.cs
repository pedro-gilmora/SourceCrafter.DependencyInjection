﻿namespace Jab.Tests
{
    [ServiceProvider]
    [Transient<AppSettings>]
    [Singleton<IDatabase, Database>]
    [Scoped<IAuthService, AuthService>]
    public sealed partial class ServerJab;

    public class AuthService(IDatabase application) : IAuthService//, IDisposable
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

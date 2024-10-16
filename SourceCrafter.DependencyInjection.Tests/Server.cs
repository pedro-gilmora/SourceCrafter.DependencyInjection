﻿using Microsoft.Extensions.Configuration;

using SourceCrafter.DependencyInjection.Attributes;
using SourceCrafter.DependencyInjection.MsConfiguration.Metadata;

using System.Text.RegularExpressions;

[assembly: JsonConfiguration]

namespace SourceCrafter.DependencyInjection.Tests
{
    [ServiceContainer]
    [JsonSetting<AppSettings>("AppSettings")]
    [JsonSetting<string>("ConnectionStrings::DefaultConnection", nameFormat: "GetConnectionString")]
    [Transient<int>("count", nameof(ResolveAsync))]
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

    public interface IAuthService //: IDisposable
    {
        IDatabase Database { get; }
    }

    public class EmployeeService(IAuthService authService);

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
            return default;
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

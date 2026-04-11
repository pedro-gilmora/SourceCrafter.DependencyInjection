

//using Microsoft.Extensions.Configuration;

//using System.Runtime.CompilerServices;
//using System.Text;

//#pragma warning disable IDE0130 // Namespace does not match folder structure
//namespace ManualTests;
//#pragma warning restore IDE0130 // Namespace does not match folder structure

//[global::System.CodeDom.Compiler.GeneratedCode("SourceCrafter.DependencyInjection", "1.25.217.41")]
//public partial class Server3 : IAsyncDisposable
//{
//    // -------- Constantes/paths precomputados (evita strings intermedias) --------
//    public static string Environment => System.Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Development";


//    private static Lock? _singletonLocker;

//    private static IConfiguration? _configuration;
//    private static IConfiguration Configuration
//    {
//        get
//        {
//            if (_configuration is { }) return _configuration;

//            lock (_singletonLocker ??= new())
//            {
//                if (_configuration is not null) return _configuration;

//                //var fileBasePath = System.IO.Path.GetFullPath("appsettings");

//                _configuration = new ConfigurationBuilder()
//                        //.AddJsonFile($"{fileBasePath}.{Environment}.json", optional: true, reloadOnChange: true)
//                        //.AddJsonFile($"{fileBasePath}.json", optional: true, reloadOnChange: true)
//                        .Build();

//                return _configuration;
//            }
//        }
//    }


//    private static AppSettings? _settings;
//    private static AppSettings Settings
//    {
//        get
//        {
//            if (_settings is not null) return _settings;

//            lock (_singletonLocker ??= new())
//            {
//                if (_settings is not null) return _settings;

//                Configuration.GetSection("AppSettings").Bind(_settings = new());

//                return _settings;
//            }
//        }
//    }

//    private static string ConnectionString => Configuration.GetValue<string>("ConnectionStrings::DefaultConnection")!;

//    private static Database? _database;
//    public IDatabase Database
//    {
//        get
//        {
//            if (_database is not null) return _database;

//            lock (_singletonLocker ??= new())
//            {
//                if (_database is not null) return _database;

//                return _database = new(Settings, ConnectionString);
//            }
//        }
//    }


//    private static global::System.Threading.CancellationTokenSource __cancellationTokenSrc = new();

//    public Scoped CreateScope() => new();

//    [MethodImpl(MethodImplOptions.AggressiveInlining)]
//    private static ValueTask<int> ResolveAsync(CancellationToken c) =>
//        new(1);

//    private Task<IAuthService>? _authTask;

//    private readonly global::System.Threading.CancellationTokenSource __scopedCancellationTokenSrc;

//    public Server3()
//    {
//        __scopedCancellationTokenSrc = new();
//    }

//    private Lock? _scopedLocker;

//    private Task<IAuthService> GetAuthServiceAsync(CancellationToken ct = default)
//    {
//        if (_authTask is not null) return _authTask;

//        lock (_scopedLocker ??= new())
//        {
//            if (_authTask is not null) return _authTask;

//            ct = CancellationTokenSource.CreateLinkedTokenSource(ct, __scopedCancellationTokenSrc.Token).Token;

//            var v = ResolveAsync(ct);

//            return _authTask = v.IsCompletedSuccessfully
//                ? Task.FromResult<IAuthService>(new AuthService(Database, v.Result))
//                : Awaited();

//            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
//            async Task<IAuthService> Awaited()
//            {
//                ct.ThrowIfCancellationRequested();
//                //"Creating2".Dump();
//                return ct.IsCancellationRequested ? default! : new AuthService(Database, await v.ConfigureAwait(false));
//            }
//        }
//    }

//    public sealed class Scoped : Server3
//    {
//        [MethodImpl(MethodImplOptions.AggressiveInlining)]
//        public new Task<IAuthService> GetAuthServiceAsync(CancellationToken ct = default) => base.GetAuthServiceAsync(ct);
//        public override ValueTask DisposeAsync() => DisposeAsyncScoped();
//    }

//    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
//    public async ValueTask<EmployeeService> GetEmployeeServiceAsync(CancellationToken ct = default)
//    {
//        return new EmployeeService(await GetAuthServiceAsync(ct).ConfigureAwait(false), Database);
//    }

//    public virtual async ValueTask DisposeAsync()
//    {
//        __cancellationTokenSrc.Cancel();
//        //__scopedCancellationTokenSrc.Cancel();
//        // Si creaste el auth en este scope y es IDisposable, libéralo
//        if (_database is not null) await _database.DisposeAsync().ConfigureAwait(false);
//        await DisposeAsyncScoped();
//    }

//    [MethodImpl(MethodImplOptions.AggressiveInlining)]
//    async ValueTask DisposeAsyncScoped()
//    {
//        if (_authTask is not null)
//        {
//            (await _authTask.ConfigureAwait(false))?.Dispose();
//            _authTask.Dispose();
//        }
//    }
//}

//[global::System.CodeDom.Compiler.GeneratedCode("SourceCrafter.DependencyInjection", "1.25.217.41")]
//public partial class Server4 : IAsyncDisposable
//{
//    // -------- Constantes/paths precomputados (evita strings intermedias) --------
//    public static string Environment => System.Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Development";

//    private static IConfiguration? _configuration;
//    private static AppSettings? _settings;
//    private static Database? _database;

//    private static Lock? _singletonLocker;

//    private static IConfiguration Configuration
//    {
//        get
//        {
//            if (global::System.Threading.Volatile.Read(ref _configuration) is { } c) return c;

//            lock (_singletonLocker ??= new())
//            {
//                if ((c = _configuration) is not null) return c;

//                var fileBasePath = System.IO.Path.GetFullPath("appsettings");

//                global::System.Threading.Volatile.Write(ref _configuration, c = new ConfigurationBuilder()
//                        //.AddJsonFile($"{fileBasePath}.{Environment}.json", optional: true, reloadOnChange: true)
//                        //.AddJsonFile($"{fileBasePath}.json", optional: true, reloadOnChange: true)
//                        .Build());

//                return c;
//            }
//        }
//    }

//    private static AppSettings Settings
//    {
//        get
//        {
//            if (global::System.Threading.Volatile.Read(ref _settings) is { } s) return s;

//            lock (_singletonLocker ??= new())
//            {
//                if ((s = _settings) is not null) return s;

//                Configuration.GetSection("AppSettings").Bind(s = new());

//                global::System.Threading.Volatile.Write(ref _settings, s);

//                return s;
//            }
//        }
//    }

//    private static string ConnectionString => Configuration.GetValue<string>("ConnectionStrings::DefaultConnection")!;

//    public IDatabase Database
//    {
//        get
//        {
//            if (global::System.Threading.Volatile.Read(ref _database) is { } db) return db;

//            lock (_singletonLocker ??= new())
//            {
//                if ((db = _database) is not null) return db;

//                global::System.Threading.Volatile.Write(ref _database, db = new(Settings, ConnectionString));

//                return db;
//            }
//        }
//    }

//    private static global::System.Threading.CancellationTokenSource __cancellationTokenSrc = null!;

//    public Server4()
//    {
//        __cancellationTokenSrc ??= new();
//    }

//    public Scoped CreateScope() => new();

//    [MethodImpl(MethodImplOptions.AggressiveInlining)]
//    private static ValueTask<int> ResolveAsync(CancellationToken c) =>
//        new(1);

//    private Lock? _scopedLocker; // 1 sola alocación por proceso

//    private Task<IAuthService>? _authTask;
//    private Task<IAuthService> GetAuthServiceAsync(CancellationToken ct = default)
//    {
//        if (global::System.Threading.Volatile.Read(ref _authTask) is { } auth) return auth;

//        lock (_scopedLocker ??= new())
//        {
//            if ((auth = _authTask) is not null) return auth;

//            ct = CancellationTokenSource.CreateLinkedTokenSource(ct, __scopedCancellationTokenSrc.Token).Token;

//            var v = ResolveAsync(ct);

//            global::System.Threading.Volatile.Write(ref _authTask, auth = v.IsCompletedSuccessfully
//                ? Task.FromResult<IAuthService>(new AuthService(Database, v.Result))
//                : Awaited());

//            return auth;

//            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
//            async Task<IAuthService> Awaited()
//            {
//                //"Creating2".Dump();
//                return ct.IsCancellationRequested ? default! : new AuthService(Database, await v.ConfigureAwait(false));
//            }
//        }
//    }

//    private global::System.Threading.CancellationTokenSource __scopedCancellationTokenSrc = new();
//    public sealed class Scoped : Server4
//    {
//        [MethodImpl(MethodImplOptions.AggressiveInlining)]
//        public new Task<IAuthService> GetAuthServiceAsync(CancellationToken ct = default) => base.GetAuthServiceAsync(ct);
//        public override async ValueTask DisposeAsync()
//        {
//            //__scopedCancellationTokenSrc.Cancel();
//            // Si creaste el auth en este scope y es IDisposable, libéralo
//            if (global::System.Threading.Volatile.Read(ref _authTask) is { } auth)
//            {
//                (await auth.ConfigureAwait(false))?.Dispose();
//                auth.Dispose();
//            }
//        }
//    }

//    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
//    public async ValueTask<EmployeeService> GetEmployeeServiceAsync(CancellationToken ct = default)
//    {
//        return new EmployeeService(await GetAuthServiceAsync(ct).ConfigureAwait(false), Database);
//    }

//    public virtual async ValueTask DisposeAsync()
//    {
//        __cancellationTokenSrc.Cancel();

//        if (global::System.Threading.Volatile.Read(ref _database) is { } db) await db.DisposeAsync().ConfigureAwait(false);
//    }
//}


//[global::System.CodeDom.Compiler.GeneratedCode("SourceCrafter.DependencyInjection", "1.25.217.41")]
//public partial class ManualSCDI : IDisposable
//{
//    private static Jab.Tests.AppSettings? _settings;
//    private Jab.Tests.AppSettings Settings =>
//        GetOrCreate(ref _settings, [MethodImpl(MethodImplOptions.AggressiveInlining)] () => new());

//    private static Jab.Tests.Database? _database;
//    public Jab.Tests.IDatabase Database =>
//        GetOrCreate(ref _database, [MethodImpl(MethodImplOptions.AggressiveInlining)] () => new(Settings));

//    public Scope CreateScope() => new();

//    public class Scope : ManualSCDI
//    {
//        public new Jab.Tests.IAuthService AuthService => base.AuthService;
//        public override void Dispose() => _authService?.Dispose();

//    }

//    private Jab.Tests.AuthService? _authService;
//    private Jab.Tests.IAuthService AuthService => GetOrCreate(ref _authService, [MethodImpl(MethodImplOptions.AggressiveInlining)] () => new (Database));

//    public virtual void Dispose()
//    {
//        TryDispose(ref _database);
//        TryDispose(ref _authService);
//    }

//    [MethodImpl(MethodImplOptions.AggressiveInlining)]
//    T GetOrCreate<T>(ref T? field, Func<T> newValue) where T : class
//    {
//        if (Volatile.Read(ref field) is { } local) return local;
//        lock (this)
//        {
//            if ((local = field) is not null) return local;
//            Volatile.Write(ref field, local = newValue());
//            return local;
//        }
//    }

//    [MethodImpl(MethodImplOptions.AggressiveInlining)]
//    static void TryDispose<T>(ref T? field) where T : class, IDisposable 
//    {
//        if (Volatile.Read(ref field) is { } local) local.Dispose();
//    }
//}
//public class AuthService(IDatabase application, int count) : IAuthService
//{
//    public IDatabase Database { get; } = application;
//    public int Count { get; } = count;

//    public void Dispose()
//    {
//    }

//    internal void Test()
//    {
//        Database.TrySave(out _, out _);
//    }
//}

//public interface IAuthService : IDisposable
//{
//    IDatabase Database { get; }
//}

//public class Database(AppSettings settings, string connString) : IDatabase, IAsyncDisposable
//{
//    public void TrySave(out string setting1, out string connectionString)
//    {
//        connectionString = connString;
//        setting1 = settings?.Setting1 ?? "Value3"/*config.Setting1*/;
//    }

//    public ValueTask DisposeAsync()
//    {
//        return default;
//    }
//}

//public interface IDatabase
//{
//    void TrySave(out string setting1, out string connectionString);
//}
//public class AppSettings
//{
//    public string Setting1 { get; set; } = "Test";
//    public string Setting2 { get; set; } = "";
//}


//#pragma warning disable CS9113 // Parameter is unread.
//public class EmployeeService(IAuthService authService, IDatabase employeesDb) : IDisposable
//#pragma warning restore CS9113 // Parameter is unread.
//{
//    private bool disposedValue;

//    protected virtual void Dispose(bool disposing)
//    {
//        if (!disposedValue)
//        {
//            if (disposing)
//            {
//                // TODO: dispose managed state (managed objects)
//            }

//            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
//            // TODO: set large fields to null
//            disposedValue = true;
//        }
//    }

//    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
//    // ~EmployeeService()
//    // {
//    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
//    //     Dispose(disposing: false);
//    // }

//    public void Dispose()
//    {
//        //authService.Dump();
//        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
//        Dispose(disposing: true);
//    }
//}
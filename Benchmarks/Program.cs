using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;


namespace Benchmarks;

[MemoryDiagnoser, HideColumns("Median", "Median", "StdDev")]
[SimpleJob(RuntimeMoniker.Net90, baseline: true)]
[SimpleJob(RuntimeMoniker.Net80)]
public class Program
{
    public static void Main()
    {
        //Pure.DI.DI.Setup("Server")
        //    .Bind().As(Pure.DI.Lifetime.Transient).To<AppSettings>()
        //    .Bind().As(Pure.DI.Lifetime.Singleton).To<Database>()
        //    .Bind().As(Pure.DI.Lifetime.Scoped).To<AuthService>()
        //    .Root<ManualServer>("Root");

        //new Program().Pure_DI();

        BenchmarkDotNet.Running.BenchmarkRunner.Run<Program>();
    }

    [Benchmark]
    public async Task Manual()
    {
        await using var container = new SourceCrafter.DependencyInjection.Tests.ServerSCDI();
        await using var scope = container.CreateScope();
        using var authService = scope.GetAuthService();
    }

    [Benchmark]
    public async Task SourceCrafter_DependencyInjection()
    {
        await using var container = new SourceCrafter.DependencyInjection.Tests.ServerSCDI();
        await using var scope = container.CreateScope();
        using var authService = scope.GetAuthService();
    }

    [Benchmark]
    public async Task Jab()
    {
        await using var container = new Jab.Tests.ServerJab();
        await using var scope = container.CreateScope();
        using var authService = scope.GetService<Jab.Tests.IAuthService>();
    }

    [Benchmark]
    public async Task MrMeeseeksDIE()
    {
        await using var container = ServerMrMeeseeks.DIE_CreateContainer();
        using var authService = container.GetAuthService();
    }
}

//public static class PureDI
//{
//    public static void Setup()
//    {
//        Pure.DI.DI.Setup("Server")
//            .Bind().As(Pure.DI.Lifetime.Transient).To<AppSettings>()
//            .Bind().As(Pure.DI.Lifetime.Singleton).To<Database>()
//            .Bind().As(Pure.DI.Lifetime.Scoped).To<AuthService>()
//            .Root<AuthService>("Root");
//    }
//}

[global::System.CodeDom.Compiler.GeneratedCode("SourceCrafter.DependencyInjection", "1.25.101.88")]
public sealed partial class ManualServer : global::System.IAsyncDisposable
{
    public static string Environment { get; } =
        global::System.Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Development";

    private static readonly Lazy<global::Benchmarks.Database> _database =
        new(() => new global::Benchmarks.Database(new()));

    private readonly Lazy<global::Benchmarks.AuthService> _authService =
        new(() => new global::Benchmarks.AuthService(_database.Value));

    public global::Benchmarks.Database GetDatabase() => _database.Value;

    public global::Benchmarks.AuthService GetAuthService() => _authService.Value;

    private bool isScoped;

    public async ValueTask DisposeAsync()
    {
        if (isScoped)
        {
            _authService?.Value.Dispose();
        }
        else if (_database.IsValueCreated)
        {
            await _database.Value.DisposeAsync();
        }
    }

    public ManualServer CreateScope() => new() { isScoped = true };
}
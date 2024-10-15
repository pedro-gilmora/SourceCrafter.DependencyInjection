using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;

using GettingStarted;

using Microsoft.Diagnostics.Tracing.Parsers.Tpl;

namespace Benchmarks;

[MemoryDiagnoser, HideColumns("Median", "Median", "StdDev")]
[SimpleJob(RuntimeMoniker.Net90, baseline: true)]
[SimpleJob(RuntimeMoniker.Net80)]
public class Program
{
    public static void Main()
    {

        //new Program().Pure_DI();

        BenchmarkDotNet.Running.BenchmarkRunner.Run<Program>();
    }


    //[Benchmark]
    //public void Pure_DI()
    //{
    //    using var container = new Benchmarks.Server();

    //    container.Root.Test();
    //}

    [Benchmark]
    public async Task MrMeeseeksDIE()
    {
        await using var container = ServerMrMeeseeks.DIE_CreateContainer();
        var authService = container.Create();
    }

    [Benchmark]
    public async Task Jab()
    {
        await using var container = new Jab.Tests.ServerJab();
        await using var scope = container.CreateScope();
        var authService = scope.GetService<Jab.Tests.IAuthService>();
    }

    //[Benchmark]
    //public async Task SourceCrafter_DependencyInjection()
    //{
    //    await using var container = new SourceCrafter.DependencyInjection.Tests.ServerSCDI();
    //    await using var scope = container.CreateScope();
    //    var authService = scope.GetAuthService();
    //}
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
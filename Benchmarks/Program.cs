using BenchmarkDotNet.Attributes;

using GettingStarted;

namespace Benchmarks;

[MemoryDiagnoser, HideColumns("Median", "Median", "StdDev")]
public class Program
{
    public static void Main()
    {

        //Pure.DI.DI.Setup("PureDiDeps")
        //    .Bind().As(Pure.DI.Lifetime.Singleton).To<PureDI.Tests.Database>()
        //    .Bind().As(Pure.DI.Lifetime.Scoped).To<PureDI.Tests.AuthService>()
        //    .Root<PureDI.Tests.AuthService>("Root");

        //new Program().Pure_DI();

        BenchmarkDotNet.Running.BenchmarkRunner.Run<Program>();
    }


    //[Benchmark]
    //public void Pure_DI()
    //{
    //    var container = new PureDiDeps();

    //    container.Root.Database.TrySave(out var setting1);
    //}

    [Benchmark]
    public void MrMeeseeksDIE()
    {
        var container = ServerMrMeeseeks.DIE_CreateContainer();
        var authService = container.Create();
    }

    [Benchmark]
    public void Jab()
    {
        var container = new Jab.Tests.ServerJab();
        var scope = container.CreateScope();
        var authService = scope.GetService<Jab.Tests.IAuthService>();
    }

    [Benchmark]
    public void SourceCrafter_DependencyInjection()
    {
        var container = new SourceCrafter.DependencyInjection.Tests.ServerSCDI();
        var scope = container.CreateScope();
        var authService = scope.GetAuthService();
    }
}
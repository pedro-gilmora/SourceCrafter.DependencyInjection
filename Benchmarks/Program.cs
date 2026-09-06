using BenchmarkDotNet.Running;

using Benchmarks;

// Sin argumentos ejecuta los cinco escenarios; con --filter se puede acotar.
// Ejemplo: dotnet run -c Release -- --filter *Scope*
BenchmarkSwitcher
    .FromTypes([
        typeof(SingletonBenchmark),
        typeof(TransientBenchmark),
        typeof(ComplexGraphBenchmark),
        typeof(ScopeBenchmark),
        typeof(EmptyScopeBenchmark),
        typeof(ContainerCreationBenchmark),
        typeof(ResolverShapeBenchmark),
        typeof(LockGranularityScopeBenchmark),
        typeof(LockGranularityEmptyScopeBenchmark),
        typeof(HotPathLocalBenchmark),
        typeof(AsyncCompositionCompletedBenchmark),
        typeof(AsyncCompositionPendingBenchmark)])
    .Run(args, HarnessConfig.Instance);
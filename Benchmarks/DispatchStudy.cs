using System.Runtime.CompilerServices;

using BenchmarkDotNet.Attributes;

namespace Benchmarks;

/// <summary>
/// Coste del despachador generico frente al interceptor, aislado del generador: se reproduce a
/// mano exactamente lo que se emite con <c>genericApi: true</c>. El interceptor llama al miembro
/// del servicio (<c>provider.Report</c>); sin interceptor, <c>GetRequiredService&lt;T&gt;()</c>
/// hace <c>Unsafe.As&lt;IProvider&lt;T&gt;&gt;(this).GetService()</c> (codigo generico compartido
/// para <typeparamref name="T"/> de referencia + llamada de interfaz).
/// </summary>
[MemoryDiagnoser]
public class DispatchBenchmark
{
	private readonly DispatchContainer _root = new();
	private DispatchContainer _scope = null!;

	[GlobalSetup]
	public void Setup()
	{
		_scope = _root.CreateScope();
		_ = _root.Report;
		_ = _scope.Report;
	}

	[Benchmark(Baseline = true, Description = "Interceptor (member)")]
	public DispatchReport Intercepted() => _scope.Report;

	[Benchmark(Description = "Generic (Unsafe.As<IProvider<T>>)")]
	public DispatchReport Generic() => _scope.GetRequiredService<DispatchReport>();

	[Benchmark(Description = "Interceptor, transient")]
	public DispatchClock InterceptedTransient() => new DispatchClock();

	[Benchmark(Description = "Generic, transient")]
	public DispatchClock GenericTransient() => _scope.GetRequiredService<DispatchClock>();
}

internal interface IDispatchProvider<T> where T : notnull
{
	T GetService();
}

public sealed class DispatchReport;

public sealed class DispatchClock;

public sealed class DispatchA;

public sealed class DispatchB;

public class DispatchContainer : IDispatchProvider<DispatchReport>, IDispatchProvider<DispatchClock>, IDispatchProvider<DispatchA>, IDispatchProvider<DispatchB>
{
	private DispatchReport? _report;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private DispatchReport CreateReport() => _report ??= new DispatchReport();

	public DispatchReport Report => _report ?? CreateReport();

	public DispatchA A { get; } = new();

	public DispatchB B { get; } = new();

	private DispatchContainer _rootRef = default!;

	public virtual DispatchContainer CreateScope() => new Scoped { _rootRef = this };

	public sealed class Scoped : DispatchContainer
	{
		public override DispatchContainer CreateScope() => new Scoped { _rootRef = _rootRef };
	}

	DispatchReport IDispatchProvider<DispatchReport>.GetService() => Report;
	DispatchClock IDispatchProvider<DispatchClock>.GetService() => new DispatchClock();
	DispatchA IDispatchProvider<DispatchA>.GetService() => A;
	DispatchB IDispatchProvider<DispatchB>.GetService() => B;

	public TOut GetRequiredService<TOut>() where TOut : notnull
		=> Unsafe.As<IDispatchProvider<TOut>>(this).GetService();
}

using System.Runtime.CompilerServices;
using System.Threading;

using BenchmarkDotNet.Attributes;

namespace Benchmarks;

/// <summary>
/// Estudio del <b>almacenamiento</b> del singleton ya construido, aislado del generador.
/// Responde a por que la fila "Resolve singleton" marca ~1,5x mientras que
/// <see cref="ResolverShapeBenchmark"/>, que mide la misma forma sobre un campo de
/// <b>instancia</b>, marca 0,56 ns.
/// <para>
/// La diferencia entre lo que mide aquel estudio y lo que se emite de verdad es que el
/// contenedor guarda los singletons en campos <b>estaticos</b>, y ademas emite siempre
/// <c>EnvironmentName</c>, un inicializador estatico que le da un <c>.cctor</c> a la clase.
/// Un acceso a campo estatico de un tipo con inicializador puede exigir comprobar que la
/// inicializacion de clase ya ocurrio; sin ningun inicializador el acceso es una direccion
/// constante. Estas cuatro variantes separan las dos cosas.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class SingletonStorageBenchmark
{
	private readonly InstanceFieldHolder _instance = new();

	[GlobalSetup]
	public void Setup()
	{
		// Todas arrancan ya construidas: se mide el camino caliente, no la construccion.
		_ = _instance.Value;
		_ = StaticFieldNoCctor.Value;
		_ = StaticFieldWithCctor.Value;
		_ = StaticFieldWithCctor.EnvironmentName;
	}

	/// <summary>Campo de instancia. Es lo que mide <see cref="ResolverShapeBenchmark"/>.</summary>
	[Benchmark(Baseline = true, Description = "Instance field")]
	public Payload InstanceField() => _instance.Value;

	/// <summary>Campo estatico en una clase <b>sin</b> ningun inicializador estatico.</summary>
	[Benchmark(Description = "Static field, no static initializer")]
	public Payload StaticNoCctor() => StaticFieldNoCctor.Value;

	/// <summary>Campo estatico en una clase con inicializador estatico: lo que se emite hoy.</summary>
	[Benchmark(Description = "Static field, class has static initializer")]
	public Payload StaticWithCctor() => StaticFieldWithCctor.Value;
}

public sealed class InstanceFieldHolder
{
	private Payload? _value;
	private readonly Lock _lock = new();

	[MethodImpl(MethodImplOptions.NoInlining)]
	private Payload Create()
	{
		lock (_lock) return _value ??= new Payload(new Settings());
	}

	public Payload Value
	{
		get
		{
			var v = _value;

			if (v is not null) return v;

			return Create();
		}
	}
}

/// <summary>
/// Sin inicializadores estaticos, asi que el compilador no emite <c>.cctor</c> alguno y el
/// acceso al campo se resuelve a una direccion constante.
/// </summary>
public sealed class StaticFieldNoCctor
{
	private static Payload? _value;
	private static readonly Lock __singletonLock = new();

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Payload Create()
	{
		lock (__singletonLock) return _value ??= new Payload(new Settings());
	}

	public static Payload Value
	{
		get
		{
			var v = _value;

			if (v is not null) return v;

			return Create();
		}
	}
}

/// <summary>
/// Reproduce exactamente lo que se emite hoy: los mismos campos, mas el par
/// <c>EnvironmentVariableName</c> / <c>EnvironmentName</c> que el generador anade siempre,
/// aunque el contenedor no registre nada por entorno. Ese inicializador es lo unico que
/// distingue esta variante de la anterior.
/// </summary>
public sealed class StaticFieldWithCctor
{
	public const string EnvironmentVariableName = "DOTNET_ENVIRONMENT";

	public static string EnvironmentName { get; } =
		global::System.Environment.GetEnvironmentVariable(EnvironmentVariableName) ?? "Development";

	private static Payload? _value;
	private static readonly Lock __singletonLock = new();

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Payload Create()
	{
		lock (__singletonLock) return _value ??= new Payload(new Settings());
	}

	public static Payload Value
	{
		get
		{
			var v = _value;

			if (v is not null) return v;

			return Create();
		}
	}
}

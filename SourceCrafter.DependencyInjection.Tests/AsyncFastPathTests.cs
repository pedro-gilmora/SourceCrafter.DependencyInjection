using System;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;

using SourceCrafter.DependencyInjection.Attributes;

using Xunit;

namespace SourceCrafter.DependencyInjection.Tests.AsyncFastPath;

public sealed class Handle : IAsyncDisposable
{
	public ValueTask DisposeAsync() => default;
}

/// <summary>
/// Contenedor propio: el campo de respaldo de un singleton es <c>static</c>, asi que
/// compartir el tipo con otro test acoplaria los dos por el orden de ejecucion.
/// </summary>
[ServiceProvider]
[Singleton<Handle>(source: nameof(_GetAsync))]
public partial class RacedContainer
{
	private static Task<Handle> _GetAsync() => Task.FromResult(new Handle());
}

/// <summary>
/// El camino rapido de un resolver cacheado <b>asincrono</b> debe leer su campo de
/// respaldo una sola vez, igual que el sincrono.
///
/// <para>La forma anterior lo leia dos veces:
/// <c>if(_f is { IsCompletedSuccessfully: true }) return _f;</c>. El liberador hace
/// <c>_f = null</c>, asi que un hilo que libere entre ambas lecturas hace que un miembro
/// declarado no nulable devuelva <c>null</c> — y, en la variante <c>ValueTask</c>, que el
/// <c>.Value</c> lance <see cref="InvalidOperationException"/>. El analisis de nulabilidad
/// de Roslyn no lo detecta porque da por hecho que nadie mas escribe el campo.</para>
///
/// <para>La forma actual usa la designacion del patron, <c>is { ... } __v</c>: el IL carga
/// el campo una sola vez y ademas deja el valor ya desenvuelto, asi que la variante
/// <c>ValueTask</c> no necesita <c>.Value</c>.</para>
/// </summary>
public class AsyncFastPathTests
{
	const string AsyncCachedContainer = """
		using SourceCrafter.DependencyInjection.Attributes;
		using System.Threading.Tasks;

		namespace Probe;

		public sealed class Dep { }
		public sealed class Composed(Dep dep) { }
		public sealed class Plain { }

		[ServiceProvider]
		[Singleton<Dep>(source: nameof(_GetDepAsync))]
		[Singleton<Composed>]
		[Scoped<Plain>(source: nameof(_GetPlainAsync))]
		public partial class Container
		{
			private static Task<Dep> _GetDepAsync() => Task.FromResult(new Dep());
			private static ValueTask<Plain> _GetPlainAsync() => new(new Plain());
		}
		""";

	[Fact]
	public void ThePropertyShapedAsyncFastPathReadsTheBackingFieldOnlyOnce()
	{
		var code = GeneratorHarness.Run(AsyncCachedContainer).Source("Container");

		code.Should().Contain("if(_depAsyncCached is { IsCompletedSuccessfully: true } __v) return __v;");
		code.Should().NotContain("if(__v is { IsCompletedSuccessfully: true }) return _depAsyncCached");
	}

	/// <summary>
	/// El campo de un resolver asincrono cacheado es <c>Task&lt;T&gt;</c> (ver
	/// <c>BackingFieldTypeName</c>): atomico al publicar y multi-consumo. Un miembro
	/// <c>ValueTask&lt;T&gt;</c> lo envuelve al salir, lo que no asigna (medido: 0 B).
	/// Lo que no debe aparecer nunca es <c>.Value</c>, una llamada que puede lanzar, ni
	/// una conversion <c>.AsTask()</c> en el camino de lectura, que asigna 72 B.
	/// </summary>
	[Fact]
	public void TheValueTaskVariantNeedsNoValueCallInTheFastPath()
	{
		var code = GeneratorHarness.Run(AsyncCachedContainer).Source("Container");

		code.Should().Contain(
			"if(_plainAsyncCached is { IsCompletedSuccessfully: true } __v) return new global::System.Threading.Tasks.ValueTask<global::Probe.Plain>(__v);");
		code.Should().NotContain("return _plainAsyncCached.Value;");
		code.Should().NotContain("return __v.Value;");
		code.Should().NotContain("return __v.AsTask();");
	}

	/// <summary>
	/// Los resolvers asincronos que componen dependencias salen como metodo y tienen su
	/// propio camino rapido, con el mismo defecto. La designacion <c>__v</c> no choca con
	/// los <c>__v0</c>, <c>__v1</c>... de las dependencias izadas.
	/// </summary>
	[Fact]
	public void TheMethodShapedAsyncFastPathAlsoReadsTheBackingFieldOnlyOnce()
	{
		var result = GeneratorHarness.Run(AsyncCachedContainer);
		var code = result.Source("Container");

		code.Should().Contain("if(_composedTask is { IsCompletedSuccessfully: true } __v) return __v;");
		code.Should().Contain("var __v0 = DepAsyncCached;");

		result.Errors.Should().BeEmpty();
	}

	/// <summary>
	/// La prueba de que el defecto era alcanzable, no teorico: con la forma de dos lecturas
	/// este test observaba millones de <c>null</c> en tres segundos, tanto en Debug como en
	/// Release.
	/// </summary>
	[Fact]
	public void ResolvingWhileAnotherThreadDisposesNeverYieldsNull()
	{
		var nulls = 0;
		var stop = false;

		var reader = new Thread(() =>
		{
			var container = new RacedContainer();

			while (!Volatile.Read(ref stop))
				if (container.AsyncCached is null) Interlocked.Increment(ref nulls);
		});

		// Cada contenedor nuevo tiene su propio '_disposed', pero el campo de respaldo del
		// singleton es compartido: liberar lo anula una y otra vez.
		var disposer = new Thread(() =>
		{
			while (!Volatile.Read(ref stop))
				new RacedContainer().DisposeAsync().GetAwaiter().GetResult();
		});

		reader.Start();
		disposer.Start();

		Thread.Sleep(500);
		Volatile.Write(ref stop, true);
		reader.Join();
		disposer.Join();

		nulls.Should().Be(0);
	}
}

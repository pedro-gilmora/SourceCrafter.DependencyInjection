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
/// el campo una sola vez y ademas deja el valor ya desenvuelto.</para>
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
	/// El camino caliente de un resolver <c>ValueTask</c> sobre un tipo de referencia lee
	/// el <b>resultado ya materializado</b>: una comprobacion de nulo, sin consultar el
	/// estado de ninguna tarea. Medido: 6,26 ns frente a 8,70 ns.
	///
	/// <para>El campo de la tarea sigue existiendo, pero solo para compartir la resolucion
	/// en vuelo y para liberar.</para>
	/// </summary>
	/// <summary>
	/// Una fabrica <c>ValueTask&lt;T&gt;</c> se expone igualmente como <c>Task&lt;T&gt;</c>:
	/// el campo que la respalda ya era <c>Task&lt;T&gt;</c>, asi que el camino caliente
	/// devuelve ese mismo campo sin envolver, convertir ni consultar un <c>.Value</c>.
	/// </summary>
	[Fact]
	public void TheValueTaskVariantNeedsNoValueCallInTheFastPath()
	{
		var code = GeneratorHarness.Run(AsyncCachedContainer).Source("Container");

		code.Should().Contain("if(_plainAsyncCached is { IsCompletedSuccessfully: true } __v) return __v;");
		code.Should().NotContain("ValueTask<global::Probe.Plain>");
		code.Should().NotContain("return _plainAsyncCached.Value;");
		code.Should().NotContain("return __v.Value;");
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

		/// <summary>
		/// El cuerpo del candado tiene que vivir en su propio metodo, y el motivo no es el
		/// mismo que en la ruta sincrona.
		///
		/// <para>La funcion local <c>ResolveCoreAsync</c> es <c>async</c> y captura
		/// <c>this</c> (para publicar el resultado) junto con los locales de tarea, asi que
		/// el compilador crea una clase de cierre y la asigna al <b>entrar</b> al miembro,
		/// antes de que el camino caliente llegue a comprobar nada. Medido sobre la forma
		/// realmente emitida: <b>48 B en cada lectura</b> y 11,1 ns frente a 8,1 ns, es
		/// decir, el camino caliente salia perdiendo. Con el camino lento en un metodo
		/// aparte la clase de cierre solo se asigna la primera vez: 0 B y 0,25 ns.</para>
		///
		/// <para>Ningun test anterior lo cazaba porque todos comprobaban expresiones
		/// sueltas, no que el miembro publico estuviera libre de candado y de cierre.</para>
		/// </summary>
		[Theory]
		[InlineData("PlainAsyncCached", "_plainAsyncCached")]
		[InlineData("DepAsyncCached", "_depAsyncCached")]
		[InlineData("GetComposedAsync", "_composedTask")]
		public void TheAsyncMemberCarriesNeitherTheLockNorTheClosure(string member, string field)
		{
			var result = GeneratorHarness.Run(AsyncCachedContainer);
			var code = result.Source("Container");

			result.Errors.Should().BeEmpty();

			var slowPath = "__Create" + field;

			code.Should().Contain("return " + slowPath + "();",
				"el miembro publico delega el camino lento");

			var memberStart = code.IndexOf(" " + member, StringComparison.Ordinal);
			memberStart.Should().BeGreaterThan(-1);

			var handOff = code.IndexOf("return " + slowPath + "();", memberStart, StringComparison.Ordinal);
			handOff.Should().BeGreaterThan(memberStart);

			var hotPath = code[memberStart..handOff];

			hotPath.Should().NotContain("lock(",
				"el candado en el cuerpo impide que el JIT inserte el miembro en linea");
			hotPath.Should().NotContain("ResolveCoreAsync",
				"la funcion local async fuerza una clase de cierre por lectura");

			// El metodo frio llega despues, y lleva el atributo que impide al JIT volver a
			// fusionarlo con el camino caliente.
			var slowPathDecl = code.IndexOf("private", handOff, StringComparison.Ordinal);

			slowPathDecl.Should().BeGreaterThan(handOff, "el camino lento se emite tras el miembro");

			code[handOff..].Should().Contain("MethodImplOptions.NoInlining");
		}
}

using FluentAssertions;

using Xunit;

namespace SourceCrafter.DependencyInjection.Tests;

/// <summary>
/// Dos formas que el generador emitia mal y que ningun test cubria, porque ambas
/// necesitan una combinacion que los bancos no tocaban: una <b>fabrica asincrona</b>
/// cuyas dependencias tambien son asincronas, y un desechable <b>sincrono</b> al que
/// solo se llega <b>esperando</b> la tarea que lo envuelve.
/// </summary>
public class AsyncFactoryCompositionTests
{
	/// <summary>
	/// Una fabrica asincrona ya devuelve <c>Task&lt;T&gt;</c>. Envolverla en
	/// <c>Task.FromResult&lt;T&gt;(...)</c> produce <c>Task&lt;Task&lt;T&gt;&gt;</c>, y el
	/// contenedor no compila.
	/// </summary>
	const string AsyncFactoryWithAsyncDeps = """
		using SourceCrafter.DependencyInjection.Attributes;
		using System;
		using System.Threading;
		using System.Threading.Tasks;

		namespace Probe;

		public class Settings { }
		public class Repo { public Repo(Settings s) { } }
		public class Audit { }
		public class Report { public Report(Repo r, Audit a) { } }

		[ServiceProvider]
		[Singleton<Settings>]
		[Singleton<Audit>(source: nameof(_GetAuditAsync))]
		[Scoped<Repo>(source: nameof(_GetRepoAsync))]
		[Scoped<Report>(source: nameof(_GetReportAsync))]
		public partial class Container
		{
			private static ValueTask<Audit> _GetAuditAsync() => new(new Audit());
			private static async Task<Repo> _GetRepoAsync(Settings s, CancellationToken ct)
			{
				await Task.Yield();
				return new Repo(s);
			}
			private static async Task<Report> _GetReportAsync(Repo r, Audit a, CancellationToken ct)
			{
				await Task.Yield();
				return new Report(r, a);
			}
		}
		""";

	[Fact]
	public void AnAsyncFactoryComposingAsyncDependenciesCompiles()
	{
		var result = GeneratorHarness.Run(AsyncFactoryWithAsyncDeps);

		result.Errors.Should().BeEmpty();
	}

	/// <summary>
	/// El camino rapido no debe envolver una fabrica que ya devuelve una tarea, y el
	/// lento tiene que esperarla: un <c>async Task&lt;T&gt;</c> no puede devolver
	/// <c>Task&lt;T&gt;</c>.
	/// </summary>
	[Fact]
	public void TheAsyncFactoryResultIsNeitherDoubleWrappedNorReturnedUnawaited()
	{
		var code = GeneratorHarness.Run(AsyncFactoryWithAsyncDeps).Source("Container");

		code.Should().NotContain("Task.FromResult<global::Probe.Report>");
		code.Should().Contain("return await _GetReportAsync(");
	}

	/// <summary>
	/// <c>Repo</c> es <c>IDisposable</c> a secas, pero se resuelve de forma asincrona: para
	/// liberarlo hay que <b>esperar</b> la tarea que lo envuelve. El liberador salia
	/// <c>async void</c> — excepciones no observables que tumban el proceso— porque el
	/// tipo de retorno se decidia solo por la disposability del servicio, sin mirar si
	/// hacia falta esperar.
	/// </summary>
	const string AsyncResolvedSyncDisposable = """
		using SourceCrafter.DependencyInjection.Attributes;
		using System;
		using System.Threading;
		using System.Threading.Tasks;

		namespace Probe;

		public class Settings { }
		public class Repo : IDisposable { public Repo(Settings s) { } public void Dispose() { } }

		[ServiceProvider]
		[Singleton<Settings>]
		[Scoped<Repo>(source: nameof(_GetRepoAsync))]
		public partial class Container
		{
			private static async Task<Repo> _GetRepoAsync(Settings s, CancellationToken ct)
			{
				await Task.Yield();
				return new Repo(s);
			}
		}
		""";

	[Fact]
	public void TheDisposerOfAnAsyncResolvedDisposableIsNeverAsyncVoid()
	{
		var result = GeneratorHarness.Run(AsyncResolvedSyncDisposable);
		var code = result.Source("Container");

		code.Should().NotContain("async void");
		result.Errors.Should().BeEmpty();
	}

	/// <summary>
	/// Si liberar exige esperar, el contenedor es <c>IAsyncDisposable</c> aunque el
	/// servicio solo sea <c>IDisposable</c>: no hay forma de cumplir un <c>Dispose()</c>
	/// sincrono sin bloquear o sin perder la excepcion.
	/// </summary>
	[Fact]
	public void AContainerThatMustAwaitToDisposeExposesDisposeAsync()
	{
		var code = GeneratorHarness.Run(AsyncResolvedSyncDisposable).Source("Container");

		code.Should().Contain("global::System.IAsyncDisposable");
		code.Should().Contain("ValueTask ScopedDisposeAsync()");
	}

	/// <summary>
	/// Tercer caso de la misma familia: un resolver <c>ValueTask</c> que compone una
	/// dependencia asincrona. El campo de respaldo es <c>ValueTask&lt;T&gt;?</c>, pero
	/// <c>ResolveCoreAsync</c> se declaraba siempre <c>Task&lt;T&gt;</c>, asi que el
	/// ternario del camino lento no convertia (CS0029). Declararlo con la forma del
	/// resolver evita ademas el <c>.AsTask()</c> de 72 B por publicacion.
	/// </summary>
	const string ValueTaskFactoryWithAsyncDep = """
		using SourceCrafter.DependencyInjection.Attributes;
		using System.Threading.Tasks;

		namespace Probe;

		public class Dep { }
		public class Made { public Made(Dep d) { } }

		[ServiceProvider]
		[Scoped<Dep>(source: nameof(_GetDepAsync))]
		[Scoped<Made>(source: nameof(_GetMadeAsync))]
		public partial class Container
		{
			private static async ValueTask<Dep> _GetDepAsync()
			{
				await Task.Yield();
				return new Dep();
			}

			private static async ValueTask<Made> _GetMadeAsync(Dep d)
			{
				await Task.Yield();
				return new Made(d);
			}
		}
		""";

	[Fact]
	public void AValueTaskResolverComposingAnAsyncDependencyCompiles()
	{
		var result = GeneratorHarness.Run(ValueTaskFactoryWithAsyncDep);

		result.Errors.Should().BeEmpty();
	}

	/// <summary>
	/// <c>ValueTask&lt;T&gt;.AsTask()</c> asigna 72 B por llamada (medido en
	/// <c>Benchmarks/AsyncPublicationStudy.cs</c>). Nunca debe aparecer en un camino
	/// que se recorra por resolucion.
	/// </summary>
	[Fact]
	public void TheValueTaskFastPathDoesNotAllocateThroughAsTask()
	{
		var code = GeneratorHarness.Run(ValueTaskFactoryWithAsyncDep).Source("Container");

		code.Should().NotContain(".AsTask()");
		code.Should().Contain("async global::System.Threading.Tasks.ValueTask<global::Probe.Made> ResolveCoreAsync()");
	}
}

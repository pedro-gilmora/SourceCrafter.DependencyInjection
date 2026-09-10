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
	/// El campo de respaldo es <c>Task&lt;T&gt;</c>, asi que un miembro
	/// <c>ValueTask&lt;T&gt;</c> lo envuelve al salir: el constructor
	/// <c>ValueTask&lt;T&gt;(Task&lt;T&gt;)</c> <b>no asigna</b> (medido: 0 B).
	///
	/// <para>La conversion contraria, <c>ValueTask&lt;T&gt;.AsTask()</c>, si asigna 72 B
	/// por llamada. Se paga una sola vez, al <b>publicar</b>, y nunca en el camino de
	/// lectura, que es el que se recorre en cada resolucion.</para>
	/// </summary>
	[Fact]
	public void TheValueTaskFastPathWrapsInsteadOfConverting()
	{
		var code = GeneratorHarness.Run(ValueTaskFactoryWithAsyncDep).Source("Container");

		// El campo, atomico y multi-consumo.
		code.Should().Contain("private global::System.Threading.Tasks.Task<global::Probe.Made>? _getMadeAsyncCached;");

		// Lectura: envuelve, no convierte.
		code.Should().Contain(
			"if(_getMadeAsyncCached is { IsCompletedSuccessfully: true } __v) return new global::System.Threading.Tasks.ValueTask<global::Probe.Made>(__v);");

		// La funcion local acompana al campo, no al miembro.
		code.Should().Contain("async global::System.Threading.Tasks.Task<global::Probe.Made> ResolveCoreAsync()");

		// El unico '.AsTask()' admisible esta en la publicacion, dentro del candado.
		code.Should().NotContain("return __v.AsTask()");
	}

	/// <summary>
	/// El campo de un resolver asincrono cacheado <b>nunca</b> puede ser
	/// <c>ValueTask&lt;T&gt;?</c>, por dos motivos independientes:
	///
	/// <para><b>Publicacion desgarrada.</b> <c>Nullable&lt;ValueTask&lt;T&gt;&gt;</c> son
	/// cinco campos y el CLR solo garantiza atomicidad hasta el tamano de puntero, asi que
	/// el <c>campo = null</c> del liberador son varios stores. Medido: 6 valores
	/// desgarrados en 216.400 lecturas, sin una sola excepcion que lo delate.</para>
	///
	/// <para><b>Consumo multiple.</b> Un <c>ValueTask</c> respaldado por
	/// <c>IValueTaskSource</c> se consume una sola vez; al reciclarse la fuente el segundo
	/// llamante recibe <c>InvalidOperationException</c>. Cachear uno y repartirlo es uso
	/// ilegal de la API.</para>
	///
	/// <para>Un <c>Task&lt;T&gt;</c> resuelve ambos: se publica con un unico store y admite
	/// cualquier numero de consumidores. Ocupa ademas 72 B frente a 96 B.</para>
	/// </summary>
	[Fact]
	public void NoAsyncCachedResolverEverBacksItselfWithANullableValueTask()
	{
		var code = GeneratorHarness.Run(ValueTaskFactoryWithAsyncDep).Source("Container");

		code.Should().NotContain("ValueTask<global::Probe.Made>? _");
		code.Should().NotContain("ValueTask<global::Probe.Dep>? _");
	}

	/// <summary>
	/// El liberador lee el campo a un local y lo anula. Con un campo de referencia ambas
	/// operaciones son un unico store, asi que un lector concurrente ve el valor antiguo o
	/// <c>null</c>, nunca una mezcla.
	/// </summary>
	[Fact]
	public void TheAsyncDisposerPublishesNullAtomically()
	{
		var code = GeneratorHarness.Run(AsyncResolvedSyncDisposable).Source("Container");

		code.Should().Contain("private global::System.Threading.Tasks.Task<global::Probe.Repo>? _getRepoAsyncCached;");
	}
}

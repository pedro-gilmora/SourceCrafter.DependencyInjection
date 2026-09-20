using FluentAssertions;

using Xunit;

namespace SourceCrafter.DependencyInjection.Tests;

/// <summary>
/// Huecos en la cobertura de las fabricas declaradas con <c>ValueTask</c>: el miembro que
/// las expone es siempre <c>Task&lt;T&gt;</c>, y la adaptacion se paga una sola vez dentro
/// del contenedor.
/// </summary>
public class ValueTaskVariantTests
{
	/// <summary>
	/// Un transient no cachea, asi que no tiene nada que acelerar ni que proteger: debe delegar
	/// en la fuente tal cual, sin campo de respaldo, sin candado y sin convertir la forma de la
	/// tarea. <see cref="ExportedTransientTests"/> comprueba que el miembro existe, pero no que
	/// sea esta forma minima.
	/// </summary>
	[Fact]
	public void AnExportedAsyncTransientDelegatesWithNoBackingField()
	{
		var result = GeneratorHarness.Run("""
			using SourceCrafter.DependencyInjection.Attributes;
			using System.Threading.Tasks;

			namespace Probe;

			public sealed class TransientDep { }

			[ServiceProvider(exportTransients: true)]
			[Transient<TransientDep>(source: nameof(_GetTransientAsync))]
			public partial class Container
			{
				private static ValueTask<TransientDep> _GetTransientAsync() => new(new TransientDep());
			}
			""");

		result.Errors.Should().BeEmpty();

		var code = result.Source("Container");

		code.Should().Contain("Task<global::Probe.TransientDep> TransientAsync");
		code.Should().Contain(".AsTask();", "la fabrica 'ValueTask' se adapta una sola vez, aqui");
		code.Should().NotContain("_transientAsyncCached", "un transient no tiene campo de respaldo");
		code.Should().NotContain("lock(", "ni candado");
	}

	/// <summary>
	/// Todo miembro asincrono generado habla en <c>Task&lt;T&gt;</c>, tanto el que hereda su
	/// asincronia de las dependencias como el que la toma de una fabrica
	/// <c>ValueTask&lt;T&gt;</c>. Antes cada uno conservaba la forma de su origen, de modo que
	/// dos servicios del mismo grafo exponian tipos de tarea distintos y cada consumidor
	/// tenia que adaptarlos.
	/// </summary>
	[Fact]
	public void AComposedServiceIsPromotedToTaskEvenIfEveryDependencyIsValueTask()
	{
		var result = GeneratorHarness.Run("""
			using SourceCrafter.DependencyInjection.Attributes;
			using System.Threading.Tasks;

			namespace Probe;

			public sealed class Dep { }
			public sealed class Composed(Dep dep) { }

			[ServiceProvider]
			[Singleton<Dep>(source: nameof(_GetDepAsync))]
			[Singleton<Composed>]
			public partial class Container
			{
				private static ValueTask<Dep> _GetDepAsync() => new(new Dep());
			}
			""");

		result.Errors.Should().BeEmpty();

		var code = result.Source("Container");

		// La fabrica sigue siendo 'ValueTask', pero el miembro que la expone no.
		code.Should().Contain("Task<global::Probe.Dep> DepAsync");
		code.Should().NotContain("ValueTask<global::Probe.Dep> DepAsync");

		code.Should().Contain("Task<global::Probe.Composed> GetComposedAsync()");
		code.Should().NotContain("ValueTask<global::Probe.Composed> GetComposedAsync()");
	}
}

using FluentAssertions;

using Xunit;

namespace SourceCrafter.DependencyInjection.Tests;

/// <summary>
/// Huecos en la cobertura de las formas <c>ValueTask</c>. Las guardas del acelerador de
/// resultado (tipo valor, liberacion, tipo de referencia) ya las fija
/// <see cref="AsyncFactoryCompositionTests"/>; aqui van las dos formas que no cubria nadie.
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

		code.Should().Contain("ValueTask<global::Probe.TransientDep> TransientAsync");
		code.Should().Contain("=> _GetTransientAsync();", "sin cache no hay nada que envolver");
		code.Should().NotContain("_transientAsyncCached", "un transient no tiene campo de respaldo");
		code.Should().NotContain("lock(", "ni candado");
	}

	/// <summary>
	/// Caracterizacion, no aprobacion.
	///
	/// <para>Un servicio que hereda su asincronia de las dependencias se promociona a
	/// <c>Task&lt;T&gt;</c> aunque todas ellas sean <c>ValueTask&lt;T&gt;</c>, porque la
	/// promocion fija <c>AsyncKind.Task</c> en vez del tipo de la dependencia y <c>Task</c> es
	/// el maximo del enum. El efecto secundario es que el acelerador de resultado, que exige
	/// <c>ValueTask</c>, no alcanza nunca a los servicios compuestos -- que son la mayoria en un
	/// grafo real.</para>
	///
	/// <para>No se cambia porque altera la firma publica del miembro generado, asi que es una
	/// decision de API y no una optimizacion interna. Este test esta aqui para que revertirlo
	/// sea deliberado. Ver la nota en <c>ServiceProviders.Parser.cs</c>.</para>
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

		// La dependencia si conserva su forma.
		code.Should().Contain("ValueTask<global::Probe.Dep> DepAsync");

		code.Should().Contain("Task<global::Probe.Composed> GetComposedAsync()");
		code.Should().NotContain("ValueTask<global::Probe.Composed> GetComposedAsync()");
		code.Should().NotContain("_composedTaskResult", "sin ValueTask no hay acelerador");
	}
}

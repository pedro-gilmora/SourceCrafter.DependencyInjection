using FluentAssertions;

using Xunit;

namespace SourceCrafter.DependencyInjection.Tests;

/// <summary>
/// Las dos restricciones de diseno acordadas: una fabrica asincrona debe declarar el tipo
/// expuesto (SCDI16), y solo un parametro por tipo de servicio puede quedarse sin clave
/// (SCDI17).
/// <para>
/// Ambos casos producian antes codigo que <b>no compila</b> — CS0029 y CS0103 respectivamente —
/// dentro del fichero generado, que es donde peor se leen. Cada test comprueba las dos mitades:
/// que el caso malo da el diagnostico, y que el caso bueno sigue sin darlo.
/// </para>
/// </summary>
public class DesignConstraintTests
{
	const string AsyncCovariance = """
		using System.Threading.Tasks;
		using SourceCrafter.DependencyInjection.Attributes;

		namespace Probe;

		public interface IService;
		public sealed class Impl : IService;

		[ServiceContainer]
		[Singleton<IService>(source: nameof(_Build))]
		public partial class Container
		{
			private static Task<Impl> _Build() => Task.FromResult(new Impl());
		}
		""";

	[Fact]
	public void AnAsyncFactoryReturningTheImplementationIsRejected()
	{
		var result = GeneratorHarness.Run(AsyncCovariance);

		// Task<T> es invariante: Task<Impl> no se convierte a Task<IService>. Sin este
		// diagnostico el fallo aparecia como CS0029 dentro del .g.cs.
		result.HasDiagnostic("SCDI16").Should().BeTrue();

		// Y aparecia *solo* alli: el diagnostico sustituye al error del compilador, no se
		// suma a el. Este aserto tambien cubre el defecto que salio al implementarlo — un
		// contenedor cuyas registraciones fallan todas devolvia null y se tragaba sus
		// propios diagnosticos.
		result.HasDiagnostic("CS0029").Should().BeFalse();
	}

	[Fact]
	public void AnAsyncFactoryDeclaringTheServiceTypeIsAccepted()
	{
		var result = GeneratorHarness.Run(
			AsyncCovariance
				.Replace("Task<Impl> _Build() => Task.FromResult(new Impl())",
					"Task<IService> _Build() => Task.FromResult<IService>(new Impl())"));

		result.HasDiagnostic("SCDI16").Should().BeFalse();
		result.Errors.Should().BeEmpty();
	}

	const string TwoUnkeyedParameters = """
		using SourceCrafter.DependencyInjection.Attributes;

		namespace Probe;

		public interface IService;
		public sealed class Alpha : IService;
		public sealed class Beta : IService;
		public sealed class Consumer(IService first, IService second);

		[ServiceContainer]
		[Singleton<IService, Alpha>]
		[Singleton<IService, Beta>]
		[Singleton<Consumer>]
		public partial class Container;
		""";

	const string OneUnkeyedParameter = """
		using SourceCrafter.DependencyInjection.Attributes;

		namespace Probe;

		public interface IService;
		public sealed class Alpha : IService;
		public sealed class Consumer(IService only);

		[ServiceContainer]
		[Singleton<IService, Alpha>]
		[Singleton<Consumer>]
		public partial class Container;
		""";

	[Fact]
	public void TwoUnkeyedParametersOfTheSameServiceTypeAreRejected()
	{
		var result = GeneratorHarness.Run(TwoUnkeyedParameters);

		// Nada distingue 'first' de 'second': antes ambos recibian en silencio el mismo
		// servicio, o se emitia un local sin declarar.
		result.HasDiagnostic("SCDI17").Should().BeTrue();
	}

	[Fact]
	public void ASingleUnkeyedParameterIsStillAccepted()
	{
		var result = GeneratorHarness.Run(OneUnkeyedParameter);

		// La regla debe morder solo en la ambiguedad real; el caso corriente no se toca.
		result.HasDiagnostic("SCDI17").Should().BeFalse();
		result.Errors.Should().BeEmpty();
	}

	[Fact]
	public void TwoParametersSharingTheOnlyRegistrationAreAccepted()
	{
		// Falso positivo que cazo la compilacion de Benchmarks, no la suite: con un unico
		// registro de 'Leaf' no hay nada que desambiguar, aunque dos parametros lo pidan.
		// La regla debe morder solo cuando hay varios registros compitiendo.
		var result = GeneratorHarness.Run("""
			using SourceCrafter.DependencyInjection.Attributes;

			namespace Probe;

			public sealed class Leaf;
			public sealed class Consumer(Leaf first, Leaf second);

			[ServiceContainer]
			[Singleton<Leaf>]
			[Singleton<Consumer>]
			public partial class Container;
			""");

		result.HasDiagnostic("SCDI17").Should().BeFalse();
		result.Errors.Should().BeEmpty();
	}

	[Fact]
	public void KeyedParametersDisambiguateAndAreAccepted()
	{
		// La salida que la restriccion pide al desarrollador: nombrar los parametros como las
		// claves registradas. Es el caso que debe seguir funcionando, y es lo que hace util al
		// diagnostico en vez de solo restrictivo.
		var result = GeneratorHarness.Run("""
			using SourceCrafter.DependencyInjection.Attributes;

			namespace Probe;

			public interface IService;
			public sealed class Alpha : IService;
			public sealed class Beta : IService;
			public sealed class Consumer(IService first, IService second);

			[ServiceContainer]
			[Singleton<IService, Alpha>("first")]
			[Singleton<IService, Beta>("second")]
			[Singleton<Consumer>]
			public partial class Container;
			""");

		result.HasDiagnostic("SCDI17").Should().BeFalse();
		result.Errors.Should().BeEmpty();
	}
}

using FluentAssertions;

using Xunit;

namespace SourceCrafter.DependencyInjection.Tests;

/// <summary>Errores de uso reales (vistos en agentes que usaban el paquete por primera vez) que deben dar su diagnostico, nunca un fallo del generador.</summary>
public class ConsumerMistakeTests
{
	/// <summary>Una interfaz registrada con clave y sin implementacion ni fabrica: SCDI06, no SCDIE00 (NullReferenceException del generador).</summary>
	[Theory]
	[InlineData("[Singleton<IPaymentGateway>(\"card\")]")]
	[InlineData("[Scoped<IPaymentGateway>(\"card\")]")]
	[InlineData("[Transient<IPaymentGateway>(\"card\")]")]
	[InlineData("[Singleton<IPaymentGateway>]")]
	public void AnInterfaceWithoutImplementationReportsSCDI06(string registration)
	{
		var result = GeneratorHarness.Run($$"""
			using SourceCrafter.DependencyInjection.Attributes;

			namespace Probe;

			public interface IPaymentGateway { }
			public sealed class Card : IPaymentGateway { }
			public sealed class Checkout(IPaymentGateway card, IPaymentGateway cash) { }

			[ServiceProvider]
			[Singleton<IPaymentGateway, Card>("cash")]
			{{registration}}
			[Transient<Checkout>]
			public partial class Container;
			""");

		var ids = result.GeneratorDiagnostics.Concat(result.CompilationDiagnostics).Select(d => d.Id + ": " + d.GetMessage()).ToList();
		ids.Should().NotContain(d => d.StartsWith("SCDIE00"), "the generator must not crash");
		ids.Should().Contain(d => d.StartsWith("SCDI06"));
	}
}

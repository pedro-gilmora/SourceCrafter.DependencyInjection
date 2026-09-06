using System.Threading.Tasks;

using FluentAssertions;

using SourceCrafter.DependencyInjection.Attributes;

using Xunit;

namespace SourceCrafter.DependencyInjection.Tests.InterceptorRuntime;

public interface IService { string Name { get; } }

public sealed class Alpha : IService { public string Name => "alpha"; }
public sealed class Beta(IService alpha) : IService { public string Name => alpha.Name + "->beta"; }
public sealed class Gamma(IService beta, IService alpha) : IService { public string Name => beta.Name + "+" + alpha.Name + "->gamma"; }
public sealed class Delta(IService gamma, IService beta, IService alpha) : IService { public string Name => gamma.Name + "+" + beta.Name + "+" + alpha.Name + "->delta"; }

[ServiceContainer(generateServiceProviderApi: true)]
[Singleton<IService>("alpha", source: nameof(_GetAlphaAsync))]
[Scoped<IService, Beta>("beta")]
[Transient<IService, Gamma>("gamma")]
[Scoped<IService, Delta>("delta")]
public partial class ChainedContainer
{
	/// <summary>
	/// Cede el hilo a proposito: con <c>Task.FromResult</c> todo estaria completo y el
	/// camino que lee <c>.Result</c> nunca se probaria de verdad.
	/// </summary>
	private static async Task<IService> _GetAlphaAsync()
	{
		await Task.Yield();

		return new Alpha();
	}
}

public interface IFailing { }
public sealed class FailingRoot : IFailing { }
public sealed class FailingLeaf(IFailing root) : IFailing { }

[ServiceContainer(generateServiceProviderApi: true)]
[Singleton<IFailing>("root", source: nameof(_GetRootAsync))]
[Scoped<IFailing, FailingLeaf>("leaf")]
public partial class FailingContainer
{
	private static async Task<IFailing> _GetRootAsync()
	{
		await Task.Yield();

		throw new System.InvalidOperationException("boom");
	}
}

/// <summary>
/// Comprueba en ejecucion que leer <c>.Result</c> de los elementos que otro elemento ya
/// resuelve devuelve los valores correctos y no bloquea sobre una tarea incompleta.
/// </summary>
public class InterceptorRuntimeTests
{
	[Fact]
	public async Task EveryElementComesBackFullyResolvedThroughTheAsynchronousPath()
	{
		// El receptor tiene que ser una variable: un `new Container().GetRequired...()` no
		// se reconoce como sitio interceptable y cae en el stub que lanza.
		var container = new ChainedContainer();
		var services = await container.GetRequiredServicesAsync<IService>();

		services.Should().HaveCount(4);
		services[0].Name.Should().Be("alpha");
		services[1].Name.Should().Be("alpha->beta");
		services[2].Name.Should().Be("alpha->beta+alpha->gamma");
		services[3].Name.Should().Be("alpha->beta+alpha->gamma+alpha->beta+alpha->delta");
	}

	[Fact]
	public async Task TheCachedElementsAreTheVerySameInstanceEverywhereTheyAppear()
	{
		var container = new ChainedContainer();
		var services = await container.GetRequiredServicesAsync<IService>();

		// Si .Result leyera una tarea distinta de la que resolvio al elemento que lo cubre,
		// estas identidades no coincidirian.
		var alpha = await container.GetAlphaAsyncCached;

		services[0].Should().BeSameAs(alpha);
		services[1].Should().BeSameAs(await container.GetBetaAsync());
	}

	[Fact]
	public async Task AFailureSurfacesAsTheOriginalExceptionAndNotAsAnAggregate()
	{
		var container = new FailingContainer();

		// Leer .Result de una tarea fallida daria AggregateException. No puede ocurrir: el
		// elemento que cubre a otro depende de el, asi que si el cubierto falla el cubridor
		// tambien, y su await lanza antes de que se lea ningun .Result.
		var act = async () => await container.GetRequiredServicesAsync<IFailing>();

		(await act.Should().ThrowAsync<InvalidOperationException>())
			.WithMessage("boom");
	}
}

using FluentAssertions;

using SourceCrafter.DependencyInjection.Attributes;

using Xunit;

namespace SourceCrafter.DependencyInjection.Tests.KeyedInterceptor;

public interface IStore { string Name { get; } }
public sealed class PublicStore : IStore { public string Name => "public"; }
public sealed class SecureStore : IStore { public string Name => "secure"; }
public static class StoreKeys { public const string Secure = "secure"; }

[ServiceProvider(genericApi: true)]
[Singleton<IStore, PublicStore>]
[Singleton<IStore, SecureStore>("secure")]
public partial class StoreContainer { }

/// <summary>
/// La clave elige la implementacion. Un interceptor solo la puede fijar si es una constante de compilacion; antes una clave que no
/// era literal ni identificador const (miembro const, variable) se tomaba por "" y la llamada devolvia el registro sin clave.
/// </summary>
public class KeyedInterceptorTests
{
	[Fact]
	public void EveryKeyedCallResolvesTheImplementationOfItsOwnKey()
	{
		var container = new StoreContainer();
		string runtimeKey = string.Concat("sec", "ure");

		container.GetRequiredKeyedService<IStore>("secure").Name.Should().Be("secure");
		container.GetRequiredKeyedService<IStore>(StoreKeys.Secure).Name.Should().Be("secure");
		container.GetRequiredKeyedService<IStore>(runtimeKey).Name.Should().Be("secure");
		container.GetRequiredService<IStore>().Name.Should().Be("public");
	}

	[Fact]
	public void AnUnregisteredConstantKeyIsAnErrorNotEveryRegistrationOfTheType()
	{
		var result = GeneratorHarness.Run("""
			using SourceCrafter.DependencyInjection.Attributes;

			namespace Probe;

			public interface IStore;
			public sealed class SecureStore : IStore;
			public sealed class TenantStore : IStore;

			[ServiceProvider(genericApi: true)]
			[Singleton<IStore, SecureStore>("secure")]
			[Singleton<IStore, TenantStore>("tenant")]
			public partial class Container;

			public static class CallSites
			{
				public static IStore[] Missing(Container c) => c.GetRequiredKeyedServices<IStore>("missing");
			}
			""");

		result.Errors.Select(e => e.Id).Should().Contain("SCDI11");
	}
}

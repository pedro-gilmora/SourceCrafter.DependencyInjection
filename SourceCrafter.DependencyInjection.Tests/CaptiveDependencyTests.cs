using FluentAssertions;

using Xunit;

namespace SourceCrafter.DependencyInjection.Tests;

/// <summary>
/// Un singleton vive en un campo estatico y se construye con el ambito que lo pide primero: si depende de algo de ese ambito
/// (directo, a traves de transients o de otro scoped, o del propio contenedor), ese ambito queda compartido por todos los demas.
/// </summary>
public class CaptiveDependencyTests
{
	const string Types = """
		using SourceCrafter.DependencyInjection.Attributes;
		using SourceCrafter.DependencyInjection.Constants;

		namespace Probe;

		public sealed class Db { }
		public sealed class Handler(Db db) { public Db Db => db; }
		public sealed class Middle(Handler h) { public Handler H => h; }
		public sealed class Captor(Handler h) { public Handler H => h; }
		public sealed class DeepCaptor(Middle m) { public Middle M => m; }
		public sealed class SelfCaptor(Container c) { public Container C => c; }
		""";

	static GeneratorHarness.Result Run(string registrations) => GeneratorHarness.Run(Types + $$"""

		[ServiceProvider]
		{{registrations}}
		public partial class Container;
		""");

	[Theory]
	[InlineData("[Scoped<Db>] [Scoped<Handler>] [Singleton<Captor>]")]                          // singleton -> scoped
	[InlineData("[Scoped<Db>] [Transient<Handler>] [Singleton<Captor>]")]                       // singleton -> transient -> scoped
	[InlineData("[Scoped<Db>] [Transient<Handler>] [Transient<Middle>] [Singleton<DeepCaptor>]")] // a cualquier profundidad
	public void ASingletonCannotCaptureScopedState(string registrations)
		=> Run(registrations).Errors.Should().Contain(d => d.Id == "SCDI27");

	[Theory]
	[InlineData("[Scoped<Db>] [Scoped<Handler>] [Scoped<Captor>]")]
	[InlineData("[Scoped<Db>] [Transient<Handler>] [Transient<Captor>]")]
	[InlineData("[Singleton<Db>] [Singleton<Handler>] [Singleton<Captor>]")]
	public void ScopedAndTransientConsumersAreFine(string registrations)
		=> Run(registrations).Errors.Should().BeEmpty();

	[Fact]
	public void ASingletonThatTakesTheContainerGetsTheRoot()
	{
		var result = Run("[Singleton<SelfCaptor>]");

		result.Errors.Should().BeEmpty();
		result.Source("Container").Should().MatchRegex(@"SelfCaptor\(\s*Root\s*\)").And.NotMatchRegex(@"SelfCaptor\(\s*this\s*\)");
	}

	[Fact]
	public void AScopedHostedServiceBehindAScopedConsumerIsCaughtToo()
	{
		var result = GeneratorHarness.RunWithServiceScopes(Types + """

			[ServiceProvider]
			[Hosted<Db>(Lifetime.Scoped)]
			[Scoped<Handler>]
			[Singleton<Captor>]
			public partial class Container;
			""");

		result.Errors.Should().Contain(d => d.Id == "SCDI27");
	}
}

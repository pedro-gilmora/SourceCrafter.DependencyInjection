using System;

using FluentAssertions;

using SourceCrafter.DependencyInjection.Attributes;

using Xunit;

namespace SourceCrafter.DependencyInjection.Tests.RootScope;

public sealed class Session : IDisposable
{
	public bool Disposed { get; private set; }
	public void Dispose() => Disposed = true;
}

public sealed class Through(Session session) { public Session Session => session; }
public sealed class Holder(Session session) { public Session Session => session; }
public sealed class DeepHolder(Through through) { public Through Through => through; }
public sealed class SelfHolder(RootScopeContainer container) { public RootScopeContainer Container => container; }

[ServiceProvider]
[Scoped<Session>]
[Transient<Through>]
[Singleton<Holder>]
[Singleton<DeepHolder>]
[Singleton<SelfHolder>]
public partial class RootScopeContainer;

/// <summary>
/// Un singleton vive lo que el contenedor, asi que sus dependencias scoped son las del ambito raiz: holding una instancia del ambito
/// raiz es decision del usuario. Lo que no puede pasar es que dependa de que ambito lo pide primero: antes se construia con ese ambito,
/// que retenia (y liberaba al cerrarse) y compartia con todos los demas.
/// </summary>
public class RootScopeSingletonTests
{
	[Fact]
	public void ASingletonFirstResolvedFromAScopeHoldsTheRootScopeInstances()
	{
		// Una sola prueba por contenedor: los singletons viven en campos estaticos del tipo.
		using var root = new RootScopeContainer();
		var first = root.CreateScope();

		var holder = first.Holder;
		var deep = first.DeepHolder;
		var self = first.SelfHolder;
		var firstSession = first.Session;

		holder.Session.Should().BeSameAs(root.Session).And.NotBeSameAs(firstSession);
		deep.Through.Session.Should().BeSameAs(root.Session);
		self.Container.Should().BeSameAs(root);

		first.Dispose();

		firstSession.Disposed.Should().BeTrue();
		holder.Session.Disposed.Should().BeFalse("the root scope outlives the first scope");

		var second = root.CreateScope();
		second.Holder.Should().BeSameAs(holder);
		second.Holder.Session.Should().BeSameAs(root.Session);
		second.Dispose();
	}

	[Theory]
	[InlineData("[Scoped<Db>] [Scoped<Handler>] [Singleton<Captor>]")]
	[InlineData("[Scoped<Db>] [Transient<Handler>] [Singleton<Captor>]")]
	public void ASingletonMayDependOnScopedServices(string registrations)
	{
		var result = GeneratorHarness.Run(Types + $$"""

			[ServiceProvider]
			{{registrations}}
			public partial class Container;
			""");

		result.Errors.Should().BeEmpty();
	}

	/// <summary>La raiz no tiene ambito del host: un per-request [Hosted] detras de un scoped fallaria al construir el singleton.</summary>
	[Fact]
	public void APerRequestHostedServiceBehindAScopedConsumerIsRejected()
	{
		var result = GeneratorHarness.RunWithServiceScopes(Types + """

			[ServiceProvider]
			[Hosted<Db>(Lifetime.Scoped)]
			[Scoped<Handler>]
			[Singleton<Captor>]
			public partial class Container;
			""");

		result.Errors.Should().Contain(d => d.Id == "SCDI24");
	}

	const string Types = """
		using SourceCrafter.DependencyInjection.Attributes;
		using SourceCrafter.DependencyInjection.Constants;

		namespace Probe;

		public sealed class Db { }
		public sealed class Handler(Db db) { public Db Db => db; }
		public sealed class Captor(Handler h) { public Handler H => h; }
		""";
}

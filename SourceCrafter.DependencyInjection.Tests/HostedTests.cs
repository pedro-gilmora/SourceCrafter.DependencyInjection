using System;

using FluentAssertions;

using SourceCrafter.DependencyInjection.Attributes;

using Xunit;

namespace SourceCrafter.DependencyInjection.Tests.Hosted;

public interface IClock { long Ticks { get; } }

/// <summary>Pertenece al host: solo el host la libera.</summary>
public sealed class HostClock : IClock, IDisposable
{
	public long Ticks => 42;
	public int Disposals;
	public void Dispose() => Disposals++;
}

public sealed class Report(IClock clock)
{
	public IClock Clock => clock;
}

public sealed class Session(IClock clock) : IDisposable
{
	public IClock Clock => clock;
	public int Disposals;
	public void Dispose() => Disposals++;
}

/// <summary>Contenedor de servicios del host (p.ej. el de ASP.NET Core).</summary>
public sealed class FakeHost(object clock) : IServiceProvider
{
	public int Calls;

	public object? GetService(Type serviceType)
	{
		Calls++;
		return serviceType == typeof(IClock) ? clock : null;
	}
}

[ServiceProvider(genericApi: true)]
[Hosted<IClock>]
[Transient<Report>]
[Scoped<Session>]
public partial class HostedContainer;

public class HostedTests
{
	[Fact]
	public void ConsumersReceiveTheHostInstance()
	{
		var clock = new HostClock();
		var container = new HostedContainer { HostServices = new FakeHost(clock) };

		container.GetRequiredService<Report>().Clock.Should().BeSameAs(clock);
	}

	[Fact]
	public void TheContainerDoesNotCacheHostServices()
	{
		var host = new FakeHost(new HostClock());
		var container = new HostedContainer { HostServices = host };

		container.GetRequiredService<Report>();
		container.GetRequiredService<Report>();

		host.Calls.Should().Be(2);
	}

	[Fact]
	public void ScopesResolveFromTheSameHost()
	{
		var clock = new HostClock();
		var container = new HostedContainer { HostServices = new FakeHost(clock) };

		var scope = container.CreateScope();

		scope.GetRequiredService<Session>().Clock.Should().BeSameAs(clock);
		scope.CreateScope().GetRequiredService<Session>().Clock.Should().BeSameAs(clock);
	}

	[Fact]
	public void HostServicesAreNeverDisposedByTheContainer()
	{
		var clock = new HostClock();
		var container = new HostedContainer { HostServices = new FakeHost(clock) };
		var scope = container.CreateScope();
		var session = scope.GetRequiredService<Session>();

		scope.Dispose();
		container.Dispose();

		session.Disposals.Should().Be(1);
		clock.Disposals.Should().Be(0);
	}

	[Fact]
	public void AMissingHostServiceFailsWithItsName()
	{
		var act = () => new HostedContainer().GetRequiredService<Report>();

		act.Should().Throw<InvalidOperationException>().WithMessage("*IClock is not available in HostServices*");
	}

	[Fact]
	public void HostedRegistrationsDoNotMakeTheContainerDisposable()
	{
		var result = GeneratorHarness.Run("""
			using SourceCrafter.DependencyInjection.Attributes;

			namespace Probe;

			public sealed class Clock : System.IDisposable { public void Dispose() { } }

			public sealed class Report(Clock clock) { public Clock Clock => clock; }

			[ServiceProvider]
			[Hosted<Clock>]
			[Transient<Report>]
			public partial class Container;
			""");

		result.Errors.Should().BeEmpty();
		result.Source("Container").Should().Contain("HostServices { get; init; }").And.NotContain("Dispose()");
	}

	[Fact]
	public void AUserDeclaredHostServicesIsReused()
	{
		var result = GeneratorHarness.Run("""
			using SourceCrafter.DependencyInjection.Attributes;

			namespace Probe;

			public interface IClock { }

			public sealed class Report(IClock clock) { public IClock Clock => clock; }

			[ServiceProvider]
			[Hosted<IClock>]
			[Transient<Report>]
			public partial class Container
			{
				public System.IServiceProvider? HostServices { get; init; }
			}
			""");

		result.Errors.Should().BeEmpty();
		result.Source("Container").Should().NotContain("HostServices { get; init; }");
	}

	[Fact]
	public void AsyncHostedServicesAreRejected()
	{
		var result = GeneratorHarness.Run("""
			using SourceCrafter.DependencyInjection.Attributes;

			namespace Probe;

			[ServiceProvider]
			[Hosted<System.Threading.Tasks.Task<int>>]
			public partial class Container;
			""");

		result.Errors.Should().Contain(d => d.Id == "SCDI23");
	}

	[Fact]
	public void GetHostedResolvesDeclaredHostedTypes()
	{
		var clock = new HostClock();
		var container = new HostedContainer { HostServices = new FakeHost(clock) };

		container.GetHostedService<IClock>().Should().BeSameAs(clock);
		container.GetHostedRequiredService<IClock>().Should().BeSameAs(clock);
		container.GetHostedServices<IClock>().Should().BeEmpty();
		new HostedContainer().GetHostedService<IClock>().Should().BeNull();
	}

	[Fact]
	public void GetHostedRejectsUndeclaredTypesAtCompileTime()
	{
		var result = GeneratorHarness.Run("""
			using SourceCrafter.DependencyInjection;
			using SourceCrafter.DependencyInjection.Attributes;

			namespace Probe;

			public interface IClock { }

			[ServiceProvider]
			[Hosted<IClock>]
			public partial class Container
			{
				object? Probe() => this.GetHostedService<string>();
			}
			""");

		result.Errors.Should().ContainSingle().Which.Id.Should().Be("CS1929");
	}

	[Fact]
	public void TheAppServiceProviderIsNotMistakenForTheContainer()
	{
		var result = GeneratorHarness.RunWithServiceScopes("""
			using Microsoft.Extensions.DependencyInjection;
			using SourceCrafter.DependencyInjection.Attributes;

			namespace Probe;

			public interface IClock { }

			public sealed class Report { }

			[ServiceProvider(genericApi: true)]
			[Hosted<IClock>]
			[Scoped<Report>]
			public partial class Container
			{
				static IClock FromApp(System.IServiceProvider app) => app.GetRequiredService<IClock>();

				static Report Chained() => new Container().CreateScope().GetRequiredService<Report>();
			}
			""");

		result.Errors.Should().BeEmpty();
	}
}

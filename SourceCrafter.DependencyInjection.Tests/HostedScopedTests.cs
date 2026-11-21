using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;

// Importado a proposito, como en ASP.NET Core: su CreateScope() de extension no debe confundir al analizador.
using Microsoft.Extensions.DependencyInjection;

using SourceCrafter.DependencyInjection.Attributes;
using SourceCrafter.DependencyInjection.Constants;

using Xunit;

namespace SourceCrafter.DependencyInjection.Tests.HostedScoped;

/// <summary>Como un DbContext: scoped en el host, una instancia por peticion.</summary>
public sealed class RequestDb : IAsyncDisposable
{
	public bool Disposed;

	public ValueTask DisposeAsync()
	{
		Disposed = true;
		return default;
	}
}

public sealed class Handler(RequestDb db)
{
	public RequestDb Db => db;
}

public sealed class Reader(RequestDb db)
{
	public RequestDb Db => db;
}

public sealed class AsyncReport(RequestDb db)
{
	public RequestDb Db => db;
}

[ServiceProvider(genericApi: true)]
[Hosted<RequestDb>(Lifetime.Scoped)]
[Scoped<Handler>]
[Transient<Reader>]
[Scoped<AsyncReport>(source: nameof(CreateReportAsync))]
public partial class RequestContainer
{
	static async Task<AsyncReport> CreateReportAsync(RequestDb db)
	{
		await Task.Yield();
		return new(db);
	}
}

/// <summary>Cuenta cuantas veces se pide la fabrica de ambitos, es decir, cuantos ambitos del host se abren.</summary>
public sealed class CountingHost(IServiceProvider inner) : IServiceProvider
{
	public int ScopesOpened;

	public object? GetService(Type serviceType)
	{
		if (serviceType == typeof(IServiceScopeFactory)) Interlocked.Increment(ref ScopesOpened);

		return inner.GetService(serviceType);
	}
}

public class HostedScopedTests
{
	static IServiceProvider NewHost() => new ServiceCollection().AddScoped<RequestDb>().BuildServiceProvider(validateScopes: true);

	static RequestContainer NewContainer() => new() { HostServices = NewHost() };

	[Fact]
	public void ChainedCreateScopeCompilesWithMicrosoftDependencyInjectionImported()
		=> NewContainer().CreateScope().GetRequiredService<Handler>().Db.Should().NotBeNull();

	[Fact]
	public async Task AScopeThatDoesNotUseThemOpensNoHostScope()
	{
		var host = new CountingHost(NewHost());

		await using (new RequestContainer { HostServices = host }.CreateScope()) { }

		host.ScopesOpened.Should().Be(0);
	}

	[Fact]
	public void ConcurrentFirstResolutionsShareOneHostScope()
	{
		var scope = NewContainer().CreateScope();
		var dbs = new RequestDb[16];

		using var barrier = new Barrier(dbs.Length);

		var threads = Enumerable.Range(0, dbs.Length).Select(i => new Thread(() =>
		{
			barrier.SignalAndWait();
			dbs[i] = scope.GetRequiredService<Reader>().Db;
		})).ToArray();

		foreach (var t in threads) t.Start();
		foreach (var t in threads) t.Join();

		dbs.Distinct().Should().ContainSingle();
	}

	[Fact]
	public void GetHostedResolvesFromTheScopeHostScope()
	{
		var scope = NewContainer().CreateScope();
		var db = scope.GetRequiredService<Handler>().Db;

		scope.GetHostedRequiredService<RequestDb>().Should().BeSameAs(db);
		scope.GetHostedServices<RequestDb>().Should().ContainSingle().Which.Should().BeSameAs(db);
	}

	[Fact]
	public void TheContainerIsAsyncDisposableWithoutOwningAnyDisposable()
		=> typeof(IAsyncDisposable).IsAssignableFrom(typeof(RequestContainer.Scoped)).Should().BeTrue();

	[Fact]
	public void TheyCanBeResolvedDirectly()
	{
		var scope = NewContainer().CreateScope();

		scope.GetRequiredService<RequestDb>().Should().BeSameAs(scope.GetRequiredService<Handler>().Db);
	}

	[Fact]
	public async Task AsyncConsumersShareTheScopeInstance()
	{
		var scope = NewContainer().CreateScope();

		(await scope.GetRequiredServiceAsync<AsyncReport>()).Db.Should().BeSameAs(scope.GetRequiredService<Handler>().Db);
	}

	[Fact]
	public void OneInstancePerScope()
	{
		var scope = NewContainer().CreateScope();

		scope.GetRequiredService<Handler>().Db.Should().BeSameAs(scope.GetRequiredService<Reader>().Db);
	}

	[Fact]
	public void EachScopeGetsItsOwnInstance()
	{
		var container = NewContainer();

		container.CreateScope().GetRequiredService<Handler>().Db
			.Should().NotBeSameAs(container.CreateScope().GetRequiredService<Handler>().Db);
	}

	[Fact]
	public async Task DisposingTheScopeDisposesTheHostScope()
	{
		var scope = NewContainer().CreateScope();
		var db = scope.GetRequiredService<Handler>().Db;

		await scope.DisposeAsync();

		db.Disposed.Should().BeTrue();
	}

	[Fact]
	public void TheRootCannotResolveThem()
	{
		var act = () => NewContainer().GetRequiredService<Reader>();

		act.Should().Throw<InvalidOperationException>().WithMessage("*need a scope*");
	}

	[Fact]
	public void WithoutHostServicesOnlyTheirResolutionFails()
	{
		var scope = new RequestContainer().CreateScope();

		var act = () => scope.GetRequiredService<Handler>();

		act.Should().Throw<InvalidOperationException>().WithMessage("*need a scope created with HostServices*");
	}

	[Theory]
	[InlineData("[Singleton<Consumer>]", "")]
	[InlineData("[Singleton<Outer>]", "[Transient<Consumer>]")]
	public void ASingletonCannotDependOnThem(string singleton, string transient)
	{
		var result = GeneratorHarness.RunWithServiceScopes($$"""
			using SourceCrafter.DependencyInjection.Attributes;
			using SourceCrafter.DependencyInjection.Constants;

			namespace Probe;

			public sealed class Db { }
			public sealed class Consumer(Db db) { public Db Db => db; }
			public sealed class Outer(Consumer c) { public Consumer C => c; }

			[ServiceProvider]
			[Hosted<Db>(Lifetime.Scoped)]
			{{transient}}
			{{singleton}}
			public partial class Container;
			""");

		result.Errors.Should().Contain(d => d.Id == "SCDI24");
	}

	[Fact]
	public void WithoutServiceScopesTheyAreRejected()
	{
		var result = GeneratorHarness.Run("""
			using SourceCrafter.DependencyInjection.Attributes;
			using SourceCrafter.DependencyInjection.Constants;

			namespace Probe;

			public sealed class Db { }

			[ServiceProvider]
			[Hosted<Db>(Lifetime.Scoped)]
			public partial class Container;
			""");

		result.Errors.Should().ContainSingle().Which.Id.Should().Be("SCDI25");
	}
}

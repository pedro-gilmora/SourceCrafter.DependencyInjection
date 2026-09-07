using System;
using System.Threading.Tasks;

using FluentAssertions;

using SourceCrafter.DependencyInjection.Attributes;

using Xunit;

namespace SourceCrafter.DependencyInjection.Tests.AsyncDisposal;

public sealed class Resource : IAsyncDisposable
{
	public bool Disposed { get; private set; }

	public ValueTask DisposeAsync()
	{
		Disposed = true;

		return default;
	}
}

[ServiceContainer]
[Singleton<Resource>(source: nameof(_GetAsync))]
public partial class HealthyContainer
{
	internal static Resource? Last;

	private static async Task<Resource> _GetAsync()
	{
		await Task.Yield();

		return Last = new Resource();
	}
}

[ServiceContainer]
[Singleton<Resource>(source: nameof(_GetAsync))]
public partial class FaultedContainer
{
	private static async Task<Resource> _GetAsync()
	{
		await Task.Yield();

		throw new InvalidOperationException("boom");
	}
}

/// <summary>
/// Liberar es limpieza: nunca debe convertirse en la fuente de una excepcion nueva, ni
/// depender de que el consumidor tenga <c>ImplicitUsings</c> activado.
/// </summary>
public class AsyncDisposalTests
{
	[Fact]
	public async Task AResolvedServiceIsStillDisposed()
	{
		var container = new HealthyContainer();

		var resource = await container.GetAsyncCached;

		await container.DisposeAsync();

		resource.Disposed.Should().BeTrue();
	}

	[Fact]
	public async Task DisposingAfterAFaultedResolutionDoesNotRethrow()
	{
		var container = new FaultedContainer();

		try { await container.GetAsyncCached; } catch (InvalidOperationException) { }

		// Antes 'TryDisposeAsync' desenvolvia la tarea con GetResult()/await y relanzaba. En
		// un 'await using' eso sustituye la excepcion real del bloque por esta.
		var act = async () => await container.DisposeAsync();

		await act.Should().NotThrowAsync();
	}

	[Fact]
	public async Task DisposingWithoutResolvingAnythingDoesNothing()
	{
		var act = async () => await new HealthyContainer().DisposeAsync();

		await act.Should().NotThrowAsync();
	}

	[Fact]
	public void TheGeneratedTaskExtensionsCompileWithoutImplicitUsings()
	{
		// El archivo se compila dentro del proyecto del consumidor. La compilacion de prueba
		// no tiene 'using System' implicito, igual que un proyecto de estilo antiguo o
		// netstandard2.0: antes 'IDisposable' e 'IAsyncDisposable' no resolvian (CS0246).
		var result = GeneratorHarness.Run("""
			using SourceCrafter.DependencyInjection.Attributes;
			using System;
			using System.Threading.Tasks;

			namespace Probe;

			public sealed class Resource : IAsyncDisposable
			{
				public ValueTask DisposeAsync() => default;
			}

			[ServiceContainer]
			[Singleton<Resource>(source: nameof(_GetAsync))]
			public partial class Container
			{
				private static Task<Resource> _GetAsync() => Task.FromResult(new Resource());
			}
			""");

		result.Errors.Should().BeEmpty();
		result.Source("TaskExtensions").Should().NotContain("using global::System.Threading.Tasks;");
	}
}

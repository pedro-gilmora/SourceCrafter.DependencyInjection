using System.Threading.Tasks;

using FluentAssertions;

using SourceCrafter.DependencyInjection.Attributes;

using Xunit;

namespace SourceCrafter.DependencyInjection.Tests.Unobserved;

public interface IFlaky { }

[ServiceProvider(genericApi: true)]
[Singleton<IFlaky>("first", source: nameof(_GetFirstAsync))]
[Scoped<IFlaky>("second", source: nameof(_GetSecondAsync))]
public partial class TwoFailuresContainer
{
	private static async Task<IFlaky> _GetFirstAsync()
	{
		await Task.Yield();
		throw new System.InvalidOperationException("first");
	}

	private static async Task<IFlaky> _GetSecondAsync()
	{
		await Task.Yield();
		throw new System.InvalidOperationException("second");
	}
}

/// <summary>
/// Al quitar <c>Task.WhenAll</c> (Fase 17) el interceptor multiple dejo de observar todas
/// las tareas: el primer <c>await</c> lanza y las hermanas quedan huerfanas, asi que su
/// excepcion acaba en <c>TaskScheduler.UnobservedTaskException</c>. Estos tests fijan el
/// cierre y sus dos limites.
/// </summary>
public class UnobservedTaskTests
{
	[Fact]
	public async Task TheOriginalExceptionStillPropagatesUnwrapped()
	{
		var container = new TwoFailuresContainer();

		// Observar a las hermanas no debe alterar lo que ve el llamador: sigue siendo la
		// excepcion del primer elemento, no una AggregateException.
		var act = async () => await container.GetRequiredServicesAsync<IFlaky>();

		(await act.Should().ThrowAsync<System.InvalidOperationException>())
			.WithMessage("first");
	}

	const string TwoTaskElements = """
		using System.Threading.Tasks;
		using SourceCrafter.DependencyInjection.Attributes;

		namespace Probe;

		public interface IService;
		public sealed class Alpha : IService;

		[ServiceProvider(genericApi: true)]
		[Singleton<IService>("a", source: nameof(_GetA))]
		[Scoped<IService>("b", source: nameof(_GetB))]
		public partial class Container
		{
			private static Task<IService> _GetA() => Task.FromResult<IService>(new Alpha());
			private static Task<IService> _GetB() => Task.FromResult<IService>(new Alpha());
		}

		public static class CallSite
		{
			public static Task<IService[]> All(Container c) => c.GetRequiredServicesAsync<IService>();
		}
		""";

	[Fact]
	public void SiblingTasksAreObservedOnTheWayOut()
	{
		var result = GeneratorHarness.Run(TwoTaskElements);
		var code = result.Source("Container");

		code.Should().Contain("catch");
		code.Should().Contain("_ = __t0.Exception;");
		code.Should().Contain("_ = __t1.Exception;");
		code.Should().Contain("throw;");

		result.Errors.Should().BeEmpty();
	}

	[Fact]
	public void ValueTaskElementsAreLeftAlone()
	{
		// Una fabrica 'ValueTask<T>' ya no deja rastro en el miembro: este es 'Task<T>' y
		// la adaptacion ocurre una sola vez, dentro del contenedor. Por eso sus elementos
		// si se pueden observar -- antes no, porque 'ValueTask<T>' no expone 'Exception' y
		// 'AsTask()' sobre una ya consumida lanza.
		var result = GeneratorHarness.Run(
			TwoTaskElements.Replace("Task<IService> _Get", "ValueTask<IService> _Get")
				.Replace("Task.FromResult<IService>(new Alpha())", "new ValueTask<IService>(new Alpha())"));

		var code = result.Source("Container");

		code.Should().Contain("_ = __t0.Exception;");
		code.Should().Contain("_ = __t1.Exception;");
		result.Errors.Should().BeEmpty();
	}
}

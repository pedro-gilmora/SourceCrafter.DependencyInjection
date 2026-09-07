using System;
using System.Threading.Tasks;

using FluentAssertions;

using SourceCrafter.DependencyInjection.Attributes;

using Xunit;

namespace SourceCrafter.DependencyInjection.Tests.InterceptorCache;

public interface ICached { }

public sealed class One : ICached;
public sealed class Two : ICached;
public sealed class Three : ICached;

[ServiceProvider(genericApi: true)]
[Singleton<ICached, One>("one")]
[Singleton<ICached, Two>("two")]
public partial class AllSingletonContainer;

[ServiceProvider(genericApi: true)]
[Singleton<ICached, One>("one")]
[Scoped<ICached, Two>("two")]
public partial class ScopedContainer;

[ServiceProvider(genericApi: true)]
[Singleton<ICached, One>("one")]
[Transient<ICached, Two>("two")]
public partial class TransientContainer;

/// <summary>
/// El array que devuelve un interceptor multiple se cachea, pero solo cuando puede: basta
/// un elemento transitorio para que guardarlo cambie la semantica del contenedor.
/// </summary>
public class InterceptorCacheTests
{
	[Fact]
	public void AnArrayOfSingletonsIsBuiltOnceForTheWholeProcess()
	{
		var container = new AllSingletonContainer();
		var other = new AllSingletonContainer();

		var first = container.GetRequiredServices<ICached>();

		first.Should().BeSameAs(container.GetRequiredServices<ICached>());

		// El campo es estatico, igual que los propios singletons del contenedor: los
		// elementos ya se compartian entre instancias, asi que el array tambien.
		first.Should().BeSameAs(other.GetRequiredServices<ICached>());
	}

	[Fact]
	public void AnArrayWithScopedElementsIsBuiltOncePerScope()
	{
		var container = new ScopedContainer();

		var a = container.CreateScope();
		var b = container.CreateScope();

		var fromA = a.GetRequiredServices<ICached>();
		var fromB = b.GetRequiredServices<ICached>();

		fromA.Should().BeSameAs(a.GetRequiredServices<ICached>());

		// Cachear en un estatico entregaria el ambito de A a todo el mundo.
		fromA.Should().NotBeSameAs(fromB);
		fromA[1].Should().NotBeSameAs(fromB[1], "el elemento scoped pertenece a su ambito");
		fromA[0].Should().BeSameAs(fromB[0], "el singleton se sigue compartiendo");
	}

	[Fact]
	public void ATransientElementKeepsTheArrayOutOfTheCache()
	{
		var container = new TransientContainer();

		var first = container.GetRequiredServices<ICached>();
		var second = container.GetRequiredServices<ICached>();

		// Guardar el array convertiria el transitorio en un singleton de hecho.
		first.Should().NotBeSameAs(second);
		first[1].Should().NotBeSameAs(second[1]);
	}

	const string AllSingletonSource = """
		using SourceCrafter.DependencyInjection.Attributes;

		namespace Probe;

		public interface IService;
		public sealed class Alpha : IService;
		public sealed class Beta : IService;

		[ServiceProvider(genericApi: true)]
		[Singleton<IService, Alpha>("a")]
		[Singleton<IService, Beta>("b")]
		public partial class Container;

		public static class CallSites
		{
			public static IService[] All(Container c) => c.GetRequiredServices<IService>();
		}
		""";

	[Fact]
	public void TheCacheOfAnArrayOfSingletonsIsAStaticFieldOfTheInterceptorClass()
	{
		var result = GeneratorHarness.Run(AllSingletonSource);

		result.Errors.Should().BeEmpty();

		var code = result.Source("Container");

		code.Should().Contain("private static global::Probe.IService[]? __interceptorCache1;");
		code.Should().Contain("=> __interceptorCache1 ??= [");
	}

	[Fact]
	public void TheCacheOfAnArrayWithScopedElementsIsAnInstanceFieldOfTheContainer()
	{
		var result = GeneratorHarness.Run(
			AllSingletonSource.Replace("[Singleton<IService, Beta>(\"b\")]", "[Scoped<IService, Beta>(\"b\")]"));

		result.Errors.Should().BeEmpty();

		var code = result.Source("Container");

		// De instancia para que cada CreateScope() estrene el suyo, e 'internal' porque
		// quien lo lee es la clase de extensiones.
		code.Should().Contain("internal global::Probe.IService[]? __interceptorCache1;");
		code.Should().Contain("=> provider.__interceptorCache1 ??= [");
		code.Should().NotContain("static global::Probe.IService[]? __interceptorCache1");
	}

	[Fact]
	public void ATransientElementRemovesTheCacheFieldAltogether()
	{
		var result = GeneratorHarness.Run(
			AllSingletonSource.Replace("[Singleton<IService, Beta>(\"b\")]", "[Transient<IService, Beta>(\"b\")]"));

		result.Errors.Should().BeEmpty();
		result.Source("Container").Should().NotContain("__interceptorCache");
	}

	[Fact]
	public void AnInterceptorThatReturnsASingleServiceIsNotCachedBecauseItsMemberAlreadyIs()
	{
		var result = GeneratorHarness.Run(
			AllSingletonSource.Replace(
				"public static IService[] All(Container c) => c.GetRequiredServices<IService>();",
				"public static IService One(Container c) => c.GetRequiredKeyedService<IService>(\"a\");"));

		result.Errors.Should().BeEmpty();
		result.Source("Container").Should().NotContain("__interceptorCache");
	}

	const string AsyncMixedSource = """
		using SourceCrafter.DependencyInjection.Attributes;
		using System.Threading.Tasks;

		namespace Probe;

		public interface IService;
		public sealed class Alpha : IService;
		public sealed class Beta : IService;

		[ServiceProvider(genericApi: true)]
		[Singleton<IService>("a", source: nameof(_GetAlphaAsync))]
		[Scoped<IService, Beta>("b")]
		public partial class Container
		{
			private static Task<IService> _GetAlphaAsync() => Task.FromResult<IService>(new Alpha());
		}

		public static class CallSites
		{
			public static Task<IService[]> All(Container c) => c.GetRequiredServicesAsync<IService>();
		}
		""";

	[Fact]
	public void AnAsyncInterceptorCachesTheResolvedArrayAndNotTheTask()
	{
		var result = GeneratorHarness.Run(AsyncMixedSource);

		result.Errors.Should().BeEmpty();

		var code = result.Source("Container");

		// Guardar la tarea dejaria un fallo cacheado para siempre; guardar el array ya
		// resuelto no puede envenenar a nadie.
		code.Should().Contain("if (provider.__interceptorCache1 is { } __cached) return __cached;");
		code.Should().Contain("return provider.__interceptorCache1 = [");
		code.Should().NotContain("__interceptorCache1 = provider.Get");
	}

	[Fact]
	public void ASyncResolverInsideAnAsyncMultipleCallIsNotAwaited()
	{
		var result = GeneratorHarness.Run(AsyncMixedSource);

		// Quien decide si el elemento es una tarea es el resolvedor, no el sitio de llamada.
		// Con el AsyncKind del sitio se emitia 'await' sobre un IService (CS1061).
		result.Errors.Should().BeEmpty();

		var code = result.Source("Container");

		code.Should().Contain("var __t0 = provider.GetAlphaAsyncCached;");
		code.Should().NotContain("var __t1 =");
		code.Should().Contain("provider.B");
	}

	[Fact]
	public void ThreeRegistrationsSharingInterfaceKeyAndLifetimeStillGetTheirOwnMember()
	{
		var result = GeneratorHarness.Run("""
			using SourceCrafter.DependencyInjection.Attributes;

			namespace Probe;

			public interface IService;
			public sealed class Alpha : IService;
			public sealed class Beta : IService;
			public sealed class Gamma : IService;

			[ServiceProvider(genericApi: true)]
			[Singleton<IService, Alpha>("svc")]
			[Singleton<IService, Beta>("svc")]
			[Singleton<IService, Gamma>("svc")]
			public partial class Container;

			public static class CallSites
			{
				public static IService[] All(Container c) => c.GetRequiredKeyedServices<IService>("svc");
			}
			""");

		// El nombre del miembro se memoizaba por tipo *expuesto*, asi que estos tres
		// registros compartian entrada y salian los tres llamados 'Svc'.
		result.Errors.Should().BeEmpty();

		var code = result.Source("Container");

		code.Should().Contain("provider.Svc,");
		code.Should().Contain("provider.SvcBeta,");
		code.Should().Contain("provider.SvcGamma]");
	}
}

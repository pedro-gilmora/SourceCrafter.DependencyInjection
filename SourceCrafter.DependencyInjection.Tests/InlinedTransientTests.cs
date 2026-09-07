using System.Linq;
using System.Threading.Tasks;

using FluentAssertions;

using SourceCrafter.DependencyInjection.Attributes;

using Xunit;

namespace SourceCrafter.DependencyInjection.Tests.InlinedTransient;

public interface IAuth { int Id { get; } }
public sealed class Auth : IAuth { public int Id { get; } = System.Threading.Interlocked.Increment(ref Counter); internal static int Counter; }

public sealed class Audit(IAuth auth) { public IAuth Auth => auth; }

public interface IHandler { IAuth Auth { get; } }
public sealed class HandlerA(IAuth auth, Audit audit) : IHandler { public IAuth Auth => auth; public Audit Audit => audit; }
public sealed class HandlerB(IAuth auth) : IHandler { public IAuth Auth => auth; }

[ServiceContainer(generateServiceProviderApi: true)]
[Transient<IAuth>(source: nameof(_GetAuthAsync))]
[Transient<Audit>]
[Transient<IHandler, HandlerA>("a")]
[Transient<IHandler, HandlerB>("b")]
public partial class AllTransientContainer
{
	private static async Task<IAuth> _GetAuthAsync()
	{
		await Task.Yield();

		return new Auth();
	}
}

/// <summary>
/// Un transitorio cuyas dependencias tampoco estan cacheadas se inlinea en quien lo consume,
/// en vez de emitir una llamada a su miembro. Eso solo vale mientras su construccion quepa en
/// una <b>expresion</b>: en cuanto es asincrono y compone dependencias asincronas, necesita el
/// prologo de locales <c>__vN</c> y sus <c>await</c>, es decir un cuerpo de metodo entero.
/// </summary>
public class InlinedTransientTests
{
	const string AllTransientAsync = """
		using SourceCrafter.DependencyInjection.Attributes;
		using System.Threading.Tasks;

		namespace Probe;

		public interface IAuth;
		public sealed class Auth : IAuth;

		public sealed class Audit(IAuth auth);

		public interface IHandler;
		public sealed class HandlerA(IAuth auth, Audit audit) : IHandler;
		public sealed class HandlerB(IAuth auth) : IHandler;

		[ServiceContainer(generateServiceProviderApi: true)]
		[Transient<IAuth>(source: nameof(_GetAuthAsync))]
		[Transient<Audit>]
		[Transient<IHandler, HandlerA>("a")]
		[Transient<IHandler, HandlerB>("b")]
		public partial class Container
		{
			private static Task<IAuth> _GetAuthAsync() => Task.FromResult<IAuth>(new Auth());
		}

		public static class CallSites
		{
			public static Task<IHandler[]> All(Container c) => c.GetRequiredServicesAsync<IHandler>();
		}
		""";

	[Fact]
	public void AnAsyncTransientThatComposesAsyncDependenciesIsNotInlined()
	{
		var result = GeneratorHarness.Run(AllTransientAsync);

		// Antes salia 'new Probe.HandlerA(await __v0..., await __v1...)' dentro del
		// interceptor, con los locales sin declarar (CS0103) y un 'await' sobre el objeto
		// recien construido (CS1061).
		result.Errors.Should().BeEmpty();

		var code = result.Source("Container");

		code.Should().Contain("var __t0 = provider.GetAAsync();");
		code.Should().Contain("var __t1 = provider.GetBAsync();");

		// El interceptor delega; el 'new' solo puede aparecer dentro del miembro, que si
		// declara los locales que necesita.
		var interceptor = code[code.IndexOf("InterceptorCall1", System.StringComparison.Ordinal)..];

		interceptor.Should().NotContain("new global::Probe.HandlerA");
		interceptor.Should().NotContain("__v0");
	}

	[Fact]
	public void AParameterOfSuchATransientReadsItsMemberAndNotAnInlinedConstructor()
	{
		var result = GeneratorHarness.Run(AllTransientAsync);

		var code = result.Source("Container");

		// 'Audit' es transitorio y sin dependencias cacheadas, pero es asincrono y compone:
		// el local tiene que recibir la *tarea* que devuelve su miembro, porque acto seguido
		// se le consulta 'IsCompletedSuccessfully' y se le hace 'await'.
		code.Should().Contain("var __v1 = GetAuditAsync();");
		code.Should().Contain("__v1.IsCompletedSuccessfully");
	}

	[Fact]
	public void APurelySynchronousTransientIsStillInlined()
	{
		var result = GeneratorHarness.Run("""
			using SourceCrafter.DependencyInjection.Attributes;

			namespace Probe;

			public interface IAuth;
			public sealed class Auth : IAuth;
			public sealed class Audit(IAuth auth);

			public interface IHandler;
			public sealed class HandlerA(IAuth auth, Audit audit) : IHandler;

			[ServiceContainer(generateServiceProviderApi: true)]
			[Transient<IAuth, Auth>]
			[Transient<Audit>]
			[Transient<IHandler, HandlerA>("a")]
			public partial class Container;

			public static class CallSites
			{
				public static IHandler[] All(Container c) => c.GetRequiredServices<IHandler>();
			}
			""");

		result.Errors.Should().BeEmpty();

		// El arreglo no debe apagar el inlineado en general: sin nada asincrono la
		// construccion sigue cabiendo en una expresion.
		var code = result.Source("Container");

		code.Should().Contain("new global::Probe.Audit(");
		code.Should().NotContain("GetAudit()");
	}

	[Fact]
	public async Task TheAllTransientGraphResolvesCorrectlyAtRuntime()
	{
		var container = new AllTransientContainer();
		var handlers = await container.GetRequiredServicesAsync<IHandler>();

		handlers.Should().HaveCount(2);

		var a = handlers.OfType<HandlerA>().Single();

		a.Auth.Should().NotBeNull();
		a.Audit.Auth.Should().NotBeNull();

		// Todo es transitorio: cada punto de inyeccion recibe su propia instancia. Si el
		// generador reutilizara una tarea, estos identificadores coincidirian.
		a.Auth.Id.Should().NotBe(a.Audit.Auth.Id);

		handlers.OfType<HandlerB>().Single().Auth.Should().NotBeNull();
	}
}

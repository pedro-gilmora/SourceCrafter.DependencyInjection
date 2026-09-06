using System;
using System.Linq;

using FluentAssertions;

using Xunit;

namespace SourceCrafter.DependencyInjection.Tests;

/// <summary>
/// Comprueba que un interceptor multiple asincrono
/// (<c>GetRequiredServicesAsync&lt;T&gt;()</c>) se compone con la misma forma que un
/// resolver con dependencias asincronas: las tareas se materializan primero en locales
/// <c>__tN</c> y despues se espera cada una <b>en el sitio de su elemento</b>, sin
/// <c>Task.WhenAll</c>.
/// </summary>
public class InterceptorShapeTests
{
	/// <summary>
	/// Dos registros del mismo tipo con clave vacia y lifetimes distintos: es la unica
	/// forma de que <c>GetRequiredServicesAsync&lt;T&gt;()</c> recoja mas de un resolver.
	/// Las fabricas devuelven <c>Task&lt;IImplementation&gt;</c> y no
	/// <c>Task&lt;First&gt;</c> porque el generador no soporta covarianza (daria CS0029).
	/// </summary>
	const string AsyncMultipleContainer = """
		using SourceCrafter.DependencyInjection.Attributes;
		using System.Threading.Tasks;

		namespace Probe;

		public interface IImplementation { }
		public sealed class First : IImplementation { }
		public sealed class Second : IImplementation { }

		[ServiceContainer(generateServiceProviderApi: true)]
		[Singleton<IImplementation>(source: nameof(GetFirstAsync))]
		[Scoped<IImplementation>(source: nameof(GetSecondAsync))]
		public partial class Container
		{
			static Task<IImplementation> GetFirstAsync() => Task.FromResult<IImplementation>(new First());
			static Task<IImplementation> GetSecondAsync() => Task.FromResult<IImplementation>(new Second());
		}

		public static class CallSites
		{
			public static Task<IImplementation[]> All(Container c) => c.GetRequiredServicesAsync<IImplementation>();
		}
		""";

	[Fact]
	public void AnAsyncMultipleInterceptorMaterializesTheTasksBeforeAwaitingThem()
	{
		var body = InterceptorBodyOf(AsyncMultipleContainer);

		// Las tareas se lanzan antes de esperar ninguna: los awaits secuenciales de abajo
		// no las serializan, solo recogen resultados de trabajo ya en marcha.
		var t0 = body.IndexOf("var __t0 =", StringComparison.Ordinal);
		var t1 = body.IndexOf("var __t1 =", StringComparison.Ordinal);
		var firstAwait = body.IndexOf("await ", StringComparison.Ordinal);

		t0.Should().BeGreaterThan(-1);
		t1.Should().BeGreaterThan(t0);
		firstAwait.Should().BeGreaterThan(t1);
	}

	[Fact]
	public void AnAsyncMultipleInterceptorAwaitsEachElementInPlaceInsteadOfThroughWhenAll()
	{
		var body = InterceptorBodyOf(AsyncMultipleContainer);

		// Misma forma que un resolver con dependencias asincronas: se espera en el sitio
		// del elemento. WhenAll asigna 2.5x mas (Task[] de params + WhenAllPromise) sin
		// comprar concurrencia, porque las tareas ya estan arrancadas.
		body.Should().NotContain("WhenAll");
		body.Should().Contain("await __t0");
		body.Should().Contain("await __t1");
	}

	[Fact]
	public void AnAsyncMultipleInterceptorEmitsOneElementPerRegisteredResolver()
	{
		var body = InterceptorBodyOf(AsyncMultipleContainer);

		body.Split("await __t", StringSplitOptions.None).Length.Should().Be(3, "hay dos registros");
		body.Should().NotContain("__t2");
	}

	/// <summary>
	/// Grafo con dependencias compartidas: <c>Controller</c> pide <c>IAuthService</c> y
	/// ademas <c>IDatabase</c>, <c>count</c> y <c>reqId</c>, que ya son dependencias
	/// transitivas de <c>IAuthService</c>. Los tres ultimos parametros deben leer
	/// <c>.Result</c> en vez de volver a esperar.
	/// </summary>
	const string SharedDependenciesContainer = """
		using SourceCrafter.DependencyInjection.Attributes;
		using System;
		using System.Threading.Tasks;

		namespace Probe;

		public interface IDatabase { }
		public sealed class Database(Guid reqId) : IDatabase { }

		public interface IAuthService { }
		public sealed class AuthService(IDatabase db, int count) : IAuthService { }

		public interface IController;
		public sealed class Controller(IAuthService auth, IDatabase db, int count, Guid reqId) : IController;
		public sealed class AdminController(IAuthService auth, Guid reqId) : IController;

		[ServiceContainer(generateServiceProviderApi: true)]
		[Singleton("reqId", source: nameof(_GetReqIdAsync))]
		[Scoped("count", source: nameof(_GetCountAsync))]
		[Singleton<IDatabase, Database>]
		[Scoped<IAuthService, AuthService>]
		[Transient<IController, Controller>]
		[Singleton<IController, AdminController>]
		public partial class Container
		{
			private static Task<Guid> _GetReqIdAsync() => Task.FromResult(Guid.NewGuid());
			private static Task<int> _GetCountAsync() => Task.FromResult(1);
		}

		public static class CallSites
		{
			public static Task<IController[]> All(Container c) => c.GetRequiredServicesAsync<IController>();
		}
		""";

	[Fact]
	public void TheSecondAndThirdDependencyResolvedByAnEarlierOneOnlyReadTheirResult()
	{
		var result = GeneratorHarness.Run(SharedDependenciesContainer);

		result.Errors.Should().BeEmpty();

		var code = result.Source("Container");
		var controller = MemberBodyOf(code, "GetControllerAsync");

		// Solo el primer parametro espera. IDatabase, count y reqId ya fueron lanzados al
		// resolver IAuthService, asi que esperarlos otra vez seria trabajo repetido.
		Occurrences(controller, "await __v").Should().Be(1);
		controller.Should().Contain("__v1.Result /* resolved previously by param 0 */");
		controller.Should().Contain("__v2.Result /* resolved previously by param 0 */");
		controller.Should().Contain("__v3.Result /* resolved previously by param 0 */");

		var admin = MemberBodyOf(code, "GetAdminControllerAsync");

		Occurrences(admin, "await __v").Should().Be(1);
		admin.Should().Contain("__v1.Result /* resolved previously by param 0 */");
	}

	[Fact]
	public void TheInterceptorDelegatesToTheMembersThatApplyThatDeduplication()
	{
		var body = InterceptorBodyOf(SharedDependenciesContainer);

		// El interceptor no deduplica por su cuenta: cada elemento es un resolvedor
		// distinto, y la reutilizacion de dependencias compartidas ocurre dentro del
		// miembro al que delega.
		body.Should().Contain("var __t0 = provider.GetControllerAsync();");
		body.Should().Contain("var __t1 = provider.GetAdminControllerAsync();");
		body.Should().Contain("await __t0");
		body.Should().Contain("await __t1");
		body.Should().NotContain("WhenAll");
	}

	/// <summary>
	/// Mismo grafo que <c>GetEmployeeControllerAsync</c> pero con <b>todas</b> las
	/// dependencias del mismo tipo de interfaz, de modo que un solo
	/// <c>GetRequiredServicesAsync&lt;IService&gt;()</c> los recoja a los tres.
	///
	/// <para>Las claves son necesarias: sin ellas los tres registros comparten el grupo
	/// (tipo, "") y un parametro <c>IService</c> se vuelve ambiguo.</para>
	/// </summary>
	const string SingleInterfaceContainer = """
		using SourceCrafter.DependencyInjection.Attributes;
		using System.Threading.Tasks;

		namespace Probe;

		public interface IService;

		public sealed class Alpha : IService;
		public sealed class Beta(IService alpha) : IService;
		public sealed class Gamma(IService beta, IService alpha) : IService;

		[ServiceContainer(generateServiceProviderApi: true)]
		[Singleton<IService>("alpha", source: nameof(_GetAlphaAsync))]
		[Scoped<IService, Beta>("beta")]
		[Transient<IService, Gamma>("gamma")]
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
	public void WithEveryDependencyOnTheSameInterfaceTheResolverStillReusesWhatIsAlreadyResolved()
	{
		var result = GeneratorHarness.Run(SingleInterfaceContainer);

		result.Errors.Should().BeEmpty();

		// Gamma(beta, alpha): beta ya arrastra alpha, asi que el segundo parametro no
		// vuelve a esperar. Es la forma de GetEmployeeControllerAsync con un unico tipo.
		var gamma = MemberBodyOf(result.Source("Container"), "GetGammaAsync");

		Occurrences(gamma, "await __v").Should().Be(1);
		gamma.Should().Contain("__v1.Result /* resolved previously by param 0 */");
	}

	[Fact]
	public void AnUnkeyedMultipleCallSiteCollectsEveryResolverOfTheTypeIncludingTheKeyedOnes()
	{
		var result = GeneratorHarness.Run(SingleInterfaceContainer);

		// La declaracion sin clave debe existir aunque todos los registros tengan clave;
		// si no, el sitio de llamada no compila y el fallback que los recoge es inalcanzable.
		result.Errors.Should().BeEmpty();
		result.Source("Container").Should().Contain("Task<TOut[]> GetRequiredServicesAsync<TOut>()");

		var body = InterceptorBodyOf(SingleInterfaceContainer);

		body.Should().Contain("var __t0 = provider.GetAlphaAsyncCached;");
		body.Should().Contain("var __t1 = provider.GetBetaAsync();");
		body.Should().Contain("var __t2 = provider.GetGammaAsync();");

		// Gamma resuelve por el camino a Alpha y Beta, que son cacheados y por tanto
		// comparten la misma tarea: basta con esperar a Gamma.
		body.Should().Contain("var __r0 = await __t2;");
		body.Should().Contain("__t0.Result /* resolved previously by __t2 */");
		body.Should().Contain("__t1.Result /* resolved previously by __t2 */");
		Occurrences(body, "await __t").Should().Be(1);
	}

	/// <summary>
	/// Cadena en escalera donde el ultimo servicio resuelve a todos los anteriores.
	/// <c>Gamma</c> es transitorio a proposito: su tarea no se comparte, asi que debe
	/// seguir esperandose aunque otro elemento lo resuelva por el camino.
	/// </summary>
	const string ChainedContainer = """
		using SourceCrafter.DependencyInjection.Attributes;
		using System.Threading.Tasks;

		namespace Probe;

		public interface IService;

		public sealed class Alpha : IService;
		public sealed class Beta(IService alpha) : IService;
		public sealed class Gamma(IService beta, IService alpha) : IService;
		public sealed class Delta(IService gamma, IService beta, IService alpha) : IService;

		[ServiceContainer(generateServiceProviderApi: true)]
		[Singleton<IService>("alpha", source: nameof(_GetAlphaAsync))]
		[Scoped<IService, Beta>("beta")]
		[Transient<IService, Gamma>("gamma")]
		[Scoped<IService, Delta>("delta")]
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
	public void TheInterceptorOnlyAwaitsWhatIsNotAlreadyGuaranteedByAnotherElement()
	{
		var result = GeneratorHarness.Run(ChainedContainer);

		result.Errors.Should().BeEmpty();

		var body = InterceptorBodyOf(ChainedContainer);

		// Cuatro tareas se lanzan, pero solo dos se esperan: Alpha y Beta son cacheados y
		// otro elemento del array ya los resuelve con la misma tarea.
		Occurrences(body, "var __t").Should().Be(4);
		Occurrences(body, "await __t").Should().Be(2);

		body.Should().Contain(".Result /* resolved previously by __t");
		Occurrences(body, ".Result /* resolved previously by __t").Should().Be(2);
	}

	[Fact]
	public void ATransientElementIsStillAwaitedBecauseItsTaskIsNotShared()
	{
		var body = InterceptorBodyOf(ChainedContainer);

		// Gamma es transitorio: la llamada del interceptor fabrica una tarea distinta de la
		// que Delta resolvio por dentro, asi que su completitud no esta garantizada.
		body.Should().NotContain("__t2.Result");
	}

	static int Occurrences(string haystack, string needle)	{
		var n = 0;

		for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
			i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal)) n++;

		return n;
	}

	/// <summary>Devuelve el cuerpo del miembro desde su firma hasta el siguiente miembro.</summary>
	static string MemberBodyOf(string code, string memberName)
	{
		var start = code.IndexOf(" " + memberName + "()", StringComparison.Ordinal);

		start.Should().BeGreaterThan(-1, $"'{memberName}' debe existir");

		var end = code.IndexOf("\r\n\tpublic ", start + 1, StringComparison.Ordinal);

		return end < 0 ? code[start..] : code[start..end];
	}

	static string InterceptorBodyOf(string source)	{
		var result = GeneratorHarness.Run(source);

		result.Errors.Should().BeEmpty();

		var code = result.Source("Container");
		var start = code.IndexOf("InterceptorCall1", StringComparison.Ordinal);

		start.Should().BeGreaterThan(-1, "el sitio de llamada debe estar interceptado");

		return code[start..];
	}
}

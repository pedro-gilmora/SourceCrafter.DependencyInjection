using FluentAssertions;

using Microsoft.CodeAnalysis.CSharp;

using Xunit;

namespace SourceCrafter.DependencyInjection.Tests;

/// <summary>
/// Tests sobre el <b>texto que el generador emite</b>. Son los que faltaban: la suite
/// solo comprobaba comportamiento en ejecucion y contratos de metadatos, asi que las
/// regresiones de emision (un <c>{;</c> suelto, un <c>return default;</c> dentro de un
/// metodo <c>async</c>, un candado con el alcance equivocado) solo se detectaban a mano.
/// </summary>
public class GeneratedCodeTests
{
    const string SingletonAndScopedContainer = """
        using SourceCrafter.DependencyInjection.Attributes;

        namespace Probe;

        public interface ISvc { }
        public sealed class Svc : ISvc { }

        public sealed class Session : System.IAsyncDisposable
        {
            public System.Threading.Tasks.ValueTask DisposeAsync() => default;
        }

        [ServiceContainer]
        [Singleton<ISvc, Svc>]
        [Scoped<Session>]
        public partial class Container { }
        """;

    [Fact]
    public void TheHarnessProducesCompilableCode()
    {
        var result = GeneratorHarness.Run(SingletonAndScopedContainer);

        result.Errors.Should().BeEmpty();
        result.Sources.Should().NotBeEmpty();
    }

    /// <summary>
    /// Un scoped que depende de un singleton y de un transient que a su vez arrastra otro
    /// singleton. Sirve para comprobar el izado: ninguna de esas resoluciones puede quedar
    /// dentro del <c>lock</c> del scoped.
    /// </summary>
    const string CrossLifetimeContainer = """
        using SourceCrafter.DependencyInjection.Attributes;

        namespace Probe;

        public sealed class Config { }
        public sealed class Clock { }
        public sealed class Wrapper(Clock clock) { }
        public sealed class Session(Config config, Wrapper wrapper) { }

        [ServiceContainer]
        [Singleton<Config>]
        [Singleton<Clock>]
        [Transient<Wrapper>]
        [Scoped<Session>]
        public partial class Container { }
        """;

    // ---------- Candados ----------

    [Fact]
    public void SingletonLockIsStaticBecauseItsBackingFieldIs()
    {
        var code = GeneratorHarness.Run(SingletonAndScopedContainer).Source("Container");

        code.Should().Contain("private static global::Probe.Svc? _svc;");

        // Un campo 'static' vigilado con 'lock(this)' no ofrece exclusion alguna: cada
        // instancia del contenedor bloquearia un objeto distinto. El candado tiene que
        // tener el mismo alcance que el campo que protege.
        code.Should().Contain("private static readonly global::System.Threading.Lock __singletonLock = new();");
        code.Should().Contain("lock(__singletonLock)");
    }

    [Fact]
    public void ScopedResolversLockOnTheScopeItself()
    {
        var code = GeneratorHarness.Run(SingletonAndScopedContainer).Source("Container");

        code.Should().Contain("private global::Probe.Session? _session;");

        // 'this' ya es un objeto por ambito: sirve de candado sin asignar nada. Cada
        // System.Threading.Lock que dejamos de crear son 40 B, y se pagaban por servicio
        // scoped declarado aunque el ambito no resolviera ninguno.
        code.Should().Contain("lock(this)");
    }

    [Fact]
    public void SyncCachedResolversAllocateNoLockObjects()
    {
        var code = GeneratorHarness.Run(SingletonAndScopedContainer).Source("Container");

        // Un unico candado estatico para todos los singletons, y ninguno por dependencia.
        code.Should().NotContain("_svcLock");
        code.Should().NotContain("_sessionLock");
        code.Should().NotContain("__EnsureLock");
    }

    [Fact]
    public void CachedDependenciesAreResolvedBeforeTakingTheLock()
    {
        var code = GeneratorHarness.Run(CrossLifetimeContainer).Source("Container");

        var slowPath = Between(code, "__Create_session()", "public");

        // Esta es la condicion que hace seguro compartir un candado por lifetime: si el
        // scoped resolviera el singleton con 'this' ya tomado, otro hilo que fuese en el
        // orden inverso cerraria el ciclo de espera. Es exactamente el interbloqueo que
        // LockingStrategyTests reproduce para el esquema sin izado.
        var lockIndex = slowPath.IndexOf("lock(", System.StringComparison.Ordinal);

        lockIndex.Should().BeGreaterThan(-1);

        var beforeLock = slowPath[..lockIndex];

        beforeLock.Should().Contain("Config");
        beforeLock.Should().Contain("Wrapper");

        // Dentro del 'lock' solo quedan los locales ya resueltos.
        slowPath[lockIndex..].Should().NotContain("Config");
        slowPath[lockIndex..].Should().NotContain("Wrapper");
    }

    static string Between(string text, string start, string end)
    {
        var from = text.IndexOf(start, System.StringComparison.Ordinal);

        from.Should().BeGreaterThan(-1);

        var to = text.IndexOf(end, from, System.StringComparison.Ordinal);

        return to < 0 ? text[from..] : text[from..to];
    }

    [Fact]
    public void TheFastPathReadsTheBackingFieldOnlyOnce()
    {
        var code = GeneratorHarness.Run(SingletonAndScopedContainer).Source("Container");

        // Dos lecturas separadas pueden ver valores distintos; con Nullable<T> eso llega a
        // devolver el HasValue de una y el Value de otra. Medido, el local es gratis.
        code.Should().Contain("var __v = _session;");
        code.Should().Contain("if(__v is not null) return __v;");
    }

    // ---------- Tipo del candado ----------

    [Fact]
    public void LocksUseTheDedicatedLockTypeWhenTheLanguageSupportsIt()
    {
        var code = GeneratorHarness.Run(SingletonAndScopedContainer).Source("Container");

        // System.Threading.Lock evita la cabecera de sincronizacion del objeto y le da al
        // compilador la forma que sabe convertir en EnterScope.
        code.Should().Contain("private static readonly global::System.Threading.Lock __singletonLock = new();");
    }

    [Fact]
    public void LocksFallBackToObjectOnOlderLanguageVersions()
    {
        var result = GeneratorHarness.Run(SingletonAndScopedContainer, LanguageVersion.CSharp12);
        var code = result.Source("Container");

        // Con C# 12 el compilador no reconoce 'lock' sobre Lock: convertiria la variable a
        // object, volveria a Monitor y ademas avisaria (CS9216). Emitir object es mejor.
        code.Should().Contain("private static readonly object __singletonLock = new();");
        code.Should().NotContain("System.Threading.Lock");

        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void TheSlowPathLivesInItsOwnMethodSoTheGetterCanBeInlined()
    {
        var code = GeneratorHarness.Run(SingletonAndScopedContainer).Source("Container");

        // Si el 'lock' se queda en el cuerpo del getter, este deja de ser una lectura de
        // campo y pasa a tener una region protegida, con lo que el JIT no lo inserta en
        // linea. Medido en el banco de pruebas: 1,13 ns (2,07x el codigo a mano) antes de
        // separarlo y 0,55 ns (1,03x) despues, por delante de Jab y de Pure.DI.
        code.Should().Contain("MethodImplOptions.NoInlining");
        code.Should().Contain("__Create_svc()");
        code.Should().Contain("return __Create_svc();");

        // El getter no puede contener el candado.
        var getter = code[code.IndexOf("public global::Probe.ISvc Svc", StringComparison.Ordinal)..];
        getter[..getter.IndexOf("\t}", StringComparison.Ordinal)].Should().NotContain("lock(");
    }

    [Fact]
    public void TheSlowPathIsNeverStaticEvenForSingletons()
    {
        var code = GeneratorHarness.Run(SingletonAndScopedContainer).Source("Container");

        // El campo de respaldo de un singleton es 'static', pero su construccion puede
        // depender de resolvers de instancia: el generador permite que un singleton dependa
        // de un scoped. Un metodo estatico daria CS0120 en codigo que el usuario no escribio.
        code.Should().NotContain("private static global::Probe.Svc __Create_svc()");
    }

    [Fact]
    public void NestedArgumentsAreIndentedByNestingLevel()
    {
        const string source = """
            using SourceCrafter.DependencyInjection.Attributes;

            namespace Probe;

            public sealed class Inner { }
            public sealed class Middle(Inner a, Inner b) { }
            public sealed class Outer(Middle a, Middle b) { }

            [ServiceContainer]
            [Transient<Inner>]
            [Transient<Middle>]
            [Transient<Outer>]
            public partial class Container { }
            """;

        var code = GeneratorHarness.Run(source).Source("Container").Replace("\r\n", "\n");

        // El formato alto debe reflejar el arbol que realmente se construye: la sangria era
        // fija en todos los niveles, asi que un grafo profundo se leia como una lista plana.
        code.Should().Contain("\n\t\t\t\tnew global::Probe.Middle(\n\t\t\t\t\tnew global::Probe.Inner()");
    }

    // ---------- Constructor ----------

    [Fact]
    public void NoConstructorIsEmittedWhenThereIsNothingToInitialize()
    {
        var code = GeneratorHarness.Run(SingletonAndScopedContainer).Source("Container");

        // Con los candados perezosos, un contenedor que no use el token de vida no necesita
        // constructor. Eso ademas deja libre el constructor sin parametros para el usuario.
        code.Should().NotContain("public Container()");

        // _disposed se inicializa en su propia declaracion.
        code.Should().Contain("private bool _disposed = false;");
    }

    [Fact]
    public void AParameterlessConstructorWrittenByTheUserIsReported()
    {
        const string source = """
            using SourceCrafter.DependencyInjection.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Probe;

            public sealed class Svc;

            [ServiceContainer]
            [Singleton<Svc>(source: nameof(Create))]
            public partial class Container
            {
                public Container() { }

                static Task<Svc> Create(CancellationToken token) => Task.FromResult(new Svc());
            }
            """;

        var result = GeneratorHarness.Run(source);

        // Solo colisiona cuando el generador emite constructor, es decir cuando algun
        // resolver consume el token de vida. Sin el diagnostico, el usuario recibiria un
        // CS0111 apuntando a codigo que no escribio.
        result.HasDiagnostic("SCDI15").Should().BeTrue();
    }

    [Fact]
    public void AParameterlessConstructorIsAllowedWhenTheGeneratorDoesNotEmitOne()
    {
        const string source = """
            using SourceCrafter.DependencyInjection.Attributes;

            namespace Probe;

            public sealed class Svc : System.IAsyncDisposable
            {
                public System.Threading.Tasks.ValueTask DisposeAsync() => default;
            }

            [ServiceContainer]
            [Scoped<Svc>]
            public partial class Container
            {
                public Container() { }
            }
            """;

        var result = GeneratorHarness.Run(source);

        result.HasDiagnostic("SCDI15").Should().BeFalse();
        result.Errors.Should().BeEmpty();
    }

    // ---------- Token de cancelacion ----------

    const string TokenContainer = """
        using SourceCrafter.DependencyInjection.Attributes;
        using System.Threading;
        using System.Threading.Tasks;

        namespace Probe;

        [ServiceContainer(generateServiceProviderApi: true)]
        [Singleton("count", source: nameof(LoadAsync))]
        [Singleton("other", source: nameof(LoadWithoutTokenAsync))]
        public partial class Container
        {
            static Task<int> LoadAsync(CancellationToken token) => Task.FromResult(1);

            static Task<long> LoadWithoutTokenAsync() => Task.FromResult(2L);
        }
        """;

    [Fact]
    public void ResolversDoNotTakeACancellationTokenParameter()
    {
        var code = GeneratorHarness.Run(TokenContainer).Source("Container");

        // Un valor cacheado se entrega a todos los llamadores: grabar en el el token del
        // primero seria incorrecto. El proveedor usa siempre el suyo.
        //
        // Se comprueba linea a linea y no con cadenas sueltas: antes solo se buscaba la
        // forma del interceptor ("CancellationToken cancellationToken") y por eso paso
        // inadvertido que la API generica seguia declarando "token = default".
        var offenders = code
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Contains("CancellationToken")
                && !line.StartsWith("private readonly global::System.Threading.CancellationTokenSource ")
                && !line.StartsWith("private readonly global::System.Threading.CancellationToken ")
                && !line.Contains("__lifetimeCts"))
            .ToArray();

        offenders.Should().BeEmpty();
        code.Should().Contain("__lifetimeToken");
    }

    [Fact]
    public void TheGenericApiIsNotDuplicatedByTheCancellationToken()
    {
        var code = GeneratorHarness.Run(TokenContainer).Source("Container");

        // El comparador de firmas genericas incluia PassCancelToken. Al dejar de emitirse
        // el token, dos resolvedores que solo se distinguian por el producian el mismo
        // miembro dos veces. El contenedor declara justo ese par: una fabrica que pide
        // token y otra que no.
        code.Should().Contain("GetRequiredKeyedServiceAsync<TOut>", "the generic API must actually be emitted");

        foreach (var member in new[] { "GetRequiredKeyedServiceAsync<TOut>", "GetRequiredKeyedServicesAsync<TOut>" })
            System.Text.RegularExpressions.Regex
                .Matches(code, System.Text.RegularExpressions.Regex.Escape(member))
                .Count.Should().Be(1, $"'{member}' should be declared exactly once");
    }

    // ---------- Composicion de dependencias asincronas ----------

    const string AsyncGraphContainer = """
        using SourceCrafter.DependencyInjection.Attributes;
        using System.Threading.Tasks;

        namespace Probe;

        public sealed class Cfg { }
        public sealed class Db { }
        public sealed class Svc(Cfg cfg, Db db)
        {
            public Cfg Cfg { get; } = cfg;
            public Db Db { get; } = db;
        }

        [ServiceContainer]
        [Singleton(source: nameof(GetCfgAsync))]
        [Singleton(source: nameof(GetDbAsync))]
        [Singleton<Svc>]
        public partial class Container
        {
            static Task<Cfg> GetCfgAsync() => Task.FromResult(new Cfg());
            static Task<Db> GetDbAsync() => Task.FromResult(new Db());
        }
        """;

    [Fact]
    public void AsyncDependenciesAreAwaitedInPlaceInsteadOfThroughWhenAll()
    {
        var result = GeneratorHarness.Run(AsyncGraphContainer);
        var code = result.Source("Container");

        result.Errors.Should().BeEmpty();

        // 'await Task.WhenAll(...)' obligaba a que todos los parametros leyeran '.Result'.
        // Medido con el arnes endurecido, costaba ~3.8x mas tiempo y 2.5x mas memoria
        // (256 B vs 96 B) por el 'Task[]' de params y su promesa, y no aportaba
        // concurrencia: las tareas ya estan arrancadas antes de esperarlas.
        code.Should().NotContain("Task.WhenAll");

        // Cada dependencia que aun hay que esperar se espera en su propio sitio.
        code.Should().Contain("await __v0.ConfigureAwait(false)");
        code.Should().Contain("await __v1.ConfigureAwait(false)");
    }

    const string AsyncDedupContainer = """
        using SourceCrafter.DependencyInjection.Attributes;
        using System.Threading.Tasks;

        namespace Probe;

        public sealed class Cfg { }
        public sealed class Db { }
        public sealed class Svc(Cfg cfg, Db db)
        {
            public Cfg Cfg { get; } = cfg;
            public Db Db { get; } = db;
        }
        public sealed class Outer(Svc svc, Cfg cfg)
        {
            public Svc Svc { get; } = svc;
            public Cfg Cfg { get; } = cfg;
        }

        [ServiceContainer]
        [Singleton(source: nameof(GetCfgAsync))]
        [Singleton(source: nameof(GetDbAsync))]
        [Singleton<Svc>]
        [Singleton<Outer>]
        public partial class Container
        {
            static Task<Cfg> GetCfgAsync() => Task.FromResult(new Cfg());
            static Task<Db> GetDbAsync() => Task.FromResult(new Db());
        }
        """;

    [Fact]
    public void DependenciesAlreadyResolvedByAnEarlierParameterOnlyReadTheirResult()
    {
        var result = GeneratorHarness.Run(AsyncDedupContainer);
        var code = result.Source("Container");

        result.Errors.Should().BeEmpty();

        // 'Outer' pide Svc y Cfg, y Svc ya depende de Cfg: al esperar el primer parametro,
        // el segundo quedo completado. Leer '.Result' sobre una tarea completada no
        // suspende, y ahorra un segundo await que en la medicion costaba ~11 ns de los 23
        // del caso con las dos tareas ya completas.
        code.Should().Contain(".Result /* resolved previously by param");
        code.Should().NotContain("Task.WhenAll");
    }

    // ---------- Interceptores ----------

    const string TwoCallSitesContainer = """
        using SourceCrafter.DependencyInjection.Attributes;

        namespace Probe;

        public interface IA { }
        public sealed class A1 : IA { }
        public sealed class A2 : IA { }

        [ServiceContainer(generateServiceProviderApi: true)]
        [Transient<IA, A1>]
        [Singleton<IA, A2>]
        public partial class Container { }

        public static class CallSites
        {
            public static IA[] One(Container c) => c.GetRequiredServices<IA>();
            public static IA[] Two(Container c) => c.GetRequiredServices<IA>();
        }
        """;

    [Fact]
    public void TwoCallSitesShareOneInterceptorWithoutDuplicatingItsElements()
    {
        var result = GeneratorHarness.Run(TwoCallSitesContainer);
        var code = result.Source("Container");

        result.Errors.Should().BeEmpty();

        // Un interceptor multiple se arma recorriendo los resolvedores de UN sitio de
        // llamada. Si un segundo sitio reutilizaba el mismo interceptor, sus resolvedores
        // se acumulaban otra vez y el array salia con 2N elementos: ambos llamadores
        // recibian el doble de servicios, sin error de compilacion.
        var body = code[code.IndexOf("InterceptorCall1")..];

        Count(body, "new global::Probe.A1()").Should().Be(1);
        Count(body, "provider.A2").Should().Be(1);

        // Ambos sitios siguen interceptados por el mismo metodo.
        Count(code, "InterceptsLocation").Should().Be(2);
        Count(code, "InterceptorCall").Should().Be(1);

        static int Count(string haystack, string needle) => System.Text.RegularExpressions.Regex
            .Matches(haystack, System.Text.RegularExpressions.Regex.Escape(needle)).Count;
    }

    [Fact]
    public void TheProviderOwnsASingleTokenCopiedOnce()
    {
        var code = GeneratorHarness.Run(TokenContainer).Source("Container");

        // Campo, no propiedad: CancellationTokenSource.Token lanza ObjectDisposedException
        // una vez liberada la fuente, cosa que ocurre en Dispose.
        code.Should().Contain("private readonly global::System.Threading.CancellationToken __lifetimeToken;");
        code.Should().Contain("__lifetimeToken = __lifetimeCts.Token;");

        // Una sola fuente para todo el proveedor.
        System.Text.RegularExpressions.Regex
            .Matches(code, @"CancellationTokenSource __lifetimeCts")
            .Should().HaveCount(1);
    }

    [Fact]
    public void DisposingTheProviderCancelsItsToken()
    {
        var code = GeneratorHarness.Run(TokenContainer).Source("Container");

        code.Should().Contain("__lifetimeCts.Cancel();");
        code.Should().Contain("__lifetimeCts.Dispose();");
    }

    // ---------- Liberacion ----------

    [Fact]
    public void DisposersNullTheFieldBeforeReleasingIt()
    {
        var code = GeneratorHarness.Run(SingletonAndScopedContainer).Source("Container");

        var nulling = code.IndexOf("_session = null;", StringComparison.Ordinal);
        var releasing = code.IndexOf("__disposing", nulling, StringComparison.Ordinal);

        nulling.Should().BeGreaterThan(0, "el liberador debe anular el campo");
        releasing.Should().BeGreaterThan(nulling,
            "anular despues de liberar dejaria entregar una instancia ya desechada");
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        var code = GeneratorHarness.Run(SingletonAndScopedContainer).Source("Container");

        code.Should().Contain("if (_disposed)");
        code.Should().Contain("_disposed = true;");
    }

    [Fact]
    public void AsyncDisposeNeverReturnsDefaultInsideAnAsyncMethod()
    {
        const string source = """
            using SourceCrafter.DependencyInjection.Attributes;

            namespace Probe;

            public sealed class A : System.IAsyncDisposable
            {
                public System.Threading.Tasks.ValueTask DisposeAsync() => default;
            }

            public sealed class B : System.IAsyncDisposable
            {
                public System.Threading.Tasks.ValueTask DisposeAsync() => default;
            }

            [ServiceContainer]
            [Singleton<A>]
            [Scoped<B>]
            public partial class Container { }
            """;

        var result = GeneratorHarness.Run(source);

        // CS1997: 'return default;' dentro de un metodo async que devuelve ValueTask.
        result.Errors.Should().BeEmpty();
    }

    // ---------- Ambitos ----------

    [Fact]
    public void RootIsVirtualSoInheritedResolversDispatchDynamically()
    {
        var code = GeneratorHarness.Run(SingletonAndScopedContainer).Source("Container");

        code.Should().Contain("public virtual Container Root => this;");
        code.Should().Contain("public override Container Root => _root;");
    }

    [Fact]
    public void ScopedOverridesDisposalInsteadOfHidingIt()
    {
        var code = GeneratorHarness.Run(SingletonAndScopedContainer).Source("Container");

        code.Should().NotContain("public new global::System.Threading.Tasks.ValueTask DisposeAsync");
        code.Should().Contain("public override global::System.Threading.Tasks.ValueTask DisposeAsync()");
    }

    // ---------- Forma del archivo ----------

    [Fact]
    public void EveryGeneratedFileIsMarkedAsAutoGenerated()
    {
        var result = GeneratorHarness.Run(SingletonAndScopedContainer);

        foreach (var (name, text) in result.Sources)
            text.Should().StartWith("// <auto-generated/>", $"'{name}' debe declararse generado");
    }

    [Fact]
    public void GeneratedCodeUsesTabsAndHasNoTrailingWhitespace()
    {
        var code = GeneratorHarness.Run(SingletonAndScopedContainer).Source("Container");

        foreach (var line in code.Split('\n'))
        {
            var text = line.TrimEnd('\r');

            text.Should().Be(text.TrimEnd(), "no debe quedar espacio en blanco al final");

            var indent = text[..(text.Length - text.TrimStart().Length)];

            indent.Should().NotContain(" ", "la indentacion se normaliza a tabuladores");
        }
    }

    [Fact]
    public void GeneratedCodeHasNoEmptyStatements()
    {
        var code = GeneratorHarness.Run(SingletonAndScopedContainer).Source("Container");

        // Regresion del '{;' que se colaba cuando el resolver no estaba cacheado.
        code.Should().NotContain("{;");
    }

    // ---------- Determinismo ----------

    [Fact]
    public void RepeatedRunsProduceIdenticalOutput()
    {
        var results = GeneratorHarness.RunRepeatedly(SingletonAndScopedContainer, passes: 3);

        var first = results[0].Source("Container");

        foreach (var result in results.Skip(1))
        {
            result.Errors.Should().BeEmpty();
            result.Source("Container").Should().Be(first,
                "el generador debe ser estable entre pasadas incrementales");
        }
    }
}

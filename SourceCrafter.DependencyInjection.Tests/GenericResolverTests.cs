using FluentAssertions;
using Xunit;

namespace SourceCrafter.DependencyInjection.Tests;

/// <summary>
/// La API generica es el <b>fallback</b> de los interceptores. Un interceptor reemplaza la
/// llamada en el sitio y resuelve sin comparar nada, pero solo puede hacerlo cuando el
/// compilador enlaza el contenedor concreto en ese sitio. Cuando no lo logra (el contenedor
/// llega por una variable de tipo interfaz, o la llamada vive en otro ensamblado) la llamada
/// sobrevive y aterriza en estos miembros, que discriminan por tipo en ejecucion.
///
/// <para>Antes se emitian como firmas que lanzaban <c>NotImplementedException</c>, de modo
/// que el fallback no existia: lo que el interceptor no cubria, no resolvia.</para>
/// </summary>
public class GenericResolverTests
{
    const string Source = """
        using System.Threading.Tasks;
        using SourceCrafter.DependencyInjection.Attributes;

        namespace Probe;

        public interface IService;
        public sealed class Alpha : IService;
        public sealed class Beta : IService;
        public sealed class Node { }

        [ServiceProvider(genericApi: true)]
        [Singleton<IService, Alpha>]
        [Singleton<IService, Beta>("b")]
        [Singleton<Node>]
        [Singleton<int>("count", source: nameof(_GetCount))]
        public partial class Container
        {
            private static int _GetCount() => 1;
        }
        """;

    static string Generated() => GeneratorHarness.Run(Source).Source("Container");

    [Fact]
    public void TheGenericApiDispatchesInsteadOfThrowing()
    {
        var code = Generated();

        code.Should().NotContain(
            "GetRequiredService<TOut>() where TOut : notnull => throw",
            "the generic API is the interceptor fallback, so it must resolve and not throw");

        code.Should().Contain("if (this is global::SourceCrafter.DependencyInjection.IProvider<TOut>");
    }

    [Fact]
    public void NoMemberDiscriminatesByComparingTypeNames()
    {
        var code = Generated();

        // La prueba de tipo la resuelve el runtime por tabla de interfaces; comparar
        // typeof(TOut).FullName contra una lista de literales era lo que se elimino.
        code.Should().NotContain("switch (typeof(TOut).FullName)");
        code.Should().NotContain("{typeof(TOut).FullName}|{key}");
    }

    [Fact]
    public void AnUnkeyedServiceIsReachedByItsOwnProviderInterface()
    {
        var code = Generated();

        // El contenedor declara la interfaz y la implementa de forma explicita: es lo que
        // hace que 'this is IProvider<TOut>' resuelva sin comparar nada.
        code.Should().Contain("global::SourceCrafter.DependencyInjection.IProvider<global::Probe.Node>");
        code.Should().Contain("global::Probe.Node global::SourceCrafter.DependencyInjection.IProvider<global::Probe.Node>.GetService() =>");
    }

    [Fact]
    public void AKeyedServiceKeepsItsSwitchButResolvesByInterface()
    {
        var code = Generated();

        // La clave si es un dato de ejecucion, asi que conserva su switch; el tipo, no.
        code.Should().Contain("switch (key)");
        code.Should().Contain("case \"count\":");
        code.Should().Contain("internal interface IContainerCountProvider<T> where T : notnull");
        code.Should().Contain("IContainerCountProvider<TOut>");
    }

    [Fact]
    public void GetServiceReturnsNullWhereGetRequiredServiceThrows()
    {
        var code = Generated();

        // GetService<T>() es un inlining de GetRequiredService<T>() que no lanza.
        code.Should().Contain("public TOut GetService<TOut>() where TOut : notnull");
        code.Should().Contain("return default!;");
        code.Should().Contain("throw new global::System.InvalidOperationException");

        // El mensaje nombra el tipo completo: 'typeof(TOut)' a secas repite el nombre corto
        // para tipos homonimos de distintos espacios de nombres.
        code.Should().Contain("{typeof(TOut).FullName}");
    }

    [Fact]
    public void GetServiceResolvesNeitherKeyedNorAsync()
    {
        var code = Generated();

        var body = code[code.IndexOf("public TOut GetService<TOut>()", System.StringComparison.Ordinal)..];
        body = body[..body.IndexOf("\n\tpublic", System.StringComparison.Ordinal)];

        body.Should().NotContain("|{key}", "GetService does not resolve keyed registrations");
        body.Should().NotContain("await", "GetService does not resolve asynchronous registrations");
    }

    [Fact]
    public void TheKeyedSyncMemberTakesNoAsynchronousRegistration()
    {
        var code = Generated();

        var body = code[code.IndexOf("public TOut GetRequiredKeyedService<TOut>(string key)", System.StringComparison.Ordinal)..];
        body = body[..body.IndexOf("\n\tpublic", System.StringComparison.Ordinal)];

        body.Should().NotContain("await", "the synchronous keyed member only serves synchronous registrations");
    }

    [Fact]
    public void TheAsynchronousMembersAreAsync()
    {
        var code = Generated();

        foreach (var member in new[]
        {
            "GetRequiredServiceAsync",
            "GetRequiredServicesAsync",
            "GetRequiredKeyedServiceAsync",
            "GetRequiredKeyedServicesAsync",
        })
        {
            code.Should().Contain("public async global::System.Threading.Tasks.Task<TOut" , $"{member} must be async");
        }
    }

    [Fact]
    public void AnUnkeyedPluralCallCollectsTheKeyedRegistrationsToo()
    {
        var code = Generated();

        const string member = "global::Probe.IService[] global::SourceCrafter.DependencyInjection.IMultipleProvider<global::Probe.IService>.GetServices() =>";

        code.Should().Contain(member);

        var body = code[code.IndexOf(member, System.StringComparison.Ordinal)..];
        body = body[..body.IndexOf(';')];

        // Alpha (sin clave) y Beta (con clave "b") exponen ambos IService.
        body.Should().Contain("[");
        body.Should().Contain(",", "both the unkeyed and the keyed registration belong to the array");
    }

    [Fact]
    public void AnInlinedTransientIsStillAnElementOfThePluralMember()
    {
        // Un transient sin dependencias se inlinea y no genera miembro. Descartarlo por eso
        // hacia desaparecer una registracion entera: IA quedaba con un solo elemento.
        const string source = """
            using SourceCrafter.DependencyInjection.Attributes;

            namespace Probe;

            public interface IA;
            public record A : IA;
            public record AA : IA;

            [ServiceProvider(genericApi: true)]
            [Transient<IA, A>]
            [Singleton<IA, AA>]
            public partial class Box;
            """;

        var code = GeneratorHarness.Run(source).Source("Box");

        const string member = "global::Probe.IA[] global::SourceCrafter.DependencyInjection.IMultipleProvider<global::Probe.IA>.GetServices() =>";

        code.Should().Contain(member);

        var body = code[code.IndexOf(member, System.StringComparison.Ordinal)..];
        body = body[..body.IndexOf(';')];

        body.Should().Contain("new global::Probe.A()", "the inlined transient rebuilds its value in place");
        body.Should().Contain(",", "both the inlined transient and the singleton belong to the array");
    }

    /// <summary>
    /// Un grupo cuyos registros son <b>todos</b> sincronos no declara interfaz plural
    /// asincrona: no hay nada que esperar, asi que el miembro solo podia envolver en una
    /// tarea ya completada un array que el llamante obtiene sin esperar. La peticion
    /// asincrona sigue resolviendo, cayendo a la interfaz plural sincrona.
    /// </summary>
    [Fact]
    public void AFullySynchronousGroupDeclaresNoAsyncPluralInterface()
    {
        const string source = """
            using SourceCrafter.DependencyInjection.Attributes;

            namespace Probe;

            public interface IA;
            public record A : IA;
            public record AA : IA;

            [ServiceProvider(genericApi: true)]
            [Singleton<IA, A>]
            [Singleton<IA, AA>]
            public partial class Box;
            """;

        var result = GeneratorHarness.Run(source);
        var code = result.Source("Box");

        code.Should().Contain("IMultipleProvider<global::Probe.IA>");
        code.Should().NotContain("IMultipleAsyncProvider<global::Probe.IA>");

        // Y la API asincrona no reparte proveedores sincronos: no cae a la interfaz plural
        // sincrona, que es la superficie que ya cubre esos registros.
        code.Should().NotContain("IMultipleProvider<TOut> __p1");

        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void TheGeneratedDispatcherCompiles()
    {
        GeneratorHarness.Run(Source).Errors.Should().BeEmpty();
    }
}

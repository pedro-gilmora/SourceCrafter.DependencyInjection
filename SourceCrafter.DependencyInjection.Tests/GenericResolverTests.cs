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

        code.Should().Contain("switch (typeof(TOut).FullName)");
    }

    [Fact]
    public void AnUnkeyedServiceIsReachedByItsRuntimeTypeName()
    {
        // La etiqueta del case es una constante de compilacion, asi que tiene que coincidir
        // exactamente con lo que typeof(T).FullName devuelve en ejecucion: sin 'global::' y
        // con el namespace separado por puntos.
        Generated().Should().Contain("case \"Probe.Node\":");
    }

    [Fact]
    public void AKeyedServiceIsDiscriminatedByTypeAndKeyInASingleSwitch()
    {
        var code = Generated();

        code.Should().Contain("switch ($\"{typeof(TOut).FullName}|{key}\")",
            "type and key are resolved in one comparison instead of nesting two switches");

        code.Should().Contain("case \"System.Int32|count\":");
    }

    [Fact]
    public void GetServiceReturnsNullWhereGetRequiredServiceThrows()
    {
        var code = Generated();

        // GetService<T>() es un inlining de GetRequiredService<T>() que no lanza.
        code.Should().Contain("public TOut GetService<TOut>() where TOut : notnull");
        code.Should().Contain("return default!;");
        code.Should().Contain("throw new global::System.InvalidOperationException");
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

        var body = code[code.IndexOf("public TOut[] GetRequiredServices<TOut>()", System.StringComparison.Ordinal)..];
        body = body[..body.IndexOf("\n\tpublic", System.StringComparison.Ordinal)];

        // Alpha (sin clave) y Beta (con clave "b") exponen ambos IService.
        body.Should().Contain("Probe.IService[] {");
        body.Should().Contain(",", "both the unkeyed and the keyed registration belong to the array");
    }

    [Fact]
    public void TheGeneratedDispatcherCompiles()
    {
        GeneratorHarness.Run(Source).Errors.Should().BeEmpty();
    }
}

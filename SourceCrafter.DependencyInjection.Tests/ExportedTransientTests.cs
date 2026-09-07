using FluentAssertions;
using Xunit;

namespace SourceCrafter.DependencyInjection.Tests;

/// <summary>
/// Un transient sin dependencias se inlinea en el call site y por defecto no genera miembro.
/// Eso lo deja irresoluble desde otro ensamblado: la interceptacion es por compilacion, asi
/// que los call sites de un consumidor externo nunca se reescriben y la API generica cae en
/// el stub que lanza. <c>exportTransients</c> lo expone como miembro con nombre.
/// </summary>
public class ExportedTransientTests
{
    const string Source = """
        using System.Threading.Tasks;
        using SourceCrafter.DependencyInjection.Attributes;

        namespace Probe;

        public sealed class Node { }
        public sealed class Leaf { }
        public sealed class Far { }

        [ServiceProvider(genericApi: true, exportTransients: true)]
        [Singleton<Node>]
        [Transient<Leaf>]
        [Transient<Far>(source: nameof(_GetFarAsync))]
        public partial class Exported
        {
            private static Task<Far> _GetFarAsync() => Task.FromResult(new Far());
        }

        [ServiceProvider(genericApi: true)]
        [Singleton<Node>]
        [Transient<Leaf>]
        [Transient<Far>(source: nameof(_GetFarAsync))]
        public partial class Default
        {
            private static Task<Far> _GetFarAsync() => Task.FromResult(new Far());
        }
        """;

    [Fact]
    public void ExportedTransientsGetANamedMember()
    {
        var result = GeneratorHarness.Run(Source);

        result.Errors.Should().BeEmpty();

        var exported = result.Source("Exported");

        exported.Should().Contain("public global::Probe.Leaf Leaf");

        // La declaracion, no el nombre suelto: el `_GetFarAsync` del autor tambien contiene
        // "FarAsync" y haria pasar la asercion sin que exista el miembro.
        exported.Should().Contain("Task<global::Probe.Far> FarAsync");
    }

    /// <summary>
    /// El comportamiento por defecto no cambia: sin la opcion, el transient sigue sin miembro
    /// y se resuelve unicamente inlinandolo en el call site.
    /// </summary>
    [Fact]
    public void WithoutTheOptionTransientsStayInlined()
    {
        var result = GeneratorHarness.Run(Source);

        var byDefault = result.Source("Default");

        byDefault.Should().Contain("public global::Probe.Node Node");
        byDefault.Should().NotContain("public global::Probe.Leaf Leaf");
        byDefault.Should().NotContain("Task<global::Probe.Far> FarAsync");
    }

    /// <summary>
    /// Exponer el miembro no debe apagar el inlinado: dentro de la propia compilacion el call
    /// site interceptado sigue construyendo la instancia en el sitio, sin pasar por el miembro.
    /// </summary>
    [Fact]
    public void ExportingDoesNotDisableInlining()
    {
        var result = GeneratorHarness.Run("""
            using SourceCrafter.DependencyInjection.Attributes;

            namespace Probe;

            public sealed class Leaf { }

            [ServiceProvider(genericApi: true, exportTransients: true)]
            [Transient<Leaf>]
            public partial class Exported { }

            public static class CallSite
            {
                public static Leaf Resolve(Exported c) => c.GetRequiredService<Leaf>();
            }
            """);

        result.Errors.Should().BeEmpty();

        result.Source("Exported").Should().Contain("new global::Probe.Leaf()");
    }

    /// <summary>
    /// La opcion se lee antes de registrar servicios, asi que el orden en que el autor escriba
    /// los atributos no puede alterar el resultado. Antes las opciones del contenedor se leian
    /// dentro del mismo bucle que registra los servicios.
    /// </summary>
    [Fact]
    public void TheOptionIsHonouredRegardlessOfAttributeOrder()
    {
        var result = GeneratorHarness.Run("""
            using SourceCrafter.DependencyInjection.Attributes;

            namespace Probe;

            public sealed class Leaf { }

            [Transient<Leaf>]
            [ServiceProvider(exportTransients: true)]
            public partial class AttributeAfter { }
            """);

        result.Errors.Should().BeEmpty();
        result.Source("AttributeAfter").Should().Contain("public global::Probe.Leaf Leaf");
    }
}

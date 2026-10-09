using FluentAssertions;

using Xunit;

namespace SourceCrafter.DependencyInjection.Tests;

/// <summary>
/// Un contenedor sin espacio de nombres (p.ej. una app de un solo archivo) no debe emitir
/// <c>namespace &lt;global namespace&gt;;</c>.
/// </summary>
public class GlobalNamespaceTests
{
    [Fact]
    public void AContainerInTheGlobalNamespaceEmitsNoNamespace()
    {
        var result = GeneratorHarness.Run("""
            using SourceCrafter.DependencyInjection.Attributes;

            public sealed class Service;

            [ServiceProvider]
            [Singleton<Service>]
            public partial class Container;
            """);

        result.Errors.Should().BeEmpty();
        result.Source("Container").Should().NotContain("namespace ");
    }
}

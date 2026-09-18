using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace SourceCrafter.DependencyInjection.Tests;

/// <summary>
/// El ayudante <c>__EnsureLock</c> vive en un unico archivo por compilacion, no dentro de
/// cada contenedor. Antes se emitia una copia identica en cada uno -el tipo del candado lo
/// decide la compilacion, no el contenedor- que encabezaba todos los archivos generados.
/// </summary>
public class SharedLockHelperTests
{
    /// <summary>
    /// Dos contenedores, cada uno con un singleton cacheado. Los singletons usan
    /// <c>LockOptions.Global</c> por defecto, y el candado global se crea de forma perezosa:
    /// son los que arrastran el ayudante.
    /// </summary>
    const string TwoAsyncContainers = """
        using System.Threading.Tasks;
        using SourceCrafter.DependencyInjection.Attributes;

        namespace Probe;

        public sealed class Alpha { }
        public sealed class Beta { }

        [ServiceProvider]
        [Singleton(source: nameof(GetAlphaAsync))]
        public partial class FirstContainer
        {
            static Task<Alpha> GetAlphaAsync() => Task.FromResult(new Alpha());
        }

        [ServiceProvider]
        [Singleton(source: nameof(GetBetaAsync))]
        public partial class SecondContainer
        {
            static Task<Beta> GetBetaAsync() => Task.FromResult(new Beta());
        }
        """;

    [Fact]
    public void TheHelperIsDeclaredOnlyOnceForTheWholeCompilation()
    {
        var result = GeneratorHarness.Run(TwoAsyncContainers);

        result.Errors.Should().BeEmpty();

        // Solo la declaracion nombra su parametro; los sitios de llamada pasan el campo.
        var declarations = result.Sources.Values
            .Count(source => Regex.IsMatch(source, @"__EnsureLock\(ref [^)]*\? location\)"));

        declarations.Should().Be(1);
    }

    [Fact]
    public void TheHelperLivesInItsOwnSharedFile()
    {
        var result = GeneratorHarness.Run(TwoAsyncContainers);

        var shared = result.Source("Locks");

        shared.Should().Contain("internal static class Locks");
        shared.Should().Contain("__EnsureLock(ref ");
        shared.Should().Contain("Interlocked.CompareExchange");
    }

    /// <summary>
    /// Se trae con <c>using static</c> en vez de cualificar cada llamada: el ayudante aparece
    /// dentro de un <c>lock(...)</c> por cada resolver asincrono cacheado, y cualificarlos
    /// costaria mas texto del que ahorra centralizar el cuerpo.
    /// </summary>
    [Fact]
    public void ContainersReachTheHelperThroughUsingStatic()
    {
        var result = GeneratorHarness.Run(TwoAsyncContainers);

        foreach (var name in new[] { "FirstContainer", "SecondContainer" })
        {
            var source = result.Source(name);

            source.Should().Contain("using static global::SourceCrafter.DependencyInjection.Extensions.Locks;");
            source.Should().NotContain("__EnsureLock(ref global::");
            source.Should().Contain("__EnsureLock(ref ");
        }
    }

    /// <summary>
    /// Un contenedor cuyas dependencias cacheadas son todas scoped se vigila con
    /// <c>lock(this)</c>: no hay ningun campo de candado que crear, asi que ni arrastra el
    /// <c>using</c> ni provoca la emision del archivo compartido.
    /// </summary>
    [Fact]
    public void ContainersThatNeedNoInstanceLockDoNotPullTheFile()
    {
        var result = GeneratorHarness.Run("""
            using SourceCrafter.DependencyInjection.Attributes;

            namespace Probe;

            public sealed class Plain { }

            [ServiceProvider]
            [Scoped<Plain>]
            public partial class SyncContainer { }
            """);

        result.Errors.Should().BeEmpty();
        result.Sources.Keys.Should().NotContain(name => name.Contains("Locks"));
        result.Source("SyncContainer").Should().NotContain("using static");
    }
}

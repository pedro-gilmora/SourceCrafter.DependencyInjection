using FluentAssertions;
using Xunit;

namespace SourceCrafter.DependencyInjection.Tests;

/// <summary>
/// Comprueba el punto de extension de generadores parciales de extremo a extremo.
///
/// <para>
/// No usa el arnes de <c>CSharpAnalyzerTest</c> a proposito: ese camino instancia el generador
/// dentro del proceso de pruebas, donde el ensamblado
/// <c>SourceCrafter.DependencyInjection.Partial.ServiceCatalog</c> no esta cargado. Lo que se
/// valida aqui es la compilacion real del proyecto, que si lleva los dos como analizadores.
/// </para>
/// </summary>
public class PartialGeneratorTests
{
    [Fact]
    public void ThePartialContributesAnExtraDeclarationOverTheGeneratedContainer()
    {
        // Si el parcial no se hubiese descubierto, este miembro no existiria y el proyecto
        // no compilaria.
        Server.ServiceCatalog.Should().NotBeEmpty();
    }

    [Fact]
    public void TheCatalogDescribesEveryRegistrationWithItsLifetimeAndMember()
    {
        Server.ServiceCatalog.Should().Contain(entry =>
            entry.StartsWith("Singleton ") && entry.Contains("IDatabase"));

        Server.ServiceCatalog.Should().Contain(entry =>
            entry.StartsWith("Scoped ") && entry.Contains("IAuthService"));

        // Un transient simple no tiene miembro al que reenviar: se reconstruye en el sitio.
        Server.ServiceCatalog.Should().Contain(entry => entry.EndsWith("-> <inlined>"));
    }

    [Fact]
    public void EveryCatalogEntryPointsAtSomething()
    {
        Server.ServiceCatalog.Should().OnlyContain(entry => entry.Contains(" -> "));
    }

    /// <summary>
    /// La marca <c>iface</c> sale de <c>ITypeSymbol.TypeKind</c>, no del nombre: el parcial no
    /// podria distinguir una interfaz de una clase leyendo solo la cadena del tipo.
    /// </summary>
    [Fact]
    public void TheCatalogDistinguishesInterfacesUsingTheSemanticModel()
    {
        Server.ServiceCatalog.Should().Contain(entry =>
            entry.Contains(".IA ") && entry.Contains(" iface "));

        // `B` es una clase: la ausencia de la marca es tan significativa como su presencia.
        Server.ServiceCatalog.Should().NotContain(entry =>
            entry.Contains(".B ") && entry.Contains(" iface "));
    }

    /// <summary>
    /// <c>disposable</c> exige recorrer <c>AllInterfaces</c> del simbolo y compararlo con el
    /// <c>System.IDisposable</c> resuelto desde la <c>Compilation</c>. Es la prueba de que el
    /// parcial ve el grafo de tipos completo, no una proyeccion.
    /// </summary>
    [Fact]
    public void TheCatalogWalksTheTypeGraphToDetectDisposables()
    {
        DisposableRootProbeContainer.ServiceCatalog.Should().Contain(entry =>
            entry.Contains("DisposableRootProbe") && entry.Contains(" disposable "));
    }
}

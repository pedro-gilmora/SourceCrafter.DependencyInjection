using FluentAssertions;
using Xunit;

namespace SourceCrafter.DependencyInjection.Tests;

/// <summary>
/// Convenciones de nombre de los miembros generados. El nombre de un resolver con fabrica se
/// deriva del metodo del autor (<c>_GetAlphaAsync</c>), asi que sin cuidado acababa produciendo
/// propiedades llamadas <c>GetAlphaAsyncCached</c>: un prefijo que anuncia una operacion sobre
/// algo que no lo es.
/// </summary>
public class MemberNamingTests
{
    const string Source = """
        using System.Threading.Tasks;
        using SourceCrafter.DependencyInjection.Attributes;

        namespace Probe;

        public sealed class Far { }
        public sealed class Alpha { }
        public sealed class Plain { }
        public sealed class Composed(Far far) { }

        [ServiceProvider(exportTransients: true)]
        [Transient<Far>(source: nameof(_GetFarAsync))]
        [Scoped<Alpha>(source: nameof(_GetAlphaAsync))]
        [Transient<Plain>]
        [Transient<Composed>]
        public partial class Derived
        {
            private static Task<Far> _GetFarAsync() => Task.FromResult(new Far());
            private static Task<Alpha> _GetAlphaAsync() => Task.FromResult(new Alpha());
        }
        """;

    [Fact]
    public void PropertiesDoNotCarryTheGetPrefix()
    {
        var result = GeneratorHarness.Run(Source);

        result.Errors.Should().BeEmpty();

        var code = result.Source("Derived");

        code.Should().Contain("Task<global::Probe.Far> FarAsync");
        code.Should().Contain("Task<global::Probe.Alpha> AlphaAsyncCached");
        code.Should().NotContain("> GetFarAsync");
        code.Should().NotContain("> GetAlphaAsyncCached");
    }

    /// <summary>
    /// El prefijo si tiene sentido en lo que de verdad se emite como metodo, que es un resolver
    /// asincrono con dependencias asincronas.
    /// </summary>
    [Fact]
    public void MethodsKeepTheGetPrefix()
    {
        var result = GeneratorHarness.Run(Source);

        result.Source("Derived").Should().Contain("Task<global::Probe.Composed> GetComposedAsync()");
    }

    /// <summary>
    /// El campo de respaldo acompana al miembro, para que no queden un <c>_getAlphaAsyncCached</c>
    /// y un <c>AlphaAsyncCached</c> hablando del mismo servicio.
    /// </summary>
    [Fact]
    public void TheBackingFieldFollowsTheMemberName()
    {
        var result = GeneratorHarness.Run(Source);

        var code = result.Source("Derived");

        code.Should().Contain("_alphaAsyncCached");
        code.Should().NotContain("_getAlphaAsyncCached");
    }

    /// <summary>
    /// Un nombre pedido con <c>nameFormat</c> manda sobre el del metodo-fabrica. Antes lo pisaba
    /// siempre la fabrica, asi que la opcion se descartaba en silencio en cuanto el registro
    /// traia <c>source:</c>.
    /// </summary>
    [Fact]
    public void AnExplicitNameFormatWinsOverTheFactoryName()
    {
        var result = GeneratorHarness.Run("""
            using System.Threading.Tasks;
            using SourceCrafter.DependencyInjection.Attributes;

            namespace Probe;

            public sealed class Far { }
            public sealed class Alpha { }

            [ServiceProvider(exportTransients: true)]
            [Transient<Far>(source: nameof(_GetFarAsync), nameFormat: "Custom{0}")]
            [Scoped<Alpha>(source: nameof(_GetAlphaAsync), nameFormat: "Renamed{0}")]
            public partial class Formatted
            {
                private static Task<Far> _GetFarAsync() => Task.FromResult(new Far());
                private static Task<Alpha> _GetAlphaAsync() => Task.FromResult(new Alpha());
            }
            """);

        result.Errors.Should().BeEmpty();

        var code = result.Source("Formatted");

        code.Should().Contain("Task<global::Probe.Far> CustomAsync");
        code.Should().Contain("RenamedCachedAsync");
        code.Should().NotContain("FarAsync;");
        code.Should().NotContain("AlphaAsyncCached");
    }

    /// <summary>
    /// El recorte solo mira el patron <c>^Get[A-Z0-9]</c>: un nombre que empieza por "Get" pero
    /// sigue en minuscula es una palabra, no un prefijo.
    /// </summary>
    [Fact]
    public void AWordStartingWithGetIsNotTreatedAsAPrefix()
    {
        var result = GeneratorHarness.Run("""
            using System.Threading.Tasks;
            using SourceCrafter.DependencyInjection.Attributes;

            namespace Probe;

            public sealed class Thing { }

            [ServiceProvider(exportTransients: true)]
            [Scoped<Thing>(source: nameof(_Gettysburg))]
            public partial class Wordy
            {
                private static Task<Thing> _Gettysburg() => Task.FromResult(new Thing());
            }
            """);

        result.Errors.Should().BeEmpty();
        result.Source("Wordy").Should().Contain("GettysburgCachedAsync");
    }

    /// <summary>
    /// El nombre derivado de una fabrica no pasaba por el registro de nombres, asi que podia
    /// pisar a uno derivado del tipo. Quitar el prefijo <c>Get</c> agrando esa exposicion:
    /// una fabrica <c>_GetFar</c> pasa a llamarse <c>Far</c>, que es justo el nombre que le
    /// toca a un <c>[Transient&lt;Far&gt;]</c>. El contenedor no compilaba (CS0102).
    /// </summary>
    [Fact]
    public void AFactoryDerivedNameDoesNotCollideWithATypeDerivedOne()
    {
        var result = GeneratorHarness.Run("""
            using SourceCrafter.DependencyInjection.Attributes;

            namespace Probe;

            public sealed class Far { }
            public sealed class Near { }

            [ServiceProvider(exportTransients: true)]
            [Transient<Far>]
            [Transient<Near>(source: nameof(_GetFar))]
            public partial class Colliding
            {
                private static Near _GetFar() => new Near();
            }
            """);

        result.Errors.Should().BeEmpty();

        var code = result.Source("Colliding");

        code.Should().Contain("global::Probe.Far Far");
        code.Should().Contain("global::Probe.Near Far1");
    }

    /// <summary>
    /// La escalera prueba primero el nombre del tipo, pero en cuanto hay clave el desempate lo
    /// hace la clave y no el lifetime: si no, dos registros del mismo tipo saldrian como
    /// <c>Db</c> y <c>SingletonDb</c>, sin rastro de cual es cual.
    /// </summary>
    [Fact]
    public void TheKeyDisambiguatesRegistrationsOfTheSameType()
    {
        var result = GeneratorHarness.Run("""
            using SourceCrafter.DependencyInjection.Attributes;

            namespace Probe;

            public interface IDb { }
            public sealed class Db : IDb { }

            [ServiceProvider]
            [Singleton<IDb, Db>("read")]
            [Singleton<IDb, Db>("write")]
            [Singleton<IDb, Db>("admin")]
            public partial class Keyed { }
            """);

        result.Errors.Should().BeEmpty();

        var code = result.Source("Keyed");

        // El primero no necesita distincion; los dos siguientes se la ganan.
        code.Should().Contain("global::Probe.IDb Db\r\n");
        code.Should().Contain("global::Probe.IDb DbWrite");
        code.Should().Contain("global::Probe.IDb DbAdmin");
        code.Should().NotContain("SingletonDb");
    }

    /// <summary>
    /// <c>nameFormat</c> admite marcadores con nombre ademas del <c>{0}</c> historico, que
    /// sigue siendo la clave.
    /// </summary>
    [Fact]
    public void NameFormatAcceptsLifetimeKeyAndTypePlaceholders()
    {
        var result = GeneratorHarness.Run("""
            using SourceCrafter.DependencyInjection.Attributes;

            namespace Probe;

            public sealed class Thing { }
            public sealed class Other { }

            [ServiceProvider]
            [Scoped<Thing>("main", nameFormat: "{lifetime}{key}{tipo}")]
            [Scoped<Other>("aux", nameFormat: "The{type}For{key}")]
            public partial class Formatted { }
            """);

        result.Errors.Should().BeEmpty();

        var code = result.Source("Formatted");

        code.Should().Contain("global::Probe.Thing ScopedMainThing");
        code.Should().Contain("global::Probe.Other TheOtherForAux");

        // El campo sigue al miembro.
        code.Should().Contain("_scopedMainThing");
        code.Should().Contain("_theOtherForAux");
    }

    /// <summary>
    /// Un nombre pedido dos veces tiene que seguir produciendo un contenedor que compile. El
    /// ultimo peldano de la escalera es un contador en base 1.
    /// </summary>
    [Fact]
    public void ARepeatedExplicitNameFallsBackToACounter()
    {
        var result = GeneratorHarness.Run("""
            using SourceCrafter.DependencyInjection.Attributes;

            namespace Probe;

            public sealed class A { }
            public sealed class B { }

            [ServiceProvider]
            [Scoped<A>(nameFormat: "Same")]
            [Scoped<B>(nameFormat: "Same")]
            public partial class Repeated { }
            """);

        result.Errors.Should().BeEmpty();

        var code = result.Source("Repeated");

        code.Should().Contain("global::Probe.A Same\r\n");
        code.Should().Contain("global::Probe.B Same1");
    }
}

using FluentAssertions;

using Xunit;

namespace SourceCrafter.DependencyInjection.Tests;

/// <summary>
/// Especificacion de las <b>fabricas genericas</b>: un unico registro que sirve a toda una
/// familia de tipos construidos en vez de a uno concreto.
/// <para>
/// Esta primera tanda fija las dos reglas que condicionan todo lo demas -- restricciones
/// obligatorias y transient exclusivo -- antes de que exista emparejado. Son las que no
/// pueden relajarse despues sin romper a quien ya dependa de ellas.
/// </para>
/// </summary>
public class GenericFactoryTests
{
    /// <summary>
    /// Las restricciones son el criterio de emparejado, no un adorno: son lo que permite
    /// descartar candidatas. Un <c>T</c> libre aplica a todo tipo construido del mismo nombre
    /// generico y deja al generador sin base para elegir, asi que se rechaza al registrar.
    /// </summary>
    [Fact]
    public void AnUnconstrainedTypeParameterIsRejected()
    {
        var result = GeneratorHarness.Run("""
            using SourceCrafter.DependencyInjection.Attributes;

            namespace Probe;

            public interface ILogger<T> { }
            public sealed class Logger<T> : ILogger<T> { }

            [ServiceProvider]
            [Transient(source: nameof(_CreateLogger))]
            public partial class Container
            {
                private static ILogger<T> _CreateLogger<T>() => new Logger<T>();
            }
            """);

        result.HasDiagnostic("SCDI19").Should().BeTrue();
    }

    /// <summary>
    /// Con una restriccion cualquiera -- aqui <c>class</c> -- la fabrica es valida y no debe
    /// aparecer el diagnostico.
    /// </summary>
    [Fact]
    public void AConstrainedTypeParameterIsAccepted()
    {
        var result = GeneratorHarness.Run("""
            using SourceCrafter.DependencyInjection.Attributes;

            namespace Probe;

            public interface ILogger<T> { }
            public sealed class Logger<T> : ILogger<T> { }

            [ServiceProvider]
            [Transient(source: nameof(_CreateLogger))]
            public partial class Container
            {
                private static ILogger<T> _CreateLogger<T>() where T : class => new Logger<T>();
            }
            """);

        result.HasDiagnostic("SCDI19").Should().BeFalse();
    }

    /// <summary>
    /// Una restriccion de tipo tambien vale: no se exige <c>class</c> ni <c>struct</c> en
    /// concreto, solo que haya algo contra lo que emparejar.
    /// </summary>
    [Fact]
    public void AnInterfaceConstraintAlsoCounts()
    {
        var result = GeneratorHarness.Run("""
            using SourceCrafter.DependencyInjection.Attributes;

            namespace Probe;

            public interface IEntity { }
            public interface IRepo<T> { }
            public sealed class Repo<T> : IRepo<T> { }

            [ServiceProvider]
            [Transient(source: nameof(_CreateRepo))]
            public partial class Container
            {
                private static IRepo<T> _CreateRepo<T>() where T : IEntity => new Repo<T>();
            }
            """);

        result.HasDiagnostic("SCDI19").Should().BeFalse();
    }

    /// <summary>
    /// Cada parametro de tipo se valida por separado: que uno este restringido no cubre al otro.
    /// </summary>
    [Fact]
    public void EveryTypeParameterIsCheckedOnItsOwn()
    {
        var result = GeneratorHarness.Run("""
            using SourceCrafter.DependencyInjection.Attributes;

            namespace Probe;

            public interface IMapper<TIn, TOut> { }
            public sealed class Mapper<TIn, TOut> : IMapper<TIn, TOut> { }

            [ServiceProvider]
            [Transient(source: nameof(_CreateMapper))]
            public partial class Container
            {
                private static IMapper<TIn, TOut> _CreateMapper<TIn, TOut>() where TIn : class
                    => new Mapper<TIn, TOut>();
            }
            """);

        result.HasDiagnostic("SCDI19").Should().BeTrue();
    }

    /// <summary>
    /// <c>new()</c> queda fuera a proposito. Exige un constructor sin parametros, pero no acota
    /// el conjunto de tipos admisibles, asi que no sirve para descartar candidatas y no cuenta
    /// como restriccion a efectos de emparejado.
    /// </summary>
    [Fact]
    public void AConstructorConstraintDoesNotCountAsAConstraint()
    {
        var result = GeneratorHarness.Run("""
            using SourceCrafter.DependencyInjection.Attributes;

            namespace Probe;

            public interface IBox<T> { }
            public sealed class Box<T> : IBox<T> { }

            [ServiceProvider]
            [Transient(source: nameof(_CreateBox))]
            public partial class Container
            {
                private static IBox<T> _CreateBox<T>() where T : new() => new Box<T>();
            }
            """);

        result.HasDiagnostic("SCDI19").Should().BeTrue();
    }

    /// <summary>
    /// Un lifetime cacheado necesita un campo por tipo construido, y ese conjunto solo se
    /// conoce recorriendo a los consumidores. Mientras no se cierre, cachear significaria una
    /// superficie de memoria que no se ve en el sitio de registro.
    /// </summary>
    [Theory]
    [InlineData("Singleton")]
    [InlineData("Scoped")]
    public void ACachedLifetimeIsRejected(string lifetime)
    {
        var result = GeneratorHarness.Run($$"""
            using SourceCrafter.DependencyInjection.Attributes;

            namespace Probe;

            public interface ILogger<T> { }
            public sealed class Logger<T> : ILogger<T> { }

            [ServiceProvider]
            [{{lifetime}}(source: nameof(_CreateLogger))]
            public partial class Container
            {
                private static ILogger<T> _CreateLogger<T>() where T : class => new Logger<T>();
            }
            """);

        result.HasDiagnostic("SCDI22").Should().BeTrue();
    }

    /// <summary>
    /// El mismo registro como transient no debe disparar nada.
    /// </summary>
    [Fact]
    public void TransientIsTheAcceptedLifetime()
    {
        var result = GeneratorHarness.Run("""
            using SourceCrafter.DependencyInjection.Attributes;

            namespace Probe;

            public interface ILogger<T> { }
            public sealed class Logger<T> : ILogger<T> { }

            [ServiceProvider]
            [Transient(source: nameof(_CreateLogger))]
            public partial class Container
            {
                private static ILogger<T> _CreateLogger<T>() where T : class => new Logger<T>();
            }
            """);

        result.HasDiagnostic("SCDI22").Should().BeFalse();
    }

    /// <summary>
    /// Una fabrica no generica no debe verse afectada por ninguna de las dos reglas nuevas.
    /// </summary>
    [Fact]
    public void ANonGenericFactoryIsUntouched()
    {
        var result = GeneratorHarness.Run("""
            using SourceCrafter.DependencyInjection.Attributes;

            namespace Probe;

            public sealed class Svc { }

            [ServiceProvider]
            [Singleton(source: nameof(_CreateSvc))]
            public partial class Container
            {
                private static Svc _CreateSvc() => new Svc();
            }
            """);

        result.HasDiagnostic("SCDI19").Should().BeFalse();
        result.HasDiagnostic("SCDI22").Should().BeFalse();
    }

    /// <summary>
    /// Estado actual: <b>la factory generica se valida pero todavia no resuelve nada</b>.
    /// <para>
    /// Registrar la factory no hace que <c>ILogger&lt;OrderService&gt;</c> sea satisfacible: el
    /// consumidor falla con <c>SCDI03</c> ("no registrado") y el parametro se emite como
    /// <c>default!</c>. Falta el cierre por consumo, que es el paso que recoge los tipos
    /// construidos del grafo y los conecta con la factory.
    /// </para>
    /// <para>
    /// Este test fija el hueco a proposito. Cuando el emparejado exista, debe fallar y
    /// convertirse en la comprobacion de que la resolucion se emite.
    /// </para>
    /// </summary>
    [Fact]
    public void AGenericFactoryDoesNotYetSatisfyItsConsumers()
    {
        var result = GeneratorHarness.Run("""
            using SourceCrafter.DependencyInjection.Attributes;

            namespace Probe;

            public interface ILogger<T> { }
            public sealed class Logger<T> : ILogger<T> { }

            public sealed class OrderService(ILogger<OrderService> log)
            {
                public ILogger<OrderService> Log => log;
            }

            [ServiceProvider]
            [Transient(source: nameof(_CreateLogger))]
            [Transient<OrderService>]
            public partial class Container
            {
                private static ILogger<T> _CreateLogger<T>() where T : class => new Logger<T>();
            }
            """);

        result.HasDiagnostic("SCDI03").Should().BeTrue(
            "el cierre por consumo todavia no conecta la factory generica con sus consumidores");

        result.Source("Container").Should().Contain("default!",
            "sin resolucion el parametro se emite como default!");
    }
}

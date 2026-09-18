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
    /// El cierre por consumo: registrar la plantilla y un consumidor basta para que
    /// <c>ILogger&lt;OrderService&gt;</c> se resuelva.
    /// <para>
    /// El consumidor es la unica fuente que dice que ese tipo construido hace falta, asi que
    /// la llamada emitida debe llevar el argumento de tipo explicito: en el sitio de uso no
    /// hay nada de donde inferirlo.
    /// </para>
    /// </summary>
    [Fact]
    public void AGenericFactorySatisfiesItsConsumers()
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

        result.Errors.Should().BeEmpty();

        result.Source("Container").Should().Contain("_CreateLogger<global::Probe.OrderService>()",
            "la llamada necesita el argumento de tipo explicito");
    }

    /// <summary>
    /// Dos consumidores distintos cierran la misma plantilla contra tipos distintos. Cada uno
    /// recibe su propia construccion; no se comparte nada porque es transient.
    /// </summary>
    [Fact]
    public void EachConsumerClosesTheTemplateAgainstItsOwnType()
    {
        var result = GeneratorHarness.Run("""
            using SourceCrafter.DependencyInjection.Attributes;

            namespace Probe;

            public interface ILogger<T> { }
            public sealed class Logger<T> : ILogger<T> { }

            public sealed class OrderService(ILogger<OrderService> log);
            public sealed class Cart(ILogger<Cart> log);

            [ServiceProvider]
            [Transient(source: nameof(_CreateLogger))]
            [Transient<OrderService>]
            [Transient<Cart>]
            public partial class Container
            {
                private static ILogger<T> _CreateLogger<T>() where T : class => new Logger<T>();
            }
            """);

        result.Errors.Should().BeEmpty();

        var source = result.Source("Container");

        source.Should().Contain("_CreateLogger<global::Probe.OrderService>()");
        source.Should().Contain("_CreateLogger<global::Probe.Cart>()");
    }

    /// <summary>
    /// Una plantilla sin consumidores no emite nada. Es la contrapartida de ser transient sin
    /// cache: no hay campo, ni miembro, ni tipo construido que justificar.
    /// </summary>
    [Fact]
    public void ATemplateWithoutConsumersEmitsNothing()
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

        result.Errors.Should().BeEmpty();
        result.Sources.Should().BeEmpty("sin tipos construidos que resolver no hay nada que emitir");
    }

    /// <summary>
    /// Las restricciones seleccionan: entre dos plantillas, solo es candidata la que el tipo
    /// pedido satisface, y la otra se descarta en silencio sin que eso sea un error.
    /// </summary>
    [Fact]
    public void ConstraintsSelectTheApplicableTemplate()
    {
        var result = GeneratorHarness.Run("""
            using SourceCrafter.DependencyInjection.Attributes;

            namespace Probe;

            public interface IEntity { }
            public interface IRepo<T> { }
            public sealed class EntityRepo<T> : IRepo<T> where T : IEntity { }
            public sealed class ValueRepo<T> : IRepo<T> where T : struct { }

            public sealed class Customer : IEntity { }

            public sealed class Consumer(IRepo<Customer> repo);

            [ServiceProvider]
            [Transient(source: nameof(_CreateEntityRepo))]
            [Transient(source: nameof(_CreateValueRepo))]
            [Transient<Consumer>]
            public partial class Container
            {
                private static IRepo<T> _CreateEntityRepo<T>() where T : IEntity => new EntityRepo<T>();
                private static IRepo<T> _CreateValueRepo<T>() where T : struct => new ValueRepo<T>();
            }
            """);

        result.Errors.Should().BeEmpty();

        var source = result.Source("Container");

        source.Should().Contain("_CreateEntityRepo<global::Probe.Customer>()");
        source.Should().NotContain("_CreateValueRepo");
    }

    /// <summary>
    /// Cuando dos plantillas pueden producir el mismo tipo, no se inventa una precedencia por
    /// orden de declaracion: se pide desambiguar con una clave.
    /// </summary>
    [Fact]
    public void TwoApplicableTemplatesAreReportedAsAmbiguous()
    {
        var result = GeneratorHarness.Run("""
            using SourceCrafter.DependencyInjection.Attributes;

            namespace Probe;

            public interface ILogger<T> { }
            public sealed class Logger<T> : ILogger<T> { }

            public sealed class OrderService(ILogger<OrderService> log);

            [ServiceProvider]
            [Transient(source: nameof(_CreateLogger))]
            [Transient(source: nameof(_CreateOtherLogger))]
            [Transient<OrderService>]
            public partial class Container
            {
                private static ILogger<T> _CreateLogger<T>() where T : class => new Logger<T>();
                private static ILogger<T> _CreateOtherLogger<T>() where T : class => new Logger<T>();
            }
            """);

        result.HasDiagnostic("SCDI21").Should().BeTrue();
    }

    /// <summary>
    /// Si ninguna plantilla acepta el tipo pedido se informa la restriccion incumplida. Sin
    /// esto el fallo aparece como un servicio no registrado, que es cierto pero inutil: la
    /// factory existe, simplemente no admite ese tipo.
    /// </summary>
    [Fact]
    public void ATypeNoTemplateAcceptsReportsTheConstraint()
    {
        var result = GeneratorHarness.Run("""
            using SourceCrafter.DependencyInjection.Attributes;

            namespace Probe;

            public interface IEntity { }
            public interface IRepo<T> { }
            public sealed class Repo<T> : IRepo<T> where T : IEntity { }

            public sealed class Plain { }

            public sealed class Consumer(IRepo<Plain> repo);

            [ServiceProvider]
            [Transient(source: nameof(_CreateRepo))]
            [Transient<Consumer>]
            public partial class Container
            {
                private static IRepo<T> _CreateRepo<T>() where T : IEntity => new Repo<T>();
            }
            """);

        result.HasDiagnostic("SCDI20").Should().BeTrue();
    }
}

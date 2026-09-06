using FluentAssertions;

using SourceCrafter.DependencyInjection.Attributes;

using System;
using System.Reflection;

using Xunit;

namespace SourceCrafter.DependencyInjection.Tests
{
    #region Sondas

    /// <summary>Captura la instancia que el contenedor inyecta en un parametro <c>[Root]</c>.</summary>
    public sealed record RootProbe(PlainRootProbeContainer Injected);

    /// <summary>Igual que <see cref="RootProbe"/>, pero desechable, para forzar un scope con disposers.</summary>
    public sealed class DisposableRootProbe(DisposableRootProbeContainer injected) : IDisposable
    {
        public DisposableRootProbeContainer Injected => injected;

        public void Dispose() { }
    }

    /// <summary>Variante A: dependencia <em>scoped</em> <b>no</b> desechable.</summary>
    [ServiceContainer]
    [Scoped<RootProbe>(source: nameof(ResolveProbe))]
    public partial class PlainRootProbeContainer
    {
        static RootProbe ResolveProbe([Root] PlainRootProbeContainer root) => new(root);
    }

    /// <summary>
    /// Variante B: dependencia <em>scoped</em> desechable, que es la unica forma de
    /// que el emisor rellene el cuerpo de la clase <c>Scoped</c>.
    /// </summary>
    [ServiceContainer]
    [Scoped<DisposableRootProbe>(source: nameof(ResolveProbe))]
    public partial class DisposableRootProbeContainer
    {
        static DisposableRootProbe ResolveProbe([Root] DisposableRootProbeContainer root) => new(root);
    }

    #endregion

    /// <summary>
    /// Prueba del punto E del diagnostico: <c>[Root]</c> debe llegar a la raiz real
    /// aunque el resolver se invoque sobre un scope.
    ///
    /// <para><b>Causa original.</b> El unico cuerpo emitido vive en el contenedor base y
    /// referencia <c>Root</c>; como <c>Root</c> no era <c>virtual</c>, el acceso se
    /// enlazaba <b>estaticamente</b> a <c>Base.Root =&gt; this</c>. Al heredarlo,
    /// <c>this</c> era el scope.</para>
    ///
    /// <para><b>Correccion (Fase 3-F).</b> <c>Root</c> y <c>CreateScope()</c> pasan a ser
    /// <c>virtual</c>/<c>override</c> y se emiten fuera del bloque de disposabilidad, de
    /// modo que un contenedor con servicios scoped no desechables tambien los declara.
    /// Con eso, reexponer los resolvers en <c>Scoped</c> deja de ser necesario.</para>
    /// </summary>
    public class RootPropagationTests
    {
        const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        // ---------- Variante A: scoped no desechable ----------

        [Fact]
        public void _0_PlainScope_DeclaresRootAndCreateScope_EvenWithoutDisposers()
        {
            var scoped = typeof(PlainRootProbeContainer.Scoped);

            // Ya no dependen de que existan liberadores scoped.
            scoped.GetProperty("Root", Declared).Should().NotBeNull();
            scoped.GetMethod("CreateScope", Declared).Should().NotBeNull();

            // Reexponer el resolver sigue sin hacer falta: basta con que Root sea virtual.
            scoped.GetProperty("ResolveProbeCached", Declared).Should().BeNull();
        }

        [Fact]
        public void _1_PlainScope_RootProperty_ReturnsTheRootContainer()
        {
            var container = new PlainRootProbeContainer();
            var scope = container.CreateScope();

            scope.Root.Should().BeSameAs(container);
            scope.Root.Should().NotBeSameAs(scope);
        }

        [Fact]
        public void _2_PlainScope_InjectedRootParameter_ReceivesTheRootContainer()
        {
            var container = new PlainRootProbeContainer();
            var scope = container.CreateScope();

            container.ResolveProbeCached.Injected.Should().BeSameAs(container);

            scope.ResolveProbeCached.Injected.Should().BeSameAs(container);
            scope.ResolveProbeCached.Injected.Should().NotBeSameAs(scope);
        }

        [Fact]
        public void _2b_NestedScope_KeepsPointingToTheSameRoot()
        {
            var container = new PlainRootProbeContainer();
            var nested = container.CreateScope().CreateScope();

            nested.Root.Should().BeSameAs(container);
            nested.ResolveProbeCached.Injected.Should().BeSameAs(container);
        }

        // ---------- Variante B: scoped desechable ----------

        [Fact]
        public void _3_DisposableScope_OverridesRoot_InsteadOfHidingItWithNew()
        {
            var baseRoot = typeof(DisposableRootProbeContainer).GetProperty("Root", Declared);
            var scopedRoot = typeof(DisposableRootProbeContainer.Scoped).GetProperty("Root", Declared);

            baseRoot.Should().NotBeNull();
            scopedRoot.Should().NotBeNull();

            // Clave de la correccion: el cuerpo heredado despacha dinamicamente.
            baseRoot!.GetMethod!.IsVirtual.Should().BeTrue();
            scopedRoot!.GetMethod!.GetBaseDefinition().DeclaringType
                .Should().Be(typeof(DisposableRootProbeContainer));

            // Y sigue sin reexponer el resolver: no hace falta.
            typeof(DisposableRootProbeContainer)
                .GetProperty("ResolveProbeCached", Declared).Should().NotBeNull();

            typeof(DisposableRootProbeContainer.Scoped)
                .GetProperty("ResolveProbeCached", Declared).Should().BeNull();
        }

        [Fact]
        public void _4_DisposableScope_RootPropertyAndInjectedRootAgree()
        {
            var container = new DisposableRootProbeContainer();
            var scope = container.CreateScope();

            scope.Root.Should().BeSameAs(container);

            var injected = scope.ResolveProbeCached.Injected;

            injected.Should().BeSameAs(container);
            injected.Should().BeSameAs(scope.Root);
        }
    }
}

using System.Linq;

using FluentAssertions;

using Xunit;

namespace SourceCrafter.DependencyInjection.Tests;

/// <summary>
/// La extension de configuracion emite un <c>partial</c> aparte del contenedor. Roslyn
/// ejecuta los dos generadores sobre la <b>misma</b> compilacion de entrada, asi que
/// ninguno ve la salida del otro: el unico contrato entre ellos es el <b>nombre</b> del
/// miembro.
///
/// <para>Ese contrato se estaba incumpliendo en silencio. El contenedor principal emitia la
/// referencia a <c>Settings</c>, pero la extension no emitia absolutamente nada, y el
/// resultado era un contenedor que no compilaba (<c>CS0103</c>) sin ninguna pista de la
/// causa. Eran los dos errores que el repositorio arrastraba en <c>Test.Data</c>, dados por
/// buenos como "datos de prueba rotos".</para>
/// </summary>
public class ConfigurationCompositionTests
{
	/// <summary>
	/// <c>JsonConfigurationAttribute</c> admite <c>AttributeTargets.Assembly</c>, que es
	/// donde vive naturalmente: el fichero de configuracion es del ensamblado, no de un
	/// contenedor concreto. La extension solo miraba los atributos de la clase.
	/// </summary>
	const string AssemblyLevelConfiguration = """
		using SourceCrafter.DependencyInjection.Attributes;
		using SourceCrafter.DependencyInjection.MsConfiguration.Metadata;

		[assembly: JsonConfiguration]

		namespace Probe;

		public class AppSettings { public string? Setting1 { get; set; } }

		public class Database(AppSettings settings) { }

		[ServiceProvider]
		[JsonSetting<AppSettings>("AppSettings")]
		[Singleton<Database>]
		public partial class Container { }
		""";

	[Fact]
	public void AnAssemblyLevelJsonConfigurationIsHonored()
	{
		var result = GeneratorHarness.RunWithConfiguration(AssemblyLevelConfiguration);

		result.Sources.Keys.Should().Contain(k => k.Contains("msConfig"),
			"la extension tiene que emitir su parcial");
	}

	/// <summary>
	/// La comprobacion que de verdad importa: el contenedor completo compila. Cubre el
	/// contrato entre ambos generadores, no solo que cada uno emita algo.
	/// </summary>
	[Fact]
	public void TheContainerAndItsConfigurationPartialCompileTogether()
	{
		var result = GeneratorHarness.RunWithConfiguration(AssemblyLevelConfiguration);

		result.Errors.Should().BeEmpty();
	}

	/// <summary>
	/// El miembro que el contenedor referencia y el que la extension emite tienen que
	/// llamarse igual. Es todo lo que los une.
	/// </summary>
	[Fact]
	public void TheSettingMemberTheContainerReferencesIsTheOneTheExtensionEmits()
	{
		var result = GeneratorHarness.RunWithConfiguration(AssemblyLevelConfiguration);

		var container = result.Source("Container");
		var partial = result.Sources.First(s => s.Key.Contains("msConfig")).Value;

		container.Should().Contain("Settings", "el contenedor referencia el miembro");
		partial.Should().Contain("AppSettings Settings", "la extension lo emite");
	}

	/// <summary>
	/// Un <c>JsonSetting</c> solo se registra si su <c>JsonConfiguration</c> ya se
	/// proceso. Con una sola pasada eso dependia del orden en que el autor escribiera los
	/// atributos; aqui la configuracion va <b>despues</b> del setting a proposito.
	/// </summary>
	[Fact]
	public void TheOrderOfTheAttributesDoesNotMatter()
	{
		var result = GeneratorHarness.RunWithConfiguration("""
			using SourceCrafter.DependencyInjection.Attributes;
			using SourceCrafter.DependencyInjection.MsConfiguration.Metadata;

			namespace Probe;

			public class AppSettings { public string? Setting1 { get; set; } }

			public class Database(AppSettings settings) { }

			[ServiceProvider]
			[JsonSetting<AppSettings>("AppSettings")]
			[JsonConfiguration]
			[Singleton<Database>]
			public partial class Container { }
			""");

		result.Errors.Should().BeEmpty();
	}
}

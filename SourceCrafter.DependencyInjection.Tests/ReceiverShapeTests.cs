using System.Threading.Tasks;

using FluentAssertions;

using SourceCrafter.DependencyInjection.Attributes;

using Xunit;

namespace SourceCrafter.DependencyInjection.Tests.Receivers;

public interface IGreeter { string Hi { get; } }

public sealed class Greeter : IGreeter { public string Hi => "hi"; }

public interface IPerRequest { }

public sealed class PerRequest : IPerRequest { }

[ServiceProvider(genericApi: true)]
[Singleton<IGreeter, Greeter>]
[Scoped<IPerRequest, PerRequest>]
public partial class ReceiverContainer
{
	internal static ReceiverContainer Make() => new();
}

public sealed class Holder
{
	public ReceiverContainer Container { get; } = new();
}

/// <summary>
/// El interceptor se emite como metodo de extension, asi que su <c>this</c> admite cualquier
/// expresion. Antes el generador exigia que el receptor fuera un <b>identificador simple</b>,
/// asi que <c>new Container().GetRequiredService&lt;T&gt;()</c> no se reconocia como sitio
/// interceptable y caia en el stub que lanza <c>NotImplementedException</c> en ejecucion.
/// <para>
/// Estos tests se ejecutan de verdad: si un sitio no se interceptase, la llamada lanzaria.
/// </para>
/// </summary>
public class ReceiverShapeTests
{
	[Fact]
	public void AConstructionExpressionIsIntercepted()
	{
		new ReceiverContainer().GetRequiredService<IGreeter>().Hi.Should().Be("hi");
	}

	[Fact]
	public void AMethodCallResultIsIntercepted()
	{
		ReceiverContainer.Make().GetRequiredService<IGreeter>().Hi.Should().Be("hi");
	}

	[Fact]
	public void APropertyChainIsIntercepted()
	{
		var holder = new Holder();

		holder.Container.GetRequiredService<IGreeter>().Hi.Should().Be("hi");
	}

	[Fact]
	public void AnIndexedElementIsIntercepted()
	{
		var containers = new[] { new ReceiverContainer() };

		containers[0].GetRequiredService<IGreeter>().Hi.Should().Be("hi");
	}

	[Fact]
	public void AParenthesizedConstructionIsIntercepted()
	{
		(new ReceiverContainer()).GetRequiredService<IGreeter>().Hi.Should().Be("hi");
	}

	[Fact]
	public void APlainLocalStillWorks()
	{
		// La forma que ya funcionaba: el camino del identificador no debe haberse tocado.
		var container = new ReceiverContainer();

		container.GetRequiredService<IGreeter>().Hi.Should().Be("hi");
	}

	[Fact]
	public void AScopeCreatedInlineIsIntercepted()
	{
		// El caso encadenado: el receptor ya *es* de tipo Container.Scoped, sin variable
		// intermedia cuyo inicializador se pueda inspeccionar. Es justo lo que la version
		// anterior no podia resolver.
		var scope = new ReceiverContainer().CreateScope();

		scope.GetRequiredService<IPerRequest>().Should().NotBeNull();
	}
}

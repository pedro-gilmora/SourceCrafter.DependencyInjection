using StrongInject;

namespace Benchmarks;

/// <summary>
/// Contenedor de StrongInject sobre el grafo comun de <c>Services.cs</c>.
/// <para>
/// Dos diferencias semanticas que hay que tener presentes al leer la tabla:
/// </para>
/// <list type="bullet">
/// <item>
/// StrongInject no tiene el concepto de ambito de peticion. Lo mas parecido a un scoped es
/// <see cref="Scope.InstancePerResolution"/>, que comparte la instancia dentro de <b>una</b>
/// resolucion en vez de dentro de un ambito explicito.
/// </item>
/// <item>
/// Su modelo es de propiedad: <c>Resolve()</c> devuelve un <c>Owned&lt;T&gt;</c> que hay que
/// liberar. Eso asigna un envoltorio por resolucion, y es deliberado por su parte, no un
/// descuido: es como garantiza el desecho determinista.
/// </item>
/// </list>
/// </summary>
[Register(typeof(Settings), Scope.InstancePerDependency, typeof(ISettings))]
[Register(typeof(Database), Scope.SingleInstance, typeof(IDatabase))]
[Register(typeof(Session), Scope.InstancePerResolution, typeof(ISession))]
[Register(typeof(Leaf), Scope.InstancePerDependency)]
[Register(typeof(Level3), Scope.InstancePerDependency)]
[Register(typeof(Level2), Scope.InstancePerDependency)]
[Register(typeof(Level1), Scope.InstancePerDependency)]
public partial class StrongInjectContainer :
    IContainer<IDatabase>,
    IContainer<ISession>,
    IContainer<Level3>,
    IContainer<Level1>;

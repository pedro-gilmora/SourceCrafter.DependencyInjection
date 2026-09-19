using CircleDIAttributes;

namespace Benchmarks.HandCoded.Rivals;

/// <summary>
/// Contenedores de <b>CircleDI</b> forzados a <see cref="CreationTiming.Lazy"/>.
/// <para>
/// Existen para cerrar el unico hueco que quedaba en la comparacion: el banco medía a CircleDI
/// solo en su modo por defecto (eager, campos de solo lectura asignados en el constructor) y a
/// SourceCrafter en modo perezoso. Esa tabla enfrentaba <i>dos semanticas</i>, no dos
/// implementaciones, y por eso <see cref="Scenarios.HeadToHeadScopeBenchmark"/> la parte en dos
/// grupos que no se comparan entre si.
/// </para>
/// <para>
/// Con estos contenedores la comparacion pasa a ser de implementacion contra implementacion:
/// mismo contrato (construccion diferida al primer uso, con la sincronizacion que eso obliga),
/// distinto generador. Es la unica forma de saber si la brecha que se observa contra CircleDI es
/// una ventaja de <i>diseño</i> o simplemente la ventaja de no ser perezoso.
/// </para>
/// <para>
/// <b>La propiedad se llama <c>CreationTime</c> y el enum <c>CreationTiming</c>.</b> Existe tanto
/// en <c>[ServiceProvider]</c> (como valor por defecto de todo el contenedor) como en cada atributo
/// de lifetime, que lo sobrescribe. Aqui se pone por servicio para dejar explicito en el sitio de
/// registro que la pereza es la variable bajo estudio.
/// </para>
/// </summary>
[ServiceProvider]
[Singleton<SyncPlain>(CreationTime = CreationTiming.Lazy)]
[Singleton<SyncDisp>(CreationTime = CreationTiming.Lazy)]
[Singleton<SyncAsyncDisp>(CreationTime = CreationTiming.Lazy)]
public sealed partial class CircleLazySingletonContainer;

[ServiceProvider]
[Scoped<SyncPlain>(CreationTime = CreationTiming.Lazy)]
[Scoped<SyncDisp>(CreationTime = CreationTiming.Lazy)]
[Scoped<SyncAsyncDisp>(CreationTime = CreationTiming.Lazy)]
public sealed partial class CircleLazyScopedContainer;

using Jab;

namespace Benchmarks.HandCoded.Rivals;

/// <summary>
/// Contenedores de <b>Jab</b> sobre el subconjunto sincrono del grafo comun.
/// <para>
/// Van tres clases y no una porque <b>ninguna de estas librerias admite registrar el mismo tipo
/// con dos lifetimes distintos</b>: el tipo es la clave del registro. Separarlos por lifetime es
/// la unica forma de medir los tres sobre los <i>mismos</i> tipos de servicio, que es lo que
/// hace comparables las tablas entre si.
/// </para>
/// <para>
/// El subconjunto es sincrono porque ninguna de las cuatro librerias tiene fabricas asincronas.
/// Eso no es una omision del banco: es que ese eje no tiene con quien compararse.
/// </para>
/// </summary>
[ServiceProvider]
[Singleton<SyncPlain>]
[Singleton<SyncDisp>]
[Singleton<SyncAsyncDisp>]
public sealed partial class JabSingletonContainer;

[ServiceProvider]
[Scoped<SyncPlain>]
[Scoped<SyncDisp>]
[Scoped<SyncAsyncDisp>]
public sealed partial class JabScopedContainer;

[ServiceProvider]
[Transient<SyncPlain>]
[Transient<SyncDisp>]
[Transient<SyncAsyncDisp>]
public sealed partial class JabTransientContainer;

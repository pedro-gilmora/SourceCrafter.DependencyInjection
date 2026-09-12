using CircleDIAttributes;

namespace Benchmarks.HandCoded.Rivals;

/// <summary>
/// Contenedores de <b>CircleDI</b> sobre el subconjunto sincrono del grafo comun.
/// <para>
/// CircleDI es el unico de los cuatro rivales que <b>no tiene ningun primitivo de
/// sincronizacion</b>: construye sus singletons en el constructor y los deja en campos de solo
/// lectura, asi que es thread-safe por inmutabilidad y no por candados. Eso lo convierte en el
/// rival a batir en el camino caliente y en el ciclo del ambito, y es la razon por la que este
/// banco incluye una variante eager propia: sin ella la comparacion enfrentaria dos semanticas
/// distintas en vez de dos implementaciones.
/// </para>
/// </summary>
[ServiceProvider]
[Singleton<SyncPlain>]
[Singleton<SyncDisp>]
[Singleton<SyncAsyncDisp>]
public sealed partial class CircleSingletonContainer;

[ServiceProvider]
[Scoped<SyncPlain>]
[Scoped<SyncDisp>]
[Scoped<SyncAsyncDisp>]
public sealed partial class CircleScopedContainer;

[ServiceProvider]
[Transient<SyncPlain>]
[Transient<SyncDisp>]
[Transient<SyncAsyncDisp>]
public sealed partial class CircleTransientContainer;

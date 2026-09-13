using CircleDIAttributes;

namespace Benchmarks.HandCoded.Rivals;

/// <summary>
/// Contenedores de <b>CircleDI</b> sobre el subconjunto sincrono del grafo comun.
/// <para>
/// CircleDI es el unico de los cuatro rivales que <b>no resuelve perezosamente por defecto</b>:
/// construye sus singletons en el constructor y los deja en campos de solo lectura, asi que en esta
/// configuracion resolver no sincroniza nada y es thread-safe por inmutabilidad. Eso lo convierte en
/// el rival a batir en el camino caliente y en el ciclo del ambito, y es la razon por la que este
/// banco incluye una variante eager propia: sin ella la comparacion enfrentaria dos semanticas
/// distintas en vez de dos implementaciones.
/// </para>
/// <para>
/// <b>Ojo con el atajo de decir que "CircleDI no usa candados": es falso.</b> Con
/// <c>CreationTiming.Lazy</c> emite exactamente el mismo doble chequeo que este banco escribe a mano
/// (lectura no volatil fuera, <c>lock</c>, segunda prueba de nulo dentro), y para rastrear
/// transitorios desechables bloquea incluso en modo eager. Lo que ahorra aqui no es carecer de
/// candados, es no ser perezoso.
/// </para>
/// <para>
/// Y en como los toma lo hace mejor que este banco: bloquea sobre un <c>private readonly Lock</c>
/// dedicado, no sobre <c>this</c>, que es publicamente alcanzable. Ademas acierta en el eje, con un
/// candado por ambito para los scoped y uno por contenedor para los singleton.
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

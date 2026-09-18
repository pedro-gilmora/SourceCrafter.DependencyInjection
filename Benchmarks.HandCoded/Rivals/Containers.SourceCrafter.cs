using SourceCrafter.DependencyInjection.Attributes;

namespace Benchmarks.HandCoded.Rivals;

/// <summary>
/// Contenedores de <b>SourceCrafter.DependencyInjection</b> sobre el subconjunto sincrono del
/// grafo comun.
/// <para>
/// Es el generador de este repositorio, y esta aqui por una razon concreta: <b>la version escrita
/// a mano replica la forma que emite este generador</b>, asi que cualquier distancia entre las dos
/// filas es margen real de mejora del generador, no una diferencia de diseño. Si empatan, el
/// generador ya esta en el suelo de lo que se puede escribir a mano con esa semantica.
/// </para>
/// <para>
/// <c>exportTransients</c> es necesario para que un transitorio sin dependencias genere un miembro
/// con nombre; sin el se alinea en el sitio de llamada y no hay nada que medir.
/// </para>
/// </summary>
[ServiceProvider]
[Singleton<SyncPlain>]
[Singleton<SyncDisp>]
[Singleton<SyncAsyncDisp>]
public partial class ScSingletonContainer;

[ServiceProvider]
[Scoped<SyncPlain>]
[Scoped<SyncDisp>]
[Scoped<SyncAsyncDisp>]
public partial class ScScopedContainer;

[ServiceProvider(exportTransients: true)]
[Transient<SyncPlain>]
[Transient<SyncDisp>]
[Transient<SyncAsyncDisp>]
public partial class ScTransientContainer;

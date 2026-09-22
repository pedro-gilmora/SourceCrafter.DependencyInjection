using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

using Benchmarks.HandCoded.Devirtualization;

namespace Benchmarks.HandCoded.Scenarios;

/// <summary>
/// Devirtualizacion del resolver generico, sobre un contenedor escrito a mano de
/// <b>100 dependencias</b> (70 de referencia, 30 de valor, la mayoria combinadas).
/// <para>
/// El contenedor implementa <c>IProvider&lt;TProvide&gt;</c> cien veces y expone un unico despachador,
/// <c>Resolve&lt;TProvide&gt;()</c>, cuyo cuerpo entero es la prueba de tipo
/// <c>this is IProvider&lt;TProvide&gt;</c>. La pregunta de esta tabla es <b>cuanto de esa prueba
/// sobrevive al JIT</b>, y la respuesta no es una sino tres, segun lo que el sitio de llamada le
/// deje ver:
/// </para>
/// <list type="number">
///   <item><b>Miembro con nombre</b> -- <c>container.OrderEndpoint</c>. Ni prueba de tipo ni
///   llamada de interfaz: una lectura de campo. Es el suelo y el baseline de cada grupo.</item>
///   <item><b>Generico cerrado</b> -- <c>container.Resolve&lt;OrderEndpoint&gt;()</c>.
///   El argumento de tipo es constante en el sitio, asi que el JIT puede alinear el despachador y
///   plegar la prueba. Esta fila mide si <i>de hecho</i> lo hace.</item>
///   <item><b>Generico abierto</b> -- la llamada vive dentro de <c>Open&lt;TProvide&gt;(container)</c>,
///   que es lo que escribe cualquier manejador o repositorio generico. <b>Aqui se parte la tabla
///   en dos</b>: con <c>TProvide</c> de referencia el codigo es compartido (<c>__Canon</c>) y la prueba
///   se resuelve en ejecucion; con <c>TProvide</c> de valor hay una instanciacion propia por tipo y el
///   JIT vuelve a tener la constante.</item>
///   <item><b>Resolver abstracto</b> -- la resolucion pasa por un <see cref="Resolver"/>
///   abstracto, que es la forma que toma un contenedor inyectado detras de una abstraccion propia.
///   Sin tipo estatico concreto no hay nada que plegar.</item>
/// </list>
/// <para>
/// <b>Por que 100 dependencias y no tres.</b> La prueba de tipo la resuelve el runtime recorriendo
/// la tabla de interfaces del objeto, cuyo coste depende de su tamano. Un contenedor de tres
/// servicios cabe entero en cache y empata con cualquier cosa; lo que se paga en produccion es el
/// contenedor de una aplicacion real.
/// </para>
/// <para>
/// <b>Por que a mano y no con el generador.</b> El generador rechaza en compilacion los sitios de
/// llamada con el parametro de tipo abierto (SCDI11), que son la mitad de esta tabla -- y lo hace
/// por nombre de metodo, de ahi que el despachador de aqui se llame <c>Resolve</c>. Replicando la
/// forma emitida se miden las cuatro filas bajo el mismo arnes; lo que se lea aqui vale como techo
/// de lo que el generador puede aspirar a conseguir.
/// </para>
/// <para>
/// <b>Como se lee.</b> Cada grupo tiene su propio baseline, porque comparar un tipo de valor
/// contra uno de referencia compara dos regimenes de compilacion y no dos formas de despacho. Y,
/// como siempre en este banco: primero <see cref="ControlBenchmark"/>; si sus filas identicas no
/// quedan agrupadas, esta tabla no es publicable. Por debajo del nanosegundo la unica evidencia
/// concluyente es el desensamblado (<c>--disasm</c>), no el tiempo.
/// </para>
/// <para>
/// <b>Primera corrida de sondeo</b> (<c>--fast</c>, tiempos no publicables, pero el orden de
/// magnitud si es informativo):
/// <list type="table">
///   <item><term>Referencia, con tipo estatico</term><description>las cuatro filas entre 0,52 y
///   0,58 ns, indistinguibles del metodo vacio: <b>el despacho desaparece</b>. Que el generico
///   abierto sobre referencia empate con la lectura de campo es el resultado interesante -- el JIT
///   alinea el metodo generico y, con el receptor sellado y una sola implementacion, resuelve la
///   prueba de tipo aunque el codigo sea compartido.</description></item>
///   <item><term>Valor, con tipo estatico</term><description>por debajo de 0,06 ns. Nada que
///   medir: la instanciacion propia por tipo pliega el despacho entero.</description></item>
///   <item><term>Resolver abstracto</term><description><b>17,5 ns en referencia y 2,46 ns en
///   valor</b>. Aqui esta el coste real y aqui se ve el eje del estudio: sin tipo estatico del
///   receptor, el codigo compartido de referencia paga la busqueda en la tabla de cien interfaces
///   en cada llamada, mientras que la instanciacion de valor conserva casi toda la
///   ventaja.</description></item>
///   <item><term>Conversion directa</term><description>empata con la prueba de tipo en los dos
///   grupos (17,60 frente a 17,54 ns; 2,40 frente a 2,46 ns): las diferencias caben dentro del
///   error. <b>Cambiar <c>is</c> por un cast no compra nada</b> -- las dos formas hacen la misma
///   busqueda y solo difieren en la ruta de fallo.</description></item>
///   <item><term>Sin comprobacion (sonda)</term><description>el resultado que reparte la culpa.
///   En referencia baja de 17,5 a <b>4,34 ns</b>: tres cuartas partes del coste son
///   <b>la busqueda del tipo</b>, no la llamada. En valor apenas se mueve (2,16 frente a 2,46 ns),
///   porque la instanciacion propia por tipo ya habia resuelto la busqueda en compilacion y lo que
///   queda es la llamada virtual. <b>El suelo del despacho por interfaz esta en ~4,3 ns en
///   referencia</b>; por debajo de eso no se puede bajar sin recuperar el tipo estatico del
///   receptor.</description></item>
/// </list>
/// La lectura practica no es "el despachador generico es caro", sino <b>"el despachador generico
/// es gratis mientras el receptor conserve su tipo estatico; esconderlo detras de una abstraccion
/// propia cuesta un orden de magnitud, y cuesta siete veces mas si el servicio es una clase que si
/// es un struct"</b>. Y el corolario que aportan las dos filas nuevas: <b>si ese coste molesta, la
/// palanca no es la forma de la comprobacion sino no perder el tipo estatico</b> -- eliminar la
/// comprobacion entera, que ya es tanto como se puede hacer, solo recupera dos tercios del camino
/// y a cambio de que un servicio no registrado deje de fallar limpiamente.
/// </para>
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class GenericResolverDevirtualizationBenchmark
{
    private CommerceContainer _container = null!;
    private Resolver _resolver = null!;

    /// <summary>
    /// El sitio de llamada que no puede cerrarse: <c>TProvide</c> sigue abierto aqui. No lleva
    /// <c>NoInlining</c> a proposito -- impedir el alineado destruiria justo el efecto que se
    /// quiere observar en las instanciaciones de valor.
    /// </summary>
    private static T Open<T>(CommerceContainer container) where T : notnull
        => container.Resolve<T>();

    /// <summary>
    /// Un contenedor detras de una abstraccion propia: el receptor pierde su tipo estatico y con
    /// el toda posibilidad de plegado.
    /// </summary>
    public abstract class Resolver
    {
        public abstract T Get<T>() where T : notnull;

        /// <summary>La misma resolucion con conversion directa en vez de prueba de tipo.</summary>
        public abstract T GetByCast<T>() where T : notnull;

        /// <summary>La misma resolucion sin comprobacion alguna: aisla el coste de la busqueda.</summary>
        public abstract T GetUnchecked<T>() where T : notnull;
    }

    private sealed class CommerceResolver(CommerceContainer container) : Resolver
    {
        public override T Get<T>() => container.Resolve<T>();

        public override T GetByCast<T>() => container.GetService<T>();

        public override T GetUnchecked<T>() => container.ResolveUnchecked<T>();
    }

    [GlobalSetup]
    public void Setup()
    {
        _container = new CommerceContainer();
        _resolver = new CommerceResolver(_container);
    }

    // ===== Referencia: codigo generico compartido (__Canon) =====

    [Benchmark(Baseline = true, Description = "Named member (no dispatch)")]
    [BenchmarkCategory("Reference")]
    public OrderEndpoint ReferenceNamed() => _container.OrderEndpoint;

    [Benchmark(Description = "Closed generic")]
    [BenchmarkCategory("Reference")]
    public OrderEndpoint ReferenceClosed() => _container.Resolve<OrderEndpoint>();

    [Benchmark(Description = "Open generic (shared code)")]
    [BenchmarkCategory("Reference")]
    public OrderEndpoint ReferenceOpen() => Open<OrderEndpoint>(_container);

    /// <summary>
    /// El servicio combinado: dependencias de referencia y de valor en el mismo constructor. Mide
    /// si la mezcla cambia algo en el <i>despacho</i> -- no deberia, y por eso esta la fila.
    /// </summary>
    [Benchmark(Description = "Open generic, combined service")]
    [BenchmarkCategory("Reference")]
    public CheckoutService ReferenceOpenCombined() => Open<CheckoutService>(_container);

    [Benchmark(Description = "Abstract resolver (virtual)")]
    [BenchmarkCategory("Reference")]
    public OrderEndpoint ReferenceAbstract() => _resolver.Get<OrderEndpoint>();

    /// <summary>
    /// El mismo caso con <c>((IProvider&lt;TProvide&gt;)this)</c> en vez de <c>this is IProvider&lt;TProvide&gt;</c>.
    /// Es el unico regimen donde las dos formas pueden diferir: en los demas el JIT pliega ambas.
    /// </summary>
    [Benchmark(Description = "Abstract resolver, direct cast")]
    [BenchmarkCategory("Reference")]
    public OrderEndpoint ReferenceAbstractCast() => _resolver.GetByCast<OrderEndpoint>();

    /// <summary>
    /// Sin comprobacion de tipo: lo que queda es la llamada de interfaz desnuda. La distancia
    /// con las dos filas anteriores es, exactamente, lo que cuesta buscar en la tabla de cien
    /// interfaces. Sonda de medicion, no codigo utilizable.
    /// </summary>
    [Benchmark(Description = "Abstract resolver, no check (probe)")]
    [BenchmarkCategory("Reference")]
    public OrderEndpoint ReferenceAbstractUnchecked() => _resolver.GetUnchecked<OrderEndpoint>();

    // ===== Valor: una instanciacion por tipo =====

    [Benchmark(Baseline = true, Description = "Named member (no dispatch)")]
    [BenchmarkCategory("Value")]
    public PricingPolicy ValueNamed() => _container.PricingPolicy;

    [Benchmark(Description = "Closed generic")]
    [BenchmarkCategory("Value")]
    public PricingPolicy ValueClosed() => _container.Resolve<PricingPolicy>();

    [Benchmark(Description = "Open generic (specialized code)")]
    [BenchmarkCategory("Value")]
    public PricingPolicy ValueOpen() => Open<PricingPolicy>(_container);

    /// <summary>
    /// Un valor mas pequeno que <see cref="PricingPolicy"/>: separa el coste del despacho del de
    /// copiar la estructura, que no es el objeto del estudio pero si entra en la cifra.
    /// </summary>
    [Benchmark(Description = "Open generic, small struct")]
    [BenchmarkCategory("Value")]
    public CartLimits ValueOpenSmall() => Open<CartLimits>(_container);

    [Benchmark(Description = "Abstract resolver (virtual)")]
    [BenchmarkCategory("Value")]
    public PricingPolicy ValueAbstract() => _resolver.Get<PricingPolicy>();

    /// <inheritdoc cref="ReferenceAbstractCast"/>
    [Benchmark(Description = "Abstract resolver, direct cast")]
    [BenchmarkCategory("Value")]
    public PricingPolicy ValueAbstractCast() => _resolver.GetByCast<PricingPolicy>();

    /// <inheritdoc cref="ReferenceAbstractUnchecked"/>
    [Benchmark(Description = "Abstract resolver, no check (probe)")]
    [BenchmarkCategory("Value")]
    public PricingPolicy ValueAbstractUnchecked() => _resolver.GetUnchecked<PricingPolicy>();
}

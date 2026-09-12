using System.Runtime.CompilerServices;

namespace Benchmarks.HandCoded;

/// <summary>
/// Estrategias de publicacion de un servicio perezoso, aisladas para poder medirlas.
/// <para>
/// <b>Los cuatro no son cuatro formas de hacer lo mismo.</b> Solo dos publican correctamente un
/// singleton; los otros dos se miden porque hay que enseñar <i>cuanto</i> cuesta el atajo y que
/// es lo que se rompe al tomarlo.
/// </para>
/// <para>
/// Conviene separar dos cosas que se confunden:
/// <list type="bullet">
///   <item><see cref="Volatile"/> <c>Read</c>/<c>Write</c> <b>no es un mecanismo de publicacion</b>.
///   Es el lado de la <i>lectura</i>: garantiza que quien ve la referencia ve tambien el objeto
///   completamente construido, pero no impide que dos hilos construyan a la vez. Aparece en las
///   cuatro estrategias porque el camino caliente es el mismo en todas.</item>
///   <item>Lo que distingue a las cuatro es el <b>lado de la escritura</b>: quien tiene derecho a
///   publicar y que pasa con lo que construyo el que no lo consiguio.</item>
/// </list>
/// </para>
/// </summary>
file static class Gates
{
    /// <summary>
    /// Candado estatico compartido por todos los contenedores del proceso. Es lo que emite un
    /// generador para un singleton, porque el campo que protege tambien es estatico.
    /// </summary>
    internal static readonly Lock Singleton = new();
}

/// <summary>
/// <b>lock(this)</b> — la estrategia de un servicio scoped.
/// <para>
/// El candado es el propio ambito, asi que <b>no cuesta ni un byte</b>: no hay que asignar un objeto
/// de sincronizacion. Y el alcance de la contienda es exactamente el correcto, porque cada ambito
/// escribe en sus propios campos: dos peticiones simultaneas no se estorban aunque ambas esten
/// resolviendo su primer servicio.
/// </para>
/// <para>
/// La objecion habitual a <c>lock(this)</c> -- que codigo ajeno pueda tomar el mismo candado -- no
/// aplica a un ambito generado, cuyo tipo no se expone para eso.
/// </para>
/// </summary>
public sealed class ScopedLockHolder
{
    private SyncPlain? _value;

    public SyncPlain Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref _value) ?? Create();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private SyncPlain Create()
    {
        lock (this)
        {
            var value = _value;
            if (value is null)
            {
                value = new SyncPlain();
                Volatile.Write(ref _value, value);
            }

            return value;
        }
    }
}

/// <summary>
/// <b>lock(candado estatico)</b> — la estrategia de un singleton.
/// <para>
/// Un campo estatico lo comparte todo el proceso, asi que el candado que lo protege tambien tiene
/// que serlo. Uncontended cuesta lo mismo que <see cref="ScopedLockHolder"/>: adquirir un candado
/// libre es la misma operacion tomando el objeto que sea.
/// </para>
/// <para>
/// <b>La diferencia no esta en el coste, esta en el alcance.</b> Un candado estatico serializa la
/// primera resolucion de <i>todos</i> los contenedores del proceso. Da igual en un servidor que
/// construye su contenedor una vez al arrancar; importa en un banco de pruebas o en un proceso que
/// crea contenedores en paralelo. Por eso el campo del valor aqui es de instancia y solo el candado
/// es estatico: asi esta tabla aisla el eje del candado y no lo mezcla con el de la estaticidad del
/// campo, que se mide en la tabla de creacion.
/// </para>
/// </summary>
public sealed class SingletonLockHolder
{
    private SyncPlain? _value;

    public SyncPlain Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref _value) ?? Create();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private SyncPlain Create()
    {
        lock (Gates.Singleton)
        {
            var value = _value;
            if (value is null)
            {
                value = new SyncPlain();
                Volatile.Write(ref _value, value);
            }

            return value;
        }
    }
}

/// <summary>
/// <b>Interlocked.CompareExchange</b> — publicacion sin candado.
/// <para>
/// Obliga a <b>construir antes de poder intentar publicar</b>, asi que bajo llegada simultanea varios
/// hilos construyen y solo uno gana. Los perdedores descartan lo que acaban de construir: medido en
/// <c>--check</c>, entre el 11% y el 82% segun los hilos.
/// </para>
/// <para>
/// <b>La identidad si se respeta:</b> todos los llamadores acaban viendo la misma instancia, la del
/// ganador, porque el CAS solo escribe si el campo sigue nulo y el perdedor devuelve lo que encontro.
/// Por eso es <i>correcto para un servicio sin recursos</i> y una fuga para uno desechable: la
/// instancia descartada nunca se publico, el contenedor no la conoce y nadie la va a desechar.
/// </para>
/// </summary>
public sealed class CompareExchangeHolder
{
    private SyncPlain? _value;

    public SyncPlain Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref _value) ?? Create();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private SyncPlain Create()
    {
        var created = new SyncPlain();
        return Interlocked.CompareExchange(ref _value, created, null) ?? created;
    }
}

/// <summary>
/// <b>Interlocked.Exchange</b> — y aqui esta la trampa.
/// <para>
/// Se mide porque se propone a menudo como la version "mas simple" del CAS, y <b>rompe la garantia
/// del singleton</b>. <c>Exchange</c> escribe <i>siempre</i>, sin mirar lo que habia. Bajo llegada
/// simultanea:
/// </para>
/// <para>
/// El hilo A construye la instancia 1 y la publica. El hilo B construye la instancia 2 y la publica
/// <b>encima</b>. A ya se llevo la 1; todo el que llegue despues recibe la 2. <b>Dos llamadores
/// tienen dos "singletons" distintos a la vez</b>, y ninguno de los dos hizo nada mal.
/// </para>
/// <para>
/// No es un descarte como el del CAS, que al menos converge: es una <b>violacion de identidad</b>.
/// Si el servicio guarda estado -- una cache, un contador, una conexion -- el estado se parte en dos
/// y el fallo aparece mucho despues y lejos de aqui. La sonda de <c>--check</c> lo cuenta.
/// </para>
/// <para>
/// Se incluye en el banco justamente porque <b>va a salir rapido</b>. Es el ejemplo de por que una
/// tabla de tiempos sin verificacion semantica al lado es un arma cargada.
/// </para>
/// </summary>
public sealed class ExchangeHolder
{
    private SyncPlain? _value;

    public SyncPlain Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref _value) ?? Create();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private SyncPlain Create()
    {
        var created = new SyncPlain();
        Interlocked.Exchange(ref _value, created);
        return created;
    }
}

/// <summary>
/// Lectura <b>no volatil</b> en el camino caliente, con candado para publicar.
/// <para>
/// Esta aqui para responder a la pregunta que suele acompañar a las otras cuatro: <i>¿cuanto cuesta
/// el <see cref="Volatile.Read{T}"/> del camino caliente?</i> La respuesta importa porque si costara
/// algo habria una tentacion real de quitarlo.
/// </para>
/// <para>
/// En x64 una lectura volatil de referencia es una lectura normal -- el modelo de memoria del
/// procesador ya no reordena cargas con cargas -- asi que la barrera es solo para el compilador. La
/// tabla deberia dar un empate exacto, y ese empate es el argumento para <b>no</b> quitarla: es
/// gratis aqui y deja de serlo en ARM64, donde el mismo codigo sin ella se rompe de forma
/// practicamente imposible de reproducir.
/// </para>
/// </summary>
public sealed class PlainReadHolder
{
    private SyncPlain? _value;

    public SyncPlain Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _value ?? Create();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private SyncPlain Create()
    {
        lock (this)
        {
            var value = _value;
            if (value is null)
            {
                value = new SyncPlain();
                Volatile.Write(ref _value, value);
            }

            return value;
        }
    }
}

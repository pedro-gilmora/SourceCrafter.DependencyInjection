using System;

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace Benchmarks.HandCoded;

/// <summary>
/// Configuracion del banco.
/// <para>
/// <b>La maquina de desarrollo no es una maquina, son dos.</b> Es un i9-14900HX, una CPU
/// hibrida con 8 nucleos de rendimiento con hilo simultaneo (logicos 0-15) y 16 nucleos de
/// eficiencia (logicos 16-31). El planificador de Windows mueve el proceso entre los dos tipos
/// y las cifras cambian de sitio. Medido sobre seis metodos <b>identicos</b>, con asignacion
/// identica byte a byte en todos los casos:
/// <list type="table">
///   <item><term>P-cores</term><description>33,00 - 35,07 ns</description></item>
///   <item><term>E-cores</term><description>72,69 - 73,94 ns</description></item>
/// </list>
/// Ratio <b>2,2x</b> por el mismo codigo. Sin fijar la afinidad, una tabla cualquiera puede
/// repartir sus filas entre los dos modos y producir "diferencias" de mas del doble que no
/// existen. Esta es la causa real de un informe que hubo que retractar en este repo.
/// </para>
/// <para>
/// De ahi las decisiones de este fichero:
/// <list type="number">
///   <item><b>Afinidad a los P-cores</b>, y ademas a uno solo por nucleo fisico. Es la
///   correccion de la causa y no del sintoma.</item>
///   <item>Eso permite <b>LaunchCount = 1</b>: promediar entre tres procesos era un parche para
///   un reparto de nucleos que ya no ocurre. Sale mas rapido <i>y</i> mas fiable.</item>
///   <item><b>MemoryRandomization</b> se conserva. No era la causa principal, pero sigue siendo
///   una fuente real de sesgo en los escenarios que asignan poco.</item>
///   <item>Y, sobre todo, <see cref="Scenarios.ControlBenchmark"/> forma parte
///   <b>permanente</b> del banco. Corregir la causa conocida no basta: hay que poder detectar
///   la que todavia no se conoce, en la corrida concreta que se va a publicar.</item>
/// </list>
/// </para>
/// <para>
/// <b>Suelo de discriminacion.</b> Incluso con todo lo anterior, seis metodos identicos quedan
/// dentro de una banda del 4-7%. Ese es el limite del instrumento. Una diferencia menor que esa
/// banda <b>no es una diferencia</b>, por estrecha que sea la barra de error de cada fila por
/// separado.
/// </para>
/// <para>
/// <b>Protocolo de lectura, sin excepciones:</b>
/// <list type="number">
///   <item>Se mira primero la tabla del control. Si sus seis metodos identicos no quedan
///   agrupados, <b>ninguna otra tabla de esa corrida es publicable</b>.</item>
///   <item>Una diferencia con <c>RatioSD</c> comparable al propio <c>Ratio</c> no es una
///   diferencia.</item>
///   <item>La columna <c>Allocated</c> si es fiable siempre: es un recuento, no un tiempo.
///   Cuando el tiempo es ruidoso y la memoria no, se reporta la memoria.</item>
/// </list>
/// </para>
/// <para>
/// <b>El banco no se puede paralelizar.</b> Medir entre 0,5 y 35 ns exige que el nucleo, su
/// cache y el ancho de banda de memoria esten dedicados; repartir escenarios entre nucleos
/// convierte la contienda en la senal. Con 32 logicos el planificador ademas <i>garantizaria</i>
/// mandar trabajo a los E-cores, que es el artefacto de arriba fabricado a proposito. La
/// afinidad da lo que se buscaba por otra via: la corrida completa baja de ~88 a ~26 minutos
/// <i>y</i> es mas fiable.
/// </para>
/// </summary>
public static class HarnessConfig
{
    /// <summary>
    /// Un procesador logico por cada uno de los 8 nucleos de rendimiento, sin hermanos de hilo
    /// simultaneo (logicos 0, 2, 4, 6, 8, 10, 12, 14).
    /// <para>
    /// Se descartan los hermanos SMT porque comparten unidades de ejecucion y cache L1/L2 con
    /// su pareja: el proceso que coordina la corrida puede caer en el hermano del nucleo que
    /// esta midiendo. Medido sobre el control, esa es la diferencia entre un metodo con
    /// <c>StdDev</c> de 7,10 ns usando los 16 logicos y ninguno por encima de 1,08 ns con esta
    /// mascara.
    /// </para>
    /// <para>
    /// Se declara como <see cref="long"/> y no como literal hexadecimal de 32 bits porque las
    /// mascaras con el bit alto puesto se interpretan con signo en mas de un sitio (PowerShell
    /// convierte <c>0xFFFF0000</c> en <c>-65536</c> y <c>ProcessorAffinity</c> lo rechaza).
    /// </para>
    /// </summary>
    private const long PerformanceCores = 0x0000000000005555;

    /// <summary>
    /// Configuracion de publicacion. Es la unica valida para reportar cifras.
    /// </summary>
    public static IConfig Instance { get; } = ManualConfig
        .Create(DefaultConfig.Instance)
        .AddJob(Job.Default
            .WithAffinity((IntPtr)PerformanceCores)
            .WithLaunchCount(1)
            .WithWarmupCount(10)
            .WithIterationCount(15)
            .WithMemoryRandomization());

    /// <summary>
    /// Configuracion rapida, para iterar sobre el codigo. <b>No sirve para publicar nada:</b>
    /// con 3 calentamientos y 5 iteraciones el error de cada medida es demasiado grande para
    /// comparar celdas entre si.
    /// <para>
    /// La columna <c>Allocated</c> si es de fiar incluso aqui, porque es un recuento y no un
    /// tiempo. Para un cambio que busca quitar asignaciones, este modo basta.
    /// </para>
    /// </summary>
    public static IConfig Fast { get; } = ManualConfig
        .Create(DefaultConfig.Instance)
        .AddJob(Job.Default
            .WithAffinity((IntPtr)PerformanceCores)
            .WithLaunchCount(1)
            .WithWarmupCount(3)
            .WithIterationCount(5));
}

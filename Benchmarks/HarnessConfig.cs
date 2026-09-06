using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace Benchmarks;

/// <summary>
/// Configuracion por defecto del banco.
/// <para>
/// No es cosmetica: con el trabajo por defecto de BenchmarkDotNet (un solo lanzamiento,
/// sin randomizacion de memoria) estos escenarios <b>no son reproducibles</b>. Se midio
/// el mismo binario dos veces y el escenario Complex dio ~38 ns para los cuatro
/// competidores en una corrida y ~80 ns para tres de ellos —dejando el baseline
/// quieto— en la siguiente. Tres librerias independientes no se degradan a la vez y al
/// mismo valor: era un artefacto de un unico proceso.
/// </para>
/// <para>
/// Los dos ajustes atacan las dos causas:
/// <list type="bullet">
///   <item><b>LaunchCount = 3</b> promedia entre procesos, asi que la suerte de
///   alineacion del codigo y el estado inicial del monton dejan de ser constantes
///   sistematicas de la medicion.</item>
///   <item><b>MemoryRandomization</b> desplaza el contexto de asignacion entre
///   iteraciones. Es lo que decide en los escenarios que asignan poco y rapido, donde
///   el coste real medido son los rellenos del contexto de asignacion del GC y no el
///   codigo del contenedor.</item>
/// </list>
/// </para>
/// <para>
/// Regla de lectura que se deriva de lo anterior: <b>una diferencia con
/// <c>RatioSD</c> comparable al propio <c>Ratio</c> no es una diferencia</b>.
/// </para>
/// </summary>
public static class HarnessConfig
{
    public static IConfig Instance { get; } = ManualConfig
        .Create(DefaultConfig.Instance)
        .AddJob(Job.Default
            .WithLaunchCount(3)
            .WithWarmupCount(10)
            .WithIterationCount(15)
            .WithMemoryRandomization());
}

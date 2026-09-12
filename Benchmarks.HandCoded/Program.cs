using BenchmarkDotNet.Running;

namespace Benchmarks.HandCoded;

/// <summary>
/// Punto de entrada.
/// <para>
/// Modos:
/// <list type="bullet">
///   <item><c>--check</c>: solo verificacion semantica. No mide nada.</item>
///   <item><c>--fast</c>: arnes rapido, para iterar. Las asignaciones son de fiar; los tiempos
///   no.</item>
///   <item>sin argumentos o con filtros de BenchmarkDotNet: arnes de publicacion.</item>
/// </list>
/// </para>
/// <para>
/// <b>La verificacion semantica corre SIEMPRE antes de medir, y si falla se aborta.</b> No es
/// una comodidad: es lo que impide que una corrida entera se publique despues de que alguien
/// haya roto la identidad de un cacheado o el rastreo de un desechable. Medir primero y validar
/// despues es el orden que garantiza que el informe malo ya existe cuando aparece el fallo.
/// </para>
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Contains("--check"))
        {
            return SemanticCheck.Run();
        }

        if (SemanticCheck.Run() != 0)
        {
            Console.WriteLine();
            Console.WriteLine("Medicion ABORTADA: la semantica no es correcta, las cifras no significarian nada.");
            return 1;
        }

        var fast = args.Contains("--fast");
        var config = fast ? HarnessConfig.Fast : HarnessConfig.Instance;
        var filtered = args.Where(a => a != "--fast").ToArray();

        Console.WriteLine();
        Console.WriteLine(fast
            ? "Arnes RAPIDO. Los tiempos NO son publicables; las asignaciones si."
            : "Arnes de PUBLICACION. Leer primero la tabla del control.");
        Console.WriteLine();

        BenchmarkSwitcher
            .FromAssembly(typeof(Program).Assembly)
            .Run(filtered, config);

        return 0;
    }
}

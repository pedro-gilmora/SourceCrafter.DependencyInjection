using BenchmarkDotNet.Running;

namespace Benchmarks.HandCoded;

/// <summary>
/// Punto de entrada.
/// <para>
/// Modos:
/// <list type="bullet">
///   <item><c>--check</c>: solo verificacion semantica. No mide nada.</item>
///   <item><c>--cpu</c>: CPU y memoria de las estrategias de publicacion bajo contienda, que es el
///   unico regimen donde divergen. BenchmarkDotNet no puede medir esto porque corre a un hilo.</item>
///   <item><c>--list</c>: imprime la tabla de valores de <see cref="Suite"/> y sale.</item>
///   <item><b>un entero</b>: mascara de bits de <see cref="Suite"/>. <c>-- 3</c> corre el control y
///   la tabla de publicacion. Tambien acepta hexadecimal (<c>0x0F</c>) o nombres separados por coma
///   (<c>Control,HeadToHeadScope</c>).</item>
///   <item><c>--fast</c>: arnes rapido, para iterar. Las asignaciones son de fiar; los tiempos
///   no.</item>
///   <item>filtros de cadena de BenchmarkDotNet, como siempre.</item>
/// </list>
/// </para>
/// <para>
/// <b>La verificacion semantica corre SIEMPRE antes de medir, y si falla se aborta.</b> No es
/// una comodidad: es lo que impide que una corrida entera se publique despues de que alguien
/// haya roto la identidad de un cacheado o el rastreo de un desechable. Medir primero y validar
/// despues es el orden que garantiza que el informe malo ya existe cuando aparece el fallo.
/// </para>
/// <para>
/// <b>Sin argumentos no se mide nada.</b> Se imprime la tabla de valores y se sale. Correr las doce
/// tablas cuesta cerca de una hora, y no debe ser lo que pasa por escribir <c>dotnet run</c> sin
/// pensar.
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

        if (args.Contains("--cpu"))
        {
            ContentionProbe.Run();
            return 0;
        }

        var fast = args.Contains("--fast");
        var rest = args.Where(a => a != "--fast").ToArray();

        if (rest.Contains("--list") || rest.Length == 0)
        {
            SuiteMap.PrintLegend();
            return 0;
        }

        var suite = Suite.None;
        var filters = new List<string>();

        foreach (var arg in rest)
        {
            if (!arg.StartsWith('-') && SuiteMap.TryParse(arg, out var parsed))
            {
                suite |= parsed;
            }
            else
            {
                filters.Add(arg);
            }
        }

        var types = SuiteMap.Resolve(suite);

        if (suite != Suite.None && types.Length == 0)
        {
            Console.WriteLine($"La mascara {(int)suite} no selecciona ninguna tabla.");
            Console.WriteLine();
            SuiteMap.PrintLegend();
            return 1;
        }

        if (SemanticCheck.Run() != 0)
        {
            Console.WriteLine();
            Console.WriteLine("Medicion ABORTADA: la semantica no es correcta, las cifras no significarian nada.");
            return 1;
        }

        var config = fast ? HarnessConfig.Fast : HarnessConfig.Instance;

        Console.WriteLine();
        Console.WriteLine(fast
            ? "Arnes RAPIDO. Los tiempos NO son publicables; las asignaciones si."
            : "Arnes de PUBLICACION. Leer primero la tabla del control.");

        if (types.Length > 0)
        {
            Console.WriteLine($"Seleccion: {suite} ({(int)suite}) -> {types.Length} tabla(s).");

            if ((suite & Suite.Control) == 0)
            {
                Console.WriteLine("AVISO: sin el control (bit 0), estas cifras no son publicables.");
            }

            Console.WriteLine();

            // La mascara YA es la seleccion, asi que si el usuario no añadio un filtro propio hay
            // que darle uno que lo acepte todo. Sin el, el switcher se limita a listar los tipos y
            // salir con codigo 0, que parece una corrida vacia y no un error.
            if (filters.Count == 0)
            {
                filters.Add("--filter");
                filters.Add("*");
            }

            BenchmarkSwitcher.FromTypes(types).Run([.. filters], config);
        }
        else
        {
            Console.WriteLine();
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run([.. filters], config);
        }

        return 0;
    }
}

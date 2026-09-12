using Benchmarks.HandCoded.Scenarios;

namespace Benchmarks.HandCoded;

/// <summary>
/// Seleccion de tablas por mascara de bits.
/// <para>
/// Existe porque el filtro de cadena de BenchmarkDotNet obliga a escribir comodines largos y a
/// acertar con el nombre exacto de la clase. Con esto, <c>dotnet run -c Release -- 3</c> corre el
/// control y la tabla de publicacion, y nada mas.
/// </para>
/// <para>
/// <b>El control esta en el bit 0 a proposito.</b> Es el unico valor impar util y hace que la
/// costumbre correcta -- sumarle 1 a la mascara que te interese -- sea tambien la mas corta de
/// escribir. Ninguna tabla de esta suite se lee sin mirar antes su grupo de control a la misma
/// magnitud.
/// </para>
/// </summary>
[Flags]
public enum Suite
{
    None = 0,

    /// <summary>Metodos identicos a tres magnitudes. Mide el instrumento, no el codigo.</summary>
    Control = 1 << 0,

    /// <summary>Las cinco estrategias de publicacion: candados, CAS, Exchange y lectura no volatil.</summary>
    Publication = 1 << 1,

    /// <summary>Eager frente a lazy, ambos escritos a mano. Aisla el coste de la pereza.</summary>
    EagerVsLazy = 1 << 2,

    /// <summary>Ciclo de vida del ambito escrito a mano: vacio, parcial y completo.</summary>
    ScopeLifecycle = 1 << 3,

    MatrixSingleton = 1 << 4,
    MatrixScoped = 1 << 5,
    MatrixTransientPlain = 1 << 6,
    MatrixTransientDisposable = 1 << 7,

    HeadToHeadSingleton = 1 << 8,
    HeadToHeadScope = 1 << 9,
    HeadToHeadCreation = 1 << 10,
    HeadToHeadTransient = 1 << 11,

    /// <summary>Las cuatro celdas de la matriz atomica (lifetime x async-kind x disposability).</summary>
    Matrix = MatrixSingleton | MatrixScoped | MatrixTransientPlain | MatrixTransientDisposable,

    /// <summary>Las cuatro comparaciones contra Jab, Pure.DI, CircleDI y SourceCrafter.</summary>
    HeadToHead = HeadToHeadSingleton | HeadToHeadScope | HeadToHeadCreation | HeadToHeadTransient,

    /// <summary>Lo que se publica en el README: control mas los cuatro head-to-head.</summary>
    Report = Control | HeadToHead,

    All = Control | Publication | EagerVsLazy | ScopeLifecycle | Matrix | HeadToHead
}

/// <summary>
/// Traduce una <see cref="Suite"/> a los tipos de BenchmarkDotNet que le corresponden.
/// </summary>
public static class SuiteMap
{
    private static readonly (Suite Flag, Type Type)[] Entries =
    [
        (Suite.Control, typeof(ControlBenchmark)),
        (Suite.Publication, typeof(PublicationBenchmark)),
        (Suite.EagerVsLazy, typeof(EagerVsLazyBenchmark)),
        (Suite.ScopeLifecycle, typeof(ScopeLifecycleBenchmark)),
        (Suite.MatrixSingleton, typeof(MatrixSingletonBenchmark)),
        (Suite.MatrixScoped, typeof(MatrixScopedBenchmark)),
        (Suite.MatrixTransientPlain, typeof(MatrixTransientPlainBenchmark)),
        (Suite.MatrixTransientDisposable, typeof(MatrixTransientDisposableBenchmark)),
        (Suite.HeadToHeadSingleton, typeof(HeadToHeadSingletonBenchmark)),
        (Suite.HeadToHeadScope, typeof(HeadToHeadScopeBenchmark)),
        (Suite.HeadToHeadCreation, typeof(HeadToHeadCreationBenchmark)),
        (Suite.HeadToHeadTransient, typeof(HeadToHeadTransientBenchmark)),
    ];

    public static Type[] Resolve(Suite suite) =>
        [.. Entries.Where(e => (suite & e.Flag) == e.Flag).Select(e => e.Type)];

    /// <summary>
    /// Acepta un entero (<c>3</c>, <c>0x0F</c>) o una lista de nombres (<c>Control,Publication</c>).
    /// Devuelve <see langword="false"/> si el texto no es ninguna de las dos cosas, para que el
    /// llamador pueda tratarlo como un filtro de cadena de BenchmarkDotNet.
    /// </summary>
    public static bool TryParse(string text, out Suite suite)
    {
        suite = Suite.None;

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(text.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out var hex))
        {
            suite = (Suite)hex;
            return true;
        }

        if (int.TryParse(text, out var value))
        {
            suite = (Suite)value;
            return true;
        }

        return Enum.TryParse(text, ignoreCase: true, out suite) && suite != Suite.None;
    }

    public static void PrintLegend()
    {
        Console.WriteLine("Tablas disponibles. Suma los valores y pasalos como un solo entero:");
        Console.WriteLine();
        Console.WriteLine("  valor  nombre");
        Console.WriteLine("  -----  ------------------------------");

        foreach (var (flag, _) in Entries)
        {
            Console.WriteLine($"  {(int)flag,5}  {flag}");
        }

        Console.WriteLine();
        Console.WriteLine("  Combinaciones con nombre propio:");
        Console.WriteLine($"  {(int)Suite.Matrix,5}  Matrix      (las cuatro celdas de la matriz atomica)");
        Console.WriteLine($"  {(int)Suite.HeadToHead,5}  HeadToHead  (las cuatro comparaciones)");
        Console.WriteLine($"  {(int)Suite.Report,5}  Report      (control + head-to-head: lo que va al README)");
        Console.WriteLine($"  {(int)Suite.All,5}  All");
        Console.WriteLine();
        Console.WriteLine("  Ejemplos:");
        Console.WriteLine("    dotnet run -c Release -- 3          control + publicacion");
        Console.WriteLine("    dotnet run -c Release -- 3841       Report");
        Console.WriteLine("    dotnet run -c Release -- Control,HeadToHeadScope");
        Console.WriteLine("    dotnet run -c Release -- 3 --fast   iterar rapido (tiempos NO publicables)");
    }
}

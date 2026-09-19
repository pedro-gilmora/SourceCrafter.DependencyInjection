using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Benchmarks.HandCoded;

/// <summary>
/// Mide <b>CPU y memoria</b> de las estrategias de publicacion <b>bajo contienda</b>.
/// <para>
/// Existe porque la tabla de BenchmarkDotNet no puede contestar esta pregunta. Aquella corre a un
/// solo hilo, y a un solo hilo las cuatro estrategias asignan exactamente lo mismo (48 B) y no
/// contienden nunca. Es decir: en el unico regimen donde la eleccion de candado frente a atomico
/// podria costar algo, BDN no mira.
/// </para>
/// <para>
/// <b>Que se mide y por que estas tres columnas.</b>
/// </para>
/// <list type="bullet">
/// <item><b>CPU/pared</b>: cuantos nucleos se mantienen ocupados de media. Es la columna que separa
/// aparcar de girar. Un candado en contienda duerme al perdedor y su CPU no sube; un atomico hace
/// que todos los perdedores construyan a la vez y su CPU sube con los hilos.</item>
/// <item><b>Bytes por ronda</b>: la basura que genera descartar instancias. Un candado con
/// <c>??=</c> construye una vez por ronda pase lo que pase. CAS y Exchange construyen ANTES de
/// saber si van a poder publicar, asi que cada perdedor deja una instancia muerta.</item>
/// <item><b>Contenciones</b>: <see cref="Monitor.LockContentionCount"/>, que cuenta las veces que un
/// hilo no pudo tomar el candado a la primera. En las filas atomicas vale cero por construccion, y
/// esa es justo la unica ventaja real que tienen.</item>
/// </list>
/// <para>
/// <b>La fila de control no es decorativa.</b> Sincronizar N hilos en una barrera cuesta CPU y esa
/// CPU se contabiliza igual que la del codigo medido. Sin una fila que ejecute la barrera y nada
/// mas, cualquier lectura de la columna de CPU estaria midiendo sobre todo la barrera. En esta
/// sonda el suelo resulto ser la parte dominante a 8 hilos, asi que <b>las cifras de CPU se leen
/// como diferencia contra el control, nunca en absoluto</b>.
/// </para>
/// </summary>
internal static class ContentionProbe
{
    /// <summary>
    /// Cada fila se calibra para durar esto. No es un capricho: <see cref="Process.TotalProcessorTime"/>
    /// esta cuantizado a ticks de 15,625 ms, asi que una ventana corta no mide CPU, mide cuantos
    /// ticks cayeron dentro. La primera version de esta sonda usaba 20.000 rondas fijas y devolvia
    /// cifras que eran todas multiplos exactos de un tick, incluida una fila con <b>0 ns de CPU</b>,
    /// que es imposible. Con tres segundos caben ~200 ticks por nucleo y la cuantizacion baja del 1%.
    /// </summary>
    private static readonly TimeSpan TargetPerRow = TimeSpan.FromSeconds(3);

    private const int CalibrationRounds = 2_000;
    private const int MinRounds = 2_000;
    private const int MaxRounds = 2_000_000;

    /// <summary>
    /// Repeticiones por fila, de las que se reporta la mediana y la dispersion.
    /// <para>
    /// No es celo: <b>la fila del candado a 8 hilos no es reproducible</b>. Entre dos corridas dio
    /// 3.248 y 1.203 ns de CPU, un factor de 2,7, mientras que las filas atomicas de la misma tabla
    /// repetian dentro del 4%. La causa es que aparcar y despertar hilos depende del planificador del
    /// sistema, que no es parte del experimento. Publicar un solo numero ahi seria inventarse una
    /// precision que no existe, asi que la tabla enseña el reparto y quien la lea decide.
    /// </para>
    /// </summary>
    private const int Repeats = 3;

    /// <summary>
    /// Publicaciones independientes por ronda. <b>Esta constante es la que hace que la columna de
    /// CPU signifique algo.</b>
    /// <para>
    /// Con una sola publicacion por ronda, sincronizar los hilos en la barrera costaba ~3,6 us a 8
    /// hilos y lo que se queria medir costaba ~100 ns: la señal era el 3% del arnes. La fila de
    /// control lo delato de la forma mas clara posible, dando diferencias <b>negativas</b> (las
    /// estrategias "gastaban menos CPU" que no hacer nada), que es imposible y solo significa que el
    /// arnes variaba mas que lo medido.
    /// </para>
    /// <para>
    /// Con 64 publicaciones por ronda la barrera se amortiza 64 veces y pasa a ser una minoria del
    /// total. Las cifras se reportan <b>por publicacion</b>, no por ronda.
    /// </para>
    /// </summary>
    private const int Slots = 64;

    private sealed class Counted
    {
        // Un par de campos para que la instancia tenga un tamaño realista y la columna de bytes
        // distinga de verdad entre construir una vez y construir N veces.
        private readonly long _a;
        private readonly long _b;

        public Counted(StrongBox<long> ctors)
        {
            Interlocked.Increment(ref ctors.Value);
            _a = Environment.TickCount64;
            _b = _a;
        }

        public long Sum => _a + _b;
    }

    private readonly record struct Sample(
        double WallNsPerRound,
        double CpuNsPerRound,
        double BytesPerRound,
        double ContentionsPerRound,
        int Gen0,
        long Ctors,
        int Rounds)
    {
        public double CtorsPerUnit => Ctors / ((double)Rounds * Slots);
    }

    public static void Run()
    {
        Console.WriteLine();
        Console.WriteLine("=== CPU y memoria de las estrategias de publicacion, BAJO CONTIENDA ===");
        Console.WriteLine();
        Console.WriteLine($"  Cada fila se calibra para durar ~{TargetPerRow.TotalSeconds:0} s, porque el contador de CPU del");
        Console.WriteLine("  sistema avanza a saltos de 15,625 ms y una ventana corta solo mide esos saltos.");
        Console.WriteLine();
        Console.WriteLine($"  Cada ronda: los N hilos salen de una barrera a la vez y compiten por publicar {Slots}");
        Console.WriteLine("  servicios independientes; luego se vacian los huecos y vuelta a empezar. Se publican");
        Console.WriteLine("  varios por ronda para que la barrera se amortice y no domine la columna de CPU.");
        Console.WriteLine("  Todas las cifras son POR PUBLICACION.");
        Console.WriteLine();
        Console.WriteLine("  CPU/pared = nucleos ocupados de media. La columna que decide es 'CPU-suelo':");
        Console.WriteLine("  lo que cuesta la fila por encima de una que solo ejecuta la barrera.");
        Console.WriteLine();
        Console.WriteLine($"  Cada fila se repite {Repeats} veces. 'disp' es cuanto se separan la mejor y la peor:");
        Console.WriteLine("  si es grande, esa fila NO tiene un valor unico y no se debe citar como si lo tuviera.");
        Console.WriteLine();

        foreach (var threads in (int[])[2, 4, 8])
        {
            Console.WriteLine($"  --- {threads} hilos ---");
            Console.WriteLine("  estrategia          pared       CPU  CPU-suelo   disp    bytes  ctors/pub");
            Console.WriteLine("  ----------------  ---------  --------  ---------  -----  -------  ---------");

            // El control primero: fija el suelo antes de que se lea ninguna fila real.
            var control = Measure(threads, -1);
            Print("(control barrera)", control, null);

            Print("lock             ", Measure(threads, 0), control);
            Print("CompareExchange  ", Measure(threads, 1), control);
            Print("Exchange         ", Measure(threads, 2), control);

            Console.WriteLine();
        }

        Console.WriteLine("  -> bytes y ctors/pub son contadores exactos y reproducen entre corridas. Son la");
        Console.WriteLine("     parte firme de esta tabla: 'ctors/pub' deberia ser 1,00 y todo lo que pase de");
        Console.WriteLine("     ahi es trabajo tirado, con su basura detras.");
        Console.WriteLine("  -> La CPU del candado a 8 hilos es la fila con mas dispersion de la sonda, porque");
        Console.WriteLine("     aparcar y despertar hilos lo decide el planificador del sistema. Mira 'disp'");
        Console.WriteLine("     antes de citarla.");
        Console.WriteLine();

        static void Print(string name, Row r, Row? control)
        {
            var delta = control is { } c
                ? $"{r.Cpu - c.Cpu,7:n1} ns"
                : $"{"(suelo)",10}";

            Console.WriteLine(
                $"  {name}  {r.Wall,6:n1} ns  {r.Cpu,5:n1} ns  {delta}  {r.CpuSpread,4:0.0}x  " +
                $"{r.Bytes,5:n0} B  {r.CtorsPerUnit,9:0.00}");
        }
    }

    /// <summary>Mediana de <see cref="Repeats"/> repeticiones, con la dispersion de la columna de CPU.</summary>
    private readonly record struct Row(
        double Wall,
        double Cpu,
        double CpuSpread,
        double Bytes,
        double CtorsPerUnit);

    /// <summary>
    /// <paramref name="strategy"/>: -1 control (solo barrera), 0 lock, 1 CompareExchange, 2 Exchange.
    /// </summary>
    private static Row Measure(int threads, int strategy)
    {
        // Pasada de calibracion: cuanto cuesta una publicacion, para elegir cuantas rondas hacen falta.
        var probe = RunOnce(threads, strategy, CalibrationRounds);
        var rounds = (int)Math.Clamp(
            TargetPerRow.TotalMilliseconds * 1_000_000.0 / Math.Max(probe.WallNsPerRound * Slots, 1.0),
            MinRounds,
            MaxRounds);

        var samples = new Sample[Repeats];
        for (var i = 0; i < Repeats; i++)
        {
            samples[i] = RunOnce(threads, strategy, rounds);
        }

        return new Row(
            Median(samples, s => s.WallNsPerRound),
            Median(samples, s => s.CpuNsPerRound),
            Spread(samples, s => s.CpuNsPerRound),
            Median(samples, s => s.BytesPerRound),
            Median(samples, s => s.CtorsPerUnit));

        static double Median(Sample[] samples, Func<Sample, double> pick)
        {
            var values = samples.Select(pick).Order().ToArray();
            return values[values.Length / 2];
        }

        // Cuanto se separan la mejor y la peor repeticion. Un 1,0x significa que la fila es
        // reproducible; cualquier cosa por encima de ~1,3x significa que no tiene un valor unico.
        static double Spread(Sample[] samples, Func<Sample, double> pick)
        {
            var values = samples.Select(pick).ToArray();
            var min = values.Min();
            return min <= 0 ? 0 : values.Max() / min;
        }
    }

    private static Sample RunOnce(int threads, int strategy, int rounds)
    {
        var ctors = new StrongBox<long>(0);
        var slots = new Counted?[Slots];
        var gate = new Lock();
        var sink = 0L;

        using var barrier = new Barrier(threads);
        var workers = new Thread[threads];

        for (var i = 0; i < threads; i++)
        {
            var index = i;

            workers[i] = new Thread(() =>
            {
                var local = 0L;

                for (var r = 0; r < rounds; r++)
                {
                    barrier.SignalAndWait();

                    if (strategy >= 0)
                    {
                        for (var s = 0; s < Slots; s++)
                        {
                            var value = strategy switch
                            {
                                1 => PublishWithCas(s),
                                2 => PublishWithExchange(s),
                                _ => PublishWithLock(s)
                            };

                            // Consumir el resultado: sin esto el analisis de escape puede borrar la
                            // construccion entera y la columna de bytes mentiria.
                            local += value.Sum;
                        }
                    }

                    barrier.SignalAndWait();

                    if (index == 0)
                    {
                        Array.Clear(slots);
                    }

                    barrier.SignalAndWait();
                }

                Interlocked.Add(ref sink, local);
            }, 256 * 1024);
        }

        // Estabilizar antes de tomar cualquier linea base.
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);

        var process = Process.GetCurrentProcess();
        process.Refresh();
        var cpu0 = process.TotalProcessorTime;
        var bytes0 = GC.GetTotalAllocatedBytes(precise: true);
        var cont0 = Monitor.LockContentionCount;
        var gen0 = GC.CollectionCount(0);
        var clock = Stopwatch.StartNew();

        foreach (var worker in workers) worker.Start();
        foreach (var worker in workers) worker.Join();

        clock.Stop();
        process.Refresh();
        var cpuNs = (process.TotalProcessorTime - cpu0).TotalMilliseconds * 1_000_000.0;
        var bytes = GC.GetTotalAllocatedBytes(precise: true) - bytes0;
        var contentions = Monitor.LockContentionCount - cont0;
        var gen0Delta = GC.CollectionCount(0) - gen0;

        // Mantener vivo el acumulador para que nada de lo anterior se pueda eliminar.
        GC.KeepAlive(sink);

        // Todo se reporta POR PUBLICACION, no por ronda: la ronda solo existe para amortizar la
        // barrera y su tamaño es un detalle del arnes, no de lo medido.
        var units = (double)rounds * Slots;

        return new Sample(
            clock.Elapsed.TotalMilliseconds * 1_000_000.0 / units,
            cpuNs / units,
            bytes / units,
            contentions / units,
            gen0Delta,
            ctors.Value,
            rounds);

        Counted PublishWithLock(int slot)
        {
            var value = slots[slot];
            if (value is not null) return value;

            lock (gate)
            {
                return slots[slot] ??= new Counted(ctors);
            }
        }

        Counted PublishWithCas(int slot)
        {
            var value = Volatile.Read(ref slots[slot]);
            if (value is not null) return value;

            // Construir ANTES de saber si se podra publicar: de ahi la basura.
            var created = new Counted(ctors);
            return Interlocked.CompareExchange(ref slots[slot], created, null) ?? created;
        }

        Counted PublishWithExchange(int slot)
        {
            var value = Volatile.Read(ref slots[slot]);
            if (value is not null) return value;

            var created = new Counted(ctors);
            Interlocked.Exchange(ref slots[slot], created);
            return created;
        }
    }
}

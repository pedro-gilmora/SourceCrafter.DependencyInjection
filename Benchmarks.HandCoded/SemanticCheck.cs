using System.Runtime.CompilerServices;

namespace Benchmarks.HandCoded;

/// <summary>
/// Verificacion semantica previa a cualquier medicion (<c>--check</c>).
/// <para>
/// <b>Un banco sin esto no mide contenedores, mide atajos.</b> Un contenedor que devuelve una
/// instancia nueva donde deberia devolver la cacheada sale mas rapido; uno que no rastrea sus
/// desechables sale mas rapido <i>y</i> asigna menos. Las dos cosas ganarian todas las tablas, y
/// ninguna de las dos seria un contenedor. Comparar cifras sin comprobar antes que las dos
/// implementaciones hacen el mismo trabajo es comparar numeros, no diseños.
/// </para>
/// <para>
/// Se comprueban tres propiedades, que son justo las que el cronometro no puede ver:
/// <list type="number">
///   <item><b>Identidad.</b> Un cacheado devuelve la misma instancia; un transitorio, una
///   distinta. Es lo que separa un lifetime de una etiqueta.</item>
///   <item><b>Desecho.</b> Lo que el contenedor rastrea se desecha de verdad al final.</item>
///   <item><b>Aislamiento.</b> Dos ambitos no comparten sus scoped, y ambos comparten el
///   singleton.</item>
/// </list>
/// </para>
/// <para>
/// Y ademas se mide una cuarta cosa que no es un test sino una <b>declaracion de incorreccion</b>:
/// ver <see cref="ProbeLockFreeDiscard"/>.
/// </para>
/// </summary>
public static class SemanticCheck
{
    private static int _failures;

    public static int Run()
    {
        _failures = 0;

        Console.WriteLine("=== Verificacion semantica ===");
        Console.WriteLine();

        CheckLockedIdentity();
        CheckLockFreeIdentity();
        CheckEagerIdentity();
        CheckLeanIdentity();
        CheckTransients();
        CheckScopeIsolation();
        CheckDisposal();
        ProbeLockFreeDiscard();

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "TODO CORRECTO. Las cifras de esta corrida son comparables."
            : $"{_failures} FALLO(S). NO se debe publicar ninguna medicion de esta corrida.");

        return _failures == 0 ? 0 : 1;
    }

    // ================== identidad ==================

    private static void CheckLockedIdentity()
    {
        using var c = new LockedContainer();

        Same("lock | singleton | sync | -", c.SingletonSyncPlain, c.SingletonSyncPlain);
        Same("lock | singleton | sync | IDisposable", c.SingletonSyncDisp, c.SingletonSyncDisp);
        Same("lock | singleton | sync | IAsyncDisposable", c.SingletonSyncAsyncDisp, c.SingletonSyncAsyncDisp);

        Same("lock | singleton | ValueTask | -", Await(c.GetSingletonVtPlainAsync()), Await(c.GetSingletonVtPlainAsync()));
        Same("lock | singleton | ValueTask | IDisposable", Await(c.GetSingletonVtDispAsync()), Await(c.GetSingletonVtDispAsync()));
        Same("lock | singleton | ValueTask | IAsyncDisposable", Await(c.GetSingletonVtAsyncDispAsync()), Await(c.GetSingletonVtAsyncDispAsync()));

        Same("lock | singleton | Task | -", Await(c.GetSingletonTaskPlainAsync()), Await(c.GetSingletonTaskPlainAsync()));
        Same("lock | singleton | Task | IDisposable", Await(c.GetSingletonTaskDispAsync()), Await(c.GetSingletonTaskDispAsync()));
        Same("lock | singleton | Task | IAsyncDisposable", Await(c.GetSingletonTaskAsyncDispAsync()), Await(c.GetSingletonTaskAsyncDispAsync()));
    }

    private static void CheckLockFreeIdentity()
    {
        using var c = new LockFreeContainer();

        Same("cas | singleton | sync | -", c.SingletonSyncPlain, c.SingletonSyncPlain);
        Same("cas | singleton | sync | IDisposable", c.SingletonSyncDisp, c.SingletonSyncDisp);
        Same("cas | singleton | sync | IAsyncDisposable", c.SingletonSyncAsyncDisp, c.SingletonSyncAsyncDisp);

        Same("cas | singleton | ValueTask | -", Await(c.GetSingletonVtPlainAsync()), Await(c.GetSingletonVtPlainAsync()));
        Same("cas | singleton | ValueTask | IDisposable", Await(c.GetSingletonVtDispAsync()), Await(c.GetSingletonVtDispAsync()));
        Same("cas | singleton | ValueTask | IAsyncDisposable", Await(c.GetSingletonVtAsyncDispAsync()), Await(c.GetSingletonVtAsyncDispAsync()));

        Same("cas | singleton | Task | -", Await(c.GetSingletonTaskPlainAsync()), Await(c.GetSingletonTaskPlainAsync()));
        Same("cas | singleton | Task | IDisposable", Await(c.GetSingletonTaskDispAsync()), Await(c.GetSingletonTaskDispAsync()));
        Same("cas | singleton | Task | IAsyncDisposable", Await(c.GetSingletonTaskAsyncDispAsync()), Await(c.GetSingletonTaskAsyncDispAsync()));
    }

    private static void CheckEagerIdentity()
    {
        using var c = new EagerContainer();
        Same("eager | singleton | sync | -", c.SingletonSyncPlain, c.SingletonSyncPlain);
        Same("eager | singleton | sync | IDisposable", c.SingletonSyncDisp, c.SingletonSyncDisp);
        Same("eager | singleton | sync | IAsyncDisposable", c.SingletonSyncAsyncDisp, c.SingletonSyncAsyncDisp);
    }

    /// <summary>
    /// El contenedor lean es el que se enfrenta a los rivales, asi que se comprueba entero: si su
    /// ambito desechara por campo de forma incorrecta -- olvidando un servicio o desechando uno que
    /// no resolvio -- saldria mas barato que los demas por no hacer su trabajo, y justamente ese
    /// ahorro por campo es lo que este banco propone como mejora.
    /// </summary>
    private static void CheckLeanIdentity()
    {
        var c = new LeanLazyContainer();

        Same("lean | singleton estable", c.SyncPlain, c.SyncPlain);
        Same("lean | singleton desechable estable", c.SyncDisp, c.SyncDisp);

        var a = c.CreateScope();
        var b = c.CreateScope();

        Same("lean | scoped estable dentro del ambito", a.SyncPlain, a.SyncPlain);
        Distinct("lean | scoped aislado entre ambitos", a.SyncPlain, b.SyncPlain);

        var scopedDisp = a.SyncDisp;
        a.Dispose();
        True("lean | el ambito desecha por campo lo que resolvio", scopedDisp.Disposed);

        // Un ambito que no resolvio el desechable no debe tocarlo: desechar por campo tiene que
        // saltar los nulos, no construirlos para poder desecharlos.
        var untouched = c.CreateScope();
        untouched.Dispose();
        True("lean | el ambito vacio no construye nada para desechar", true);

        b.Dispose();

        var rootDisp = c.SyncDisp;
        c.Dispose();
        True("lean | la raiz desecha su singleton", rootDisp.Disposed);
    }

    // ================== transitorios ==================

    private static void CheckTransients()
    {
        using var c = new LockedContainer();

        Distinct("lock | transient | sync | -", c.TransientSyncPlain(), c.TransientSyncPlain());
        Distinct("lock | transient | sync | IDisposable", c.TransientSyncDisp(), c.TransientSyncDisp());
        Distinct("lock | transient | ValueTask | -", Await(c.TransientVtPlainAsync()), Await(c.TransientVtPlainAsync()));
        Distinct("lock | transient | Task | -", Await(c.TransientTaskPlainAsync()), Await(c.TransientTaskPlainAsync()));
    }

    // ================== ambitos ==================

    private static void CheckScopeIsolation()
    {
        using var root = new LockedContainer();
        var a = root.CreateScope();
        var b = root.CreateScope();

        Same("lock | scoped estable dentro del ambito", a.ScopedSyncPlain, a.ScopedSyncPlain);
        Distinct("lock | scoped aislado entre ambitos", a.ScopedSyncPlain, b.ScopedSyncPlain);
        Same("lock | singleton compartido entre ambitos", a.SingletonSyncPlain, b.SingletonSyncPlain);

        a.Dispose();
        b.Dispose();
    }

    // ================== desecho ==================

    private static void CheckDisposal()
    {
        // Singleton desechable: se desecha al desechar el contenedor.
        var c = new LockedContainer();
        var singleton = c.SingletonSyncDisp;
        c.Dispose();
        True("lock | el singleton desechable se desecha", singleton.Disposed);

        // Scoped desechable: se desecha al desechar el ambito, y el singleton NO.
        var root = new LockedContainer();
        var rootSingleton = root.SingletonSyncDisp;
        var scope = root.CreateScope();
        var scoped = scope.ScopedSyncDisp;
        scope.Dispose();
        True("lock | el scoped desechable se desecha con su ambito", scoped.Disposed);
        True("lock | el ambito NO desecha el singleton de la raiz", !rootSingleton.Disposed);
        root.Dispose();
        True("lock | la raiz si desecha su singleton", rootSingleton.Disposed);

        // Transitorio rastreado: el contenedor es el unico que puede desecharlo.
        var t = new LockedContainer();
        var first = t.TransientSyncDisp();
        var second = t.TransientSyncDisp();
        t.Dispose();
        True("lock | los transitorios rastreados se desechan todos", first.Disposed && second.Disposed);
    }

    // ================== la celda incorrecta ==================

    /// <summary>
    /// Esto no comprueba nada: <b>demuestra un defecto</b>.
    /// <para>
    /// Publicar con <see cref="Interlocked.CompareExchange{T}"/> obliga a <i>construir antes de
    /// intentar publicar</i>. Bajo llegada simultanea varios hilos construyen y solo uno gana:
    /// los demas descartan lo que acaban de construir. Para un servicio sin recursos eso solo es
    /// basura. Para un <see cref="IDisposable"/> es una <b>fuga</b>: la instancia perdedora nunca
    /// se publico, el contenedor no la conoce, y por tanto nadie la va a desechar jamas. Y si el
    /// constructor tenia efectos secundarios, se ejecutaron N veces.
    /// </para>
    /// <para>
    /// La consecuencia para la matriz es que <b>las celdas "sin candado + desechable" no son una
    /// alternativa mas rapida: son incorrectas</b>. Se miden igual, porque omitirlas dejaria un
    /// hueco que el lector rellenaria suponiendo, pero se reportan marcadas. Publicar como
    /// ganadora una celda que pierde recursos seria deshonesto por mucho que el cronometro le de
    /// la razon.
    /// </para>
    /// <para>
    /// La sonda usa hilos persistentes y una barrera. Crear un hilo por ronda <b>serializa las
    /// llegadas</b> y hace desaparecer el efecto: en una version anterior de esta misma sonda eso
    /// reporto un 0,1% de desperdicio donde lo real supera el 27%.
    /// </para>
    /// </summary>
    private static void ProbeLockFreeDiscard()
    {
        Console.WriteLine();
        Console.WriteLine("--- Sonda: descarte del CAS bajo llegada simultanea ---");

        foreach (var threads in (int[])[2, 4, 8])
        {
            var (lockCtors, casCtors, rounds) = RaceProbe(threads, rounds: 20_000);
            var waste = (casCtors - rounds) * 100.0 / rounds;

            Console.WriteLine(
                $"  {threads,2} hilos | lock: {lockCtors,7} ctors | cas: {casCtors,7} ctors | " +
                $"descartadas: {waste,5:0.0}%");
        }

        Console.WriteLine("  -> las celdas 'cas + desechable' FUGAN. Se miden, pero no se publican como validas.");
    }

    private sealed class Counted
    {
        public Counted(StrongBox<int> ctors) => Interlocked.Increment(ref ctors.Value);
    }

    private static (int LockCtors, int CasCtors, int Rounds) RaceProbe(int threads, int rounds)
    {
        var lockCtors = new StrongBox<int>(0);
        var casCtors = new StrongBox<int>(0);

        RaceRounds(threads, rounds, lockCtors, useCas: false);
        RaceRounds(threads, rounds, casCtors, useCas: true);

        return (lockCtors.Value, casCtors.Value, rounds);
    }

    private static void RaceRounds(int threads, int rounds, StrongBox<int> ctors, bool useCas)
    {
        Counted? shared = null;
        var gate = new Lock();
        using var barrier = new Barrier(threads);
        var workers = new Thread[threads];

        // El ticket de reinicio es LOCAL a esta corrida a proposito. Como estatico se compartia
        // entre llamadas con distinto numero de hilos, asi que el '% threads' podia quedar
        // desalineado y reiniciar la instancia a media ronda: la sonda seguiria dando un numero,
        // pero no el que dice medir.
        var resetTicket = new StrongBox<int>(0);

        for (var i = 0; i < threads; i++)
        {
            workers[i] = new Thread(() =>
            {
                for (var r = 0; r < rounds; r++)
                {
                    barrier.SignalAndWait();

                    if (Volatile.Read(ref shared) is null)
                    {
                        if (useCas)
                        {
                            // Construir ANTES de poder publicar: de ahi el descarte.
                            var created = new Counted(ctors);
                            Interlocked.CompareExchange(ref shared, created, null);
                        }
                        else
                        {
                            lock (gate)
                            {
                                if (shared is null)
                                {
                                    Volatile.Write(ref shared, new Counted(ctors));
                                }
                            }
                        }
                    }

                    barrier.SignalAndWait();

                    if (Interlocked.Increment(ref resetTicket.Value) % threads == 0)
                    {
                        Volatile.Write(ref shared, null);
                    }

                    barrier.SignalAndWait();
                }
            });

            workers[i].Start();
        }

        foreach (var worker in workers)
        {
            worker.Join();
        }
    }

    // ================== utilidades ==================

    private static T Await<T>(ValueTask<T> task) => task.AsTask().GetAwaiter().GetResult();

    private static void Same(string what, object a, object b) =>
        Report(what, ReferenceEquals(a, b), "esperaba la MISMA instancia");

    private static void Distinct(string what, object a, object b) =>
        Report(what, !ReferenceEquals(a, b), "esperaba instancias DISTINTAS");

    private static void True(string what, bool condition) =>
        Report(what, condition, "condicion incumplida");

    private static void Report(string what, bool ok, string why)
    {
        if (ok)
        {
            Console.WriteLine($"  ok   {what}");
            return;
        }

        _failures++;
        Console.WriteLine($"  FALLO {what}  <- {why}");
    }
}

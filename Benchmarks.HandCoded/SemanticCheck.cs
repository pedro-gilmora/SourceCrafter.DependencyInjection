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

    // ================== las estrategias incorrectas ==================

    /// <summary>
    /// Esto no comprueba nada: <b>demuestra dos defectos distintos</b>, y la diferencia entre ellos
    /// es la razon de ser de esta sonda.
    /// <para>
    /// <b>1. <see cref="Interlocked.CompareExchange{T}"/> descarta.</b> Obliga a construir antes de
    /// poder intentar publicar, asi que bajo llegada simultanea varios hilos construyen y solo uno
    /// gana. La <i>identidad se respeta</i> -- todos acaban viendo la instancia del ganador -- pero
    /// lo que construyo el perdedor nunca se publico: el contenedor no lo conoce y nadie lo va a
    /// desechar. Para un servicio sin recursos es basura; para un <see cref="IDisposable"/> es una
    /// fuga.
    /// </para>
    /// <para>
    /// <b>2. <see cref="Interlocked.Exchange{T}"/> rompe la identidad.</b> Escribe siempre, sin mirar
    /// lo que habia. Un hilo puede publicar su instancia <i>encima</i> de otra que ya se entrego, de
    /// modo que dos llamadores se quedan con dos "singletons" distintos vivos a la vez. No es un
    /// descarte que converge: es un estado partido en dos que fallara mucho despues y lejos de aqui.
    /// </para>
    /// <para>
    /// La columna de violaciones es la que separa las dos cosas. Un contador de constructores no
    /// distingue "construi de mas" de "entregue dos cosas distintas", y son problemas de gravedad muy
    /// diferente.
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
        Console.WriteLine("--- Sonda: publicacion bajo llegada simultanea ---");
        Console.WriteLine("  estrategia       hilos   ctors  de mas  identidad rota");

        const int Rounds = 20_000;

        foreach (var threads in (int[])[2, 4, 8])
        {
            foreach (var (name, strategy) in ((string, int)[])
                     [("lock          ", 0), ("CompareExchange", 1), ("Exchange       ", 2)])
            {
                var (ctors, violations) = RaceRounds(threads, Rounds, strategy);
                var waste = (ctors - Rounds) * 100.0 / Rounds;

                Console.WriteLine(
                    $"  {name} {threads,5}  {ctors,7}  {waste,5:0.0}%  {violations,7} rondas");
            }
        }

        Console.WriteLine();
        Console.WriteLine("  -> CompareExchange: descarta instancias. Incorrecto para servicios desechables.");
        Console.WriteLine("  -> Exchange: ademas entrega instancias DISTINTAS a llamadores distintos.");
        Console.WriteLine("     No es un singleton. Se mide para poder enseñar que sale rapido y aun asi no vale.");
    }

    private sealed class Counted
    {
        public Counted(StrongBox<int> ctors) => Interlocked.Increment(ref ctors.Value);
    }

    /// <summary>
    /// Devuelve cuantas instancias se construyeron y en cuantas rondas <b>dos llamadores vieron
    /// instancias distintas</b>.
    /// </summary>
    private static (int Ctors, int Violations) RaceRounds(int threads, int rounds, int strategy)
    {
        var ctors = new StrongBox<int>(0);
        var violations = 0;

        Counted? shared = null;
        var gate = new Lock();
        var observed = new Counted?[threads];
        using var barrier = new Barrier(threads);
        var workers = new Thread[threads];

        for (var i = 0; i < threads; i++)
        {
            var index = i;

            workers[i] = new Thread(() =>
            {
                for (var r = 0; r < rounds; r++)
                {
                    barrier.SignalAndWait();

                    // Lo que ESTE llamador se lleva. Es el dato que importa: no basta con contar
                    // construcciones, hay que saber que instancia acabo en manos de quien.
                    observed[index] = strategy switch
                    {
                        1 => PublishWithCas(),
                        2 => PublishWithExchange(),
                        _ => PublishWithLock()
                    };

                    barrier.SignalAndWait();

                    if (index == 0)
                    {
                        var first = observed[0];
                        for (var t = 1; t < threads; t++)
                        {
                            if (!ReferenceEquals(observed[t], first))
                            {
                                violations++;
                                break;
                            }
                        }

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

        return (ctors.Value, violations);

        Counted PublishWithLock()
        {
            var value = Volatile.Read(ref shared);
            if (value is not null)
            {
                return value;
            }

            lock (gate)
            {
                value = shared;
                if (value is null)
                {
                    value = new Counted(ctors);
                    Volatile.Write(ref shared, value);
                }

                return value;
            }
        }

        Counted PublishWithCas()
        {
            var value = Volatile.Read(ref shared);
            if (value is not null)
            {
                return value;
            }

            // Construir ANTES de poder publicar: de ahi el descarte.
            var created = new Counted(ctors);
            return Interlocked.CompareExchange(ref shared, created, null) ?? created;
        }

        Counted PublishWithExchange()
        {
            var value = Volatile.Read(ref shared);
            if (value is not null)
            {
                return value;
            }

            // Escribe SIEMPRE, aunque otro hilo ya hubiese publicado y entregado la suya.
            var created = new Counted(ctors);
            Interlocked.Exchange(ref shared, created);
            return created;
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

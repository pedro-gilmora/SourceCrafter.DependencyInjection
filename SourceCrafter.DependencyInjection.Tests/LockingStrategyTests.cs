using FluentAssertions;

using Xunit;

namespace SourceCrafter.DependencyInjection.Tests;

/// <summary>
/// Tests aislados sobre las <b>estrategias de bloqueo</b>, sin generador ni codigo generado.
/// Modelan a mano los tres esquemas posibles para responder a una pregunta concreta: cuantos
/// objetos de bloqueo hacen falta y con que alcance.
/// <para>
/// Conviene tener presente que <c>lock</c> en .NET es <b>reentrante</b>: un hilo que ya posee
/// un candado puede volver a adquirirlo sin bloquearse. Por eso un unico candado global nunca
/// produce interbloqueo por anidamiento, y por eso <c>lock(this)</c> parece funcionar. Los
/// tests siguientes delimitan donde cada esquema si falla de verdad.
/// </para>
/// </summary>
public class LockingStrategyTests
{
    static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(500);

    /// <summary>Ejecuta <paramref name="body"/> en un hilo de fondo, para que un interbloqueo
    /// no impida terminar al proceso de pruebas.</summary>
    static Thread Start(Action body)
    {
        Thread thread = new(() => body()) { IsBackground = true };
        thread.Start();
        return thread;
    }

    // ---------------------------------------------------------------
    // Escenario 1: un candado por lifetime  (singletonLock + scopedLock)
    // ---------------------------------------------------------------

    [Fact]
    public void OneLockPerLifetimeDeadlocksWhenDependenciesCrossLifetimes()
    {
        // El generador permite que un singleton dependa de un scoped y que un scoped dependa
        // de un singleton; no emite ningun diagnostico por ello. Con un candado por lifetime
        // eso produce dos candados tomados en ordenes opuestos: interbloqueo clasico.
        object singletonLock = new(), scopedLock = new();

        using Barrier bothHoldTheirFirstLock = new(2);

        bool singletonThreadGotTheSecondLock = true, scopedThreadGotTheSecondLock = true;

        // Resuelve un Singleton que depende de un Scoped.
        var resolvingSingleton = Start(() =>
        {
            lock (singletonLock)
            {
                bothHoldTheirFirstLock.SignalAndWait(Timeout);

                singletonThreadGotTheSecondLock = Monitor.TryEnter(scopedLock, Timeout);
                if (singletonThreadGotTheSecondLock) Monitor.Exit(scopedLock);
            }
        });

        // Resuelve un Scoped que depende de un Singleton.
        var resolvingScoped = Start(() =>
        {
            lock (scopedLock)
            {
                bothHoldTheirFirstLock.SignalAndWait(Timeout);

                scopedThreadGotTheSecondLock = Monitor.TryEnter(singletonLock, Timeout);
                if (scopedThreadGotTheSecondLock) Monitor.Exit(singletonLock);
            }
        });

        resolvingSingleton.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
        resolvingScoped.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();

        // Ninguno de los dos consigue avanzar: cada uno retiene lo que el otro necesita.
        singletonThreadGotTheSecondLock.Should().BeFalse(
            "el hilo del singleton retiene singletonLock y espera scopedLock");
        scopedThreadGotTheSecondLock.Should().BeFalse(
            "el hilo del scoped retiene scopedLock y espera singletonLock");
    }

    // ---------------------------------------------------------------
    // Escenario 2: un unico candado para todo
    // ---------------------------------------------------------------

    [Fact]
    public void ASingleLockNeverDeadlocksBecauseLocksAreReentrant()
    {
        // Esta es la razon por la que usar 'this' como candado no ha fallado nunca por
        // interbloqueo: resolver A dentro de la resolucion de B reentra en el mismo candado.
        object theOnlyLock = new();

        var reachedTheInnermostResolution = false;

        var resolving = Start(() =>
        {
            lock (theOnlyLock)          // resolviendo A
            {
                lock (theOnlyLock)      // A necesita B
                {
                    lock (theOnlyLock)  // B necesita C
                    {
                        reachedTheInnermostResolution = true;
                    }
                }
            }
        });

        resolving.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
        reachedTheInnermostResolution.Should().BeTrue();
    }

    [Fact]
    public void ASingleLockSerializesUnrelatedConstructions()
    {
        // El precio de un candado unico no es correccion, es concurrencia: dos servicios sin
        // relacion alguna no pueden construirse a la vez, aunque sus fabricas sean lentas.
        object theOnlyLock = new();

        using Barrier bothInsideTheirConstructor = new(2);

        bool bothRanConcurrently = true;

        var first = Start(() =>
        {
            lock (theOnlyLock)
            {
                bothRanConcurrently &= bothInsideTheirConstructor.SignalAndWait(Timeout);
            }
        });

        var second = Start(() =>
        {
            lock (theOnlyLock)
            {
                bothRanConcurrently &= bothInsideTheirConstructor.SignalAndWait(Timeout);
            }
        });

        first.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
        second.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();

        bothRanConcurrently.Should().BeFalse(
            "con un candado unico el segundo constructor no puede entrar hasta que el primero salga");
    }

    // ---------------------------------------------------------------
    // Escenario 3: un candado por dependencia
    // ---------------------------------------------------------------

    [Fact]
    public void OneLockPerDependencyDoesNotDeadlockOnCrossLifetimeGraphs()
    {
        // Mismo grafo que en el escenario 1, pero cada servicio tiene su propio candado. Los
        // candados se toman siguiendo las aristas del grafo de dependencias; como el generador
        // rechaza los ciclos, ese orden es un orden topologico y no puede haber ciclo de
        // espera.
        object sing1Lock = new(), sing2Lock = new(), scoped1Lock = new(), scoped2Lock = new();

        using Barrier bothHoldTheirFirstLock = new(2);

        bool singletonThreadCompleted = false, scopedThreadCompleted = false;

        // Sing1 -> Scoped2
        var resolvingSingleton = Start(() =>
        {
            lock (sing1Lock)
            {
                bothHoldTheirFirstLock.SignalAndWait(Timeout);

                if (Monitor.TryEnter(scoped2Lock, Timeout))
                {
                    Monitor.Exit(scoped2Lock);
                    singletonThreadCompleted = true;
                }
            }
        });

        // Scoped1 -> Sing2
        var resolvingScoped = Start(() =>
        {
            lock (scoped1Lock)
            {
                bothHoldTheirFirstLock.SignalAndWait(Timeout);

                if (Monitor.TryEnter(sing2Lock, Timeout))
                {
                    Monitor.Exit(sing2Lock);
                    scopedThreadCompleted = true;
                }
            }
        });

        resolvingSingleton.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
        resolvingScoped.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();

        singletonThreadCompleted.Should().BeTrue();
        scopedThreadCompleted.Should().BeTrue();
    }

    [Fact]
    public void OneLockPerDependencyAllowsUnrelatedConstructionsToRunConcurrently()
    {
        object firstLock = new(), secondLock = new();

        using Barrier bothInsideTheirConstructor = new(2);

        var bothRanConcurrently = true;

        var first = Start(() =>
        {
            lock (firstLock)
            {
                bothRanConcurrently &= bothInsideTheirConstructor.SignalAndWait(Timeout);
            }
        });

        var second = Start(() =>
        {
            lock (secondLock)
            {
                bothRanConcurrently &= bothInsideTheirConstructor.SignalAndWait(Timeout);
            }
        });

        first.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
        second.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();

        bothRanConcurrently.Should().BeTrue(
            "candados independientes permiten construir servicios sin relacion en paralelo");
    }

    // ---------------------------------------------------------------
    // Donde 'this' si falla: alcance, no interbloqueo
    // ---------------------------------------------------------------

    [Fact]
    public void AnInstanceLockDoesNotGuardAStaticField()
    {
        // El fallo real de lock(this) sobre un campo singleton no es un interbloqueo sino la
        // ausencia total de exclusion mutua: cada contenedor bloquea un objeto distinto, asi
        // que dos instancias construyen el mismo singleton compartido.
        object firstContainer = new(), secondContainer = new();

        string? sharedSingleton = null;
        var constructions = 0;

        using Barrier bothInsideTheirLock = new(2);

        void Resolve(object container)
        {
            lock (container)
            {
                // Que las dos puedan estar aqui a la vez es exactamente el fallo.
                bothInsideTheirLock.SignalAndWait(Timeout);

                var mustConstruct = sharedSingleton is null;

                // Segunda fase: ambos han leido ya, asi que la escritura de uno no puede
                // ocultarle al otro que tambien vio el campo vacio.
                bothInsideTheirLock.SignalAndWait(Timeout);

                if (mustConstruct)
                {
                    Interlocked.Increment(ref constructions);
                    sharedSingleton = "instancia";
                }
            }
        }

        var first = Start(() => Resolve(firstContainer));
        var second = Start(() => Resolve(secondContainer));

        first.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
        second.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();

        constructions.Should().Be(2,
            "dos candados distintos no excluyen nada: el singleton se construye dos veces");
    }

    [Fact]
    public void AStaticLockGuardsAStaticField()
    {
        // El mismo escenario con el candado en el alcance correcto. Aqui no se puede forzar el
        // solapamiento con una barrera: que sea imposible es precisamente lo que se comprueba.
        object sharedLock = new();

        string? sharedSingleton = null;
        var constructions = 0;

        void Resolve()
        {
            lock (sharedLock)
            {
                if (sharedSingleton is null)
                {
                    Interlocked.Increment(ref constructions);
                    Thread.Sleep(20); // ensancha la ventana de carrera
                    sharedSingleton = "instancia";
                }
            }
        }

        var first = Start(Resolve);
        var second = Start(Resolve);

        first.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
        second.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();

        constructions.Should().Be(1);
    }

    // ---------------------------------------------------------------
    // La propuesta concreta: 'this' para scoped + un estatico para singleton
    // ---------------------------------------------------------------

    /// <summary>
    /// Modela un contenedor con exactamente esa estrategia. El estado "estatico" se pasa
    /// como objeto compartido en vez de usar campos <c>static</c> reales, para que cada
    /// test quede aislado; el comportamiento de bloqueo es identico, que es lo que importa.
    /// </summary>
    sealed class ThisAndStaticContainer(ThisAndStaticContainer.SharedSingletonState shared)
    {
        internal sealed class SharedSingletonState
        {
            internal readonly object Lock = new();
            internal object? Singleton;
        }

        object? _scoped;

        /// <summary>Resuelve un singleton cuya dependencia es un scoped.</summary>
        internal bool ResolveSingletonNeedingScoped(Barrier barrier, TimeSpan wait)
        {
            lock (shared.Lock)
            {
                barrier.SignalAndWait(wait);

                // El getter del scoped hace lock(this).
                if (!Monitor.TryEnter(this, wait)) return false;

                try { shared.Singleton ??= _scoped ??= new(); }
                finally { Monitor.Exit(this); }

                return true;
            }
        }

        /// <summary>Resuelve un scoped cuya dependencia es un singleton.</summary>
        internal bool ResolveScopedNeedingSingleton(Barrier barrier, TimeSpan wait)
        {
            lock (this)
            {
                barrier.SignalAndWait(wait);

                // El getter del singleton hace lock(SingletonLock).
                if (!Monitor.TryEnter(shared.Lock, wait)) return false;

                try { _scoped ??= shared.Singleton ??= new(); }
                finally { Monitor.Exit(shared.Lock); }

                return true;
            }
        }
    }

    [Fact]
    public void ThisForScopedPlusAStaticForSingletonDeadlocksOnASharedContainer()
    {
        // Dos hilos resolviendo sobre la MISMA instancia. Es el caso del contenedor raiz, que
        // por definicion se comparte: ahi viven los singletons y ahi tambien se cachean los
        // scoped cuando no se ha creado un ambito.
        ThisAndStaticContainer.SharedSingletonState shared = new();
        ThisAndStaticContainer container = new(shared);

        using Barrier bothHoldTheirFirstLock = new(2);

        bool singletonThreadCompleted = true, scopedThreadCompleted = true;

        var a = Start(() => singletonThreadCompleted =
            container.ResolveSingletonNeedingScoped(bothHoldTheirFirstLock, Timeout));

        var b = Start(() => scopedThreadCompleted =
            container.ResolveScopedNeedingSingleton(bothHoldTheirFirstLock, Timeout));

        a.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
        b.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();

        // Un hilo retiene el candado estatico y quiere 'this'; el otro retiene 'this' y quiere
        // el estatico. Es el mismo ciclo de espera del escenario 1: los nombres del candado
        // cambian, la topologia no.
        singletonThreadCompleted.Should().BeFalse();
        scopedThreadCompleted.Should().BeFalse();
    }

    [Fact]
    public void ThisForScopedPlusAStaticForSingletonSurvivesWhenScopesAreSeparate()
    {
        // Matiz importante y a favor de la propuesta: con dos ambitos distintos, 'this' difiere,
        // el ciclo se rompe y solo hay espera, no interbloqueo. Por eso el fallo es
        // intermitente y dificil de atribuir: solo aparece cuando dos hilos comparten instancia.
        ThisAndStaticContainer.SharedSingletonState shared = new();
        ThisAndStaticContainer first = new(shared), second = new(shared);

        using Barrier bothHoldTheirFirstLock = new(2);

        bool singletonThreadCompleted = false, scopedThreadCompleted = false;

        var a = Start(() => singletonThreadCompleted =
            first.ResolveSingletonNeedingScoped(bothHoldTheirFirstLock, Timeout));

        var b = Start(() => scopedThreadCompleted =
            second.ResolveScopedNeedingSingleton(bothHoldTheirFirstLock, Timeout));

        a.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
        b.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();

        singletonThreadCompleted.Should().BeTrue();
        scopedThreadCompleted.Should().BeTrue();
    }

    [Fact]
    public void ThisIsAPubliclyReachableLockThatAnyoneCanHold()
    {
        // Problema independiente del interbloqueo: 'this' es visible para cualquiera que tenga
        // una referencia al contenedor, asi que codigo ajeno puede bloquear toda resolucion
        // scoped sin saberlo. Un candado privado no es alcanzable desde fuera.
        ThisAndStaticContainer.SharedSingletonState shared = new();
        ThisAndStaticContainer container = new(shared);

        using ManualResetEventSlim outsiderHoldsTheContainer = new();
        using ManualResetEventSlim releaseTheOutsider = new();

        var outsider = Start(() =>
        {
            lock (container)
            {
                outsiderHoldsTheContainer.Set();
                releaseTheOutsider.Wait(TimeSpan.FromSeconds(5));
            }
        });

        outsiderHoldsTheContainer.Wait(Timeout).Should().BeTrue();

        var resolutionGotThrough = Monitor.TryEnter(container, Timeout);
        if (resolutionGotThrough) Monitor.Exit(container);

        releaseTheOutsider.Set();
        outsider.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();

        resolutionGotThrough.Should().BeFalse(
            "codigo ajeno al contenedor puede detener la resolucion de servicios scoped");
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;

using SourceCrafter.DependencyInjection.Attributes;

using Xunit;

namespace SourceCrafter.DependencyInjection.Tests;

public sealed class Flaky
{
    internal static int Attempts;

    internal static async Task<Flaky> CreateAsync()
    {
        if (Interlocked.Increment(ref Attempts) == 1)
        {
            await Task.Yield();
            throw new InvalidOperationException("fallo transitorio");
        }

        return new Flaky();
    }
}

[ServiceContainer]
[Singleton<Flaky>(source: nameof(CreateFlakyAsync))]
public partial class FlakyContainer
{
    static Task<Flaky> CreateFlakyAsync() => Flaky.CreateAsync();
}

/// <summary>
/// Semantica de la cache de tareas de las fabricas asincronas.
///
/// <para>El resolver cachea la <see cref="Task"/>, no el valor, para que N llamadores
/// concurrentes esperen una sola ejecucion. Eso plantea una pregunta que estos tests fijan:
/// que pasa cuando esa unica ejecucion <b>falla</b>.</para>
/// </summary>
public class AsyncTaskCachingTests
{
    /// <summary>
    /// El campo de respaldo de un singleton es <c>static</c>, asi que los dos hechos que
    /// interesan se comprueban en un solo test secuencial: dos tests separados compartirian
    /// estado y dependerian del orden de ejecucion.
    /// </summary>
    [Fact]
    public async Task AFailedFactoryDoesNotPoisonTheContainerForever()
    {
        Flaky.Attempts = 0;

        var container = new FlakyContainer();

        // La primera resolucion falla.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => container.CreateFlakyAsyncCached);

        // La segunda debe reintentar, no devolver la tarea fallida cacheada. Cachear el
        // fallo dejaria el contenedor inservible tras un fallo de red transitorio, y ademas
        // obligaria a tomar el candado en cada llamada posterior para siempre.
        var second = await container.CreateFlakyAsyncCached;

        second.Should().NotBeNull();
        Flaky.Attempts.Should().Be(2);

        // Y una vez que hay una tarea completada con exito, se cachea: la fabrica no
        // vuelve a ejecutarse.
        var third = await container.CreateFlakyAsyncCached;

        third.Should().BeSameAs(second);
        Flaky.Attempts.Should().Be(2);
    }
}

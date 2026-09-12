namespace Benchmarks.HandCoded;

/// <summary>
/// Grafo de servicios compartido por <b>todos</b> los contenedores del banco.
/// <para>
/// La matriz que mide este proyecto cruza cuatro ejes: <b>locking</b> (con candado o sin el),
/// <b>lifetime</b> (singleton, scoped, transient), <b>async-kind</b> (sincrono,
/// <see cref="ValueTask{TResult}"/>, <see cref="Task{TResult}"/>) y <b>disposability</b> (nada,
/// <see cref="IDisposable"/>, <see cref="IAsyncDisposable"/>).
/// </para>
/// <para>
/// <b>Los nueve tipos tienen el constructor igual de barato a proposito.</b> Ninguno toma
/// dependencias y ninguno hace trabajo. Si un tipo costara mas que otro, la celda de la matriz
/// que lo usa saldria peor y se leeria como si la <i>combinacion</i> fuera mas lenta, cuando lo
/// caro seria el servicio. Al igualarlos, lo unico que separa dos celdas es la maquinaria del
/// contenedor, que es justo lo que se quiere medir.
/// </para>
/// <para>
/// Por la misma razon hay nueve tipos distintos en vez de tres reutilizados con clave: meter
/// claves para distinguir la forma de produccion mezclaria el eje de despacho por clave dentro
/// del eje async-kind, y las dos cosas dejarian de poder leerse por separado.
/// </para>
/// <para>
/// El campo <c>Disposed</c> no existe para medir: existe para que
/// <see cref="SemanticCheck"/> pueda comprobar que un contenedor desecha de verdad lo que dice
/// desechar, <b>antes</b> de que se publique ninguna cifra suya.
/// </para>
/// </summary>
public interface IService
{
    bool Disposed { get; }
}

// ===== async-kind: sincrono =====

public sealed class SyncPlain : IService
{
    public bool Disposed => false;
}

public sealed class SyncDisp : IService, IDisposable
{
    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}

public sealed class SyncAsyncDisp : IService, IAsyncDisposable
{
    public bool Disposed { get; private set; }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return default;
    }
}

// ===== async-kind: ValueTask =====
//
// Las fabricas devuelven una tarea ya completada. Es deliberado y es lo que hace honesto el
// eje: una fabrica que de verdad cede el hilo (Task.Yield, E/S real) mediria la planificacion
// del pool, que es varios ordenes de magnitud mayor que el contenedor y tapa por completo lo
// que se intenta comparar. Una sesion anterior de este repo publico un 475x por ese error.
//
// Lo que si queda medido es lo unico que aqui depende del contenedor: si el camino rapido
// evita la maquina de estados cuando el valor ya esta disponible, o si se paga una por
// resolucion.

public sealed class VtPlain : IService
{
    public bool Disposed => false;

    public static ValueTask<VtPlain> CreateAsync() => new(new VtPlain());
}

public sealed class VtDisp : IService, IDisposable
{
    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;

    public static ValueTask<VtDisp> CreateAsync() => new(new VtDisp());
}

public sealed class VtAsyncDisp : IService, IAsyncDisposable
{
    public bool Disposed { get; private set; }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return default;
    }

    public static ValueTask<VtAsyncDisp> CreateAsync() => new(new VtAsyncDisp());
}

// ===== async-kind: Task =====

public sealed class TaskPlain : IService
{
    public bool Disposed => false;

    public static Task<TaskPlain> CreateAsync() => Task.FromResult(new TaskPlain());
}

public sealed class TaskDisp : IService, IDisposable
{
    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;

    public static Task<TaskDisp> CreateAsync() => Task.FromResult(new TaskDisp());
}

public sealed class TaskAsyncDisp : IService, IAsyncDisposable
{
    public bool Disposed { get; private set; }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return default;
    }

    public static Task<TaskAsyncDisp> CreateAsync() => Task.FromResult(new TaskAsyncDisp());
}

// ===== Grafo profundo, solo para el head-to-head =====
//
// Cuatro niveles y quince nodos, la misma forma que publican los bancos de Pure.DI, para que
// la cifra sea comparable con lo que publica la competencia. No participa en la matriz: alli
// lo que se aisla es la maquinaria por celda, y un grafo de quince nodos la taparia.

public sealed class Leaf;

public sealed class Level3(Leaf first, Leaf second)
{
    public Leaf First { get; } = first;
    public Leaf Second { get; } = second;
}

public sealed class Level2(Level3 first, Level3 second)
{
    public Level3 First { get; } = first;
    public Level3 Second { get; } = second;
}

public sealed class Level1(Level2 first, Level2 second, IService service)
{
    public Level2 First { get; } = first;
    public Level2 Second { get; } = second;
    public IService Service { get; } = service;
}

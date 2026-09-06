using System.Runtime.CompilerServices;
using System.Threading;

using BenchmarkDotNet.Attributes;

namespace Benchmarks;

/// <summary>
/// Estudio de la <b>forma</b> del resolver cacheado, aislado del generador: tres variantes
/// escritas a mano del mismo servicio ya construido. Responde a una sola pregunta: ¿que aporta
/// realmente sacar el camino lento a un metodo <c>NoInlining</c>?
/// </summary>
[MemoryDiagnoser]
public class ResolverShapeBenchmark
{
    private readonly AlwaysLocks _always = new();
    private readonly InlineLock _inline = new();
    private readonly SplitSlowPath _split = new();

    [GlobalSetup]
    public void Setup()
    {
        // Todas arrancan ya construidas: se mide el camino caliente, no la construccion.
        _ = _always.Value;
        _ = _inline.Value;
        _ = _split.Value;
    }

    /// <summary>Sin comprobacion previa: entra al candado en cada resolucion.</summary>
    [Benchmark(Description = "Always locks")]
    public Payload AlwaysLocksResolve() => _always.Value;

    /// <summary>Comprobacion rapida, pero el <c>lock</c> vive dentro del getter.</summary>
    [Benchmark(Baseline = true, Description = "Fast path + inline lock")]
    public Payload InlineLockResolve() => _inline.Value;

    /// <summary>Comprobacion rapida y camino lento en un metodo aparte (lo que se emite hoy).</summary>
    [Benchmark(Description = "Fast path + NoInlining slow path")]
    public Payload SplitSlowPathResolve() => _split.Value;
}

public sealed class Payload(Settings settings)
{
    public Settings Settings { get; } = settings;
}

public sealed class AlwaysLocks
{
    private Payload? _value;
    private readonly Lock _lock = new();

    public Payload Value
    {
        get
        {
            lock (_lock) return _value ??= new Payload(new Settings());
        }
    }
}

public sealed class InlineLock
{
    private Payload? _value;
    private readonly Lock _lock = new();

    public Payload Value
    {
        get
        {
            if (_value is not null) return _value;

            lock (_lock) return _value ??= new Payload(new Settings());
        }
    }
}

public sealed class SplitSlowPath
{
    private Payload? _value;
    private readonly Lock _lock = new();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Payload Create()
    {
        lock (_lock) return _value ??= new Payload(new Settings());
    }

    public Payload Value
    {
        get
        {
            if (_value is not null) return _value;

            return Create();
        }
    }
}

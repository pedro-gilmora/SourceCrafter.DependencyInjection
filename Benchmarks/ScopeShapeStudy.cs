using BenchmarkDotNet.Attributes;

namespace Benchmarks;

/// <summary>
/// Estudio de la <b>forma</b> del ambito, aislado del generador. Responde a una sola pregunta:
/// ¿de donde sale la diferencia frente a Jab en "crear ambito sin resolver nada"?
/// <para>
/// Las tres variantes tienen los <b>mismos campos</b>, asi que asignan exactamente lo mismo
/// (40 B). Lo unico que cambia es el despacho: virtual heredado (lo que se emite hoy),
/// virtual heredado pero con el ambito <c>sealed</c>, y no-virtual con <c>_root ?? this</c>
/// (lo que hacen Jab y Pure.DI). Si la diferencia con Jab fuera real, tendria que aparecer aqui.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class ScopeShapeBenchmark
{
	private readonly VirtualScopeRoot _virtual = new();
	private readonly SealedScopeRoot _sealed = new();
	private readonly NonVirtualScopeRoot _nonVirtual = new();
	private readonly JabLikeRoot _jabLike = new();

	/// <summary>Lo que se emite hoy: <c>CreateScope</c> virtual y ambito derivado no sellado.</summary>
	[Benchmark(Baseline = true, Description = "Virtual CreateScope, open Scoped")]
	public object VirtualOpen()
	{
		var scope = _virtual.CreateScope();
		scope.Dispose();
		return scope;
	}

	/// <summary>Igual, pero el ambito derivado es <c>sealed</c>.</summary>
	[Benchmark(Description = "Virtual CreateScope, sealed Scoped")]
	public object VirtualSealed()
	{
		var scope = _sealed.CreateScope();
		scope.Dispose();
		return scope;
	}

	/// <summary>Sin virtual: un solo metodo que propaga con <c>_root ?? this</c>.</summary>
	[Benchmark(Description = "Non-virtual CreateScope, sealed Scoped")]
	public object NonVirtualSealed()
	{
		var scope = _nonVirtual.CreateScope();
		scope.Dispose();
		return scope;
	}

	/// <summary>Forma de Jab: ambito en una clase propia, no derivada del contenedor.</summary>
	[Benchmark(Description = "Separate scope class (Jab shape)")]
	public object JabShape()
	{
		var scope = _jabLike.CreateScope();
		scope.Dispose();
		return scope;
	}
}

#pragma warning disable CA1816

public class VirtualScopeRoot : global::System.IDisposable
{
	protected bool _disposed;
	protected Session? _session;
	private VirtualScopeRoot _root = default!;

	public virtual VirtualScopeRoot Root => this;

	public virtual Scoped CreateScope() => new() { _root = this };

	public class Scoped : VirtualScopeRoot
	{
		public override VirtualScopeRoot Root => _root;

		public override Scoped CreateScope() => new() { _root = _root };

		public override void Dispose()
		{
			if (_disposed) return;

			_disposed = true;

			ScopedDispose();
		}
	}

	protected void ScopedDispose()
	{
		var disposing = _session;
		_session = null;
		disposing?.Dispose();
	}

	public virtual void Dispose()
	{
		if (_disposed) return;

		_disposed = true;

		ScopedDispose();
	}
}

public class SealedScopeRoot : global::System.IDisposable
{
	protected bool _disposed;
	protected Session? _session;
	private SealedScopeRoot _root = default!;

	public virtual SealedScopeRoot Root => this;

	public virtual Scoped CreateScope() => new() { _root = this };

	public sealed class Scoped : SealedScopeRoot
	{
		public override SealedScopeRoot Root => _root;

		public override Scoped CreateScope() => new() { _root = _root };

		public override void Dispose()
		{
			if (_disposed) return;

			_disposed = true;

			ScopedDispose();
		}
	}

	protected void ScopedDispose()
	{
		var disposing = _session;
		_session = null;
		disposing?.Dispose();
	}

	public virtual void Dispose()
	{
		if (_disposed) return;

		_disposed = true;

		ScopedDispose();
	}
}

/// <summary>
/// Ni <c>CreateScope</c> ni <c>Root</c> necesitan ser virtuales: basta con que el ambito
/// propague <c>_root ?? this</c>, que es justo lo que hace el constructor de ambito de Pure.DI.
/// Un metodo no virtual cuyo cuerpo es una asignacion si lo puede insertar el JIT.
/// </summary>
public class NonVirtualScopeRoot : global::System.IDisposable
{
	private bool _disposed;
	private Session? _session;
	private NonVirtualScopeRoot? _root;

	public NonVirtualScopeRoot Root => _root ?? this;

	public Scoped CreateScope() => new() { _root = _root ?? this };

	public sealed class Scoped : NonVirtualScopeRoot;

	public void Dispose()
	{
		if (_disposed) return;

		_disposed = true;

		var disposing = _session;
		_session = null;
		disposing?.Dispose();
	}
}

/// <summary>Forma de Jab: el ambito es una clase independiente con un campo al contenedor.</summary>
public class JabLikeRoot
{
	public Scope CreateScope() => new(this);

	public sealed class Scope(JabLikeRoot root) : global::System.IDisposable
	{
		private Session? _session;
		private readonly JabLikeRoot _root = root;

		public JabLikeRoot Root => _root;

		public void Dispose() => _session?.Dispose();
	}
}

#pragma warning restore CA1816

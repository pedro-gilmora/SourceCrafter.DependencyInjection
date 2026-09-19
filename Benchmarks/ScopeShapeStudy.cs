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

// ---------------------------------------------------------------------------
// Estudio: ¿cuanto de la ventaja de CircleDI en el escenario "ambito con resolucion"
// es su forma de ambito, y cuanto es simplemente no sincronizar?
//
// La comparacion de contenedores media dos cosas a la vez (forma + candado), asi que
// no permitia atribuir la diferencia. Aqui la forma se mantiene FIJA -- la ligera, la
// de CircleDI: ambito propio, solo el campo scoped, sin virtuales -- y lo unico que
// cambia es la sincronizacion del resolver:
//
//   - sin candado            : lo que hace CircleDI hoy
//   - lock(this)             : nuestro LockOptions.Instance sobre esa misma forma
//   - lock(objeto dedicado)  : un candado por dependencia, LockOptions.Dedicated
//
// Responde a la pregunta directamente: si CircleDI tuviera un candado configurado,
// ¿seguiria rindiendo igual?
// ---------------------------------------------------------------------------

/// <summary>
/// Abrir un ambito, resolver el servicio scoped y liberarlo. Misma forma en las tres
/// variantes; lo unico que cambia es el candado.
/// </summary>
[MemoryDiagnoser]
public class ScopeLockCostBenchmark
{
	private readonly LightScopeRoot _root = new();

	[Benchmark(Baseline = true, Description = "No lock (CircleDI shape)")]
	public ISession NoLock()
	{
		var scope = _root.CreateScopeUnlocked();
		var session = scope.Session;
		scope.Dispose();
		return session;
	}

	[Benchmark(Description = "lock(this) on the same shape")]
	public ISession LockThis()
	{
		var scope = _root.CreateScopeLockThis();
		var session = scope.Session;
		scope.Dispose();
		return session;
	}

	[Benchmark(Description = "lock(dedicated object) on the same shape")]
	public ISession LockDedicated()
	{
		var scope = _root.CreateScopeLockDedicated();
		var session = scope.Session;
		scope.Dispose();
		return session;
	}
}

/// <summary>
/// Contenedor con la forma ligera: el ambito es una clase propia que solo lleva el campo
/// scoped y una referencia al contenedor. Las tres clases de ambito son identicas salvo
/// por la sincronizacion del resolver.
/// </summary>
public sealed class LightScopeRoot
{
	private static readonly IDatabase Database = new Database(new Settings());

	public UnlockedScope CreateScopeUnlocked() => new(this);

	public LockThisScope CreateScopeLockThis() => new(this);

	public LockDedicatedScope CreateScopeLockDedicated() => new(this);

	/// <summary>Sin sincronizar: un ambito modela una peticion y no se comparte entre hilos.</summary>
	public sealed class UnlockedScope(LightScopeRoot root) : global::System.IDisposable
	{
		private readonly LightScopeRoot _root = root;
		private Session? _session;

		public ISession Session => _session ?? Create();

		[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		private Session Create() => _session ??= new Session(Database);

		public void Dispose()
		{
			var disposing = _session;
			_session = null;
			disposing?.Dispose();
		}
	}

	/// <summary>Cierra sobre <c>this</c>: no asigna candado, pero paga el monitor.</summary>
	public sealed class LockThisScope(LightScopeRoot root) : global::System.IDisposable
	{
		private readonly LightScopeRoot _root = root;
		private Session? _session;

		public ISession Session => _session ?? Create();

		[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		private Session Create()
		{
			lock (this) return _session ??= new Session(Database);
		}

		public void Dispose()
		{
			var disposing = _session;
			_session = null;
			disposing?.Dispose();
		}
	}

	/// <summary>Candado propio: ademas del monitor, asigna un objeto por ambito.</summary>
	public sealed class LockDedicatedScope(LightScopeRoot root) : global::System.IDisposable
	{
		private readonly LightScopeRoot _root = root;
		private readonly global::System.Threading.Lock _lock = new();
		private Session? _session;

		public ISession Session => _session ?? Create();

		[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		private Session Create()
		{
			lock (_lock) return _session ??= new Session(Database);
		}

		public void Dispose()
		{
			var disposing = _session;
			_session = null;
			disposing?.Dispose();
		}
	}
}

#pragma warning restore CA1816

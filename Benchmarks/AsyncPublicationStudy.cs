using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using BenchmarkDotNet.Attributes;

namespace Benchmarks;

// ---------------------------------------------------------------------------
// Estudio: como ALMACENAR un resolver asincrono cacheado.
//
// El campo que se emite hoy es 'ValueTask<T>?', o sea 'Nullable<ValueTask<T>>':
// un struct de cinco campos (hasValue, _obj, _result, _token,
// _continueOnCapturedContext). El CLR solo garantiza atomicidad hasta el tamano
// de puntero, asi que el '_campo = null' del liberador son varios stores y un
// lector concurrente puede ver una mezcla. Esta medido: 6 valores desgarrados en
// 216.400 lecturas, sin una sola excepcion que lo delate.
//
// Las alternativas publican con UN SOLO store de puntero, que si es atomico y
// ademas es un 'release' segun el modelo de memoria de .NET.
//
// Nota sobre el nombre: aqui NO se llama a ValueTask<T>.AsTask(), que es otra
// cosa (convierte un ValueTask ya existente y asigna si no venia de un Task).
// 'TaskFieldHolder' devuelve el Task<T> que YA tiene guardado en el campo: cero
// conversion y cero asignacion en la lectura. Por eso se mide aparte.
//
// La cuarta variante es la que importa: una vez que la tarea completo con exito,
// lo unico que hace falta guardar es el RESULTADO. Un 'T?' es una referencia:
// publicacion atomica, cero asignacion, y el ValueTask se reconstruye al vuelo
// desde el valor (el constructor de ValueTask<T> sobre un T es un struct, no
// asigna).
// ---------------------------------------------------------------------------

/// <summary>
/// Camino <b>caliente</b>: leer un resolver asincrono que ya completo con exito.
/// Es lo que se paga en cada resolucion.
/// </summary>
[MemoryDiagnoser]
public class AsyncPublicationReadBenchmark
{
	private readonly NullableValueTaskHolder _nullable = new();
	private readonly BoxHolder _box = new();
	private readonly TaskFieldHolder _task = new();
	private readonly ResultFieldHolder _result = new();

	[GlobalSetup]
	public void Setup()
	{
		// Todas arrancan ya resueltas: se mide el camino rapido, no la construccion.
		_ = _nullable.Value;
		_ = _box.Value;
		_ = _task.Value;
		_ = _task.Task;
		_ = _result.Value;
	}

	/// <summary>Lo que se emite hoy. La publicacion no es atomica.</summary>
	[Benchmark(Baseline = true, Description = "ValueTask<T>? field (current, tears)")]
	public ValueTask<Payload> NullableValueTask() => _nullable.Value;

	/// <summary>Caja: el <c>ValueTask</c> vive dentro de una clase; el campo es una referencia.</summary>
	[Benchmark(Description = "Box class wrapping the ValueTask<T>")]
	public ValueTask<Payload> Box() => _box.Value;

	/// <summary>Campo <c>Task&lt;T&gt;</c>, envuelto en <c>ValueTask&lt;T&gt;</c> al salir.</summary>
	[Benchmark(Description = "Task<T> field -> ValueTask<T>")]
	public ValueTask<Payload> TaskFieldAsValueTask() => _task.Value;

	/// <summary>Campo <c>Task&lt;T&gt;</c> devuelto tal cual, sin conversion alguna.</summary>
	[Benchmark(Description = "Task<T> field -> Task<T>")]
	public Task<Payload> TaskFieldBare() => _task.Task;

	/// <summary>Solo el resultado. Referencia: atomica y sin asignar.</summary>
	[Benchmark(Description = "T? field -> new ValueTask<T>(result)")]
	public ValueTask<Payload> ResultField() => _result.Value;
}

/// <summary>
/// Camino <b>frio</b>: publicar. Ocurre una vez por resolver, no una por resolucion,
/// pero es donde estan las diferencias de asignacion.
/// </summary>
[MemoryDiagnoser]
public class AsyncPublicationWriteBenchmark
{
	private readonly Payload _payload = new(new Settings());

	[Benchmark(Baseline = true, Description = "Publish ValueTask<T>? (current)")]
	public ValueTask<Payload>? PublishNullable() => new ValueTask<Payload>(_payload);

	[Benchmark(Description = "Publish a Box")]
	public ValueTaskBox<Payload> PublishBox() => new(new ValueTask<Payload>(_payload));

	[Benchmark(Description = "Publish via Task.FromResult")]
	public Task<Payload> PublishFromResult() => Task.FromResult(_payload);

	/// <summary>Convierte un <c>ValueTask</c> ya existente: esto si es el <c>AsTask()</c> del BCL.</summary>
	[Benchmark(Description = "Publish via ValueTask<T>.AsTask()")]
	public Task<Payload> PublishAsTask() => new ValueTask<Payload>(_payload).AsTask();

	[Benchmark(Description = "Publish the result reference")]
	public Payload PublishResult() => _payload;
}

/// <summary>La caja. Un unico campo de solo lectura: se publica entera o no se publica.</summary>
public sealed class ValueTaskBox<T>(ValueTask<T> value)
{
	public readonly ValueTask<T> Value = value;
}

public sealed class NullableValueTaskHolder
{
	private ValueTask<Payload>? _field;
	private readonly Lock _lock = new();

	[MethodImpl(MethodImplOptions.NoInlining)]
	private ValueTask<Payload> Create()
	{
		lock (_lock) return (_field = new ValueTask<Payload>(new Payload(new Settings()))).Value;
	}

	public ValueTask<Payload> Value
	{
		get
		{
			if (_field is { IsCompletedSuccessfully: true } v) return v;

			return Create();
		}
	}
}

public sealed class BoxHolder
{
	private ValueTaskBox<Payload>? _field;
	private readonly Lock _lock = new();

	[MethodImpl(MethodImplOptions.NoInlining)]
	private ValueTask<Payload> Create()
	{
		lock (_lock) return (_field ??= new(new ValueTask<Payload>(new Payload(new Settings())))).Value;
	}

	public ValueTask<Payload> Value
	{
		get
		{
			// Una sola lectura del campo; el ValueTask de dentro ya no puede desgarrarse
			// porque la caja se publica entera con un unico store de puntero.
			if (_field is { } box && box.Value.IsCompletedSuccessfully) return box.Value;

			return Create();
		}
	}
}

public sealed class TaskFieldHolder
{
	private Task<Payload>? _field;
	private readonly Lock _lock = new();

	[MethodImpl(MethodImplOptions.NoInlining)]
	private Task<Payload> Create()
	{
		lock (_lock) return _field ??= System.Threading.Tasks.Task.FromResult(new Payload(new Settings()));
	}

	/// <summary>Envuelve el <c>Task</c> guardado. El constructor no asigna.</summary>
	public ValueTask<Payload> Value
	{
		get
		{
			if (_field is { IsCompletedSuccessfully: true } v) return new ValueTask<Payload>(v);

			return new ValueTask<Payload>(Create());
		}
	}

	/// <summary>Devuelve el <c>Task</c> guardado tal cual: ni conversion ni asignacion.</summary>
	public Task<Payload> Task
	{
		get
		{
			if (_field is { IsCompletedSuccessfully: true } v) return v;

			return Create();
		}
	}
}

/// <summary>
/// Guarda solo el <b>resultado</b>, no la tarea. Una vez que completo con exito la tarea
/// no aporta nada: el <c>ValueTask</c> se reconstruye desde el valor sin asignar.
/// </summary>
public sealed class ResultFieldHolder
{
	private Payload? _field;
	private readonly Lock _lock = new();

	[MethodImpl(MethodImplOptions.NoInlining)]
	private ValueTask<Payload> Create()
	{
		lock (_lock) return new ValueTask<Payload>(_field ??= new Payload(new Settings()));
	}

	public ValueTask<Payload> Value
	{
		get
		{
			var v = _field;

			if (v is not null) return new ValueTask<Payload>(v);

			return Create();
		}
	}
}

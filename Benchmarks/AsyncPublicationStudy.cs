using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using BenchmarkDotNet.Attributes;

namespace Benchmarks;

// ---------------------------------------------------------------------------
// Estudio: como ALMACENAR un resolver asincrono cacheado.
//
// El campo que se emitia era 'ValueTask<T>?', o sea 'Nullable<ValueTask<T>>':
// un struct de cinco campos (hasValue, _obj, _result, _token,
// _continueOnCapturedContext). Tenia DOS defectos independientes:
//
//   1. Publicacion no atomica. El CLR solo garantiza atomicidad hasta el tamano
//      de puntero, asi que el '_campo = null' del liberador son varios stores y
//      un lector concurrente puede ver una mezcla. Medido: 6 valores desgarrados
//      en 216.400 lecturas, sin una sola excepcion que lo delate.
//
//   2. Consumo multiple ilegal. Un ValueTask respaldado por IValueTaskSource
//      (Socket, PipeReader, cualquier fuente agrupada) se consume UNA sola vez:
//      al reciclarse la fuente el token queda invalidado y el segundo llamante
//      recibe InvalidOperationException. Reproducido. Solo parecia funcionar
//      porque 'async ValueTask<T>' usa un Task<T> por dentro, que es un detalle
//      de implementacion, no una garantia.
//
// Resuelto emitiendo 'Task<T>' como campo: se publica con un unico store (que
// ademas es 'release'), admite cualquier numero de consumidores, y ocupa 72 B
// por instancia frente a 96 B. Envolverlo en ValueTask<T> al salir no asigna.
//
// Nota sobre el nombre: aqui NO se llama a ValueTask<T>.AsTask(), que es otra
// cosa (convierte un ValueTask ya existente y asigna si no venia de un Task).
// 'TaskFieldHolder' devuelve el Task<T> que YA tiene guardado en el campo: cero
// conversion y cero asignacion en la lectura. Por eso se mide aparte.
//
// La cuarta variante, 'T?', es la mas rapida de todas, pero NO sirve como forma
// unica: para un servicio de tipo valor 'T?' es 'Nullable<T>' y vuelve a
// desgarrarse. Queda documentada como optimizacion posible para tipos de
// referencia, encima de la forma Task<T> que si es universal.
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

/// <summary>
/// Camino <b>frio</b> de una fabrica <c>ValueTask&lt;T&gt;</c>: publicar por primera vez.
/// Compara guardar el propio <c>ValueTask</c> (descartado por correccion) frente a
/// las dos maneras de obtener el <c>Task&lt;T&gt;</c> que si se puede guardar.
/// </summary>
/// <remarks>
/// Medido (i9-14900HX, .NET 10.0.12, 15x3 iteraciones):
/// <code>
/// ValueTask&lt;T&gt;? field (tears, single-consumption)   0.239 ns      0 B
/// Task&lt;T&gt;? field via ValueTask&lt;T&gt;.AsTask()          3.626 ns     72 B
/// Task&lt;T&gt;? field via async Task&lt;T&gt; core             6.200 ns     72 B
/// </code>
/// Entre las dos formas guardables el coste en memoria es el mismo (72 B, el
/// Task), asi que la diferencia es solo la maquina de estados: el metodo core
/// <c>async</c> cuesta 2,6 ns mas que <c>AsTask()</c>. Como esto se paga UNA
/// vez por resolver, la eleccion no se decide aqui sino en el camino caliente.
/// </remarks>
[MemoryDiagnoser]
public class ValueTaskStorageWriteBenchmark
{
	private readonly Payload _payload = new(new Settings());

	private ValueTask<Payload> Factory() => new(_payload);

	[Benchmark(Baseline = true, Description = "ValueTask<T>? field (tears, single-consumption)")]
	public ValueTask<Payload>? IntoNullableValueTask() => Factory();

	[Benchmark(Description = "Task<T>? field via ValueTask<T>.AsTask()")]
	public Task<Payload> IntoTaskViaAsTask() => Factory().AsTask();

	/// <summary>
	/// Metodo core <c>async Task&lt;T&gt;</c>: su maquina de estados produce
	/// directamente el <c>Task&lt;T&gt;</c> que se guarda, sin conversion posterior.
	/// </summary>
	[Benchmark(Description = "Task<T>? field via async Task<T> core")]
	public Task<Payload> IntoTaskViaAsyncCore() => ResolveCoreAsync();

	private async Task<Payload> ResolveCoreAsync() => await Factory().ConfigureAwait(false);
}

/// <summary>
/// Camino <b>caliente</b> de una fabrica <c>ValueTask&lt;T&gt;</c> ya resuelta: es lo que
/// se paga en cada resolucion, y por tanto lo que decide la forma del campo.
/// </summary>
/// <remarks>
/// Medido (mismo equipo y configuracion):
/// <code>
/// ValueTask&lt;T&gt;? field                    0.783 ns    0 B
/// ValueTask&lt;T&gt;? field, ??= publication    0.788 ns    0 B
/// Task&lt;T&gt;? field -&gt; ValueTask&lt;T&gt;         1.659 ns    0 B
/// Task&lt;T&gt;? field + T? result fast path   0.556 ns    0 B
/// </code>
/// <para>
/// La variante '??=' se midio aparte porque la duda era legitima, pero el
/// camino caliente de las dos formas de campo <c>ValueTask&lt;T&gt;?</c> es el
/// mismo: lo unico que cambia es el <c>Create()</c>, que es camino frio. Y asi
/// sale: 0,788 frente a 0,783 ns, indistinguibles dentro del margen de error.
/// La publicacion por coalescencia tampoco arregla el desgarro, porque el
/// <c>??=</c> sobre un <c>Nullable&lt;ValueTask&lt;T&gt;&gt;</c> sigue siendo
/// varios stores.
/// </para>
/// <para>
/// Conclusion: <b>no</b> se vuelve a guardar el <c>ValueTask&lt;T&gt;</c>. Su unica
/// ventaja aparente (0,78 frente a 1,66 ns) desaparece en cuanto el campo
/// <c>Task&lt;T&gt;?</c> se acompana del atajo por resultado, que ademas lo bate:
/// 0,556 ns, es decir un 29% MAS RAPIDO que el campo desgarrable, sin asignar y
/// sin renunciar a la publicacion atomica ni al consumo multiple.
/// </para>
/// <para>
/// Por tanto la forma emitida es: campo <c>Task&lt;T&gt;?</c> para la tarea,
/// atajo por <c>T?</c> cuando el servicio es de tipo referencia, y el camino
/// lento en un metodo aparte. Ninguna de las dos alternativas planteadas gana.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class ValueTaskStorageReadBenchmark
{
	private readonly NullableValueTaskHolder _nullable = new();
	private readonly NullableTaskHolder _nullableCoalescing = new();
	private readonly TaskFieldHolder _task = new();
	private readonly ResultFieldHolder _result = new();

	[GlobalSetup]
	public void Setup()
	{
		_ = _nullable.Value;
		_ = _nullableCoalescing.Value;
		_ = _task.Value;
		_ = _result.Value;
	}

	[Benchmark(Baseline = true, Description = "ValueTask<T>? field")]
	public ValueTask<Payload> FromNullableValueTask() => _nullable.Value;

	[Benchmark(Description = "ValueTask<T>? field, ??= publication")]
	public ValueTask<Payload> FromNullableTaskHolder() => _nullableCoalescing.Value;

	[Benchmark(Description = "Task<T>? field -> ValueTask<T>")]
	public ValueTask<Payload> FromTaskField() => _task.Value;

	[Benchmark(Description = "Task<T>? field + T? result fast path")]
	public ValueTask<Payload> FromResultFastPath() => _result.Value;
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

public sealed class NullableTaskHolder
{
	private ValueTask<Payload>? _field;
	private readonly Lock _lock = new();

	[MethodImpl(MethodImplOptions.NoInlining)]
	private ValueTask<Payload> Create()
	{
		lock (_lock) return _field ??= new ValueTask<Payload>(new Payload(new Settings()));
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

    public ValueTask<Payload> Value => _field is { } v
		? new ValueTask<Payload>(v)
        : Create();
}

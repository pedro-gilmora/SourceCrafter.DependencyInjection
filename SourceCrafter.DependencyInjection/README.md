# SourceCrafter.DependencyInjection - Fastest & Truly Compile-Time Dependency Injection

## Overview

**SourceCrafter.DependencyInjection** is a high-performance, compile-time dependency injection framework that eliminates the overhead of runtime reflection. Declare your services once with attributes, and the generator creates optimized, type-safe resolver code at compile time.

### Why Compile-Time DI?
- **Zero Runtime Reflection**: All service resolution is compiled into direct method calls
- **Performance**: No dictionary lookups, no expression trees, no dynamic instantiation
- **Compile-Time Safety**: Type mismatches and missing dependencies caught before runtime
- **Transparent**: Generated code is readable and debuggable

### Core Features
- **Attribute-Based Registration**: Mark services with `[Singleton]`, `[Scoped]`, `[Transient]` on your container
- **Multiple Lifetimes**: `Singleton` (application-wide), `Scoped` (per container instance), `Transient` (always new)
- **Factory Support**: Use static methods, properties, or fields to provide instances
- **Smart Async Handling**: `Task<T>` and `ValueTask<T>` factories are automatically cached; no redundant executions
- **Intelligent Disposal**: Container automatically implements `IAsyncDisposable` or `IDisposable` based on dependencies
- **Flexible Configuration**: JSON settings, custom namespaces, and advanced caching strategies
- **Scoped Isolation**: Create isolated scopes for request lifecycles with built-in disposal tracking
---

---

## Installation

### Core Package
Install the compile-time generator:
```bash
dotnet add package SourceCrafter.DependencyInjection
```

### With Microsoft Configuration Support
For JSON settings (`[JsonSetting<T>]`) and MS.Extensions integration, use the
configuration-aware generator **instead of** the core one:
```bash
dotnet add package SourceCrafter.DependencyInjection.MsConfiguration
```

---

## Quick Start

### Step 1: Create Your Container
Define a `partial` class with `[ServiceContainer]` and register services:

```csharp
[ServiceContainer]
[Singleton<IDatabase, Database>]
[Scoped<IAuthService, AuthService>]
[Scoped<EmployeeController>]
public partial class ServiceContainer : IServiceProvider
{
    public object? GetService(Type serviceType) 
        => throw new NotImplementedException();
}
```

### Step 2: Use Your Services
The generator creates strongly-typed resolver methods on your container:

```csharp
var container = new ServiceContainer();
var controller = await container.GetEmployeeControllerAsync();
var scope = container.CreateScope();  // for scoped services
```

---

## Detailed Example

### 1. Annotating the `Server` container class

```csharp
namespace SourceCrafter.DependencyInjection.Tests
{
    [ServiceContainer]
    [JsonSetting<AppSettings>("AppSettings")]
    [Scoped("count", source: nameof(CountAsync))]
    [Scoped("reqId", source: nameof(ResolveRequestIdTask))]
    [Singleton<IDatabase, Database>]
    [Scoped<IAuthService, AuthService>]
    [Scoped<EmployeeController>]
    public partial class Server : IServiceProvider
    {
        static Task<int> CountAsync() => Task.FromResult(1);

        public object? GetService(Type serviceType)
        {
            throw new NotImplementedException();
        }

        static ValueTask<Guid> ResolveRequestIdTask => new(Guid.NewGuid());
    }
}
```

### 2. Service Definitions

#### `AuthService`

This service is scoped, meaning it is created once per request.

```csharp
public class AuthService(IDatabase application, int count) : IAuthService
{
    public IDatabase Database { get; } = application;

    public ValueTask DisposeAsync()
    {
        return default;
    }

    public void Dispose()
    {

    }
}


public interface IAuthService : IAsyncDisposable
{
    IDatabase Database { get; }
}
```

#### `Database`

This is a singleton service that depends on `AppSettings` and a connection string. It implements `IDatabase` and uses `IAsyncDisposable` for asynchronous cleanup.

```csharp
public class Database(AppSettings settings, string connection) : IDatabase, IAsyncDisposable
{
    public void TrySave(out string setting1)
    {
        setting1 = settings?.Setting1 ?? "Value3";
    }

    public ValueTask DisposeAsync()
    {
        return default;
    }
}
```

#### `EmployeeController`

This is a singleton service that depends on `AppSettings` and a connection string. It implements `IDatabase` and uses `IAsyncDisposable` for asynchronous cleanup.

```csharp
public class EmployeeController(IAuthService authService, IDatabase application, int count, Guid reqId);
```

### 3. Configuration and Settings

#### `AppSettings`

A simple class for application settings, loaded via `[JsonSetting<AppSettings>("AppSettings")]`.

```csharp
public class AppSettings
{
    public string Setting1 { get; set; }
    public string Setting2 { get; set; }
}
```

### 4. Attribute Reference

#### Lifetime attributes' anatomy:

- V1: [Lifetime()]


| Attribute | Lifetime | Caching | Use Case |
|-----------|----------|---------|----------|
| `[Singleton(typeof(T), typeof(TImpl)?)]` <br/>or `[Singleton<T>]` <br/>or `[Singleton<T, TImpl>]` | Application | Static | Stateless services, expensive resources |
| `[Scoped(typeof(T), typeof(TImpl)?)]` <br/>or `[Scoped<T>]` <br/>or `[Scoped<T, TImpl>]` | Per instance | Instance | Context-local services, repositories |
| `[Transient(typeof(T), typeof(TImpl)?)]` <br/>or `[Transient<T>]` <br/>or `[Transient<T, TImpl>]` | Per request | None | Stateful objects, value types |
| `[JsonSetting<T>(section)]` | Config | Static | Maps a setting section loaded from `appsettings.json` |
| `[Scoped(name, source: Method)]` | Per instance | Instance | Named factory-produced services |

### Key Behaviors

**Factory Method Caching**: Any `Task<T>` or `ValueTask<T>` used as a factory is cached even in transient scenarios to prevent redundant async work.

**Smart Disposal**: The container automatically detects if any registered service is `IAsyncDisposable` and generates async disposal code. If all disposables are synchronous, a sync `Dispose()` is generated instead.

**Scoped Isolation**: Call `container.CreateScope()` to create an isolated scope instance. Scoped services registered in that scope are cached independently; disposal doesn't affect the parent.

---

## Advanced Topics

### Disposability Control

Specify custom disposal behavior with the `Disposability` parameter:
```csharp
[Singleton<IResource, Resource>(Disposability.AsyncDispose)]
```

Options:
- `None`: No disposal logic generated
- `Dispose`: Synchronous disposal (`IDisposable`)
- `AsyncDispose`: Asynchronous disposal (`IAsyncDisposable`)

### Dependency Graphs

The generator analyzes your dependency tree at compile time:

```csharp
[Singleton<IDatabase, Database>]           // Needs AppSettings
[Scoped<IAuthService, AuthService>]         // Needs IDatabase, int count
[Scoped<EmployeeController>]                // Needs IAuthService, IDatabase
```

Generated code automatically:
- Resolves transitive dependencies
- Parallelizes independent Task/ValueTask resolutions
- Caches results appropriately
- Handles mixed sync/async dependencies

### Async Factories

Use `Task<T>` or `ValueTask<T>` factory methods. They're cached automatically:

```csharp
[Scoped(source: nameof(GetUserAsync))]
static Task<User> GetUserAsync() => /* ... */;
```

The container calls this once per scope, caches the result, and reuses it for all dependents.

### How async dependencies are composed

Independent async dependencies are **started first, then awaited**, so they progress
concurrently:

```csharp
var __v0 = GetDatabaseAsync();
var __v1 = GetCountAsyncCached();

return _authServiceTask = __v0.IsCompletedSuccessfully && __v1.IsCompletedSuccessfully
    ? Task.FromResult(new AuthService(__v0.Result, __v1.Result))   // fast path, no state machine
    : ResolveCoreAsync();

async Task<IAuthService> ResolveCoreAsync()
    => new AuthService(
        await __v0.ConfigureAwait(false),
        await __v1.ConfigureAwait(false));
```

Two decisions here are worth spelling out, because the obvious alternatives are worse:

**No `Task.WhenAll`.** It looks like the idiomatic way to await several tasks, but it buys
nothing once the tasks are already started — awaiting them one by one does not serialize
them. It only adds work: the `Task[]` for the `params` argument plus the promise that
tracks them. Measured on the hardened harness (3 launches, memory randomization):

| shape | time | allocated |
|---|---:|---:|
| `new X(await v0, v1.Result)` | 11.6 ns | 96 B |
| `new X(await v0, await v1)` | 23.2 ns | 96 B |
| `await WhenAll(v0, v1)` + `.Result` | 44.1 ns | 256 B |

**Dependencies already resolved by an earlier parameter only read `.Result`.** When a
parameter's subtree has already completed another parameter's task, awaiting it again would
cost a second state machine transition for a value that is already there:

```csharp
return new EmployeeController(
    await __v0.ConfigureAwait(false),
    __v1.Result /* resolved previously by param 0 */,
    __v2.Result /* resolved previously by param 0 */);
```

The trade-off of not using `WhenAll`: if two dependencies fault at once, the first `await`
throws and the second task's exception is never observed, which raises
`TaskScheduler.UnobservedTaskException`. On .NET Core that does not tear down the process,
and paying ~2x on every successful resolution to avoid it was not worth it.

Interceptors that return several services apply the same idea across array elements. All
tasks are started first; then only the elements that no other element already resolves are
awaited, and the rest read `.Result`:

```csharp
var __t0 = provider.GetAlphaAsyncCached;
var __t1 = provider.GetBetaAsync();
var __t2 = provider.GetGammaAsync();
var __r0 = await __t2;

return [
    __t0.Result /* resolved previously by __t2 */,
    __t1.Result /* resolved previously by __t2 */,
    __r0];
```

Only *cached* elements (singleton or scoped) qualify: a transient hands out a fresh task on
every call, so the one another element awaited internally is not the one this array holds.
Reading `.Result` never degrades an exception into an `AggregateException` either, because
the covering element depends on the covered one — if the covered task faults, so does the
covering one, and its `await` throws first.

### Generic `IServiceProvider` API

By default the container exposes only the strongly-typed resolvers it generated. Opt into
the generic, MEDI-shaped surface (`GetService<T>()`, `GetRequiredService<T>()`, keyed
overloads and their async counterparts) with a constructor argument:

```csharp
[ServiceContainer(generateServiceProviderApi: true)]
public partial class AppContainer { }
```

It is opt-in because those methods only make sense when you actually need to hand the
container to code written against `IServiceProvider`; without it, every unresolvable call
is a compile error instead of a runtime one.

The plural, un-keyed overload (`GetRequiredServices<T>()` and its async form) is declared
even when *every* registration of that type carries a key, and it returns all of them. A
call site that asks for "every service of this type" is not asking about keys, and the
interception already collects the keyed resolvers for it.

### IServiceProvider-like Interception

Generated extension methods intercept `IServiceProvider`-like calls:

```csharp
// Your code
await provider.GetRequiredService<Task<MyService>>();

// Intercepted to optimized generated code
await provider.GetMyServiceAsync();
```

---

## Architecture & Performance

### Generated Code Structure

The generator creates:

1. **Cached Properties/Fields** (with lock guards):
   - Singleton services: `static` fields guarded by a `static readonly` lock
   - Scoped services: instance fields guarded by an instance lock created on first use

   The lock always matches the scope of the field it guards, so lazy initialization is
   genuinely thread-safe even across several container instances. Instance locks are
   allocated lazily, so opening a scope costs nothing for services it never resolves.

2. **Resolver Methods**:
   - Direct instantiation for transients
   - Cached lookup + return for singletons/scoped
   - Async-aware parallel resolution for Task-based dependencies

3. **Disposal Routing**:
   - Automatic tracking of disposable dependencies
   - Proper disposal order in `DisposeAsync()` / `Dispose()`

### Benchmarks

Measured against every actively maintained compile-time DI container, following the
scenarios of [.NET Matrix](https://github.com/DevTeam/dotnet-matrix), with hand-written
`new(...)` as the baseline. `net10.0`, Intel i9-14900HX, BenchmarkDotNet 0.15.8. Reproduce
with `dotnet run -c Release --project Benchmarks -- --filter "*"`.

**Read the allocation table first, and treat the timing table as approximate.** That is not
modesty, it is what repeated measurement showed: see [How much to trust
these numbers](#how-much-to-trust-these-numbers) below.

#### Allocations per operation

These are exact, and they reproduced byte-for-byte across every harness configuration tried.

| Scenario | Hand Coded | **SourceCrafter** | Jab | Pure.DI | StrongInject | MrMeeseeks.DIE |
|---|---|---|---|---|---|---|
| Resolve singleton | 0 B | **0 B** | 0 B | 0 B | 0 B | 136 B |
| Resolve transient | 80 B | **80 B** | 80 B | 80 B | 80 B | 168 B |
| Complex graph (15 nodes) | 424 B | **424 B** | 424 B | 424 B | 424 B | 560 B |
| Create scope, resolve, dispose | 24 B | **64 B** | 64 B | 152 B | n/a | n/a |
| Create scope, resolve nothing | 24 B | **40 B** | 40 B | 128 B | n/a | n/a |
| Create container + resolve | 48 B | **48 B** | 88 B | 216 B | 184 B | 1064 B |

Building object graphs — which is what a container spends its life doing — allocates exactly
what the equivalent hand-written `new(...)` allocates. The overhead is confined to scopes.

#### Timing

Ratio against `Hand Coded`; lower is better. Harness: 3 process launches, 10 warmup and 15
measured iterations each, with memory randomization enabled.

| Scenario | **SourceCrafter** | Jab | Pure.DI |
|---|---|---|---|
| Resolve singleton | **~1.5x** | ~2.0x | ~1.0x |
| Resolve transient | **0.96x** | 0.99x | 0.98x |
| Complex graph (15 nodes) | **1.01x** | 0.99x | 1.03x |
| Create scope, resolve, dispose | **tie with Jab** | tie | 4.2x slower |
| Create scope, resolve nothing | **1.51x** | 1.38x | 3.16x |
| Create container + resolve | **0.58x** | 2.79x | 6.55x |

The scope rows read "tie" on purpose: this container and Jab measured 13.47 ns and 13.37 ns,
a 0.7% gap against a `RatioSD` two orders of magnitude larger. Reporting that as a win in
either direction would be reporting noise. The singleton row is approximate for the same
reason — at half a nanosecond the measurement is dominated by whatever the JIT decided that
process, and BenchmarkDotNet itself declines to compute a ratio (it prints `?`).

Scope allocations used to be 112 B here against Jab's 64 B. The gap was one 40 B
`System.Threading.Lock` per cached dependency. Sharing a single lock per lifetime — and
hoisting every lock-acquiring dependency out of the guarded region so that sharing stays
deadlock-free — closed it exactly, without adopting the deadlock described below.

StrongInject and MrMeeseeks.DIE have no request scope: the former models lifetime through
ownership (`Owned<T>`), the latter through creation-function-bound transient scopes. Inventing
an equivalent would produce a number no user of theirs could reproduce. StrongInject is also
doing strictly more work in every row, since `Run`/`Resolve` track ownership for deterministic
disposal. Their timings are omitted above because they were not re-measured under the hardened
harness; MrMeeseeks.DIE in particular is too noisy to summarize with a single ratio.

#### How much to trust these numbers

Less than a table of three-significant-digit ratios suggests. Earlier revisions of this file
published figures from a short-job run; re-measuring contradicted several of them, and
investigating the contradiction is what produced the harness described above.

Two findings are worth stating plainly, because they bound what any DI benchmark can claim:

- **The complex-graph scenario measured 0.95x, 1.01x, 1.02x and 2.10x for this container in
  four runs of the same binary, with identical 424 B allocations every time.** Allocation
  counts are a property of the code; sub-100 ns timings on this hardware are partly a property
  of the process that happened to run them.
- **In one single-launch run, all three containers jumped from ~38 ns to ~80 ns while the
  hand-coded baseline did not move.** Three independent libraries do not regress in lockstep
  to the same value; that was an artifact, and it is the reason the harness now averages
  across processes.

The rule this implies: **a difference whose `RatioSD` is comparable to the `Ratio` is not a
difference.** Anything within roughly ±10% in the timing table above should be read as a tie.

#### Thread-safety schemes

Read from each library's generated output, not from documentation. This is what the
allocation differences in the table above actually buy:

| | Lock granularity | Consequence |
|---|---|---|
| **SourceCrafter** | One per lifetime — `lock(this)` for scoped, one static lock for singletons — with every lock-acquiring dependency hoisted out of the guarded region | Free to allocate *and* deadlock-free: a resolver never holds one lock while acquiring another, so the wait-for graph has no edges |
| Jab | `lock(this)` on the root and on each scope | Free, but it is one-lock-per-lifetime *without* hoisting: a singleton depending on a scoped service and another thread resolving in the opposite order close a wait cycle |
| Pure.DI | One `Lock` per composition, *inherited* by child scopes | Correct — `lock` is reentrant — but every construction in the whole tree serializes on it |
| StrongInject | One `SemaphoreSlim` per container | Correct; a semaphore is heavier than a monitor and is not reentrant |
| MrMeeseeks.DIE | `SemaphoreSlim` plus `Interlocked` resolution counters | Explains the 136 B and 144 ns to hand back an already-built singleton |

Pure.DI independently makes the same `System.Threading.Lock`-when-available choice this
generator does, which is a good sign that the trade-off is the right one.

Hoisting is what separates row one from row two. Sharing a lock across a lifetime is only
safe if nothing inside the critical section can block on another lock, so the slow path
resolves its cached dependencies — and any transient whose subtree contains one — into locals
*before* taking the lock:

```csharp
private Session __Create_session()
{
    var __a0 = Config;            // acquires the singleton lock, then releases it
    var __a1 = new Wrapper(Clock); // transient, but Clock is cached — also hoisted

    lock(this)                     // nothing inside can acquire anything
    {
        return _session ??= new Session(__a0, __a1);
    }
}
```

`LockingStrategyTests` contains a test that reproduces the deadlock this avoids, and
`GeneratedCodeTests.CachedDependenciesAreResolvedBeforeTakingTheLock` asserts on the emitted
text that nothing which acquires a lock is left inside the guarded region.

The trade-offs this accepts, stated plainly: `lock(this)` leaves the lock publicly acquirable
by anyone holding the scope, and if two threads race into the same slow path they both hoist,
so a hoisted *transient* may be built twice and one copy discarded. That can only happen on
the very first resolution of a service, and transients are not tracked for disposal.

One difference in the table is not a lock-scheme decision:

- **`Create container + resolve` is not the same semantics across libraries.** Here
  singletons are `static`, so a fresh container reuses the one already built.

The fast path of a cached resolver reads the backing field into a local, and falls through to
a `[MethodImpl(NoInlining)]` slow path. Three measurements justify that shape, taken on
hand-written variants of the same already-built service (`ResolverShapeBenchmark`):

- **Having a fast path at all is worth 11x**: entering the lock on every resolve costs
  8.83 ns against 0.56 ns.
- **Moving the lock out of the getter is worth a further ~32%**: 0.83 ns with the `lock`
  inline versus 0.56 ns with it split out (ratio 0.68, RatioSD 0.04). A getter containing a
  `lock` is too big for the JIT to inline into the caller; a getter that is just a field
  read and a call is not.
- **The local costs nothing**: 0.5607 ns versus 0.5716 ns for reading the field twice — and it
  cuts the standard deviation from 1.067 to 0.018, because the JIT no longer gets to choose
  whether to rematerialize the second read. It also removes a real bug for value types, where
  two reads of a `Nullable<T>` can return one read's `HasValue` with another's `Value`.

The slow path is only ever entered on the first resolution, so it costs nothing per call
afterwards.

---

## Generated Code Example

For this container:

```csharp
[ServiceContainer]
[Singleton<IClock, SystemClock>]
[Scoped<DbSession>]
[Transient<Handler>]
public partial class AppContainer { }
```

the generator emits the following — this is real, verbatim output:

```csharp
// <auto-generated/>
#nullable enable
namespace MyApp;

[global::System.CodeDom.Compiler.GeneratedCode("SourceCrafter.DependencyInjection", "<version>")]
public partial class AppContainer : global::System.IAsyncDisposable
{
	public const string EnvironmentVariableName = "DOTNET_ENVIRONMENT";

	public static string EnvironmentName { get; } = global::System.Environment.GetEnvironmentVariable(EnvironmentVariableName) ?? "Development";

	private bool _disposed = false;

	private static readonly global::System.Threading.Lock __singletonLock = new();

	private static global::MyApp.SystemClock? _systemClock;
	[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	private global::MyApp.SystemClock __Create_systemClock()
	{
		lock(__singletonLock)
		{
			return _systemClock ??= new global::MyApp.SystemClock();
		}
	}

	public global::MyApp.IClock SystemClock
	{
		get
		{
			var __v = _systemClock;

			if(__v is not null) return __v;

			return __Create_systemClock();
		}
	}

	private global::MyApp.DbSession? _dbSession;
	[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	private global::MyApp.DbSession __Create_dbSession()
	{
		var __a0 = SystemClock;

		lock(this)
		{
			return _dbSession ??= new global::MyApp.DbSession(
				__a0);
		}
	}

	public global::MyApp.DbSession DbSession
	{
		get
		{
			var __v = _dbSession;

			if(__v is not null) return __v;

			return __Create_dbSession();
		}
	}

	public global::MyApp.Handler Handler
		=> new global::MyApp.Handler(
				DbSession);

	private AppContainer _root = default!;

	public virtual AppContainer Root => this;

	public virtual Scoped CreateScope() => new() { _root = this };

	public class Scoped : AppContainer
	{
		public override AppContainer Root => _root;

		public override Scoped CreateScope() => new() { _root = _root };

		public override global::System.Threading.Tasks.ValueTask DisposeAsync()
		{
			if (_disposed) return default;

			_disposed = true;

			return ScopedDisposeAsync();
		}
	}

	private global::System.Threading.Tasks.ValueTask ScopedDisposeAsync()
	{
		{
			var __disposing = _dbSession;
			_dbSession = null;
			return __disposing?.DisposeAsync() ?? default;
		}
	}

	public virtual global::System.Threading.Tasks.ValueTask DisposeAsync()
	{
		if (_disposed) return default;

		_disposed = true;

		return ScopedDisposeAsync();
	}

}
```

Several things in that output are deliberate and worth calling out:

- **Each lock has the same scope as the field it protects.** `_systemClock` is `static`, so
  its lock is the `static` `__singletonLock`. A `lock(this)` guarding a `static` field would
  give no mutual exclusion at all — two container instances would happily build the same
  singleton twice.
- **One lock per lifetime, made safe by hoisting.** `_dbSession` is per-instance, so the
  scope itself serves as its lock: `lock(this)` allocates nothing. On its own that is the
  scheme that deadlocks — a singleton may depend on a scoped service and vice versa, so one
  thread takes the singleton lock then wants the scope while another does the exact opposite.
  What removes the cycle is the line above the lock: `var __a0 = SystemClock;`. Every
  dependency that acquires a lock — cached ones, and transients whose subtree contains one —
  is resolved into a local *before* the lock is taken, so a resolver never holds one lock
  while acquiring another. The alternative, a lock per cached dependency, is also correct but
  costs 40 bytes per resolved service; measured over a scope's create-resolve-dispose cycle,
  hoisting cut 192 B to 96 B and 43.3 ns to 30.6 ns. `LockingStrategyTests` demonstrates each
  scheme in isolation, including the deadlock that hoisting avoids.
- **Locks are typed `System.Threading.Lock` when the target project can use it** (C# 13 and
  a runtime that ships the type), falling back to `object` otherwise. The dedicated type
  avoids the object header's sync block and lets the compiler emit `EnterScope` instead of
  `Monitor`. Emitting it under C# 12 would be counterproductive: the compiler would convert
  it back to `object`, fall back to `Monitor` anyway, and warn (CS9216).
- **Cached *asynchronous* resolvers keep a lazily created lock per dependency.** Their bodies
  still start tasks inside the guarded region, so they do not satisfy the hoisting condition.
  Mixing the two schemes is safe precisely because the synchronous side hoists: whoever holds
  `this` is not waiting on anything else, so no cycle can close.
- **The cached fast path reads the field into a local; the `lock` lives in a separate
  `[MethodImpl(NoInlining)]` method.** With the `lock` inside the getter, the getter carries a
  protected region and the JIT stops inlining it — 0.83 ns against 0.56 ns once split. The
  local is free and removes a real bug: two separate reads of a `Nullable<T>` backing field
  can return one read's `HasValue` with another's `Value`. That slow-path method is never
  `static`, even for singletons: building a singleton may touch instance resolvers.
- **A constructor is emitted only when a resolver consumes the lifetime cancellation token**,
  which is the only remaining thing that needs initializing. Otherwise the parameterless
  constructor stays yours.
- **`_disposed` is initialized at its declaration**, not in the constructor.
- **`Root` and `CreateScope()` are `virtual`/`override`.** Resolver bodies are declared
  once on the container and inherited by `Scoped`. Without virtual dispatch, a `[Root]`
  parameter injected from a scope would bind at compile time to `Root => this` and
  receive the scope instead of the root.
- **Disposers null the field before releasing it**, and run in reverse construction order.
  Transients are never tracked — you own their lifetime.

---

## Cancellation

Factory methods may take a `CancellationToken`:

```csharp
[Singleton("count", source: nameof(LoadAsync))]
static Task<int> LoadAsync(CancellationToken token) => /* ... */;
```

The generator never forwards the *caller's* token. A cached value is handed to every
later caller, so recording the first caller's token inside it would be wrong. Instead the
container owns a single `CancellationTokenSource`, copies its token once into a `readonly`
field, and cancels + disposes the source when the container is disposed:

```csharp
private readonly global::System.Threading.CancellationTokenSource __lifetimeCts;
private readonly global::System.Threading.CancellationToken __lifetimeToken;
```

The token is copied into a field rather than read from `__lifetimeCts.Token` on every
access because `CancellationTokenSource.Token` throws `ObjectDisposedException` once the
source is disposed — which is exactly what disposal does, while resolvers may still be
invoked. A `CancellationToken` is a struct wrapping a reference, so the copy allocates
nothing.

Each scope is its own container instance, so each scope gets its own source: cancellation
is per-scope for free. A container that uses the token becomes disposable even when none
of its services are, because the source itself has to be released.

Because of that, **no generated member accepts a `CancellationToken`** — not the resolvers,
not the interceptors, not the generic `GetRequired*Async<T>()` surface. A parameter that is
silently ignored is worse than no parameter at all, so it is not emitted:

```csharp
// emitted
public Task<TOut> GetRequiredServiceAsync<TOut>() where TOut : notnull => ...;

// not emitted
public Task<TOut> GetRequiredServiceAsync<TOut>(CancellationToken token = default) ...
```

---

## Use Cases

- **ASP.NET Core apps** — compile-time safe resolution on every request
- **Microservices** — minimal overhead, predictable performance
- **High-frequency APIs** — no runtime cache lookups
- **Console apps** — transparent, zero-configuration DI
- **Testing** — type-safe mocking and scope isolation

---

## Troubleshooting

All generator diagnostics use the `SCDI` prefix:

| Id | Meaning |
|----|---------|
| `SCDI01` | Duplicate service registration for the same lifetime/type/key |
| `SCDI02` | Duplicate key across registrations |
| `SCDI03` | Factory member not found on the container |
| `SCDI04` | An async factory should accept a `CancellationToken` |
| `SCDI05` | A service key must be a `string` or an `enum` |
| `SCDI06` | An interface registration requires a factory or an implementation type |
| `SCDI07` | The container class must be declared `partial` |
| `SCDI08` | Naming/registration conflict with an existing member |
| `SCDI09` | The implementation does not derive from the declared service type |
| `SCDI10` | The factory return type does not match the registered service type |
| `SCDI11` | No resolver covers this `GetService`/`GetRequiredService` call |
| `SCDI12` | Invalid inner factory specification |
| `SCDI13` | More than one container claims this call site |
| `SCDI14` | Invalid async type argument on a resolver |
| `SCDI15` | The container already declares a parameterless constructor |

Frequent situations:

**"Resolver not generated for type X"** — check that `X` is registered with
`[Singleton<X>]`, `[Scoped<X>]` or `[Transient<X>]`, and that the container class carries
`[ServiceContainer]` and is `partial`.

**`SCDI15`** — the generator owns the parameterless constructor whenever a resolver consumes
the lifetime cancellation token, since that is where the cancellation source is initialized.
Remove yours, or move its body into a method you call explicitly. Containers that do not use
the token leave the parameterless constructor free for you.

**Container won't dispose services** — call `Dispose()` / `await DisposeAsync()`, and check
that the services actually implement `IDisposable` / `IAsyncDisposable`.

## Resources

- **GitHub Repository**: [pedro-gilmora/SourceCrafter.DependencyInjection](https://github.com/pedro-gilmora/SourceCrafter.DependencyInjection)
- **NuGet Packages**:
  - Core: `SourceCrafter.DependencyInjection`
  - MS Configuration: `SourceCrafter.DependencyInjection.MsConfiguration`
- **License**: See repository for details
---

## See also
- **Core Metadata**: [SourceCrafter.DependencyInjection.Metadata](https://www.nuget.org/packages/SourceCrafter.DependencyInjection.Metadata#readme-body-tab)
- **Core Configuration**: [SourceCrafter.DependencyInjection.MsConfiguration](https://www.nuget.org/packages/SourceCrafter.DependencyInjection.MsConfiguration#readme-body-tab)
- **Configuration Metadata**: [SourceCrafter.DependencyInjection.MsConfiguration.Metadata](https://www.nuget.org/packages/SourceCrafter.DependencyInjection.MsConfiguration.Metadata#readme-body-tab)
---

**SourceCrafter.DependencyInjection** makes compile-time DI seamless, fast, and transparent—zero magic, 100% predictable.




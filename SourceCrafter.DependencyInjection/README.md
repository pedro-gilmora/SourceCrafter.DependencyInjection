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

> Looking for the reasoning, the measurements or the anatomy of the generated code?
> That lives in **[INTERNALS.md](INTERNALS.md)**. This README stays on design guidelines
> and public surface.

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
Define a `partial` class with `[ServiceProvider]` and register services:

```csharp
[ServiceProvider]
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
    [ServiceProvider]
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

#### `[ServiceProvider]` options

| Option | Default | Effect |
|--------|---------|--------|
| `envName` | `"DOTNET_ENVIRONMENT"` | Environment variable backing `EnvironmentName` |
| `genericApi` | `false` | Emit the generic, MEDI-shaped surface (`GetRequiredService<T>()` and friends) |
| `exportTransients` | `false` | Give dependency-less transients a named member, so other assemblies can resolve them |

#### How generated members are named

Every registration becomes a member on the container. The generator takes the shortest name
that is still free and only adds a distinction when it has to:

| | Tried in order |
|---|---|
| No key | `{type}`, `{lifetime}{type}` |
| With key | `{type}`, `{type}{key}`, `{lifetime}{key}`, `{lifetime}{key}{type}` |
| Still taken | the same name with `1`, `2`, … appended |

So three registrations of `IService` keyed `"svc"` but implemented by `Alpha`, `Beta` and
`Gamma` become `Alpha`, `Beta` and `Gamma`, while two registrations of the same `Db` keyed
`"read"` and `"write"` become `Db` and `DbWrite`.

`source:` takes the name from your factory method, minus the leading underscore and minus a
`Get` prefix when the member is a property rather than a method.

To choose the name yourself, use `nameFormat`. It accepts `{lifetime}`, `{key}` and `{type}`,
plus the older `{0}`, which is the key:

```csharp
[Scoped<Thing>("main", nameFormat: "{lifetime}{key}{type}")]  // ScopedMainThing
[Scoped<Other>("aux",  nameFormat: "The{type}For{key}")]      // TheOtherForAux
```

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

#### Concurrent composition and invariance

Independent async dependencies are started first and awaited afterwards, so they progress
concurrently. A resolution whose dependencies are already complete returns without building
a state machine at all. The generated shapes, and the measurements behind them, are in
[INTERNALS.md](INTERNALS.md#how-async-dependencies-are-composed).

An async factory must declare **exactly** the exposed service type: `Task<T>` is invariant,
so `Task<Impl>` is not a `Task<IService>`. The generator reports `SCDI16` instead of
awaiting and re-wrapping, which would cost an allocation on every resolution.

### Generic `IServiceProvider` API

By default the container exposes only the strongly-typed resolvers it generated. Opt into
the generic, MEDI-shaped surface (`GetService<T>()`, `GetRequiredService<T>()`, keyed
overloads and their async counterparts) with a constructor argument:

```csharp
[ServiceProvider(genericApi: true)]
public partial class AppContainer { }
```

It is opt-in because those methods only make sense when you actually need to hand the
container to code written against `IServiceProvider`; without it, every unresolvable call
is a compile error instead of a runtime one.

These members are the **fallback of interception**. An interceptor rewrites the call site,
so it resolves with no comparison at all — but it can only do that when the compiler binds
the concrete container there. When it cannot (the container arrives through an
`IServiceProvider`-typed variable, or the call lives in another assembly) the call survives
and lands here, where it is dispatched by runtime type:

```csharp
public TOut GetRequiredService<TOut>() where TOut : notnull
{
    switch (typeof(TOut).FullName)
    {
        case "MyApp.IClock":
            return (TOut)(object)Clock;
    }

    throw new InvalidOperationException($"No service of type '{typeof(TOut)}' is registered.");
}
```

Keyed members discriminate type and key in a single comparison, over
`$"{typeof(TOut).FullName}|{key}"`. Constructed generics are the exception: their runtime
`FullName` embeds the assembly-qualified name of every type argument, so it cannot be
emitted as a `case` label and they are matched with `typeof(TOut) == typeof(X)` instead.

When a type has several registrations, the **singular** members return the last one
registered — the export type is the only criterion, whether it is an interface, an abstract
class or a concrete one. The **plural** members return every registration of that type.
`GetService<T>()` resolves exactly what `GetRequiredService<T>()` does but returns `null`
instead of throwing, and neither of them resolves keyed or async registrations.

The plural, un-keyed overload (`GetRequiredServices<T>()` and its async form) is declared
even when *every* registration of that type carries a key, and it returns all of them. A
call site that asks for "every service of this type" is not asking about keys, and the
interception already collects the keyed resolvers for it.

### Exporting dependency-less transients

A transient with no dependencies is built straight at the call site (`new Leaf()`), so by
default it gets no named member on the container. That is the fastest shape, but it makes the
service unreachable from *another assembly*: interception is per-compilation, so a consumer's
call sites are never rewritten, and with no member to forward to, the generic API cannot
dispatch to it either.

Opt in when your container is part of a library's public surface:

```csharp
[ServiceProvider(exportTransients: true)]
[Transient<Leaf>]
public partial class AppContainer { }
```

Consumers can then use `container.Leaf`. Inlining is unaffected: inside the declaring
compilation the call site still builds the instance in place.

### IServiceProvider-like Interception

Generated extension methods intercept `IServiceProvider`-like calls:

```csharp
// Your code
await provider.GetRequiredService<Task<MyService>>();

// Intercepted to optimized generated code
await provider.GetMyServiceAsync();
```

#### What can be a receiver

Interception is decided by the **type** of the receiving expression, not by its syntactic
shape. Anything whose result type is the container works:

```csharp
new AppContainer().GetRequiredService<IGreeter>();      // construction
AppContainer.Create().GetRequiredService<IGreeter>();   // method result
holder.Container.GetRequiredService<IGreeter>();        // property chain
containers[0].GetRequiredService<IGreeter>();           // indexed element
new AppContainer().CreateScope().GetRequiredService<IPerRequest>();
```

This matters because a call site that is *not* recognised does not fail to compile — it falls
through to the `throw new NotImplementedException()` stub and fails at run time. Earlier
versions only accepted a plain local variable, so every form above threw.

#### Caching of the returned array

The plural overloads build a `T[]`. That array is cached, but only when caching cannot
change what the container promises: the lifetime of the array is the **minimum** lifetime of
its elements, and a single `Transient` element disables it entirely — a transient promises a
fresh instance per call, and keeping the array would silently turn it into a singleton.

Because the array is shared, **do not mutate what these methods return**. Details and the
reasoning behind the missing lock are in
[INTERNALS.md](INTERNALS.md#caching-of-the-array-returned-by-the-plural-overloads).

---

## Cancellation

Factory methods may take a `CancellationToken`. The generator never forwards the *caller's*
token: a cached value is handed to every later caller, so recording the first caller's token
inside it would be wrong. Instead the container owns its own `CancellationTokenSource` and
cancels it on disposal, which makes cancellation per-scope for free.

For the same reason **no generated member accepts a `CancellationToken`** — a parameter that
is silently ignored is worse than no parameter at all. See
[INTERNALS.md](INTERNALS.md#cancellation-why-the-callers-token-is-never-forwarded).

---

## Architecture & Performance

The generator emits cached fields guarded by a lock of matching scope, direct instantiation
for transients, and disposal routing in reverse construction order. Scoped locks are
allocated lazily, so opening a scope costs nothing for services it never resolves.

Allocation matches hand-written `new(...)` in every benchmarked scenario. The full generated
anatomy, the benchmark tables and how much to trust them live in
[INTERNALS.md](INTERNALS.md).

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
| `SCDI16` | An async factory must declare the exposed service type |
| `SCDI17` | Two parameters of the same service type both resolve with no key |

Frequent situations:

**"Resolver not generated for type X"** — check that `X` is registered with
`[Singleton<X>]`, `[Scoped<X>]` or `[Transient<X>]`, and that the container class carries
`[ServiceProvider]` and is `partial`.

**`SCDI16`** — `Task<T>` and `ValueTask<T>` are **invariant**: `Task<Impl>` does not convert
to `Task<IService>`, even though `Impl` implements `IService`. Declare the factory as
`Task<IService>` and cast inside it. The generator could await and re-wrap for you, but that
would cost a state machine or an extra allocation on *every* resolution, so it asks instead.

**`SCDI17`** — when several registrations share a service type, only **one** parameter may
take the unkeyed one; anything else would be resolved arbitrarily. Name the other parameters
after a registered key, or annotate them with the matching lifetime attribute and key. Two
parameters sharing the *only* registration of a type are fine — there is nothing to decide.

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




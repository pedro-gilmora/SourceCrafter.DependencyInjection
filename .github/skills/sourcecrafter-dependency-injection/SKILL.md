---
name: sourcecrafter-dependency-injection
description: Use when a C# project references SourceCrafter.DependencyInjection, or when declaring a compile-time DI container with [ServiceProvider], [Singleton<>], [Scoped<>], [Transient<>] or [Hosted<>], resolving keyed services, scopes, async factories or disposal, or fixing SCDI* diagnostics. Don't decompile the generator or web-search the API: the idioms are here.
---

# SourceCrafter.DependencyInjection

Compile-time DI: attributes on a `partial` class declare the services, a Roslyn generator emits plain C# members that build them. No reflection, no runtime registration; an unresolvable graph is a compile error.

## Packages and namespaces

| Need | Install |
|---|---|
| A container | `SourceCrafter.DependencyInjection` (generator + attributes) |
| `appsettings.json` settings | `SourceCrafter.DependencyInjection.MsConfiguration` instead (brings the core, the attributes and Microsoft.Extensions.Configuration); see skill `sourcecrafter-di-configuration` |
| A library that only annotates types | `SourceCrafter.DependencyInjection.Metadata` (attributes, no generator) |

```csharp
using SourceCrafter.DependencyInjection.Attributes; // ServiceProvider, Singleton, Scoped, Transient, Hosted, Root
using SourceCrafter.DependencyInjection.Constants;  // Lifetime, LockOptions, Disposability
```

## Core pattern

```csharp
[ServiceProvider]
[Singleton<IClock, SystemClock>]                          // one per container (static)
[Scoped<IOrderRepository, InMemoryOrderRepository>]       // one per scope
[Singleton<IPaymentGateway, CardGateway>("card")]         // keyed
[Singleton<IPaymentGateway, CashGateway>("cash")]
[Transient<OrderService>]                                 // new every time
public partial class AppContainer;

// Keyed dependencies bind BY PARAMETER NAME: 'card' gets the "card" registration.
public sealed class OrderService(IClock clock, IOrderRepository repo, IPaymentGateway card, IPaymentGateway cash);

using var container = new AppContainer();
using (var scope = container.CreateScope())               // AppContainer.Scoped
{
    var orders = scope.OrderService;                      // generated property
}                                                         // disposes the scope's IDisposable scoped services
```

## Generated surface

- **Member names**: the implementation type name (`SystemClock`, `InMemoryOrderRepository`), not the interface. With keys: `{type}`, then `{type}{key}`, `{lifetime}{key}`… whichever is free. Force one with `nameFormat: "{lifetime}{key}{type}"`.
- **Sync graph** → property (`scope.OrderService`). **Any async factory in the graph** → `Get{Name}Async()` returning `Task<T>`.
- `CreateScope()` returns `Container.Scoped`; the root container is itself a scope. Every registration is a member of the container **and** of each scope (`scope.InMemoryOrderRepository`, `scope.SystemClock`).
- The container and scopes implement `IDisposable`, or `IAsyncDisposable` when an async-disposable service is registered. They dispose only what they built (singletons with the container, scoped with the scope), in reverse order. The container doesn't track scopes: dispose each scope yourself.
- A **singleton is always built from the root**: its scoped dependencies are the root scope's instances (they live as long as the container). Holding one (e.g. a `DbContext`) in a singleton is your choice; make it safe for concurrent use.

## Recipes

| Task | How |
|---|---|
| Factory | `[Singleton<IFoo>(source: nameof(CreateFoo))]` + `static IFoo CreateFoo(...)` on the container (parameters are resolved). An interface without implementation or `source:` is **SCDI06**. |
| Async factory | `static Task<IFoo> CreateFooAsync(CancellationToken token)`: the return type must be exactly `Task<IFoo>` (invariant, **SCDI16**). The token is the container's own, cancelled on disposal. |
| Pick a keyed service at runtime | Inject each key by parameter name and `switch`; or `[ServiceProvider(genericApi: true)]` and `container.GetRequiredKeyedService<IPaymentGateway>(method)` at the composition root (a runtime key goes through a `switch (key)`; it never falls back to another registration). |
| `IServiceProvider`-shaped API | `[ServiceProvider(genericApi: true)]` → `GetService<T>()`, `GetRequiredService<T>()`, keyed and async variants. Without it those calls are **SCDI26**. |
| Two services of one type, no key | Name parameters after keys; two unkeyed parameters of a multiply-registered type are **SCDI17**. |
| Services from an ASP.NET Core / Generic Host | `[Hosted<ILogger<Foo>>]`, `new AppContainer { HostServices = app.Services }`: resolved from the host every time, never built, cached or disposed by the container. |
| Per-request host services (`DbContext`) | `[Hosted<AppDb>(Lifetime.Scoped)]`: each container scope opens one host scope on first use and disposes it with the scope. Resolving it from the root throws; a singleton depending on it (even through scoped services) is **SCDI24**. |
| The container as a dependency | Take `AppContainer` as a parameter; `[Root] AppContainer` asks for the root. |
| Locks | `locks: LockOptions.Dedicated` on a slow singleton factory; `Global` on `Scoped` is **SCDI18**. |

## Common mistakes

| Symptom | Fix |
|---|---|
| CS0246 `ServiceProvider`/`Singleton` not found | `using SourceCrafter.DependencyInjection.Attributes;` |
| CS0103 `Lifetime` | `using SourceCrafter.DependencyInjection.Constants;` |
| **SCDI07** / no members generated | The container class must be `partial`. |
| `[Singleton(key: "card")]` on a constructor parameter "to pick" a key | Attributes on parameters **register** a new service. To consume a key, name the parameter after it. |
| **SCDI06** / CS0144 `new IFoo()` | Register an implementation (`[Singleton<IFoo, Foo>]`) or a factory (`source:`). |
| **SCDI11** on `GetRequiredService<T>()` | `T` (or that constant key) isn't registered on that container. |
| Scoped service shared across requests | You resolved it from the root (`container.X`): create a scope per unit of work. |

All diagnostics are `SCDI01`–`SCDI26`; the message names the offending registration.

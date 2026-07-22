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
For JSON settings (`[JsonSetting<T>]`) and MS.Extensions integration:
```bash
dotnet add package SourceCrafter.DependencyInjection
dotnet add package SourceCrafter.DependencyInjection.Metadata
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

| Attribute | Lifetime | Caching | Use Case |
|-----------|----------|---------|----------|
| `[Singleton<T, TImpl>]` | Application | Static | Stateless services, expensive resources |
| `[Scoped<T, TImpl>]` | Per instance | Instance | Context-local services, repositories |
| `[Transient<T>]` | Per request | None | Stateful objects, value types |
| `[JsonSetting<T>(section)]` | Config | Static | Loaded from `appsettings.json` |
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

### IServiceProvider Interception

Generated extension methods intercept `IServiceProvider` calls:

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
   - Singleton services: `static` fields with thread-safe lazy initialization
   - Scoped services: instance fields with per-scope caching

2. **Resolver Methods**:
   - Direct instantiation for transients
   - Cached lookup + return for singletons/scoped
   - Async-aware parallel resolution for Task-based dependencies

3. **Disposal Routing**:
   - Automatic tracking of disposable dependencies
   - Proper disposal order in `DisposeAsync()` / `Dispose()`

### Performance Characteristics

| Scenario | Runtime Lookup | SourceCrafter | Benefit |
|----------|----------------|---------------|---------|
| Singleton access | ~5μs | <1μs | 5–10x faster |
| Transient creation | ~10μs | ~0.3μs | 30x faster |
| Async dependency | ~8μs + async overhead | Compiled + cached | No reflection → pure execution |

---

---

## Generated Code Example

For the container defined earlier, the generator produces:

```csharp
public partial class ServiceContainer : IAsyncDisposable {
    // Singleton cached statically
    private static Database? _database;

    // Scoped cached per instance
    private AuthService? _authService;

    public Task<Database> GetDatabaseAsync() {
        if (_database?.IsInitialized ?? false) 
            return Task.FromResult<Database>(_database);

        lock (this) {
            _database ??= new Database(/* dependencies */);
            return Task.FromResult<Database>(_database);
        }
    }

    public Task<EmployeeController> GetEmployeeControllerAsync() 
        => /* resolves all dependencies, respecting lifetimes & caching */;

    public Scoped CreateScope() => new();

    public class Scoped : ServiceContainer {
        // Scoped services isolated per scope instance
    }

    public async ValueTask DisposeAsync() {
        if (_authService is IAsyncDisposable ad)
            await ad.DisposeAsync();
    }
}
```

---

## Use Cases

✅ **ASP.NET Core Apps**: Fast, compile-time safe service resolution for every request  
✅ **Microservices**: Minimal overhead, predictable performance  
✅ **High-Frequency APIs**: Cache misses are compile-time artifacts, not runtime penalties  
✅ **Console Apps**: Transparent, zero-configuration DI  
✅ **Testing**: Type-safe mocking and scope isolation  

---

## Troubleshooting

**"Resolver not generated for type X"**
- Check that `X` is registered with `[Singleton<X>]`, `[Scoped<X>]`, or `[Transient<X>]`
- Verify the container class has `[ServiceContainer]` attribute

**"Circular dependency detected at compile time"**
- Restructure to break the cycle (often solvable via factory or lazy wrapper)

**Container won't dispose services**
- Ensure you call `container.Dispose()` or `await container.DisposeAsync()`
- Verify disposable services implement `IDisposable` or `IAsyncDisposable`

---
```cs
#nullable enable
using global::SourceCrafter.DepedencyInjection.Extensions;

namespace SourceCrafter.DependencyInjection.Tests;

[global::System.CodeDom.Compiler.GeneratedCode("SourceCrafter.DependencyInjection", "1.25.259.33")]
public partial class Server : global::System.IAsyncDisposable	
{
    public static string EnvironmentName => global::System.Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Development";

    private global::System.Threading.Tasks.Task<int>? _countAsyncCached;
    private global::System.Threading.Tasks.Task<int> CountAsyncCached
    {
        get
        {
            if(_countAsyncCached is not null) return _countAsyncCached;
				
            lock(this)
			
			return _countAsyncCached ??= CountAsync();
        }
    }

    private global::System.Threading.Tasks.ValueTask<global::System.Guid>? _resolveRequestIdTaskCached;
    private global::System.Threading.Tasks.ValueTask<global::System.Guid> ResolveRequestIdTaskCached
    {
        get
        {
            if(_resolveRequestIdTaskCached.HasValue) return _resolveRequestIdTaskCached.Value;
				
            lock(this)
			
			return _resolveRequestIdTaskCached ??= ResolveRequestIdTask;
        }
    }

    private static global::System.Threading.Tasks.Task<global::SourceCrafter.DependencyInjection.Tests.IDatabase>? _databaseTask;
    public global::System.Threading.Tasks.Task<global::SourceCrafter.DependencyInjection.Tests.IDatabase> GetDatabaseAsync()
    {
        if(_databaseTask is not null) return _databaseTask;
					
		lock(this)
		{
			if(_databaseTask is not null) return _databaseTask;

			var __v1 = ResolveRequestIdTaskCached;

			return _databaseTask = __v1.IsCompletedSuccessfully
				? global::System.Threading.Tasks.Task.FromResult<global::SourceCrafter.DependencyInjection.Tests.IDatabase>(
					new global::SourceCrafter.DependencyInjection.Tests.Database(
						Settings,
						__v1.Result))
				: CompleteAsync();

			async global::System.Threading.Tasks.Task<global::SourceCrafter.DependencyInjection.Tests.IDatabase> CompleteAsync()
			{				
				return new global::SourceCrafter.DependencyInjection.Tests.Database(
					Settings,
					await __v1.ConfigureAwait(false));
			}
		}
    }

    private global::System.Threading.Tasks.Task<global::SourceCrafter.DependencyInjection.Tests.IAuthService>? _authServiceTask;
    private global::System.Threading.Tasks.Task<global::SourceCrafter.DependencyInjection.Tests.IAuthService> GetAuthServiceAsync()
    {
        if(_authServiceTask is not null) return _authServiceTask;
					
		lock(this)
		{
			if(_authServiceTask is not null) return _authServiceTask;

			var __v0 = GetDatabaseAsync();
			var __v1 = CountAsyncCached;

			return _authServiceTask = __v0.IsCompletedSuccessfully
					&& __v1.IsCompletedSuccessfully
				? global::System.Threading.Tasks.Task.FromResult<global::SourceCrafter.DependencyInjection.Tests.IAuthService>(
					new global::SourceCrafter.DependencyInjection.Tests.AuthService(
						__v0.Result,
						__v1.Result))
				: CompleteAsync();

			async global::System.Threading.Tasks.Task<global::SourceCrafter.DependencyInjection.Tests.IAuthService> CompleteAsync()
			{
				await global::System.Threading.Tasks.Task.WhenAll(__v0, __v1);
				
				return new global::SourceCrafter.DependencyInjection.Tests.AuthService(
					__v0.Result,
					__v1.Result);
			}
		}
    }

    private global::System.Threading.Tasks.Task<global::SourceCrafter.DependencyInjection.Tests.EmployeeController>? _employeeControllerTask;
    private global::System.Threading.Tasks.Task<global::SourceCrafter.DependencyInjection.Tests.EmployeeController> GetEmployeeControllerAsync()
    {
        if(_employeeControllerTask is not null) return _employeeControllerTask;
					
		lock(this)
		{
			if(_employeeControllerTask is not null) return _employeeControllerTask;

			var __v0 = GetAuthServiceAsync();
			var __v1 = GetDatabaseAsync();
			var __v2 = CountAsyncCached;
			var __v3 = ResolveRequestIdTaskCached;

			return _employeeControllerTask = __v0.IsCompletedSuccessfully
					&& __v1.IsCompletedSuccessfully
					&& __v2.IsCompletedSuccessfully
					&& __v3.IsCompletedSuccessfully
				? global::System.Threading.Tasks.Task.FromResult<global::SourceCrafter.DependencyInjection.Tests.EmployeeController>(
					new global::SourceCrafter.DependencyInjection.Tests.EmployeeController(
						__v0.Result,
						__v1.Result,
						__v2.Result,
						__v3.Result))
				: CompleteAsync();

			async global::System.Threading.Tasks.Task<global::SourceCrafter.DependencyInjection.Tests.EmployeeController> CompleteAsync()
			{				
				return new global::SourceCrafter.DependencyInjection.Tests.EmployeeController(
					await __v0.ConfigureAwait(false),
					__v1.Result /* resolved previously by param 0 */,
					__v2.Result /* resolved previously by param 0 */,
					__v3.Result /* resolved previously by param 0 */);
			}
		}
    }

	public Scoped CreateScope() => new();
	
	public class Scoped : Server	
	{
		public new global::System.Threading.Tasks.Task<int> CountAsyncCached 
			=> base.CountAsyncCached;

		public new global::System.Threading.Tasks.ValueTask<global::System.Guid> ResolveRequestIdTaskCached 
			=> base.ResolveRequestIdTaskCached;

		public new global::System.Threading.Tasks.Task<global::SourceCrafter.DependencyInjection.Tests.IAuthService> GetAuthServiceAsync() 
			=> base.GetAuthServiceAsync();

		public new global::System.Threading.Tasks.Task<global::SourceCrafter.DependencyInjection.Tests.EmployeeController> GetEmployeeControllerAsync() 
			=> base.GetEmployeeControllerAsync();

		public override global::System.Threading.Tasks.ValueTask DisposeAsync() => base.DisposeAsync();
	}

	public virtual global::System.Threading.Tasks.ValueTask DisposeAsync()
	{
        return _authServiceTask.TryDisposeAsync();
	}

}

public static class ServerExtensions
{
    [global::System.Runtime.CompilerServices.InterceptsLocation(1, "BRzaMNjSxBNQhAv1dHJDmtUBAABUZXN0cy5jcw==")] //D:\Code\SourceCrafter.DependencyInjection\SourceCrafter.DependencyInjection.Tests\Tests.cs(20,37)
    public static global::SourceCrafter.DependencyInjection.Tests.AppSettings CallSingletonSettings(this global::System.IServiceProvider provider)
        => ((global::SourceCrafter.DependencyInjection.Tests.Server)provider).Settings;

    [global::System.Runtime.CompilerServices.InterceptsLocation(1, "BRzaMNjSxBNQhAv1dHJDmlwDAABUZXN0cy5jcw==")] //D:\Code\SourceCrafter.DependencyInjection\SourceCrafter.DependencyInjection.Tests\Tests.cs(30,48)
    public static global::System.Threading.Tasks.Task<global::SourceCrafter.DependencyInjection.Tests.EmployeeController> CallScopedGetEmployeeControllerAsync(this global::System.IServiceProvider provider)
        => ((global::SourceCrafter.DependencyInjection.Tests.Server.Scoped)provider).GetEmployeeControllerAsync();
}
```

## Resources

- **GitHub Repository**: [pedro-gilmora/SourceCrafter.DependencyInjection](https://github.com/pedro-gilmora/SourceCrafter.DependencyInjection)
- **NuGet Packages**:
  - Core: `SourceCrafter.DependencyInjection`
  - MS Configuration: `SourceCrafter.DependencyInjection.MsConfiguration`
- **License**: See repository for details

---

**SourceCrafter.DependencyInjection** makes compile-time DI seamless, fast, and transparent—zero magic, 100% predictable.




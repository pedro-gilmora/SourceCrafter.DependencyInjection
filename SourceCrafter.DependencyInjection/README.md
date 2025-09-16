# SourceCrafter.DependencyInjection - Truly compile-time depedency injection generator

## Overview

**SourceCrafter.DependencyInjection** is a compile-time dependency injection library utilizing attributes to simplify and automate service registration. The package is designed to provide flexibility in configuring service lifetimes, custom factory methods, and other advanced DI features while ensuring compile-time safety.

### Key Features
- **Attribute-based Service Registration**: Register services directly on classes and interfaces using attributes.
- **Flexible Lifetimes**: Supports `Singleton`, `Scoped`, and `Transient` lifetimes.
- **Custom Factories**: Use factory static methods or existing instances (static properties or fields) to provide service implementations.
- **Disposability Management**: Control how services are disposed with customizable `Disposability` settings. It scales at compile time according the disposability. 
  If there are IDisposable services and just having a single one IAsynDiposable, automatically the service is async disposable
- **Advanced Configuration Options**: Define settings like resolver method name formatting, caching, and more through attribute parameters.
---

## Installation

Install the **`SourceCrafter.DependencyInjection`** NuGet package:

```bash
dotnet add package SourceCrafter.DependencyInjection
```

---

## Example Usage

Below is an example of how to apply the available attributes for service registration in a `Server` class, using **`SourceCrafter.DependencyInjection`**.

### 1. Annotating the `Server` Class

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

### 4. Attribute Definitions and Explanation

- **`[ServiceContainer]`**: Marks the `Server` class as a container for services.
- **`[JsonSetting<T>]`**: Specifies that the configuration section `T` should be loaded from a JSON configuration file. In the example, `AppSettings` and `ConnectionStrings::DefaultConnection` are loaded.
- **`[Singleton<T, TImplementation>]`**: Registers a singleton service of type `T` with an implementation of `TImplementation`. Singleton services are created once and shared across the application.
- **`[Scoped<T, TImplementation>]`**: Registers a scoped service of type `T` with an implementation of `TImplementation`. Scoped services are created once per request.
- **`[Transient<T>]`**: Registers a transient service, meaning a new instance of `T` is created each time it is requested. In this example, the `int` value is generated using the `ResolveAsync` method.

---

## Advanced Configuration Options

### 1. Disposability

You can control the lifecycle of services using the `Disposability` parameter, which supports the following options:
- **`None`**: No specific disposal behavior is applied.
- **`Dispose`**: Standard disposal pattern.
- **`AsyncDispose`**: Asynchronous disposal pattern using `IAsyncDisposable`.

### 2. Factory Methods

For advanced scenarios, you can specify factory methods or instances directly using the `source` parameter in the attributes. This allows fine-grained control over how services are created and managed.

### 3. Caching

- Singleton services are cached at static level with appropiate thread-safe handling
- Scoped services are at instance level with appropiate thread-safe handling

>Both of previous ones registered will consider even caching factory obtained values

---

## Conclusion

**SourceCrafter.DependencyInjection** provides a flexible and powerful approach to dependency injection using attributes. It removes much of the boilerplate code required for service registration while allowing you to leverage advanced DI techniques such as factory methods, caching, and disposability control.

For more advanced scenarios and detailed API references, see the official documentation on GitHub.

--- 

## Generated code

As result of the previous example, we can notice some aspects:

- Transient and non-cached services depedencies are called as they are defined: ()

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

----

## Benchmark




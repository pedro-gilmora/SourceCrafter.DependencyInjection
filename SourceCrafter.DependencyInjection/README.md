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
    [JsonSetting<AppSettings>("AppSettings")] // Load settings into AppSettings class
    [JsonSetting<string>("ConnectionStrings::DefaultConnection", nameFormat: "GetConnectionString")] // Connection string
    [Transient<int>("count", nameof(ResolveAsync))] // Register a transient int value using the ResolveAsync method
    [Singleton<IDatabase, Database>] // Register Database as a singleton service
    [Scoped<IAuthService, AuthService>] // Register AuthService as a scoped service
    public partial class Server
    {
        internal static ValueTask<int> ResolveAsync(CancellationToken _)
        {
            return ValueTask.FromResult(1);
        }
    }
}
```

### 2. Service Definitions

#### `AuthService`

This service is scoped, meaning it is created once per request.

```csharp
public class AuthService(IDatabase application, int count) : IAuthService, IDisposable
{
    public int Count => count;
    public IDatabase Database { get; } = application;

    public void Dispose()
    {
        // Cleanup resources, e.g., database connections
    }
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

For advanced scenarios, you can specify factory methods or instances directly using the `factoryOrInstance` parameter in the attributes. This allows fine-grained control over how services are created and managed.

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
namespace SourceCrafter.DependencyInjection.Tests;

[global::System.CodeDom.Compiler.GeneratedCode("SourceCrafter.DependencyInjection", "0.24.280.49")]
public partial class Server : global::System.IAsyncDisposable	
{
    public static string Environment => global::System.Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Development";
    static readonly object __lock = new object();

    private static readonly global::System.Threading.SemaphoreSlim __globalSemaphore = new global::System.Threading.SemaphoreSlim(1, 1);

    private static global::System.Threading.CancellationTokenSource __globalCancellationTokenSrc = new global::System.Threading.CancellationTokenSource();

    private bool isScoped = false;

    [global::System.CodeDom.Compiler.GeneratedCode("SourceCrafter.DependencyInjection", "0.24.280.49")]
    public Server CreateScope() =>
		new global::SourceCrafter.DependencyInjection.Tests.Server { isScoped = true };

    [global::System.CodeDom.Compiler.GeneratedCode("SourceCrafter.DependencyInjection", "0.24.280.49")]
    private static global::SourceCrafter.DependencyInjection.Tests.Database? _getDatabase;

    [global::System.CodeDom.Compiler.GeneratedCode("SourceCrafter.DependencyInjection", "0.24.280.49")]
    public global::SourceCrafter.DependencyInjection.Tests.Database GetDatabase()
    {
		if (_getDatabase is not null) return _getDatabase;

        lock(__lock) return _getDatabase ??= new global::SourceCrafter.DependencyInjection.Tests.Database(GetSettings(), GetConnectionString());
    }

    [global::System.CodeDom.Compiler.GeneratedCode("SourceCrafter.DependencyInjection", "0.24.280.49")]
    private global::SourceCrafter.DependencyInjection.Tests.IAuthService? _getAuthService;

    [global::System.CodeDom.Compiler.GeneratedCode("SourceCrafter.DependencyInjection", "0.24.280.49")]
    public async global::System.Threading.Tasks.ValueTask<global::SourceCrafter.DependencyInjection.Tests.IAuthService> GetAuthServiceAsync(global::System.Threading.CancellationToken? cancellationToken = default)
    {
		if (_getAuthService is not null) return _getAuthService;

        await __globalSemaphore.WaitAsync(cancellationToken ??= __globalCancellationTokenSrc.Token);

        try
        {
            return _getAuthService ??= new global::SourceCrafter.DependencyInjection.Tests.AuthService(GetDatabase(), await ResolveAsync(cancellationToken.Value));
        }
        finally
        {
            __globalSemaphore.Release();
        }
    }

    [global::System.CodeDom.Compiler.GeneratedCode("SourceCrafter.DependencyInjection", "0.24.280.49")]
    public async global::System.Threading.Tasks.ValueTask DisposeAsync()
    {
		if(isScoped)
        {
           _getAuthService?.Dispose();
		}
		else
        {
            (_getConfiguration as global::System.IDisposable)?.Dispose();
            if (_getDatabase is not null) await _getDatabase.DisposeAsync();
		}
	}
}
```


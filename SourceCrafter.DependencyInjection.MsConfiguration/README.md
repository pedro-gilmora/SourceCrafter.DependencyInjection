# SourceCrafter.DependencyInjection.MsConfiguration

## Purpose

**Code generator with Microsoft.Extensions.Configuration support**. This package extends the core SourceCrafter.DependencyInjection generator to add compile-time support for strongly-typed configuration loading from `appsettings.json` and other `IConfiguration` sources.

## What It Provides

This is the **Roslyn code generator** that processes your container and generates optimized resolvers with full Microsoft.Extensions.Configuration integration:

- Analyzes `[ServiceContainer]` classes and service attributes
- Generates configuration-aware resolver methods
- Supports `[JsonSetting<T>]` for strongly-typed settings
- Integrates with `IConfiguration` from Microsoft.Extensions.DependencyInjection
- Generates async-aware, cached resolution code

## Installation

### With Microsoft.Extensions.Configuration Support (Recommended)
```bash
dotnet add package SourceCrafter.DependencyInjection
dotnet add package SourceCrafter.DependencyInjection.Metadata
dotnet add package SourceCrafter.DependencyInjection.MsConfiguration
dotnet add package SourceCrafter.DependencyInjection.MsConfiguration.Metadata
```

This includes support for:
- `[JsonSetting<T>(section)]` – Load settings from JSON configuration
- Seamless `IConfiguration` integration
- Dynamic configuration reloading
- Configuration validation at compile time

### Core Package Only (No Configuration)
If you don't need Microsoft.Extensions.Configuration:
```bash
dotnet add package SourceCrafter.DependencyInjection
```

---

## Microsoft.Extensions.Configuration Support

### Loading Typed Settings

Decorate your container with `[JsonSetting<T>]` to load configuration:

```csharp
[assembly: JsonConfiguration]

[ServiceContainer]
[JsonSetting<AppSettings>("AppSettings")]
[Singleton<IDatabase, Database>]
public partial class ServiceContainer;

public class AppSettings {
    public string ConnectionString { get; set; }
    public int Timeout { get; set; }
}
```

The generator creates:
```csharp

public partial class ServiceContainer
{
    private static global::Microsoft.Extensions.Configuration.IConfiguration? _configuration = null;

    internal global::Microsoft.Extensions.Configuration.IConfiguration Configuration
    {
        get
        {
            if(_configuration is not null) return _configuration;

            lock (this)
            {
                var fileName = global::System.IO.Path.GetFullPath("appsettings");

                return _configuration ??= new global::Microsoft.Extensions.Configuration.ConfigurationBuilder()
                    .AddJsonFile($"{fileName}.{EnvironmentName}.json", true, true)
                    .AddJsonFile($"{fileName}.json", true, true)
                    .Build();
            }
        }
    }

    private static global::SourceCrafter.DependencyInjection.Tests.AppSettings? _appSettings = default;

    internal global::SourceCrafter.DependencyInjection.Tests.AppSettings Settings
    {
        get
        {
            if (_appSettings is not null) return _appSettings;
            
            lock (this)     

            return _appSettings ??= BuildSetting();

            global::SourceCrafter.DependencyInjection.Tests.AppSettings BuildSetting()
            {
                global::SourceCrafter.DependencyInjection.Tests.AppSettings setting = new global::SourceCrafter.DependencyInjection.Tests.AppSettings();

                Configuration
                    .GetSection("AppSettings")                
                    .Bind(setting);

                return setting;
            }
        }
    }

}
```

### At Runtime

```csharp
// With IConfiguration from appsettings.json
var container = new ServiceContainer();
var settings = await container.Settings;
var database = await container.GetDatabaseAsync();  // Probably uses settings
```

### Configuration Reloading

Configuration changes are detected automatically:
- Singleton settings load once and cache
- Scoped settings respect configuration changes per scope
- No manual refresh needed

---

## Code Generation

### At Compile Time

1. **Scans** your container class decorated with `[ServiceContainer]`
2. **Analyzes** all registered services: `[Singleton]`, `[Scoped]`, `[Transient]`, `[JsonSetting<T>]`
3. **Builds** dependency graph and validates configuration section mapping
4. **Generates** resolver methods with configuration binding and caching logic
5. **Creates** disposal routines for `IDisposable`/`IAsyncDisposable` services
6. **Produces** `Scoped` inner class for per-instance service and configuration isolation


## Key Features

- **Compile-Time Safety**: Type-checked service resolution + configuration schema validation
- **Zero Runtime Reflection**: No dictionaries, no dynamic instantiation, no `Activator.CreateInstance`
- **Configuration Binding**: Strongly-typed `[JsonSetting<T>]` with compile-time validation
- **Smart Async**: Caches `Task<T>` and `ValueTask<T>` results; prevents redundant execution
- **Intelligent Disposal**: Detects and applies `IDisposable` or `IAsyncDisposable` automatically
- **Scoped Isolation**: Per-instance configuration and service caching for request-local state
- **Configuration Hot-Reload**: Respects configuration changes in scoped contexts

---

**Use MsConfiguration when** you need to load settings from `appsettings.json` or `IConfiguration`  
**Use Core package when** you only need service registration without configuration

---

## References

- **Core Metadata**: [SourceCrafter.DependencyInjection.Metadata](../SourceCrafter.DependencyInjection.Metadata/README.md)
- **Configuration Metadata**: [SourceCrafter.DependencyInjection.MsConfiguration.Metadata](../SourceCrafter.DependencyInjection.MsConfiguration.Metadata/README.md)
- **Main Documentation**: [SourceCrafter.DependencyInjection](../SourceCrafter.DependencyInjection/README.md)

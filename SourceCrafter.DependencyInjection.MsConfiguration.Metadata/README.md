# SourceCrafter.DependencyInjection.MsConfiguration.Metadata

## Purpose

Provides **metadata attributes for Microsoft.Extensions.Configuration integration** with SourceCrafter.DependencyInjection. This package extends the core attributes to enable strongly-typed configuration loading from `appsettings.json` and other Microsoft configuration sources.

## What It Provides

**Configuration Loading Attributes** for DI containers:

- `[JsonSetting<T>(sectionName)]` – Load strongly-typed settings from JSON configuration sections
- `[ConfigurationSection(key)]` – Raw access to configuration values
- Support for nested and complex configuration objects

These attributes work seamlessly with Microsoft's `IConfiguration` from `Microsoft.Extensions.Configuration`.

## Who Should Use This?

✅ **Library Authors** – Define configuration contracts without coupling consumers to the code generator  
✅ **Shared Kernel Projects** – Define shared service and configuration metadata  
✅ **Minimal Dependencies** – When you only want metadata, not compile-time code generation  

❌ **Not needed if** – You're building a final application; just use `SourceCrafter.DependencyInjection.MsConfiguration` directly

## Installation

```bash
dotnet add package SourceCrafter.DependencyInjection.MsConfiguration.Metadata
```

(Consumers of your library will add `SourceCrafter.DependencyInjection.MsConfiguration` to trigger code generation.)

## How It Works

**Intended for library authors** who want to support configuration without requiring the code generator as a direct dependency.

### Workflow

1. **Library Author**: Adds `SourceCrafter.DependencyInjection.MsConfiguration.Metadata` to library
2. **Library**: Decorates container with configuration attributes:
   ```csharp
   [ServiceContainer]
   [JsonSetting<AppSettings>("AppSettings")]
   public partial class ServiceContainer { }
   ```
3. **Library Consumer**: Adds `SourceCrafter.DependencyInjection.MsConfiguration` (code generator) to their project
4. **At Compile Time**: Generator processes both metadata packages, generates configuration-aware resolvers
5. **At Runtime**: Configuration loads from `IConfiguration` via the generated methods

### Example

```csharp
public class AppSettings {
	public string ConnectionString { get; set; }
	public int Timeout { get; set; }
}

// In library project (only metadata dependency)
[ServiceContainer]
[JsonSetting<AppSettings>("AppSettings")]
[Singleton<IAuthService, AuthService>]
public partial class ServiceContainer { }

// In consumer project (adds code generator)
var container = new ServiceContainer();
var settings = await container.Settings;  // Generated resolver
```


## References

- [SourceCrafter.DependencyInjection](https://www.nuget.org/packages/SourceCrafter.DependencyInjection#readme-body-tab) – Core code generator
- [SourceCrafter.DependencyInjection.Metadata](https://www.nuget.org/packages/SourceCrafter.DependencyInjection.Metadata#readme-body-tab) – Extended metadata for MSEC generator
- [SourceCrafter.DependencyInjection.MsConfiguration](https://www.nuget.org/packages/SourceCrafter.DependencyInjection.MsConfiguration#readme-body-tab) – MSEC generator

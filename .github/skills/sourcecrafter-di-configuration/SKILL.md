---
name: sourcecrafter-di-configuration
description: Use when a SourceCrafter.DependencyInjection container must read appsettings.json or Microsoft.Extensions.Configuration values, using [JsonConfiguration] or [JsonSetting<T>], or when settings come out empty or a generated Settings member is missing (CS0103). Don't decompile the generator or web-search the API: the idioms are here.
---

# SourceCrafter.DependencyInjection: configuration

Binds `appsettings.json` (plus `appsettings.{Environment}.json`) to typed settings that the container injects like any other singleton. Read skill `sourcecrafter-dependency-injection` for containers in general.

## Install

```xml
<PackageReference Include="SourceCrafter.DependencyInjection.MsConfiguration" Version="..." />
```

That single package brings the core generator, the attributes and `Microsoft.Extensions.Configuration.Json`/`.Binder`. Don't add the other DI packages separately.

## Core pattern

```csharp
using SourceCrafter.DependencyInjection.Attributes;
using SourceCrafter.DependencyInjection.MsConfiguration.Metadata;

[assembly: JsonConfiguration]                 // REQUIRED once: which file to load (default "appsettings").
                                              // With top-level statements: after the usings, before the first statement.

[ServiceProvider]
[JsonSetting<ShopSettings>("Shop")]           // section path in the JSON
[Transient<OrderService>]
public partial class AppContainer;

public class ShopSettings { public string Currency { get; set; } = ""; public decimal TaxRate { get; set; } }

public sealed class OrderService(ShopSettings settings);   // injected by type, like any service
```

```xml
<ItemGroup>
  <None Update="appsettings.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

## How it behaves

- The file is resolved **next to the application** (`AppContext.BaseDirectory`), so copy it to the output. An absolute `fileName` is used as is.
- `appsettings.{EnvironmentName}.json` is layered on top. `EnvironmentName` comes from `DOTNET_ENVIRONMENT` (`[ServiceProvider(envName: "...")]` to change the variable) and defaults to `Development`.
- `[JsonConfiguration(optional: true)]` is the default: a missing file gives default-valued settings **without an error**. Use `optional: false` when the settings are mandatory.
- A setting is a singleton by default (`lifetime:` changes it). The generated member is `internal`, named `Settings`, or `{Key}Settings` with `key:` (`nameFormat:` to change it).
- `IConfiguration` itself: the container exposes it as the `Configuration` member (`nameFormat` on `[JsonConfiguration]`).

## Common mistakes

| Symptom | Fix |
|---|---|
| CS0103 `Settings` does not exist | `[JsonConfiguration]` is missing (assembly or container). Without it no setting is emitted. |
| Settings empty / zero, no error | `appsettings.json` isn't copied to the output, or the section path is wrong (`"Shop"` vs `"Shop:Sub"`). |
| CS0246 `JsonSetting` not found | `using SourceCrafter.DependencyInjection.MsConfiguration.Metadata;` |

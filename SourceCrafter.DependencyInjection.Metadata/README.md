# SourceCrafter.DependencyInjection.Metadata

## Purpose

This NuGet package contains **compile-time attribute metadata** only. It provides the attribute definitions—`[ServiceContainer]`, `[Singleton<T>]`, `[Scoped<T>]`, `[Transient<T>]`, and `[JsonSetting<T>]`—without any code generator.

## Use Cases

- **For Library Authors**: Add attribute support to your libraries without requiring the full code generator as a dependency
- **Minimal Footprint**: Ships only metadata; no compile-time overhead (attribute definitions are erased after compilation)
- **Design-Time Contracts**: Define service contracts that other projects can implement

## Installation

```bash
dotnet add package SourceCrafter.DependencyInjection.Metadata
```

## What's Included

- `[ServiceContainer]` – Container marker
- `[Singleton<T, TImpl>]`, `[Scoped<T, TImpl>]`, `[Transient<T>]` – Service lifetime attributes
- `[JsonSetting<T>(section)]` – Configuration loading
- Supporting enums: `Disposability` (None, Dispose, AsyncDispose)

## Typical Workflow

1. Add this package to your **library project**
2. Decorate your classes with service attributes
3. Consumers of your library add `SourceCrafter.DependencyInjection` to get the code generator
4. Generator sees your attributes and generates resolvers in consumer's container

## Target Framework

- `.NET Standard 2.0` – Wide compatibility with .NET Framework, .NET Core, and modern .NET

## See Also

- [SourceCrafter.DependencyInjection](../SourceCrafter.DependencyInjection/README.md) – Core code generator
- [SourceCrafter.DependencyInjection.MsConfiguration.Metadata](../SourceCrafter.DependencyInjection.MsConfiguration.Metadata/README.md) – Extended metadata for MS.Extensions

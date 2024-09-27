global using DependencyMap = SourceCrafter.DependencyInjection.Map<
    (SourceCrafter.DependencyInjection.Interop.Lifetime lifeTime, string exportFullTypeName, string? enumKey),
    SourceCrafter.DependencyInjection.Interop.ServiceDescriptor>;
global using DependencyKey = (SourceCrafter.DependencyInjection.Interop.Lifetime LifeTime, string ExportFullTypeName, string? Key);

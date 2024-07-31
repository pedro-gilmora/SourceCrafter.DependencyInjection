global using DependencyMap = SourceCrafter.DependencyInjection.Map<
    (SourceCrafter.DependencyInjection.Interop.Lifetime lifeTime, string exportFullTypeName, Microsoft.CodeAnalysis.IFieldSymbol? enumKey),
    SourceCrafter.DependencyInjection.Interop.ServiceDescriptor>;
global using DependencyKey = (SourceCrafter.DependencyInjection.Interop.Lifetime LifeTime, string ExportFullTypeName, Microsoft.CodeAnalysis.IFieldSymbol? Key);

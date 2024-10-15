using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;

using System.Linq;

using System.Text;

using System;
using System.IO;

namespace SourceCrafter.DependencyInjection.Interop;

using static ServiceDescriptor;

delegate void DisposabilityBuilder(StringBuilder code, string? indent = "    ");

internal sealed class ServiceContainer
{
    //CommaSeparateBuilder? interfaces = null;

    MemberBuilder?
        methods = null;

    DisposabilityBuilder?
        singletonDisposeStatments = null,
        disposeStatments = null;

    internal bool requiresSemaphore = false;

    bool //useIComma = false,
        hasScopedServices = false,
        requiresLocker = false/*,
        hasAsyncService = false*/;

    (Disposability, bool) DisposableInfo
    {
        get
        {
            (Disposability d, bool isd) result = default;

            foreach (var value in ServicesMap.Values)
            {
                if (value is not { IsCached: true, Lifetime: < Lifetime.Transient, Disposability: > Disposability.None }) continue;

                if (value.Disposability > result.d) result.d = value.Disposability;

                if (!result.isd && value is { IsCached: true, Lifetime: Lifetime.Scoped }) result.isd = true;
            }

            return result;
        }
    }

    internal readonly SemanticModel Model;

    internal readonly string GeneratorGuid;

    internal readonly string ProviderTypeName;

    internal readonly Compilation Compilation;

    internal readonly Set<Diagnostic> Diagnostics;

    internal readonly INamedTypeSymbol ProviderClass;

    readonly ImmutableArray<AttributeData> Attributes;

    internal readonly ImmutableArray<InvokeInfo> ServiceCalls;

    internal readonly HashSet<(string?, string)> InterfacesRegistry = [];

    internal HashSet<string> MethodsRegistry = new(StringComparer.Ordinal);

    internal readonly DependencyMap ServicesMap = new(new DependencyComparer<string>());

    internal readonly DependencyNamesMap MethodNamesMap = new(new DependencyComparer<int>());

    internal Disposability disposability = 0;

    public static ServiceContainer Parse(
        Compilation compilation,
        SemanticModel model,
        INamedTypeSymbol providerClass,
        Set<Diagnostic> diagnostics,
        ImmutableArray<AttributeData> externals,
        string generatorGuid,
        ImmutableArray<InvokeInfo> serviceCalls)

            => new(compilation, model, providerClass, diagnostics, externals, generatorGuid, serviceCalls);

    ServiceContainer(
        Compilation compilation,
        SemanticModel model,
        INamedTypeSymbol providerClass,
        Set<Diagnostic> diagnostics,
        ImmutableArray<AttributeData> externals,
        string generatorGuid,
        ImmutableArray<InvokeInfo> serviceCalls)
    {
        ProviderClass = providerClass;
        Compilation = compilation;
        Model = model;
        Diagnostics = diagnostics;
        this.GeneratorGuid = generatorGuid;
        ServiceCalls = serviceCalls;
        ProviderTypeName = ProviderClass.ToGlobalNamespaced();
        Attributes = ProviderClass.GetAttributes();

        foreach (var attr in externals.Concat(Attributes))
        {
            if (attr.AttributeClass is null) continue;

            ParseDependencyAttribute(
                attr,
                providerClass);
        }

        /*foreach (var item in servicesMap.Values) ResolveService(item)*/;
    }

    internal void CheckMethodUsage(bool isOrHasScopedDependencies, string methodName)
    {
        if (isOrHasScopedDependencies &&
            ServiceCalls.FirstOrDefault(sc =>
                SymbolEqualityComparer.Default.Equals(sc.ContainerType, ProviderClass)
                && methodName == sc.Name
                && sc.NotFromScopedInstance) is { } el)
        {
            Diagnostics.TryAdd(
                ServiceContainerGeneratorDiagnostics.DependencyCallMustBeScoped(ProviderTypeName, el.MethodSyntax));
        }
    }

    private void ParseDependencyAttribute(
        AttributeData attributeData,
        INamedTypeSymbol providerClass)
    {
        disposability = default;
        INamedTypeSymbol? originalAtrClass = attributeData.AttributeClass;
        INamedTypeSymbol attrClass = originalAtrClass!;
        var isExternal = false;

        if (!Model.TryGetDependencyInfo(
            attributeData,
            ref isExternal,
            "",
            null,
            out var lifetime,
            out var finalType,
            out var iFaceType,
            out var implType,
            out var factory,
            out var factoryKind,
            out var outKey,
            out var nameFormat,
            out var defaultParamValues,
            out var isCached,
            out var _disposability,
            out var isValid,
            out var attrSyntax)) return;

        if (!isCached && lifetime is not Lifetime.Transient) isCached = true;

        if (HasNoType() || InterfaceRequiresInternalFactory()) return;

        var isAsync = finalType.TryGetAsyncType(out var realParamType);

        if (isAsync)
        {
            finalType = realParamType!;

            if (!requiresSemaphore) UpdateAsyncStatus();

            if (factoryKind is SymbolKind.Method
                && !((IMethodSymbol)factory!).Parameters.Any(p => p.Type.ToDisplayString() is CancelTokenFQMetaName))
            {
                Diagnostics.TryAdd(ServiceContainerGeneratorDiagnostics.CancellationTokenShouldBeProvided(factory, attrSyntax));
            }
        }

        Disposability thisDisposability = Disposability.None;

        if (isCached)
        {
            thisDisposability = implType.GetDisposability();

            if (thisDisposability > disposability) disposability = thisDisposability;

            if (_disposability > disposability) disposability = _disposability;
        }

        var typeName = (implType ?? finalType).ToGlobalNamespaced();

        var exportTypeFullName = iFaceType?.ToGlobalNamespaced() ?? typeName;

        ref var existingOrNew = ref ServicesMap.GetValueOrAddDefault((lifetime, exportTypeFullName, outKey), out var exists)!;

        string methodName = GetMethodName(isExternal, lifetime, finalType, implType, factory, outKey, nameFormat, isCached, isAsync, MethodsRegistry, MethodNamesMap);

        if (exists)
        {
            Diagnostics.TryAdd(
                ServiceContainerGeneratorDiagnostics
                    .DuplicateService(lifetime, outKey, attrSyntax, typeName, exportTypeFullName));

            return;
        }
        else
        {
            if (!isExternal && implType!.IsPrimitive() && outKey is "")
            {
                Diagnostics.TryAdd(
                    ServiceContainerGeneratorDiagnostics
                        .PrimitiveDependencyShouldBeKeyed(lifetime, attrSyntax, typeName, exportTypeFullName));
            }

            existingOrNew = new(finalType, outKey, iFaceType)
            {
                ServiceContainer = this,
                OriginDefinition = attrSyntax,
                Lifetime = lifetime,
                Key = outKey,
                IsExternal = isExternal,
                FullTypeName = typeName,
                ExportTypeName = (iFaceType ?? implType ?? finalType).ToGlobalNamespaced(),
                ResolverMethodName = methodName,
                CacheField = "_" + methodName.Camelize(),
                Factory = factory,
                FactoryKind = factoryKind,
                Disposability = (Disposability)Math.Max((byte)thisDisposability, (byte)_disposability),
                IsResolved = true,
                Attributes = implType!.GetAttributes(),
                RequiresDisposabilityCast = thisDisposability is Disposability.None && _disposability is not Disposability.None,
                IsAsync = isAsync,
                ContainerType = providerClass,
                IsCached = isCached,
                Params = implType.GetParameters(),
                DefaultParamValues = defaultParamValues
            };

            ResolveService(existingOrNew);
        }

        bool HasNoType() => implType is null && iFaceType is null;

        bool InterfaceRequiresInternalFactory()
        {
            if (iFaceType is not null && implType is null && factory is null && !isExternal)
            {
                Diagnostics.TryAdd(
                    ServiceContainerGeneratorDiagnostics.InterfaceRequiresFactory(attrSyntax.Name));

                return true;
            }

            return false;
        }
    }

    internal void ResolveService(ServiceDescriptor service)
    {
        if (service is { Factory: null, IsAsync: false } && service.Type.IsPrimitive()) return;

        service.CheckParamsDependencies();

        if (service.NotRegistered || service.IsCancelTokenParam) return;

        //if (InterfacesRegistry.Add((service.Key, service.ExportTypeName)))
        //{
        //    interfaces += service.AddInterface;
        //}

        if (service.Lifetime is Lifetime.Scoped && !hasScopedServices) hasScopedServices = true;

        if (service.Lifetime is not Lifetime.Transient)
        {
            if (!requiresSemaphore && service.IsAsync)
            {
                UpdateAsyncStatus();
            }
            else if (!requiresLocker)
            {
                requiresLocker = true;
            }

            switch (service.Disposability)
            {
                case Disposability.AsyncDisposable:

                    if (service.Lifetime is Lifetime.Scoped)
                    {
                        disposeStatments += service.BuildDisposeAsyncStatment;
                    }
                    else
                    {
                        singletonDisposeStatments += service.BuildDisposeAsyncStatment;
                    }

                    break;
                case Disposability.Disposable:

                    if (service.Lifetime is Lifetime.Scoped)
                    {
                        disposeStatments += service.BuildDisposeStatment;
                    }
                    else
                    {
                        singletonDisposeStatments += service.BuildDisposeStatment;
                    }
                    break;
            }
        }

        if (service.Disposability > disposability) disposability = service.Disposability;

        if (service is { IsExternal: true } or { IsFactory: true, IsCached: false } or { IsSimpleTransient: true }) return;

        methods += service.BuildMethod;
    }

    internal void UpdateAsyncStatus()
    {
        requiresSemaphore = true;

        if (Compilation.GetTypeByMetadataName(CancelTokenFQMetaName) is { } cancelType)
        {
            string cancelTypeName = cancelType.ToGlobalNamespaced();

            ServicesMap.TryInsert(
                (Lifetime.Singleton, cancelTypeName, ""),
                () => new(cancelType, "")
                {
                    Lifetime = Lifetime.Singleton,
                    ExportTypeName = cancelTypeName,
                    FullTypeName = cancelTypeName,
                    ServiceContainer = this,
                    IsResolved = true,
                    IsCancelTokenParam = true
                });
        }
    }

    public void Build(
        Dictionary<string, DependencyMap> containers,
        ImmutableArray<ITypeSymbol> usages,
        Map<string, byte> uniqueName,
        Action<string, string> addSource,
        string? net9Lock,
        SyntaxNode declaration)
    {
        if (ServicesMap.IsEmpty /*interfaces == null*/) return;

        containers[ProviderTypeName] = ServicesMap;

        StringBuilder code = new(@"#nullable enable
");

        var fileName = ProviderClass.ToMetadataLongName(uniqueName);

        if (ProviderClass.ContainingNamespace is { IsGlobalNamespace: false } ns)
        {
            code.Append("namespace ")
                .Append(ns.ToDisplayString()!)
                .Append(@";

");
        }

        var (modifiers, typeName) = declaration switch
        {
            ClassDeclarationSyntax { Modifiers: var mods, Keyword: { } keyword, Identifier: { } identifier, TypeParameterList: var argList } =>
                ($"{mods} {keyword}".TrimStart(), $"{identifier}{argList}"),
            InterfaceDeclarationSyntax { Modifiers: var mods, Keyword: { } keyword, Identifier: { } identifier, TypeParameterList: var argList } =>
                ($"{mods} {keyword}".TrimStart(), $"{identifier.ValueText[1..]}{argList}"),
            _ => ("", "")
        };

        code.AppendLine(GeneratorGuid)
            .Append(modifiers)
            .AddSpace()
            .Append(typeName);

        var (disposability, hasDisposableScoped) = DisposableInfo;

        BuildDisposability(code, disposability);

        if (ProviderClass.TypeKind is TypeKind.Struct)
        {
            code.Append(@"
    public ").Append(typeName).Append(@"() { }
");
        }

        code
            .Append(@"
    public static string Environment => global::System.Environment.GetEnvironmentVariable(""DOTNET_ENVIRONMENT"") ?? ""Development"";
");

        if (requiresLocker)
        {
            code.Append(@"
    static readonly ").Append(net9Lock ?? "object").Append(@" __lock = new ();
");
        }

        if (requiresSemaphore)
        {
            code.Append(@"
    private static readonly global::System.Threading.SemaphoreSlim __globalSemaphore = new (1, 1);

    private static global::System.Threading.CancellationTokenSource __globalCancellationTokenSrc = new ();
");
        }

        methods?.Invoke(code, true);

        BuildDisposabilityMethods(code, typeName, disposability, hasDisposableScoped);

        var codeStr = code.Append('}').ToString();

        addSource(fileName + ".generated", codeStr);
    }
    private void BuildDisposabilityMethods(StringBuilder code, string typeName, Disposability disposability, bool hasDisposableScoped)
    {
        if (disposability is not Disposability.None)
        {
            if (hasScopedServices)

                code.Append(@"
    private bool isScoped = false;

    public ")
                .Append(typeName)
                .Append(@" CreateScope() => new ").Append(typeName).Append(@" { isScoped = true };
");

            code.Append(@"
    public ");

            if (ProviderClass is { TypeKind: not TypeKind.Struct, IsSealed: false })
                code.Append("virtual ");

            if (disposability is Disposability.Disposable)

                code.Append(@"void Dispose()
    {");

            else

                code.Append(@"async global::System.Threading.Tasks.ValueTask DisposeAsync()
    {");

            if (hasDisposableScoped)
            {
                switch ((disposeStatments, singletonDisposeStatments))
                {
                    case ({ }, { }):

                        code.Append(@"
        if(isScoped)
        {");

                        disposeStatments(code);

                        code.Append(@"
        }
        else
        {");

                        singletonDisposeStatments(code);

                        code.Append(@"
        }");

                        break;

                    case ({ }, null):

                        disposeStatments(code, null);

                        break;

                    case (null, { }):

                        if (hasScopedServices)
                        {
                            code.Append(@"
        if(isScoped) return;
");

                        }

                        singletonDisposeStatments(code, null);

                        break;
                }
            }
            else if (singletonDisposeStatments is { })
            {
                if (hasScopedServices)
                {
                    code.Append(@"
        if(isScoped) return;
");

                }

                singletonDisposeStatments(code, null);
            }

            code.Append(@"
    }
");
        }
    }

    private void BuildDisposability(StringBuilder code, Disposability disposability)
    {

        switch (disposability)
        {
            case Disposability.Disposable:

                code.Append(@" : global::System.IDisposable	
{");

                break;

            case Disposability.AsyncDisposable:

                code.Append(@" : global::System.IAsyncDisposable	
{");

                break;

            default:
                code.Append(@"	
{");
                break;
        }
    }
}

internal record InvokeInfo(ITypeSymbol ContainerType, string Name, IdentifierNameSyntax MethodSyntax, bool NotFromScopedInstance);

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

delegate void DisposabilityBuilder(StringBuilder code);

internal class ServiceContainer
{
    internal readonly string providerTypeName;

    readonly ImmutableArray<AttributeData> attributes;

    internal readonly SemanticModel _model;

    internal readonly Set<Diagnostic> _diagnostics;

    internal readonly string _generatorGuid;

    internal readonly ImmutableArray<InvokeInfo> _serviceCalls;

    internal readonly INamedTypeSymbol _providerClass;

    internal readonly Compilation _compilation;

    internal HashSet<string> methodsRegistry = new(StringComparer.Ordinal);

    internal readonly HashSet<(string?, string)> interfacesRegistry = [];

    internal readonly DependencyNamesMap methodNamesMap = new(new DependencyComparer<int>());

    internal readonly DependencyMap servicesMap = new(new DependencyComparer<string>());

    CommaSeparateBuilder? interfaces = null;

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

    (Disposability, bool) DisposableInfo => servicesMap.Values
        .Where(s => s is { IsCached: true, Lifetime: < Lifetime.Transient, Disposability: > Disposability.None })
        .Aggregate((d: Disposability.None, isd: false), (s, i) => (i.Disposability > s.d ? i.Disposability : s.d, s.isd || i is { IsCached: true, Lifetime: Lifetime.Scoped }));

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
        _providerClass = providerClass;
        _compilation = compilation;
        _model = model;
        _diagnostics = diagnostics;
        _generatorGuid = generatorGuid;
        _serviceCalls = serviceCalls;
        providerTypeName = _providerClass.ToGlobalNamespaced();
        attributes = _providerClass.GetAttributes();
        foreach (var attr in externals.Concat(attributes))
        {
            if (attr.AttributeClass is null) continue;

            ParseDependencyAttribute(
                attr.AttributeClass,
                (AttributeSyntax)attr.ApplicationSyntaxReference!.GetSyntax(),
                attr.AttributeConstructor?.Parameters,
                providerClass);
        }

        foreach (var item in servicesMap.ValuesAsSpan()) ResolveService(item);
    }

    internal void CheckMethodUsage(Lifetime lifetime, string methodName)
    {
        if (lifetime is Lifetime.Scoped
                    && _serviceCalls.FirstOrDefault(sc => SymbolEqualityComparer.Default.Equals(sc.ContainerType, _providerClass) && methodName == sc.Name && sc.IsNotScoped) is { } el)
        {
            _diagnostics.TryAdd(
                ServiceContainerGeneratorDiagnostics.DependencyCallShouldBeScoped(providerTypeName, el.MethodSyntax));
        }
    }

    private void ParseDependencyAttribute(
        INamedTypeSymbol originalAttrClass,
        AttributeSyntax attrSyntax,
        ImmutableArray<IParameterSymbol>? parameters,
        INamedTypeSymbol providerClass)
    {
        disposability = default;
        INamedTypeSymbol attrClass = originalAttrClass;
        var isExternal = false;

        if (attrClass is null
            || attrClass.Name.StartsWith("ServiceContainer")
            || GetLifetimeFromCtor(ref attrClass, ref isExternal, attrSyntax) is not { } lifetime) return;


        if (!TryGetDependencyInfo(
                _model,
                attrClass.TypeArguments,
                attrSyntax.ArgumentList?.Arguments ?? default,
                parameters,
                null,
                "",
                out var depInfo)) return;

        if (!depInfo.IsCached && lifetime is not Lifetime.Transient) depInfo.IsCached = true;

        if (HasNoType() || InterfaceRequiresInternalFactory()) return;

        var isAsync = depInfo.FinalType.TryGetAsyncType(out var realParamType);

        if (isAsync)
        {
            depInfo.FinalType = realParamType!;

            if(!requiresSemaphore) UpdateAsyncStatus();

            if (depInfo.FactoryKind is SymbolKind.Method
                && !((IMethodSymbol)depInfo.Factory!).Parameters.Any(p => p.Type.ToDisplayString() is CancelTokenFQMetaName))
            {
                _diagnostics.TryAdd(ServiceContainerGeneratorDiagnostics.CancellationTokenShouldBeProvided(depInfo.Factory, attrSyntax));
            }
        }

        Disposability thisDisposability = Disposability.None;

        if (depInfo.IsCached)
        {
            thisDisposability = depInfo.ImplType.GetDisposability();

            if (thisDisposability > disposability) disposability = thisDisposability;

            if (depInfo.Disposability > disposability) disposability = depInfo.Disposability;
        }

        var typeName = (depInfo.ImplType ?? depInfo.FinalType).ToGlobalNamespaced();

        var exportTypeFullName = depInfo.IFaceType?.ToGlobalNamespaced() ?? typeName;

        ref var existingOrNew = ref servicesMap.GetValueOrAddDefault((lifetime, exportTypeFullName, depInfo.Key), out var exists)!;

        string methodName = GetMethodName(isExternal, lifetime, depInfo, isAsync, methodsRegistry, methodNamesMap);

        if (exists)
        {
            _diagnostics.TryAdd(
                ServiceContainerGeneratorDiagnostics
                    .DuplicateService(lifetime, depInfo.Key, attrSyntax, typeName, exportTypeFullName));

            return;
        }
        else
        {
            if (!isExternal && depInfo.ImplType!.IsPrimitive() && depInfo.Key is "")
            {
                _diagnostics.TryAdd(
                    ServiceContainerGeneratorDiagnostics
                        .PrimitiveDependencyShouldBeKeyed(lifetime, attrSyntax, typeName, exportTypeFullName));
            }

            existingOrNew = new(depInfo.FinalType, depInfo.Key, depInfo.IFaceType)
            {
                ServiceContainer = this,
                OriginDefinition = attrSyntax,
                Lifetime = lifetime,
                Key = depInfo.Key,
                IsExternal = isExternal,
                FullTypeName = typeName,
                ExportTypeName = (depInfo.IFaceType ?? depInfo.ImplType ?? depInfo.FinalType).ToGlobalNamespaced(),
                ResolverMethodName = methodName,
                CacheField = "_" + methodName.Camelize(),
                Factory = depInfo.Factory,
                FactoryKind = depInfo.FactoryKind,
                Disposability = (Disposability)Math.Max((byte)thisDisposability, (byte)depInfo.Disposability),
                IsResolved = true,
                Attributes = depInfo.ImplType!.GetAttributes(),
                RequiresDisposabilityCast = thisDisposability is Disposability.None && depInfo.Disposability is not Disposability.None,
                IsAsync = isAsync,
                ContainerType = providerClass,
                IsCached = depInfo.IsCached,
                Params = Extensions.GetParameters(depInfo),
                DefaultParamValues = depInfo.DefaultParamValues
            };
        }

        bool HasNoType() => depInfo is { ImplType: null, IFaceType: null };

        bool InterfaceRequiresInternalFactory()
        {
            if (depInfo is { IFaceType: not null, ImplType: null, Factory: null } && !isExternal)
            {
                _diagnostics.TryAdd(
                    ServiceContainerGeneratorDiagnostics.InterfaceRequiresFactory(attrSyntax.Name));

                return true;
            }

            return false;
        }
    }

    internal void ResolveService(ServiceDescriptor foundService)
    {
        if (foundService.Factory is null && foundService.Type.IsPrimitive()) return;

        foundService.CheckParamsDependencies(this, _serviceCalls);

        if (foundService.NotRegistered || foundService.IsCancelTokenParam) return;

        if (interfacesRegistry.Add((foundService.Key, foundService.ExportTypeName)))
        {
            interfaces += foundService.AddInterface;
        }

        if (foundService.Lifetime is Lifetime.Scoped && !hasScopedServices) hasScopedServices = true;

        if (foundService.Lifetime is not Lifetime.Transient)
        {
            if (!requiresSemaphore && foundService.IsAsync)
            {
                UpdateAsyncStatus();
            }
            else if (!requiresLocker)
            {
                requiresLocker = true;
            }

            switch (foundService.Disposability)
            {
                case Disposability.AsyncDisposable:

                    if (foundService.Lifetime is Lifetime.Scoped)
                    {
                        disposeStatments += foundService.BuildDisposeAsyncStatment;
                    }
                    else
                    {
                        singletonDisposeStatments += foundService.BuildDisposeAsyncStatment;
                    }

                    break;
                case Disposability.Disposable:

                    if (foundService.Lifetime is Lifetime.Scoped)
                    {
                        disposeStatments += foundService.BuildDisposeStatment;
                    }
                    else
                    {
                        singletonDisposeStatments += foundService.BuildDisposeStatment;
                    }
                    break;
            }
        }

        if (foundService.Disposability > disposability) disposability = foundService.Disposability;

        if (foundService is { IsExternal: true } or { IsFactory: true, IsCached: false } || foundService.Lifetime is Lifetime.Transient) return;

        methods += foundService.BuildResolver;
    }

    internal void UpdateAsyncStatus()
    {
        requiresSemaphore = true;

        if (_compilation.GetTypeByMetadataName(CancelTokenFQMetaName) is { } cancelType)
        {
            string cancelTypeName = cancelType.ToGlobalNamespaced();

            servicesMap.GetValueOrAddDefault(
                (Lifetime.Singleton, cancelTypeName, ""),
                out _,
                () => new(cancelType, "")
                {
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
        Action<string, string> addSource)
    {
        if (servicesMap.IsEmpty /*interfaces == null*/) return;

        containers[providerTypeName] = servicesMap;

        StringBuilder code = new(@"#nullable enable
");

        var fileName = _providerClass.ToMetadataLongName(uniqueName);

        if (_providerClass.ContainingNamespace is { IsGlobalNamespace: false } ns)
        {
            code.Append("namespace ")
                .Append(ns.ToDisplayString()!)
                .Append(@";

");
        }

        var (modifiers, typeName) = _providerClass.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() switch
        {
            ClassDeclarationSyntax { Modifiers: var mods, Keyword: { } keyword, Identifier: { } identifier, TypeParameterList: var argList } =>
                ($"{mods} {keyword}".TrimStart(), $"{identifier}{argList}"),
            StructDeclarationSyntax { Modifiers: var mods, Keyword: { } keyword, Identifier: { } identifier, TypeParameterList: var argList } =>
                ($"{mods} {keyword}".TrimStart(), $"{identifier}{argList}"),
            _ => ("partial class ", "")
        };

        code.AppendLine(_generatorGuid)
            .Append(modifiers)
            .AddSpace()
            .Append(typeName);

        var (disposability, hasDisposableScoped) = DisposableInfo;

        BuildDisposability(code, disposability);

        if(_providerClass.TypeKind is TypeKind.Struct)
        {
            code.Append(@"
    public ").Append(typeName).Append(@"() { }
");
        }

        code
            .Append(@"
    public static string Environment => global::System.Environment.GetEnvironmentVariable(""DOTNET_ENVIRONMENT"") ?? ""Development"";");

        if (requiresLocker)
        {
            code.Append(@"
    static readonly object __lock = new object();
");
        }

        if (requiresSemaphore)
        {
            code.Append(@"
    private static readonly global::System.Threading.SemaphoreSlim __globalSemaphore = new global::System.Threading.SemaphoreSlim(1, 1);

    private static global::System.Threading.CancellationTokenSource __globalCancellationTokenSrc = new global::System.Threading.CancellationTokenSource();
");
        }

        methods?.Invoke(code, true, _generatorGuid);

        BuildDisposabilityMethods(code, typeName, disposability, hasDisposableScoped);

        var codeStr = code.Append('}').ToString();

        addSource(fileName + ".generated", codeStr);
    }
    private void BuildDisposabilityMethods(StringBuilder code, string typeName, Disposability disposability, bool hasDisposableScoped)
    {
        if (disposability is not Disposability.None)
        {
            if(hasScopedServices)
                
                code.Append(@"
    private bool isScoped = false;

    ")
                .AppendLine(_generatorGuid)
                .Append(@"    public ")
                .Append(typeName)
                .Append(@" CreateScope() => new ").Append(typeName).Append(@" { isScoped = true };
");


            switch (disposability)
            {

                case Disposability.Disposable:

                    code.Append(@"
    ").AppendLine(_generatorGuid)
                        .Append(@"    public");

                    if(_providerClass is { TypeKind: not TypeKind.Struct, IsSealed: false }) 
                        code.Append(" virtual");
                    

                    code.Append(@" void Dispose()
    {");

                    break;

                case Disposability.AsyncDisposable:

                    code.Append(@"
    ").AppendLine(_generatorGuid);



                    code.Append(@"    public");

                    if (_providerClass is { TypeKind: not TypeKind.Struct, IsSealed: false })
                        code.Append(" virtual");
                    
                    code.Append(@" async global::System.Threading.Tasks.ValueTask DisposeAsync()
    {");

                    break;
            }

            if (hasDisposableScoped)
            {
                switch ((disposeStatments, singletonDisposeStatments))
                {
                    case ({ }, { }):

                        disposeStatments(code);

                        if (hasScopedServices)
                        {
                            code.Append(@"

        if(isScoped) return;
");
                        }

                        singletonDisposeStatments(code);

                        break;

                    case ({ }, null):

                        disposeStatments(code);

                        break;

                    case (null, { }):

                        if(hasScopedServices)
                        {
                            code.Append(@"
        if(isScoped) return;
");

                        }

                        singletonDisposeStatments(code);

                        break;
                }
            }
            else if(singletonDisposeStatments is { })
            {
                if (hasScopedServices)
                {
                    code.Append(@"
        if(isScoped) return;
");

                }

                singletonDisposeStatments(code);
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

internal record InvokeInfo(ITypeSymbol ContainerType, string Name, IdentifierNameSyntax MethodSyntax, bool IsNotScoped);

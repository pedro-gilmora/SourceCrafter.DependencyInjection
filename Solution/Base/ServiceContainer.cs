using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SourceCrafter.DependencyInjection.Constants;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
namespace SourceCrafter.DependencyInjection;

using static ServiceDescriptor;
using static SourceCrafter.DependencyInjection.Helpers;

delegate void DisposabilityBuilder(StringBuilder code, string? indent = "    ");

internal sealed class ServiceContainer
{
    //CommaSeparateBuilder? interfaces = null;

    MemberBuilder?
        methods = null;

    DisposabilityBuilder?
        singletonDisposeStatments = null,
        disposeStatments = null;

    internal HashSet<string> externalAssemblies = new(StringComparer.OrdinalIgnoreCase);

    internal bool requiresSemaphore = false;

    bool //useIComma = false,
        hasScopedServices = false,
        requiresLocker = false;

    //internal bool hasAsyncService = false;

    internal readonly SemanticModel Model;

    internal readonly string GeneratorGuid;

    internal readonly string ProviderTypeName;

    internal readonly Compilation Compilation;

    internal readonly int ProviderId;

    internal readonly int CompilationId;

    internal readonly Set<Diagnostic> Diagnostics;

    internal readonly INamedTypeSymbol ProviderClass;

    readonly ImmutableArray<AttributeData> Attributes;

    internal readonly Set<InvokeInfo> ServiceCalls;

    internal readonly HashSet<(string?, string)> InterfacesRegistry = [];

    internal HashSet<string> MethodsRegistry = new(StringComparer.Ordinal);

    internal readonly DependencyMap ServicesMap = new(new DependencyComparer<int>());

    internal readonly DependencyNamesMap MethodNamesMap = new(new DependencyComparer<string>());

    internal Disposability Disposability = 0;

    internal LockerTypes lockerTypes;

    internal ServiceContainer(
        Compilation compilation,
        SemanticModel model,
        INamedTypeSymbol providerClass,
        Set<Diagnostic> diagnostics,
        ImmutableArray<AttributeData> externals,
        string generatorGuid,
        Set<InvokeInfo> serviceCalls)
    {
        ProviderClass = providerClass;
        Compilation = compilation;
        ProviderId = SymbolEqualityComparer.Default.GetHashCode(providerClass);
        CompilationId = compilation.GetHashCode();
        Model = model;
        Diagnostics = diagnostics;
        GeneratorGuid = generatorGuid;
        ServiceCalls = serviceCalls;
        ProviderTypeName = ProviderClass.ToGlobalNamespaced();
        Attributes = ProviderClass.GetAttributes();

        foreach (var attr in externals.Concat(Attributes))
        {
            if (!Model.TryGetDependencyInfo(attr, externalAssemblies, "", null, out var depInfo)) continue;

            if (depInfo.IsAsync)
            {
                if (depInfo.Lifetime is not Lifetime.Transient)
                    lockerTypes |= depInfo.Lifetime is Lifetime.Singleton ? LockerTypes.StaticSemaphore : LockerTypes.Semaphore;

                if (!requiresSemaphore) UpdateAsyncStatus();

                if (depInfo.FactoryKind is SymbolKind.Method
                    && !((IMethodSymbol)depInfo.Factory!).Parameters.Any(p => p.Type.ToDisplayString() is CancelTokenFQMetaName))
                {
                    Diagnostics.TryAdd(ServiceContainerGeneratorDiagnostics.CancellationTokenShouldBeProvided(depInfo.Factory, depInfo.AttrSyntax));
                }
            }
            else if (depInfo.Lifetime is not Lifetime.Transient)
            {
                lockerTypes |= depInfo.Lifetime is Lifetime.Singleton ? LockerTypes.StaticLock : LockerTypes.Lock;
            }

            Disposability thisDisposability = Disposability.None;

            if (depInfo.IsCached)
            {
                thisDisposability = depInfo.Type.GetDisposability();

                if (thisDisposability > Disposability) Disposability = thisDisposability;

                if (depInfo.Disposability > Disposability) Disposability = depInfo.Disposability;
            }

            var type = depInfo.Type ?? depInfo.FinalType;
            var typeName = type.ToGlobalNamespaced();
            var exportTypeFullName = depInfo.InterfaceType?.ToGlobalNamespaced() ?? typeName;
            var exportTypeHashCode = SymbolEqualityComparer.Default.GetHashCode(depInfo.InterfaceType ?? type);

            ref var existingOrNew = ref ServicesMap.GetValueRefOrAddDefault((depInfo.Lifetime, exportTypeHashCode, depInfo.KeyHash), out var exists)!;

            if (exists)
            {
                Diagnostics.TryAdd(
                    ServiceContainerGeneratorDiagnostics
                        .DuplicateService(depInfo.Lifetime, depInfo.Key, depInfo.AttrSyntax, typeName, exportTypeFullName));

                continue;
            }

            var (backingFieldName, methodName) = depInfo.GetMethodName(MethodsRegistry, MethodNamesMap);

            if (!depInfo.IsExternal && depInfo.Type!.IsPrimitive() && depInfo.Key is "")
            {
                Diagnostics.TryAdd(
                    ServiceContainerGeneratorDiagnostics
                        .PrimitiveDependencyShouldBeKeyed(depInfo.Lifetime, depInfo.AttrSyntax, typeName, exportTypeFullName));
            }

            existingOrNew = new(depInfo.FinalType, depInfo.Key, depInfo.InterfaceType)
            {
                ServiceContainer = this,
                OriginDefinition = depInfo.AttrSyntax,
                Lifetime = depInfo.Lifetime,
                Key = depInfo.Key,
                IsExternal = depInfo.IsExternal,
                FullTypeName = typeName,
                ExportTypeName = (depInfo.InterfaceType ?? depInfo.Type ?? depInfo.FinalType).ToGlobalNamespaced(),
                ResolverMethodName = methodName,
                CacheField = backingFieldName,
                Factory = depInfo.Factory,
                FactoryKind = depInfo.FactoryKind,
                Disposability = (Disposability)Math.Max((byte)thisDisposability, (byte)depInfo.Disposability),
                IsResolved = true,
                Attributes = depInfo.Type!.GetAttributes(),
                RequiresDisposabilityCast = thisDisposability is Disposability.None && depInfo.Disposability is not Disposability.None,
                IsAsync = depInfo.IsAsync,
                ContainerType = providerClass,
                IsCached = depInfo.IsCached,
                Params = depInfo.Type.GetParameters(),
                DefaultParamValues = depInfo.DefaultParamValues
            };

#if DISG
            Trace.WriteLine($"SCDI: Found from attrs: {existingOrNew.FullTypeName}:{SymbolEqualityComparer.Default.GetHashCode(existingOrNew.Type)}");
#endif
            ResolveService(existingOrNew);
        }

        /*foreach (var item in servicesMap.Values) ResolveService(item)*/
        ;
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
            if (service.IsAsync)
            {
                if (service.Lifetime is not Lifetime.Transient)
                    lockerTypes |= service.Lifetime is Lifetime.Singleton ? LockerTypes.StaticSemaphore : LockerTypes.Semaphore;

                lockerTypes |= service.Lifetime is Lifetime.Singleton ? LockerTypes.StaticLock : LockerTypes.Lock;

                if (!requiresSemaphore) UpdateAsyncStatus();
            }
            else if (!requiresLocker)
            {
                if (service.Lifetime is not Lifetime.Transient)
                    lockerTypes |= service.Lifetime is Lifetime.Singleton ? LockerTypes.StaticSemaphore : LockerTypes.Semaphore;

                lockerTypes |= service.Lifetime is Lifetime.Singleton ? LockerTypes.StaticLock : LockerTypes.Lock;

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

        if (service.Disposability > Disposability) Disposability = service.Disposability;

        if (service is { IsExternal: true } or { IsFactory: true, IsCached: false } or { IsSimpleTransient: true })
        {
            return;
        }

        methods += service.BuildMethod;
    }

    internal void UpdateAsyncStatus()
    {
        requiresSemaphore = true;

        if (Compilation.GetTypeByMetadataName(CancelTokenFQMetaName) is { } cancelType)
        {
            string cancelTypeName = cancelType.ToGlobalNamespaced();

            ServicesMap.TryInsert(
                (Lifetime.Singleton, SymbolEqualityComparer.Default.GetHashCode(cancelType), Helpers.EmptyStringHashCode),
                () => new(cancelType, "")
                {
                    Lifetime = Lifetime.Singleton,
                    ExportTypeName = cancelTypeName,
                    FullTypeName = cancelTypeName,
                    ServiceContainer = this,
                    IsResolved = true,
                    IsCancelTokenParam = true,
                });
        }
    }

    public void Build(
        DependencyMapDictionary containers,
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

        Disposability disposability = 0;
        bool hasDisposableScoped = false;

        foreach (var value in ServicesMap.Values)
        {
            if (value is not { IsCached: true, Lifetime: < Lifetime.Transient, Disposability: > Disposability.None }) continue;

            if (value.Disposability > disposability) disposability = value.Disposability;

            if (!hasDisposableScoped && value is { IsCached: true, Lifetime: Lifetime.Scoped }) hasDisposableScoped = true;
        }

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

        if (lockerTypes.HasFlag(LockerTypes.Lock))
        {
            code.Append(@"
    private readonly ").Append(net9Lock ?? "object").Append(@" __lock = new ();
");
        }

        if (lockerTypes.HasFlag(LockerTypes.StaticLock))
        {
            code.Append(@"
    private static readonly ").Append(net9Lock ?? "object").Append(@" ___lock = new ();
");
        }

        if (lockerTypes.HasFlag(LockerTypes.Semaphore))
        {
            code.Append(@"
    private readonly global::System.Threading.SemaphoreSlim __globalSemaphore = new (1, 1);

    private global::System.Threading.CancellationTokenSource __globalCancellationTokenSrc = new ();
");
        }

        if (lockerTypes.HasFlag(LockerTypes.StaticSemaphore))
        {
            code.Append(@"
    private static readonly global::System.Threading.SemaphoreSlim ___globalSemaphore = new (1, 1);

    private static global::System.Threading.CancellationTokenSource ___globalCancellationTokenSrc = new ();
");
        }

        methods?.Invoke(code, true);

        BuildDisposabilityMethods(code, disposability, hasDisposableScoped);

        if (hasScopedServices)

            code.Append(@"
    private bool isScoped = false;

    public ")
            .Append(typeName)
            .Append(@" CreateScope() => new ").Append(typeName).Append(@" { isScoped = true };
");

        var codeStr = code.Append('}').ToString();

        addSource(fileName + ".g", codeStr);
    }

    private void BuildDisposabilityMethods(StringBuilder code, Disposability disposability, bool hasDisposableScoped)
    {
        if (disposability is not Disposability.None)
        {
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

                        if (lockerTypes.HasFlag(LockerTypes.Semaphore))
                        {
                            code.Append(@"
            __globalSemaphore.Dispose();");
                        }

                        code.Append(@"
        }
        else
        {");

                        singletonDisposeStatments(code);

                        if (lockerTypes.HasFlag(LockerTypes.StaticSemaphore))
                        {
                            code.Append(@"
            ___globalSemaphore.Dispose();");
                        }

                        code.Append(@"
        }");

                        break;

                    case ({ }, null):

                        disposeStatments(code, null);

                        if (lockerTypes.HasFlag(LockerTypes.Semaphore))
                        {
                            code.Append(@"
            __globalSemaphore.Dispose();");
                        }

                        break;

                    case (null, { }):

                        if (hasScopedServices)
                        {
                            code.Append(@"
        if(isScoped) return;
");
                        }

                        singletonDisposeStatments(code, null);

                        if (lockerTypes.HasFlag(LockerTypes.StaticSemaphore))
                        {
                            code.Append(@"
            ___globalSemaphore.Dispose();");
                        }

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
                code.Append(@"
                ___globalSemaphore.Dispose();");
            }

            code.Append(@"
    }
");
        }
    }
}

[Flags]
internal enum LockerTypes
{
    None,
    StaticLock,
    Lock,
    Semaphore,
    StaticSemaphore
}

internal sealed class DependencyComparer<T> : IEqualityComparer<(Lifetime Lifetime, int Type, T Key)> where T : IEquatable<T>
{
    public bool Equals((Lifetime Lifetime, int Type, T Key) x, (Lifetime Lifetime, int Type, T Key) y)
    {
        return x.Lifetime.Equals(y.Lifetime)
            && x.Type.Equals(y.Type)
            && x.Key.Equals(y.Key);
    }

    public int GetHashCode((Lifetime Lifetime, int Type, T Key) obj)
    {
        return obj.GetHashCode();
    }
}

internal record InvokeInfo(int ContainerTypeId, string Name, IdentifierNameSyntax MethodSyntax, bool NotFromScopedInstance);

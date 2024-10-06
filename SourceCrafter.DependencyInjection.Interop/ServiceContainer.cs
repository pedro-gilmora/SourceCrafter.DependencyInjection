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

    //readonly Map<ServiceDescriptor, Action<StringBuilder>> keyedMethods = new(new KeyedServiceComparer());

    CommaSeparateBuilder? interfaces = null;

    MemberBuilder?
        methods = null;

    Action<StringBuilder, string>?
        singletonDisposeStatments = null,
        disposeStatments = null;

    internal bool requiresSemaphore = false;

    bool //useIComma = false,
        hasScopedService = false,
        requiresLocker = false/*,
        hasAsyncService = false*/;

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
                ref disposability,
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
        ref Disposability disposability,
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

            existingOrNew = new(depInfo.FinalType, exportTypeFullName, depInfo.Key, depInfo.IFaceType)
            {
                ServiceContainer = this,
                OriginDefinition = attrSyntax,
                Lifetime = lifetime,
                Key = depInfo.Key,
                IsExternal = isExternal,
                FullTypeName = typeName,
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

        if (foundService.Lifetime is Lifetime.Scoped && !hasScopedService) hasScopedService = true;

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
                () => new(cancelType, cancelTypeName, "")
                {
                    ServiceContainer = this,
                    IsResolved = true,
                    IsCancelTokenParam = true
                });
        }
    }

    //TODO: add cancel token

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
        //StringBuilder icode = new(@"#nullable enable
        //");
        var fileName = _providerClass.ToMetadataLongName(uniqueName);

        if (_providerClass.ContainingNamespace is { IsGlobalNamespace: false } ns)
        {
            code.Append("namespace ")
                .Append(ns.ToDisplayString()!)
                .Append(@";

");
            //            icode.Append("namespace ")
            //                .Append(ns.ToDisplayString()!)
            //                .Append(@";

            //");
        }

        var (modifiers, typeName) = _providerClass.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() switch
        {
            ClassDeclarationSyntax { Modifiers: var mods, Keyword: { } keyword, Identifier: { } identifier, TypeParameterList: var argList } =>
                ($"{mods} {keyword}".TrimStart(), $"{identifier}{argList}"),
            _ => ("partial class ", "")
        };

        //        #region Generate Container Interface

        //        icode.AppendLine()
        //            .Append("public interface I")
        //            .Append(typeName);

        //        BuildDisposability(icode, true);

        //        methods?.Invoke(icode, false, _generatorGuid);

        //        if (hasScopedService)
        //        {
        //            icode.Append(@"
        //    ")
        //                .AppendLine(_generatorGuid)
        //                .Append("    ")
        //                .Append(_providerClass.ContainingNamespace.ToGlobalNamespaced())
        //                .Append(".I")
        //                .Append(typeName)
        //                .Append(@" CreateScope();");
        //        }

        //        icode.Append(@"
        //}");

        //        addSource("I" + fileName + ".generated", icode.ToString());

        //        #endregion

        code.AppendLine(_generatorGuid)
            .Append(modifiers)
            .AddSpace()
            .Append(typeName);

        BuildDisposability(code, true);

        code
            //.Append(@" : ")
            //.Append('I')
            //.Append(typeName
//{)
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

        if (hasScopedService)
        {
            code.Append(@"
    private bool isScoped = false;

    ")
                .AppendLine(_generatorGuid)
                .Append(@"    public ")
                .Append(typeName)
                .Append(@" CreateScope() =>
		new ").Append(providerTypeName).Append(@" { isScoped = true };
");
        }

        methods?.Invoke(code, true, _generatorGuid);

        BuildDisposability(code, false);

        var codeStr = code.Append('}').ToString();

        addSource(fileName + ".generated", codeStr);

        //ChueckUsages(usages);
    }

    private void BuildDisposability(StringBuilder code, bool buildingInterface)
    {
        if (disposability > 0)
        {
            switch (disposability, buildingInterface)
            {
                case (Disposability.Disposable, true):

                    code.Append(@" : global::System.IDisposable	
{");

                    break;

                case (Disposability.AsyncDisposable, true):

                    code.Append(@" : global::System.IAsyncDisposable	
{");

                    break;

                case (Disposability.Disposable, false):

                    code.Append(@"
    ").AppendLine(_generatorGuid)
                        .Append(@"    public void Dispose()
	{");

                    break;

                case (Disposability.AsyncDisposable, false):

                    code.Append(@"
    ").AppendLine(_generatorGuid)
                        .Append(@"    public async global::System.Threading.Tasks.ValueTask DisposeAsync()
    {");

                    break;

                default :
                    
                    if(buildingInterface) code.Append(@"
{");

                    break;
            }

            if (!buildingInterface)
            {
                if (hasScopedService)
                {
                    code.Append(@"
		if(isScoped)
        {");

                    disposeStatments?.Invoke(code, "   ");

                    if (singletonDisposeStatments is null)
                    {
                        code.Append(@"
		}");
                    }
                    else
                    {
                        code.Append(@"
		}
		else
        {");

                        singletonDisposeStatments?.Invoke(code, "    ");

                        code.Append(@"
		}");
                    }

                }
                else
                {
                    singletonDisposeStatments?.Invoke(code, "");
                }

                code.Append(@"
	}
");
            }
        }
        else if (buildingInterface)
        {
            code.Append(@"	
{");
        }
    }

    /*
    private void CheckUsages(ImmutableArray<InvocationExpressionSyntax> usages)
    {
        foreach (var invExpr in usages)
        {
            bool found = false;

            if (((GenericNameSyntax)((MemberAccessExpressionSyntax)invExpr.Expression).Name)
                    .TypeArgumentList
                    .Arguments
                    .FirstOrDefault() is not { } type)

                continue;

            var contextModel = _compilation.GetSemanticModel(invExpr.SyntaxTree);

            var refType = contextModel.GetSymbolInfo(((MemberAccessExpressionSyntax)invExpr.Expression).Expression).Symbol switch
            {
                ILocalSymbol { Type: { } rType } => rType,
                IFieldSymbol { Type: { } rType } => rType,
                IPropertySymbol { Type: { } rType } => rType,
                _ => null
            };

            if (refType is null || !SymbolEqualityComparer.Default.Equals(refType, _providerClass)) continue;

            var typeSymbol = contextModel.GetTypeInfo(type).Type;

            if (typeSymbol is null) continue;

            var typeFullName = typeSymbol.ToGlobalNamespaced();

            IFieldSymbol? key = null;
            ITypeSymbol? keyType = null;

            if (invExpr.ArgumentList.Arguments is [{ Expression: { } keyArg } arg])
            {
                keyType = contextModel.GetTypeInfo(keyArg).Type;

                if (keyType?.TypeKind is not TypeKind.Enum)
                {
                    _diagnostics.TryAdd(
                        ServiceContainerGeneratorDiagnostics.InvalidKeyType(arg.Expression));

                    continue;
                }
                else if (contextModel.GetSymbolInfo(keyArg).Symbol is IFieldSymbol { IsConst: true } fieldValue)
                {
                    key = fieldValue;
                }
            }

            discoveredServices.ForEach((DependecyKey itemK, ref ServiceDescriptor item) =>
            {
                if (item.ExportTypeName == typeFullName
                    && ((item.Key, key) switch
                    {
                        ({ } itemKey, { }) => SymbolEqualityComparer.Default.Equals(itemK.Key, key),
                        (var itemKey, _) => itemKey is null || SymbolEqualityComparer.Default.Equals(itemKey.Type, keyType)
                    })
                    && item.IsResolved)
                {
                    found = true;
                }
            });

            if (!found)
            {
                _diagnostics.TryAdd(
                    ServiceContainerGeneratorDiagnostics.UnresolvedDependency(invExpr, providerClassName, typeFullName, keyType, key));
            }
        }
    }
    */
}
internal record InvokeInfo(ITypeSymbol ContainerType, string Name, IdentifierNameSyntax MethodSyntax, bool IsNotScoped);

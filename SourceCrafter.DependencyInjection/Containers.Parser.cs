using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualBasic;
using SourceCrafter.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

#pragma warning disable CA1050 // Declarar tipos en espacios de nombres
internal partial class Containers
#pragma warning restore CA1050 // Declarar tipos en espacios de nombres
{
    private static Emitter TryParseContainer(
        in GeneratorAttributeSyntaxContext gasc,
        in CancellationToken cancelToken)
    {
        SemanticModel model = gasc.SemanticModel;
        INamedTypeSymbol providerType = (INamedTypeSymbol)gasc.TargetSymbol;
        HashSet<Diagnostic> diagnostics = [];
        var providerFullTypeName = providerType.FullGlobalQualifiedName;
        var isInterfaceProvider = providerType.TypeKind == TypeKind.Interface;
        var providerTypeName = providerType.TypeNameFormat;
        var className = isInterfaceProvider ? providerTypeName[1..] : providerTypeName;
        var fullProviderImplName = providerType.ContainingNamespace.FullGlobalQualifiedName + '.' + className;
        var attributes = providerType.GetAttributes();

        bool
            hasScopedDependencies = false,
            useInterceptors = providerType.AllInterfaces.Any(i => i.FullGlobalQualifiedName == "global::System.IServiceProvider");

        HashSet<string> externalAssemblies = [];

        var defaultKeyComparer = EqualityComparer<FirstLevelDependencyKey>.Default;
        var defaultSubKeyComparer = EqualityComparer<DependencyKey>.Default;

        DependencyDictionary dependencyValueBuilders = [];
        Dictionary<DependencyKey, string> methodNamesMap = new(defaultSubKeyComparer);
        HashSet<string> methodsRegistry = [];
        Dictionary<DependencyKey, MemberBuilder> dependencyMemberBuilders = [];
        Dictionary<FirstLevelDependencyKey, Interceptor> interceptors = new(defaultKeyComparer);

        Disposability
            containerDisposability = Disposability.None,
            scopedDisposability = Disposability.None;
        int
            asyncScopedDisposable = 0,
            asyncSingletonDisposable = 0,
            asyncScopedAsyncDisposable = 0,
            asyncSingletonAsyncDisposable = 0,
            scopedDisposable = 0,
            singletonDisposable = 0,
            scopedAsyncDisposable = 0,
            singletonAsyncDisposable = 0;

        var (modifiers, typeName) = providerType.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancelToken) switch
        {
            ClassDeclarationSyntax { Modifiers: var mods, Keyword: { } keyword, Identifier: { } identifier, TypeParameterList: var typeParamsList } =>
                ($"{mods} {keyword}".TrimStart(), $"{identifier}{typeParamsList}"),
            InterfaceDeclarationSyntax { Modifiers: var mods, Identifier: { } identifier, TypeParameterList: var typeParamsList } =>
                ($"{mods/*.Except([SyntaxFactory.Token(SyntaxKind.InterfaceKeyword)])*/} partial class".TrimStart(), $"{identifier.ValueText[1..]}{typeParamsList}"),
            _ => ("", "")
        };

        ResolverBuilder selfDepInfo = new($"{providerFullTypeName}")
        {
            Key = (Lifetime.Singleton, providerFullTypeName, ""),
            AppendValue = (_, _, _) => { }
        };

        string envName = "";

        foreach (var attr in attributes)
        {
            TryRegisterService(attr, null, out _, cancelToken);
        }

        if (dependencyValueBuilders.Count == 0) return null!;

        var nameSpace = providerType.ContainingNamespace is { } ns ? ns.ToDisplayString() : null;

        Emitter emitter = new(
            providerType.MetadataLongName,
            nameSpace,
            diagnostics,
            providerFullTypeName,
            isInterfaceProvider,
            providerType.AllInterfaces.Any(i => i.GlobalNamespaced == "global::System.IServiceProvider"),
            className,
            dependencyValueBuilders,
            dependencyMemberBuilders,
            interceptors,
            containerDisposability,
            scopedDisposability,
            asyncScopedDisposable,
            asyncSingletonDisposable,
            asyncScopedAsyncDisposable,
            asyncSingletonAsyncDisposable,
            scopedDisposable,
            singletonDisposable,
            scopedAsyncDisposable,
            singletonAsyncDisposable,
            modifiers,
            typeName,
            envName);

        foreach (var item in dependencyValueBuilders.Values)
            foreach (var resolver in item.Values)
                emitter.genericResolvers.Add(resolver);

        diagnostics = null!;
        dependencyValueBuilders = null!;
        dependencyMemberBuilders = null!;
        interceptors = null!;

        return emitter;

        bool TryRegisterService(AttributeData? attr, ISymbol? sourceSymbol, out ResolverBuilder? resolver, CancellationToken cancelToken, ChildDependencyHandler? validateAsChildDependency = null)
        {
            (SymbolKind sourceKind, ITypeSymbol? sourceType) = sourceSymbol switch
            {
                IParameterSymbol { Type: ITypeSymbol factoryType } => (SymbolKind.Parameter, factoryType),
                IPropertySymbol { Type: ITypeSymbol factoryType } => (SymbolKind.Property, factoryType),
                IFieldSymbol { Type: ITypeSymbol factoryType } => (SymbolKind.Field, factoryType),
                IMethodSymbol { ReturnType: ITypeSymbol factoryType } => (SymbolKind.Method, factoryType),
                _ => (SymbolKind.Discard, null)
            };

            bool
                isSimpleTransient = false,
                isExternal = false,
                isCached = false,
                isValid = false,
                isFactory = false,
                hasAsyncDependencies = false,
                isStaticFactory = false,
                isFactoryFromCurrentProvider = false,
                isFactoryIndexerProperty = false,
                useWhenAll = false,
                needsCancelToken = false;

            string
                name = string.Empty,
                whenAll = string.Empty,
                exportTypeFullName = string.Empty,
                typeFullName = string.Empty,
                factoryProviderName = string.Empty;

            string?
                nameOrFormat = null,
                factoryName = null,
                interfaceFullTypeName = null;

            AsyncKind
                AsyncKind = default,
                initialAsyncType = default;

            FirstLevelDependencyKey key = default;
            DependencyKey subKey = default;

            ImmutableArray<IParameterSymbol>
                defaultParamValues = [],
                prms = [];
            Disposability
                disposability = default;
            AttributeSyntax
                attrSyntax = null!;
            INamedTypeSymbol?
                attrClass;
            Lifetime
                lifetime = default;
            ITypeSymbol?
                interfaceType = null;
            ISymbol?
                factory = null;
            SymbolKind
                factoryKind = default;
            Dictionary<DependencyKey, AsyncLocalResolver>
                asyncLocalResolvers = new(defaultSubKeyComparer);
            List<ParamBuildOptions>
                appendParams = [];
            ITypeSymbol
                exportType = null!,
                type = null!;

            resolver = null!;

            var (backingFieldName, methodName) = ("", "");

            if (!IsValidServiceAttribute(attr, cancelToken))
            {
                //(lifetime, exportType?.ToDisplayString(), type?.ToDisplayString(), name, false).Dump("Checking:");
                return false;
            }

            //(lifetime, interfaceType?.ToDisplayString(), type.ToDisplayString(), name).Dump("Checking:");

            var deepParamsCount = 0;
            var services = CollectionsMarshal.GetValueRefOrAddDefault(dependencyValueBuilders, key, out var exportSignatureExists) ??= [];

            // hecks the implementation existence
            if (type is null && exportSignatureExists)
            {
                (backingFieldName, methodName) = GetResolverName();
                //new { lifetime, name, exportType }.Dump("Duplicated service:");
                diagnostics.Add(
                    ServiceContainerDiagnostics
                        .DuplicateService(lifetime, name, attrSyntax, typeFullName, exportTypeFullName));

                return false;
            }

            bool hasNoCachedDeps = lifetime is Lifetime.Transient;

            // Takes or create a new Resolver instance
            resolver = CollectionsMarshal.GetValueRefOrAddDefault(services, subKey, out var implExists) ??=
                new($"{lifetime} {(exportTypeFullName + (exportTypeFullName == typeFullName ? null : $"<{typeFullName}>"))} {name}".Trim())
                {
                    Key = subKey,
                    ExportTypeFullName = exportTypeFullName,
                    AppendValue = AppendValue,
                    AsyncKind = AsyncKind,
                    ParamsLength = prms.Length,
                    TransientWithoutCachedDeps = isSimpleTransient
                };

            // If it comes from params check, validates the symbols as dependency
            if (validateAsChildDependency?.Invoke(
                    exportSignatureExists,
                    isValid,
                    lifetime,
                    implExists ? resolver.AsyncKind : AsyncKind,
                    implExists ? resolver.ParamsLength : prms.Length,
                    AppendValue,
                    type is null,
                    isExternal && name is "") is false)
            {
                return false;
            }

            asyncLocalResolvers = resolver.AsyncLocalResolvers;

            disposability = type!.GetDisposability();

            // Determines disposability at instance or scoped instance level
            if (isCached)
            {
                if (lifetime is Lifetime.Scoped && disposability > scopedDisposability)
                    scopedDisposability = disposability;
                else if (disposability > containerDisposability)
                    containerDisposability = disposability;
            }

            // Internal primitive value service providers must be wkeyed
            if (!isExternal && (sourceType ?? type!).IsPrimitive() && name is "")
            {
                //exportTypeFullName.Dump($"Primitive {lifetime} type should be keyed:");
                diagnostics.Add(
                    ServiceContainerDiagnostics
                        .PrimitiveDependencyMustBeKeyed(lifetime, attrSyntax, typeFullName, exportTypeFullName));
            }

            //$"{key}: {existingOrNewValueBuilder}".Dump();

            if (isSimpleTransient)
            {
                (backingFieldName, methodName) = GetResolverName();
                //TryRegisterInterceptorMethod();
                return true;
            }

            var paramsToResolve = prms.Length;

            byte paramPos = 0/*, valueTaskCount = 0, asyncParamCount = 0*/;

            Dictionary<int, HashSet<AsyncLocalResolver>> asyncParams = [];

            foreach (var prm in prms)
            {
                var paramIndex = paramPos++;
                var paramAsyncType = prm.Type.TryGetAsyncType(out var paramType);

                var getServices = false;
                if (!paramType.IsPrimitive())
                {
                    if (paramType.AllInterfaces.FirstOrDefault(i => i.SpecialType is
                        SpecialType.System_Collections_Generic_IEnumerable_T or
                        SpecialType.System_Collections_Generic_ICollection_T or
                        SpecialType.System_Collections_Generic_IReadOnlyCollection_T or
                        SpecialType.System_Collections_Generic_IList_T) is { TypeArguments: [{ } _type] })
                    {
                        getServices = true;
                        paramType = _type;
                    }
                    else if (paramType is IArrayTypeSymbol { ElementType: { } _type1 })
                    {
                        getServices = true;
                        paramType = _type1;
                    }
                }

                var paramFullTypeName = paramType.FullGlobalQualifiedName;
                var paramTypeHashCode = paramFullTypeName.GetHashCode();

                if (paramType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == className || SymbolEqualityComparer.Default.Equals(paramType, providerType))
                {
                    var asksForRoot = prm.GetAttributes().Any(a => a.AttributeClass?.FullGlobalQualifiedName == $"{GlobalBaseAttributeNS}.RootAttribute");
                    appendParams.Add(new(selfDepInfo.Key, AppendProvider));
                    continue;
                    void AppendProvider(StringBuilder code, bool _, bool __) => code.Append(asksForRoot ? "Root" : "this");
                }

                if (Equals(prm.Type.FullGlobalQualifiedName, CancelTokenFQMetaName))
                {
                    needsCancelToken = true;
                    resolver.PassCancelToken = true;
                    appendParams.Add(new(selfDepInfo.Key, AppendCancelToken));
                    continue;
                }

                var isPrimitiveParamType = paramType.IsPrimitive();

                SubDependencyDictionary foundServices = null!;
                ResolverBuilder foundService = null!;
                var foundAsyncType = AsyncKind.None;
                HashSet<(int, bool)> existingAsyncDepsCalls = [];
                DependencyKey resolvedSubKey = default;

                if (prm.GetAttributes() is { Length: > 0 } paramAttrs)
                {
                    foreach (var paramAttr in paramAttrs)
                    {
                        if (TryRegisterService(paramAttr, prm, out foundService!, cancelToken, ValidateChildParameterDependency))
                        {
                            break;
                        }
                    }
                }

                var paramName = prm.Name;
                var paramKeyHash = paramName.GetHashCode();

                if ((foundService is not null
                        || dependencyValueBuilders.TryGetValue((paramFullTypeName, paramName), out foundServices!)
                        || dependencyValueBuilders.TryGetValue((paramFullTypeName, ""), out foundServices!)
                    && foundServices.Count > 0))
                {
                    if (getServices)
                    {
                        int i = 0, len = foundServices.Values.Count - 1;
                        foreach (var service in foundServices.Values)
                        {
                            if (hasNoCachedDeps && !service.TransientWithoutCachedDeps)
                                hasNoCachedDeps = false;

                            resolvedSubKey = (foundService = service).Key;
                            CreateParamResolverBuilder(foundService, i == 0, i++ == len);
                        }
                    }
                    else if (lifeTimes.Any(
                        paramLifeTime => (foundService = foundServices.LastOrDefault(
                            e => e.Key.lifetime == paramLifeTime && (e.Key.key == paramName || e.Key.key == "")).Value) is not null))
                    {
                        if (hasNoCachedDeps && !foundService!.TransientWithoutCachedDeps)
                            hasNoCachedDeps = false;

                        CreateParamResolverBuilder(foundService!);
                    }

                    void CreateParamResolverBuilder(ResolverBuilder foundService, bool startsCollectionExpression = false, bool endsCollectionExpression = false)
                    {
                        if (foundService.PassCancelToken && !needsCancelToken)
                            needsCancelToken = true;

                        var AppendParam = foundService!.AppendValue;
                        var foundExportTypeFullName = foundService.ExportTypeFullName;

                        appendParams.Add(new(foundService.Key, (code, asyncContext, _) =>
                        {
                            var awaits = asyncContext && foundService.AsyncKind is not 0 && paramAsyncType is 0;

                            if (awaits)
                            {
                                code.Append("await ");
                                AppendParam(code, asyncContext);
                            }
                            else if (paramAsyncType is not 0 && foundService.AsyncKind is 0)
                            {
                                if (paramAsyncType is AsyncKind.Task)
                                {
                                    code.Append("global::System.Threading.Tasks.Task.FromResult<")
                                        .Append(foundExportTypeFullName)
                                        .Append(">(");
                                    AppendParam(code, asyncContext);
                                    code.Append(')');
                                }
                                else
                                {
                                    code.Append("new global::System.Threading.Tasks.ValueTask<")
                                        .Append(foundExportTypeFullName)
                                        .Append(">(");
                                    AppendParam(code, asyncContext);
                                    code.Append(')');
                                }
                            }
                            else if (foundService.AsyncKind is AsyncKind.ValueTask && paramAsyncType is AsyncKind.Task)
                            {
                                AppendParam(code, asyncContext);
                                code.Append(".AsTask()");
                            }
                            else if (foundService.AsyncKind is AsyncKind.Task && paramAsyncType is AsyncKind.ValueTask)
                            {
                                code.Append("new global::System.Threading.Tasks.ValueTask<")
                                    .Append(foundExportTypeFullName)
                                    .Append(">(");
                                AppendParam(code, asyncContext);
                                code.Append(')');
                            }
                            else
                            {
                                AppendParam(code, asyncContext);
                            }
                        }, startsCollectionExpression, endsCollectionExpression));

                        deepParamsCount += foundService.ParamsLength;
                        foundAsyncType = foundService.AsyncKind;

                        if (foundAsyncType is not 0)
                        {
                            if (AsyncKind is 0)
                                AsyncKind = AsyncKind.Task;

                            if (!hasAsyncDependencies)
                                hasAsyncDependencies = true;

                            AsyncLocalResolver resolved = null!;

                            if (paramAsyncType is 0)
                            {
                                if (asyncLocalResolvers.TryGetValue(foundService.Key, out resolved!))
                                {
                                    var isCachedResolved = foundService.Key.lifetime is not Lifetime.Transient;
                                    resolved.AppendAsyncLocal = AppendAsyncLocalResolver;

                                    if (resolved.ParamIndex == -1 || isCachedResolved)
                                        resolved.ParamIndex = paramIndex;

                                    if (!resolved.ResolvedBefore && resolved.ParamIndex > resolved.ResolvedByParamIndex)
                                        resolved.ResolvedBefore = isCachedResolved;
                                }
                                else
                                {
                                    asyncLocalResolvers.TryAdd(foundService.Key, resolved = new(foundService.Key)
                                    {
                                        ResolvedByParamIndex = paramIndex,
                                        ParamIndex = paramIndex,
                                        IsValueTask = foundAsyncType is AsyncKind.ValueTask,
                                        ResolverDep = foundService.Key,
                                        AppendAsyncLocal = AppendAsyncLocalResolver
                                    });
                                }
                            }

                            foreach (var childValue in foundService.AsyncLocalResolvers.Values)
                            {
                                asyncLocalResolvers.TryAdd(childValue.DepKey, resolved = new(childValue.DepKey)
                                {
                                    IsValueTask = childValue.IsValueTask,
                                    ResolvedByParamIndex = paramIndex,
                                    ResolverDep = foundService.Key,
                                    ResolvedBefore = !childValue.IsValueTask && childValue.DepKey.lifetime is not Lifetime.Transient,
                                    AppendAsyncLocal = AppendAsyncLocalResolver
                                });
                            }

                            void AppendAsyncLocalResolver(StringBuilder code, string? indent)
                            {
                                code.Append(@"
		").Append(indent).Append("var __v").Append(paramIndex).Append(" = ");

                                AppendParam(code);

                                code.Append(';');
                            }

                        }
                    }
                }
                else if (paramType.TypeKind is not TypeKind.Interface)
                {
                    TryRegisterService(null, prm, out _, cancelToken, ValidateChildParameterDependency);
                }
                else
                {
                    appendParams.Add(new(resolvedSubKey, AppendDefault));
                }

                bool ValidateChildParameterDependency(
                    bool childExists,
                    bool isChildValid,
                    Lifetime childLifetime,
                    AsyncKind childAsyncType,
                    int childParamCount,
                    AppendValue AppendParam,
                    bool isNullChildType,
                    bool isUnkeyedInternalPrimitive)
                {
                    if (!hasAsyncDependencies && childAsyncType > 0) hasAsyncDependencies = true;

                    if (paramAsyncType is 0)
                    {
                        if (asyncLocalResolvers.TryGetValue(resolvedSubKey, out var resolved))
                        {
                            resolved.AppendAsyncLocal = AppendAsyncLocalResolver;
                        }
                        else
                        {
                            asyncLocalResolvers.TryAdd(resolvedSubKey, resolved = new(resolvedSubKey)
                            {
                                ResolvedByParamIndex = paramIndex,
                                IsValueTask = foundAsyncType is AsyncKind.ValueTask,
                                ResolverDep = resolvedSubKey,
                                AppendAsyncLocal = AppendAsyncLocalResolver
                            });
                        }

                        void AppendAsyncLocalResolver(StringBuilder code, string? indent)
                        {
                            code.Append(@"
			").Append(indent).Append("var __v").Append(paramIndex).Append(" = ");

                            AppendParam(code);

                            code.Append(';');
                        }
                    }

                    if (childAsyncType > AsyncKind)
                    {
                        AsyncKind = childAsyncType;
                    }

                    if (childExists)
                    {
                        childParamCount += childParamCount;
                        appendParams.Add(new(resolvedSubKey, AppendParam));
                        return false;
                    }
                    else if ((isNullChildType && isPrimitiveParamType) || (isUnkeyedInternalPrimitive && prm.Type.IsPrimitive()) || !isChildValid)
                    {
                        deepParamsCount += 1;
                        appendParams.Add(new(resolvedSubKey, AppendDefault));
                        return false;
                    }

                    return true;
                }
            }

            if (!resolver.PassCancelToken && needsCancelToken)
                resolver.PassCancelToken = true;

            if (appendParams.Count == 0)
            {
                isSimpleTransient = true;
            }
            else if (asyncLocalResolvers.Count > 0)
            {
                asyncLocalResolvers = asyncLocalResolvers.Values
                    .Where(lr => lr.ParamIndex > -1).OrderBy(lr => lr.ParamIndex)
                    .ToDictionary(i => i.DepKey);

                if (asyncLocalResolvers.Values
                    .Where(i => !i.IsValueTask && !i.ResolvedBefore && i.ParamIndex > -1)
                    .Select(i => "__v" + i.ParamIndex)
                    .ToArray() is { Length: > 1 } vars)
                {
                    whenAll = string.Join(", ", vars);
                }

                useWhenAll = asyncLocalResolvers.Values.Count(i => i.ParamIndex > -1 && !i.IsValueTask && !i.ResolvedBefore) > 2;
            }

            //if (asyncParamCount > 0 && valueTaskCount == asyncParamCount) AsyncKind = AsyncKind.ValueTask;

            (backingFieldName, methodName) = GetResolverName();

            //existingOrNewValueBuilder.AsyncNestedDeps.Dump($"Async deps for {existingOrNewValueBuilder}");

            if (disposability is not 0 && isCached)
            {
                switch (AsyncKind is not 0, lifetime, disposability)
                {
                    case (true, Lifetime.Scoped, Disposability.Disposable): asyncScopedDisposable++; break;
                    case (true, Lifetime.Singleton, Disposability.Disposable): asyncSingletonDisposable++; break;
                    case (true, Lifetime.Scoped, Disposability.AsyncDisposable): asyncScopedAsyncDisposable++; break;
                    case (true, Lifetime.Singleton, Disposability.AsyncDisposable): asyncSingletonAsyncDisposable++; break;
                    case (false, Lifetime.Scoped, Disposability.Disposable): scopedDisposable++; break;
                    case (false, Lifetime.Singleton, Disposability.Disposable): singletonDisposable++; break;
                    case (false, Lifetime.Scoped, Disposability.AsyncDisposable): scopedAsyncDisposable++; break;
                    case (false, Lifetime.Singleton, Disposability.AsyncDisposable): singletonAsyncDisposable++; break;
                }
            }


            if (!isExternal && (isCached || !isSimpleTransient))
                dependencyMemberBuilders
                    .TryAdd((lifetime, typeFullName, name),
                        new(lifetime, exportTypeFullName, name, AsyncKind, disposability, nameOrFormat) 
                        {
                            RequiresCancelToken = needsCancelToken,
                            BuildAndExpose = AppendMethod 
                        });

            resolver.TransientWithoutCachedDeps = hasNoCachedDeps;
            resolver.AsyncKind = AsyncKind;

            //TryRegisterInterceptorMethod();

            return true;

            bool IsValidServiceAttribute(AttributeData? attr, CancellationToken cancelToken)
            {
                var isContainerAttr = false;

                if (attr is not { AttributeClass: { } _attrClass, ApplicationSyntaxReference: { } attrSyntaxRef }
                    || (isContainerAttr = _attrClass.FullGlobalQualifiedName is ServiceContainerAttr)
                    || attrSyntaxRef.GetSyntax(cancelToken) is not AttributeSyntax { } _attrSyntax
                    || !TryGetAttributeParamsDefinition(model.GetSymbolInfo(_attrSyntax, cancellationToken: cancelToken), out ImmutableArray<IParameterSymbol> attrParams)
                    || !TryGetLifetime(_attrSyntax, ref _attrClass, ref isExternal, out lifetime))
                {
                    if (isContainerAttr)
                    {
                        envName = attr switch
                        {
                            { Syntax.ArgumentList.Arguments: [{ } arg, ..] } => arg switch
                            {
                                { Expression: LiteralExpressionSyntax { Token.ValueText: { } envString } } when envString.Trim() is { Length: > 0 } => $@"""{envString}""",
                                { Expression: MemberAccessExpressionSyntax { Name: { } member } } => model.GetSymbolInfo(member, cancellationToken: cancelToken) switch
                                {
                                    { Symbol: IFieldSymbol { } field } => field.GlobalNamespaced,
                                    _ => DefaultEnvName
                                },
                                { Expression: IdentifierNameSyntax member } => isInterfaceProvider ? $"{providerTypeName}.{member.Identifier.ValueText}" : member.Identifier.ValueText,
                                _ => DefaultEnvName
                            },
                            { ConstructorArguments: [{ Value: string v }, ..] } => $@"""{v}""",
                            _ => DefaultEnvName
                        };
                    }
                    return false;
                }

                attrSyntax = _attrSyntax;
                attrClass = _attrClass;

                if (isExternal = _attrClass.ContainingNamespace.ToDisplayString() != BaseAttributesNS)
                    externalAssemblies.Add(_attrClass.ContainingAssembly.MetadataName.Replace(".Metadata", ""));

                var foundInAttribute = false;

                if (attr.AttributeClass!.TypeArguments.Length > 0 is { } isGeneric)
                {
                    switch (_attrClass!.TypeArguments)
                    {
                        case [{ } t1, { } t2, ..]:
                            foundInAttribute = true;
                            interfaceType = t1;
                            interfaceFullTypeName = interfaceType.FullGlobalQualifiedName;
                            type = t2;
                            typeFullName = type.FullGlobalQualifiedName;
                            break;

                        case [{ } t1]:
                            foundInAttribute = true;
                            type = t1;
                            typeFullName = type.FullGlobalQualifiedName;

                            break;
                    }
                }
                ITypeSymbol? factoryReturnType = null;
                foreach (var (param, arg) in GetAttributeParamsMap(attrParams, _attrSyntax.ArgumentList?.Arguments ?? []))
                {
                    switch (param.Name)
                    {
                        case ImplParamName when type is null && !isGeneric 
                            && arg is { Expression: TypeOfExpressionSyntax { Type: { } _type } }:

                            type = (ITypeSymbol)model.GetSymbolInfo(_type, cancellationToken: cancelToken).Symbol!;
                            typeFullName = type.FullGlobalQualifiedName;

                            continue;

                        case IfaceParamName when 
                            interfaceType is null 
                            && !isGeneric 
                            && arg is { Expression: TypeOfExpressionSyntax { Type: { } type } }:

                            interfaceType = (ITypeSymbol)model.GetSymbolInfo(type, cancellationToken: cancelToken).Symbol!;
                            interfaceFullTypeName = interfaceType.FullGlobalQualifiedName;

                            continue;

                        case KeyParamName when GetStringExpressionOrValue(model, param!, arg, out var keyValue):

                            name = keyValue;

                            continue;

                        case NameFormatParamName when GetStringExpressionOrValue(model, param, arg, out var keyValue):

                            nameOrFormat = keyValue;

                            continue;

                        case SourceParamName

                            when arg?.Expression is InvocationExpressionSyntax
                            {
                                Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" },
                                ArgumentList.Arguments: [{ } methodRef]
                            }:

                            switch (model.GetSymbolInfo(methodRef.Expression, cancellationToken: cancelToken))
                            {
                                case { Symbol: (IFieldSymbol or IPropertySymbol) and { ContainingType: ITypeSymbol containingType, Kind: var kind, IsStatic: var isStatic } fieldOrProp }:

                                    CheckInnerFactorySpecs(fieldOrProp);

                                    factory = fieldOrProp;
                                    factoryName = factory.Name;
                                    factoryKind = kind;
                                    isFactory = true;
                                    isStaticFactory = isStatic;
                                    isFactoryFromCurrentProvider = SymbolEqualityComparer.Default.Equals(providerType, containingType);

                                    (AsyncKind, isFactoryIndexerProperty) = fieldOrProp switch
                                    {
                                        IPropertySymbol { Type: ITypeSymbol type, IsIndexer: var isIndexer } => (initialAsyncType = type.TryGetAsyncType(out factoryReturnType), isIndexer),
                                        _ => (((IFieldSymbol)fieldOrProp).Type.TryGetAsyncType(out factoryReturnType), false)
                                    };

                                    if (factoryReturnType is not { TypeKind: TypeKind.Interface, IsAbstract: true })
                                    {
                                        type ??= factoryReturnType;
                                        typeFullName = type.FullGlobalQualifiedName;
                                    }
                                    else
                                    {
                                        interfaceType ??= factoryReturnType;
                                        interfaceFullTypeName = interfaceType.FullGlobalQualifiedName;
                                    }

                                    factoryProviderName = isFactoryFromCurrentProvider ? providerTypeName : containingType.GlobalNamespaced;

                                    continue;

                                case { CandidateReason: CandidateReason.MemberGroup, CandidateSymbols: [IMethodSymbol { ContainingType: ITypeSymbol containingType, ReturnsVoid: false, IsStatic: var isStatic } method] }:

                                    CheckInnerFactorySpecs(method);

                                    factory = method;
                                    isStaticFactory = isStatic;
                                    factoryKind = SymbolKind.Method;
                                    defaultParamValues = method.Parameters;
                                    initialAsyncType = AsyncKind = method.ReturnType.TryGetAsyncType(out factoryReturnType);
                                    isFactory = true;
                                    isFactoryFromCurrentProvider = SymbolEqualityComparer.Default.Equals(providerType, containingType);
                                    factoryName = factory.Name;

                                    if (factoryReturnType is not { TypeKind: TypeKind.Interface, IsAbstract: true })
                                    {
                                        type ??= factoryReturnType;
                                        typeFullName = type.FullGlobalQualifiedName;
                                    }
                                    else
                                    {
                                        interfaceType ??= factoryReturnType;
                                        interfaceFullTypeName = interfaceType.FullGlobalQualifiedName;
                                    }

                                    factoryProviderName = isFactoryFromCurrentProvider ? providerTypeName : containingType.GlobalNamespaced;

                                    continue;
                            }

                            continue;

                            void CheckInnerFactorySpecs(ISymbol method)
                            {
                                if (!isInterfaceProvider
                                    && lifetime == Lifetime.Transient
                                    && (SymbolEqualityComparer.Default.Equals(method.ContainingType, providerType)
                                    || method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == className)
                                    && (method.DeclaredAccessibility != Accessibility.Private || !method.Name.StartsWith("_"))
                                        && method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancelToken)?.GetLocation() is Location location2)
                                {
                                    diagnostics.Add(ServiceContainerDiagnostics.ThrowInnerFactorySpecs(method.Name, location2));
                                }
                            }

                        //case "disposability" when param.HasExplicitDefaultValue:

                        //    disposability = (Disposability)(byte)param.ExplicitDefaultValue!;

                        //    continue;
                    }
                }

                if (interfaceType != null)
                {
                    var count = diagnostics.Count;
                    if (type is null)
                    {
                        if (factoryReturnType is null)
                            diagnostics.Add(ServiceContainerDiagnostics.InterfaceWithNoImplementation(attrSyntax.GetLocation(), interfaceType, providerFullTypeName, lifetime));
                    }
                    else if(!foundInAttribute 
                        && !HasBaseType(type, interfaceType)
                        && !type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, interfaceType)))

                            diagnostics.Add(ServiceContainerDiagnostics.BaseAndImplementationMissmatch(attrSyntax.GetLocation(), type, interfaceType));

                    if (factoryReturnType is not null)
                    {
                        var typeToMatch = type ?? interfaceType;
                        if (!SymbolEqualityComparer.Default.Equals(typeToMatch, factoryReturnType)
                            && !HasBaseType(typeToMatch, interfaceType)
                            && !typeToMatch.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, interfaceType)))
                        {
                            diagnostics.Add(ServiceContainerDiagnostics.FactoryReturnMismatch(factory!, interfaceType, factoryReturnType, attrSyntax));
                        }
                    }

                    if (count < diagnostics.Count)
                        return false;

                    static bool HasBaseType(ITypeSymbol type, ITypeSymbol baseType)
                    {
                        while (type != null)
                            if (SymbolEqualityComparer.Default.Equals(type, baseType))
                                return true;
                            else type = type.BaseType!;
                        return false;
                    }
                }

                exportType ??= interfaceType ?? type!;
                exportTypeFullName = interfaceFullTypeName ?? typeFullName;

                if (!(isValid = exportType is not null && type is not null && attrClass is not null && _attrSyntax is not null)) return ReturnNotFound();

                if (!hasScopedDependencies && lifetime is Lifetime.Scoped)
                {
                    hasScopedDependencies = true;
                }

                if (AsyncKind is not 0 && factoryKind is SymbolKind.Method && !((IMethodSymbol)factory!).Parameters.Any(p => p.Type.FullGlobalQualifiedName is CancelTokenFQMetaName))
                {
                    //factory.ToDisplayString().Dump("Cancellation token should be Appendd");
                    diagnostics.Add(ServiceContainerDiagnostics.CancellationTokenShouldBeProvided(factory, attrSyntax));
                }

                key = (exportTypeFullName, name);
                subKey = (lifetime, typeFullName, name);

                isCached = isValid && lifetime is not Lifetime.Transient;

                if (factory switch
                {
                    IMethodSymbol factoryMethod => factoryMethod.Parameters,
                    IPropertySymbol { IsIndexer: true } factoryProperty => factoryProperty.Parameters,
                    IFieldSymbol => [],
                    _ => GetConstructorParameters(type)
                }
                    is { IsDefaultOrEmpty: false, Length: > 0 } parameters)
                {
                    prms = parameters;
                    isSimpleTransient = lifetime is Lifetime.Transient && prms.Length == 0;
                }
                else if (!isSimpleTransient && !isCached)
                {
                    isSimpleTransient = true;
                }

                return true;


                bool ReturnNotFound()
                {
                    diagnostics.Add(ServiceContainerDiagnostics.UnresolvedDependency(attrSyntax, providerTypeName, exportTypeFullName));
                    return false;
                }


                static ImmutableArray<IParameterSymbol> GetConstructorParameters(ITypeSymbol? implType)
                {
                    if (implType is not INamedTypeSymbol { Constructors: var ctor, InstanceConstructors: var insCtor }
                        || ctor.IsDefaultOrEmpty
                        || insCtor.IsDefaultOrEmpty) return [];

                    ImmutableArray<IParameterSymbol> parameters = [];

                    int min = int.MaxValue;

                    foreach (var item in ctor.Concat(insCtor).Distinct(SymbolEqualityComparer.Default).Cast<IMethodSymbol>())
                    {
                        if (item.Parameters.IsDefaultOrEmpty || item.Parameters.Length >= min) continue;
                        min = (parameters = item.Parameters).Length;
                    }

                    return parameters;
                }

                static bool TryGetAttributeParamsDefinition(SymbolInfo info, out ImmutableArray<IParameterSymbol> prms)
                {
                    if (info.Symbol is IMethodSymbol { Parameters: { } _prms })
                    {
                        prms = _prms;
                        return true;
                    }

                    foreach (var item in info.CandidateSymbols)
                    {
                        if (item is IMethodSymbol { Parameters: { } _prms2 })
                        {
                            prms = _prms2;
                            return true;
                        }
                    }

                    prms = [];

                    return false;
                }

                static Span<MarkedParameter> GetAttributeParamsMap(
                   ImmutableArray<IParameterSymbol> paramSymbols,
                   SeparatedSyntaxList<AttributeArgumentSyntax> argsSyntax)
                {
                    int i = -1;
                    Span<MarkedParameter> result = new MarkedParameter[paramSymbols.Length];

                    foreach (var param in paramSymbols)
                    {
                        result[++i] = argsSyntax.Count > i && argsSyntax[i] is { NameColon: null, NameEquals: null } argSyntax
                            ? new(param, argSyntax)
                            : new(param, argsSyntax.FirstOrDefault(arg => param.Name == arg.NameColon?.Name.Identifier.ValueText));
                    }

                    return result;
                }

                static bool GetStringExpressionOrValue(
                    SemanticModel model,
                    IParameterSymbol paramSymbol,
                    AttributeArgumentSyntax? arg,
                    out string value)
                {
                    value = null!;

                    if (arg is not null)
                    {
                        if (model.GetSymbolInfo(arg.Expression).Symbol is IFieldSymbol
                            {
                                IsConst: true,
                                Type.SpecialType: SpecialType.System_String,
                                ConstantValue: { } val
                            })
                        {
                            return (value = val.ToString()!) != "";
                        }
                        else if (arg.Expression is LiteralExpressionSyntax { Token.ValueText: { } valueText } e
                            && e.IsKind(SyntaxKind.StringLiteralExpression))
                        {
                            return (value = valueText) != "";
                        }
                    }
                    else if (paramSymbol.HasExplicitDefaultValue)
                    {
                        value = paramSymbol.ExplicitDefaultValue?.ToString()!;
                        return value != "";
                    }

                    return false;
                }
            }

            void AppendParams(
                StringBuilder code,
                bool completeAsyncContext,
                bool appendInterceptorProvider,
                string newIndentedLine)
            {
                var needsComma = false;
                byte pos = 0;

                foreach (var item in appendParams)
                {
                    if (needsComma.Exchange(true)) code.Append(',');

                    code.Append(newIndentedLine);

                    if (item.StartsCollectionExpression) code.Append('[');

                    if (asyncLocalResolvers.TryGetValue(item.Key, out var task))
                    {
                        if (completeAsyncContext && (!task.ResolvedBefore && !whenAll.Contains("__v" + pos)))
                        {
                            code.Append("await ");
                            code.Append("__v").Append(pos).Append(".ConfigureAwait(false)");
                        }
                        else
                        {
                            code.Append("__v").Append(pos).Append(".Result");
                            if (completeAsyncContext && task.ResolvedBefore)
                                code.Append($" /* resolved previously by param {task.ResolvedByParamIndex} */");
                        }
                    }
                    else
                    {
                        item.Append(code, completeAsyncContext);
                    }

                    if (item.EndsCollectionExpression) code.Append(']');

                    pos++;
                }
            }

            void AppendMethod(StringBuilder code, List<Action> scopedExposers, List<DisposeBuilder> singletonDisposers, List<DisposeBuilder> scopedDisposers)
            {
                //hasAsyncDependencies.Dump($"Has [{typeFullName}] async dependencies?");

                if (isCached)
                {
                    code.Append(@"
    private ");
                    if (lifetime is Lifetime.Singleton) code.Append("static ");

                    code.Append(GetTypeName(typeFullName)).Append("? ").Append(backingFieldName).Append(';');

                    if (disposability is not 0)
                    {
                        if (lifetime is Lifetime.Scoped)
                            scopedDisposers
                                .Add(disposability is Disposability.Disposable ? AppendDisposerStatement : AppendAsyncDisposerStatment);
                        else if (lifetime is Lifetime.Singleton)
                            singletonDisposers
                                .Add(disposability is Disposability.Disposable ? AppendDisposerStatement : AppendAsyncDisposerStatment);
                    }
                }

                code.Append(@"
    public ");

                //if (!isCached && (hasAsyncDependencies/* || (AsyncKind is not 0 && factory is not null)*/)) code.Append("async ");

                AppendSignature(code);

                if (lifetime is Lifetime.Scoped) scopedExposers.Add(AppendExposedSignature);

                void AppendExposedSignature()
                {
                    code.Append(@"
		public new ");

                    AppendSignature(code);

                    code.Append(@" 
			=> base.").Append(methodName);

                    if (AsyncKind is not 0 && (hasAsyncDependencies || needsCancelToken))
                    {
                        code.Append('(');
                        if (needsCancelToken) code.Append("cancellationToken");
                        code.Append(')');
                    }

                    code.Append(@";
");
                }

                if (!isCached && !hasAsyncDependencies)
                {
                    code.Append(@" 
        => ");

                    if (isFactory)
                    {
                        AppendFactoryCaller(code, true);
                    }
                    else
                    {
                        AppendInstance(code, true);
                    }
                    code.Append(@";
");
                }
                else
                {
                    code.Append(@"
    {");

                    if (AsyncKind is 0 || !(hasAsyncDependencies || needsCancelToken))
                    {
                        var isValueType = AsyncKind is 0 ? type!.IsValueType : AsyncKind is AsyncKind.ValueTask;

                        code.Append(@"
        get
        {
            if(").Append(backingFieldName).Append(isValueType ? ".HasValue" : " is not null").Append(") return ").Append(backingFieldName);

                        if (isValueType) code.Append(".Value");

                        code.Append(@";
				
            lock(this)
			
			return ").Append(backingFieldName).Append(@" ??= ");

                        if (isFactory)
                        {
                            AppendFactoryCaller(code, false);
                        }
                        else
                        {
                            AppendInstance(code, false);
                        }

                        code.Append(@";
        }");
                    }
                    else
                    {
                        var isAsyncEmptyFactory = factory is not null && !(hasAsyncDependencies || needsCancelToken);

                        var indent = isCached ? "   " : null;

                        if (isCached)
                        {
                            code.Append(@"
        if(").Append(backingFieldName).Append(AsyncKind is AsyncKind.ValueTask ? ".HasValue" : " is not null").Append(") return ").Append(backingFieldName);

                            if (AsyncKind is AsyncKind.ValueTask) code.Append(".Value");

                            code.Append(';');
                        }

                        if (isAsyncEmptyFactory)
                        {
                            code.Append(@"
					
		lock(this)
		
		return ").Append(backingFieldName).Append(" ??= ");

                            AppendFactoryCaller(code, false);

                            code.Append(';');
                        }
                        else
                        {
                            var hasAsyncLocalResolvers = asyncLocalResolvers.Count > 0;

                            if (isCached) code.Append(@"
					
		lock(this)
		{
			if(").Append(backingFieldName).Append(AsyncKind is AsyncKind.ValueTask ? ".HasValue" : " is not null").Append(") return ").Append(backingFieldName);

                            if (AsyncKind is AsyncKind.ValueTask) code.Append(".Value");

                            code.Append(@";
");
                            //ct = global::System.Threading.CancellationTokenSource.CreateLinkedTokenSource(ct, __scopedCancellationTokenSrc.Token).Token;

                            if (hasAsyncLocalResolvers)
                            {
                                foreach (var resolver in asyncLocalResolvers.Values)
                                {
                                    if (resolver.ParamIndex > -1) resolver.AppendAsyncLocal(code, indent);
                                }

                                code.Append(@"
");
                            }

                            code.Append(@"
		").Append(indent);

                            code.Append("return ");

                            if (isCached) code.Append(backingFieldName).Append(" ??= ");

                            if (hasAsyncLocalResolvers)
                            {
                                var useAnd = false;

                                foreach (var param in asyncLocalResolvers.Values)
                                {
                                    if (param.ParamIndex == -1) continue;
                                    if (useAnd.Exchange(true)) code.Append(@"
				").Append(indent).Append("&& ");

                                    code.Append("__v").Append(param.ParamIndex).Append(".IsCompletedSuccessfully");
                                }

                                code.Append(@"
		    ").Append(indent).Append("? global::System.Threading.Tasks.Task.FromResult<").Append(exportTypeFullName).Append(@">(
				").Append(indent);
                            }

                            if (isFactory)
                            {
                                AppendFactoryCaller(code, false, false, @"
						");
                            }
                            else
                            {
                                AppendInstance(code, false, false, @"
						");
                            }

                            if (isCached && !hasAsyncLocalResolvers)
                            {
                                code.Append(';');
                                goto exitLock;
                            }
                            else
                            {
                                code.Append(')');
                            }

                            code.Append(@"
            ").Append(indent).Append(@": ResolveCoreAsync();

		").Append(indent).Append("async global::System.Threading.Tasks.Task<").Append(exportTypeFullName).Append(@"> ResolveCoreAsync()
		").Append(indent).Append('{');

                            if (whenAll.Length > 1)
                            {
                                code.Append(@"
			").Append(indent).Append("await global::System.Threading.Tasks.Task.WhenAll(").Append(whenAll).Append(@");
");
                            }

                            code.Append(@"				
			").Append(indent).Append("return ");

                            if (isFactory)
                            {
                                AppendFactoryCaller(code, true, false, @"
				" + indent);
                            }
                            else
                            {
                                AppendInstance(code, true, false, @"
				" + indent);
                            }

                            code.Append(@";
		").Append(indent).Append('}');
                            exitLock:
                            if (isCached) code.Append(@"
		}");
                        }
                    }
                    code.Append(@"
    }
");
                }
            }

            void AppendDisposerStatement(StringBuilder code, bool awaits = true)
            {
                code.Append(@"
        ").Append(backingFieldName).Append("?.").Append("Dispose();");
            }

            void AppendAsyncDisposerStatment(StringBuilder code, bool awaits = true)
            {
                code.Append(@"
        ");

                if (AsyncKind > 0)
                {
                    code.Append(awaits ? "await " : "return ")
                        .Append(backingFieldName).Append(".TryDisposeAsync();");
                }
                else
                {
                    if (awaits)
                    {

                        code.Append("if(").Append(backingFieldName)
                            .Append(type!.IsValueType ? ".HasValue) " : " is not null) ")
                            .Append("await ")
                            .Append(backingFieldName);

                        if (type.IsValueType) code.Append(".Value");

                        code.Append(".DisposeAsync();");
                    }
                    else
                    {
                        code.Append("return ")
                            .Append(backingFieldName)
                            .Append("?.DisposeAsync() ?? default!;");
                    }
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void AppendSignature(StringBuilder code)
            {
                code.Append(GetTypeName(exportTypeFullName)).Append(' ').Append(methodName);

                if (AsyncKind is not 0 && (hasAsyncDependencies || needsCancelToken))
                {
                    code.Append('(');
                    if (needsCancelToken) code.Append("global::System.Threading.CancellationToken cancellationToken = default");
                    code.Append(')');
                }
            }

            string GetTypeName(string typeName)
            {
                return AsyncKind > 0
                    ? $"global::System.Threading.Tasks.{(initialAsyncType is AsyncKind.ValueTask ? "Value" : null)}Task<{exportTypeFullName}>"
                    : typeName;
            }

            void AppendValue(StringBuilder code, bool asyncContext = false, bool interceptorContext = false)
            {
                if (!interceptorContext && !isCached && isFactory)
                {
                    //if (asyncContext && AsyncKind is not 0) code.Append("await ");

                    AppendFactoryCaller(code, asyncContext);
                }
                else if (hasNoCachedDeps && !isCached && !isExternal)
                {
                    AppendInstance(code, asyncContext, interceptorContext);
                }
                else
                {
                    //if (asyncContext && (AsyncKind is not 0)) code.Append("await ");

                    AppendCachedCaller(code, interceptorContext);
                }
            }

            void AppendCachedCaller(StringBuilder code, bool interceptorContext = false, string newIndentedLine = @"
				")
            {
                if (interceptorContext) code.Append("provider.");
                code.Append(methodName);

                if (AsyncKind is not 0 && (hasAsyncDependencies || needsCancelToken))
                {
                    code.Append('(');
                    if (needsCancelToken) code.Append("cancellationToken");
                    code.Append(')');
                }
            }

            void AppendInstance(StringBuilder code, bool isAsyncContext, bool appendInterceptorProvider = false, string newIndentedLine = @"
				")
            {
                code.Append("new ")
                    .Append(typeFullName)
                    .Append('(');

                AppendParams(code, isAsyncContext, appendInterceptorProvider,  newIndentedLine);

                code.Append(')');
            }

            void AppendFactoryCaller(
                StringBuilder code,
                bool allowAwait,
                bool appendInterceptorProvider = false,
                string newIndentedLine = @"
				")
            {
                switch (factoryKind)
                {
                    case SymbolKind.Method:

                        AppendFactoryContainingType(code, appendInterceptorProvider);

                        //if (initialAsyncType > 0)
                        //{
                        //    code.Append(factoryName)
                        //        .Append('<')
                        //        .Append(exportTypeFullName)
                        //        .Append(">(");

                        //    AppendParams(code, allowAwait, newIndentedLine);

                        //    code.Append(')');
                        //}
                        //else
                        //{
                        code.Append(factoryName)
                            .Append('(');

                        AppendParams(code, allowAwait, appendInterceptorProvider, newIndentedLine);

                        code.Append(')');
                        //}

                        break;

                    case SymbolKind.Property:

                        AppendFactoryContainingType(code, appendInterceptorProvider);

                        if (isFactoryIndexerProperty)
                        {
                            code.Append(factoryName)
                                .Append('[');

                            AppendParams(code, allowAwait, appendInterceptorProvider, newIndentedLine);

                            code.Append(']');
                        }
                        else
                        {
                            code.Append(factoryName);
                        }

                        break;


                    case SymbolKind.Field:

                        AppendFactoryContainingType(code, appendInterceptorProvider);

                        code.Append(factoryName);

                        break;

                    default:

                        AppendDefault(code);

                        break;
                }
            }

            void AppendFactoryContainingType(StringBuilder code, bool appendInterceptorProvider)
            {
                if (isFactoryFromCurrentProvider && !isInterfaceProvider)
                    return;

                if (appendInterceptorProvider)
                    code.Append("provider.");
                if (isInterfaceProvider && !isStaticFactory)
                    code.Append("((").Append(factoryProviderName).Append(")this)").Append('.');
                else if (isStaticFactory)
                    code.Append(factoryProviderName).Append('.');
            }

            void AppendDefault(StringBuilder code, bool _ = false, bool __ = false)
            {
                code.Append("default");

                if (type?.IsNullable is false) code.Append('!');
            }

            /// <summary>
            /// Resolves caching backing field member and resolver member names
            /// </summary>
            (string, string) GetResolverName()
            {
                var memberName = nameOrFormat is not null
                    ? string.Format(nameOrFormat, name.Pascalize()!).RemoveDuplicates()
                    : SanitizedTypeName();

                memberName = (isExternal ? memberName : factory?.Name ?? memberName).TrimStart('_');

                if (factory is not null && isCached && !memberName.EndsWith("Cached") && !memberName.EndsWith("Cache"))
                    memberName += "Cached";

                var fieldName = "_" + memberName.Camelize();

                if (!isExternal && factory is null && AsyncKind is not 0 && !isSimpleTransient) memberName = "Get" + memberName;

                if (!(memberName.Contains("Async") || memberName.Contains("Task")) && AsyncKind is not 0)
                    (memberName, fieldName) = (memberName + "Async", fieldName + "Task");

                return (fieldName, memberName);
            }

            string SanitizedTypeName()
            {
                var sanitizedTypeName = Sanitize(type!).Replace(" ", "").Capitalize();

                ref var idOut = ref CollectionsMarshal.GetValueRefOrAddDefault(methodNamesMap, (lifetime, exportTypeFullName, name), out var exists)!;

                if (exists)
                {
                    return idOut;
                }

                var key = name.Pascalize();

                if (key is "")
                {
                    if (!methodsRegistry.Add(idOut = sanitizedTypeName)) methodsRegistry.Add(idOut = $"{lifetime}{sanitizedTypeName}");
                }
                else if (!(methodsRegistry.Add(idOut = key!)
                    || methodsRegistry.Add(idOut = $"{key}{sanitizedTypeName}")
                    || methodsRegistry.Add(idOut = $"{lifetime}{key}")))
                {
                    methodsRegistry.Add(idOut = $"{lifetime}{key}{sanitizedTypeName}");
                }

                return idOut;

                static string Sanitize(ITypeSymbol type)
                {
                    switch (type)
                    {
                        case INamedTypeSymbol { IsTupleType: true, TupleElements: { Length: > 0 } els }:

                            return "TupleOf" + string.Join("", els.Select(f => Sanitize(f.Type)));

                        case INamedTypeSymbol { IsGenericType: true, TypeParameters: { } args }:

                            return type.Name + "Of" + string.Join("", args.Select(Sanitize));

                        default:

                            string typeName = type.TypeNameFormat;

                            if (type is IArrayTypeSymbol { ElementType: { } elType })
                                typeName = Sanitize(elType) + "Array";

                            return char.ToUpperInvariant(typeName[0]) + typeName[1..].TrimEnd('?', '_');
                    }
                }
            }
        }
    }

}

internal record struct MarkedParameter(IParameterSymbol Parameter, AttributeArgumentSyntax? Attribute);
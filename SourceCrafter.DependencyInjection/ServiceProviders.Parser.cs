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
internal partial class ServiceProviders
#pragma warning restore CA1050 // Declarar tipos en espacios de nombres
{
    private static Emitter TryParseContainer(
        in GeneratorAttributeSyntaxContext gasc,
        in CancellationToken cancelToken)
    {
        var model = gasc.SemanticModel;
        var providerType = (INamedTypeSymbol)gasc.TargetSymbol;
        var providerDeclarationSyntax = providerType.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancelToken);        
        string modifiers, typeName;

        switch(providerDeclarationSyntax)
        {
            case ClassDeclarationSyntax { Modifiers: var mods, Keyword: { } keyword, Identifier: { } identifier, TypeParameterList: var typeParamsList }:
                (modifiers, typeName) = ($"{mods} {keyword}".TrimStart(), $"{identifier}{typeParamsList}");
                break;
            case InterfaceDeclarationSyntax { Modifiers: var mods, Identifier: { } identifier, TypeParameterList: var typeParamsList }:
                (modifiers, typeName) = ($"{mods/*.Except([SyntaxFactory.Token(SyntaxKind.InterfaceKeyword)])*/} partial class".TrimStart(), $"{identifier.ValueText[1..]}{typeParamsList}");
                break;
            default: return null!;
        };

        var providerFullTypeName = providerType.FullGlobalQualifiedName;
        var isInterfaceProvider = providerType.TypeKind == TypeKind.Interface;
        var providerTypeName = providerType.TypeNameFormat;
        var className = isInterfaceProvider ? providerTypeName[1..] : providerTypeName;
        var fullProviderImplName = providerType.ContainingNamespace.FullGlobalQualifiedName + '.' + className;
        var attributes = providerType.GetAttributes();

        bool
            hasScopedDependencies = false,
            implementsServiceProvider = providerType.AllInterfaces.Any(i => i.FullGlobalQualifiedName == "global::System.IServiceProvider"),
            generateServiceProviderApi = false;

        // Si el usuario ya declara estos miembros en su parcial, el generador no los emite.
        var hasUserEnvironmentName =
            providerType.GetMembers("EnvironmentName").Length > 0
            || providerType.GetMembers("EnvironmentVariableName").Length > 0;

        // El generador emite un constructor sin parametros para inicializar candados y
        // cancelacion. Si el usuario ya declaro uno, se le avisa en vez de dejar que el
        // compilador escupa un CS0111 apuntando a codigo que el no escribio.
        var hasUserParameterlessConstructor = providerType
            .GetMembers(WellKnownMemberNames.InstanceConstructorName)
            .Any(m => m is IMethodSymbol { Parameters.Length: 0, IsImplicitlyDeclared: false, IsStatic: false });

        // Se proyecta a cadena aqui: el renderizador no puede ver simbolos.
        var lockTypeName = SupportsDedicatedLockType(model)
            ? "global::System.Threading.Lock"
            : "object";

        HashSet<string> externalAssemblies = [];

        var defaultKeyComparer = EqualityComparer<FirstLevelDependencyKey>.Default;
        var defaultSubKeyComparer = EqualityComparer<DependencyKey>.Default;

        DependencyDictionary dependencyValueBuilders = [];
        Dictionary<DependencyKey, string> methodNamesMap = new(defaultSubKeyComparer);
        HashSet<string> methodsRegistry = [];
        Dictionary<DependencyKey, MemberBuilder> dependencyMemberBuilders = [];
        HashSet<ResolverBuilder> genericResolvers = new(new GenericResolverBuilderComparer());

        Disposability
            containerDisposability = Disposability.None,
            scopedDisposability = Disposability.None;
        int
            asyncScopedDisposable = 0,
            asyncSingletonDisposable = 0,
            asyncScopedAsyncDisposable = 0,
            asyncSingletonAsyncDisposable = 0,
            scopedAsyncDisposable = 0,
            singletonAsyncDisposable = 0;

        HashSet<Diagnostic> diagnostics = [];

        ResolverBuilder selfDepInfo = new($"{providerFullTypeName}")
        {
            Key = (Lifetime.Singleton, providerFullTypeName, ""),
            AppendValue = (_, _, _, _) => { }
        };

        string envName = DefaultEnvName;

        foreach (var attr in attributes)
        {
            if(TryRegisterService(attr, null, out var resolver, cancelToken))
                genericResolvers.Add(resolver!); ;
        }

        if (dependencyValueBuilders.Count == 0) return null!;

        // El generador ya no emite constructor para los candados (se crean de forma
        // perezosa), asi que solo hay conflicto real si algun resolver usa el token de vida.
        if (hasUserParameterlessConstructor
            && dependencyMemberBuilders.Values.Any(m => m.RequiresCancelToken))
            diagnostics.Add(ServiceContainerDiagnostics.ConflictingParameterlessConstructor(
                providerType.Locations.FirstOrDefault() ?? Location.None,
                className));

        var nameSpace = providerType.ContainingNamespace is { } ns ? ns.ToDisplayString() : null;

        Emitter emitter = new(
            providerType.MetadataLongName,
            nameSpace,
            providerFullTypeName,
            modifiers,
            isInterfaceProvider,
            implementsServiceProvider,
            generateServiceProviderApi,
            hasUserEnvironmentName,
            className,
            typeName,
            envName,
            asyncScopedDisposable,
            asyncSingletonDisposable,
            asyncScopedAsyncDisposable,
            asyncSingletonAsyncDisposable,
            scopedAsyncDisposable,
            singletonAsyncDisposable,
            containerDisposability,
            scopedDisposability,
            dependencyValueBuilders,
            dependencyMemberBuilders,
            diagnostics,
            genericResolvers);

        diagnostics = null!;
        dependencyValueBuilders = null!;
        dependencyMemberBuilders = null!;
        genericResolvers = null!;

        return emitter;

        /// <summary>
        /// Lee los parametros de <c>[ServiceContainer]</c> emparejando por nombre de
        /// parametro y no por posicion: con argumentos con nombre el orden no es fiable
        /// (antes, <c>[ServiceContainer(generateServiceProviderApi: true)]</c> acababa
        /// tomando el booleano como nombre de la variable de entorno).
        /// </summary>
        void ReadContainerOptions(AttributeData attr, CancellationToken cancelToken)
        {
            var ctorParams = attr.AttributeConstructor?.Parameters ?? [];
            var args = (attr.ApplicationSyntaxReference?.GetSyntax(cancelToken) as AttributeSyntax)?.ArgumentList?.Arguments;

            if (args is not { Count: > 0 })
            {
                // Atributo sin sintaxis disponible: se usan los valores ya resueltos.
                if (attr.ConstructorArguments is [{ Value: string v }, ..] && v.Trim().Length > 0)
                    envName = $@"""{v}""";

                if (attr.ConstructorArguments is [_, { Value: bool flag }, ..])
                    generateServiceProviderApi = flag;

                return;
            }

            var position = 0;

            foreach (var arg in args)
            {
                var paramName = arg.NameColon?.Name.Identifier.ValueText
                    ?? (position < ctorParams.Length ? ctorParams[position].Name : null);

                if (arg.NameColon is null) position++;

                switch (paramName)
                {
                    case "envName":

                        envName = arg.Expression switch
                        {
                            LiteralExpressionSyntax { Token.ValueText: { } envString } when envString.Trim() is { Length: > 0 } => $@"""{envString}""",
                            MemberAccessExpressionSyntax { Name: { } member } => model.GetSymbolInfo(member, cancellationToken: cancelToken) switch
                            {
                                { Symbol: IFieldSymbol { } field } => field.GlobalNamespaced,
                                _ => DefaultEnvName
                            },
                            IdentifierNameSyntax member => isInterfaceProvider ? $"{providerTypeName}.{member.Identifier.ValueText}" : member.Identifier.ValueText,
                            _ => DefaultEnvName
                        };

                        break;

                    case "generateServiceProviderApi":

                        generateServiceProviderApi = model.GetConstantValue(arg.Expression, cancelToken) is { HasValue: true, Value: true };

                        break;
                }
            }
        }

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
                needsCancelToken = false;

            string
                name = string.Empty,
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

            // Estado de render, libre de simbolos y sintaxis. Los delegados que el
            // emisor cachea apuntan a esta celda, nunca a la clase de cierre del
            // parser (que retendria la Compilation entre pasadas incrementales).
            // El modelo en si es inmutable: se construye entero en CommitRenderState.
            ResolverRendererRef render = new();

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
                    AppendValue = render.AppendValue,
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
                    render.AppendValue,
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
                CommitRenderState();
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
                    appendParams.Add(new(selfDepInfo.Key, new SelfProviderAppender(asksForRoot).Append));
                    continue;
                }

                if (Equals(prm.Type.FullGlobalQualifiedName, CancelTokenFQMetaName))
                {
                    needsCancelToken = true;
                    resolver.PassCancelToken = true;
                    appendParams.Add(new(selfDepInfo.Key, render.AppendCancelToken));
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

                        appendParams.Add(new(
                            foundService.Key,
                            new ParamValueAppender(foundService, AppendParam, foundExportTypeFullName, paramAsyncType).Append,
                            startsCollectionExpression,
                            endsCollectionExpression,
                            // Toma algun candado al resolverse: o esta cacheado, o es un
                            // transient que arrastra cacheados dentro. En ambos casos hay que
                            // sacarlo fuera del 'lock' del resolver que lo consume.
                            foundService.Key.lifetime is not Lifetime.Transient || !foundService.TransientWithoutCachedDeps));

                        deepParamsCount += foundService.ParamsLength;
                        foundAsyncType = foundService.AsyncKind;

                        if (foundAsyncType is not 0)
                        {
                            if (AsyncKind is 0)
                                AsyncKind = AsyncKind.Task;

                            if (!hasAsyncDependencies)
                                hasAsyncDependencies = true;

                            AsyncLocalResolver resolved = null!;

                            Action<StringBuilder, string?> AppendAsyncLocalResolver =
                                new AsyncLocalAppender(paramIndex, AppendParam, "\r\n\t\t").Append;

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

						}
					}
				}
                else if (paramType.TypeKind is not TypeKind.Interface)
                {
                    TryRegisterService(null, prm, out _, cancelToken, ValidateChildParameterDependency);
                }
                else
                {
                    appendParams.Add(new(resolvedSubKey, render.AppendDefault));
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
                        Action<StringBuilder, string?> AppendAsyncLocalResolver =
                            new AsyncLocalAppender(paramIndex, AppendParam, "\r\n\t\t\t").Append;

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
					}

					if (childAsyncType > AsyncKind)
                    {
                        AsyncKind = childAsyncType;
                    }

                    if (childExists)
                    {
                        deepParamsCount += childParamCount;
                        appendParams.Add(new(resolvedSubKey, AppendParam));
                        return false;
                    }
                    else if ((isNullChildType && isPrimitiveParamType) || (isUnkeyedInternalPrimitive && prm.Type.IsPrimitive()) || !isChildValid)
                    {
                        deepParamsCount += 1;
                        appendParams.Add(new(resolvedSubKey, render.AppendDefault));
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
                            BuildAndExpose = render.AppendMethod 
                        });

            resolver.TransientWithoutCachedDeps = hasNoCachedDeps;
            resolver.AsyncKind = AsyncKind;

            //TryRegisterInterceptorMethod();

            CommitRenderState();

            return true;

            // Congela el estado del parser en un renderizador inmutable, ya proyectado a
            // cadenas, banderas y enumeraciones. Se invoca en cada salida exitosa,
            // siempre antes de que el emisor pueda ejecutar los delegados.
            void CommitRenderState()
            {
                render.Value = new()
                {
                    typeFullName = typeFullName,
                    exportTypeFullName = exportTypeFullName,
                    factoryProviderName = factoryProviderName,
                    backingFieldName = backingFieldName,
                    methodName = methodName,
                    factoryName = factoryName,
                    AsyncKind = AsyncKind,
                    initialAsyncType = initialAsyncType,
                    lifetime = lifetime,
                    disposability = disposability,
                    factoryKind = factoryKind,
                    isCached = isCached,
                    isFactory = isFactory,
                    isExternal = isExternal,
                    hasNoCachedDeps = hasNoCachedDeps,
                    hasAsyncDependencies = hasAsyncDependencies,
                    needsCancelToken = needsCancelToken,
                    isStaticFactory = isStaticFactory,
                    isFactoryFromCurrentProvider = isFactoryFromCurrentProvider,
                    isFactoryIndexerProperty = isFactoryIndexerProperty,
                    isInterfaceProvider = isInterfaceProvider,
                    typeIsValueType = type?.IsValueType is true,
                    typeIsNonNullable = type?.IsNullable is false,
                    hasFactorySymbol = factory is not null,
                    lockTypeName = lockTypeName,
                    appendParams = appendParams,
                    asyncLocalResolvers = asyncLocalResolvers
                };
            }

            bool IsValidServiceAttribute(AttributeData? attr, CancellationToken cancelToken)
            {
                var isContainerAttr = false;

                if (attr is not { AttributeClass: { } _attrClass, ApplicationSyntaxReference: { } attrSyntaxRef }
                    || (isContainerAttr = _attrClass.FullGlobalQualifiedName is ServiceContainerAttr)
                    || attrSyntaxRef.GetSyntax(cancelToken) is not AttributeSyntax { } _attrSyntax
                    || !TryGetAttributeParamsDefinition(model.GetSymbolInfo(_attrSyntax, cancellationToken: cancelToken), out ImmutableArray<IParameterSymbol> attrParams)
                    || !TryGetLifetime(_attrSyntax, ref _attrClass, ref isExternal, out lifetime))
                {
                    if (isContainerAttr) ReadContainerOptions(attr!, cancelToken);

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

    /// <summary>
    /// Indica si el codigo generado puede declarar sus candados como
    /// <c>System.Threading.Lock</c> en vez de <c>object</c>.
    /// </summary>
    /// <remarks>
    /// Hacen falta las dos condiciones. El tipo existe desde .NET 9, pero es el compilador
    /// quien reconoce <c>lock (x)</c> sobre el y emite <c>EnterScope</c>; con C# 12 o menos
    /// la variable se convierte a <c>object</c>, se vuelve a caer en <c>Monitor</c> y ademas
    /// se emite el aviso CS9216. Es decir: emitirlo sin C# 13 seria mas lento y mas ruidoso
    /// que seguir usando <c>object</c>.
    /// <para>
    /// Se exige que el tipo venga del mismo ensamblado que <c>System.Object</c> para no
    /// confundirlo con un <c>System.Threading.Lock</c> definido por el usuario, que no
    /// recibe trato especial del compilador.
    /// </para>
    /// </remarks>
    private static bool SupportsDedicatedLockType(SemanticModel model)
    {
        if (model.SyntaxTree.Options is not CSharpParseOptions { LanguageVersion: >= LanguageVersion.CSharp13 })
            return false;

        var compilation = model.Compilation;

        return compilation.GetTypeByMetadataName("System.Threading.Lock") is { } lockType
            && SymbolEqualityComparer.Default.Equals(
                lockType.ContainingAssembly,
                compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly);
    }

}

internal record struct MarkedParameter(IParameterSymbol Parameter, AttributeArgumentSyntax? Attribute);
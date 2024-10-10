using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SourceCrafter.DependencyInjection.Interop;

internal delegate void CommaSeparateBuilder(ref bool useIComma, StringBuilder code);
internal delegate void ValueBuilder(StringBuilder code);
internal delegate void MemberBuilder(StringBuilder code, bool isImplementation, string generatedCodeAttribute);
internal delegate void ParamsBuilder(StringBuilder code);

public sealed class ServiceDescriptor(ITypeSymbol type, string key, ITypeSymbol? _interface = null)
{
    static readonly Lifetime[] lifetimes = [Lifetime.Singleton, Lifetime.Scoped, Lifetime.Transient];

    internal const string
        CancelTokenFQMetaName = "System.Threading.CancellationToken",
        EnumFQMetaName = "global::System.Enum",
        KeyParamName = "key",
        NameFormatParamName = "nameFormat",
        FactoryOrInstanceParamName = "factoryOrInstance",
        ImplParamName = "impl",
        IfaceParamName = "iface",
        SingletonAttr = "global::SourceCrafter.DependencyInjection.Attributes.SingletonAttribute",
        ScopedAttr = "global::SourceCrafter.DependencyInjection.Attributes.ScopedAttribute",
        TransientAttr = "global::SourceCrafter.DependencyInjection.Attributes.TransientAttribute",
        DependencyAttr = "global::SourceCrafter.DependencyInjection.Attributes.DependencyAttribute",
        ServiceContainerAttr = "global::SourceCrafter.DependencyInjection.Attributes.ServiceContainerAttribute";

    public string FullTypeName = null!;
    internal string ResolverMethodName = null!;
    internal string CacheField = null!;
    internal ITypeSymbol Type = type;
    internal ITypeSymbol? Interface = _interface;
    internal ISymbol? Factory;
    internal SymbolKind FactoryKind;
    public Lifetime Lifetime = Lifetime.Singleton;
    public bool IsCached = true;
    public string Key = key;
    public Disposability Disposability;
    //internal ValueBuilder GenerateValue = null!;
    internal CommaSeparateBuilder? BuildParams = null!;
    internal SemanticModel TypeModel = null!;
    public bool IsResolved;
    internal ImmutableArray<AttributeData> Attributes = [];
    internal ITypeSymbol ContainerType = null!;
    public bool NotRegistered = false;
    internal bool RequiresDisposabilityCast = false;
    public bool IsCancelTokenParam;
    public bool IsExternal;
    internal AttributeSyntax OriginDefinition = null!;
    internal ServiceContainer ServiceContainer = null!;
    public string ExportTypeName = null!;

    public Disposability ContainerDisposability => ServiceContainer.disposability;

    private bool? isFactory;

    private bool? isNamed;

    bool _isAsync = false;
    public bool IsAsync
    {
        get => _isAsync;
        set
        {
            if (!_isAsync && value && !ResolverMethodName.EndsWith("Async"))
            {
                ResolverMethodName += "Async";
            }
            _isAsync = value;
        }
    }

    public bool IsFactory => isFactory ??= Factory is not null;

    internal bool IsKeyed => isNamed ??= Key is { Length: > 0 };

    internal ImmutableArray<IParameterSymbol>?
        Params,
        DefaultParamValues;

    public override string ToString()
    {
        return $"{{{Lifetime}}} {ExportTypeName} {Key}".Trim();
    }

    internal void AddInterface(ref bool useIComma, StringBuilder code)
    {
        (useIComma.Exchange(true) ? code.Append(", ") : code)
            .Append(@"
    global::SourceCrafter.DependencyInjection.I");

        if (IsKeyed) code.Append("Keyed");

        if (IsAsync) code.Append("Async");

        code.Append("ServiceProvider<");

        if (IsKeyed) code.Append(Key).Append(", ");

        code.Append(ExportTypeName)
            .Append('>');
    }

    internal void BuildCachedCaller(StringBuilder code)
    {
        code.Append(ResolverMethodName)
            .Append(IsAsync
                ? (ResolverMethodName.EndsWith("Async") ? "Async" : null) + "(cancellationToken.Value"
                : "(")
            .Append(')');
    }

    internal void BuildResolver(StringBuilder code, bool isImplementation, string generatedCodeAttribute)
    {
        if (isImplementation)
        {
            ServiceContainer.CheckMethodUsage(Lifetime, ResolverMethodName);

            code.Append(@"
    ")
                .Append(generatedCodeAttribute)
                .Append(@"
    private ");

            if (Lifetime is Lifetime.Singleton) code.Append("static ");

            code
                .Append(FullTypeName)
                .Append("? ")
                .Append(CacheField)
                .Append(@" = null;

    ")
                .AppendLine(generatedCodeAttribute);

            code.Append("    ");

            code.Append("public ");
        }
        else
        {
            code.Append(@"
    ")
                .Append(generatedCodeAttribute)
                .Append(@"
    ");
        }

        if (IsAsync)
        {
            if (isImplementation) code.Append("async ");

            code.Append("global::System.Threading.Tasks.ValueTask<")
                .Append(ExportTypeName)
                .Append("> ")
                .Append(ResolverMethodName);

            code.Append(@"(global::System.Threading.CancellationToken? cancellationToken = default)");
        }
        else
        {
            code.Append(ExportTypeName)
                .Append(' ')
                .Append(ResolverMethodName)
                .Append(@"()");
        }

        if (!isImplementation)
        {
            if (!IsResolved)
            {
                code.Append(" => default");

                if (Type?.IsNullable() is false) code.Append('!');
            }

            code.Append(@";
");
            return;
        }

        code.Append(@"
    {");

        BuildResolverBody(code);

        code.Append(@"
    }
");
    }

    void BuildResolverBody(StringBuilder code)
    {
        if (IsCached)
        {
            var checkNullOnValueType = (Interface ?? Type) is { IsValueType: true, NullableAnnotation: not NullableAnnotation.Annotated };

            code.Append(@"
        if (")
                .Append(CacheField);

            if (checkNullOnValueType)
            {
                code
                    .Append(@".HasValue");
            }
            else
            {
                code
                    .Append(@" is not null");
            }

            code.Append(@") return ")
                .Append(CacheField);

            if (checkNullOnValueType)
            {
                code.Append(@".Value;");
            }
            else
            {
                code.Append(';');
            }

            if (IsAsync)
            {
                code.Append(@"

        await __globalSemaphore.WaitAsync(cancellationToken ??= __globalCancellationTokenSrc.Token);

        try
        {
            return ");

                code.Append(CacheField)
                    .Append(@" ??= ");

                AppendBuilder(code);

                code.Append(@";
        }
        finally
        {
            __globalSemaphore.Release();
        }");

            }
            else
            {
                code.Append(@"

        lock(__lock) return ")
                    .Append(CacheField)
                    .Append(@" ??= ");

                AppendBuilder(code);

                code.Append(@";");
            }
        }
        else
        {
            code.Append(@"
        return ");

            BuildInstance(code);

            code.Append(@";");
        }

        void AppendBuilder(StringBuilder code)
        {
            if (IsFactory)
            {
                if (IsAsync && IsFactory) code.Append("await ");

                BuildFactoryCaller(code);
            }
            else
            {
                BuildInstance(code);
            }
        }
    }

    internal void BuildFactoryCaller(StringBuilder code)
    {
        bool comma = false;

        switch (Factory)
        {
            case IMethodSymbol { ContainingType: { } containingType, IsStatic: { } isStatic } method:

                AppendFactoryContainingType(code, containingType, isStatic);

                if (method is { Name: "Task" or "ValueTask", TypeArguments: { IsDefaultOrEmpty: false } and [{ } argType] }
                    && SymbolEqualityComparer.Default.Equals(argType, Type))
                {
                    code.Append(method.Name);
                    code.Append('<')
                        .Append(ExportTypeName)
                        .Append(">(");

                    BuildParams?.Invoke(ref comma, code);

                    code.Append(')');
                }
                else
                {
                    code.Append(method.Name);
                    code.Append('(');

                    comma = false;
                    BuildParams?.Invoke(ref comma, code);

                    code.Append(')');
                }

                break;


            case IPropertySymbol { IsIndexer: bool isIndexer, ContainingType: { } containingType, IsStatic: { } isStatic } prop:

                AppendFactoryContainingType(code, containingType, isStatic);

                if (isIndexer)
                {
                    code.Append(prop.Name);
                    code.Append('[');

                    comma = false;
                    BuildParams?.Invoke(ref comma, code);

                    code.Append(']');
                }
                else
                {
                    code.Append(prop.Name);
                }

                break;


            case IFieldSymbol { ContainingType: { } containingType, IsStatic: { } isStatic } field:

                AppendFactoryContainingType(code, containingType, isStatic);

                code.Append(field.Name);

                break;

            default:

                AppendDefault(code);

                break;
        }
    }

    private void AppendDefault(StringBuilder code)
    {
        code.Append("default");

        if (Type?.IsNullable() is false) code.Append('!');
    }

    private void AppendFactoryContainingType(StringBuilder code, INamedTypeSymbol containingType, bool isStatic)
    {
        if (SymbolEqualityComparer.Default.Equals(containingType, ContainerType))
            return;

        if (isStatic)
            code.Append(containingType.ToGlobalNamespaced()).Append('.');
        else
            return;
    }

    internal void CheckParamsDependencies(ServiceContainer container, ImmutableArray<InvokeInfo> _serviceCalls)
    {
        if (GetParameters() is not { IsDefaultOrEmpty: false } parameters) return;

        int resolvedDeps = 0;

        foreach (var param in parameters)
        {
            var paramType = param.Type;
            var paramTypeName = param.Type.ToGlobalNamespaced();

            var isExternal = false;

            DependencySlimInfo depInfo = default;
            Lifetime lifetime = default;
            ServiceDescriptor found = null!;

            if (paramTypeName.Equals("global::" + CancelTokenFQMetaName))
            {
                resolvedDeps++;
                BuildParams += AppendCancelToken;
                continue;
            }

            if (param.GetAttributes() is { Length: > 0 } paramAttrs)
            {
                foreach (var attr in paramAttrs)
                {
                    INamedTypeSymbol attrClass = attr.AttributeClass!;

                    if (attr.ApplicationSyntaxReference!.GetSyntax() is not AttributeSyntax attrSyntax
                        || attrClass is null
                        || GetLifetimeFromCtor(ref attrClass, ref isExternal, attrSyntax) is not { } _lifetime) continue;

                    lifetime = _lifetime;

                    var attrParams = (IMethodSymbol)container._model.GetSymbolInfo(attr.ApplicationSyntaxReference.GetSyntax()).Symbol!;

                    if (TryGetDependencyInfo(
                        container._model,
                        attr.AttributeClass!.TypeArguments,
                        attrSyntax.ArgumentList?.Arguments ?? [],
                        attrParams.Parameters,
                        paramType,
                        param.Name,
                        out depInfo)) break;
                }
            }
            else
            {
                foreach (var lifeTime in lifetimes)
                {
                    if (container.servicesMap.TryGetValue((lifeTime, paramTypeName, ""), out found)
                        || container.servicesMap.TryGetValue((lifeTime, paramTypeName, param.Name), out found))
                    {
                        if (found.IsAsync && !IsAsync)
                        {
                            IsAsync = true;

                            if (!container.requiresSemaphore) container.UpdateAsyncStatus();
                        }
                        resolvedDeps++;
                        BuildParams += found.BuildAsParam;
                        goto check;
                    }
                }
            }

            if (!depInfo.IsCached && lifetime is not Lifetime.Transient) depInfo.IsCached = true;

            var isAsync = depInfo.FinalType.TryGetAsyncType(out var realParamType);

            if (isAsync)
            {
                if (!IsAsync) IsAsync = true;

                depInfo.FinalType = realParamType!;

                if (!container.requiresSemaphore) container.UpdateAsyncStatus();

                if (depInfo.FactoryKind is SymbolKind.Method
                    && !((IMethodSymbol)depInfo.Factory!).Parameters.Any(p => p.Type.ToDisplayString() is CancelTokenFQMetaName))
                {
                    container._diagnostics.TryAdd(
                        ServiceContainerGeneratorDiagnostics
                            .CancellationTokenShouldBeProvided(depInfo.Factory, OriginDefinition));
                }
            }

            found = container.servicesMap.GetValueOrInserter((lifetime, paramTypeName, depInfo.Key), out var insertService);

            if (found != null)
            {
                if (found.IsAsync && !IsAsync)
                {
                    IsAsync = true;

                    if (!container.requiresSemaphore) container.UpdateAsyncStatus();
                }

                resolvedDeps++;

                BuildParams += found.BuildAsParam;

                continue;
            }

            var paramSyntax = param.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() ?? OriginDefinition;

            if (found is null && depInfo.ImplType is null && paramType.TypeKind is TypeKind.Interface)
            {
                container._diagnostics.TryAdd(
                    ServiceContainerGeneratorDiagnostics
                        .ParamInterfaceTypeWithoutImplementation(
                            paramSyntax,
                            ContainerType.ToGlobalNamespaced()));

                continue;
            }

            if (!isExternal && paramType.IsPrimitive() && depInfo.Key is "")
            {
                container._diagnostics.TryAdd(
                    ServiceContainerGeneratorDiagnostics.PrimitiveDependencyShouldBeKeyed(
                        lifetime,
                        paramSyntax,
                        paramTypeName,
                        paramTypeName));

                continue;
            }

            Disposability thisDisposability = Disposability.None;

            if (depInfo.IsCached)
            {
                thisDisposability = depInfo.FinalType.GetDisposability();

                if (thisDisposability > container.disposability) container.disposability = thisDisposability;

                if (depInfo.Disposability > container.disposability) container.disposability = depInfo.Disposability;
            }

            if (!depInfo.IsValid)
            {
                resolvedDeps++;

                BuildParams += AddDefault;

                continue;
            }

            var methodName = GetMethodName(isExternal, lifetime, depInfo, isAsync, container.methodsRegistry, container.methodNamesMap);

            found = new(depInfo.FinalType, depInfo.Key, null)
            {
                ServiceContainer = ServiceContainer,
                Lifetime = lifetime,
                Key = depInfo.Key,
                FullTypeName = depInfo.ImplType!.ToGlobalNamespaced(),
                ExportTypeName = paramTypeName,
                RequiresDisposabilityCast = thisDisposability is Disposability.None && depInfo.Disposability is not Disposability.None,
                ResolverMethodName = methodName,
                CacheField = "_" + methodName.Camelize(),
                Factory = depInfo.Factory,
                FactoryKind = depInfo.FactoryKind,
                Disposability = (Disposability)Math.Max((byte)thisDisposability, (byte)depInfo.Disposability),
                IsResolved = true,
                Attributes = depInfo.FinalType.GetAttributes(),
                IsAsync = isAsync,
                IsCached = depInfo.IsCached,
                Params = Extensions.GetParameters(depInfo),
                ContainerType = ContainerType
            };

            BuildParams += found.BuildAsParam;

            container.ResolveService(found);

            resolvedDeps++;

        check:;

            void AddDefault(ref bool comma, StringBuilder code)
            {
                if (!comma) comma = true; else code.Append(", ");

                code.Append("default");

                if (!paramType.IsNullable() && paramType.AllowsNull()) code.Append('!');
            }
        }

        if (resolvedDeps < parameters.Length && Type.DeclaringSyntaxReferences is [{ } first])
        {
            container._diagnostics.TryAdd(
                ServiceContainerGeneratorDiagnostics.DependencyWithUnresolvedParameters(
                    first.GetSyntax(),
                    ExportTypeName));
        }
    }

    internal static string GetMethodName(bool isExternal, Lifetime lifetime, DependencySlimInfo depInfo, bool isAsync, HashSet<string> methodsRegistry, DependencyNamesMap dependencyRegistry)
    {
        var identifier = depInfo.NameFormat is not null
            ? string.Format(depInfo.NameFormat, depInfo.Key.Pascalize()!).RemoveDuplicates()
            : Extensions.SanitizeTypeName(depInfo.ImplType ?? depInfo.FinalType, methodsRegistry, dependencyRegistry, lifetime, depInfo.Key);

        identifier = isExternal ? identifier : depInfo.Factory?.ToNameOnly() ?? ("Get" + identifier);

        if (depInfo.Factory != null && depInfo.IsCached && !identifier.EndsWith("Cached") && !identifier.EndsWith("Cache")) identifier += "Cached";
        if (!identifier.EndsWith("Async") && isAsync) identifier += "Async";

        return identifier;
    }

    public static Lifetime? GetLifetimeFromCtor(ref INamedTypeSymbol attrClass, ref bool isExternal, AttributeSyntax attrSyntax)
    {
        var lifetime = GetLifetimeFromSyntax(attrSyntax);

        if (lifetime.HasValue) return lifetime.Value;

        do
        {
            (isExternal, lifetime) = attrClass.ToGlobalNonGenericNamespace() switch
            {
                SingletonAttr => (isExternal, Lifetime.Singleton),
                ScopedAttr => (isExternal, Lifetime.Scoped),
                TransientAttr => (isExternal, Lifetime.Transient),
                { } val => (val is not DependencyAttr, GetFromCtorSymbol(attrClass))
            };

            if (lifetime.HasValue) return lifetime.Value;

            isExternal = true;
        }
        while ((attrClass = attrClass?.BaseType!) is not null);

        return null;

        static Lifetime? GetFromCtorSymbol(INamedTypeSymbol attrClass)
        {
            foreach (var ctor in attrClass.Constructors)
                foreach (var param in ctor.Parameters)
                    if (param.Name is "lifetime" && param.HasExplicitDefaultValue)
                        return (Lifetime)(byte)param.ExplicitDefaultValue!;

            return null;
        }
        
        static Lifetime? GetLifetimeFromSyntax(AttributeSyntax attribute)
        {
            if (attribute.ArgumentList?.Arguments
                    .FirstOrDefault(x => x.NameColon?.Name.Identifier.ValueText is "lifetime")?.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: { } memberName }
                && Enum.TryParse(memberName, out Lifetime lifetime))
            {
                return lifetime;
            }

            return null;
        }
    }

    internal ImmutableArray<IParameterSymbol> GetParameters()
    {
        return Factory switch
        {
            IMethodSymbol factoryMethod => factoryMethod.Parameters,
            IPropertySymbol { IsIndexer: true } factoryProperty => factoryProperty.Parameters,
            IFieldSymbol => [],
            _ => Params ?? Type
                    .GetMembers()
                    .OfType<IMethodSymbol>()
                    .Where(IsConstructor)
                    .OrderBy(r => r.Parameters.Length)
                    .FirstOrDefault()
                    ?.Parameters ?? []
        };
    }

    static bool IsConstructor(IMethodSymbol methodSymbol) => methodSymbol is IMethodSymbol
    {
        MethodKind: MethodKind.Constructor,
        DeclaredAccessibility: Accessibility.Internal or Accessibility.Public
    };

    private void AppendCancelToken(ref bool useIComma, StringBuilder code)
    {
        if (useIComma.Exchange(true)) code.Append(", ");

        code.Append("cancellationToken.Value");
    }

    internal void BuildAsParam(ref bool useIComma, StringBuilder code)
    {
        if (useIComma.Exchange(true)) code.Append(", ");

        BuildValue(code);
    }

    public void BuildValue(StringBuilder code)
    {
        if (IsAsync) code.Append("await ");

        if (IsFactory)
        {
            BuildFactoryCaller(code);
        }
        else if (!IsExternal && Lifetime is Lifetime.Transient)
        {
            BuildInstance(code);
        }
        else
        {
            BuildCachedCaller(code);
        }
    }

    internal void BuildAsExternalValue(StringBuilder code)
    {
        if (IsAsync) code.Append("await ");

        code.Append(ResolverMethodName).Append("()");
    }

    internal void BuildInstance(StringBuilder code)
    {
        code.Append("new ")
            .Append(FullTypeName)
            .Append('(');

        bool comma = false;

        BuildParams?.Invoke(ref comma, code);

        code.Append(')');
    }

    internal void BuildDisposeAsyncStatment(StringBuilder code)
    {
        code.Append(@"
        if (");

        if (RequiresDisposabilityCast)
            code.Append(CacheField).Append(" is global::System.IAsyncDisposable ").Append(CacheField).Append("AsyncDisposable) await ")
            .Append(CacheField)
            .Append("AsyncDisposable.DisposeAsync();");
        else
            code.Append(CacheField)
            .Append(" is not null) await ")
            .Append(CacheField)
            .Append(".DisposeAsync();");
    }

    internal void BuildDisposeStatment(StringBuilder code)
    {
        code.Append(@"
        ");

        if (RequiresDisposabilityCast)
            code.Append('(').Append(CacheField).Append(" as global::System.IDisposable)");
        else
            code.Append(CacheField);

        code.Append("?.Dispose();");
    }

    /// <summary>
    /// Tries to get the dependency information based on the provided attributes and parameters.
    /// </summary>
    /// <param name="model">The semantic model.</param>
    /// <param name="attrParamTypes">The types of the attribute parameters.</param>
    /// <param name="attrArgsSyntax">The syntax nodes of the attribute arguments.</param>
    /// <param name="attrParams">The parameter symbols of the attribute.</param>
    /// <param name="paramType">The type of the parameter.</param>
    /// <param name="key">The key of the dependency.</param>
    /// <param name="depInfo">The dependency information.</param>
    /// <returns><c>true</c> if the dependency information was successfully retrieved; otherwise, <c>false</c>.</returns>
    public static bool TryGetDependencyInfo(
        SemanticModel model,
        ImmutableArray<ITypeSymbol> attrParamTypes,
        SeparatedSyntaxList<AttributeArgumentSyntax> attrArgsSyntax,
        ImmutableArray<IParameterSymbol>? attrParams,
        ITypeSymbol? paramType,
        string key,
        out DependencySlimInfo depInfo)
    {
        depInfo = default;

        var isGeneric = attrParamTypes.Length > 0;

        if (isGeneric)
        {
            switch (attrParamTypes)
            {
                case [{ } t1, { } t2, ..]: depInfo.IFaceType = t1; depInfo.ImplType = t2; break;

                case [{ } t1]: depInfo.ImplType = t1; break;
            }
        }

        foreach (var (param, arg) in GetAttrParamsMap(attrParams ?? [], attrArgsSyntax))
        {
            switch (param.Name)
            {
                case ImplParamName when !isGeneric && arg is { Expression: TypeOfExpressionSyntax { Type: { } type } }:

                    depInfo.ImplType = (ITypeSymbol)model!.GetSymbolInfo(type).Symbol!;

                    continue;

                case IfaceParamName when !isGeneric && arg is { Expression: TypeOfExpressionSyntax { Type: { } type } }:

                    depInfo.IFaceType = (ITypeSymbol)model!.GetSymbolInfo(type).Symbol!;

                    continue;

                case KeyParamName when GetStrExpressionOrValue(model, param!, arg) is { } keyValue:

                    depInfo.Key = keyValue;

                    continue;

                case NameFormatParamName when GetStrExpressionOrValue(model, param, arg) is { } nameOrValue:

                    depInfo.NameFormat = nameOrValue;

                    continue;

                case FactoryOrInstanceParamName

                    when arg?.Expression is InvocationExpressionSyntax
                    {
                        Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" },
                        ArgumentList.Arguments: [{ } methodRef]
                    }:

                    switch (model.GetSymbolInfo(methodRef.Expression))
                    {
                        case { Symbol: (IFieldSymbol or IPropertySymbol) and { IsStatic: true, Kind: { } kind } fieldOrProp }:
                            depInfo.Factory = fieldOrProp;
                            depInfo.FactoryKind = kind;
                            break;

                        case { CandidateReason: CandidateReason.MemberGroup, CandidateSymbols: [IMethodSymbol { ReturnsVoid: false, IsStatic: true } method] }:
                            depInfo.Factory = method;
                            depInfo.FactoryKind = SymbolKind.Method;
                            depInfo.DefaultParamValues = method.Parameters;
                            break;
                    }

                    continue;

                case "disposability" when param.HasExplicitDefaultValue:

                    depInfo.Disposability = (Disposability)(byte)param.ExplicitDefaultValue!;

                    continue;
            }
        }

        depInfo.FinalType = depInfo.FactoryKind switch
        {
            SymbolKind.Method => ((IMethodSymbol)depInfo.Factory!).ReturnType,
            SymbolKind.Field => ((IFieldSymbol)depInfo.Factory!).Type,
            SymbolKind.Property => ((IPropertySymbol)depInfo.Factory!).Type,
            _ => depInfo.IFaceType ?? depInfo.ImplType ?? paramType!
        };

        if (paramType is { TypeKind: not TypeKind.Interface }) depInfo.ImplType ??= paramType;

        depInfo.Key ??= key ?? "";

        return depInfo is { FinalType: not null, ImplType: not null };
    }

    static IEnumerable<(IParameterSymbol, AttributeArgumentSyntax?)> GetAttrParamsMap(
        ImmutableArray<IParameterSymbol> paramSymbols,
        SeparatedSyntaxList<AttributeArgumentSyntax> argsSyntax)
    {
        int i = 0;
        foreach (var param in paramSymbols)
        {
            if (argsSyntax.Count > i && argsSyntax[i] is { NameColon: null, NameEquals: null } argSyntax)
            {
                yield return (param, argSyntax);
            }
            else
            {
                yield return (param, argsSyntax.FirstOrDefault(arg => param.Name == arg.NameColon?.Name.Identifier.ValueText));
            }

            i++;
        }
    }

    private static string? GetStrExpressionOrValue(SemanticModel model, IParameterSymbol paramSymbol, AttributeArgumentSyntax? arg)
    {
        if (arg is not null)
        {
            if (model.GetSymbolInfo(arg.Expression).Symbol is IFieldSymbol
                {
                    IsConst: true,
                    Type.SpecialType: SpecialType.System_String,
                    ConstantValue: { } val
                })
            {
                return val.ToString();
            }
            else if (arg.Expression is LiteralExpressionSyntax { Token.ValueText: { } value } e
                && e.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return value;
            }
        }
        else if (paramSymbol.HasExplicitDefaultValue)
        {
            return paramSymbol.ExplicitDefaultValue?.ToString();
        }

        return null;
    }
}

public record struct DependencySlimInfo(
    ITypeSymbol FinalType,
    ITypeSymbol? IFaceType,
    ITypeSymbol ImplType,
    ISymbol? Factory,
    SymbolKind FactoryKind,
    string Key,
    string? NameFormat,
    ImmutableArray<IParameterSymbol> DefaultParamValues,
    bool IsCached,
    Disposability Disposability,
    bool IsValid);
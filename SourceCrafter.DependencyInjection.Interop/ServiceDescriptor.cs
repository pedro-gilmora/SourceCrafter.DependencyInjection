using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Transactions;
using System.Xml.Linq;

namespace SourceCrafter.DependencyInjection.Interop;

public sealed class ServiceDescriptor(ITypeSymbol type, string typeName, string exportTypeFullName, string? key, ITypeSymbol? _interface = null)
{
    static readonly Lifetime[] lifetimes = [Lifetime.Singleton, Lifetime.Scoped, Lifetime.Scoped];

    public const string
        CancelTokenFQMetaName = "System.Threading.CancellationToken",
        EnumFQMetaName = "global::System.Enum",
        KeyParamName = "key",
        NameFormatParamName = "nameFormat",
        FactoryOrInstanceParamName = "factoryOrInstance",
        ImplParamName = "impl",
        IfaceParamName = "iface",
        CacheParamName = "cache",
        SingletonAttr = "global::SourceCrafter.DependencyInjection.Attributes.SingletonAttribute",
        ScopedAttr = "global::SourceCrafter.DependencyInjection.Attributes.ScopedAttribute",
        TransientAttr = "global::SourceCrafter.DependencyInjection.Attributes.TransientAttribute",
        DependencyAttr = "global::SourceCrafter.DependencyInjection.Attributes.DependencyAttribute",
        ServiceContainerAttr = "global::SourceCrafter.DependencyInjection.Attributes.ServiceContainerAttribute";

    public string FullTypeName = typeName;
    public string ResolverMethodName = null!;
    public string CacheField = null!;
    public ITypeSymbol Type = type;
    public ITypeSymbol? Interface = _interface;
    public ISymbol? Factory;
    public SymbolKind FactoryKind;
    public Lifetime Lifetime = Lifetime.Singleton;
    public bool IsCached = true;
    public string? Key = key;
    public Disposability Disposability;
    //public ValueBuilder GenerateValue = null!;
    public CommaSeparateBuilder? BuildParams = null!;
    public SemanticModel TypeModel = null!;
    public bool IsResolved;
    public ImmutableArray<AttributeData> Attributes = [];
    public ITypeSymbol ContainerType = null!;
    public bool NotRegistered = false;
    public bool IsAsync = false;
    public bool RequiresDisposabilityCast = false;
    internal bool IsCancelTokenParam;
    public bool IsExternal;
    public Guid ResolvedBy;

    public readonly string ExportTypeName = exportTypeFullName;

    private bool? isFactory;
    internal bool IsFactory => isFactory ??= Factory is not null;

    private bool? isNamed;

    internal bool IsKeyed => isNamed ??= Key is not null;

    internal ImmutableArray<IParameterSymbol>?
        Params,
        DefaultParamValues;

    void AppendFactoryContainingType(StringBuilder code)
    {
        if (!SymbolEqualityComparer.Default.Equals(Factory!.ContainingType, ContainerType))
        {
            code.Append(Factory.ContainingType.ToGlobalNamespaced()).Append('.');
        }
    }

    public void BuildSwitchBranch(StringBuilder code)
    {
        code.Append(@"
			case """)
            .Append(Key)
            .Append(@""" :
");

        BuildResolverBody(code, 2);

        code.Append(@"
");
    }

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
            .Append(IsAsync ? "Async(cancellationToken.Value" : "(")
            .Append(')');
    }

    internal void BuildResolver(StringBuilder code, bool isImplementation, string generatedCodeAttribute)
    {
        if (isImplementation)
        {
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
                .Append(@";

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
                .Append(FullTypeName)
                .Append("> ")
                .Append(ResolverMethodName);

            if (IsAsync && !ResolverMethodName.EndsWith("Async")) code.Append("Async");
            
            code.Append(@"(global::System.Threading.CancellationToken? cancellationToken = default)");
        }
        else
        {
            code.Append(FullTypeName)
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

    void BuildResolverBody(StringBuilder code, int indentSeeding = 0)
    {
        string? indentSeed = indentSeeding == 0 ? null : new(' ', indentSeeding * 4);

        if (IsCached)
        {
            var checkNullOnValueType = (Interface ?? Type) is { IsValueType: true, NullableAnnotation: not NullableAnnotation.Annotated };

            code.AppendFormat(@"
{0}		if (", indentSeed)
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
                code.AppendFormat(@"

{0}        await __globalSemaphore.WaitAsync(cancellationToken ??= __globalCancellationTokenSrc.Token);

{0}        try
{0}        {{
{0}            return ", indentSeed);

                code.Append(CacheField)
                    .Append(@" ??= ");

                BuildValue(code);

                code.AppendFormat(@";
{0}        }}
{0}        finally
{0}        {{
{0}            __globalSemaphore.Release();
{0}        }}", indentSeed);

            }
            else
            {
                code.AppendFormat(@"

{0}        lock(__lock) return ", indentSeed)
                    .Append(CacheField)
                    .Append(@" ??= ");

                BuildValue(code);

                code.Append(@";");
            }
        }
        else
        {
            code.AppendFormat(@"
{0}        return ", indentSeed);

            BuildInstance(code);

            code.Append(@";");
        }

        void BuildValue(StringBuilder code)
        {
            switch (Lifetime)
            {
                case Lifetime.Singleton:

                    if (IsFactory)
                    {
                        if (IsAsync && IsFactory) code.Append("await ");

                        BuildFactoryCaller(code);
                    }
                    else
                    {
                        BuildInstance(code);
                    }

                    break;
                case Lifetime.Scoped:

                    code
                        .AppendFormat(@"isScoped 
 {0}               ? ", indentSeed);

                    if (IsFactory)
                    {
                        if (IsAsync && IsFactory) code.Append("await ");

                        BuildFactoryCaller(code);
                    }
                    else
                    {
                        BuildInstance(code);
                    }

                    code
                        .AppendFormat(@"
 {0}               : throw InvalidCallOutOfScope(""", indentSeed)
                        .Append(FullTypeName)
                        .Append(@""");");

                    break;
                case Lifetime.Transient:

                    if (IsAsync && IsFactory) code.Append("await ");

                    BuildInstance(code);

                    break;
            }
        }
    }

    internal void BuildFactoryCaller(StringBuilder code)
    {
        bool comma = false;

        switch (Factory)
        {
            case IMethodSymbol method:

                AppendFactoryContainingType(code);

                if (method.TypeArguments is { IsDefaultOrEmpty: false } and [{ } argType]
                    && SymbolEqualityComparer.Default.Equals(argType, Type)
                    && SymbolEqualityComparer.Default.Equals(method.ReturnType, Type))
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


            case IPropertySymbol { IsIndexer: bool isIndexer } prop:

                AppendFactoryContainingType(code);

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


            case IFieldSymbol field:

                AppendFactoryContainingType(code);

                code.Append(field.Name);

                break;

            default:

                code.Append("default");

                if (Type?.IsNullable() is false) code.Append('!');

                break;
        }
    }

    internal void CheckParamsDependencies(
        SemanticModel model,
        DependencyMap entries,
        HashSet<string> methodsRegistry,
        Map<(int, Lifetime, string?), string> dependencyRegistry,
        Action updateAsyncContainerStatus,
        Compilation compilation,
        Set<Diagnostic> diagnostics,
        Guid generatorGuid,
        Action<ServiceDescriptor> onFoundService,
        ref Disposability disposability)
    {
        if (GetParameters() is not { IsDefaultOrEmpty: false } parameters) return;

        int resolvedDeps = 0;

        foreach (var param in parameters)
        {
            var paramType = param.Type;
            var paramTypeName = param.Type.ToGlobalNamespaced();

            if (param.GetAttributes() is { Length: > 0 } paramAttrs)
            {
                foreach (var attr in paramAttrs)
                {
                    INamedTypeSymbol attrClass = attr.AttributeClass!;

                    var isExternal = false;

                    if (attr.ApplicationSyntaxReference!.GetSyntax() is not AttributeSyntax attrSyntax
                        || attrClass is null
                        || GetLifetimeFromCtor(ref attrClass, ref isExternal, attrSyntax) is not { } lifetime) continue;

                    var isAsync = paramType.TryGetAsyncType(out var realParamType);

                    realParamType ??= paramType;

                    var attrParams = (IMethodSymbol)model.GetSymbolInfo(attr.ApplicationSyntaxReference.GetSyntax()).Symbol!;

                    GetDependencyInfo(
                        model,
                        attr.AttributeClass!.TypeArguments,
                        attrSyntax.ArgumentList?.Arguments ?? [],
                        attrParams.Parameters,
                        out var depInfo);


                    if (!depInfo.IsCached && lifetime is not Lifetime.Transient) depInfo.IsCached = true;

                    var found = entries.GetValueOrInserter((lifetime, paramTypeName, depInfo.Key), out var insertService);

                    if (paramType.IsPrimitive() && depInfo.Key is null)
                    {
                        diagnostics.TryAdd(
                            ServiceContainerGeneratorDiagnostics.PrimitiveDependencyShouldBeKeyed(
                                lifetime,
                                attrSyntax,
                                paramTypeName,
                                paramTypeName));

                        continue;
                    }

                    if (found != null)
                    {
                        if (found.IsAsync && !IsAsync) IsAsync = true;

                        updateAsyncContainerStatus();

                        resolvedDeps++;

                        BuildParams += found.BuildAsParam;

                        continue;
                    }

                    Disposability thisDisposability = Disposability.None;

                    if (depInfo.IsCached)
                    {
                        thisDisposability = depInfo.ImplType.GetDisposability();

                        if (thisDisposability > disposability) disposability = thisDisposability;

                        if (depInfo.Disposability > disposability) disposability = depInfo.Disposability;
                    }

                    var methodName = GetMethodName(isExternal, lifetime, depInfo, isAsync, methodsRegistry, dependencyRegistry);

                    found = new(realParamType, paramTypeName, paramTypeName, depInfo.Key, null)
                    {
                        Lifetime = lifetime,
                        Key = depInfo.Key,
                        RequiresDisposabilityCast = thisDisposability is Disposability.None && depInfo.Disposability is not Disposability.None, 
                        ResolverMethodName = methodName,
                        CacheField = "_" + methodName.Camelize(),
                        Factory = depInfo.Factory,
                        FactoryKind = depInfo.FactoryKind,
                        Disposability = (Disposability)Math.Max((byte)thisDisposability, (byte)depInfo.Disposability),
                        IsResolved = true,
                        ResolvedBy = generatorGuid,
                        Attributes = realParamType.GetAttributes(),
                        IsAsync = isAsync,
                        IsCached = depInfo.IsCached,
                        Params = Extensions.GetParameters(depInfo)
                    };

                    BuildParams += found.BuildAsParam;

                    found.CheckParamsDependencies(
                        model,
                        entries,
                        methodsRegistry,
                        dependencyRegistry,
                        updateAsyncContainerStatus,
                        compilation,
                        diagnostics,
                        generatorGuid, 
                        onFoundService,
                        ref disposability);

                    onFoundService(found);

                    resolvedDeps++;
                }
            }
            else if (paramTypeName.Equals("global::" + CancelTokenFQMetaName))
            {
                BuildParams += AppendCancelToken;
                resolvedDeps++;
            }
            else
            {
                foreach (var lifetime in lifetimes)
                {
                    if (entries.TryGetValue((lifetime, paramTypeName, null), out var found) || entries.TryGetValue((lifetime, paramTypeName, ""), out found))
                    {
                        BuildParams += found.BuildAsParam;
                        resolvedDeps++;
                        break;
                    }
                }
            }
        }

        if (resolvedDeps < parameters.Length && Type.DeclaringSyntaxReferences is [{ } first])
        {
            diagnostics.TryAdd(
                ServiceContainerGeneratorDiagnostics.DependencyWithUnresolvedParameters(
                    first.GetSyntax(),
                    ExportTypeName));
        }
    }
    public static string GetMethodName(bool isExternal, Lifetime lifetime, DependencySlimInfo depInfo, bool isAsync, HashSet<string> methodsRegistry, Map<(int, Lifetime, string?), string> dependencyRegistry)
    {
        var identifier = (depInfo.NameFormat is not null)
                    ? string.Format(depInfo.NameFormat, depInfo.Key).RemoveDuplicates()
                    : Extensions.SanitizeTypeName(depInfo.ImplType, methodsRegistry, dependencyRegistry, lifetime, depInfo.Key, depInfo.IsCached);

        identifier = isExternal ? identifier : depInfo.Factory?.ToNameOnly() ?? ("Get" + identifier);

        if (depInfo.Factory != null && depInfo.IsCached && !identifier.EndsWith("Cached") && !identifier.EndsWith("Cache")) identifier += "Cached";
        if (!identifier.EndsWith("Async") && isAsync) identifier += "Async";

        return identifier;
    }

    public static Lifetime? GetLifetimeFromCtor(ref INamedTypeSymbol attrClass, ref bool isExternal, AttributeSyntax attrSyntax)
    {
        var lifetime = GetLifetimeFromSyntax(attrSyntax);

        if(lifetime.HasValue) return lifetime.Value;

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
    }

    public static Lifetime? GetLifetimeFromSyntax(AttributeSyntax attribute)
    {
        if (attribute.ArgumentList?.Arguments
                .FirstOrDefault(x => x.NameColon?.Name.Identifier.ValueText is "lifetime")?.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: { } memberName } 
            && Enum.TryParse(memberName, out Lifetime lifetime))
        {
            return lifetime;
        }

        return null;
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

    public void BuildAsParam(ref bool useIComma, StringBuilder code)
    {
        if (useIComma.Exchange(true)) code.Append(", ");

        BuildValue(code);
    }

    internal     void BuildValue(StringBuilder code)
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

    internal void BuildDisposeAsyncStatment(StringBuilder code, string indent)
    {
        code.AppendFormat(@"
{0}        if (", indent);

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

    internal void BuildDisposeStatment(StringBuilder code, string indent)
    {
        code.AppendFormat(@"
{0}        ", indent);

        if (RequiresDisposabilityCast)
            code.Append('(').Append(CacheField).Append(" as global::System.IDisposable)");
        else
            code.Append(CacheField);
        
        code.Append("?.Dispose();");
    }

    public static void GetDependencyInfo(
        SemanticModel model,
        ImmutableArray<ITypeSymbol> attrParamTypes,
        SeparatedSyntaxList<AttributeArgumentSyntax> attrArgsSyntax,
        ImmutableArray<IParameterSymbol>? attrParams,
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
                case KeyParamName:

                    depInfo.Key = GetStrExpressionOrValue(model, param, arg);

                    continue;
                case NameFormatParamName:

                    depInfo.NameFormat = GetStrExpressionOrValue(model, param, arg);

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
                            depInfo.Factory = fieldOrProp; depInfo.FactoryKind = kind; break;

                        case { CandidateReason: CandidateReason.MemberGroup, CandidateSymbols: [IMethodSymbol { ReturnsVoid: false, IsStatic: true } method] }:
                            depInfo.Factory = method; depInfo.FactoryKind = SymbolKind.Method; depInfo.DefaultParamValues = method.Parameters; break;
                    }

                    continue;

                case CacheParamName:

                    depInfo.IsCached |= arg is not null && bool.TryParse(arg.Expression.ToString(), out var _cache) 
                        ? _cache
                        : param.HasExplicitDefaultValue && (bool)param.ExplicitDefaultValue!;

                    continue;

                case "disposability" when param.HasExplicitDefaultValue:

                    depInfo.Disposability = (Disposability)(byte)param.ExplicitDefaultValue!;

                    continue;
            }
        }
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
                return val.ToString().Pascalize();
            }
            else if (arg.Expression is LiteralExpressionSyntax { Token.ValueText: { } value } e
                && e.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return value.Pascalize();
            }
        }
        else if (paramSymbol.HasExplicitDefaultValue)
        {
            return paramSymbol.ExplicitDefaultValue?.ToString().Pascalize();
        }

        return null;
    }

    internal void AddBuilders(
        HashSet<(string?, string)> interfacesRegistry,
        ref CommaSeparateBuilder? interfaces,
        ref bool hasScopedService,
        ref Action<StringBuilder, string>? disposeStatments,
        ref Action<StringBuilder, string>? singletonDisposeStatments,
        ref bool requiresLocker,
        ref MemberBuilder? methods,
        ref Disposability disposability,
        Action updateAsyncStatus)
    {
        if (NotRegistered || IsCancelTokenParam) return;

        if (interfacesRegistry.Add((Key, ExportTypeName)))
        {
            interfaces += AddInterface;
        }

        if (Lifetime is Lifetime.Scoped && !hasScopedService) hasScopedService = true;

        if (Lifetime is not Lifetime.Transient)
        {
            if (IsAsync)
            {
                updateAsyncStatus();
            }
            else if (!requiresLocker)
            {
                requiresLocker = true;
            }

            switch (Disposability)
            {
                case Disposability.AsyncDisposable:

                    if (Lifetime is Lifetime.Scoped)
                    {
                        disposeStatments += BuildDisposeAsyncStatment;
                    }
                    else
                    {
                        singletonDisposeStatments += BuildDisposeAsyncStatment;
                    }

                    break;
                case Disposability.Disposable:

                    if (Lifetime is Lifetime.Scoped)
                    {
                        disposeStatments += BuildDisposeStatment;
                    }
                    else
                    {
                        singletonDisposeStatments += BuildDisposeStatment;
                    }
                    break;
            }
        }

        if (Disposability > disposability) disposability = Disposability;

        if (this is { IsExternal: true } or { IsFactory: true, IsCached: false }) return;

        methods += BuildResolver;
    }
}

public record struct DependencySlimInfo(ITypeSymbol? IFaceType,
    ITypeSymbol ImplType,
    ISymbol? Factory,
    SymbolKind FactoryKind,
    string? Key,
    string? NameFormat,
    ImmutableArray<IParameterSymbol> DefaultParamValues,
    bool IsCached,
    Disposability Disposability);
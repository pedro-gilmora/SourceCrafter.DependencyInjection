using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text;
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
    public bool Cached = true;
    public string? Key = key;
    public Disposability Disposability;
    public ValueBuilder GenerateValue = null!;
    public CommaSeparateBuilder? BuildParams = null!;
    public SemanticModel TypeModel = null!;
    public bool IsResolved;
    public ImmutableArray<AttributeData> Attributes = [];
    public ITypeSymbol ContainerType = null!;
    public bool NotRegistered = false;
    public bool IsAsync = false;
    internal bool IsCancelTokenParam;
    public bool ExternalGenerated;
    public Guid ResolvedBy;

    public readonly string ExportTypeName = exportTypeFullName;

    private bool? isFactory;
    internal bool IsFactory => isFactory ??= Factory is not null;


    internal readonly static Dictionary<int, string>
        genericParamNames = new()
        {
            {0, KeyParamName},
            {1, FactoryOrInstanceParamName },
            {2, CacheParamName }
        },
        paramNames = new()
        {
            {0, KeyParamName },
            {1, ImplParamName },
            {2, IfaceParamName },
            {3, FactoryOrInstanceParamName },
            {4, CacheParamName }
        };

    private bool? isNamed;

    internal bool IsKeyed => isNamed ??= Key is not null;

    internal ImmutableArray<IParameterSymbol>?
        Params,
        DefaultParamValues;

    public void BuildValue(StringBuilder code)
    {
        if (GenerateValue is not null)
        {
            GenerateValue(code);
        }
        else
        {
            code.Append("default");
        }
    }

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

    internal void UseCachedValueResolver(StringBuilder code)
    {
        code.Append(ResolverMethodName)
            .Append(IsAsync ? "Async(cancellationToken" : "(")
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
                .Append(ResolverMethodName)
                .Append(@"Async(global::System.Threading.CancellationToken cancellationToken = default)");
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

        if (Cached)
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

{0}        await __globalSemaphore.WaitAsync(cancellationToken);

{0}        try
{0}        {{
{0}            return ", indentSeed);

                code.Append(CacheField)
                    .Append(@" ??= ");

                BuildValueResolver(code);

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

                BuildValueResolver(code);

                code.Append(@";");
            }
        }
        else
        {
            code.AppendFormat(@"
{0}        return ", indentSeed);

            BuildValueResolver(code);

            code.Append(@";");
        }

        void BuildValueResolver(StringBuilder code)
        {
            if (Lifetime is Lifetime.Scoped)
            {
                code
                    .AppendFormat(@"isScoped 
 {0}               ? ", indentSeed);

                if (IsAsync && IsFactory) code.Append("await ");

                GenerateValue(code);

                code
                    .AppendFormat(@"
 {0}               : throw InvalidCallOutOfScope(""", indentSeed)
                    .Append(FullTypeName)
                    .Append(@""");");

            }
            else
            {
                if (IsAsync && IsFactory) code.Append("await ");

                GenerateValue(code);
            }
        }
    }

    internal void UseFactoryValueResolver(StringBuilder code)
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
        Guid generatorGuid)
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
                    bool isExternal = false;

                    if (attr.ApplicationSyntaxReference!.GetSyntax() is not AttributeSyntax attrSyntax
                        || attrClass is null
                        || GetLifetime(ref attrClass, ref isExternal) is not { } lifetime) continue;

                    var isAsync = paramType.TryGetAsyncType(out var realParamType);

                    realParamType ??= paramType;

                    var attrParams = (IMethodSymbol)model.GetSymbolInfo(attr.ApplicationSyntaxReference.GetSyntax()).Symbol!;

                    GetDependencyInfo(
                        model,
                        attr.AttributeClass!.TypeArguments,
                        attrSyntax.ArgumentList?.Arguments ?? [],
                        attrParams.Parameters,
                        out var depInfo);

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

                    var identifier = (depInfo.NameFormat is not null)
                        ? string.Format(depInfo.NameFormat, depInfo.Key).RemoveDuplicates()
                        : Extensions.SanitizeTypeName(paramType, methodsRegistry, dependencyRegistry, lifetime, key);

                    var disposability = paramType.GetDisposability();

                    if (disposability > Disposability) Disposability = disposability;

                    found = new(realParamType, paramTypeName, paramTypeName, key, null)
                    {
                        Lifetime = lifetime,
                        Key = key,
                        ResolverMethodName = isExternal ? identifier : "Get" + identifier,
                        CacheField = "_" + identifier.Camelize(),
                        Factory = depInfo.Factory,
                        FactoryKind = depInfo.FactoryKind,
                        Disposability = disposability,
                        IsResolved = true,
                        ResolvedBy = generatorGuid,
                        Attributes = realParamType.GetAttributes(),
                        IsAsync = isAsync,
                        Cached = depInfo.Cached ?? false,
                        Params = Extensions.GetParameters(depInfo)
                    };

                    found.GenerateValue = isExternal
                        ? found.BuildAsExternalValue
                        : found.IsFactory
                            ? found.UseFactoryValueResolver
                            : found.BuildValueInstance;

                    BuildParams += found.BuildAsParam;

                    found.CheckParamsDependencies(
                        model,
                        entries,
                        methodsRegistry,
                        dependencyRegistry,
                        updateAsyncContainerStatus,
                        compilation,
                        diagnostics,
                        generatorGuid);

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
                    if (entries.TryGetValue((lifetime, paramTypeName, null), out _))
                    {
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

        code.Append("cancellationToken");
    }

    public void BuildAsParam(ref bool useIComma, StringBuilder code)
    {
        if (useIComma.Exchange(true)) code.Append(", ");

        if (IsAsync) code.Append("await ");

        BuildValue(code);
    }

    internal void BuildAsExternalValue(StringBuilder code)
    {
        if (IsAsync) code.Append("await ");

        code.Append(ResolverMethodName).Append("()");
    }

    internal void BuildValueInstance(StringBuilder code)
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
		if(")
            .Append(CacheField)
            .Append(" is not null) await ")
            .Append(CacheField)
            .Append(".DisposeAsync();");
    }

    internal void BuildDisposeStatment(StringBuilder code)
    {
        code.Append(@"
		")
            .Append(CacheField)
            .Append("?.Dispose();");
    }

    public static Lifetime? GetLifetime(ref INamedTypeSymbol attrClass, ref bool isExternal)
    {
    check:
        switch (attrClass.ToGlobalNonGenericNamespace())
        {
            case SingletonAttr:

                return Lifetime.Singleton;

            case ScopedAttr:

                return Lifetime.Scoped;

            case TransientAttr:

                return Lifetime.Transient;

            case DependencyAttr:

                return Lifetime.Transient;

            case ServiceContainerAttr:

                return null;

            default:

                if (attrClass.BaseType is { } baseAttrType)
                {
                    isExternal = true;
                    attrClass = baseAttrType;

                    goto check;
                }

                return null;
        }
    }

    //public static void GetServiceParams(
    //    SemanticModel model,
    //    SeparatedSyntaxList<AttributeArgumentSyntax> _params,
    //    out string? key,
    //    out ISymbol? factory,
    //    out SymbolKind factoryKind,
    //    out bool? cache,
    //    out string? nameFormat,
    //    out ImmutableArray<IParameterSymbol> defaultParamValues)
    //{
    //    nameFormat = key = null;
    //    cache = true;

    //    factory = null;
    //    factoryKind = default;
    //    defaultParamValues = default;

    //    int i = 0;

    //    foreach (var arg in _params)
    //    {
    //        switch (arg.NameColon?.Name.Identifier.ValueText ?? paramNames[i])
    //        {
    //            case KeyParamName:

    //                if (model.GetSymbolInfo(arg.Expression).Symbol is IFieldSymbol
    //                    {
    //                        IsConst: true,
    //                        Type.SpecialType: SpecialType.System_String,
    //                        ConstantValue: { } val
    //                    })
    //                {
    //                    key = val.ToString().Pascalize();
    //                }
    //                else if (arg.Expression is LiteralExpressionSyntax { Token.ValueText: { } value } e 
    //                    && e.IsKind(SyntaxKind.StringLiteralExpression))
    //                {
    //                    key = value.Pascalize();
    //                }

    //                break;

    //            case NameFormatParamName:

    //                if (model.GetSymbolInfo(arg.Expression).Symbol is IFieldSymbol
    //                    {
    //                        IsConst: true,
    //                        Type.SpecialType: SpecialType.System_String,
    //                        ConstantValue: string val2
    //                    })
    //                {
    //                    nameFormat = val2.Pascalize();
    //                }
    //                else if (arg.Expression is LiteralExpressionSyntax { Token.ValueText: { } value } e 
    //                    && e.IsKind(SyntaxKind.StringLiteralExpression))
    //                {
    //                    nameFormat = value.Pascalize();
    //                }

    //                break;

    //            case FactoryOrInstanceParamName

    //                when arg.Expression is InvocationExpressionSyntax
    //                {
    //                    Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" },
    //                    ArgumentList.Arguments: [{ } methodRef]
    //                }:

    //                (factory, factoryKind, defaultParamValues) = model.GetSymbolInfo(methodRef.Expression) switch
    //                {
    //                    { Symbol: (IFieldSymbol or IPropertySymbol) and { IsStatic: true, Kind: { } kind } fieldOrProp }
    //                        => (fieldOrProp, kind, ImmutableArray<IParameterSymbol>.Empty),

    //                    { CandidateReason: CandidateReason.MemberGroup, CandidateSymbols: [IMethodSymbol { ReturnsVoid: false, IsStatic: true } method] }
    //                        => (method, SymbolKind.Method, method.Parameters),

    //                    _ => (null, default, ImmutableArray<IParameterSymbol>.Empty)
    //                };

    //                break;

    //            case CacheParamName when bool.TryParse(arg.Expression.ToString(), out var _cache):

    //                cache = _cache;

    //                break;
    //        }

    //        i++;
    //    }
    //}

    //internal static bool TryGetFactory(SemanticModel model, AttributeArgumentSyntax arg, out ISymbol? factory, out SymbolKind factoryKind)
    //{

    //    (factory, factoryKind) = (default, default);
    //    return false;
    //}

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

                case CacheParamName when arg is not null && bool.TryParse(arg.Expression.ToString(), out var _cache):

                    depInfo.Cached = _cache;

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
}

public record struct DependencySlimInfo(ITypeSymbol? IFaceType,
    ITypeSymbol ImplType,
    ISymbol? Factory,
    SymbolKind FactoryKind,
    string? Key,
    string? NameFormat,
    bool? Cached,
    ImmutableArray<IParameterSymbol> DefaultParamValues);

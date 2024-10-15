using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text;

namespace SourceCrafter.DependencyInjection.Interop;

internal delegate void CommaSeparateBuilder(ref bool useIComma, StringBuilder code, string baseIndent);
internal delegate void ValueBuilder(StringBuilder code);
internal delegate void MemberBuilder(StringBuilder code, bool isImplementation);
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
    private bool HasScopedDependencies;
    internal bool IsSimpleTransient;

    int deepParamsCount = 0;

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

    internal ImmutableArray<IParameterSymbol>
        Params,
        DefaultParamValues;

    internal void CheckParamsDependencies()
    {
        if (GetParameters() is not { IsDefaultOrEmpty: false } parameters)
        {
            if (!IsSimpleTransient && Lifetime is Lifetime.Transient)
            {
                IsSimpleTransient = true;
            }
            return;
        }

        deepParamsCount += parameters.Length;

        int resolvedDeps = 0;

        foreach (var param in parameters)
        {
            var paramType = param.Type;
            var paramTypeName = param.Type.ToGlobalNamespaced();

            var isExternal = false;

            Lifetime lifetime = Lifetime.Transient;
            ServiceDescriptor found = null!;
            ITypeSymbol finalType, implType = null!;
            ITypeSymbol? iFaceType = finalType = iFaceType = null!;
            ISymbol? factory = default;
            SymbolKind factoryKind = default;
            string outKey = null!;
            string? nameFormat = default;
            ImmutableArray<IParameterSymbol> defaultParamValues = [];
            bool isCached = false;
            Disposability _disposability = Disposability.None;
            bool isValid = false;
            AttributeSyntax attrSyntax = null!;

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
                    if (ServiceContainer.Model.TryGetDependencyInfo(
                        attr,
                        ref isExternal,
                        param.Name,
                        paramType,
                        out lifetime,
                        out finalType,
                        out iFaceType,
                        out implType,
                        out factory,
                        out factoryKind,
                        out outKey,
                        out nameFormat,
                        out defaultParamValues,
                        out isCached,
                        out _disposability,
                        out isValid,
                        out attrSyntax)) break;
                }

                if (!HasScopedDependencies && Lifetime is not Lifetime.Scoped && lifetime is Lifetime.Scoped)
                {
                    HasScopedDependencies = true;
                }
            }
            else
            {
                foreach (var lifeTime in lifetimes)
                {
                    if (ServiceContainer.ServicesMap.TryGetValue((lifeTime, paramTypeName, param.Name), out found)
                        || ServiceContainer.ServicesMap.TryGetValue((lifeTime, paramTypeName, ""), out found))
                    {
                        if (found.IsAsync && !IsAsync)
                        {
                            IsAsync = true;

                            if (!ServiceContainer.requiresSemaphore) ServiceContainer.UpdateAsyncStatus();
                        }

                        deepParamsCount += found.Params.Length;

                        resolvedDeps++;

                        BuildParams += found.BuildAsParam;

                        goto check;
                    }
                }

                if (param.Type.TypeKind is TypeKind.Interface)
                {
                    continue;
                }

                if (!param.Name.Equals(param.Type.ToNameOnly(), StringComparison.OrdinalIgnoreCase))
                    outKey = param.Name;
                else
                    outKey = "";

                implType = finalType = paramType;

                isValid = true;
            }

            if (!isCached && lifetime is not Lifetime.Transient) isCached = true;

            var isAsync = finalType.TryGetAsyncType(out var realParamType);

            if (isAsync)
            {
                finalType = realParamType!;

                if (!IsAsync) IsAsync = true;

                if (!ServiceContainer.requiresSemaphore) ServiceContainer.UpdateAsyncStatus();

                if (factoryKind is SymbolKind.Method
                    && !((IMethodSymbol)factory!).Parameters.Any(p => p.Type.ToDisplayString() is CancelTokenFQMetaName))
                {
                    ServiceContainer.Diagnostics.TryAdd(
                        ServiceContainerGeneratorDiagnostics
                            .CancellationTokenShouldBeProvided(factory, OriginDefinition));
                }
            }

            found = ServiceContainer.ServicesMap.GetValueOrInserter((lifetime, paramTypeName, outKey), out var insertService);

            if (found != null)
            {
                if (found.IsAsync && !IsAsync)
                {
                    IsAsync = true;

                    if (!ServiceContainer.requiresSemaphore) ServiceContainer.UpdateAsyncStatus();
                }

                resolvedDeps++;

                BuildParams += found.BuildAsParam;

                deepParamsCount += found.Params.Length;

                continue;
            }

            var paramSyntax = param.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() ?? OriginDefinition;

            if (found is null && implType is null && paramType.TypeKind is TypeKind.Interface)
            {
                ServiceContainer.Diagnostics.TryAdd(
                    ServiceContainerGeneratorDiagnostics
                        .ParamInterfaceTypeWithoutImplementation(
                            paramSyntax,
                            paramTypeName,
                            ContainerType.ToGlobalNamespaced()));

                resolvedDeps++;

                BuildParams += AddDefault;

                deepParamsCount += 1;

                continue;
            }

            if (!isExternal && paramType.IsPrimitive() && outKey is "")
            {
                ServiceContainer.Diagnostics.TryAdd(
                    ServiceContainerGeneratorDiagnostics.PrimitiveDependencyShouldBeKeyed(
                        lifetime,
                        paramSyntax,
                        paramTypeName,
                        paramTypeName));

                resolvedDeps++;

                BuildParams += AddDefault;

                deepParamsCount += 1;

                continue;
            }

            Disposability thisDisposability = Disposability.None;

            if (isCached)
            {
                thisDisposability = finalType.GetDisposability();

                if (thisDisposability > ServiceContainer.disposability) ServiceContainer.disposability = thisDisposability;

                if (_disposability > ServiceContainer.disposability) ServiceContainer.disposability = _disposability;
            }

            if (!isValid)
            {
                resolvedDeps++;

                BuildParams += AddDefault;

                deepParamsCount += 1;

                continue;
            }

            string methodName = GetMethodName(isExternal, lifetime, finalType, implType, factory, outKey, nameFormat, isCached, isAsync, ServiceContainer.MethodsRegistry, ServiceContainer.MethodNamesMap);

            found = new(finalType, outKey, null)
            {
                ServiceContainer = ServiceContainer,
                Lifetime = lifetime,
                Key = outKey,
                FullTypeName = implType!.ToGlobalNamespaced(),
                ExportTypeName = paramTypeName,
                RequiresDisposabilityCast = thisDisposability is Disposability.None && _disposability is not Disposability.None,
                ResolverMethodName = methodName,
                CacheField = "_" + methodName.Camelize(),
                Factory = factory,
                FactoryKind = factoryKind,
                Disposability = (Disposability)Math.Max((byte)thisDisposability, (byte)_disposability),
                IsResolved = true,
                Attributes = finalType.GetAttributes(),
                IsAsync = isAsync,
                IsCached = isCached,
                Params = implType!.GetParameters(),
                ContainerType = ContainerType
            };

            insertService(found);

            deepParamsCount += found.Params.Length;

            BuildParams += found.BuildAsParam;

            ServiceContainer.ResolveService(found);

            if (found.IsAsync && !IsAsync)
            {
                IsAsync = true;

                if (!ServiceContainer.requiresSemaphore) ServiceContainer.UpdateAsyncStatus();
            }

            resolvedDeps++;

            check:;

            void AddDefault(ref bool comma, StringBuilder code, string newIndentLine)
            {
                if (!comma) comma = true; else code.Append(", ");

                code.Append("default");

                if (!paramType.IsNullable() && paramType.AllowsNull()) code.Append('!');
            }
        }

        if (resolvedDeps < parameters.Length && Type.DeclaringSyntaxReferences is [{ } first])
        {
            ServiceContainer.Diagnostics.TryAdd(
                ServiceContainerGeneratorDiagnostics.DependencyWithUnresolvedParameters(
                    first.GetSyntax(),
                    ExportTypeName));
        }
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
            .Append(IsAsync ? "(cancellationToken.Value" : "(")
            .Append(')');
    }

    internal void BuildMethod(StringBuilder code, bool isImplementation)
    {
        if (isImplementation)
        {
            ServiceContainer.CheckMethodUsage(Lifetime is Lifetime.Scoped || HasScopedDependencies, ResolverMethodName);

            if (Lifetime is not Lifetime.Transient) 
            {
                code.Append(@"
    private ");

                if (Lifetime is Lifetime.Singleton) code.Append("static ");

                code.Append(FullTypeName)
                    .Append("? ")
                    .Append(CacheField)
                    .Append(@" = null;
");
            }

            code.Append(@"
    public ");
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

        if (IsCached)
        {
            var checkNullOnValueType = ( Type) is { IsValueType: true, NullableAnnotation: not NullableAnnotation.Annotated };

            code.Append(@"
        if (")
                .Append(CacheField)
                .Append(checkNullOnValueType ? ".HasValue" : " is not null")
                .Append(@") return ")
                .Append(CacheField)
                .Append(checkNullOnValueType ? ".Value;" : ";");

            if (IsAsync)
            {
                code.Append(@"

        await __globalSemaphore.WaitAsync(cancellationToken ??= __globalCancellationTokenSrc.Token);

        try
        {
            return ");

                code.Append(CacheField)
                    .Append(@" ??= ");

                AppendBuilder(code, @"
                ");

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

        lock(__lock) 

            return ")
                    .Append(CacheField)
                    .Append(@" ??= ");

                AppendBuilder(code, @"
                ");

                code.Append(@";");
            }
        }
        else
        {
            if (IsAsync)
            {
                code.Append(@"
        cancellationToken ??= __globalCancellationTokenSrc.Token;
");
            }

            code.Append(@"
        return ");

            BuildInstance(code, @"
            ");

            code.Append(@";");
        }

        void AppendBuilder(StringBuilder code, string newIndentedLine)
        {
            if (IsFactory)
            {
                if (IsAsync && IsFactory) code.Append("await ");

                BuildFactoryCaller(code, newIndentedLine);
            }
            else
            {
                BuildInstance(code, newIndentedLine);
            }
        }

        code.Append(@"
    }
");
    }

    internal void BuildFactoryCaller(StringBuilder code, string newIndentedLine)
    {
        bool comma = false;

        if (deepParamsCount < 2) newIndentedLine = "";

        switch (Factory)
        {
            case IMethodSymbol { ContainingType: { } containingType, IsStatic: { } isStatic } method:

                if (isStatic) AppendFactoryContainingType(code, containingType);

                if (method is { Name: "Task" or "ValueTask", TypeArguments: { IsDefaultOrEmpty: false } and [{ } argType] }
                    && SymbolEqualityComparer.Default.Equals(argType, Type))
                {
                    code.Append(method.Name)
                        .Append('<')
                        .Append(ExportTypeName)
                        .Append(">(");

                    BuildParams?.Invoke(ref comma, code, newIndentedLine + "    ");

                    code.Append(')');
                }
                else
                {
                    code.Append(method.Name)
                        .Append('(');

                    comma = false;
                    BuildParams?.Invoke(ref comma, code, newIndentedLine + "    ");

                    code.Append(')');
                }

                break;


            case IPropertySymbol { IsIndexer: bool isIndexer, ContainingType: { } containingType, IsStatic: { } isStatic } prop:

                if (isStatic) AppendFactoryContainingType(code, containingType);

                if (isIndexer)
                {
                    code.Append(prop.Name)
                        .Append('[');

                    comma = false;
                    BuildParams?.Invoke(ref comma, code, newIndentedLine + "    ");

                    code.Append(']');
                }
                else
                {
                    code.Append(prop.Name);
                }

                break;


            case IFieldSymbol { ContainingType: { } containingType, IsStatic: { } isStatic } field:

                if (isStatic) AppendFactoryContainingType(code, containingType);

                code.Append(newIndentedLine)
                    .Append("    ")
                    .Append(field.Name);

                break;

            default:

                AppendDefault(code, newIndentedLine);

                break;
        }
    }

    private void AppendDefault(StringBuilder code, string newIndentedLine)
    {
        if(deepParamsCount > 1) code.Append(newIndentedLine).Append("    ");
        
        code.Append("default");

        if (Type?.IsNullable() is false) code.Append('!');
    }

    private void AppendFactoryContainingType(StringBuilder code, INamedTypeSymbol containingType)
    {
        if (SymbolEqualityComparer.Default.Equals(containingType, ContainerType))
            return;

        code.Append(containingType.ToGlobalNamespaced()).Append('.');
    }

    internal static string GetMethodName(
        bool isExternal,
        Lifetime lifetime,
        ITypeSymbol finalType,
        ITypeSymbol? implType,
        ISymbol? factory,
        string key,
        string? nameOrFormat,
        bool isCached,
        bool isAsync,
        HashSet<string> methodsRegistry,
        DependencyNamesMap dependencyRegistry)
    {
        var identifier = nameOrFormat is not null
            ? string.Format(nameOrFormat, key.Pascalize()!).RemoveDuplicates()
            : Extensions.SanitizeTypeName(implType ?? finalType, methodsRegistry, dependencyRegistry, lifetime, key.Pascalize()!);

        identifier = isExternal ? identifier : factory?.ToNameOnly() ?? ("Get" + identifier);

        if (factory != null && isCached && !identifier.EndsWith("Cached") && !identifier.EndsWith("Cache")) identifier += "Cached";
        if (!identifier.EndsWith("Async") && isAsync) identifier += "Async";

        return identifier;
    }


    internal ImmutableArray<IParameterSymbol> GetParameters()
    {
        return Factory switch
        {
            IMethodSymbol factoryMethod => factoryMethod.Parameters,
            IPropertySymbol { IsIndexer: true } factoryProperty => factoryProperty.Parameters,
            IFieldSymbol => [],
            _ => Params
        };
    }

    private void AppendCancelToken(ref bool useIComma, StringBuilder code, string newIndentedLine)
    {
        if (useIComma.Exchange(true)) code.Append(", ");

        if (deepParamsCount > 1) code.Append(newIndentedLine).Append("    ");

        code.Append("cancellationToken.Value");
    }

    internal void BuildAsParam(ref bool useIComma, StringBuilder code, string newIndentedLine)
    {
        if (useIComma.Exchange(true)) code.Append(", ");

        code.Append(newIndentedLine).Append("    ");

        if (IsFactory)
        {
            if (IsAsync) code.Append("await ");

            BuildFactoryCaller(code, newIndentedLine);
        }
        else if (!IsExternal && Lifetime is Lifetime.Transient)
        {
            BuildInstance(code, newIndentedLine);
        }
        else
        {
            if (IsAsync) code.Append("await ");

            BuildCachedCaller(code);
        }
    }

    internal void BuildAsExternalValue(StringBuilder code)
    {
        if (IsAsync) code.Append("await ");

        code.Append(ResolverMethodName).Append("()");
    }

    internal void BuildInstance(StringBuilder code, string newIndentedLine)
    {
        code.Append("new ")
            .Append(FullTypeName)
            .Append('(');

        bool comma = false;

        if (deepParamsCount < 2) newIndentedLine = "";

        BuildParams?.Invoke(ref comma, code, newIndentedLine + "    ");

        code.Append(')');
    }

    internal void BuildDisposeAsyncStatment(StringBuilder code, string? indent)
    {
        code.AppendLine().Append(indent).Append("        if (");

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

    internal void BuildDisposeStatment(StringBuilder code, string? indent)
    {
        code.AppendLine().Append(indent).Append("        ");

        if (RequiresDisposabilityCast)
            code.Append('(').Append(CacheField).Append(" as global::System.IDisposable)");
        else
            code.Append(CacheField);

        code.Append("?.Dispose();");
    }
}
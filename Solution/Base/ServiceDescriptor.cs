using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;

using static SourceCrafter.DependencyInjection.Helpers;

namespace SourceCrafter.DependencyInjection;

internal delegate void CommaSeparateBuilder(ref bool useIComma, StringBuilder code, string baseIndent);
internal delegate void ValueBuilder(StringBuilder code);
internal delegate void MemberBuilder(StringBuilder code, bool isImplementation);
internal delegate void ParamsBuilder(StringBuilder code);

enum GenericType { None, JustImplementationType, InterfaceAndImplememtation }
internal sealed class ServiceDescriptor(ITypeSymbol type, string key, ITypeSymbol? _interface = null)
{
    static readonly Lifetime[] lifetimes = [Lifetime.Singleton, Lifetime.Scoped, Lifetime.Transient];

    internal const string
        CancelTokenFQMetaName = "System.Threading.CancellationToken",
        EnumFQMetaName = "global::System.Enum",
        KeyParamName = "key",
        NameFormatParamName = "nameFormat",
        SourceParamName = "source",
        ImplParamName = "impl",
        IfaceParamName = "iface",
        SingletonAttr = "global::SourceCrafter.DependencyInjection.Attributes.SingletonAttribute",
        ScopedAttr = "global::SourceCrafter.DependencyInjection.Attributes.ScopedAttribute",
        TransientAttr = "global::SourceCrafter.DependencyInjection.Attributes.TransientAttribute",
        DependencyAttr = "global::SourceCrafter.DependencyInjection.Attributes.DependencyAttribute",
        ServiceContainerAttr = "global::SourceCrafter.DependencyInjection.Attributes.ServiceContainerAttribute";

    internal string FullTypeName = null!;
    internal string ResolverMethodName = null!;
    internal string CacheField = null!;
    internal ITypeSymbol Type = type;
    internal ITypeSymbol? Interface = _interface;
    internal ISymbol? Factory;
    internal SymbolKind FactoryKind;
    internal Lifetime Lifetime = Lifetime.Singleton;
    internal bool IsCached = true;
    internal string Key = key;
    internal Disposability Disposability;
    //internal ValueBuilder GenerateValue = null!;
    internal CommaSeparateBuilder? buildParams = null!;
    internal SemanticModel TypeModel = null!;
    internal bool IsResolved;
    internal ImmutableArray<AttributeData> Attributes = [];
    internal ITypeSymbol ContainerType = null!;
    internal bool NotRegistered = false;
    internal bool RequiresDisposabilityCast = false;
    internal bool IsCancelTokenParam;
    internal bool IsExternal;
    internal AttributeSyntax OriginDefinition = null!;
    internal ServiceContainer ServiceContainer = null!;
    internal string ExportTypeName = null!;
    internal bool HasScopedDependencies;
    /// <summary>
    /// Indicates that this service is registered as a simple transient service, without any dependencies. 
    /// Otherwise will be considered a complex transient service and it deserves a separate resolver method.
    /// </summary>
    internal bool IsSimpleTransient;

    int deepParamsCount = 0;

    public Disposability ContainerDisposability => ServiceContainer.Disposability;

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
            var paramTypeId = SymbolEqualityComparer.Default.GetHashCode(paramType);
            var paramTypeName = param.Type.ToGlobalNamespaced();
            string paramName = param.Name;
            int keyHash = paramName.GetHashCode();

            ServiceDescriptor? found = null;
            DependencyInfo depInfo = default;

            if (paramTypeName.Equals("global::" + CancelTokenFQMetaName))
            {
                resolvedDeps++;
                buildParams += AppendCancelToken;
                continue;
            }

            if (param.GetAttributes() is { Length: > 0 } paramAttrs)
            {
                foreach (var attr in paramAttrs)
                {
                    if (ServiceContainer.Model.TryGetDependencyInfo(attr, ServiceContainer.externalAssemblies, param.Name, paramType, out depInfo))
                    {
                        break;
                    }
                }

                if (!HasScopedDependencies && Lifetime is not Lifetime.Scoped && depInfo.Lifetime is Lifetime.Scoped)
                {
                    HasScopedDependencies = true;
                }
            }
            else
            {
                foreach (var lifeTime in lifetimes)
                {
                    if (ServiceContainer.ServicesMap.TryGetValue((lifeTime, paramTypeId, keyHash), out found)
                        || ServiceContainer.ServicesMap.TryGetValue((lifeTime, paramTypeId, EmptyStringHashCode), out found))
                    {
                        if (found.IsAsync && !IsAsync)
                        {
                            IsAsync = true;

                            if (!ServiceContainer.requiresSemaphore) ServiceContainer.UpdateAsyncStatus();
                        }

                        deepParamsCount += found.Params.Length;

                        resolvedDeps++;

                        buildParams += found.BuildAsParam;

                        goto exit;
                    }
                }

                if (param.Type.TypeKind is TypeKind.Interface)
                {
                    continue;
                }

                depInfo = new()
                {
                    Key = paramName = (!param.Name.Equals(paramType.ToNameOnly(), StringComparison.OrdinalIgnoreCase))
                         ? param.Name
                         : "",
                    KeyHash = paramName.GetHashCode(),
                    Type = paramType,
                    FinalType = paramType,
                    IsValid = true,
                    IsCached = true
                };

            }            

            if (depInfo.IsAsync)
            {
                if (!IsAsync) IsAsync = true;

                if (!ServiceContainer.requiresSemaphore) ServiceContainer.UpdateAsyncStatus();

                if (depInfo.FactoryKind is SymbolKind.Method
                    && !((IMethodSymbol)depInfo.Factory!).Parameters.Any(p => p.Type.ToDisplayString() is CancelTokenFQMetaName))
                {
                    ServiceContainer.Diagnostics.TryAdd(
                        ServiceContainerGeneratorDiagnostics
                            .CancellationTokenShouldBeProvided(depInfo.Factory, OriginDefinition));
                }
            }

            found = ServiceContainer.ServicesMap.GetValueOrInserter((depInfo.Lifetime, paramTypeId, depInfo.KeyHash), out var insertService);

            if (found != null)
            {
                if (found.IsAsync && !IsAsync)
                {
                    IsAsync = true;

                    if (!ServiceContainer.requiresSemaphore) ServiceContainer.UpdateAsyncStatus();
                }

                resolvedDeps++;

                buildParams += found.BuildAsParam;

                deepParamsCount += found.Params.Length;

                continue;
            }

            var paramSyntax = param.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() ?? OriginDefinition;

            if (found is null && depInfo.Type is null && paramType.TypeKind is TypeKind.Interface)
            {
                ServiceContainer.Diagnostics.TryAdd(
                    ServiceContainerGeneratorDiagnostics
                        .ParamInterfaceTypeWithoutImplementation(
                            paramSyntax,
                            paramTypeName,
                            ContainerType.ToGlobalNamespaced()));

                resolvedDeps++;

                buildParams += AddDefault;

                deepParamsCount += 1;

                continue;
            }

            if (!depInfo.IsExternal && paramType.IsPrimitive() && depInfo.Key is "")
            {
                ServiceContainer.Diagnostics.TryAdd(
                    ServiceContainerGeneratorDiagnostics
                        .PrimitiveDependencyShouldBeKeyed(depInfo.Lifetime, paramSyntax, paramTypeName, paramTypeName));

                resolvedDeps++;

                buildParams += AddDefault;

                deepParamsCount += 1;

                continue;
            }

            Disposability thisDisposability = Disposability.None;

            if (depInfo.IsCached)
            {
                thisDisposability = depInfo.FinalType.GetDisposability();

                if (thisDisposability > ServiceContainer.Disposability) ServiceContainer.Disposability = thisDisposability;

                if (depInfo.Disposability > ServiceContainer.Disposability) ServiceContainer.Disposability = depInfo.Disposability;
            }

            if (!depInfo.IsValid)
            {
                resolvedDeps++;

                buildParams += AddDefault;

                deepParamsCount += 1;

                continue;
            }

            var (backingFieldName, methodName) = depInfo.GetMethodName(ServiceContainer.MethodsRegistry, ServiceContainer.MethodNamesMap);

            found = new(depInfo.FinalType, depInfo.Key)
            {
                ServiceContainer = ServiceContainer,
                Lifetime = depInfo.Lifetime,
                Key = depInfo.Key,
                FullTypeName = depInfo.Type!.ToGlobalNamespaced(),
                ExportTypeName = paramTypeName,
                RequiresDisposabilityCast = thisDisposability is Disposability.None && depInfo.Disposability is not Disposability.None,
                ResolverMethodName = methodName,
                CacheField = backingFieldName,
                Factory = depInfo.Factory,
                FactoryKind = depInfo.FactoryKind,
                Disposability = (Disposability)Math.Max((byte)thisDisposability, (byte)depInfo.Disposability),
                IsResolved = true,
                Attributes = depInfo.FinalType.GetAttributes(),
                IsAsync = depInfo.IsAsync,
                IsCached = depInfo.IsCached,
                Params = depInfo.Type!.GetParameters(),
                ContainerType = ContainerType
            };

            insertService(found);

            deepParamsCount += found.Params.Length;

            buildParams += found.BuildAsParam;

            ServiceContainer.ResolveService(found);

            if (found.IsAsync && !IsAsync)
            {
                IsAsync = true;

                if (!ServiceContainer.requiresSemaphore) ServiceContainer.UpdateAsyncStatus();
            }

            resolvedDeps++;

            exit:;

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

    internal void BuildCachedCaller(StringBuilder code)
    {
        code.Append(ResolverMethodName)
            .Append(IsAsync ? "(cancellationToken.Value" : "(")
            .Append(')');
    }

    internal void BuildMethod(StringBuilder code, bool isImplementation)
    {
#if DISG
        if(IsExternal) Trace.WriteLine($"SCDI: Building {ResolverMethodName}: {FullTypeName}#{SymbolEqualityComparer.Default.GetHashCode(Type)}");
#endif

        if (isImplementation)
        {
            if ((Lifetime is Lifetime.Scoped || HasScopedDependencies) 
                && ServiceContainer.ServiceCalls.TryGetValue((ServiceContainer.ProviderId, ResolverMethodName, true), out var el))
            {
                ServiceContainer.Diagnostics.TryAdd(
                    ServiceContainerGeneratorDiagnostics.DependencyCallMustBeScoped(ServiceContainer.ProviderTypeName, el.MethodSyntax));
            }

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

        string singletonDash = Lifetime is Lifetime.Singleton ? "_" : "";

        if (IsCached)
        {
            var checkNullOnValueType = Type is { IsValueType: true, NullableAnnotation: not NullableAnnotation.Annotated };

            code.Append(@"
        if (")
                .Append(CacheField)
                .Append(checkNullOnValueType ? ".HasValue" : " is not null")
                .Append(@") return ")
                .Append(CacheField)
                .Append(checkNullOnValueType ? ".Value;" : ";");

            if (IsAsync)
            {
                code.AppendFormat(@"

        await __{0}globalSemaphore.WaitAsync(cancellationToken ??= __{0}globalCancellationTokenSrc.Token);

        try
        {{
            return ", singletonDash);

                code.Append(CacheField)
                    .Append(@" ??= ");

                AppendBuilder(code, @"
                ");

                code.AppendFormat(@";
        }}
        finally
        {{
            __{0}globalSemaphore.Release();
        }}", singletonDash);

            }
            else
            {
                code.AppendFormat(@"

        lock(__{0}lock) 

            return ", singletonDash)
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
                code.AppendFormat(@"
        cancellationToken ??= __{0}globalCancellationTokenSrc.Token;
", singletonDash);
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

                    buildParams?.Invoke(ref comma, code, newIndentedLine + "    ");

                    code.Append(')');
                }
                else
                {
                    code.Append(method.Name)
                        .Append('(');

                    comma = false;
                    buildParams?.Invoke(ref comma, code, newIndentedLine + "    ");

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
                    buildParams?.Invoke(ref comma, code, newIndentedLine + "    ");

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
                    
                    .Append(field.Name);

                break;

            default:

                AppendDefault(code, newIndentedLine);

                break;
        }
    }

    private void AppendDefault(StringBuilder code, string newIndentedLine)
    {
        if(deepParamsCount > 1) code.Append(newIndentedLine);
        
        code.Append("default");

        if (Type?.IsNullable() is false) code.Append('!');
    }

    private void AppendFactoryContainingType(StringBuilder code, INamedTypeSymbol containingType)
    {
        if (SymbolEqualityComparer.Default.Equals(containingType, ContainerType))
            return;

        code.Append(containingType.ToGlobalNamespaced()).Append('.');
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

        if (deepParamsCount > 1) code.Append(newIndentedLine);

        code.Append("cancellationToken.Value");
    }

    internal void BuildAsParam(ref bool useIComma, StringBuilder code, string newIndentedLine)
    {
        if (useIComma.Exchange(true)) code.Append(", ");

        code.Append(newIndentedLine);

        BuildAsValue(code, newIndentedLine);
    }

    internal void BuildAsValue(StringBuilder code, string newIndentedLine = "", bool shouldAwait = true)
    {
        if (IsFactory)
        {
            if (shouldAwait && IsAsync) code.Append("await ");

            BuildFactoryCaller(code, newIndentedLine);
        }
        else if (!IsExternal && Lifetime is Lifetime.Transient)
        {
            BuildInstance(code, newIndentedLine);
        }
        else
        {
            if (shouldAwait && IsAsync) code.Append("await ");

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

        buildParams?.Invoke(ref comma, code, newIndentedLine);

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
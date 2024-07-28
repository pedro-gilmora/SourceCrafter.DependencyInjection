using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;

using System.Linq;

using System.Text;

using System;
using SourceCrafter.DependencyInjection.Interop;
using Microsoft.CodeAnalysis.CSharp;

using static SourceCrafter.DependencyInjection.Interop.ServiceDescriptor;

namespace SourceCrafter.DependencyInjection;


class ServiceContainerGenerator
{
    readonly string providerTypeName, providerClassName;

    readonly ImmutableArray<AttributeData> attributes;

    readonly SemanticModel _model;
    private readonly Set<Diagnostic> _diagnostics;
    readonly INamedTypeSymbol _providerClass;
    private readonly Compilation _compilation;
    readonly StringBuilder code = new(@"#nullable enable
");

    readonly HashSet<(bool, string)> enumKeysRegistry = [];
    readonly HashSet<string>
        propsRegistry = [],
        interfacesRegistry = [];

    readonly Set<ServiceDescriptor> entries = new(Extensions.GenKey);


    readonly Map<string, Action>
        keyedMethods = new(StringComparer.Ordinal);

    CommaSeparateBuilder? interfaces = null;

    MemberBuilder?
        methods = null,
        props = null;

    Action<StringBuilder>?
        singletonDisposeStatments = null,
        disposeStatments = null;

    bool useIComma = false,
        hasScopedService = false,
        requiresLocker = false,
        requiresSemaphore = false;

    readonly bool
        hasService = false,
        hasAsyncService = false;

    readonly Disposability disposability = 0;

    readonly static Dictionary<int, string>
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

    public ServiceContainerGenerator(INamedTypeSymbol providerClass, Compilation compilation, SemanticModel model, Set<Diagnostic> diagnostics)
    {
        _providerClass = providerClass;
        _compilation = compilation;
        _model = model;
        _diagnostics = diagnostics;
        providerTypeName = _providerClass.ToGlobalNamespaced();
        providerClassName = _providerClass.ToNameOnly();
        attributes = _providerClass.GetAttributes();

        foreach (var attr in attributes)
        {
            Lifetime lifetime;

            switch (attr.AttributeClass!.ToGlobalNonGenericNamespace())
            {
                case SingletonAttr:
                    lifetime = Lifetime.Singleton;
                    break;
                case ScopedAttr:
                    lifetime = Lifetime.Scoped;
                    break;
                case TransientAttr:
                    lifetime = Lifetime.Transient;
                    break;
                default: continue;
            };

            ITypeSymbol type;
            ITypeSymbol? iface;
            ISymbol? factory;
            SymbolKind factoryKind;
            IFieldSymbol? key;
            bool? cache;

            var attrSyntax = (AttributeSyntax)attr.ApplicationSyntaxReference!.GetSyntax();

            var _params = attrSyntax
                        .ArgumentList?
                        .Arguments ?? default;

            if (attr.AttributeClass!.TypeArguments is { IsDefaultOrEmpty: false } typeArgs)
            {
                GetTypes(typeArgs, out type, out iface);
                GetParams(_params, out key, out factory, out factoryKind, out cache);
            }
            else
            {
                GetTypesAndParams(_params, out type, out iface, out key, out factory, out factoryKind, out cache);
            }

            ITypeSymbol? factoryType;

            var (isAsync, shouldAddAsyncAwait) = (factoryType = factory switch
            {
                IMethodSymbol m => m.ReturnType,
                IPropertySymbol p => p.Type,
                IFieldSymbol p => p.Type,
                _ => null
            })?.ToGlobalNonGenericNamespace() switch
            {
                "global::System.Threading.Tasks.ValueTask" => (true, false),
                "global::System.Threading.Tasks.Task" => (true, true),
                _ => (false, false)
            };

            if (isAsync)
            {
                UpdateSemaphoreUsage();

                if (factoryKind is SymbolKind.Method
                    && !((IMethodSymbol)factory!).Parameters.Any(p => p.Type.ToDisplayString() is CancelTokenFQMetaName))
                {
                    _diagnostics.TryAdd(ServiceContainerGeneratorDiagnostics.CancellationTokenShouldBeProvided(factory, attrSyntax));
                }
            }

            var thisDisposability = UpdateDisposability(type);

            if (thisDisposability > disposability) disposability = thisDisposability;

            var identifier = SanitizeTypeName(type, key);
            var typeName = type.ToGlobalNamespaced();
            var exportTypeFullName = iface?.ToGlobalNamespaced() ?? typeName;

            ref var existingOrNew = ref entries
                .GetOrAddDefault(
                    Extensions.GenKey(lifetime, exportTypeFullName, key),
                    out var exists)!;

            if (exists)
            {
                _diagnostics.TryAdd(
                    ServiceContainerGeneratorDiagnostics.DuplicateService(lifetime, key, attrSyntax, typeName, exportTypeFullName));

                continue;
            }
            else
            {
                if (type.IsPrimitive() && key is null)
                {
                    _diagnostics.TryAdd(
                        ServiceContainerGeneratorDiagnostics.PrimitiveDependencyShouldBeKeyed(lifetime, attrSyntax, typeName, exportTypeFullName));
                }

                existingOrNew = new(type, typeName, exportTypeFullName, key, iface)
                {
                    Lifetime = lifetime,
                    Key = key,
                    CacheMethodName = identifier,
                    CacheField = identifier.Camelize(),
                    Factory = factory,
                    FactoryKind = factoryKind,
                    Disposability = thisDisposability,
                    Resolved = true,
                    Attributes = type.GetAttributes(),
                    IsAsync = isAsync,
                    ShouldAddAsyncAwait = shouldAddAsyncAwait,
                    ContainerType = providerClass,
                    Cached = cache ?? factory is null
                };

                if (existingOrNew.IsKeyed)
                {
                    enumKeysRegistry.Add((isAsync, existingOrNew.KeyEnumTypeName!));
                }
                else
                {
                    if (isAsync)
                    {
                        if (!hasAsyncService) hasAsyncService = true;
                    }
                    else if (!hasService)
                    {
                        hasService = true;
                    }
                }
            }
        }

        entries.ForEach(RegisterService);
    }

    void RegisterService(ref ServiceDescriptor service)
    {
        if (service.NotRegistered || service.IsCancelTokenParam) return;

        if (interfacesRegistry.Add($"{service.KeyEnumTypeName}|{service.ExportTypeName}"))
        {
            interfaces += service.AddInterface;
        }

        if (service.Lifetime is Lifetime.Scoped && !hasScopedService) hasScopedService = true;

        if (service.Lifetime is not Lifetime.Transient)
        {
            if (service.IsAsync)
            {
                UpdateSemaphoreUsage();
            }
            else if (!requiresLocker)
            {
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
                case Disposability.Diposable:

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

            if (!service.IsFactory || service.Cached) props += service.BuildCachedResolver;
        }

        service.GenerateValue = service.Lifetime is Lifetime.Transient
            ? service.IsFactory ? service.UseFactoryResolver : service.UseInstance
            : service is { IsFactory: true, Cached: false }
                   ? service.UseFactoryResolver
                   : service.UseCachedMethodResolver;

        service.CheckParamsDependencies(entries, UpdateSemaphoreUsage, _compilation);

        if (service.IsKeyed)
        {
            ref var builder = ref keyedMethods.GetOrAddDefault(
                $"{service.IsAsync}|{service.ExportTypeName}|{service.KeyEnumTypeName}",
                out var existKeyedMethod);

            if (!existKeyedMethod) builder += service.BuildSwitchBranch(code);
        }
        else
        {
            methods += service.BuildMethod;
        }

        /*switch (service.Lifetime)
        {
            case Lifetime.Singleton:

                if (service.IsAsync)
                {
                    UpdateSemaphoreUsage();
                }
                else if (!requiresLocker)
                {
                    requiresLocker = true;
                }

                AppendDisposability(service.Disposability, service.CacheField, ref singletonDisposeStatments);

                if (!isFactory || service.Cached) props += service.BuildCachedResolver;

                service.GenerateValue = service is { IsFactory: true, Cached: false }
                    ? service.UseFactoryResolver
                    : service.UseCachedMethodResolver;

                service.CheckParamsDependencies(entries, ref requiresSemaphore);

                if (isNamed)
                {
                    ref var builder = ref keyedMethods.GetOrAddDefault(
                        $"{service.IsAsync}|{service.ExportTypeName}",
                        out var existKeyedMethod);

                    builder += service.BuildSwitchBranch(code);
                }
                else
                {
                    methods += service.BuildMethod;
                }

                return;

            case Lifetime.Scoped:

                hasScopedService = true;

                if (service.IsAsync)
                {
                    UpdateSemaphoreUsage();
                }
                else if (!requiresLocker)
                {
                    requiresLocker = true;
                }

                if (!isFactory || service.Cached) props += service.BuildCachedResolver;

                AppendDisposability(service.Disposability, service.CacheField, ref disposeStatments);

                service.GenerateValue = service is { IsFactory: true, Cached: false }
                    ? service.UseFactoryResolver
                    : service.UseCachedMethodResolver;

                service.CheckParamsDependencies(entries, ref requiresSemaphore);

                if (service.Name != null)
                {
                    ref var builder = ref keyedMethods.GetOrAddDefault(
                        $"{service.IsAsync}|{service.ExportTypeName}",
                        out var existKeyedMethod);

                    builder += service.BuildSwitchBranch(code);
                }
                else
                {
                    methods += service.BuildMethod;
                }

                return;

            case Lifetime.Transient:

                service.GenerateValue = service.IsFactory ? service.UseFactoryResolver : service.UseInstance;

                service.CheckParamsDependencies(entries, ref requiresSemaphore);

                if (isNamed)
                {
                    ref var builder = ref keyedMethods.GetOrAddDefault(
                        $"{service.IsAsync}|{service.ExportTypeName}",
                        out var existKeyedMethod);

                    builder += service.BuildSwitchBranch(code);
                }
                else
                {
                    methods += service.BuildMethod;
                }

                return;
        }*/
    }

    private void UpdateSemaphoreUsage()
    {
        if (!requiresSemaphore)
        {
            if (_compilation.GetTypeByMetadataName(CancelTokenFQMetaName) is { } cancelType)
            {
                string cancelTypeName = cancelType.ToGlobalNamespaced();

                entries.TryAdd(new ServiceDescriptor(cancelType, cancelTypeName, cancelTypeName, null)
                {
                    Resolved = true,
                    IsCancelTokenParam = true
                });
            }

            requiresSemaphore = true;
        }
    }

    public void TryBuild(ImmutableArray<InvocationExpressionSyntax> usages, Map<string, byte> uniqueName, Action<string, string> addSource)
    {
        if (interfaces == null) return;

        var fileName = _providerClass.ToMetadataLongName(uniqueName);

        foreach (var (prefix, extraCode) in DependencyInjectionPartsGenerator.InvokeContainerRegistration(code, _compilation, _providerClass, entries))
        {
            addSource(fileName + "." + prefix + ".generated", extraCode);
        }

        if (_providerClass.ContainingNamespace is { IsGlobalNamespace: false } ns)
        {
            code.Append("namespace ")
                .Append(ns.ToDisplayString()!)
                .Append(@";

");
        }

        var initialization = _providerClass.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() switch
        {
            ClassDeclarationSyntax { Modifiers: { } modifiers, Identifier: { } identifier, TypeParameterList: { } argList } iFaceDeclaration =>
                $"{modifiers} {identifier}{argList}",
            _ => "partial class "
        };

        code.AppendLine(Generator.generatedCodeAttribute)
            .Append(initialization)
            .Append(providerClassName)
            .Append(@" : ");

        interfaces(ref useIComma, code);

        if (hasService)
        {
            code.Append(@",
	global::SourceCrafter.DependencyInjection.IServiceProvider");
        }

        if (hasAsyncService)
        {
            code.Append(@",
	global::SourceCrafter.DependencyInjection.IAsyncServiceProvider");
        }

        foreach (var (isAsync, enumFullType) in enumKeysRegistry)
        {
            code.Append(@",
	global::SourceCrafter.DependencyInjection.IKeyed");

            if (isAsync) code.Append("Async");

            code.Append("ServiceProvider<")
                .Append(enumFullType)
                .Append(">");
        }

        if (disposability > 0)
        {
            switch (disposability)
            {
                case Disposability.Diposable:

                    code.Append(@",
	global::System.IDisposable	
{
    ")
                    .AppendLine(Generator.generatedCodeAttribute)
                    .Append(@"    public void Dispose()
	{");

                    break;
                case Disposability.AsyncDisposable:
                    code.Append(@",
	global::System.IAsyncDisposable	
{
    ")
                    .AppendLine(Generator.generatedCodeAttribute)
                    .Append(@"    public async global::System.Threading.Tasks.ValueTask DisposeAsync()
	{");

                    break;
            }

            disposeStatments?.Invoke(code);

            if (hasScopedService)
            {
                if (disposeStatments != null)
                {
                    code.AppendLine();
                }

                code.Append(@"
		if(isScoped) return;
");
            }

            singletonDisposeStatments?.Invoke(code);

            code.Append(@"
	}
");
        }
        else
        {
            code.Append(@"	
{");
        }

        props?.Invoke(code, Generator.generatedCodeAttribute);

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
                .AppendLine(Generator.generatedCodeAttribute)
                .Append(@"    public ").Append(providerTypeName).Append(@" CreateScope() =>
		new ").Append(providerTypeName).Append(@" { isScoped = true };
");
        }

        methods?.Invoke(code, Generator.generatedCodeAttribute);

        foreach (var tuple in keyedMethods.AsSpan())
        {
            var parts = tuple.Key.Split('|');
            (bool isAsync, string serviceName, string enumType) = (bool.Parse(parts[0]), parts[1], parts[2]);

            code
                .Append(@"
	")
                .AppendLine(Generator.generatedCodeAttribute)
                .Append("    ");

            if (isAsync)
            {
                code.Append("global::System.Threading.Tasks.ValueTask<")
                    .Append(serviceName)
                    .Append(@"> global::SourceCrafter.DependencyInjection.IKeyedAsyncServiceProvider<")
                    .Append(enumType)
                    .Append(@", ")
                    .Append(serviceName)
                    .Append(@">.GetServiceAsync(")
                    .Append(enumType)
                    .Append(" key, global::System.Threading.CancellationToken cancellationToken = default");
            }
            else
            {
                code.Append(serviceName)
                    .Append(@" global::SourceCrafter.DependencyInjection.IKeyedServiceProvider<")
                    .Append(enumType)
                    .Append(@", ")
                    .Append(serviceName)
                    .Append(@">.GetService(").Append(enumType).Append(" key");
            }

            code.Append(@")
	{
		switch(key)
		{");


            tuple.Value();

            code.Append(@"
			default: throw InvalidKeyedService(""")
                .Append(serviceName)
                .Append(@""", """)
                .Append(enumType)
                .Append(@""", key);
		}
	}
");
        }

        if (keyedMethods.Count > 0)
        {
            code.Append(@"
	private static global::System.NotImplementedException InvalidKeyedService(string typeFullName, string serviceKeyName, Enum value) =>
			new global::System.NotImplementedException($""There's no registered keyed-implementation for [{serviceKeyName}.{value} = global::SourceCrafter.DependencyInjection.IKeyedServiceProvider<{typeFullName}>.GetService({serviceKeyName} name)]"");
");
        }

        if (hasScopedService)
        {
            code.Append(@"
    ")
                .AppendLine(Generator.generatedCodeAttribute)
                .Append(@"    private static global::System.InvalidOperationException InvalidCallOutOfScope(string typeFullName) => 
		new global::System.InvalidOperationException($""The initialization of the scoped service instance [{typeFullName}] requires a scope creation through the call of [IServiceProviderFactory.CreateScope()] method."");
");
        }

        foreach (var (isAsync, enumFullType) in enumKeysRegistry)
        {
            if (isAsync)
            {
                code
                    .Append(@"
    ")
                    .AppendLine(Generator.generatedCodeAttribute)
                    .Append(@"    public global::System.Threading.Tasks.ValueTask<T> GetServiceAsync<T>(")
                    .Append(enumFullType)
                    .Append(@" key, global::System.Threading.CancellationToken cancellationToken = default) => 
        ((global::SourceCrafter.DependencyInjection.IKeyedAsyncServiceProvider<")
                    .Append(enumFullType)
                    .Append(@", T>)
            (global::SourceCrafter.DependencyInjection.IKeyedAsyncServiceProvider<")
                    .Append(enumFullType)
                    .Append(@">)this)
                .GetServiceAsync(key, cancellationToken == default ? __globalCancellationTokenSrc.Token : cancellationToken);
");
            }
            else
            {
                code
                    .Append(@"
    ")
                    .AppendLine(Generator.generatedCodeAttribute)
                    .Append(@"    public T GetService<T>(")
                    .Append(enumFullType)
                    .Append(@" key) => 
        ((global::SourceCrafter.DependencyInjection.IKeyedServiceProvider<")
                    .Append(enumFullType)
                    .Append(@", T>)
            (global::SourceCrafter.DependencyInjection.IKeyedServiceProvider<")
                    .Append(enumFullType)
                    .Append(@">)this)
                .GetService(key);
");

            }
        }

        if (hasService)
        {
            code
                .Append(@"
    ")
                .AppendLine(Generator.generatedCodeAttribute)
                .Append(@"    public T GetService<T>() => 
        ((global::SourceCrafter.DependencyInjection.IServiceProvider<T>)
            (global::SourceCrafter.DependencyInjection.IServiceProvider)this).GetService();
");
        }

        if (hasAsyncService)
        {
            code
                .Append(@"
    ")
                .AppendLine(Generator.generatedCodeAttribute)
                .Append(@"    public global::System.Threading.Tasks.ValueTask<T> GetServiceAsync(global::System.Threading.CancellationToken cancellationToken = default) => 
        ((global::SourceCrafter.DependencyInjection.IAsyncServiceProvider<T>)
            (global::SourceCrafter.DependencyInjection.IAsyncServiceProvider)this)
                .GetServiceAsync(cancellationToken == default ? __globalCancellationTokenSrc.Token : cancellationToken);
");
        }

        var codeStr = code.Append("}").ToString();

        addSource(fileName + ".generated", codeStr);

        CheckUsages(usages);
    }

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

            var model = _compilation.GetSemanticModel(invExpr.SyntaxTree);

            var refType = model.GetSymbolInfo(((MemberAccessExpressionSyntax)invExpr.Expression).Expression).Symbol switch
            {
                ILocalSymbol { Type: { } rType } => rType,
                IFieldSymbol { Type: { } rType } => rType,
                IPropertySymbol { Type: { } rType } => rType,
                _ => null
            };

            if (refType is null || !SymbolEqualityComparer.Default.Equals(refType, _providerClass)) continue;

            var typeSymbol = model.GetTypeInfo(type).Type;

            if (typeSymbol is null) continue;

            var typeFullName = typeSymbol.ToGlobalNamespaced();

            IFieldSymbol? key = null;

            if (invExpr.ArgumentList.Arguments is [{ Expression: { } keyArg } arg])
            {
                if (model.GetSymbolInfo(keyArg).Symbol is IFieldSymbol
                {
                    IsConst: true,
                    Type: INamedTypeSymbol { TypeKind: TypeKind.Enum }
                } f)
                {
                        key = f;
                }
                else
                {
                    _diagnostics.TryAdd(
                        ServiceContainerGeneratorDiagnostics.InvalidKeyType(arg.Expression));
                }
            }

            entries.ForEach((ref ServiceDescriptor item) =>
            {
                if (item.ExportTypeName == typeFullName
                    && SymbolEqualityComparer.Default.Equals(item.Key, key) && item.Resolved)
                {
                    found = true;
                }
            });

            if (!found)
            {
                _diagnostics.TryAdd(
                    ServiceContainerGeneratorDiagnostics.UnresolvedDependency(invExpr, providerClassName, typeFullName, key));
            }
        }
    }

    void GetTypes(ImmutableArray<ITypeSymbol> symbols, out ITypeSymbol type, out ITypeSymbol? iface) =>
        (type, iface) = symbols.IsDefaultOrEmpty
            ? (default!, default)
            : symbols switch
            {
            [{ } t1, { } t2, ..] => (t2, t1),

            [{ } t1] => (t1, default),

                _ => (default!, default)
            };

    string SanitizeTypeName(ITypeSymbol type, IFieldSymbol? prefix = null)
    {
        string varName = (prefix is { ContainingType.Name: { } enumType, Name: { } name } ? $"{enumType}_{name}_" : null) + Sanitize(type).Capitalize();

        string varName1 = varName;

        var i = 0;

        while (!propsRegistry.Add(varName1))
        {
            varName1 = varName + (++i);
        }

        return varName1;

        string Sanitize(ITypeSymbol type)
        {
            switch (type)
            {
                case INamedTypeSymbol { IsTupleType: true, TupleElements: { Length: > 0 } els }:

                    return "TupleOf" + string.Join("", els.Select(f => SanitizeTypeName(f.Type)));

                case INamedTypeSymbol { IsGenericType: true, TypeArguments: { } args }:

                    return type.Name + "Of" + string.Join("", args.Select(Sanitize));

                default:

                    string typeName = type.ToTypeNameFormat();

                    if (type is IArrayTypeSymbol { ElementType: { } elType })
                        typeName = Sanitize(elType) + "Array";

                    return char.ToUpperInvariant(typeName[0]) + typeName[1..].TrimEnd('?', '_');
            };
        }
    }

    Disposability UpdateDisposability(ITypeSymbol type)
    {
        Disposability disposability = Disposability.None;

        foreach (var iFace in type.AllInterfaces)
        {
            switch (iFace.ToGlobalNonGenericNamespace())
            {
                case "global::System.IDisposable" when disposability is Disposability.None:
                    disposability = Disposability.Diposable;
                    break;
                case "global::System.IAsyncDisposable" when disposability < Disposability.AsyncDisposable:
                    return Disposability.AsyncDisposable;
            }
        }

        return disposability;
    }

    void GetParams(SeparatedSyntaxList<AttributeArgumentSyntax> _params, out IFieldSymbol? key, out ISymbol? factory, out SymbolKind factoryKind, out bool? cache)
    {
        key = null;
        factory = null;
        factoryKind = default;
        cache = true;
        int i = 0;

        foreach (var arg in _params)
        {
            switch (arg.NameColon?.Name.Identifier.ValueText ?? genericParamNames[i])
            {
                case KeyParamName:

                    if (_model.GetSymbolInfo(arg.Expression).Symbol is IFieldSymbol
                        {
                            IsConst: true,
                            Type: INamedTypeSymbol { TypeKind: TypeKind.Enum }
                        } field)
                    {
                        key = field;
                    }
                    else
                    {

                        _diagnostics.TryAdd(
                            ServiceContainerGeneratorDiagnostics.InvalidKeyType(arg.Expression));
                    }

                    break;

                case FactoryOrInstanceParamName:

                    GetFactory(arg, ref factory, ref factoryKind);

                    break;

                case CacheParamName:

                    cache = bool.Parse(arg.Expression.ToString());

                    break;
            }

            i++;
        }
    }

    void GetTypesAndParams(
        SeparatedSyntaxList<AttributeArgumentSyntax> _params,
        out ITypeSymbol implType,
        out ITypeSymbol? ifaceType,
        out IFieldSymbol? key,
        out ISymbol? factory,
        out SymbolKind factoryKind,
        out bool? cache)
    {
        key = null;
        implType = ifaceType = null!;
        factory = null;
        factoryKind = default;
        cache = true;

        int i = 0;

        foreach (var arg in _params)
        {
            switch (arg.NameColon?.Name.Identifier.ValueText ?? paramNames[i])
            {
                case ImplParamName when arg.Expression is TypeOfExpressionSyntax { Type: { } type }:

                    implType = (ITypeSymbol)_model!.GetSymbolInfo(type).Symbol!;

                    break;

                case IfaceParamName when arg.Expression is TypeOfExpressionSyntax { Type: { } type }:

                    ifaceType = (ITypeSymbol)_model!.GetSymbolInfo(type).Symbol!;

                    break;

                case KeyParamName:

                    if (_model.GetSymbolInfo(arg.Expression).Symbol is IFieldSymbol
                        {
                            IsConst: true,
                            Type: INamedTypeSymbol { TypeKind: TypeKind.Enum }
                        } field)
                    {
                        key = field;
                    }
                    else
                    {
                        _diagnostics.TryAdd(
                            ServiceContainerGeneratorDiagnostics.InvalidKeyType(arg.Expression));
                    }

                    break;

                case FactoryOrInstanceParamName:

                    GetFactory(arg, ref factory, ref factoryKind);

                    break;

                case CacheParamName:

                    cache = bool.Parse(arg.Expression.ToString());

                    break;
            }

            i++;
        }

        if (ifaceType != null)
        {
            (ifaceType, implType) = (implType, ifaceType);
        }
    }

    void GetFactory(AttributeArgumentSyntax arg, ref ISymbol? factory, ref SymbolKind factoryKind)
    {
        if (arg.Expression is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" }, ArgumentList.Arguments: [{ } methodRef] })
        {
            var info = _model.GetSymbolInfo(methodRef.Expression);

            (factory, factoryKind) = info switch
            {
                { Symbol: (IFieldSymbol or IPropertySymbol) and { IsStatic: true, Kind: { } kind } fieldOrProp }
                    => (fieldOrProp, kind),

                { CandidateReason: CandidateReason.MemberGroup, CandidateSymbols: [IMethodSymbol { ReturnsVoid: false, IsStatic: true } method] }
                    => (method, SymbolKind.Method),

                _ => (null, default)
            };
        }
    }

}
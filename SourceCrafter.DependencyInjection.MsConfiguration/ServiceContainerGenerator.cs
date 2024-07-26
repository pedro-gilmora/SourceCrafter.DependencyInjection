using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;

using System.Linq;

using System.Text;

using System;
using SourceCrafter.DependencyInjection.Interop;

namespace SourceCrafter.DependencyInjection.MsConfiguration;


class ServiceContainerGenerator
{
    readonly string providerTypeName, providerClassName;

    readonly ImmutableArray<AttributeData> attributes;

    readonly SemanticModel _model;
    readonly INamedTypeSymbol _providerClass;

    readonly StringBuilder code = new();

    readonly HashSet<string>
        propsRegistry = [],
         interfacesRegistry = [];

    readonly Map<string, ServiceDescriptor>
        entries = new(StringComparer.Ordinal);


    readonly Map<string, Action>
        keyedMethods = new(StringComparer.Ordinal);

    Action?
        interfaces = null,
        methods = null,
        singletonDisposeStatments = null,
        disposeStatments = null,
        props = null;

    bool useIComma = false,
        hasScoped = false,
        requiresLocker = false;

    readonly bool
        hasNamedService = true,
        hasService = true;

    readonly byte disposability = 0;

    readonly static Dictionary<int, string>
        genericParamNames = new()
        {
            {0, "name"},
            {1, "factory"}
        },
        paramNames = new()
        {
            {0, "impl"},
            {1, "iface"},
            {2, "name"},
            {3, "factory"}
        };

    public ServiceContainerGenerator(INamedTypeSymbol providerClass, Compilation compilation, SemanticModel model)
    {
        _providerClass = providerClass;
        providerTypeName = _providerClass.ToGlobalNamespaced();
        providerClassName = _providerClass.ToNameOnly();
        attributes = _providerClass.GetAttributes();
        _model = model;

        List<ServiceDescriptor> services = [];

        foreach (var attr in attributes)
        {
            ServiceType serviceType;

            switch (attr.AttributeClass!.ToGlobalNonGenericNamespace())
            {
                case "global::SourceCrafter.DependencyInjection.Attributes.SingletonAttribute":
                    serviceType = ServiceType.Singleton;
                    break;
                case "global::SourceCrafter.DependencyInjection.Attributes.ScopedAttribute":
                    serviceType = ServiceType.Scoped;
                    break;
                case "global::SourceCrafter.DependencyInjection.Attributes.TransientAttribute":
                    serviceType = ServiceType.Transient;
                    break;
                default: continue;
            };

            ITypeSymbol type;
            ITypeSymbol? iface;
            IMethodSymbol? factory;
            string? name;

            var attrSyntax = (AttributeSyntax)attr.ApplicationSyntaxReference!.GetSyntax();

            var _params = attrSyntax
                        .ArgumentList?
                        .Arguments ?? default;

            if (attr.AttributeClass!.TypeArguments is { IsDefaultOrEmpty: false } typeArgs)
            {
                GetTypes(typeArgs, out type, out iface);
                GetParams(_params, out name, out factory);
            }
            else
            {
                GetTypesAndParams(_params, out type, out iface, out name, out factory);
            }

            if (name != null)
            {
                hasNamedService |= true;

                serviceType = serviceType switch
                {
                    ServiceType.Singleton => ServiceType.NamedSingleton,
                    ServiceType.Scoped => ServiceType.NamedScoped,
                    _ => ServiceType.NamedTransient,
                };
            }
            else
            {
                hasService |= true;
            }

            var thisDisposability = UpdateDisposability(type);

            if (thisDisposability > disposability) disposability = thisDisposability;

            var varName = SanitizeTypeName(type, name);
            var typeName = type.ToGlobalNamespaced();

            ref var existingOrNew = ref entries.GetOrAddDefault(
                GenKey(serviceType, typeName, name),
                out var exists,
                () => 
                    new() {
                        Type = type,
                        Interface = iface,
                        ServiceType = serviceType,
                        FullTypeName = typeName,
                        Name = name,
                        VarName = varName,
                        Factory = factory,
                        Disposability = thisDisposability
                    })!;

            if (exists)
            {
                // TODO: Notify duplicate registration
                continue;
            }

            services.Add(existingOrNew);
        }

        if (services.Any())
        {

            foreach (var item in services)
            {
                RegisterService(item);
            }
            DependencyInjectionPartsGenerator.OnRegisteredContainer?.Invoke(compilation, _providerClass, services);
        }
    }

    public void TryBuild(Map<string, byte> uniqueName, Action<string, string> addSource)
    {
        if (interfaces == null) return;

        if(_providerClass.ContainingNamespace is { IsGlobalNamespace: false} ns)
        {
            code.Append("namespace ")
                .Append(ns.ToDisplayString()!)
                .Append(@";

");
        }

        code.AppendLine(Generator.generatedCodeAttribute)
            .Append("public partial class ")
            .Append(providerClassName)
            .Append(@" : ");

        interfaces();

        if (hasNamedService)
        {
            code.Append(@",
	global::SourceCrafter.DependencyInjection.IKeyedServiceProvider");
        }
        if (hasService)
        {
            code.Append(@",
	global::SourceCrafter.DependencyInjection.IServiceProvider");
        }

        if (disposability > 0)
        {
            switch (disposability)
            {
                case 1:
                    code.Append(@",
	global::System.IDisposable	
{
    ")
                    .AppendLine(Generator.generatedCodeAttribute)
                    .Append(@"    public void Dispose()
	{");
                    disposeStatments?.Invoke();

                    if (hasScoped)
                    {
                        if (disposeStatments != null)
                        {
                            code.AppendLine();
                        }

                        code.Append(@"
		if(isScoped) return;
");
                    }
                    singletonDisposeStatments?.Invoke();
                    code.Append(@"
	}
");
                    break;
                case 2:
                    code.Append(@",
	global::System.IAsyncDisposable	
{
    ")
                    .AppendLine(Generator.generatedCodeAttribute)
                    .Append(@"    public async global::System.Threading.Tasks.ValueTask DisposeAsync()
	{");
                    disposeStatments?.Invoke();

                    if (hasScoped)
                    {
                        if (disposeStatments != null)
                        {
                            code.AppendLine();
                        }

                        code.Append(@"
		if(isScoped) return;
");
                    }
                    singletonDisposeStatments?.Invoke();
                    code.Append(@"
	}
");
                    break;
            }
        }
        else
        {
            code.Append(@"	
{");
        }

        props?.Invoke();

        if (requiresLocker)
        {
            code.Append(@"
    private static readonly object _lock = new object();
");
        }

        if (hasScoped)
        {
            code.Append(@"
    private bool isScoped = false;

    ")
                .AppendLine(Generator.generatedCodeAttribute)
                .Append(@"    public ").Append(providerTypeName).Append(@" CreateScope() =>
		new ").Append(providerTypeName).Append(@" { isScoped = true };
");
        }

        methods?.Invoke();

        foreach (var tuple in keyedMethods.AsSpan())
        {
            code
                .Append(@"
	")
                .AppendLine(Generator.generatedCodeAttribute)
                .Append("    ").Append(tuple.Key)
                .Append(@" 
        global::SourceCrafter.DependencyInjection.IKeyedServiceProvider<")
                .Append(tuple.Key)
                .Append(@">
            .GetService(string name)
	{
		switch(name)
		{");


            tuple.Value();

            code.Append(@"
			default: throw InvalidNamedService(""")
                .Append(tuple.Key)
                .Append(@""", name);
		}
	}
");
        }

        if (hasScoped)
        {
            code.Append(@"
    ")                  
                .AppendLine(Generator.generatedCodeAttribute)
                .Append(@"    private static global::System.InvalidOperationException InvalidCallOutOfScope(string typeFullName) => 
		new global::System.InvalidOperationException($""The initialization of the scoped service instance [{typeFullName}] requires a scope creation through the call of [IServiceProviderFactory.CreateScope()] method."");
");
        }

        if (hasNamedService)
        {
            code
                .Append(@"
    ")
                .AppendLine(Generator.generatedCodeAttribute)
                .Append(@"    public T GetService<T>(string key) => (this as global::SourceCrafter.DependencyInjection.IKeyedServiceProvider<T> ?? throw InvalidNamedService(typeof(T).AssemblyQualifiedName!, key)).GetService(key);

    ")                  
                .AppendLine(Generator.generatedCodeAttribute)
                .Append(@"    private static global::System.NotImplementedException InvalidNamedService(string typeFullName, string serviceKeyName) => 
		new global::System.NotImplementedException($""There's no registered keyed-implementation for [{serviceKeyName} = global::SourceCrafter.DependencyInjection.IKeyedServiceProvider<{typeFullName}>.GetService(string name)]"");
");
        }

        if (hasService)
        {
            code
                .Append(@"
    ")
                .AppendLine(Generator.generatedCodeAttribute)
                .Append(@"    public T GetService<T>() => (this as global::SourceCrafter.DependencyInjection.IServiceProvider<T> ?? throw InvalidService(typeof(T).AssemblyQualifiedName!)).GetService();

    ")                  
                .AppendLine(Generator.generatedCodeAttribute)
                .Append(@"    private static global::System.NotImplementedException InvalidService(string typeFullName) =>
		new global::System.NotImplementedException($""There's no registered implementation for [global::SourceCrafter.DependencyInjection.IKeyedServiceProvider<{typeFullName}>.GetService(string name)]"");
");
        }

        var codeStr = code.Append("}").ToString();

        var fileName = _providerClass.ToMetadataLongName(uniqueName);

        addSource(fileName, codeStr);
    }

    T Exchange<T>(ref T oldVal, T newVal) => ((oldVal, newVal) = (newVal, oldVal)).newVal;

    void GetTypes(ImmutableArray<ITypeSymbol> symbols, out ITypeSymbol arg1, out ITypeSymbol? arg2) =>
        _ = symbols.IsDefaultOrEmpty
            ? (arg1, arg2) = (default!, default)
            : symbols switch
            {
            [{ } t1, { } t2, ..] => (arg1, arg2) = (t1, t2),

            [{ } t1] => (arg1, arg2) = (t1, default),

                _ => (arg1, arg2) = (default!, default)
            };

    void RegisterService(ServiceDescriptor service)
    {
        if (interfacesRegistry.Add($"{service.Name != null}|{service.FullTypeName}"))
        {
            interfaces += () =>
            {
                //DependencyInjectionPartsGenerator.OnInterfaceAppending?.Invoke(code, service);

                (Exchange(ref useIComma, true) ? code.Append(", ") : code)
                    .Append(@"
	global::SourceCrafter.DependencyInjection.I");

                if (service.Name != null) code.Append("Keyed");

                code.Append("ServiceProvider<")
                    .Append(service.FullTypeName)
                    .Append(">");
            };
        }

        Action? paramRef = null;
        bool paramsComma = false;

        var parameters = service.Factory is not null
            ? service.Factory.Parameters
            : ((IMethodSymbol?)service.Type
                .GetMembers()
                .FirstOrDefault(a => a is IMethodSymbol { MethodKind: MethodKind.Constructor, DeclaredAccessibility: Microsoft.CodeAnalysis.Accessibility.Internal or Microsoft.CodeAnalysis.Accessibility.Public }))
                ?.Parameters ?? default!;

        Action typeBuilder = service.Factory is not null
            ? BuildTypeBuilder(service.Factory, _providerClass)
            : () =>
                {
                    code.Append("new ")
                        .Append(service.FullTypeName)
                        .Append(@"(");

                    paramRef?.Invoke();

                    code.Append(@")");
                };

        switch (service.ServiceType)
        {
            case ServiceType.Singleton or ServiceType.NamedSingleton:

                requiresLocker |= true;

                AppendDisposability(service.Disposability, service.VarName, ref singletonDisposeStatments);



                service.ValueRef = () => code.Append(service.VarName);

                ParseParams();

                props += () =>
                {
                    code.Append(@"
    private static ")
                        .Append(service.FullTypeName)
                        .Append("? _")
                        .Append(service.VarName)
                        .Append(@";

    ")
                        .AppendLine(Generator.generatedCodeAttribute)
                        .Append("    private static ")
                        .Append(service.FullTypeName)
                        .Append(" ")
                        .Append(service.VarName)
                        .Append(@"
	{
		[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		get
		{
			if (_")
                        .Append(service.VarName)
                        .Append(@" is null)
				lock (_lock)
					return _")
                        .Append(service.VarName)
                        .Append(@" ??= ");

                    typeBuilder();

                    code.Append(@";
			return _")
                        .Append(service.VarName)
                        .Append(@";
		}
	}
");
                };

                if (service.Name != null)
                {
                    ref var builder = ref keyedMethods.GetOrAddDefault(
                        service.FullTypeName,
                        out var existKeyedMethod);

                    builder += new NamedBranchBuilder(code, service.Name, service.ValueRef).Build;
                }
                else
                {
                    methods += () => code
                        .Append(@"
    ")
                        .AppendLine(Generator.generatedCodeAttribute)
                        .Append(@"    ")
                        .Append(service.FullTypeName)
                        .Append(@" 
		global::SourceCrafter.DependencyInjection.IServiceProvider<")
                        .Append(service.FullTypeName)
                        .Append(@">
			.GetService() => ").Append(service.VarName).Append(@";
");
                }

                return;

            case ServiceType.Scoped or ServiceType.NamedScoped:

                hasScoped = true;

                requiresLocker |= true;

                service.ValueRef = () => code.Append(service.VarName);

                AppendDisposability(service.Disposability, service.VarName, ref disposeStatments);

                ParseParams();

                props += () =>
                {
                    code.Append(@"
    private ")
                        .Append(service.FullTypeName)
                        .Append("? _")
                        .Append(service.VarName)
                        .Append(@";

    ")
                        .AppendLine(Generator.generatedCodeAttribute)
                        .Append("    private ")
                        .Append(service.FullTypeName)
                        .Append(" ")
                        .Append(service.VarName)
                        .Append(@"
	{
		[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		get
		{
			if (_")
                        .Append(service.VarName)
                        .Append(@" is null)
				lock (_lock)
					return _")
                        .Append(service.VarName)
                        .Append(@" ??= ");

                    typeBuilder();

                    code.Append(@";
			return _")
                        .Append(service.VarName)
                        .Append(@";
		}
	}
");
                };

                if (service.Name != null)
                {
                    ref var builder = ref keyedMethods.GetOrAddDefault(
                        service.FullTypeName,
                        out var existKeyedMethod);

                    builder += new NamedBranchBuilder(code, service.Name, service.ValueRef).Build;
                }
                else
                {
                    methods += () => code
                        .Append(@"
    ")
                        .AppendLine(Generator.generatedCodeAttribute)
                        .Append("    ")
                        .Append(service.FullTypeName)
                        .Append(@" 
		global::SourceCrafter.DependencyInjection.IServiceProvider<")
                        .Append(service.FullTypeName)
                        .Append(@">
			.GetService() => isScoped 
				? ")
                        .Append(service.VarName)
                        .Append(@" 
				: throw InvalidCallOutOfScope(""")
                        .Append(service.FullTypeName).Append(@""");
");
                }

                return;

            case ServiceType.Transient or ServiceType.NamedTransient:

                service.ValueRef = typeBuilder;

                ParseParams();

                if (service.Name != null)
                {
                    ref var builder = ref keyedMethods.GetOrAddDefault(
                        service.FullTypeName,
                        out var existKeyedMethod);

                    builder += new NamedBranchBuilder(code, service.Name, service.ValueRef).Build;
                }
                else
                {
                    methods += () =>
                    {
                        code
                            .Append(@"
    ")
                            .AppendLine(Generator.generatedCodeAttribute)
                            .Append(@"    ")
                            .Append(service.FullTypeName)
                            .Append(@" 
		global::SourceCrafter.DependencyInjection.IServiceProvider<")
                            .Append(service.FullTypeName)
                            .Append(@">
			.GetService() => 
				new ").Append(service.FullTypeName).Append("(");

                        paramRef?.Invoke();

                        code.Append(@");
");
                    };
                }

                return;
        }

        void ParseParams()
        {
            if (!parameters.IsDefaultOrEmpty)
            {
                string? paramName = null;
                foreach (var _param in parameters)
                {
                    if (_param
                        .GetAttributes()
                        .FirstOrDefault(a => a.AttributeClass?.ToGlobalNamespaced().StartsWith("global::SourceCrafter.DependencyInjection.Attributes.Named") is true) is { } attr)
                    {
                        var paramAttrArgs = ((AttributeSyntax)attr.ApplicationSyntaxReference!.GetSyntax())
                                .ArgumentList
                                ?.Arguments;

                        paramName = attr.ConstructorArguments is [{ Value: { } v }]
                            ? v.ToString()
                            : paramAttrArgs is [{ Expression: { } argExpr }] && argExpr is LiteralExpressionSyntax { Token.Value: { } val } expr
                                ? val.ToString()
                                : paramAttrArgs?.FirstOrDefault()?.ToString();
                    }
                    var paramTypeName = _param.Type.ToGlobalNamespaced();
                    var (checkType, limit) = paramName is null ? (0, 2) : (3, 5);
                CHK:
                    if (!entries.TryGetValue(GenKey((ServiceType)checkType, paramTypeName, paramName), out var asParam))
                    {
                        if (++checkType <= limit)
                        {
                            goto CHK;
                        }
                        else
                        {
                            // Mark attribute with error, not found
                            continue;
                        }
                    }

                    paramRef += () =>
                    {
                        if (Exchange(ref paramsComma, true)) code.Append(", ");

                        asParam.ValueRef?.Invoke();
                    };
                }
            }
        }

        static Action BuildTypeBuilder(IMethodSymbol serviceFactory, ITypeSymbol cls)
        {
            var methodCall = SymbolEqualityComparer.Default.Equals(serviceFactory.ContainingType, cls)
                ? serviceFactory.Name
                : serviceFactory.ContainingType.ToGlobalNamespaced() + "." + serviceFactory.Name;

            return null!;
        }
    }

    void AppendDisposability(byte thisDisposability, string varName, ref Action? statement)
    {
        switch (thisDisposability)
        {
            case 2:
                statement += () => code.Append(@"
		await ").Append(varName).Append(".DisposeAsync();");
                break;
            case 1:
                disposeStatments += () => code.Append(@"
		").Append(varName).Append(".Dispose();");
                break;
        }
    }

    string SanitizeTypeName(ITypeSymbol type, string? prefix = null)
    {
        string varName = prefix + Sanitize(type);
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

    byte UpdateDisposability(ITypeSymbol type)
    {
        byte disposability = 0;

        foreach (var iFace in type.AllInterfaces)
        {
            switch (iFace.ToGlobalNonGenericNamespace())
            {
                case "global::System.IDisposable" when disposability is 0:
                    disposability = 1;
                    break;
                case "global::System.IAsyncDisposable" when disposability < 2:
                    disposability = 2;
                    break;
            }

            if (disposability == 2)
            {
                break;
            }
        }

        return disposability;
    }
    void GetParams(SeparatedSyntaxList<AttributeArgumentSyntax> _params, out string? name, out IMethodSymbol? factory)
    {
        name = null;
        factory = null;

        int i = 0;

        foreach (var arg in _params)
        {
            switch (arg.NameColon?.Name.Identifier.ValueText ?? genericParamNames[i])
            {
                case "name":
                    name = arg.Expression is LiteralExpressionSyntax { Token.Value: { } val } expr
                        ? val.ToString()
                        : arg.Expression.ToString();
                    break;
                case "factory":
                    if (arg.Expression is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" }, ArgumentList.Arguments: [{ } methodRef] })
                    {
                        var r = _model.GetSymbolInfo(((MemberAccessExpressionSyntax)methodRef.Expression).Expression).Symbol;
                        factory = r as IMethodSymbol;
                    }
                    break;
            }
        }
    }

    void GetTypesAndParams(SeparatedSyntaxList<AttributeArgumentSyntax> _params, out ITypeSymbol implType, out ITypeSymbol? ifaceType, out string? name, out IMethodSymbol? factory)
    {
        name = null;
        implType = ifaceType = null!;
        factory = null;

        int i = 0;

        foreach (var arg in _params)
        {
            switch (arg.NameColon?.Name.Identifier.ValueText ?? paramNames[i])
            {
                case "impl" when arg.Expression is TypeOfExpressionSyntax { Type: { } type }:
                    implType = (ITypeSymbol)_model!.GetSymbolInfo(type).Symbol!;
                    break;
                case "iface" when arg.Expression is TypeOfExpressionSyntax { Type: { } type }:
                    ifaceType = (ITypeSymbol)_model!.GetSymbolInfo(type).Symbol!;
                    break;
                case "name":
                    name = arg.Expression is LiteralExpressionSyntax { Token.Value: { } val } expr
                        ? val.ToString()
                        : arg.Expression.ToString();
                    break;
                case "factory":
                    factory = arg.Expression is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" }, ArgumentList.Arguments: [{ } methodRef] }
                        ? _model.GetSymbolInfo(methodRef).Symbol as IMethodSymbol
                        : null;
                    break;
            }
        }
    }

    static string GenKey(ServiceType serviceType, string typeFullName, string? name)
    {
        return $"{serviceType}|{typeFullName}|{name}";
    }
}

readonly struct NamedBranchBuilder(StringBuilder code, string name, Action existingOrNew)
{
    public readonly void Build()
    {
        code.Append(@"
			case """)
            .Append(name).Append(@""": return ");

        existingOrNew();

        code.Append(";");

    }
}

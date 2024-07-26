using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;

using System.Linq;

using System.Text;

using System;
using SourceCrafter.DependencyInjection.Interop;
using System.Runtime.ConstrainedExecution;

namespace SourceCrafter.DependencyInjection;


class ServiceContainerGenerator
{
    readonly string providerTypeName, providerClassName;

    readonly ImmutableArray<AttributeData> attributes;

    readonly SemanticModel _model;
    readonly INamedTypeSymbol _providerClass;
    private readonly Compilation _compilation;
    readonly StringBuilder code = new();

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

    Action?
        singletonDisposeStatments = null,
        disposeStatments = null;

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
        _compilation = compilation;
        _model = model;

        providerTypeName = _providerClass.ToGlobalNamespaced();
        providerClassName = _providerClass.ToNameOnly();
        attributes = _providerClass.GetAttributes();

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
            var exportTypeFullName = iface?.ToGlobalNamespaced() ?? typeName;

            ref var existingOrNew = ref entries
                .GetOrAddDefault(
                    Extensions.GenKey(serviceType, exportTypeFullName, name),
                    out var exists)!;

            if (exists)
            {
                // TODO: Notify duplicate registration
                continue;
            }
            else
            {
                existingOrNew = new(type, typeName, exportTypeFullName, iface)
                {
                    ServiceType = serviceType,
                    Name = name,
                    Identifier = varName,
                    Factory = factory,
                    Disposability = thisDisposability,
                    Resolved = true,
                    Attributes = type.GetAttributes(),
                    ContainerType = providerClass
                };
            }
        }

        entries.ForEach(RegisterService);
    }

    void RegisterService(ref ServiceDescriptor service)
    {
        if (service.NotRegistered) return;

        bool isNamed = service.Name != null;

        if (interfacesRegistry.Add($"{isNamed}|{service.ExportTypeName}"))
        {
            interfaces += service.AddInterface;
        }

        switch (service.ServiceType)
        {
            case ServiceType.Singleton or ServiceType.NamedSingleton:

                if (!requiresLocker) requiresLocker = true;

                AppendDisposability(service.Disposability, service.Identifier, ref singletonDisposeStatments);

                service.GenerateValue = service.BuildWithVarName;

                service.ParseParams(entries);

                props += service.BuildProperty;

                if (isNamed)
                {
                    ref var builder = ref keyedMethods.GetOrAddDefault(
                        service.ExportTypeName,
                        out var existKeyedMethod);

                    builder += service.BuildSwitchBranch(code);
                }
                else
                {
                    methods += service.BuildMethod;
                }

                return;

            case ServiceType.Scoped or ServiceType.NamedScoped:

                hasScoped = true;

                if (!requiresLocker) requiresLocker = true;

                service.GenerateValue = service.BuildWithVarName;

                AppendDisposability(service.Disposability, service.Identifier, ref disposeStatments);

                service.ParseParams(entries);

                props += service.BuildProperty;

                if (service.Name != null)
                {
                    ref var builder = ref keyedMethods.GetOrAddDefault(
                        service.ExportTypeName,
                        out var existKeyedMethod);

                    builder += service.BuildSwitchBranch(code);
                }
                else
                {
                    methods += service.BuildMethod;
                }

                return;

            case ServiceType.Transient or ServiceType.NamedTransient:

                service.GenerateValue = service.BuildInstance;

                service.ParseParams(entries);

                if (isNamed)
                {
                    ref var builder = ref keyedMethods.GetOrAddDefault(
                        service.ExportTypeName,
                        out var existKeyedMethod);

                    builder += service.BuildSwitchBranch(code);
                }
                else
                {
                    methods += service.BuildMethod;
                }

                return;
        }
    }

    public void TryBuild(Map<string, byte> uniqueName, Action<string, string> addSource)
    {
        if (interfaces == null) return;

        var fileName = _providerClass.ToMetadataLongName(uniqueName);

        foreach (var (prefix, extraCode) in DependencyInjectionPartsGenerator.InvokeContainerRegistration(code, _compilation, _providerClass, entries))
        {
            addSource(fileName + "." + prefix, extraCode);
        }

        if (_providerClass.ContainingNamespace is { IsGlobalNamespace: false } ns)
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

        interfaces(ref useIComma, code);

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

        props?.Invoke(code, Generator.generatedCodeAttribute);

        if (requiresLocker)
        {
            code.Append(@"
    static readonly object _lock = new object();
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

        methods?.Invoke(code, Generator.generatedCodeAttribute);

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

        addSource(fileName, codeStr);
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

        if (ifaceType != null)
        {
            (ifaceType, implType) = (implType, ifaceType);
        }
    }
}
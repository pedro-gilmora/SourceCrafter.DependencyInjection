global using DependecyKey = (SourceCrafter.DependencyInjection.Interop.Lifetime LifeTime, string ExportFullTypeName, string? Key);

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

using DependencyMap = SourceCrafter.DependencyInjection.Map<(SourceCrafter.DependencyInjection.Interop.Lifetime, string, string?), SourceCrafter.DependencyInjection.Interop.ServiceDescriptor>;

namespace SourceCrafter.DependencyInjection;


class ServiceContainerGenerator
{
    readonly string providerTypeName, providerClassName;

    readonly ImmutableArray<AttributeData> attributes;

    readonly SemanticModel _model;
    private readonly Set<Diagnostic> _diagnostics;
    readonly INamedTypeSymbol _providerClass;
    private readonly Compilation _compilation;

    readonly HashSet<(bool, string)> enumKeysRegistry = [];

    readonly HashSet<string>
        methodsRegistry = new(StringComparer.Ordinal);

    readonly HashSet<(string?, string)> interfacesRegistry = [];

    readonly Map<(int, Lifetime, string?), string> dependencyRegistry = new(EqualityComparer<(int, Lifetime, string?)>.Default);

    readonly DependencyMap discoveredServices = new(new DependencyComparer());

    readonly Map<ServiceDescriptor, Action<StringBuilder>>
        keyedMethods = new(new KeyedServiceComparer());

    CommaSeparateBuilder? interfaces = null;

    MemberBuilder?
        methods = null;

    Action<StringBuilder>?
        singletonDisposeStatments = null,
        disposeStatments = null;

    bool //useIComma = false,
        hasScopedService = false,
        requiresLocker = false,
        requiresSemaphore = false/*,
        hasAsyncService = false*/;

    Disposability disposability = 0;
    //unify attribute reading
    public ServiceContainerGenerator(
        Compilation compilation,
        SemanticModel model,
        INamedTypeSymbol providerClass,
        Set<Diagnostic> diagnostics)
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
            if (attr.AttributeClass is null) continue;

            ParseDependencyAttribute(
                attr.AttributeClass,
                (AttributeSyntax)attr.ApplicationSyntaxReference!.GetSyntax(),
                attr.AttributeConstructor?.Parameters,
                out disposability,
                providerClass);
        }

        foreach(var item in discoveredServices.ValuesAsSpan()) ResolveService(item);
    }

    private void ParseDependencyAttribute(
        INamedTypeSymbol originalAttrClass,
        AttributeSyntax attrSyntax,
        ImmutableArray<IParameterSymbol>? parameters,
        out Disposability disposability,
        INamedTypeSymbol providerClass)
    {
        disposability = default;
        var isExternal = false;
        INamedTypeSymbol attrClass = originalAttrClass;

        if (attrClass is null || GetLifetime(ref attrClass, ref isExternal) is not { } lifetime) return;

        var _params = attrSyntax.ArgumentList?.Arguments ?? default;

        GetDependencyInfo(_model, attrClass.TypeArguments, _params, parameters, out var depInfo);

        if (HasNoType() || InterfaceRequiresInternalFactory()) return;

        var isAsync = depInfo.Factory.TryGetAsyncType(out var factoryType);

        if (isAsync)
        {
            UpdateAsyncStatus();

            if (depInfo.FactoryKind is SymbolKind.Method
                && !((IMethodSymbol)depInfo.Factory!).Parameters.Any(p => p.Type.ToDisplayString() is CancelTokenFQMetaName))
            {
                _diagnostics.TryAdd(ServiceContainerGeneratorDiagnostics.CancellationTokenShouldBeProvided(depInfo.Factory, attrSyntax));
            }
        }

        var thisDisposability = depInfo.ImplType.GetDisposability();

        if (thisDisposability > disposability) disposability = thisDisposability;

        var typeName = depInfo.ImplType.ToGlobalNamespaced();

        var exportTypeFullName = depInfo.IFaceType?.ToGlobalNamespaced() ?? typeName;

        ref var existingOrNew = ref discoveredServices.GetValueOrAddDefault((lifetime, exportTypeFullName, depInfo.Key), out var exists)!;

        var identifier = (depInfo.NameFormat is not null)
            ? string.Format(depInfo.NameFormat, depInfo.Key).RemoveDuplicates()
            : Extensions.SanitizeTypeName(depInfo.ImplType, methodsRegistry, dependencyRegistry, lifetime, depInfo.Key);

        if (exists)
        {
            _diagnostics.TryAdd(
                ServiceContainerGeneratorDiagnostics
                    .DuplicateService(lifetime, depInfo.Key, attrSyntax, typeName, exportTypeFullName));

            return;
        }
        else
        {
            if (depInfo.ImplType.IsPrimitive() && depInfo.Key is null)
            {
                _diagnostics.TryAdd(
                    ServiceContainerGeneratorDiagnostics
                        .PrimitiveDependencyShouldBeKeyed(lifetime, attrSyntax, typeName, exportTypeFullName));
            }

            existingOrNew = new(depInfo.ImplType, typeName, exportTypeFullName, depInfo.Key, depInfo.IFaceType)
            {
                Lifetime = lifetime,
                Key = depInfo.Key,
                ExternalGenerated = isExternal,
                ResolverMethodName = isExternal ? identifier : "Get" + identifier,
                CacheField = "_" + identifier.Camelize(),
                Factory = depInfo.Factory,
                FactoryKind = depInfo.FactoryKind,
                Disposability = thisDisposability,
                IsResolved = true,
                ResolvedBy = Generator.generatorGuid,
                Attributes = depInfo.ImplType.GetAttributes(),
                IsAsync = isAsync,
                ContainerType = providerClass,
                Cached = depInfo.Cached ?? depInfo.Factory is null,
                Params = Extensions.GetParameters(depInfo),
                DefaultParamValues = depInfo.DefaultParamValues
            };
        }

        bool HasNoType() => depInfo is { ImplType:null, IFaceType: null };

        bool InterfaceRequiresInternalFactory()
        {
            if (depInfo is { IFaceType: not null, ImplType:null, Factory:null } && !isExternal)
            {
                _diagnostics.TryAdd(
                    ServiceContainerGeneratorDiagnostics.InterfaceRequiresFactory(attrSyntax.Name));

                return true;
            }

            return false;
        }
    }

    void ResolveService(ServiceDescriptor service)
    {
        if (service.NotRegistered || service.IsCancelTokenParam) return;

        if (interfacesRegistry.Add((service.Key, service.ExportTypeName)))
        {
            interfaces += service.AddInterface;
        }

        if (service.Lifetime is Lifetime.Scoped && !hasScopedService) hasScopedService = true;

        if (service.Lifetime is not Lifetime.Transient)
        {
            if (service.IsAsync)
            {
                UpdateAsyncStatus();
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
                case Disposability.Disposable:

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
        }


        service.GenerateValue = service.ExternalGenerated
            ? service.BuildAsExternalValue
            : service.IsFactory
                ? service.UseFactoryValueResolver
                : service.BuildValueInstance;

        service.CheckParamsDependencies(
            _model,
            discoveredServices,
            methodsRegistry,
            dependencyRegistry,
            UpdateAsyncStatus,
            _compilation,
            _diagnostics,
            Generator.generatorGuid);

        if (service.Disposability > disposability) disposability = service.Disposability;

        if (service.ExternalGenerated) return;

        if (service.IsKeyed)
        {
            keyedMethods.TryAdd(service, service.BuildSwitchBranch);
        }
        else
        {
            methods += service.BuildResolver;
        }
    }

    private void UpdateAsyncStatus()
    {
        if (!requiresSemaphore)
        {
            requiresSemaphore = true;

            if (_compilation.GetTypeByMetadataName(CancelTokenFQMetaName) is { } cancelType)
            {
                string cancelTypeName = cancelType.ToGlobalNamespaced();

                discoveredServices.GetValueOrAddDefault(
                    (Lifetime.Singleton, cancelTypeName, null),
                    out _,
                    () => new(cancelType, cancelTypeName, cancelTypeName, null)
                    {
                        IsResolved = true,
                        ResolvedBy = Generator.generatorGuid,
                        IsCancelTokenParam = true
                    });
            }
        }
    }

    //TODO: add cancel token

    public void TryBuild(ImmutableArray<ITypeSymbol> usages, Map<string, byte> uniqueName, Action<string, string> addSource)
    {
        if (discoveredServices.IsEmpty /*interfaces == null*/) return;

        StringBuilder code = new(@"#nullable enable
");
        StringBuilder icode = new(@"#nullable enable
");
        var fileName = _providerClass.ToMetadataLongName(uniqueName);

        InteropServices.ResolveExternalDependencies(
            _compilation,
            _providerClass,
            discoveredServices);

        if (_providerClass.ContainingNamespace is { IsGlobalNamespace: false } ns)
        {
            code.Append("namespace ")
                .Append(ns.ToDisplayString()!)
                .Append(@";

");
            icode.Append("namespace ")
                .Append(ns.ToDisplayString()!)
                .Append(@";

");
        }

        var (modifiers, typeName) = _providerClass.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() switch
        {
            ClassDeclarationSyntax { Modifiers: var mods, Keyword: { } keyword, Identifier: { } identifier, TypeParameterList: var argList } =>
                ($"{mods} {keyword}".TrimStart(), $"{identifier}{argList}"),
            _ => ("partial class ", "")
        };

        #region Generate Container Interface

        icode.AppendLine(Generator.generatedCodeAttribute)
            .Append("public interface I")
            .Append(typeName);

        BuildDisposability(icode, true);

        methods?.Invoke(icode, false, Generator.generatedCodeAttribute);

        foreach (var service in keyedMethods.KeysAsSpan())
        {
            icode
                .Append(@"
	")
                .AppendLine(Generator.generatedCodeAttribute)
                .Append("    ");

            BuildKeyedResolverMethodSignature(icode, service);

            icode.Append(@";
");
        }

        if (hasScopedService)
        {
            icode.Append(@"
    ")
                .AppendLine(Generator.generatedCodeAttribute)
                .Append("    ")
                .Append(_providerClass.ContainingNamespace.ToGlobalNamespaced())
                .Append(".I")
                .Append(typeName)
                .Append(@" CreateScope();");
        }

        icode.Append(@"
}");

        addSource("I" + fileName + ".generated", icode.ToString());

        #endregion

        code.AppendLine(Generator.generatedCodeAttribute)
            .Append(modifiers)
            .AddSpace()
            .Append(typeName)
            .Append(@" : ")
            .Append('I')
            .Append(typeName)
            .Append(@"
{
    public static string Environment => global::System.Environment.GetEnvironmentVariable(""DOTNET_ENVIRONMENT"") ?? ""Development"";");

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
                .Append(@"    public ")
                .Append(_providerClass.ContainingNamespace.ToGlobalNamespaced())
                .Append(".I")
                .Append(typeName)
                .Append(@" CreateScope() =>
		new ").Append(providerTypeName).Append(@" { isScoped = true };
");
        }

        methods?.Invoke(code, true, Generator.generatedCodeAttribute);

        foreach (var (service, keyedValueResolverBuilder) in keyedMethods.AsSpan())
        {
            if (service.ExternalGenerated) continue;

            if (service.Cached)
            {
                code.Append(@"
    ")
                    .Append(Generator.generatedCodeAttribute)
                    .Append(@"
    private ");

                if (service.Lifetime is Lifetime.Singleton) code.Append("static ");

                code
                    .Append(service.FullTypeName)
                    .Append("? ")
                    .Append(service.CacheField)
                    .Append(@";");
            }

            code
                .Append(@"
	")
                .AppendLine(Generator.generatedCodeAttribute)
                .Append("    public ");

            if (service.IsAsync) code.Append("async ");

            BuildKeyedResolverMethodSignature(code, service);

            code.Append(@"
	{
		switch(key)
		{");

            keyedValueResolverBuilder(code);

            code.Append(@"
			default: throw InvalidKeyedService(""")
                .Append(service.ExportTypeName)
                .Append(@""", """)
                .Append(service.Key)
                .Append(@""");
		}
	}
");
        }

        if (keyedMethods.Count > 0)
        {
            code.Append(@"
	private static global::System.NotImplementedException InvalidKeyedService(string typeFullName, string serviceKeyName) =>
			new global::System.NotImplementedException($""There's no registered keyed-implementation for [{serviceKeyName} = global::SourceCrafter.DependencyInjection.IKeyedServiceProvider<{typeFullName}>.GetService({serviceKeyName} name)]"");
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

        BuildDisposability(code, false);

        var codeStr = code.Append('}').ToString();

        addSource(fileName + ".generated", codeStr);

        //CheckUsages(usages);
    }

    private void BuildDisposability(StringBuilder code, bool buildingInterface)
    {
        if (disposability > 0)
        {
            switch (disposability, buildingInterface)
            {
                case (Disposability.Disposable, true):

                    code.Append(@" : global::System.IDisposable	
{");

                    break;

                case (Disposability.AsyncDisposable, true):

                    code.Append(@" : global::System.IAsyncDisposable	
{");

                    break;

                case (Disposability.Disposable, false):

                    code.Append(@"
    ").AppendLine(Generator.generatedCodeAttribute)
                        .Append(@"    public void Dispose()
	{");

                    break;

                case (Disposability.AsyncDisposable, false):

                    code.Append(@"
    ").AppendLine(Generator.generatedCodeAttribute)
                        .Append(@"    public async global::System.Threading.Tasks.ValueTask DisposeAsync()
    {");

                    break;
            }

            if (!buildingInterface)
            {
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
        }
        else if (buildingInterface)
        {
            code.Append(@"	
{");
        }
    }

    private static void BuildKeyedResolverMethodSignature(StringBuilder code, ServiceDescriptor service)
    {
        if (service.IsAsync)
        {
            code.Append("global::System.Threading.Tasks.ValueTask<")
                .Append(service.ExportTypeName)
                .Append(@"> ")
                .Append(service.ResolverMethodName)
                .Append(@"Async(string key, global::System.Threading.CancellationToken cancellationToken = default)");
        }
        else
        {
            code.Append(service.ExportTypeName)
                .Append(@" ")
                .Append(service.ResolverMethodName)
                .Append(@"(")
                .Append(service.Key)
                .Append(" key)");
        }
    }
    /*
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

            var contextModel = _compilation.GetSemanticModel(invExpr.SyntaxTree);

            var refType = contextModel.GetSymbolInfo(((MemberAccessExpressionSyntax)invExpr.Expression).Expression).Symbol switch
            {
                ILocalSymbol { Type: { } rType } => rType,
                IFieldSymbol { Type: { } rType } => rType,
                IPropertySymbol { Type: { } rType } => rType,
                _ => null
            };

            if (refType is null || !SymbolEqualityComparer.Default.Equals(refType, _providerClass)) continue;

            var typeSymbol = contextModel.GetTypeInfo(type).Type;

            if (typeSymbol is null) continue;

            var typeFullName = typeSymbol.ToGlobalNamespaced();

            IFieldSymbol? key = null;
            ITypeSymbol? keyType = null;

            if (invExpr.ArgumentList.Arguments is [{ Expression: { } keyArg } arg])
            {
                keyType = contextModel.GetTypeInfo(keyArg).Type;

                if (keyType?.TypeKind is not TypeKind.Enum)
                {
                    _diagnostics.TryAdd(
                        ServiceContainerGeneratorDiagnostics.InvalidKeyType(arg.Expression));

                    continue;
                }
                else if (contextModel.GetSymbolInfo(keyArg).Symbol is IFieldSymbol { IsConst: true } fieldValue)
                {
                    key = fieldValue;
                }
            }

            discoveredServices.ForEach((DependecyKey itemK, ref ServiceDescriptor item) =>
            {
                if (item.ExportTypeName == typeFullName
                    && ((item.Key, key) switch
                    {
                        ({ } itemKey, { }) => SymbolEqualityComparer.Default.Equals(itemK.Key, key),
                        (var itemKey, _) => itemKey is null || SymbolEqualityComparer.Default.Equals(itemKey.Type, keyType)
                    })
                    && item.IsResolved)
                {
                    found = true;
                }
            });

            if (!found)
            {
                _diagnostics.TryAdd(
                    ServiceContainerGeneratorDiagnostics.UnresolvedDependency(invExpr, providerClassName, typeFullName, keyType, key));
            }
        }
    }
    */
}

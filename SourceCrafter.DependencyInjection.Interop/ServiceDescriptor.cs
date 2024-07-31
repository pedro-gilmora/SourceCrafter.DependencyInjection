using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace SourceCrafter.DependencyInjection.Interop
{
    public sealed class ServiceDescriptor(ITypeSymbol type, string typeName, string exportTypeFullName, IFieldSymbol? key, ITypeSymbol? _interface = null)
    {
        internal const string
            CancelTokenFQMetaName = "System.Threading.CancellationToken",
            EnumFQMetaName = "global::System.Enum",
            KeyParamName = "key",
            FactoryOrInstanceParamName = "factoryOrInstance",
            ImplParamName = "impl",
            IfaceParamName = "iface",
            CacheParamName = "cache",
            SingletonAttr = "global::SourceCrafter.DependencyInjection.Attributes.SingletonAttribute",
            ScopedAttr = "global::SourceCrafter.DependencyInjection.Attributes.ScopedAttribute",
            TransientAttr = "global::SourceCrafter.DependencyInjection.Attributes.TransientAttribute";

        public string FullTypeName = typeName;
        public string CacheMethodName = null!;
        public string CacheField = null!;
        public ITypeSymbol Type = type;
        public ITypeSymbol? Interface = _interface;
        public ISymbol? Factory;
        public SymbolKind FactoryKind;
        public Lifetime Lifetime;
        public bool Cached = true;
        public IFieldSymbol? Key = key;
        public Disposability Disposability;
        public ValueBuilder GenerateValue = null!;
        public CommaSeparateBuilder? BuildParams = null!;
        public SemanticModel TypeModel = null!;
        public bool Resolved;
        public ImmutableArray<AttributeData> Attributes = [];
        public ITypeSymbol ContainerType = null!;
        public bool NotRegistered = false;
        public bool IsAsync = false;
        internal bool ShouldAddAsyncAwait;
        internal bool IsCancelTokenParam;
        public bool ExternalGenerated;
        public Guid ResolvedBy;

        public readonly string ExportTypeName = exportTypeFullName;
        public readonly string? EnumKeyTypeName = key?.Type.ToGlobalNamespaced();

        private bool? isFactory;
        internal bool IsFactory => isFactory ??= Factory is not null;


        private bool? isNamed;

        internal bool IsKeyed => isNamed ??= Key is not null;

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

        void AppendFactoryOriginType(StringBuilder code)
        {
            if (!SymbolEqualityComparer.Default.Equals(Factory!.ContainingType, ContainerType))
            {
                code.Append(Factory.ContainingType.ToGlobalNamespaced()).Append('.');
            }
        }

        public void BuildSwitchBranch(StringBuilder code)
        {
            code.Append(@"
			case ")
                .Append(EnumKeyTypeName ?? "UnknownEnumType")
                .Append(".")
                .Append(Key?.Name ?? "UknownValue")
                .Append(@" : return ");

            if (ShouldAddAsyncAwait) code.Append("await ");

            BuildValue(code);

            code.Append(";");
        }

        public override string ToString()
        {
            return $"{{{Lifetime}}} {FullTypeName} {Key}".Trim();
        }

        internal void AddInterface(ref bool useIComma, StringBuilder code)
        {
            (useIComma.Exchange(true) ? code.Append(", ") : code)
                .Append(@"
	global::SourceCrafter.DependencyInjection.I");

            if (IsKeyed) code.Append("Keyed");
            if (IsAsync) code.Append("Async");

            code.Append("ServiceProvider<");

            if (IsKeyed) code.Append(EnumKeyTypeName).Append(", ");

            code.Append(ExportTypeName)
                .Append(">");
        }

        internal void UseCachedMethodResolver(StringBuilder code)
        {
            code.Append(CacheMethodName)
                .Append(IsAsync ? "Async(cancellationToken" : "(")
                .Append(")");
        }

        internal void BuildCachedResolver(StringBuilder code, string generatedCodeAttribute)
        {
            //TODO: Mix with old BuildMethod

            code.Append(@"
    ")
                .AppendLine(generatedCodeAttribute)
                .Append(@"
    private ");

            if (Lifetime is Lifetime.Singleton) code.Append("static ");

            code
                .Append(FullTypeName)
                .Append("? ")
                .Append(CacheField)
                .Append(@";

    ")
                .AppendLine(generatedCodeAttribute)
                .Append("    public ");

            if (Lifetime is Lifetime.Singleton)
                code.Append("static ");

            if (IsAsync)
            {
                code.Append("async global::System.Threading.Tasks.ValueTask<")
                    .Append(FullTypeName)
                    .Append("> ")
                    .Append(CacheMethodName)
                    .Append(@"Async(global::System.Threading.CancellationToken cancellationToken)
	{
		if (")
                    .Append(CacheField)
                    .Append(@" is not null)
        {
            return ")
                    .Append(CacheField)
                    .Append(@";
        }

        await __globalSemaphore.WaitAsync(cancellationToken);

        try
        {
            return ")
                    .Append(CacheField)
                    .Append(@" ??= ");

                if (IsFactory)
                {
                    UseFactoryResolver(code);
                }
                else
                {
                    code.Append("new ")
                        .Append(FullTypeName)
                        .Append("(");

                    bool comma = false;
                    BuildParams?.Invoke(ref comma, code);

                    code.Append(")");
                }

                code.Append(@";
        }
        finally
        {
            __globalSemaphore.Release();
        }");

            }
            else
            {
                code.Append(FullTypeName)
                    .Append(" ")
                    .Append(CacheMethodName)
                    .Append(@"()
	{
		if (")
                    .Append(CacheField)
                    .Append(@" is null)
			lock (__lock)
				return ")
                    .Append(CacheField)
                    .Append(@" ??= ");

                if (IsFactory)
                {
                    UseFactoryResolver(code);
                }
                else
                {
                    code.Append("new ")
                        .Append(FullTypeName)
                        .Append("(");

                    bool comma = false;
                    BuildParams?.Invoke(ref comma, code);

                    code.Append(")");
                }

                code.Append(@";
		return ")
                    .Append(CacheField)
                    .Append(@";");
            }

            code.Append(@"
	}
");
        }

        internal void BuildMethod(StringBuilder code, string generatedCodeAttribute)
        {
            code
                .Append(@"
    ")
                .AppendLine(generatedCodeAttribute)
                .Append("    ");

            if (IsAsync)
            {
                code.Append("global::System.Threading.Tasks.ValueTask<")
                    .Append(ExportTypeName)
                    .Append(@"> global::SourceCrafter.DependencyInjection.IAsyncServiceProvider<")
                    .Append(ExportTypeName)
                    .Append(@">.GetServiceAsync(global::System.Threading.CancellationToken cancellationToken)
    {
        return ");
            }
            else
            {
                code.Append(ExportTypeName)
                    .Append(@" global::SourceCrafter.DependencyInjection.IServiceProvider<")
                    .Append(ExportTypeName);
                code.Append(@">.GetService(");

                code.Append(@")
    {
        return ");
            }

            switch (Lifetime)
            {
                case Lifetime.Singleton:

                    if (IsFactory && !Cached)
                    {
                        UseFactoryResolver(code);
                    }
                    else
                    {
                        UseCachedMethodResolver(code);
                    }

                    code.Append(@";
    }
");
                    break;
                case Lifetime.Scoped:

                    code
                    .Append(@"isScoped 
			? ");

                    if (IsFactory && !Cached)
                    {
                        UseFactoryResolver(code);
                    }
                    else
                    {
                        UseCachedMethodResolver(code);
                    }

                    code.Append(@" 
			: throw InvalidCallOutOfScope(""")
                                .Append(FullTypeName).Append(@""");
    }
");
                    break;


                default:

                    if (IsFactory && !Cached)
                    {
                        UseFactoryResolver(code);
                    }
                    else
                    {
                        UseInstance(code);
                    }

                    code.Append(@";
    }
");
                    break;
            }
        }

        internal void UseFactoryResolver(StringBuilder code)
        {
            bool comma = false;

            switch (Factory)
            {
                case IMethodSymbol method:

                    AppendFactoryOriginType(code);

                    if (method.TypeArguments is { IsDefaultOrEmpty: false } and [{ } argType]
                        && SymbolEqualityComparer.Default.Equals(argType, Type)
                        && SymbolEqualityComparer.Default.Equals(method.ReturnType, Type))
                    {
                        code.Append(method.Name);
                        code.Append("<")
                            .Append(ExportTypeName)
                            .Append(">(");

                        BuildParams?.Invoke(ref comma, code);

                        code.Append(")");
                    }
                    else
                    {
                        code.Append(method.Name);
                        code.Append("(");

                        comma = false;
                        BuildParams?.Invoke(ref comma, code);

                        code.Append(")");
                    }

                    break;


                case IPropertySymbol { IsIndexer: bool isIndexer } prop:

                    AppendFactoryOriginType(code);

                    if (isIndexer)
                    {
                        code.Append(prop.Name);
                        code.Append("[");

                        comma = false;
                        BuildParams?.Invoke(ref comma, code);

                        code.Append("]");
                    }
                    else
                    {
                        code.Append(prop.Name);
                    }

                    break;


                case IFieldSymbol field:

                    AppendFactoryOriginType(code);

                    code.Append(field.Name);

                    break;

                default:

                    code.Append("default");

                    break;
            }
        }

        internal void CheckParamsDependencies(DependencyMap entries, Action updateAsyncContainerStatus, Compilation compilation)
        {
            var parameters = Factory switch
            {
                IMethodSymbol factoryMethod => factoryMethod.Parameters,
                IPropertySymbol { IsIndexer: true } factoryProperty => factoryProperty.Parameters,
                IFieldSymbol factoryProperty => [],
                _ => ((IMethodSymbol?)Type
                    .GetMembers()
                    .FirstOrDefault(static a => a is IMethodSymbol { MethodKind: MethodKind.Constructor, DeclaredAccessibility: Accessibility.Internal or Accessibility.Public }))
                    ?.Parameters ?? default!
            };

            if (parameters.IsDefaultOrEmpty) return;    

            IFieldSymbol? serviceKey = null;

            Lifetime lifetime = Lifetime.Singleton;

            foreach (var _param in parameters)
            {
                lifetime = GetParamMetadata(_param.GetAttributes(), out serviceKey);
                 
                var paramTypeName = _param.Type.ToGlobalNamespaced();

                ref var found = ref entries.GetValueOrInsertor(
                    (lifetime, paramTypeName, serviceKey),
                    out Action<ServiceDescriptor>? insert);

                if (found != null)
                {
                    if (found.IsAsync && !IsAsync)
                    {
                        IsAsync = true;

                        updateAsyncContainerStatus();
                    }

                    BuildParams += found.BuildAsParam;
                }
                else if(paramTypeName.EndsWith(CancelTokenFQMetaName))
                {
                    BuildParams += AppendCancelToken;
                }
                else if (insert != null)
                {
                    ServiceDescriptor item = new(_param.Type, paramTypeName, paramTypeName, serviceKey)
                    {
                        Attributes = _param.GetAttributes(),
                        ContainerType = ContainerType,
                        NotRegistered = true,
                        Lifetime = lifetime,
                    };

                    BuildParams += item.BuildAsParam;

                    insert(item);
                }
            }

            Lifetime GetParamMetadata(ImmutableArray<AttributeData> attrs, out IFieldSymbol? key)
            {
                key = null;

                foreach (var attr in attrs)
                {
                    if (attr.ApplicationSyntaxReference?.GetSyntax() is AttributeSyntax attrSyntax)
                    {
                        var model = compilation.GetSemanticModel(attrSyntax.SyntaxTree);

                        if (attrSyntax.ArgumentList?.Arguments is { Count: > 0 } args)
                        {
                            foreach (var arg in args)
                            {
                                if (model.GetSymbolInfo(arg.Expression).Symbol is IFieldSymbol
                                    {
                                        IsConst: true,
                                        Type: INamedTypeSymbol { TypeKind: var kind }
                                    } field)
                                {
                                    if (kind is TypeKind.Enum)
                                    {
                                        key = field;
                                    }
                                    else
                                    {
                                        ServiceContainerGeneratorDiagnostics.InvalidKeyType(arg.Expression);
                                    }

                                    goto checkType;
                                }
                            }
                        }
                    }

                checkType:

                    switch (attr.AttributeClass?.ToGlobalNamespaced())
                    {
                        case SingletonAttr: return Lifetime.Singleton;
                        case ScopedAttr: return Lifetime.Scoped;
                        case TransientAttr: return Lifetime.Transient;
                    }

                    continue;
                }

                return Lifetime.Singleton;
            }
        }

        private void AppendCancelToken(ref bool useIComma, StringBuilder code)
        {
            if (useIComma.Exchange(true)) code.Append(", "); 
            
            code.Append("cancellationToken");
        }

        private void BuildAsParam(ref bool useIComma, StringBuilder code)
        {
            if (useIComma.Exchange(true)) code.Append(", ");

            if (IsAsync) code.Append("await ");

            BuildValue(code);
        }

        internal void UseInstance(StringBuilder code)
        {
            if (IsFactory && Cached && Factory is IFieldSymbol { Name: { } name })
            {
                code.Append(name);
            }
            else
            {
                code.Append("new ")
                    .Append(FullTypeName)
                    .Append("(");

                bool comma = false;
                BuildParams?.Invoke(ref comma, code);

                code.Append(")");
            }
        }

        internal void BuildDisposeAsyncStatment(StringBuilder code)
        {
            code.Append(@"
		if(").Append(CacheField).Append(" is not null) await ").Append(CacheField).Append(".DisposeAsync();");
        }

        internal void BuildDisposeStatment(StringBuilder code)
        {
            code.Append(@"
		").Append(CacheField).Append("?.Dispose();");
        }
    }
}

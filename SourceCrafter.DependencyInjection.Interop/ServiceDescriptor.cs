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
        public string ResolverMethodName = null!;
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
        public bool IsResolved;
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
			case ")
                .Append(EnumKeyTypeName)
                .Append('.')
                .Append(Key!.Name)
                .Append(@" :
");

            BuildResolverBody(code, 2);

            code.Append(@"
");
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
            //TODO: Mix with old BuildMethod
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
                var paramAttrs = _param.GetAttributes();

                lifetime = GetParamMetadata(paramAttrs, out serviceKey);

                var paramTypeName = _param.Type.ToGlobalNamespaced();

                ref var found = ref entries.GetValueOrInsertor(
                    (lifetime, paramTypeName, serviceKey),
                    out var insert);

                if (found != null)
                {
                    if (found.IsAsync && !IsAsync)
                    {
                        IsAsync = true;

                        updateAsyncContainerStatus();
                    }

                    BuildParams += found.BuildAsParam;
                }
                else if (paramTypeName.EndsWith(CancelTokenFQMetaName))
                {
                    BuildParams += AppendCancelToken;
                }
                else if (insert != null)
                {
                    ServiceDescriptor item = new(_param.Type, paramTypeName, paramTypeName, serviceKey)
                    {
                        Attributes = paramAttrs,
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
    }
}

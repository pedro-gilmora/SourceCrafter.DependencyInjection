using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;

[assembly: InternalsVisibleTo("SourceCrafter.DependencyInjection")]

namespace SourceCrafter.DependencyInjection.Interop
{
    public delegate (string, string)? ContainerRegistrationHandler(Compilation compilation, ITypeSymbol serviceContainer, Set<ServiceDescriptor> servicesDescriptors);
    public delegate Action? ValueGeneratorResolver(StringBuilder code, Compilation compilation, ITypeSymbol unresolvedType);

    public delegate void CommaSeparateBuilder(ref bool useIComma, StringBuilder code);
    public delegate void VarNameBuilder(StringBuilder code);
    public delegate void MemberBuilder(StringBuilder code, string generatedCodeAttribute);
    public delegate void ParamsBuilder(StringBuilder code);
    public delegate void ResolveDependencyHandler(ref ServiceDescriptor item);

    public static class DependencyInjectionPartsGenerator
    {
        public static event ContainerRegistrationHandler? OnContainerRegistered;
        public static event ValueGeneratorResolver? ResolveValueGenerator;
        //public static event ResolveDependencyHandler? OnResolveDependency;

        internal static List<(string, string)> InvokeContainerRegistration(StringBuilder code, Compilation compilation, ITypeSymbol serviceContainer, Set<ServiceDescriptor> servicesDescriptors)
        {
            List<(string, string)> list = [];

            foreach (ContainerRegistrationHandler item in OnContainerRegistered?.GetInvocationList() ?? [])
            {
                if (item(compilation, serviceContainer, servicesDescriptors) is { } itemToAdd)
                    list.Add(itemToAdd);
            }

            return list;
        }

        internal static Action? ResolveDependencyValueGenerator(StringBuilder code, Compilation compilation, ITypeSymbol type)
        {
            return (ResolveValueGenerator
                ?.GetInvocationList()
                ?.FirstOrDefault() as ValueGeneratorResolver)
                ?.Invoke(code, compilation, type);
        }

        //internal static void ResolveDependency(ref ServiceDescriptor item)
        //{
        //    OnResolveDependency?.Invoke(ref item);
        //}
    }

    public enum ServiceType : byte { Singleton, Scoped, Transient, NamedSingleton, NamedScoped, NamedTransient }

    public sealed class ServiceDescriptor(ITypeSymbol type, string typeName, string exportTypeFullName, ITypeSymbol? _interface = null)
    {
        public string FullTypeName = typeName;
        public string Identifier = null!;
        public ITypeSymbol Type = type;
        public ITypeSymbol? Interface = _interface;
        public IMethodSymbol? Factory;
        public ServiceType ServiceType;
        public string? Name;
        public byte Disposability;
        public VarNameBuilder GenerateValue = null!;
        public CommaSeparateBuilder? BuildParams = null!;
        public SemanticModel TypeModel = null!;
        public bool Resolved;
        public ImmutableArray<AttributeData> Attributes = [];
        public ITypeSymbol ContainerType = null!;
        public bool NotRegistered = false;

        public readonly string ExportTypeName = exportTypeFullName;

        internal bool IsNamed => Name is not null;

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

        public Action BuildSwitchBranch(StringBuilder code)
        {
            return () =>
            {
                code.Append(@"
			case """)
                    .Append(Name).Append(@""": return ");

                BuildValue(code);

                code.Append(";");
            };

        }

        public override string ToString()
        {
            return $"{{{ServiceType}}} {FullTypeName} {Name}".Trim();
        }

        internal void AddInterface(ref bool useIComma, StringBuilder code)
        {
            (useIComma.Exchange(true) ? code.Append(", ") : code)
                .Append(@"
	global::SourceCrafter.DependencyInjection.I");

            if (IsNamed) code.Append("Keyed");

            code.Append("ServiceProvider<")
                .Append(ExportTypeName)
                .Append(">");
        }

        internal void BuildWithVarName(StringBuilder code)
        {
            code.Append(Identifier);
        }

        internal void BuildProperty(StringBuilder code, string generatedCodeAttribute)
        {
            code.Append(@"
    private ");

            if (ServiceType is ServiceType.Singleton or ServiceType.NamedSingleton)
                code.Append("static ");

            code
                .Append(FullTypeName)
                .Append("? _")
                .Append(Identifier)
                .Append(@";

    ")
                .AppendLine(generatedCodeAttribute)
                .Append("    private ");

            if (ServiceType is ServiceType.Singleton or ServiceType.NamedSingleton)
                code.Append("static ");

            code
                .Append(FullTypeName)
                .Append(" ")
                .Append(Identifier)
                .Append(@"
	{
		get
		{
			if (_")
                .Append(Identifier)
                .Append(@" is null)
				lock (_lock)
					return _")
                .Append(Identifier)
                .Append(@" ??= ").Append("new ")
                .Append(FullTypeName)
                .Append(@"(");

            bool useComma = false;
            BuildParams?.Invoke(ref useComma, code);

            code.Append(@");
			return _")
                .Append(Identifier)
                .Append(@";
		}
	}
");
        }

        internal void BuildMethod(StringBuilder code, string generatedCodeAttribute)
        {
            code
                .Append(@"
    ")
                .AppendLine(generatedCodeAttribute)
                .Append(@"    ")
                .Append(FullTypeName)
                .Append(@" 
		global::SourceCrafter.DependencyInjection.IServiceProvider<")
                .Append(ExportTypeName)
                .Append(@">
			.GetService() => ");

            switch (ServiceType)
            {
                case ServiceType.Singleton or ServiceType.NamedSingleton:

                    BuildWithVarName(code);

                    code.Append(@";
");
                    break;
                case ServiceType.Scoped or ServiceType.NamedScoped:

                    code
                    .Append(@"isScoped 
				? ");

                    BuildWithVarName(code);

                    code.Append(@" 
				: throw InvalidCallOutOfScope(""")
                                .Append(FullTypeName).Append(@""");
");
                    break;


                default:

                    BuildInstance(code);

                    code.Append(@";
");
                    break;
            }
        }

        internal void ParseParams(Set<ServiceDescriptor> entries)
        {
            var parameters = Factory is not null
                ? Factory.Parameters
                : ((IMethodSymbol?)Type
                    .GetMembers()
                    .FirstOrDefault(static a => a is IMethodSymbol { MethodKind: MethodKind.Constructor, DeclaredAccessibility: Accessibility.Internal or Accessibility.Public }))
                    ?.Parameters ?? default!;

            if (!parameters.IsDefaultOrEmpty)
            {
                string? paramName = null;

                foreach (var _param in parameters)
                {
                    var attrs = _param.GetAttributes();
                    if (attrs
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

                    Action<ServiceDescriptor>? insert = null;
                    Action<StringBuilder> buildValue = null!;

                    for (int i = 0; i < 6; i++)
                    {
                        ref var found = ref entries.GetValueOrInsertor(
                            Extensions.GenKey((ServiceType)i, paramTypeName, paramName),
                            out insert);

                        if (found != null)
                        {
                            buildValue = found.BuildValue;
                            break;
                        }
                        else if(i == 5 && insert != null)
                        {
                            ServiceDescriptor item = new(_param.Type, paramTypeName, paramTypeName)
                            {
                                Attributes = attrs,
                                ContainerType = ContainerType,
                                NotRegistered = true
                            };

                            buildValue = item.BuildValue;

                            insert(item);
                        }
                    }

                    BuildParams += (ref bool useComma, StringBuilder code) =>
                    {
                        if (useComma.Exchange(true)) code.Append(", ");

                        (buildValue ?? (code => code.Append("default")))(code);
                    };
                }

            }
        }

        internal void BuildInstance(StringBuilder code)
        {
            if (Factory is not null)
            {
                //if()
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
    }
}

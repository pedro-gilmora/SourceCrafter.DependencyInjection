using Microsoft.CodeAnalysis;
using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;

//[assembly: InternalsVisibleTo("SourceCrafter.MappingGenerator.UnitTests")]
namespace SourceCrafter.DependencyInjection;

[AttributeUsage(AttributeTargets.Assembly)]
public class DependencyResolverAttribute<IDependencyResolver> : Attribute;

[Generator]
public class Generator : IIncrementalGenerator
{
    internal readonly static string generatedCodeAttribute = ParseToolAndVersion();
    internal readonly static Guid generatorGuid = new("31C54896-DE65-4FDC-8EBA-5A169A6E3CBB");

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
#if DEBUG_SG
        System.Diagnostics.Debugger.Launch();
#endif

        var resolvers = context.CompilationProvider.SelectMany((comp, _) => GetResolvers(comp)).Collect();

        var getServiceCheck = context.SyntaxProvider
                .CreateSyntaxProvider(
                    (syntax, _) => syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name: GenericNameSyntax { TypeArgumentList.Arguments: [var a], Identifier.ValueText: "GetService" } } },
                    (gsc, _) => (InvocationExpressionSyntax)gsc.Node)
                .Collect();

        var generatedResolvers = context.SyntaxProvider
                .CreateSyntaxProvider(
                    (syntax, _) => syntax is AttributeSyntax {Name : GenericNameSyntax { Identifier.ValueText: { } id} } && id.StartsWith("DependencyResolver"),
                    (gsc, _) => (AttributeSyntax)gsc.Node)
                .Collect();

        var servicesContainers = context.SyntaxProvider
                .ForAttributeWithMetadataName("SourceCrafter.DependencyInjection.Attributes.ServiceContainerAttribute",
                    (node, a) => true,
                    (t, c) => (t.SemanticModel, Class: (INamedTypeSymbol)t.TargetSymbol))
                .Collect();

        context.RegisterSourceOutput(context.CompilationProvider
            .Combine(servicesContainers)
            .Combine(getServiceCheck)
            .Combine(resolvers), static (p, info) =>
        {
            var (((compilation, servicesContainers), serviceCheck), resolvers) = info;

            Map<string, byte> uniqueName = new(StringComparer.Ordinal);

            var sb = new StringBuilder("/*").AppendLine();
            int start = sb.Length;

            Set<Diagnostic> diagnostics = new(e => e.Location.ToString());

            try
            {
                foreach (var item in servicesContainers)
                {
                    new ServiceContainerGenerator(compilation, item.SemanticModel, item.Class, diagnostics)
                        .TryBuild(serviceCheck, uniqueName, p.AddSource);
                }
            }
            catch (Exception e)
            {
                sb.AppendLine(e.ToString());
            }

            diagnostics.ForEach(item => p.ReportDiagnostic(item));

            if (sb.Length > start)
            {
                p.AddSource("errors", sb.ToString());
            }
        });
    }

    private static IEnumerable<INamedTypeSymbol> GetResolvers(Compilation comp)
    {
        foreach (var referencedAssembly in comp.ExternalReferences)
        {
            if (comp.GetAssemblyOrModuleSymbol(referencedAssembly) is not IAssemblySymbol assemblySymbol) continue;

            foreach (var attribute in assemblySymbol.GetAttributes())
            {
                if (attribute.AttributeClass is INamedTypeSymbol cls && cls?.ToGlobalNonGenericNamespace() is "global::SourceCrafter.DependencyInjection.Interop.DependencyResolverAttribute")
                {
                    yield return cls;
                }
            }
        }
    }

    private static string ParseToolAndVersion()
    {
        string name = "SourceCrafter.DependencyInjection";

        int i = 0;

        foreach (var item in Assembly.GetExecutingAssembly().FullName.Split(','))
        {
            switch (item.Split('='))
            {
                case [{ } _name] when i <= 1: name = _name; break;
                case [" Version", { } version]: return $@"[global::System.CodeDom.Compiler.GeneratedCode(""{name}"", ""{version}"")]";
            }
            i++;
        }

        return $@"[global::System.CodeDom.Compiler.GeneratedCode(""{name}"", ""1.0.0"")]";
    }

}

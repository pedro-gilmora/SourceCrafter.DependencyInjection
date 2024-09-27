using Microsoft.CodeAnalysis;
using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using SourceCrafter.DependencyInjection.Attributes;

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

        //var getExternal = context.SyntaxProvider
        //        .CreateSyntaxProvider(
        //            (syntax, _) => syntax is  BaseTypeDeclarationSyntax { Identifier.ValueText: "SingletonAttribute" or "ScopedAttribute" or "TransientAttribute" } ,
        //            (gsc, _) => gsc.SemanticModel.GetTypeInfo(((ClassDeclarationSyntax)gsc.Node.Parent!)).Type!)
        //        .Collect();

        var servicesContainers = context.SyntaxProvider
                .ForAttributeWithMetadataName("SourceCrafter.DependencyInjection.Attributes.ServiceContainerAttribute",
                    (node, a) => true,
                    (t, c) => (t.SemanticModel, Class: (INamedTypeSymbol)t.TargetSymbol))
                .Collect();

        context.RegisterSourceOutput(context.CompilationProvider
            .Combine(servicesContainers),
            //.Combine(getExternal), 
            static (p, info) =>
        {
            var (compilation, servicesContainers) = info;

            Map<string, byte> uniqueName = new(StringComparer.Ordinal);

            var sb = new StringBuilder("/*").AppendLine();
            int start = sb.Length;

            Set<Diagnostic> diagnostics = Set<Diagnostic>.Create(e => e.Location.GetHashCode());

            try
            {
                foreach (var item in servicesContainers)
                {
                    new ServiceContainerGenerator(compilation, item.SemanticModel, item.Class, diagnostics)
                        .TryBuild([], uniqueName, p.AddSource);
                }

                foreach(var item in diagnostics) p.ReportDiagnostic(item);
            }
            catch (Exception e)
            {
                sb.AppendLine(e.ToString());
            }
            if (sb.Length > start)
            {
                p.AddSource("errors", sb.Append("*/").ToString());
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
                if (attribute.AttributeClass is INamedTypeSymbol cls && cls?.ToGlobalNonGenericNamespace() is "global::SourceCrafter.DependencyInjection.Interop.UseAttribute")
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

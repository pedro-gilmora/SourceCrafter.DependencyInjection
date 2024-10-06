global using DependencyMap = SourceCrafter.DependencyInjection.Interop.Map<(SourceCrafter.DependencyInjection.Interop.Lifetime, string, string), SourceCrafter.DependencyInjection.Interop.ServiceDescriptor>;

using Microsoft.CodeAnalysis;
using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SourceCrafter.DependencyInjection;
using SourceCrafter.DependencyInjection.Interop;
using System.Collections.Immutable;
using static Microsoft.Extensions.DependencyInjection.ServiceDescriptor;

[Generator]
public class Generator : IIncrementalGenerator
{
    private const string serviceContainerFullTypeName = "SourceCrafter.DependencyInjection.Attributes.ServiceContainerAttribute";
    internal readonly static string generatedCodeAttribute = ParseToolAndVersion();
    internal readonly static Guid generatorGuid = new("31C54896-DE65-4FDC-8EBA-5A169A6E3CBB");

    ~Generator()
    {
        server?.Stop();
    }
    static object _lock = new();
    static DependenciesServer? server;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
#if DEBUG_SG
        System.Diagnostics.Debugger.Launch();
#endif

        var getExternal = context.SyntaxProvider
                .CreateSyntaxProvider(
                    (syntax, _) => syntax is AttributeSyntax { Parent: AttributeListSyntax { Target.Identifier.ValueText: "assembly" } },
                    (gsc, _) =>
                    {
                        if (gsc.SemanticModel.GetTypeInfo(gsc.Node).Type is { BaseType.Name: "SingletonAttribute" or "ScopedAttribute" or "TransientAttribute" } type)
                            return gsc.SemanticModel.Compilation.Assembly.GetAttributes().Where(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, type));
                        return [];
                    })
                .SelectMany((info, _) => info)
                .Collect();

        var scopedUsage = context.SyntaxProvider
                .CreateSyntaxProvider<InvokeInfo>(
                    (syntax, _) => syntax is MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax },
                    (gsc, _) =>
                    {
                        if (gsc.Node is MemberAccessExpressionSyntax { Name: IdentifierNameSyntax { Identifier.ValueText: { } name } method, Expression: IdentifierNameSyntax { } refVar })
                        {
                            if (gsc.SemanticModel.GetSymbolInfo(refVar).Symbol is ILocalSymbol { Type: { } containerType } local
                                && containerType.GetAttributes().Any(attr => attr.AttributeClass?.ToGlobalNamespaced().EndsWith(serviceContainerFullTypeName) ?? false))
                            {
                                var isCtor = local.DeclaringSyntaxReferences
                                    .Any(s => // var declaration or parameter
                                        s.GetSyntax() is VariableDeclaratorSyntax
                                        { Initializer.Value: ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax });

                                return new(containerType, name, method, isCtor);
                            }
                            else if (gsc.SemanticModel.GetSymbolInfo(refVar).Symbol is ILocalSymbol { Type: { } containerType2 } parameter
                                && containerType2.GetAttributes().Any(attr => attr.AttributeClass?.ToGlobalNamespaced().EndsWith(serviceContainerFullTypeName) ?? false))
                            {
                                return new(containerType2, name, method, false);
                            }
                        }
                        return null!;
                    })
                .Where(info => info is not null)
                .Collect();

        var servicesContainers = context.SyntaxProvider
                .ForAttributeWithMetadataName(serviceContainerFullTypeName,
                    (node, a) => true,
                    (t, c) => (t.SemanticModel, Class: (INamedTypeSymbol)t.TargetSymbol))
                .Collect();

        // Different containers registry for dependencies providers
        Dictionary<string, DependencyMap> containers = [];

        context.RegisterSourceOutput(context.CompilationProvider
            .Combine(servicesContainers)
            .Combine(getExternal)
            .Combine(scopedUsage)
            ,(p, info) =>
            {
                var (((compilation, servicesContainers), externals), serviceCall) = info;

                var errorsSb = new StringBuilder("/*").AppendLine();

                int start = errorsSb.Length;
                
                if (!EnsureDependenciesServer(p, containers)) return;

                try
                {
                    // Uniqueness in generated names
                    Map<string, byte> uniqueName = new(StringComparer.Ordinal);

                    Set<Diagnostic> diagnostics = Set<Diagnostic>.Create(e => e.Location.GetHashCode());

                    foreach (var serviceContainer in servicesContainers)
                    {
                        ServiceContainer
                            .Parse(
                                compilation,
                                serviceContainer.SemanticModel,
                                serviceContainer.Class,
                                diagnostics,
                                externals,
                                generatedCodeAttribute,
                                serviceCall
                                    .Where(usage => SymbolEqualityComparer.Default.Equals(usage.ContainerType, serviceContainer.Class))
                                    .ToImmutableArray())
                            .Build(containers, [], uniqueName, p.AddSource);
                    }

                    foreach (var item in diagnostics) p.ReportDiagnostic(item);
                }
                catch (Exception e)
                {
                    errorsSb.AppendLine(e.ToString());
                }

                if (errorsSb.Length > start)
                {
                    p.AddSource("errors", errorsSb.Append("*/").ToString());
                }
            });
    }

    private static bool EnsureDependenciesServer(SourceProductionContext p, Dictionary<string, DependencyMap> containers)
    {        
        int attempts = 2;

        while (attempts-- > -1)
            try
            {
                if (server is null)
                    lock (_lock) (server ??= new())._containers = containers;
                else
                    server._containers = containers;

                return true;
            }
            catch(Exception ex)
            {
                if (attempts == 0)
                {
                    p.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor(
                            "SCDI0000",
                            "Dependencies server could not initiate.",
                            $"Error: {ex}",
                            "Operability",
                            DiagnosticSeverity.Info,
                            true),
                        null)); 
                    
                    return false;
                }
            }

        return false;
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
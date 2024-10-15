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
public sealed class Generator : IIncrementalGenerator
{
    private const string serviceContainerFullTypeName = "SourceCrafter.DependencyInjection.Attributes.ServiceContainerAttribute";
    internal readonly static string generatedCodeAttribute = ParseToolAndVersion();
    internal readonly static Guid generatorGuid = new("31C54896-DE65-4FDC-8EBA-5A169A6E3CBB");

    ~Generator()
    {
        Dependencies.Clear();
    }
    //static DependenciesServer? server;

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
                    GetInvokeInfos)
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
            ,(context, info) =>
            {
                var (((compilation, servicesContainers), externals), serviceCall) = info;

                var errorsSb = new StringBuilder("/*").AppendLine();

                var net9Lock = compilation.GetTypeByMetadataName("System.Threading.Lock")?.ToGlobalNamespaced();

                int start = errorsSb.Length;

                if (!Dependencies.TryBroadcastDependencies(context, containers, out string error)) 
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            new DiagnosticDescriptor(
                                "SCDI00",
                                "Dependencies server could not initiate.",
                                $"Error: {error}",
                                "Operability",
                                DiagnosticSeverity.Info,
                                true),
                            null));

                    return; 
                }

                try
                {
                    // Uniqueness in generated names
                    Map<string, byte> uniqueName = new(StringComparer.Ordinal);

                    Set<Diagnostic> diagnostics = Set<Diagnostic>.Create(e => e.Location.GetHashCode());

                    foreach (var (model, cls) in servicesContainers)
                    {
                        var declaration = cls.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();

                        if (declaration is not ClassDeclarationSyntax or InterfaceDeclarationSyntax)
                        {
                            context.ReportDiagnostic(
                                Diagnostic.Create(
                                    new DiagnosticDescriptor(
                                        "SCDI11",
                                        "Structs are not supported as containers",
                                        "",
                                        "Design",
                                        DiagnosticSeverity.Error,
                                        true),
                                    null));
                            
                            continue;
                        }

                        ServiceContainer
                            .Parse(
                                compilation,
                                model,
                                cls,
                                diagnostics,
                                externals,
                                generatedCodeAttribute,
                                serviceCall
                                    .Where(usage => SymbolEqualityComparer.Default.Equals(usage.ContainerType, cls))
                                    .ToImmutableArray())
                            .Build(containers, [], uniqueName, context.AddSource, net9Lock, declaration);
                    }

                    foreach (var item in ((Set<int, Diagnostic>)diagnostics)) context.ReportDiagnostic(item);
                }
                catch (Exception e)
                {
                    errorsSb.AppendLine(e.ToString());
                }

                if (errorsSb.Length > start)
                {
                    context.AddSource("errors", errorsSb.Append("*/").ToString());
                }
            });
    }

    private static InvokeInfo GetInvokeInfos(GeneratorSyntaxContext gsc, System.Threading.CancellationToken _)
    {
        if (gsc.Node is MemberAccessExpressionSyntax
            {
                Name: IdentifierNameSyntax
                { Identifier.ValueText: { } name } method,
                Expression: IdentifierNameSyntax { } refVar
            })
        {
            if (gsc.SemanticModel.GetSymbolInfo(refVar).Symbol is ILocalSymbol { Type: { } containerType } local
                && containerType.GetAttributes()
                                .Any(attr => attr.AttributeClass?.ToGlobalNamespaced().EndsWith(serviceContainerFullTypeName) ?? false))
            {
                var isCtor = local.DeclaringSyntaxReferences
                    .Any(s =>
                    {
                        var varDecl = (s.GetSyntax() as VariableDeclaratorSyntax)?.Initializer?.Value;
                        return varDecl is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax
                            || (containerType.TypeKind is TypeKind.Struct && varDecl is DefaultExpressionSyntax);
                    });

                return new(containerType, name, method, isCtor);
            }
            else if (gsc.SemanticModel.GetSymbolInfo(refVar).Symbol is ILocalSymbol { Type: { } containerType2 } parameter
                && containerType2.GetAttributes().Any(attr => attr.AttributeClass?.ToGlobalNamespaced().EndsWith(serviceContainerFullTypeName) ?? false))
            {
                return new(containerType2, name, method, false);
            }
        }
        return null!;
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
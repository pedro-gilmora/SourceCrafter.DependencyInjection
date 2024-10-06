using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using SourceCrafter.DependencyInjection.Interop;

using System;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("SourceCrafter.DependencyInjection")]
namespace SourceCrafter.DependencyInjection;

internal static class ServiceContainerGeneratorDiagnostics
{
    internal static Diagnostic DuplicateService(Lifetime lifetime, string? key, AttributeSyntax attrSyntax, string typeName, string exportTypeFullName)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI01",
            title: $"[{lifetime},{exportTypeFullName}, {key}] is already present in this container",
            messageFormat: "'{0}' is duplicate",
            category: "SourceCrafter.DependencyInjection.Usage",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: $"[{lifetime},{exportTypeFullName}, {key}] is already present and it should be removed in order to properly compile the project."
        );

        return Diagnostic.Create(rule, attrSyntax.GetLocation(), typeName);
    }

    internal static Diagnostic PrimitiveDependencyShouldBeKeyed(
        Lifetime lifetime,
        SyntaxNode? node,
        string typeName,
        string exportTypeFullName)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI02",
            title: $"[{lifetime}, {exportTypeFullName}] should be keyed",
            messageFormat: "'{0}' should be properly keyed as service to provide multiple primitive value as dependency",
            category: "SourceCrafter.DependencyInjection.Usage",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: $"[{lifetime}, {exportTypeFullName}] should be properly keyed as service to provide multiple primitive value as dependency"
        );

        return Diagnostic.Create(rule, node?.GetLocation(), typeName);
    }

    internal static Diagnostic UnresolvedDependency(
        SyntaxNode invExpr,
        string providerClassName,
        string typeFullName,
        string? key)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI03",
            title: "Type not registered in container",
            messageFormat: "'{0}' is not registered in [{1}] container",
            category: "SourceCrafter.DependencyInjection.Usage",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Type is not registered in container"
        );

        return Diagnostic.Create(
            rule,
            invExpr.GetLocation(),
            $@"[{(key is { } ? key + ", " : null)}{typeFullName}]",
            providerClassName);
    }

    internal static Diagnostic CancellationTokenShouldBeProvided(ISymbol factory, SyntaxNode? node)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI04",
            title: $"",
            messageFormat: "A CancellationToken parameter should be provided to factory method '{0}'",
            category: "SourceCrafter.DependencyInjection.Usage",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true
        );

        return Diagnostic.Create(
            rule,
            node?.GetLocation(),
            factory);
    }

    internal static Diagnostic InvalidKeyType(ExpressionSyntax arg)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI05",
            title: "Not valid key type",
            messageFormat: "Invalid key type. Only enum keys are allowed",
            category: "SourceCrafter.DependencyInjection.Usage",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Invalid key type. Only enum keys are allowed"
        );

        return Diagnostic.Create(rule, arg.GetLocation());
    }

    internal static Diagnostic InterfaceRequiresFactory(ExpressionSyntax node)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI06",
            title: "Container-internal interface-only resolver requires factory method",
            messageFormat: "Container-internal interface-only resolver requires factory method in order to provide as dependency",
            category: "SourceCrafter.DependencyInjection.Usage",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Please provide a value for [factoryOrInstance] parameter"
        );

        return Diagnostic.Create(rule, node.GetLocation());
    }

    internal static Diagnostic DependencyWithUnresolvedParameters(
        SyntaxNode invExpr,
        string providerClassName)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI03",
            title: "Dependency has unresolved types",
            messageFormat: "'{0}' has some unresolved types.",
            category: "SourceCrafter.DependencyInjection.Usage",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Dependency has unresolved types. Make sure to register properly the required types for minimal parameterized constructors"
        );

        return Diagnostic.Create(
            rule,
            invExpr.GetLocation(),
            providerClassName);
    }

    internal static Diagnostic ParamInterfaceTypeWithoutImplementation(
        SyntaxNode? parameter,
        string providerClassName)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI03",
            title: "Dependency has unresolved types",
            messageFormat: "'{0}' has some unresolved types.",
            category: "SourceCrafter.DependencyInjection.Usage",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Dependency has unresolved types. Make sure to register properly the required types for minimal parameterized constructors"
        );

        return Diagnostic.Create(
            rule,
            parameter?.GetLocation(),
            providerClassName);
    }

    internal static Diagnostic DependencyCallShouldBeScoped(string providerName, IdentifierNameSyntax methodNameSyntax)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI03",
            title: "Resolver method called on non-scoped instance.",
            messageFormat: $"Scoped resolver method [{providerName}.{methodNameSyntax.Identifier.ValueText}] should be called just through [{providerName}.CreateScope()]",
            category: "SourceCrafter.DependencyInjection.Usage",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Dependency has unresolved types. Make sure to register properly the required types for minimal parameterized constructors"
        );

        return Diagnostic.Create(
            rule,
            methodNameSyntax.GetLocation());
    }
}
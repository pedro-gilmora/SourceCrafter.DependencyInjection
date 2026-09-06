using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using System;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("SourceCrafter.DependencyInjection")]
namespace SourceCrafter.DependencyInjection;

internal static class ServiceContainerDiagnostics
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

    internal static Diagnostic PrimitiveDependencyMustBeKeyed(
        Lifetime lifetime,
        SyntaxNode? node,
        string typeName,
        string exportTypeFullName)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI02",
            title: $"[{lifetime}, {exportTypeFullName}] must be keyed",
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
        string? typeFullName)
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
            $@"[{typeFullName ?? "[Unknown Type]"}]",
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

    internal static Diagnostic InterfaceRequiresFactory(AttributeSyntax node)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI06",
            title: "Container-internal interface-only resolver requires factory method",
            messageFormat: "Container-internal interface-only resolver requires factory method in order to provide as dependency",
            category: "SourceCrafter.DependencyInjection.Usage",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Please provide a value for [source] parameter"
        );

        return Diagnostic.Create(rule, node.GetLocation());
    }

    internal static Diagnostic DependencyWithUnresolvedParameters(
        SyntaxNode invExpr,
        string providerClassName)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI07",
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

    internal static Diagnostic InterfaceWithNoImplementation(
        Location attrLocation,
        ITypeSymbol interfeis,
        string providerClassName,
        Lifetime lifetime)
    {
        var interfaceName = interfeis.NameOnly;
        if(interfeis.TypeKind == TypeKind.Interface) interfaceName = interfaceName.TrimStart('I');
        DiagnosticDescriptor rule = new(
            id: "SCDI08",
            title: "Dependency has unresolved types",
            messageFormat: "Interface {0} has not specified implementation at container {2}.",
            category: "SourceCrafter.DependencyInjection.Definition",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Define or fix a [LifeTime<I{1}, {1}>] as decorator attribute over type {1} definition"
        );

        return Diagnostic.Create(
            rule,
            attrLocation,
            interfeis.GlobalNamespaced,
            providerClassName,
            interfaceName,
            lifetime);
    }

    internal static Diagnostic BaseAndImplementationMissmatch(
        Location attrLocation,
        ITypeSymbol type,
        ITypeSymbol interfeis)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI09",
            title: "Implementation does not derive from the declared service type",
            messageFormat: "Type '{0}' is not an implementation of '{1}'.",
            category: "SourceCrafter.DependencyInjection.Definition",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        return Diagnostic.Create(
            rule,
            attrLocation,
            type.GlobalNamespaced,
            interfeis.GlobalNamespaced);
    }

    //internal static Diagnostic DependencyCallMustBeScoped(string providerName, IdentifierNameSyntax methodNameSyntax)
    //{
    //    DiagnosticDescriptor rule = new(
    //        id: "SCDI09",
    //        title: "Resolver factorySource called on non-scoped instance.",
    //        messageFormat: $"Method [{methodNameSyntax.Identifier.ValueText}] must be called from scoped instance using [{providerName}.CreateScope()].",
    //        category: "SourceCrafter.DependencyInjection.Usage",
    //        defaultSeverity: DiagnosticSeverity.Error,
    //        isEnabledByDefault: true,
    //        description: "Please, just use a CreateScope reference to call the indicated factorySource"
    //    );

    //    return Diagnostic.Create(
    //        rule,
    //        methodNameSyntax.GetLocation());
    //}

    internal static Diagnostic FactoryReturnMismatch(ISymbol factorySource, ITypeSymbol type, ITypeSymbol returnType, AttributeSyntax attrSyntax)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI10",
            title: "Return type doesn't match service {4} type",
            messageFormat: "{0} {1} as return type for method {2}, should match {3} as service base {4}.",
            category: "SourceCrafter.DependencyInjection.Design",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        return Diagnostic.Create(
            rule,
            attrSyntax.GetLocation(),
            returnType.TypeKind,
            returnType.ToDisplayString(),
            factorySource.ToDisplayString(),
            type.ToDisplayString(),
            type.TypeKind is TypeKind.Interface || type.IsAbstract ? "base" : type.Kind.ToString().ToLower());
    }

    internal static Diagnostic UncoveredGenericResolver(Location location, string type, string providerFullTypeName, bool isScopedCall)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI11",
            title: "Generic service resolver support couldn't cover this call",
            messageFormat: "No dependency resolver was found for '{0}' at '{1}' container{2}.",
            category: "SourceCrafter.DependencyInjection.Design",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        return Diagnostic.Create(
            rule,
            location,
            type,
            providerFullTypeName,
            isScopedCall ? " scope" : null);
    }

    internal static Diagnostic ThrowInnerFactorySpecs(string name, Location location)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI12",
            title: "Internal factory must be private and name must have underscore leading (Eg: _Name)",
            messageFormat: "Internal factory '{0}' must be private and name must have underscore leading (Eg: _{0}).",
            category: "SourceCrafter.DependencyInjection.Design",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        return Diagnostic.Create(rule, location, name);
    }

    internal static Diagnostic AmbiguousContainerForCall(Location location, string methodName, string containerTypeFullName)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI13",
            title: "Ambiguous container for interceptable call",
            messageFormat: "More than one service container can resolve '{0}' on '{1}'. Call it on the concrete container type so the generator can pick one.",
            category: "SourceCrafter.DependencyInjection.Usage",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Two or more generated containers matched the same call location, which would emit duplicated [InterceptsLocation] attributes."
        );

        return Diagnostic.Create(rule, location, methodName, containerTypeFullName);
    }

    internal static Diagnostic InvalidAsyncTypeArgument(Location location, AsyncKind AsyncKind, string methodName)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI14",
            title: "IServiceProvider-like method must not use Task<T> or ValueTask<T> as generic argument",
            messageFormat: "IServiceProvider-like '{0}' method must not use {1}<T> as generic argument.",
            category: "SourceCrafter.DependencyInjection.Design",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        return Diagnostic.Create(rule, location, methodName, AsyncKind);
    }

    internal static Diagnostic ConflictingParameterlessConstructor(Location location, string containerClassName)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI15",
            title: "Service container declares a parameterless constructor",
            messageFormat: "'{0}' declares a parameterless constructor, which collides with the one the generator emits to initialize its cancellation source.",
            category: "SourceCrafter.DependencyInjection.Design",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Move the initialization logic to a field initializer or to a constructor taking parameters."
        );

        return Diagnostic.Create(rule, location, containerClassName);
    }
}
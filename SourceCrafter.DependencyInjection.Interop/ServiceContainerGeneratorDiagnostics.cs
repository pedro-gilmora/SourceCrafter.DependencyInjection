﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using SourceCrafter.DependencyInjection.Interop;

using System.Collections.Generic;
using System.Linq;

namespace SourceCrafter.DependencyInjection
{
    internal static class ServiceContainerGeneratorDiagnostics
    {
        internal static Diagnostic DuplicateService(Lifetime lifetime, IFieldSymbol? key, AttributeSyntax attrSyntax, string typeName, string exportTypeFullName)
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
            AttributeSyntax attrSyntax,
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

            return Diagnostic.Create(rule, attrSyntax.GetLocation(), typeName);
        }

        internal static Diagnostic UnresolvedDependency(InvocationExpressionSyntax invExpr, string providerClassName, string typeFullName, ITypeSymbol? keyType, IFieldSymbol? name)
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
                (keyType, name) switch
                {
                    ({ }, null) => $@"<{keyType.ToGlobalNamespaced()}, {typeFullName}>",
                    (_, { }) => $@"<{name.ToGlobalNamespaced()}, {typeFullName}>",
                    _ => $@"[{typeFullName}]",

                },
                providerClassName);
        }

        internal static Diagnostic CancellationTokenShouldBeProvided(ISymbol factory, AttributeSyntax attrSyntax)
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
                (factory.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() ?? attrSyntax).GetLocation(),
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

    }
}
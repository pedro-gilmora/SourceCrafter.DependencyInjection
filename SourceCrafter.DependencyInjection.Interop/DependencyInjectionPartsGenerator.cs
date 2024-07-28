using Microsoft.CodeAnalysis;

using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.IO.IsolatedStorage;
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
    }
}

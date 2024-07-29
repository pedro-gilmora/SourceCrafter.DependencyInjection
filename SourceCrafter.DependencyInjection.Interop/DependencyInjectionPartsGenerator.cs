using Microsoft.CodeAnalysis;

using System;
using System.Collections.Concurrent;
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
    
    public delegate void CommaSeparateBuilder(ref bool useIComma, StringBuilder code);
    public delegate void VarNameBuilder(StringBuilder code);
    public delegate void MemberBuilder(StringBuilder code, string generatedCodeAttribute);
    public delegate void ParamsBuilder(StringBuilder code);
    public delegate void ResolveDependencyHandler(ref ServiceDescriptor item);

    public static class DependencyInjectionPartsGenerator
    {
        static readonly object _lock = new();
        static ConcurrentDictionary<Guid, ContainerRegistrationHandler>? OnResolveDependency;

        static DependencyInjectionPartsGenerator()
        {
            InitializeBag();
        }

        private static void InitializeBag()
        {
            if (OnResolveDependency is not null) return;

            lock (_lock)
            {
                if (OnResolveDependency is not null) return;

                OnResolveDependency = [];
            }
        }

        public static void RegisterDependencyResolvers(Guid key, ContainerRegistrationHandler handler)
        {
            if(OnResolveDependency is  null) return;

            OnResolveDependency.TryAdd(key, handler);
        }

        public static void UnregisterDependencyResolvers(Guid key)
        {
            if (OnResolveDependency is null) return;

            OnResolveDependency.TryRemove(key, out _);
        }

        internal static List<(string, string)> GetResolvedDependencies(StringBuilder code, Compilation compilation, ITypeSymbol serviceContainer, Set<ServiceDescriptor> servicesDescriptors)
        {
            List<(string, string)> list = [];

            if(OnResolveDependency is null) return list;

            foreach (var item in OnResolveDependency.Values)
            {
                if (item(compilation, serviceContainer, servicesDescriptors) is { } itemToAdd) list.Add(itemToAdd);
            }

            return list;
        }
    }
}

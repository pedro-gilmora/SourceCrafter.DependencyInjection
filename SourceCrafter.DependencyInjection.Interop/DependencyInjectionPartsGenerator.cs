using Microsoft.CodeAnalysis;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

[assembly: InternalsVisibleTo("SourceCrafter.DependencyInjection")]


namespace SourceCrafter.DependencyInjection.Interop
{
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Method)]
    public class DependencyResolverAttribute<IDependencyResolver> : Attribute;

    public delegate void ContainerRegistrationHandler(Compilation compilation, ITypeSymbol serviceContainer, DependencyMap servicesDescriptors);

    public delegate void CommaSeparateBuilder(ref bool useIComma, StringBuilder code);
    public delegate void ValueBuilder(StringBuilder code);
    public delegate void MemberBuilder(StringBuilder code, string generatedCodeAttribute);
    public delegate void ParamsBuilder(StringBuilder code);

    public static class DependencyInjectionPartsGenerator
    {
        static readonly object _lock = new();
        static ConcurrentDictionary<Guid, ContainerRegistrationHandler>? ExternalResolvers;

        static DependencyInjectionPartsGenerator()
        {
            InitializeBag();
        }

        private static void InitializeBag()
        {
            if (ExternalResolvers is not null) return;

            lock (_lock)
            {
                if (ExternalResolvers is not null) return;

                ExternalResolvers = [];
            }
        }

        public static void RegisterDependencyResolvers(Guid key, ContainerRegistrationHandler handler)
        {
            if (ExternalResolvers is null) return;

            ExternalResolvers.AddOrUpdate(key, handler, (_, _) => handler);
        }

        public static void UnregisterDependencyResolvers(Guid key)
        {
            if (ExternalResolvers is null) return;

            ExternalResolvers.TryRemove(key, out _);
        }

        internal static List<string> ResolveExternalDependencies(SynchronizationContext context, Compilation compilation, ITypeSymbol serviceContainer, DependencyMap servicesDescriptors)
        {
            List<string> list = [];

            if (ExternalResolvers is null) return list;

            foreach (var item in ExternalResolvers.Values)
            {
                lock (_lock)
                {
                    context.Send(_ => item(compilation, serviceContainer, servicesDescriptors), null);
                }
            }

            return list;
        }
    }
}

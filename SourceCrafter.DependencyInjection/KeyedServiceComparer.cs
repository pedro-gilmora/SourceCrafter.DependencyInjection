using System.Collections.Generic;

using SourceCrafter.DependencyInjection.Interop;

namespace SourceCrafter.DependencyInjection;

internal readonly struct KeyedServiceComparer : IEqualityComparer<ServiceDescriptor>
{
    readonly (bool isAsync, string serviceName, string enumType) GetKey(ServiceDescriptor key)
    {
        return (key.IsAsync, key.FullTypeName, key.Key!);
    }
    public readonly bool Equals(ServiceDescriptor x, ServiceDescriptor y)
    {
        return GetKey(x) == GetKey(y);
    }

    public readonly int GetHashCode(ServiceDescriptor obj)
    {
        return GetKey(obj).GetHashCode();
    }
}
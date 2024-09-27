
global using DependencyMap = SourceCrafter.DependencyInjection.Map<(SourceCrafter.DependencyInjection.Interop.Lifetime, string, string?), SourceCrafter.DependencyInjection.Interop.ServiceDescriptor>;

using Microsoft.CodeAnalysis;

using System.Collections.Generic;

namespace SourceCrafter.DependencyInjection.Interop;

public class DependencyComparer : IEqualityComparer<(Lifetime, string, string?)>
{
    public bool Equals((Lifetime, string, string?) x, (Lifetime, string, string?) y)
    {
        return x.Item1 == y.Item1
            && x.Item2 == y.Item2
            && x.Item3 == y.Item3;
    }

    public int GetHashCode((Lifetime, string, string?) obj)
    {
        return obj.GetHashCode();
    }
}
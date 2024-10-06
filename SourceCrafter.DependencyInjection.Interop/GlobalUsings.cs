global using DependencyMap = SourceCrafter.DependencyInjection.Interop.Map<(SourceCrafter.DependencyInjection.Interop.Lifetime, string, string), SourceCrafter.DependencyInjection.Interop.ServiceDescriptor>;
global using DependencyNamesMap = SourceCrafter.DependencyInjection.Interop.Map<(SourceCrafter.DependencyInjection.Interop.Lifetime, int, string), string>;

using System;
using System.Collections.Generic;

namespace SourceCrafter.DependencyInjection.Interop;

public class DependencyComparer<T> : IEqualityComparer<(Lifetime, T, string)> where T : IEquatable<T>
{
    public bool Equals((Lifetime, T, string) x, (Lifetime, T, string) y)
    {
        return x.Item1.Equals(y.Item1)
            && x.Item2.Equals(y.Item2)
            && x.Item3.Equals(y.Item3);
    }

    public int GetHashCode((Lifetime, T, string) obj)
    {
        return obj.GetHashCode();
    }
}
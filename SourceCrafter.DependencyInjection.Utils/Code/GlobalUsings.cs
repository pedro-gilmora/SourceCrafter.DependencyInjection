global using DependencyMap = SourceCrafter.DependencyInjection.Map<(SourceCrafter.DependencyInjection.Lifetime, string, string), SourceCrafter.DependencyInjection.ServiceDescriptor>;
global using DependencyNamesMap = SourceCrafter.DependencyInjection.Map<(SourceCrafter.DependencyInjection.Lifetime, int, string), string>;
global using DependencyMapDictionary = System.Collections.Generic.Dictionary<string, SourceCrafter.DependencyInjection.Map<(SourceCrafter.DependencyInjection.Lifetime, string, string), SourceCrafter.DependencyInjection.ServiceDescriptor>>;

using System;
using System.Collections.Generic;

namespace SourceCrafter.DependencyInjection;

internal sealed class DependencyComparer<T> : IEqualityComparer<(Lifetime, T, string)> where T : IEquatable<T>
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
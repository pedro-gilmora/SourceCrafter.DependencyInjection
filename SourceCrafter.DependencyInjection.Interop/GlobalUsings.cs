
global using DependencyMap = SourceCrafter.DependencyInjection.Map<(SourceCrafter.DependencyInjection.Interop.Lifetime, string, Microsoft.CodeAnalysis.IFieldSymbol?), SourceCrafter.DependencyInjection.Interop.ServiceDescriptor>;

using Microsoft.CodeAnalysis;

using System.Collections.Generic;

namespace SourceCrafter.DependencyInjection.Interop;

public class DependencyComparer : IEqualityComparer<(Lifetime, string, IFieldSymbol?)>
{
    public bool Equals((Lifetime, string, IFieldSymbol?) x, (Lifetime, string, IFieldSymbol?) y)
    {
        return x.Item1 == y.Item1
            && x.Item2 == y.Item2
            && SymbolEqualityComparer.Default.Equals(x.Item3, y.Item3);
    }

    public int GetHashCode((Lifetime, string, IFieldSymbol?) obj)
    {
        return obj.GetHashCode();
    }
}
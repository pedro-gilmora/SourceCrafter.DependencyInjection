
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Text;
using SourceCrafter.DependencyInjection;
using System.Collections;
using System.Diagnostics.CodeAnalysis;

internal class DiagnosticLocationComparer : IEqualityComparer<Diagnostic>
{
#pragma warning disable CS8767 // La nulabilidad de los tipos de referencia del tipo de parámetro no coincide con el miembro implementado de forma implícita (posiblemente debido a los atributos de nulabilidad).
    public bool Equals(Diagnostic x, Diagnostic y)
#pragma warning restore CS8767 // La nulabilidad de los tipos de referencia del tipo de parámetro no coincide con el miembro implementado de forma implícita (posiblemente debido a los atributos de nulabilidad).
    {
        return GetHashCode(x) == GetHashCode(y);
    }

    public int GetHashCode(Diagnostic obj)
    {
        return (obj.Id, obj.Location.GetHashCode()).GetHashCode();
    }
}

internal sealed class SubDependencyDictionary()
    : Dictionary<DependencyKey, ResolverBuilder>(EqualityComparer<DependencyKey>.Default);
internal sealed class DependencyDictionary()
    : Dictionary<FirstLevelDependencyKey, SubDependencyDictionary>(EqualityComparer<FirstLevelDependencyKey>.Default);


internal record InvokeInfo(
    bool IsOwnedByContainerType,
    string ContainerTypeNameOnly,
    string ContainerTypeFullName,
    string MethodName,
    bool IsScopedCall,
    AsyncType AsyncType,
    string ReturnType,
    InterceptableLocation Interceptor,
    string Key,
    bool NotFromScopedInstance,
    bool IsKeyed,
    bool IsMultiple)
{
    internal bool Acknowledged = false;
    internal required Location Location;
    internal bool InvalidAsyncTypeArg;
}

class AsyncLocalResolver(DependencyKey dep)
{
    public int ParamIndex = -1, ResolvedByParamIndex;
    public bool IsValueTask, ResolvedBefore;
    public DependencyKey DepKey = dep, ResolverDep;
    internal Action<StringBuilder, string?> AppendAsyncLocal = null!;

    public override bool Equals(object? obj)
    {
        return ((AsyncLocalResolver)obj!).GetHashCode() == GetHashCode();
    }
    public override int GetHashCode()
    {
        return DepKey.GetHashCode();
    }
}

class GenericResolverBuilderComparer : IEqualityComparer<ResolverBuilder>
{
#pragma warning disable CS8767 // La nulabilidad de los tipos de referencia del tipo de parámetro no coincide con el miembro implementado de forma implícita (posiblemente debido a los atributos de nulabilidad).
    public bool Equals([DisallowNull] ResolverBuilder x, [DisallowNull] ResolverBuilder y)
#pragma warning restore CS8767 // La nulabilidad de los tipos de referencia del tipo de parámetro no coincide con el miembro implementado de forma implícita (posiblemente debido a los atributos de nulabilidad).
    {
        return (x.Key.key != "", x.AsyncType, x.PassCancelToken) == (y.Key.key != "", y.AsyncType, y.PassCancelToken);
    }

    public int GetHashCode([DisallowNull] ResolverBuilder obj)
    {
        return HashCode.Combine(obj.Key.key != "", obj.AsyncType, obj.PassCancelToken);
    }
}

internal class ResolverBuilder(string toStr)
{
    private static int _id = 0;
    internal int Id = _id++;
    internal DependencyKey Key;
    internal AsyncType AsyncType;
    internal HashSet<(int, bool)> AsyncNestedDeps = [];
    internal Dictionary<DependencyKey, AsyncLocalResolver> AsyncLocalResolvers = new(EqualityComparer<DependencyKey>.Default);
    internal AppendValue AppendValue = null!;
    internal string ExportTypeFullName = null!;
    internal int ParamsLength;
    internal int ImplTypeKey;
    internal bool PassCancelToken;

    internal void GenericMemberSignature(StringBuilder code)
    {
        code.Append(@"
    public ");

        switch (AsyncType)
        {
            case AsyncType.None:
                code.Append("TOut");
                break;
            case AsyncType.ValueTask:
                code.Append("global::System.Threading.Tasks.ValueTask<TOut>");
                break;
            case AsyncType.Task:
                code.Append("global::System.Threading.Tasks.Task<TOut>");
                break;
        }

        code.Append(" GetRequired");

        bool hasKey = Key.key != "";

        if (hasKey) code.Append("Keyed");

        if(AsyncType == AsyncType.ValueTask) code.Append("Value");

        code.Append("Service").Append(AsyncType > 0 ? "Async<TOut>(" : "<TOut>(");

        if (hasKey) code.Append("string key");

        if (AsyncType > 0 && PassCancelToken)
        {
            if (hasKey) code.Append(", ");
            code.Append("global::System.Threading.CancellationToken token = default");
        }

        code.Append(@") where TOut : notnull => throw new global::System.NotImplementedException();
");
    }

    public override string ToString() => toStr;
}
internal class Interceptor(FirstLevelDependencyKey key, string methodName, bool multiple, bool passCancelToken, AsyncType asyncType, string exportTypeFullName, bool isKeyed, Action<StringBuilder> firstDependency)
{
    internal FirstLevelDependencyKey Key = key;
    internal AsyncType AsyncType = asyncType;
    internal HashSet<InterceptableLocation> Locations = [];
    internal readonly string Method = methodName;
    internal List<Action<StringBuilder>> AppendInterceptorValue = [firstDependency];

    public override bool Equals(object? obj)
    {
        return (obj as Interceptor)?.Key.Equals(Key) ?? false;
    }
    public override int GetHashCode()
    {
        return Key.GetHashCode();
    }

    internal void Append(StringBuilder code, string providerTypeName, ref int i)
    {
        if (Locations.Count == 0) return;

        foreach (var item in Locations)
        {
            code.Append(@"
    [global::System.Runtime.CompilerServices.InterceptsLocation(").Append(item.Version).Append(@", """).Append(item.Data).Append('"').Append(@")] //").Append(item.GetDisplayLocation());
        }

        AppendInterceptor(code, providerTypeName, ref i);
    }

    void AppendInterceptor(StringBuilder code, string providerTypeName, ref int i)
    {
        code.Append(@"
    public static ");

        //if (multiple)

        switch (AsyncType)
        {
            case AsyncType.None:
                AppendReturnType();
                break;
            case AsyncType.ValueTask:
                code.Append("global::System.Threading.Tasks.ValueTask<");
                AppendReturnType();
                code.Append('>');
                break;
            case AsyncType.Task:
                code.Append("global::System.Threading.Tasks.Task<");
                AppendReturnType();
                code.Append('>');
                break;
        }

        code.Append(" InterceptorCall").Append(++i).Append(@"(this ").Append(providerTypeName);

        //if (isScopedCall) code.Append(".Scoped");

        code.Append(" provider");

        if (isKeyed) code.Append(", string _");

        if (AsyncType > 0 && passCancelToken) code.Append(", global::System.Threading.CancellationToken cancellationToken");

        code.Append(@") => ");

        if (multiple)
        {
            code.Append(@"[
            ");

            string? comma = null;

            foreach (var appendValue in AppendInterceptorValue)
            {
                if (comma is null) code.Append(comma = @",
        ");
                appendValue(code);
            }

            code.Append(']');
        }
        else
        {
            AppendInterceptorValue[^1](code);
        }


        code.Append(@";
");
        void AppendReturnType() => _ = multiple

            ? code.Append("global::System.Collections.Generic.IEnumerable<").Append(exportTypeFullName).Append('>')
            : code.Append(exportTypeFullName);
    }
}


static class Helpers
{
    internal static AsyncType TryGetAsyncType(this ITypeSymbol typeSymbol, out ITypeSymbol factoryType)
    {
        switch (typeSymbol.FullGlobalQualifiedNonGenericName)
        {
            case "global::System.Threading.Tasks.ValueTask" or "global::System.Threading.Tasks.Task"
                when typeSymbol is INamedTypeSymbol { TypeArguments: [{ } firstTypeArg] }:

                factoryType = firstTypeArg;
                return typeSymbol.Name is "ValueTask" ? AsyncType.ValueTask : AsyncType.Task;

            default:
                // TODO: if there's a case of inheriting from task, a recursive approach should be taken here 
                factoryType = typeSymbol;
                return AsyncType.None;
        }
    }

    internal static Disposability GetDisposability(this ITypeSymbol type)
    {
        if (type is null) return Disposability.None;

        Disposability disposability = Disposability.None;

        foreach (var iFace in type.AllInterfaces)
        {
            switch (iFace.FullGlobalQualifiedNonGenericName)
            {
                case "global::System.IDisposable" when disposability is Disposability.None:
                    disposability = Disposability.Disposable;
                    break;
                case "global::System.IAsyncDisposable" when disposability < Disposability.AsyncDisposable:
                    return Disposability.AsyncDisposable;
            }
        }

        return disposability;
    }
}

sealed record MemberBuilder(Lifetime Lifetime, string TypeFullName, string Key, AsyncType AsyncType, Disposability Disposability, string? NameOrFormat)
{
    internal required bool RequiresCancelToken;


    internal Action<StringBuilder, List<Action>, List<DisposeBuilder>, List<DisposeBuilder>> BuildAndExpose = null!;
    public bool Equals(MemberBuilder? other)
    {
        return this == other
            || (this is not null
                && other is not null
                && Lifetime == other.Lifetime
                && TypeFullName == other.TypeFullName
                && Key == other.Key
                && AsyncType == other.AsyncType
                && Disposability == other.Disposability
                && NameOrFormat == other.NameOrFormat);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Lifetime, TypeFullName, Key, AsyncType, Disposability, NameOrFormat);
    }
}

record ParamBuildOptions(DependencyKey Key, AppendValue Append, bool StartsCollectionExpression = false, bool EndsCollectionExpression = false);

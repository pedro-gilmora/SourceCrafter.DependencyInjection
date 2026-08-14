
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
    AsyncKind AsyncKind,
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
        return (x.Key.key != "", x.AsyncKind, x.PassCancelToken) == (y.Key.key != "", y.AsyncKind, y.PassCancelToken);
    }

    public int GetHashCode([DisallowNull] ResolverBuilder obj)
    {
        return HashCode.Combine(obj.Key.key != "", obj.AsyncKind, obj.PassCancelToken);
    }
}

internal class ResolverBuilder(string toStr)
{
    private static int _id = 0;
    internal int Id = _id++;
    internal DependencyKey Key;
    internal AsyncKind AsyncKind;
    internal HashSet<(int, bool)> AsyncNestedDeps = [];
    internal Dictionary<DependencyKey, AsyncLocalResolver> AsyncLocalResolvers = new(EqualityComparer<DependencyKey>.Default);
    internal AppendValue AppendValue = null!;
    internal string ExportTypeFullName = null!;
    internal int ParamsLength;
    internal int ImplTypeKey;
    internal bool PassCancelToken;
    internal bool TransientWithoutCachedDeps;

    internal void GenericMemberSignature(StringBuilder code)
    {
        AppendMethod();
        AppendMethod(true);

        void AppendMethod(bool isMultiple = false)
        {
            code.Append(@"
    public ");

            switch (AsyncKind)
            {
                case AsyncKind.None:
                    code.Append("TOut");
                    if (isMultiple) code.Append("[]");
                    break;
                case AsyncKind.ValueTask:
                    code.Append("global::System.Threading.Tasks.ValueTask<TOut");
                    if (isMultiple) code.Append("[]"); 
                    code.Append('>');
                    break;
                case AsyncKind.Task:
                    code.Append("global::System.Threading.Tasks.Task<TOut");
                    if (isMultiple) code.Append("[]");
                    code.Append('>');
                    break;
            }

            code.Append(" GetRequired");

            bool hasKey = Key.key != "";

            if (hasKey) code.Append("Keyed");

            if (AsyncKind == AsyncKind.ValueTask) code.Append("Value");

            code.Append("Service");

            if (isMultiple) code.Append('s');

            code.Append(AsyncKind > 0 ? "Async<TOut>(" : "<TOut>(");

            if (hasKey) code.Append("string key");

            if (AsyncKind > 0 && PassCancelToken)
            {
                if (hasKey) code.Append(", ");
                code.Append("global::System.Threading.CancellationToken token = default");
            }

            code.Append(@") where TOut : notnull => throw new global::System.NotImplementedException();
");
        }
    }

    public override string ToString() => toStr;
}
internal class Interceptor(FirstLevelDependencyKey key, string methodName, bool multiple, bool passCancelToken, AsyncKind asyncKind, string exportTypeFullName, bool isKeyed, (bool, Action<StringBuilder, bool>) firstDependency)
{
    internal FirstLevelDependencyKey Key = key;
    internal bool IsKeyed = isKeyed;
    internal AsyncKind AsyncKind = asyncKind;
    internal HashSet<InterceptableLocation> Locations = [];
    internal readonly string Method = methodName;
    internal List<(bool, Action<StringBuilder, bool>)> AppendInterceptorValue = [firstDependency];

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

        var useAsync = multiple && AsyncKind > 0;

        if (useAsync) code.Append("async ");

        switch (AsyncKind)
        {
            case AsyncKind.None:
                code.Append(exportTypeFullName);
                if (multiple) code.Append("[]");
                break;
            case AsyncKind.ValueTask:
                code.Append("global::System.Threading.Tasks.ValueTask<").Append(exportTypeFullName);
                if(multiple) code.Append("[]");
                code.Append('>');
                break;
            case AsyncKind.Task:
                code.Append("global::System.Threading.Tasks.Task<").Append(exportTypeFullName);
                if(multiple) code.Append("[]");
                code.Append('>');
                break;
        }

        code.Append(" InterceptorCall").Append(++i).Append(@"(this ").Append(providerTypeName);

        //if (isScopedCall) code.Append(".Scoped");

        code.Append(" provider");

        if (IsKeyed) code.Append(", string _");

        if (AsyncKind > 0 && passCancelToken) code.Append(", global::System.Threading.CancellationToken cancellationToken");

        code.Append(@") => ");

        if (multiple)
        {
            code.Append(@"[
        ");

            string? comma = null;

            foreach (var (isAsync, appendValue) in AppendInterceptorValue)
            {
                if (comma is null) comma = @",
        ";
                else code.Append(comma);
                if (useAsync && isAsync) code.Append("await ");
                appendValue(code, useAsync);
            }

            code.Append(']');
        }
        else
        {
            AppendInterceptorValue[^1].Item2(code, useAsync);
        }


        code.Append(@";
");
    }
}


static class Helpers
{
    internal static AsyncKind TryGetAsyncType(this ITypeSymbol typeSymbol, out ITypeSymbol factoryType)
    {
        switch (typeSymbol.FullGlobalQualifiedNonGenericName)
        {
            case "global::System.Threading.Tasks.ValueTask" or "global::System.Threading.Tasks.Task"
                when typeSymbol is INamedTypeSymbol { TypeArguments: [{ } firstTypeArg] }:

                factoryType = firstTypeArg;
                return typeSymbol.Name is "ValueTask" ? AsyncKind.ValueTask : AsyncKind.Task;

            default:
                // TODO: if there's a case of inheriting from task, a recursive approach should be taken here 
                factoryType = typeSymbol;
                return AsyncKind.None;
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

sealed record MemberBuilder(Lifetime Lifetime, string TypeFullName, string Key, AsyncKind AsyncKind, Disposability Disposability, string? NameOrFormat)
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
                && AsyncKind == other.AsyncKind
                && Disposability == other.Disposability
                && NameOrFormat == other.NameOrFormat);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Lifetime, TypeFullName, Key, AsyncKind, Disposability, NameOrFormat);
    }
}

record ParamBuildOptions(DependencyKey Key, AppendValue Append, bool StartsCollectionExpression = false, bool EndsCollectionExpression = false);

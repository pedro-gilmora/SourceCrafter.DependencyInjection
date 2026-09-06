
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Text;
using SourceCrafter.DependencyInjection;
using System.Collections;
using System.Diagnostics.CodeAnalysis;

internal class DiagnosticLocationComparer : IEqualityComparer<Diagnostic>
{
    public bool Equals(Diagnostic? x, Diagnostic? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;

        return x.Id == y.Id && x.Location.Equals(y.Location);
    }

    public int GetHashCode(Diagnostic obj)
    {
        return HashCode.Combine(obj.Id, obj.Location);
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
        return obj is AsyncLocalResolver other && DepKey.Equals(other.DepKey);
    }
    public override int GetHashCode()
    {
        return DepKey.GetHashCode();
    }
}

/// <summary>
/// Agrupa los resolvedores que producen la *misma* firma generica de compatibilidad.
/// Solo cuentan "tiene clave" y el tipo de asincronia: el CancellationToken ya no
/// aparece en la firma, asi que incluirlo aqui generaria dos miembros identicos.
/// </summary>
class GenericResolverBuilderComparer : IEqualityComparer<ResolverBuilder>
{
    public bool Equals(ResolverBuilder? x, ResolverBuilder? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;

        return (x.Key.key != "", x.AsyncKind) == (y.Key.key != "", y.AsyncKind);
    }

    public int GetHashCode([DisallowNull] ResolverBuilder obj)
    {
        return HashCode.Combine(obj.Key.key != "", obj.AsyncKind);
    }
}

internal class ResolverBuilder(string toStr)
{
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

    /// <summary>
    /// Emite los miembros genericos de compatibilidad con <c>IServiceProvider</c>.
    ///
    /// <para>Ninguna sobrecarga acepta un <c>CancellationToken</c>: el contenedor resuelve
    /// con su propio token de vida (<c>__lifetimeToken</c>), asi que aceptar uno del
    /// llamador solo prometeria una cancelacion que nunca se honra. Ademas, un valor
    /// cacheado se entrega a todos los llamadores, por lo que grabar en el el token del
    /// primero seria incorrecto.</para>
    /// </summary>
    internal void GenericMemberSignature(StringBuilder code)
    {
        AppendSignature(code, AsyncKind, Key.key != "", false);
        AppendSignature(code, AsyncKind, Key.key != "", true);
    }

    /// <summary>
    /// Emite una firma de la API generica de compatibilidad.
    /// </summary>
    internal static void AppendSignature(StringBuilder code, AsyncKind asyncKind, bool hasKey, bool isMultiple)
    {
        code.Append(@"
    public ");

        switch (asyncKind)
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

        if (hasKey) code.Append("Keyed");

        if (asyncKind == AsyncKind.ValueTask) code.Append("Value");

        code.Append("Service");

        if (isMultiple) code.Append('s');

        code.Append(asyncKind > 0 ? "Async<TOut>(" : "<TOut>(");

        if (hasKey) code.Append("string key");

        code.Append(@") where TOut : notnull => throw new global::System.NotImplementedException();
");
    }

    public override string ToString() => toStr;
}
/// <summary>
/// Un elemento del array que devuelve un interceptor multiple.
/// </summary>
/// <param name="IsAsync">Si el valor se produce como tarea.</param>
/// <param name="Append">Emite el valor del elemento.</param>
/// <param name="Key">Identidad del resolvedor que produce el elemento.</param>
/// <param name="ResolvedDeps">
/// Dependencias asincronas que este elemento resuelve por el camino. Si otro elemento del
/// mismo array esta aqui dentro <b>y</b> es cacheado, esperar a este ya lo deja completo.
/// </param>
internal readonly record struct InterceptorElement(
    bool IsAsync,
    Action<StringBuilder, bool> Append,
    DependencyKey Key,
    IReadOnlyCollection<DependencyKey> ResolvedDeps);

internal class Interceptor(FirstLevelDependencyKey key, string methodName, bool multiple, AsyncKind asyncKind, string exportTypeFullName, bool isKeyed, InterceptableLocation builtFrom, InterceptorElement firstDependency)
{
    internal FirstLevelDependencyKey Key = key;
    internal bool IsKeyed = isKeyed;
    internal AsyncKind AsyncKind = asyncKind;
    internal HashSet<InterceptableLocation> Locations = [];
    internal readonly string Method = methodName;
    internal List<InterceptorElement> AppendInterceptorValue = [firstDependency];

    /// <summary>
    /// Sitio de llamada que construyo la lista de valores.
    ///
    /// <para>Un interceptor multiple se arma recorriendo los resolvedores de *un* sitio de
    /// llamada. Si otro sitio distinto resuelve la misma clave, comparte el mismo metodo
    /// interceptor y solo debe aportar su localizacion: volver a acumular sus resolvedores
    /// duplicaria los elementos del array. Comparar contra esta localizacion distingue
    /// "otro resolvedor del mismo sitio" de "otro sitio".</para>
    /// </summary>
    internal readonly InterceptableLocation BuiltFrom = builtFrom;

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

        if (useAsync)
        {
            // Antes se emitia [await A, await B]: cada elemento esperaba al anterior.
            // Se materializan primero todas las tareas para que avancen en paralelo y
            // luego se esperan una a una.
            code.Append(@")
    {");

            var index = 0;

            foreach (var element in AppendInterceptorValue)
            {
                if (element.IsAsync)
                {
                    code.Append(@"
        var __t").Append(index).Append(" = ");

                    element.Append(code, true);

                    code.Append(';');
                }

                index++;
            }

            // Un elemento cacheado que otro elemento ya resuelve por el camino comparte con
            // el la *misma* tarea, asi que esperar al segundo lo deja completo: leer su
            // Result evita un await por elemento. No vale para transitorios, que fabrican
            // una tarea distinta en cada llamada.
            var resolvedBy = ResolveCoverage();

            var awaited = new Dictionary<int, int>();

            foreach (var coveringIndex in resolvedBy.Values.Distinct().OrderBy(i => i))
            {
                code.Append(@"
        var __r").Append(awaited.Count).Append(" = await __t").Append(coveringIndex).Append(';');

                awaited.Add(coveringIndex, awaited.Count);
            }

            code.Append(@"

        return [");

            index = 0;

            foreach (var element in AppendInterceptorValue)
            {
                if (index > 0) code.Append(',');

                code.Append(@"
            ");

                if (!element.IsAsync) element.Append(code, true);
                else if (resolvedBy.TryGetValue(index, out var coveringIndex))
                    code.Append("__t").Append(index)
                        .Append(".Result /* resolved previously by __t").Append(coveringIndex).Append(" */");
                else if (awaited.TryGetValue(index, out var local)) code.Append("__r").Append(local);
                else code.Append("await __t").Append(index);

                index++;
            }

            code.Append(@"];
    }
");

            return;
        }

        /// <summary>
        /// Empareja cada elemento cacheado con un elemento del mismo array que lo resuelve
        /// por el camino, para que baste con esperar a este ultimo.
        /// </summary>
        Dictionary<int, int> ResolveCoverage()
        {
            Dictionary<int, int> resolvedBy = [];

            var elements = AppendInterceptorValue;

            bool Covers(int covering, int covered) =>
                covering != covered
                && elements[covering].IsAsync
                && elements[covered].IsAsync
                // Un transitorio fabrica una tarea nueva en cada llamada, asi que la que
                // resolvio el otro elemento no es la que este array tiene en su local.
                && elements[covered].Key.lifetime is not Lifetime.Transient
                && elements[covering].ResolvedDeps.Contains(elements[covered].Key);

            bool IsCovered(int covered)
            {
                for (var i = 0; i < elements.Count; i++)
                    if (Covers(i, covered)) return true;

                return false;
            }

            for (var covered = 0; covered < elements.Count; covered++)
                for (var covering = 0; covering < elements.Count; covering++)
                    // El cubridor tiene que ser una raiz: si el mismo estuviese cubierto no
                    // se emitiria su await y nadie garantizaria la tarea de este elemento.
                    if (Covers(covering, covered) && !IsCovered(covering))
                    {
                        resolvedBy[covered] = covering;
                        break;
                    }

            return resolvedBy;
        }
        code.Append(@") => ");

        if (multiple)
        {
            code.Append(@"[
        ");

            string? comma = null;

            foreach (var element in AppendInterceptorValue)
            {
                if (comma is null) comma = @",
        ";
                else code.Append(comma);

                element.Append(code, false);
            }

            code.Append(']');
        }
        else
        {
            AppendInterceptorValue[^1].Append(code, false);
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

    /// <summary>
    /// Renderiza el miembro en <paramref name="code"/> y registra en el contexto lo que
    /// el contenedor debe emitir por el: candados a inicializar, liberadores y si
    /// consume el token de vida.
    /// </summary>
    internal Action<StringBuilder, ContainerRenderContext> BuildAndExpose = null!;

    public bool Equals(MemberBuilder? other)
    {
        // No usar 'this == other': el operador == sintetizado por el compilador para
        // un record llama de vuelta a Equals, produciendo recursion infinita y un
        // StackOverflowException que mata el proceso host (Visual Studio incluido).
        return ReferenceEquals(this, other)
            || (other is not null
                && Lifetime == other.Lifetime
                && TypeFullName == other.TypeFullName
                && Key == other.Key
                && AsyncKind == other.AsyncKind
                && Disposability == other.Disposability
                && RequiresCancelToken == other.RequiresCancelToken
                && NameOrFormat == other.NameOrFormat);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Lifetime, TypeFullName, Key, AsyncKind, Disposability, RequiresCancelToken, NameOrFormat);
    }
}

/// <summary>
/// Un parametro del constructor o de la fabrica.
/// <para><paramref name="MustHoist"/>: el parametro adquiere algun candado al resolverse
/// (es cacheado, o es un transient cuyo subarbol contiene cacheados). Esos deben resolverse
/// <b>antes</b> de tomar el candado propio, o dos candados tomados en ordenes opuestos
/// podrian interbloquearse.</para>
/// </summary>
record ParamBuildOptions(DependencyKey Key, AppendValue Append, bool StartsCollectionExpression = false, bool EndsCollectionExpression = false, bool MustHoist = false);

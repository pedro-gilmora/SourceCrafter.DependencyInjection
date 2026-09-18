
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
    /// Nombre del miembro del contenedor que resuelve este servicio, o <c>null</c> si el
    /// resolver no llego a exponerse (un transient inlineado sin <c>exportTransients</c>).
    /// Sin miembro no hay nada a lo que despachar, asi que esos quedan fuera del
    /// <c>switch</c> de la API generica.
    /// </summary>
    internal string? MemberName;

    /// <summary>
    /// Discriminador de este servicio en el <c>switch</c> de la API generica: el
    /// <c>typeof(T).FullName</c> del tipo expuesto. Es <c>null</c> para los genericos
    /// construidos, que se comparan por <c>typeof</c> en vez de por cadena.
    /// </summary>
    internal string? RuntimeTypeName;

    /// <summary>
    /// Cierto si el miembro se emite como metodo y por tanto hay que invocarlo.
    /// </summary>
    internal bool MemberIsMethodShaped;

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
/// <param name="IsValueTask">
/// Cierto si el local <c>__tN</c> sera un <c>ValueTask&lt;T&gt;</c>. Importa porque
/// <c>ValueTask</c> no expone <c>Exception</c> y <c>AsTask()</c> sobre una ya consumida
/// lanza, asi que su excepcion no se puede observar sin consumirla.
/// </param>
internal readonly record struct InterceptorElement(
    bool IsAsync,
    Action<StringBuilder, bool> Append,
    DependencyKey Key,
    IReadOnlyCollection<DependencyKey> ResolvedDeps,
    bool IsValueTask);

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

    /// <summary>
    /// Numero que distingue a este interceptor de los demas. Se asigna antes de emitir el
    /// contenedor porque el campo de cache de los elementos scoped se declara ahi dentro,
    /// mientras que el metodo interceptor se emite despues, en la clase de extensiones.
    /// </summary>
    internal int Index;

    /// <summary>
    /// Lifetime del array que devuelve el interceptor, o <c>null</c> si no se puede cachear.
    ///
    /// <para>Es el <b>minimo</b> de los lifetimes de sus elementos. Basta un elemento
    /// transitorio para que no haya cache posible: un transitorio promete una instancia nueva
    /// por llamada, asi que guardar el array convertiria ese elemento en un singleton de
    /// hecho. No es una cuestion de rendimiento sino de semantica.</para>
    ///
    /// <para>Si todos son singleton el array es el mismo para todo el proceso y el campo es
    /// estatico. Si hay alguno scoped el array solo vale dentro de su ambito, asi que el
    /// campo es de instancia y cada <c>CreateScope()</c> estrena el suyo.</para>
    /// </summary>
    internal Lifetime? CacheLifetime
    {
        get
        {
            // Sin array no hay nada que ahorrar: el valor unico ya lo cachea su miembro.
            if (!multiple) return null;

            var result = Lifetime.Singleton;

            foreach (var element in AppendInterceptorValue)
            {
                if (element.Key.lifetime is Lifetime.Transient) return null;

                if (element.Key.lifetime is Lifetime.Scoped) result = Lifetime.Scoped;
            }

            return result;
        }
    }

    internal string CacheFieldName => "__interceptorCache" + Index;

    /// <summary>
    /// Expresion con la que se lee y escribe la cache. Los elementos scoped viven en la
    /// instancia que recibe el interceptor; los singleton, en un estatico de la clase de
    /// extensiones.
    /// </summary>
    string CacheAccess => CacheLifetime is Lifetime.Scoped ? "provider." + CacheFieldName : CacheFieldName;

    /// <summary>
    /// Reserva el numero de este interceptor. Se salta los que no tienen sitio de llamada
    /// para que la numeracion sea la misma que la de la emision.
    /// </summary>
    internal void AssignIndex(ref int i)
    {
        if (Locations.Count == 0) return;

        Index = ++i;
    }

    /// <summary>
    /// Declara dentro del contenedor el campo de cache de un interceptor cuyo array depende
    /// del ambito. Los singleton no pasan por aqui: su campo se declara en la clase de
    /// extensiones junto al metodo.
    /// </summary>
    internal void AppendScopedCacheField(StringBuilder code)
    {
        if (Locations.Count == 0 || CacheLifetime is not Lifetime.Scoped) return;

        // 'internal' y no 'private': quien lo lee es el metodo interceptor, que vive en la
        // clase de extensiones del mismo ensamblado.
        code.Append(@"

    internal ").Append(exportTypeFullName).Append("[]? ").Append(CacheFieldName).Append(@";
");
    }

    public override bool Equals(object? obj)
    {
        return (obj as Interceptor)?.Key.Equals(Key) ?? false;
    }
    public override int GetHashCode()
    {
        return Key.GetHashCode();
    }

    internal void Append(StringBuilder code, string providerTypeName)
    {
        if (Locations.Count == 0) return;

        if (CacheLifetime is Lifetime.Singleton)
        {
            code.Append(@"
    private static ").Append(exportTypeFullName).Append("[]? ").Append(CacheFieldName).Append(@";
");
        }

        foreach (var item in Locations)
        {
            code.Append(@"
    [global::System.Runtime.CompilerServices.InterceptsLocation(").Append(item.Version).Append(@", """).Append(item.Data).Append('"').Append(@")] //").Append(item.GetDisplayLocation());
        }

        AppendInterceptor(code, providerTypeName);
    }

    void AppendInterceptor(StringBuilder code, string providerTypeName)
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

        code.Append(" InterceptorCall").Append(Index).Append(@"(this ").Append(providerTypeName);

        //if (isScopedCall) code.Append(".Scoped");

        code.Append(" provider");

        if (IsKeyed) code.Append(", string _");

        var cached = CacheLifetime is not null;

        if (useAsync)
        {
            // Antes se emitia [await A, await B]: cada elemento esperaba al anterior.
            // Se materializan primero todas las tareas para que avancen en paralelo y
            // luego se esperan una a una.
            code.Append(@")
    {");

            // La cache guarda el array ya resuelto, no la tarea: una tarea fallida se
            // quedaria cacheada y todo el proceso heredaria el fallo. Si dos llamadas
            // concurrentes se cruzan, cada una construye un array cuyos elementos son los
            // mismos objetos cacheados, asi que la carrera solo desperdicia una asignacion.
            if (cached)
                code.Append(@"
        if (").Append(CacheAccess).Append(@" is { } __cached) return __cached;
");

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

            // Si dos tareas fallan, el primer await lanza y las demas quedan huerfanas: su
            // excepcion nunca se observa y termina en TaskScheduler.UnobservedTaskException.
            // Observarlas en el camino de salida cuesta cero (medido en la Fase 17: el
            // try/catch es indistinguible de no tenerlo), pero solo se puede hacer sobre
            // Task<T>. ValueTask<T> no expone Exception, y AsTask() sobre una ya consumida
            // lanza InvalidOperationException, asi que ahi se deja como estaba.
            var observable = AppendInterceptorValue
                .Where(e => e.IsAsync)
                .ToList();

            var observes = observable.Count > 1 && observable.TrueForAll(e => !e.IsValueTask);

            if (observes)
                code.Append(@"

        try
        {");

            // Sangria extra para el cuerpo que queda dentro del try. NormalizeLayout convierte
            // cada 4 espacios en un tabulador, asi que se cuenta en espacios.
            var pad = observes ? "    " : "";

            var awaited = new Dictionary<int, int>();

            foreach (var coveringIndex in resolvedBy.Values.Distinct().OrderBy(i => i))
            {
                code.Append(@"
        ").Append(pad).Append("var __r").Append(awaited.Count).Append(" = await __t").Append(coveringIndex).Append(';');

                awaited.Add(coveringIndex, awaited.Count);
            }

            code.Append(@"

        ").Append(pad).Append("return ");

            if (cached) code.Append(CacheAccess).Append(" = ");

            code.Append('[');

            index = 0;

            foreach (var element in AppendInterceptorValue)
            {
                if (index > 0) code.Append(',');

                code.Append(@"
            ").Append(pad);

                if (!element.IsAsync) element.Append(code, true);
                else if (resolvedBy.TryGetValue(index, out var coveringIndex))
                    code.Append("__t").Append(index)
                        .Append(".Result /* resolved previously by __t").Append(coveringIndex).Append(" */");
                else if (awaited.TryGetValue(index, out var local)) code.Append("__r").Append(local);
                else code.Append("await __t").Append(index);

                index++;
            }

            code.Append("];");

            if (observes)
            {
                code.Append(@"
        }
        catch
        {");

                index = 0;

                foreach (var element in AppendInterceptorValue)
                {
                    if (element.IsAsync)
                        code.Append(@"
            _ = __t").Append(index).Append(".Exception;");

                    index++;
                }

                code.Append(@"

            throw;
        }");
            }

            code.Append(@"
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
            // Sin candado a proposito: todos los elementos son cacheados, asi que dos
            // arrays construidos a la vez contienen exactamente los mismos objetos. Lo
            // unico que cuesta una carrera es la asignacion que se iba a ahorrar.
            if (cached) code.Append(CacheAccess).Append(" ??= ");

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

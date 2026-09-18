using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SourceCrafter.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

#pragma warning disable CA1050 // Declarar tipos en espacios de nombres
internal partial class ServiceProviders
#pragma warning restore CA1050 // Declarar tipos en espacios de nombres
{
    private static Emitter TryParseContainer(
        in GeneratorAttributeSyntaxContext gasc,
        in CancellationToken cancelToken)
    {
        var model = gasc.SemanticModel;
        var providerType = (INamedTypeSymbol)gasc.TargetSymbol;
        var providerDeclarationSyntax = providerType.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancelToken);        
        string modifiers, typeName;

        switch(providerDeclarationSyntax)
        {
            case ClassDeclarationSyntax { Modifiers: var mods, Keyword: { } keyword, Identifier: { } identifier, TypeParameterList: var typeParamsList }:
                (modifiers, typeName) = ($"{mods} {keyword}".TrimStart(), $"{identifier}{typeParamsList}");
                break;
            case InterfaceDeclarationSyntax { Modifiers: var mods, Identifier: { } identifier, TypeParameterList: var typeParamsList }:
                (modifiers, typeName) = ($"{mods/*.Except([SyntaxFactory.Token(SyntaxKind.InterfaceKeyword)])*/} partial class".TrimStart(), $"{identifier.ValueText[1..]}{typeParamsList}");
                break;
            default: return null!;
        };

        var providerFullTypeName = providerType.FullGlobalQualifiedName;
        var isInterfaceProvider = providerType.TypeKind == TypeKind.Interface;
        var providerTypeName = providerType.TypeNameFormat;
        var className = isInterfaceProvider ? providerTypeName[1..] : providerTypeName;
        var fullProviderImplName = providerType.ContainingNamespace.FullGlobalQualifiedName + '.' + className;
        var attributes = providerType.GetAttributes();

        bool
            hasScopedDependencies = false,
            implementsServiceProvider = providerType.AllInterfaces.Any(i => i.FullGlobalQualifiedName == "global::System.IServiceProvider"),
            genericApi = false,
            exportTransients = false;

        // Si el usuario ya declara estos miembros en su parcial, el generador no los emite.
        var hasUserEnvironmentName =
            providerType.GetMembers("EnvironmentName").Length > 0
            || providerType.GetMembers("EnvironmentVariableName").Length > 0;

        // El generador emite un constructor sin parametros para inicializar candados y
        // cancelacion. Si el usuario ya declaro uno, se le avisa en vez de dejar que el
        // compilador escupa un CS0111 apuntando a codigo que el no escribio.
        var hasUserParameterlessConstructor = providerType
            .GetMembers(WellKnownMemberNames.InstanceConstructorName)
            .Any(m => m is IMethodSymbol { Parameters.Length: 0, IsImplicitlyDeclared: false, IsStatic: false });

        // Se proyecta a cadena aqui: el renderizador no puede ver simbolos.
        var lockTypeName = SupportsDedicatedLockType(model)
            ? "global::System.Threading.Lock"
            : "object";

        HashSet<string> externalAssemblies = [];

        var defaultKeyComparer = EqualityComparer<FirstLevelDependencyKey>.Default;
        var defaultSubKeyComparer = EqualityComparer<DependencyKey>.Default;

        DependencyDictionary dependencyValueBuilders = [];
        Dictionary<DependencyKey, (string Field, string Member)> methodNamesMap = new(defaultSubKeyComparer);
        HashSet<string> methodsRegistry = [];
        Dictionary<DependencyKey, MemberBuilder> dependencyMemberBuilders = [];
        HashSet<ResolverBuilder> genericResolvers = new(new GenericResolverBuilderComparer());

        Disposability
            containerDisposability = Disposability.None,
            scopedDisposability = Disposability.None;
        int
            asyncScopedDisposable = 0,
            asyncSingletonDisposable = 0,
            asyncScopedAsyncDisposable = 0,
            asyncSingletonAsyncDisposable = 0,
            scopedAsyncDisposable = 0,
            singletonAsyncDisposable = 0;

        HashSet<Diagnostic> diagnostics = [];

        // Fabricas genericas pendientes de cerrar. Una plantilla no es un servicio: no se
        // puede registrar `ILogger<T>` porque `T` no designa nada. Se guarda aparte y se
        // instancia una vez por tipo construido que algun consumidor pida (cierre por
        // consumo), que es lo unico que convierte la plantilla en registros concretos.
        List<GenericFactoryTemplate> genericFactoryTemplates = [];

        // Tipos construidos que ya se cerraron, para no registrar dos veces el mismo ni
        // reentrar al resolver un parametro que la propia expansion acaba de introducir.
        HashSet<string> closedGenericServices = [];

        ResolverBuilder selfDepInfo = new($"{providerFullTypeName}")
        {
            Key = (Lifetime.Singleton, providerFullTypeName, ""),
            AppendValue = (_, _, _, _) => { }
        };

        string envName = DefaultEnvName;

        // Las opciones del contenedor se leen antes de registrar nada: `exportTransients`
        // decide si un transient sin dependencias genera miembro, y el orden en que el
        // usuario escriba los atributos no debe alterar el resultado. `envName` y
        // `genericApi` solo se consumen despues del bucle, pero se leen aqui por coherencia.
        foreach (var attr in attributes)
        {
            if (attr.AttributeClass?.FullGlobalQualifiedName is ServiceProviderAttr)
            {
                ReadContainerOptions(attr, cancelToken);
                break;
            }
        }

        foreach (var attr in attributes)
        {
            if(TryRegisterService(attr, null, out var resolver, cancelToken))
                genericResolvers.Add(resolver!); ;
        }

        if (dependencyValueBuilders.Count == 0 && diagnostics.Count == 0) return null!;

        // El generador ya no emite constructor para los candados (se crean de forma
        // perezosa), asi que solo hay conflicto real si algun resolver usa el token de vida.
        if (hasUserParameterlessConstructor
            && dependencyMemberBuilders.Values.Any(m => m.RequiresCancelToken))
            diagnostics.Add(ServiceContainerDiagnostics.ConflictingParameterlessConstructor(
                providerType.Locations.FirstOrDefault() ?? Location.None,
                className));

        var nameSpace = providerType.ContainingNamespace is { } ns ? ns.ToDisplayString() : null;

        Emitter emitter = new(
            providerType.MetadataLongName,
            nameSpace,
            providerFullTypeName,
            modifiers,
            isInterfaceProvider,
            implementsServiceProvider,
            genericApi,
            hasUserEnvironmentName,
            className,
            typeName,
            envName,
            asyncScopedDisposable,
            asyncSingletonDisposable,
            asyncScopedAsyncDisposable,
            asyncSingletonAsyncDisposable,
            scopedAsyncDisposable,
            singletonAsyncDisposable,
            containerDisposability,
            scopedDisposability,
            dependencyValueBuilders,
            dependencyMemberBuilders,
            diagnostics,
            genericResolvers);

        diagnostics = null!;
        dependencyValueBuilders = null!;
        dependencyMemberBuilders = null!;
        genericResolvers = null!;

        return emitter;

        /// <summary>
        /// Lee los parametros de <c>[ServiceProvider]</c> emparejando por nombre de
        /// parametro y no por posicion: con argumentos con nombre el orden no es fiable
        /// (antes, <c>[ServiceProvider(genericApi: true)]</c> acababa
        /// tomando el booleano como nombre de la variable de entorno).
        /// </summary>
        void ReadContainerOptions(AttributeData attr, CancellationToken cancelToken)
        {
            var ctorParams = attr.AttributeConstructor?.Parameters ?? [];
            var args = (attr.ApplicationSyntaxReference?.GetSyntax(cancelToken) as AttributeSyntax)?.ArgumentList?.Arguments;

            if (args is not { Count: > 0 })
            {
                // Atributo sin sintaxis disponible: se usan los valores ya resueltos.
                if (attr.ConstructorArguments is [{ Value: string v }, ..] && v.Trim().Length > 0)
                    envName = $@"""{v}""";

                if (attr.ConstructorArguments is [_, { Value: bool flag }, ..])
                    genericApi = flag;

                if (attr.ConstructorArguments is [_, _, { Value: bool exportFlag }, ..])
                    exportTransients = exportFlag;

                return;
            }

            var position = 0;

            foreach (var arg in args)
            {
                var paramName = arg.NameColon?.Name.Identifier.ValueText
                    ?? (position < ctorParams.Length ? ctorParams[position].Name : null);

                if (arg.NameColon is null) position++;

                switch (paramName)
                {
                    case "envName":

                        envName = arg.Expression switch
                        {
                            LiteralExpressionSyntax { Token.ValueText: { } envString } when envString.Trim() is { Length: > 0 } => $@"""{envString}""",
                            MemberAccessExpressionSyntax { Name: { } member } => model.GetSymbolInfo(member, cancellationToken: cancelToken) switch
                            {
                                { Symbol: IFieldSymbol { } field } => field.GlobalNamespaced,
                                _ => DefaultEnvName
                            },
                            IdentifierNameSyntax member => isInterfaceProvider ? $"{providerTypeName}.{member.Identifier.ValueText}" : member.Identifier.ValueText,
                            _ => DefaultEnvName
                        };

                        break;

                    case "genericApi":

                        genericApi = model.GetConstantValue(arg.Expression, cancelToken) is { HasValue: true, Value: true };

                        break;

                    case "exportTransients":

                        exportTransients = model.GetConstantValue(arg.Expression, cancelToken) is { HasValue: true, Value: true };

                        break;
                }
            }
        }

        bool TryRegisterService(AttributeData? attr, ISymbol? sourceSymbol, out ResolverBuilder? resolver, CancellationToken cancelToken, ChildDependencyHandler? validateAsChildDependency = null, IMethodSymbol? closedGenericFactory = null)
        {
            (SymbolKind sourceKind, ITypeSymbol? sourceType) = sourceSymbol switch
            {
                IParameterSymbol { Type: ITypeSymbol factoryType } => (SymbolKind.Parameter, factoryType),
                IPropertySymbol { Type: ITypeSymbol factoryType } => (SymbolKind.Property, factoryType),
                IFieldSymbol { Type: ITypeSymbol factoryType } => (SymbolKind.Field, factoryType),
                IMethodSymbol { ReturnType: ITypeSymbol factoryType } => (SymbolKind.Method, factoryType),
                _ => (SymbolKind.Discard, null)
            };

            bool
                isSimpleTransient = false,
                isExternal = false,
                isCached = false,
                isValid = false,
                isFactory = false,
                hasAsyncDependencies = false,
                isStaticFactory = false,
                isFactoryFromCurrentProvider = false,
                isFactoryIndexerProperty = false,
                needsCancelToken = false;

            string
                name = string.Empty,
                exportTypeFullName = string.Empty,
                typeFullName = string.Empty,
                factoryProviderName = string.Empty;

            string?
                nameOrFormat = null,
                factoryName = null,
                interfaceFullTypeName = null;

            AsyncKind
                AsyncKind = default,
                initialAsyncType = default;

            FirstLevelDependencyKey key = default;
            DependencyKey subKey = default;

            ImmutableArray<IParameterSymbol>
                prms = [];
            Disposability
                disposability = default;
            AttributeSyntax
                attrSyntax = null!;
            INamedTypeSymbol?
                attrClass;
            Lifetime
                lifetime = default;
            LockOptions
                lockOption = default;
            AttributeArgumentSyntax?
                lockOptionArgSyntax = null;
            ITypeSymbol?
                interfaceType = null;
            ISymbol?
                factory = null;
            IMethodSymbol?
                genericFactoryTemplate = null;
            SymbolKind
                factoryKind = default;
            Dictionary<DependencyKey, AsyncLocalResolver>
                asyncLocalResolvers = new(defaultSubKeyComparer);
            List<ParamBuildOptions>
                appendParams = [];
            ITypeSymbol
                exportType = null!,
                type = null!;

            resolver = null!;

            var (backingFieldName, methodName) = ("", "");

            // Estado de render, libre de simbolos y sintaxis. Los delegados que el
            // emisor cachea apuntan a esta celda, nunca a la clase de cierre del
            // parser (que retendria la Compilation entre pasadas incrementales).
            // El modelo en si es inmutable: se construye entero en CommitRenderState.
            ResolverRendererRef render = new();

            if (!IsValidServiceAttribute(attr, cancelToken))
            {
                //(lifetime, exportType?.ToDisplayString(), type?.ToDisplayString(), name, false).Dump("Checking:");
                return false;
            }

            // La plantilla se aparta con la clave y el atributo que la registraron, para que
            // cada cierre herede lo que el usuario escribio. No produce resolver por si
            // misma: sin consumidores no hay tipos construidos y no hay nada que emitir,
            // que es justo el comportamiento que se quiere para una factory transient.
            if (genericFactoryTemplate is { } template && attr is not null)
            {
                if (template.ReturnType.TryGetAsyncType(out var openReturn) is var _
                    && openReturn is INamedTypeSymbol { IsGenericType: true } openNamed)
                {
                    genericFactoryTemplates.Add(new(template, attr, openNamed, name));
                }

                return false;
            }

            //(lifetime, interfaceType?.ToDisplayString(), type.ToDisplayString(), name).Dump("Checking:");

            var deepParamsCount = 0;
            var services = CollectionsMarshal.GetValueRefOrAddDefault(dependencyValueBuilders, key, out var exportSignatureExists) ??= [];

            // hecks the implementation existence
            if (type is null && exportSignatureExists)
            {
                (backingFieldName, methodName) = GetResolverName();
                //new { lifetime, name, exportType }.Dump("Duplicated service:");
                diagnostics.Add(
                    ServiceContainerDiagnostics
                        .DuplicateService(lifetime, name, attrSyntax, typeFullName, exportTypeFullName));

                return false;
            }

            bool hasNoCachedDeps = lifetime is Lifetime.Transient;

            // Takes or create a new Resolver instance
            resolver = CollectionsMarshal.GetValueRefOrAddDefault(services, subKey, out var implExists) ??=
                new($"{lifetime} {(exportTypeFullName + (exportTypeFullName == typeFullName ? null : $"<{typeFullName}>"))} {name}".Trim())
                {
                    Key = subKey,
                    ExportTypeFullName = exportTypeFullName,
                    AppendValue = render.AppendValue,
                    AsyncKind = AsyncKind,
                    ParamsLength = prms.Length,
                    TransientWithoutCachedDeps = isSimpleTransient
                };

            // If it comes from params check, validates the symbols as dependency
            if (validateAsChildDependency?.Invoke(
                    exportSignatureExists,
                    isValid,
                    lifetime,
                    implExists ? resolver.AsyncKind : AsyncKind,
                    implExists ? resolver.ParamsLength : prms.Length,
                    render.AppendValue,
                    type is null,
                    isExternal && name is "") is false)
            {
                return false;
            }

            asyncLocalResolvers = resolver.AsyncLocalResolvers;

            disposability = type!.GetDisposability();

            // Determines disposability at instance or scoped instance level
            if (isCached)
            {
                if (lifetime is Lifetime.Scoped && disposability > scopedDisposability)
                    scopedDisposability = disposability;
                else if (disposability > containerDisposability)
                    containerDisposability = disposability;
            }

            // Internal primitive value service providers must be wkeyed
            if (!isExternal && (sourceType ?? type!).IsPrimitive() && name is "")
            {
                //exportTypeFullName.Dump($"Primitive {lifetime} type should be keyed:");
                diagnostics.Add(
                    ServiceContainerDiagnostics
                        .PrimitiveDependencyMustBeKeyed(lifetime, attrSyntax, typeFullName, exportTypeFullName));
            }

            //$"{key}: {existingOrNewValueBuilder}".Dump();

            if (isSimpleTransient)
            {
                (backingFieldName, methodName) = GetResolverName();

                // Un transient sin dependencias se inlinea en el call site y sale por aqui
                // sin generar miembro. El problema es que entonces resulta *irresoluble*
                // desde otro ensamblado: la interceptacion es por compilacion, y sin miembro
                // con nombre la API generica cae en el stub que lanza. `exportTransients` lo
                // expone sin tocar el inlinado, que se sigue aplicando dentro de la propia
                // compilacion.
                if (exportTransients && !isExternal) RegisterExposedMember();

                //TryRegisterInterceptorMethod();
                CommitRenderState();
                return true;
            }

            var paramsToResolve = prms.Length;

            byte paramPos = 0/*, valueTaskCount = 0, asyncParamCount = 0*/;

            Dictionary<int, HashSet<AsyncLocalResolver>> asyncParams = [];

            // Un solo parametro por tipo de servicio puede quedarse sin clave. El segundo ya no
            // tiene forma de distinguirse, asi que se registra aqui cual se llevo el comodin
            // para poder senalar la pareja en el diagnostico.
            Dictionary<string, string> unkeyedParamByType = [];

            foreach (var prm in prms)
            {
                var paramIndex = paramPos++;
                var paramAsyncType = prm.Type.TryGetAsyncType(out var paramType);

                var getServices = false;
                if (!paramType.IsPrimitive())
                {
                    if (paramType.AllInterfaces.FirstOrDefault(i => i.SpecialType is
                        SpecialType.System_Collections_Generic_IEnumerable_T or
                        SpecialType.System_Collections_Generic_ICollection_T or
                        SpecialType.System_Collections_Generic_IReadOnlyCollection_T or
                        SpecialType.System_Collections_Generic_IList_T) is { TypeArguments: [{ } _type] })
                    {
                        getServices = true;
                        paramType = _type;
                    }
                    else if (paramType is IArrayTypeSymbol { ElementType: { } _type1 })
                    {
                        getServices = true;
                        paramType = _type1;
                    }
                }

                var paramFullTypeName = paramType.FullGlobalQualifiedName;
                var paramTypeHashCode = paramFullTypeName.GetHashCode();

                if (paramType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == className || SymbolEqualityComparer.Default.Equals(paramType, providerType))
                {
                    var asksForRoot = prm.GetAttributes().Any(a => a.AttributeClass?.FullGlobalQualifiedName == $"{GlobalBaseAttributeNS}.RootAttribute");
                    appendParams.Add(new(selfDepInfo.Key, new SelfProviderAppender(asksForRoot).Append));
                    continue;
                }

                if (Equals(prm.Type.FullGlobalQualifiedName, CancelTokenFQMetaName))
                {
                    needsCancelToken = true;
                    resolver.PassCancelToken = true;
                    appendParams.Add(new(selfDepInfo.Key, render.AppendCancelToken));
                    continue;
                }

                var isPrimitiveParamType = paramType.IsPrimitive();

                SubDependencyDictionary foundServices = null!;
                ResolverBuilder foundService = null!;
                var foundAsyncType = AsyncKind.None;
                HashSet<(int, bool)> existingAsyncDepsCalls = [];
                DependencyKey resolvedSubKey = default;

                if (prm.GetAttributes() is { Length: > 0 } paramAttrs)
                {
                    foreach (var paramAttr in paramAttrs)
                    {
                        if (TryRegisterService(paramAttr, prm, out foundService!, cancelToken, ValidateChildParameterDependency))
                        {
                            break;
                        }
                    }
                }

                var paramName = prm.Name;
                var paramKeyHash = paramName.GetHashCode();

                // Cierre por consumo. Si nadie ha registrado este tipo construido y hay
                // plantillas genericas, este es el momento de instanciarlas: el parametro
                // que se esta resolviendo es la unica fuente que dice que
                // `ILogger<AuditLog>` hace falta. Se hace antes de buscar para que la
                // consulta de abajo lo encuentre ya registrado y siga el camino normal.
                if (foundService is null
                    && genericFactoryTemplates.Count > 0
                    && !dependencyValueBuilders.ContainsKey((paramFullTypeName, paramName))
                    && !dependencyValueBuilders.ContainsKey((paramFullTypeName, "")))
                {
                    TryCloseGenericServiceFor(paramType, paramFullTypeName, paramName, prm);
                }

                // La busqueda se hace siempre, aunque un atributo del parametro ya haya
                // dejado un `foundService`: las dos ramas de abajo desreferencian
                // `foundServices`, asi que entrar con el diccionario a null es un fallo
                // seguro. Antes la condicion era una cadena de `||` con un
                // `&& foundServices.Count > 0` al final, pero `&&` liga mas fuerte que `||`,
                // asi que la comprobacion de cardinalidad solo cubria la ultima alternativa
                // y las otras dos podian entrar con null o con un diccionario vacio.
                if (!dependencyValueBuilders.TryGetValue((paramFullTypeName, paramName), out foundServices!))
                    dependencyValueBuilders.TryGetValue((paramFullTypeName, ""), out foundServices!);

                if (foundService is not null || foundServices is { Count: > 0 })
                {
                    if (foundServices is null or { Count: 0 })
                    {
                        // Lo resolvio un atributo del propio parametro, asi que no esta
                        // indexado bajo este tipo. Es el unico candidato que hay.
                        resolvedSubKey = foundService!.Key;

                        if (hasNoCachedDeps && !foundService.TransientWithoutCachedDeps)
                            hasNoCachedDeps = false;

                        CreateParamResolverBuilder(foundService);
                    }
                    else if (getServices)
                    {
                        int i = 0, len = foundServices.Values.Count - 1;
                        foreach (var service in foundServices.Values)
                        {
                            if (hasNoCachedDeps && !service.TransientWithoutCachedDeps)
                                hasNoCachedDeps = false;

                            resolvedSubKey = (foundService = service).Key;
                            CreateParamResolverBuilder(foundService, i == 0, i++ == len);
                        }
                    }
                    else if (lifeTimes.Any(
                        paramLifeTime => (foundService = foundServices.LastOrDefault(
                            e => e.Key.lifetime == paramLifeTime && (e.Key.key == paramName || e.Key.key == "")).Value) is not null))
                    {
                        if (hasNoCachedDeps && !foundService!.TransientWithoutCachedDeps)
                            hasNoCachedDeps = false;

                        // El servicio se eligio por el comodin sin clave, no porque el nombre
                        // del parametro casara con una. Eso solo es ambiguo si hay **varios**
                        // registros compitiendo por ese tipo: con un unico candidato, dos
                        // parametros pueden compartirlo sin que nada quede sin decidir.
                        // Cuando si compiten, hasta ahora ambos recibian en silencio el mismo
                        // servicio (o se emitia un local sin declarar, CS0103).
                        if (foundServices.Count > 1 && foundService!.Key.key is "" && paramName is not "")
                        {
                            if (unkeyedParamByType.TryGetValue(paramFullTypeName, out var firstParamName))
                            {
                                diagnostics.Add(ServiceContainerDiagnostics.AmbiguousUnkeyedParameters(
                                    prm.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancelToken).GetLocation()
                                        ?? attrSyntax.GetLocation(),
                                    paramName,
                                    paramType.ToDisplayString(),
                                    firstParamName));
                            }
                            else
                            {
                                unkeyedParamByType[paramFullTypeName] = paramName;
                            }
                        }

                        CreateParamResolverBuilder(foundService!);
                    }

                    void CreateParamResolverBuilder(ResolverBuilder foundService, bool startsCollectionExpression = false, bool endsCollectionExpression = false)
                    {
                        if (foundService.PassCancelToken && !needsCancelToken)
                            needsCancelToken = true;

                        var AppendParam = foundService!.AppendValue;
                        var foundExportTypeFullName = foundService.ExportTypeFullName;

                        appendParams.Add(new(
                            foundService.Key,
                            new ParamValueAppender(foundService, AppendParam, foundExportTypeFullName, paramAsyncType).Append,
                            startsCollectionExpression,
                            endsCollectionExpression,
                            // Toma algun candado al resolverse: o esta cacheado, o es un
                            // transient que arrastra cacheados dentro. En ambos casos hay que
                            // sacarlo fuera del 'lock' del resolver que lo consume.
                            foundService.Key.lifetime is not Lifetime.Transient || !foundService.TransientWithoutCachedDeps));

                        deepParamsCount += foundService.ParamsLength;
                        foundAsyncType = foundService.AsyncKind;

                        if (foundAsyncType is not 0)
                        {
                            // Se promociona a 'Task', no a 'foundAsyncType'. Como 'Task' es el
                            // maximo del enum, el 'childAsyncType > AsyncKind' de mas abajo ya no
                            // puede volver a bajarlo: un servicio que hereda su asincronia de las
                            // dependencias acaba SIEMPRE como 'Task<T>', aunque todas ellas sean
                            // 'ValueTask<T>'.
                            //
                            // No es incorrecto -- 'Task<T>' es una forma valida y segura -- pero
                            // tiene un coste: el acelerador de resultado exige 'ValueTask' (ver
                            // 'UsesResultFastPath' en el renderer, que con un miembro 'Task<T>'
                            // asignaria 72 B por lectura al reconstruir la tarea), asi que nunca
                            // alcanza a los servicios compuestos, que son la mayoria en un grafo
                            // real. Solo lo aprovechan los que declaran 'source:' con 'ValueTask'.
                            //
                            // Propagar 'ValueTask' cuando todas las dependencias lo son cambiaria
                            // la firma publica del miembro generado, asi que es una decision de
                            // API, no una optimizacion interna: romperia a quien encadene
                            // '.ContinueWith(...)' o asigne el resultado a un 'Task'.
                            if (AsyncKind is 0)
                                AsyncKind = AsyncKind.Task;

                            if (!hasAsyncDependencies)
                                hasAsyncDependencies = true;

                            AsyncLocalResolver resolved = null!;

                            Action<StringBuilder, string?> AppendAsyncLocalResolver =
                                new AsyncLocalAppender(paramIndex, AppendParam, "\r\n\t\t").Append;

                            if (paramAsyncType is 0)
                            {
                                if (asyncLocalResolvers.TryGetValue(foundService.Key, out resolved!))
                                {
                                    var isCachedResolved = foundService.Key.lifetime is not Lifetime.Transient;
                                    resolved.AppendAsyncLocal = AppendAsyncLocalResolver;

                                    if (resolved.ParamIndex == -1 || isCachedResolved)
                                        resolved.ParamIndex = paramIndex;

                                    if (!resolved.ResolvedBefore && resolved.ParamIndex > resolved.ResolvedByParamIndex)
                                        resolved.ResolvedBefore = isCachedResolved;
                                }
                                else
                                {
                                    asyncLocalResolvers.TryAdd(foundService.Key, resolved = new(foundService.Key)
                                    {
                                        ResolvedByParamIndex = paramIndex,
                                        ParamIndex = paramIndex,
                                        IsValueTask = foundAsyncType is AsyncKind.ValueTask,
                                        ResolverDep = foundService.Key,
                                        AppendAsyncLocal = AppendAsyncLocalResolver
                                    });
                                }
                            }

                            foreach (var childValue in foundService.AsyncLocalResolvers.Values)
                            {
                                asyncLocalResolvers.TryAdd(childValue.DepKey, resolved = new(childValue.DepKey)
                                {
                                    IsValueTask = childValue.IsValueTask,
                                    ResolvedByParamIndex = paramIndex,
                                    ResolverDep = foundService.Key,
                                    ResolvedBefore = !childValue.IsValueTask && childValue.DepKey.lifetime is not Lifetime.Transient,
                                    AppendAsyncLocal = AppendAsyncLocalResolver
                                });
							}

						}
					}
				}
                else if (paramType.TypeKind is not TypeKind.Interface)
                {
                    TryRegisterService(null, prm, out _, cancelToken, ValidateChildParameterDependency);
                }
                else
                {
                    appendParams.Add(new(resolvedSubKey, render.AppendDefault));
                }

                bool ValidateChildParameterDependency(
                    bool childExists,
                    bool isChildValid,
                    Lifetime childLifetime,
                    AsyncKind childAsyncType,
                    int childParamCount,
                    AppendValue AppendParam,
                    bool isNullChildType,
                    bool isUnkeyedInternalPrimitive)
                {
                    if (!hasAsyncDependencies && childAsyncType > 0) hasAsyncDependencies = true;

                    if (paramAsyncType is 0)
                    {
                        Action<StringBuilder, string?> AppendAsyncLocalResolver =
                            new AsyncLocalAppender(paramIndex, AppendParam, "\r\n\t\t\t").Append;

                        if (asyncLocalResolvers.TryGetValue(resolvedSubKey, out var resolved))
                        {
                            resolved.AppendAsyncLocal = AppendAsyncLocalResolver;
                        }
                        else
                        {
                            asyncLocalResolvers.TryAdd(resolvedSubKey, resolved = new(resolvedSubKey)
                            {
                                ResolvedByParamIndex = paramIndex,
                                IsValueTask = foundAsyncType is AsyncKind.ValueTask,
                                ResolverDep = resolvedSubKey,
                                AppendAsyncLocal = AppendAsyncLocalResolver
                            });
						}
					}

					if (childAsyncType > AsyncKind)
                    {
                        AsyncKind = childAsyncType;
                    }

                    if (childExists)
                    {
                        deepParamsCount += childParamCount;
                        appendParams.Add(new(resolvedSubKey, AppendParam));
                        return false;
                    }
                    else if ((isNullChildType && isPrimitiveParamType) || (isUnkeyedInternalPrimitive && prm.Type.IsPrimitive()) || !isChildValid)
                    {
                        deepParamsCount += 1;
                        appendParams.Add(new(resolvedSubKey, render.AppendDefault));
                        return false;
                    }

                    return true;
                }
            }

            if (!resolver.PassCancelToken && needsCancelToken)
                resolver.PassCancelToken = true;

            if (appendParams.Count == 0)
            {
                isSimpleTransient = true;
            }
            else if (asyncLocalResolvers.Count > 0)
            {
                asyncLocalResolvers = asyncLocalResolvers.Values
                    .Where(lr => lr.ParamIndex > -1).OrderBy(lr => lr.ParamIndex)
                    .ToDictionary(i => i.DepKey);
            }

            // Aqui vivia el intento de propagar 'ValueTask' cuando todas las dependencias
            // asincronas lo eran. Ver la nota en la promocion a 'AsyncKind.Task' de mas
            // arriba: no se reactiva porque cambia la firma publica del miembro generado.

            (backingFieldName, methodName) = GetResolverName();

            //existingOrNewValueBuilder.AsyncNestedDeps.Dump($"Async deps for {existingOrNewValueBuilder}");

            if (disposability is not 0 && isCached)
            {
                switch (AsyncKind is not 0, lifetime, disposability)
                {
                    case (true, Lifetime.Scoped, Disposability.Disposable): asyncScopedDisposable++; break;
                    case (true, Lifetime.Singleton, Disposability.Disposable): asyncSingletonDisposable++; break;
                    case (true, Lifetime.Scoped, Disposability.AsyncDisposable): asyncScopedAsyncDisposable++; break;
                    case (true, Lifetime.Singleton, Disposability.AsyncDisposable): asyncSingletonAsyncDisposable++; break;
                    case (false, Lifetime.Scoped, Disposability.AsyncDisposable): scopedAsyncDisposable++; break;
                    case (false, Lifetime.Singleton, Disposability.AsyncDisposable): singletonAsyncDisposable++; break;
                }
            }


            // `isSimpleTransient` tambien puede activarse arriba, cuando todos los parametros
            // se resolvieron a valores por defecto y no queda nada que componer. Vale el
            // mismo razonamiento que en la salida temprana: se inlinea, y solo se expone si
            // el autor lo pidio con `exportTransients`.
            if (!isExternal && (isCached || !isSimpleTransient || exportTransients))
                RegisterExposedMember();

            resolver.TransientWithoutCachedDeps = hasNoCachedDeps;
            resolver.AsyncKind = AsyncKind;

            //TryRegisterInterceptorMethod();

            CommitRenderState();

            return true;

            // Expone el resolver como miembro con nombre del contenedor. Se lee el estado en
            // el momento de la llamada, no al declararla: las dos salidas lo invocan en
            // puntos distintos y con valores distintos de disposability y AsyncKind.
            void RegisterExposedMember() =>
                dependencyMemberBuilders
                    .TryAdd((lifetime, typeFullName, name),
                        new(lifetime, exportTypeFullName, name, AsyncKind, disposability, nameOrFormat)
                        {
                            RequiresCancelToken = needsCancelToken,
                            BuildAndExpose = render.AppendMethod
                        });

            // Cierra las plantillas genericas contra un tipo construido concreto que algun
            // consumidor acaba de pedir. Registra el resultado como un servicio normal, de
            // modo que a partir de aqui `ILogger<AuditLog>` deja de ser un caso especial.
            void TryCloseGenericServiceFor(
                ITypeSymbol requestedType,
                string requestedFullName,
                string requestedKey,
                IParameterSymbol requestingParam)
            {
                // Un mismo tipo construido puede pedirse desde varios consumidores. Solo se
                // cierra la primera vez: las siguientes ya lo encuentran registrado.
                if (!closedGenericServices.Add(requestedFullName + '|' + requestedKey)) return;

                GenericFactoryTemplate? chosen = null;
                IMethodSymbol chosenClosed = null!;
                GenericFactoryTemplate? ambiguousWith = null;
                ITypeParameterSymbol? lastUnsatisfied = null;
                string? lastCandidateName = null;

                foreach (var candidate in genericFactoryTemplates)
                {
                    // La clave forma parte de la identidad del servicio: una plantilla con
                    // clave solo atiende a parametros que piden esa misma clave.
                    if (candidate.Key != requestedKey && candidate.Key is not "") continue;

                    if (!TryCloseGenericFactory(candidate, requestedType, out var closed, out var unsatisfied))
                    {
                        if (unsatisfied is not null)
                        {
                            lastUnsatisfied = unsatisfied;
                            lastCandidateName = candidate.Factory.Name;
                        }

                        continue;
                    }

                    if (chosen is null)
                    {
                        (chosen, chosenClosed) = (candidate, closed);
                        continue;
                    }

                    // Dos plantillas producen el mismo tipo. No se inventa una precedencia
                    // por orden de declaracion: eso haria depender el servicio elegido de
                    // algo que no se lee en el sitio de registro. Se pide una clave, que es
                    // el mecanismo de desambiguacion que el resto del contenedor ya usa.
                    ambiguousWith = candidate;
                    break;
                }

                var paramLocation = requestingParam.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancelToken).GetLocation()
                    ?? attrSyntax.GetLocation();

                if (ambiguousWith is not null)
                {
                    diagnostics.Add(ServiceContainerDiagnostics.AmbiguousGenericFactories(
                        paramLocation,
                        requestedType.ToDisplayString(),
                        chosen!.Factory.Name,
                        ambiguousWith.Factory.Name));

                    return;
                }

                if (chosen is null)
                {
                    // Solo se informa si alguna candidata era del tipo generico correcto pero
                    // fallo una restriccion. Si ninguna lo era, este tipo simplemente no tiene
                    // nada que ver con las plantillas y el SCDI03 habitual es mejor mensaje.
                    if (lastUnsatisfied is not null)
                    {
                        diagnostics.Add(ServiceContainerDiagnostics.NoGenericFactorySatisfiesType(
                            paramLocation,
                            requestedType.ToDisplayString(),
                            lastCandidateName!,
                            DescribeConstraint(lastUnsatisfied)));
                    }

                    return;
                }

                // Se vuelve a entrar por el registro normal con la fabrica ya construida. El
                // atributo original viaja con la plantilla, asi que el servicio cerrado
                // hereda lifetime y clave sin duplicar la lectura de argumentos.
                TryRegisterService(chosen.Attribute, null, out _, cancelToken, null, chosenClosed);

                static string DescribeConstraint(ITypeParameterSymbol typeParameter)
                {
                    if (typeParameter.HasReferenceTypeConstraint) return "class";
                    if (typeParameter.HasValueTypeConstraint) return "struct";
                    if (typeParameter.HasUnmanagedTypeConstraint) return "unmanaged";
                    if (typeParameter.HasNotNullConstraint) return "notnull";

                    return typeParameter.ConstraintTypes is [{ } first, ..]
                        ? first.ToDisplayString()
                        : typeParameter.Name;
                }
            }

            // Congela el estado del parser en un renderizador inmutable, ya proyectado a
            // cadenas, banderas y enumeraciones. Se invoca en cada salida exitosa,
            // siempre antes de que el emisor pueda ejecutar los delegados.
            void CommitRenderState()
            {
                render.Value = new()
                {
                    typeFullName = typeFullName,
                    exportTypeFullName = exportTypeFullName,
                    factoryProviderName = factoryProviderName,
                    backingFieldName = backingFieldName,
                    methodName = methodName,
                    factoryName = factoryName,
                    AsyncKind = AsyncKind,
                    initialAsyncType = initialAsyncType,
                    lifetime = lifetime,
                    disposability = disposability,
                    factoryKind = factoryKind,
                    isCached = isCached,
                    isFactory = isFactory,
                    isExternal = isExternal,
                    hasNoCachedDeps = hasNoCachedDeps,
                    hasAsyncDependencies = hasAsyncDependencies,
                    needsCancelToken = needsCancelToken,
                    isStaticFactory = isStaticFactory,
                    isFactoryFromCurrentProvider = isFactoryFromCurrentProvider,
                    isFactoryIndexerProperty = isFactoryIndexerProperty,
                    isInterfaceProvider = isInterfaceProvider,
                    typeIsValueType = type?.IsValueType is true,
                    typeIsNonNullable = type?.IsNullable is false,
                    hasFactorySymbol = factory is not null,
                    lockTypeName = lockTypeName,
                    lockOption = lockOption,
                    appendParams = appendParams,
                    asyncLocalResolvers = asyncLocalResolvers
                };
            }

            bool IsValidServiceAttribute(AttributeData? attr, CancellationToken cancelToken)
            {
                if (attr is not { AttributeClass: { } _attrClass, ApplicationSyntaxReference: { } attrSyntaxRef }
                    // El atributo del contenedor no registra servicio; sus opciones ya se
                    // leyeron en la pasada previa, antes de este bucle.
                    || _attrClass.FullGlobalQualifiedName is ServiceProviderAttr
                    || attrSyntaxRef.GetSyntax(cancelToken) is not AttributeSyntax { } _attrSyntax
                    || !TryGetAttributeParamsDefinition(model.GetSymbolInfo(_attrSyntax, cancellationToken: cancelToken), out ImmutableArray<IParameterSymbol> attrParams)
                    || !TryGetLifetime(_attrSyntax, ref _attrClass, ref isExternal, out lifetime))
                {
                    return false;
                }

                attrSyntax = _attrSyntax;
                attrClass = _attrClass;

                if (isExternal = _attrClass.ContainingNamespace.ToDisplayString() != BaseAttributesNS)
                    externalAssemblies.Add(_attrClass.ContainingAssembly.MetadataName.Replace(".Metadata", ""));

                var foundInAttribute = false;

                if (attr.AttributeClass!.TypeArguments.Length > 0 is { } isGeneric)
                {
                    switch (_attrClass!.TypeArguments)
                    {
                        case [{ } t1, { } t2, ..]:
                            foundInAttribute = true;
                            interfaceType = t1;
                            interfaceFullTypeName = interfaceType.FullGlobalQualifiedName;
                            type = t2;
                            typeFullName = type.FullGlobalQualifiedName;
                            break;

                        case [{ } t1]:
                            foundInAttribute = true;
                            type = t1;
                            typeFullName = type.FullGlobalQualifiedName;

                            break;
                    }
                }
                ITypeSymbol? factoryReturnType = null;
                foreach (var (param, arg) in GetAttributeParamsMap(attrParams, _attrSyntax.ArgumentList?.Arguments ?? []))
                {
                    switch (param.Name)
                    {
                        case ImplParamName when type is null && !isGeneric 
                            && arg is { Expression: TypeOfExpressionSyntax { Type: { } _type } }:

                            type = (ITypeSymbol)model.GetSymbolInfo(_type, cancellationToken: cancelToken).Symbol!;
                            typeFullName = type.FullGlobalQualifiedName;

                            continue;

                        case IfaceParamName when 
                            interfaceType is null 
                            && !isGeneric 
                            && arg is { Expression: TypeOfExpressionSyntax { Type: { } type } }:

                            interfaceType = (ITypeSymbol)model.GetSymbolInfo(type, cancellationToken: cancelToken).Symbol!;
                            interfaceFullTypeName = interfaceType.FullGlobalQualifiedName;

                            continue;

                        case KeyParamName when GetStringExpressionOrValue(model, param!, arg, out var keyValue):

                            name = keyValue;

                            continue;

                        case NameFormatParamName when GetStringExpressionOrValue(model, param, arg, out var keyValue):

                            nameOrFormat = keyValue;

                            continue;

                        // El alcance del candado es una constante de compilacion: se toma del
                        // argumento si esta escrito, y si no, del valor por defecto declarado
                        // en el atributo. 'Default' se resuelve mas abajo segun el lifetime,
                        // que puede venir del propio atributo generico.
                        case LocksParamName:

                            if (arg is { Expression: { } lockExpr })
                            {
                                if (model.GetConstantValue(lockExpr, cancellationToken: cancelToken) is { HasValue: true, Value: byte lockRaw })
                                {
                                    lockOption = (LockOptions)lockRaw;
                                    lockOptionArgSyntax = arg;
                                }
                            }
                            else if (param is { HasExplicitDefaultValue: true, ExplicitDefaultValue: byte lockDefault })
                            {
                                lockOption = (LockOptions)lockDefault;
                            }

                            continue;

                        case SourceParamName

                            when arg?.Expression is InvocationExpressionSyntax
                            {
                                Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" },
                                ArgumentList.Arguments: [{ } methodRef]
                            }:

                            switch (model.GetSymbolInfo(methodRef.Expression, cancellationToken: cancelToken))
                            {
                                case { Symbol: (IFieldSymbol or IPropertySymbol) and { ContainingType: ITypeSymbol containingType, Kind: var kind, IsStatic: var isStatic } fieldOrProp }:

                                    CheckInnerFactorySpecs(fieldOrProp);

                                    factory = fieldOrProp;
                                    factoryName = factory.Name;
                                    factoryKind = kind;
                                    isFactory = true;
                                    isStaticFactory = isStatic;
                                    isFactoryFromCurrentProvider = SymbolEqualityComparer.Default.Equals(providerType, containingType);

                                    (AsyncKind, isFactoryIndexerProperty) = fieldOrProp switch
                                    {
                                        IPropertySymbol { Type: ITypeSymbol type, IsIndexer: var isIndexer } => (initialAsyncType = type.TryGetAsyncType(out factoryReturnType), isIndexer),
                                        _ => (((IFieldSymbol)fieldOrProp).Type.TryGetAsyncType(out factoryReturnType), false)
                                    };

                                    if (factoryReturnType is not { TypeKind: TypeKind.Interface, IsAbstract: true })
                                    {
                                        type ??= factoryReturnType;
                                        typeFullName = type.FullGlobalQualifiedName;
                                    }
                                    else
                                    {
                                        interfaceType ??= factoryReturnType;
                                        interfaceFullTypeName = interfaceType.FullGlobalQualifiedName;
                                    }

                                    factoryProviderName = isFactoryFromCurrentProvider ? providerTypeName : containingType.GlobalNamespaced;

                                    continue;

                                case { CandidateReason: CandidateReason.MemberGroup, CandidateSymbols: [IMethodSymbol { ContainingType: ITypeSymbol containingType, ReturnsVoid: false, IsStatic: var isStatic } openMethod] }:

                                    CheckInnerFactorySpecs(openMethod);
                                    CheckGenericFactorySpecs(openMethod);

                                    // Una plantilla generica no se registra como servicio:
                                    // `ILogger<T>` no designa nada resoluble. Se aparta y se
                                    // cierra despues, una vez por tipo construido que el grafo
                                    // pida. Registrarla aqui es justo lo que hacia que el
                                    // consumidor de `ILogger<AuditLog>` no encontrase nada.
                                    if (openMethod.IsGenericMethod && closedGenericFactory is null)
                                    {
                                        genericFactoryTemplate = openMethod;
                                        continue;
                                    }

                                    // En la reentrada por cierre llega la version construida,
                                    // que ya devuelve el tipo concreto y por tanto recorre el
                                    // resto del registro como cualquier factory no generica.
                                    var method = closedGenericFactory ?? openMethod;

                                    factory = method;
                                    isStaticFactory = isStatic;
                                    factoryKind = SymbolKind.Method;
                                    initialAsyncType = AsyncKind = method.ReturnType.TryGetAsyncType(out factoryReturnType);
                                    isFactory = true;
                                    isFactoryFromCurrentProvider = SymbolEqualityComparer.Default.Equals(providerType, containingType);

                                    // El nombre debe llevar los argumentos de tipo: lo que se
                                    // emite es `_CreateLogger<AuditLog>()`, no `_CreateLogger()`.
                                    // Sin ellos la llamada no compila, porque en el sitio de uso
                                    // no hay nada de donde inferirlos.
                                    factoryName = closedGenericFactory is { TypeArguments: { Length: > 0 } typeArgs }
                                        ? $"{method.Name}<{string.Join(", ", typeArgs.Select(t => t.FullGlobalQualifiedName))}>"
                                        : factory.Name;

                                    if (closedGenericFactory is not null)
                                    {
                                        // En una factory el metodo es la implementacion. Al
                                        // cerrar `ILogger<T>` el retorno es una interfaz, y
                                        // dejar `type` nulo haria que el registro la tratase
                                        // como interfaz sin implementar.
                                        type = interfaceType = factoryReturnType;
                                        typeFullName = interfaceFullTypeName = factoryReturnType!.FullGlobalQualifiedName;
                                        factoryProviderName = isFactoryFromCurrentProvider ? providerTypeName : containingType.GlobalNamespaced;

                                        continue;
                                    }

                                    if (factoryReturnType is not { TypeKind: TypeKind.Interface, IsAbstract: true })
                                    {
                                        type ??= factoryReturnType;
                                        typeFullName = type.FullGlobalQualifiedName;
                                    }
                                    else
                                    {
                                        interfaceType ??= factoryReturnType;
                                        interfaceFullTypeName = interfaceType.FullGlobalQualifiedName;
                                    }

                                    factoryProviderName = isFactoryFromCurrentProvider ? providerTypeName : containingType.GlobalNamespaced;

                                    continue;
                            }

                            continue;

                            void CheckInnerFactorySpecs(ISymbol method)
                            {
                                if (!isInterfaceProvider
                                    && lifetime == Lifetime.Transient
                                    && (SymbolEqualityComparer.Default.Equals(method.ContainingType, providerType)
                                    || method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == className)
                                    && (method.DeclaredAccessibility != Accessibility.Private || !method.Name.StartsWith("_"))
                                        && method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancelToken)?.GetLocation() is Location location2)
                                {
                                    diagnostics.Add(ServiceContainerDiagnostics.ThrowInnerFactorySpecs(method.Name, location2));
                                }
                            }

                            // Una fabrica generica se decide por sus restricciones, asi que sin
                            // ellas no hay criterio de emparejado; y solo puede ser transient
                            // porque un cacheado exigiria un campo por tipo construido, conjunto
                            // que no se conoce en el sitio de registro.
                            void CheckGenericFactorySpecs(IMethodSymbol method)
                            {
                                if (!method.IsGenericMethod) return;

                                var factoryLocation = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancelToken)?.GetLocation()
                                    ?? attrSyntax.GetLocation();

                                foreach (var typeParameter in method.TypeParameters)
                                {
                                    if (!HasAnyConstraint(typeParameter))
                                    {
                                        diagnostics.Add(ServiceContainerDiagnostics.GenericFactoryTypeParameterNeedsConstraint(
                                            factoryLocation, method.Name, typeParameter.Name));
                                    }
                                }

                                if (lifetime is not Lifetime.Transient)
                                {
                                    diagnostics.Add(ServiceContainerDiagnostics.GenericFactoryMustBeTransient(
                                        factoryLocation, method.Name, lifetime.ToString()));
                                }

                                // 'new()' queda fuera a proposito: no acota el conjunto de tipos
                                // admisibles, solo exige un constructor sin parametros, asi que no
                                // sirve como criterio de emparejado.
                                static bool HasAnyConstraint(ITypeParameterSymbol typeParameter)
                                    => typeParameter.HasReferenceTypeConstraint
                                        || typeParameter.HasValueTypeConstraint
                                        || typeParameter.HasUnmanagedTypeConstraint
                                        || typeParameter.HasNotNullConstraint
                                        || typeParameter.ConstraintTypes.Length > 0;
                            }

                        //case "disposability" when param.HasExplicitDefaultValue:

                        //    disposability = (Disposability)(byte)param.ExplicitDefaultValue!;

                        //    continue;
                    }
                }

                // Una plantilla generica se aparta aqui, antes de las comprobaciones que
                // asumen un tipo concreto. `ILogger<T>` no tiene implementacion ni puede
                // casar con nada: exigirselo produciria el SCDI03 que veiamos, cuando lo
                // cierto es que todavia no hay nada que registrar.
                if (genericFactoryTemplate is not null) return true;

                // El alcance efectivo del candado. 'Default' se resuelve aqui, ya conocido el
                // lifetime: un singleton comparte campo entre instancias del contenedor y
                // necesita el candado estatico. Un scoped, en cambio, no se sincroniza por
                // defecto: un ambito modela una peticion y no se comparte entre hilos, asi que
                // el candado se pagaba siempre sin contencion (medido: 25,0 ns frente a 6,3 ns
                // sin el). Quien comparta un ambito entre hilos pide 'LockOptions.Instance',
                // que sigue cerrando sobre 'this' para no asignar un candado por dependencia.
                // Un transient no cacheado nunca toma candado, asi que la opcion se ignora.
                switch (lockOption, lifetime)
                {
                    case (LockOptions.Default, Lifetime.Singleton):
                        lockOption = LockOptions.Global;
                        break;

                    case (LockOptions.Default, Lifetime.Scoped):
                        lockOption = LockOptions.None;
                        break;

                    case (LockOptions.Default, _):
                        lockOption = LockOptions.Instance;
                        break;

                    case (LockOptions.Global, Lifetime.Scoped):
                        diagnostics.Add(ServiceContainerDiagnostics.GlobalLockNotAllowedForScoped(
                            (lockOptionArgSyntax ?? (SyntaxNode)attrSyntax).GetLocation()));

                        lockOption = LockOptions.Instance;
                        break;
                }

                if (interfaceType != null)
                {
                    var count = diagnostics.Count;
                    if (type is null)
                    {
                        if (factoryReturnType is null)
                            diagnostics.Add(ServiceContainerDiagnostics.InterfaceWithNoImplementation(attrSyntax.GetLocation(), interfaceType, providerFullTypeName, lifetime));
                    }
                    else if(!foundInAttribute 
                        && !HasBaseType(type, interfaceType)
                        && !type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, interfaceType)))

                            diagnostics.Add(ServiceContainerDiagnostics.BaseAndImplementationMissmatch(attrSyntax.GetLocation(), type, interfaceType));

                    if (factoryReturnType is not null)
                    {
                        var typeToMatch = type ?? interfaceType;
                        if (!SymbolEqualityComparer.Default.Equals(typeToMatch, factoryReturnType)
                            && !HasBaseType(typeToMatch, interfaceType)
                            && !typeToMatch.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, interfaceType)))
                        {
                            diagnostics.Add(ServiceContainerDiagnostics.FactoryReturnMismatch(factory!, interfaceType, factoryReturnType, attrSyntax));
                        }

                    }

                    if (count < diagnostics.Count)
                        return false;

                    static bool HasBaseType(ITypeSymbol type, ITypeSymbol baseType)
                    {
                        while (type != null)
                            if (SymbolEqualityComparer.Default.Equals(type, baseType))
                                return true;
                            else type = type.BaseType!;
                        return false;
                    }
                }

                exportType ??= interfaceType ?? type!;
                exportTypeFullName = interfaceFullTypeName ?? typeFullName;

                // Task<T> y ValueTask<T> son invariantes: aunque la implementacion satisfaga la
                // interfaz, Task<Impl> no se convierte a Task<IService>. Las comprobaciones de
                // arriba aceptan el caso porque razonan sobre la relacion de herencia, que si se
                // cumple; el fallo reaparece despues como CS0029 dentro del codigo generado, que
                // es donde peor se lee. Se exige aqui que la fabrica asincrona declare ya el tipo
                // expuesto, en vez de esperar y reenvolver en la emision (lo que costaria una
                // maquina de estados o una asignacion extra por resolucion).
                if (initialAsyncType is not AsyncKind.None
                    && factoryReturnType is not null
                    && exportType is not null
                    && !SymbolEqualityComparer.Default.Equals(exportType, factoryReturnType))
                {
                    diagnostics.Add(ServiceContainerDiagnostics.AsyncFactoryMustDeclareServiceType(
                        attrSyntax.GetLocation(),
                        factory?.Name ?? factoryName ?? "?",
                        initialAsyncType is AsyncKind.ValueTask ? "ValueTask" : "Task",
                        factoryReturnType.ToDisplayString(),
                        exportType.ToDisplayString()));

                    return false;
                }

                if (!(isValid = exportType is not null && type is not null && attrClass is not null && _attrSyntax is not null)) return ReturnNotFound();

                if (!hasScopedDependencies && lifetime is Lifetime.Scoped)
                {
                    hasScopedDependencies = true;
                }

                if (AsyncKind is not 0 && factoryKind is SymbolKind.Method && !((IMethodSymbol)factory!).Parameters.Any(p => p.Type.FullGlobalQualifiedName is CancelTokenFQMetaName))
                {
                    //factory.ToDisplayString().Dump("Cancellation token should be Appendd");
                    diagnostics.Add(ServiceContainerDiagnostics.CancellationTokenShouldBeProvided(factory, attrSyntax));
                }

                key = (exportTypeFullName, name);
                subKey = (lifetime, typeFullName, name);

                isCached = isValid && lifetime is not Lifetime.Transient;

                if (factory switch
                {
                    IMethodSymbol factoryMethod => factoryMethod.Parameters,
                    IPropertySymbol { IsIndexer: true } factoryProperty => factoryProperty.Parameters,
                    IFieldSymbol => [],
                    _ => GetConstructorParameters(type)
                }
                    is { IsDefaultOrEmpty: false, Length: > 0 } parameters)
                {
                    prms = parameters;
                    isSimpleTransient = lifetime is Lifetime.Transient && prms.Length == 0;
                }
                else if (!isSimpleTransient && !isCached)
                {
                    isSimpleTransient = true;
                }

                return true;


                bool ReturnNotFound()
                {
                    diagnostics.Add(ServiceContainerDiagnostics.UnresolvedDependency(attrSyntax, providerTypeName, exportTypeFullName));
                    return false;
                }


                static ImmutableArray<IParameterSymbol> GetConstructorParameters(ITypeSymbol? implType)
                {
                    if (implType is not INamedTypeSymbol { Constructors: var ctor, InstanceConstructors: var insCtor }
                        || ctor.IsDefaultOrEmpty
                        || insCtor.IsDefaultOrEmpty) return [];

                    ImmutableArray<IParameterSymbol> parameters = [];

                    int min = int.MaxValue;

                    foreach (var item in ctor.Concat(insCtor).Distinct(SymbolEqualityComparer.Default).Cast<IMethodSymbol>())
                    {
                        if (item.Parameters.IsDefaultOrEmpty || item.Parameters.Length >= min) continue;
                        min = (parameters = item.Parameters).Length;
                    }

                    return parameters;
                }

                static bool TryGetAttributeParamsDefinition(SymbolInfo info, out ImmutableArray<IParameterSymbol> prms)
                {
                    if (info.Symbol is IMethodSymbol { Parameters: { } _prms })
                    {
                        prms = _prms;
                        return true;
                    }

                    foreach (var item in info.CandidateSymbols)
                    {
                        if (item is IMethodSymbol { Parameters: { } _prms2 })
                        {
                            prms = _prms2;
                            return true;
                        }
                    }

                    prms = [];

                    return false;
                }

                static Span<MarkedParameter> GetAttributeParamsMap(
                   ImmutableArray<IParameterSymbol> paramSymbols,
                   SeparatedSyntaxList<AttributeArgumentSyntax> argsSyntax)
                {
                    int i = -1;
                    Span<MarkedParameter> result = new MarkedParameter[paramSymbols.Length];

                    foreach (var param in paramSymbols)
                    {
                        result[++i] = argsSyntax.Count > i && argsSyntax[i] is { NameColon: null, NameEquals: null } argSyntax
                            ? new(param, argSyntax)
                            : new(param, argsSyntax.FirstOrDefault(arg => param.Name == arg.NameColon?.Name.Identifier.ValueText));
                    }

                    return result;
                }

                static bool GetStringExpressionOrValue(
                    SemanticModel model,
                    IParameterSymbol paramSymbol,
                    AttributeArgumentSyntax? arg,
                    out string value)
                {
                    value = null!;

                    if (arg is not null)
                    {
                        if (model.GetSymbolInfo(arg.Expression).Symbol is IFieldSymbol
                            {
                                IsConst: true,
                                Type.SpecialType: SpecialType.System_String,
                                ConstantValue: { } val
                            })
                        {
                            return (value = val.ToString()!) != "";
                        }
                        else if (arg.Expression is LiteralExpressionSyntax { Token.ValueText: { } valueText } e
                            && e.IsKind(SyntaxKind.StringLiteralExpression))
                        {
                            return (value = valueText) != "";
                        }
                    }
                    else if (paramSymbol.HasExplicitDefaultValue)
                    {
                        value = paramSymbol.ExplicitDefaultValue?.ToString()!;
                        return value != "";
                    }

                    return false;
                }
            }

            /// <summary>
            /// Resuelve el nombre del miembro que expone el resolver y el de su campo de
            /// respaldo, garantizando que ninguno choque con otro ya emitido.
            /// </summary>
            /// <remarks>
            /// Las distinciones (<c>{lifetime}</c>, <c>{key}</c>) se anaden <b>solo cuando
            /// hacen falta</b>: se prueba el nombre mas corto y se baja por la escalera hasta
            /// encontrar uno libre. La reserva se hace sobre el nombre <b>final</b>, ya
            /// decorado; hacerla sobre el nombre base dejaba fuera del registro tanto los
            /// nombres derivados de una fabrica como los pedidos con <c>nameFormat</c>, y
            /// tampoco cubria los sufijos <c>Cached</c> / <c>Async</c>.
            /// </remarks>
            (string, string) GetResolverName()
            {
                // La identidad de un resolver es el subKey (lifetime, tipo de implementacion,
                // clave). Memoizar por el tipo *expuesto* hacia que tres registros del mismo
                // interfaz compartieran entrada y salieran con el mismo nombre
                // (CS0102/CS0111/CS0229). Memoizar aqui, y no dentro de la escalera, tambien
                // evita que una segunda llamada para el mismo resolver choque consigo misma y
                // se lleve un sufijo numerico.
                var identity = (lifetime, typeFullName, name);

                if (methodNamesMap.TryGetValue(identity, out var memoized)) return memoized;

                // Misma regla que <c>Renderer.IsMethodShaped</c>: solo los resolvers
                // asincronos que componen dependencias salen como metodo.
                var isMethodShaped = AsyncKind is not 0 && (hasAsyncDependencies || needsCancelToken);

                var key = name.Pascalize() ?? "";
                var typeName = SanitizedTypeName();
                var lifetimeName = lifetime.ToString();

                var result = Resolve();

                methodNamesMap[identity] = result;

                return result;

                (string, string) Resolve()
                {
                    // 1. Nombre pedido por el autor: manda tal cual, no se le recorta nada.
                    //    Antes el nombre del metodo-fabrica lo pisaba siempre, asi que
                    //    'nameFormat' se descartaba en silencio si el registro traia 'source:'.
                    if (nameOrFormat is not null)
                        return ReserveOrNumber(FormatRequestedName(nameOrFormat), trimGetPrefix: false);

                    // 2. Nombre del metodo-fabrica del autor.
                    if (!isExternal && factory is not null)
                        return ReserveOrNumber(factory.Name, trimGetPrefix: true);

                    // 3. Escalera derivada del tipo.
                    foreach (var candidate in Candidates())
                        if (TryReserve(candidate, trimGetPrefix: true, out var reserved)) return reserved;

                    return ReserveOrNumber(lifetimeName + key + typeName, trimGetPrefix: true);
                }

                /// <summary>El nombre mas corto primero; cada peldano anade una distincion.</summary>
                IEnumerable<string> Candidates()
                {
                    yield return typeName;

                    // Sin clave, el lifetime es lo unico que queda para desempatar.
                    if (key is "")
                    {
                        yield return lifetimeName + typeName;

                        yield break;
                    }

                    // Con clave, el desempate lo hace la clave y no el lifetime. Meter aqui
                    // '{lifetime}{tipo}' la dejaria fuera del nombre: dos registros del mismo
                    // tipo distinguidos solo por la clave saldrian como 'Db' y 'SingletonDb',
                    // sin rastro de cual es cual y a merced del orden de declaracion.
                    yield return typeName + key;
                    yield return lifetimeName + key;
                    yield return lifetimeName + key + typeName;
                }

                /// <summary>
                /// Admite <c>{lifetime}</c>, <c>{key}</c> y <c>{tipo}</c> (o <c>{type}</c>)
                /// ademas del <c>{0}</c> historico, que sigue siendo la clave.
                /// </summary>
                string FormatRequestedName(string format)
                {
                    var text = format
                        .Replace("{lifetime}", lifetimeName)
                        .Replace("{key}", key)
                        .Replace("{tipo}", typeName)
                        .Replace("{type}", typeName);

                    // 'string.Format' lanza si el texto trae una llave suelta, y ya no queda
                    // ningun marcador con nombre que justifique correr ese riesgo.
                    if (text.IndexOf("{0}", StringComparison.Ordinal) >= 0)
                        text = string.Format(text, key);

                    return text.RemoveDuplicates();
                }

                /// <summary>
                /// Ultimo recurso de la escalera: <c>...{CountBase1}</c>. Existe para que el
                /// contenedor siempre compile, incluso cuando el autor pide dos veces el
                /// mismo nombre con <c>nameFormat</c>.
                /// </summary>
                (string, string) ReserveOrNumber(string baseName, bool trimGetPrefix)
                {
                    if (TryReserve(baseName, trimGetPrefix, out var reserved)) return reserved;

                    for (var count = 1; ; count++)
                        if (TryReserve(baseName + count, trimGetPrefix, out reserved)) return reserved;
                }

                bool TryReserve(string baseName, bool trimGetPrefix, out (string, string) reserved)
                {
                    var (fieldName, memberName) = reserved = Decorate(baseName, trimGetPrefix);

                    // Se comprueban los dos nombres. El campo se deriva del miembro, pero no
                    // biyectivamente: un metodo 'GetX' y una propiedad 'X' comparten el campo
                    // '_x', asi que mirar solo el miembro deja pasar un CS0102 del campo.
                    if (methodsRegistry.Contains(memberName) || methodsRegistry.Contains(fieldName))
                        return false;

                    methodsRegistry.Add(memberName);
                    methodsRegistry.Add(fieldName);

                    return true;
                }

                (string, string) Decorate(string baseName, bool trimGetPrefix)
                {
                    var memberName = baseName.TrimStart('_');

                    if (factory is not null && isCached && !memberName.EndsWith("Cached") && !memberName.EndsWith("Cache"))
                        memberName += "Cached";

                    // Una propiedad no debe llamarse 'GetX': el prefijo anuncia una operacion.
                    // El nombre suele venir del metodo-fabrica del autor ('_GetAlphaAsync'),
                    // asi que se recorta cuando lo derivamos nosotros; si el autor escribio el
                    // nombre, se respeta tal cual.
                    if (trimGetPrefix && !isMethodShaped && HasGetPrefix(memberName))
                        memberName = memberName.Substring(3);

                    // El campo se deriva *antes* del prefijo, para que campo y miembro no se
                    // separen ('_alphaAsyncCached' / 'AlphaAsyncCached').
                    var fieldName = "_" + memberName.Camelize();

                    // El prefijo solo se anade a lo que de verdad se emite como metodo.
                    if (!isExternal && factory is null && isMethodShaped) memberName = "Get" + memberName;

                    if (!(memberName.Contains("Async") || memberName.Contains("Task")) && AsyncKind is not 0)
                        (memberName, fieldName) = (memberName + "Async", fieldName + "Task");

                    return (fieldName, memberName);
                }

                static bool HasGetPrefix(string value) =>
                    value.Length > 3
                    && value[0] is 'G' && value[1] is 'e' && value[2] is 't'
                    && (char.IsUpper(value[3]) || char.IsDigit(value[3]));
            }

            string SanitizedTypeName()
            {
                return Sanitize(type!).Replace(" ", "").Capitalize();

                static string Sanitize(ITypeSymbol type)
                {
                    switch (type)
                    {
                        case INamedTypeSymbol { IsTupleType: true, TupleElements: { Length: > 0 } els }:

                            return "TupleOf" + string.Join("", els.Select(f => Sanitize(f.Type)));

                        case INamedTypeSymbol { IsGenericType: true, TypeParameters: { } args }:

                            return type.Name + "Of" + string.Join("", args.Select(Sanitize));

                        default:

                            string typeName = type.TypeNameFormat;

                            if (type is IArrayTypeSymbol { ElementType: { } elType })
                                typeName = Sanitize(elType) + "Array";

                            return char.ToUpperInvariant(typeName[0]) + typeName[1..].TrimEnd('?', '_');
                    }
                }
            }
        }
    }

    /// <summary>
    /// Indica si el codigo generado puede declarar sus candados como
    /// <c>System.Threading.Lock</c> en vez de <c>object</c>.
    /// </summary>
    /// <remarks>
    /// Hacen falta las dos condiciones. El tipo existe desde .NET 9, pero es el compilador
    /// quien reconoce <c>lock (x)</c> sobre el y emite <c>EnterScope</c>; con C# 12 o menos
    /// la variable se convierte a <c>object</c>, se vuelve a caer en <c>Monitor</c> y ademas
    /// se emite el aviso CS9216. Es decir: emitirlo sin C# 13 seria mas lento y mas ruidoso
    /// que seguir usando <c>object</c>.
    /// <para>
    /// Se exige que el tipo venga del mismo ensamblado que <c>System.Object</c> para no
    /// confundirlo con un <c>System.Threading.Lock</c> definido por el usuario, que no
    /// recibe trato especial del compilador.
    /// </para>
    /// </remarks>
    private static bool SupportsDedicatedLockType(SemanticModel model)
    {
        if (model.SyntaxTree.Options is not CSharpParseOptions { LanguageVersion: >= LanguageVersion.CSharp13 })
            return false;

        var compilation = model.Compilation;

        return compilation.GetTypeByMetadataName("System.Threading.Lock") is { } lockType
            && SymbolEqualityComparer.Default.Equals(
                lockType.ContainingAssembly,
                compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly);
    }

    /// <summary>
    /// Una fabrica generica a la espera de que el grafo diga que tipos construidos hacen
    /// falta.
    /// <para>
    /// Se guarda el metodo sin construir junto al atributo que lo registro. El atributo se
    /// necesita porque la expansion vuelve a pasar por el registro normal: un
    /// <c>ILogger&lt;AuditLog&gt;</c> cerrado no es un caso especial, es un servicio como
    /// cualquier otro, y debe heredar la clave y el resto de opciones que el usuario
    /// escribio en la plantilla.
    /// </para>
    /// </summary>
    private sealed record GenericFactoryTemplate(
        IMethodSymbol Factory,
        AttributeData Attribute,
        INamedTypeSymbol OpenReturnType,
        string Key);

    /// <summary>
    /// Decide si <paramref name="template"/> puede producir <paramref name="requested"/>, y
    /// si puede, devuelve el metodo ya construido.
    /// <para>
    /// El emparejado es puramente estructural: se exige el mismo tipo generico original
    /// (<c>ILogger&lt;&gt;</c> frente a <c>ILogger&lt;&gt;</c>) y se infiere cada parametro
    /// de tipo por posicion. No se intenta unificar nada mas complejo, como un
    /// <c>T</c> anidado dentro de otro generico: esos casos se rechazan en silencio y caen
    /// en el diagnostico de "ninguna candidata", que es un mensaje mas util que una
    /// inferencia parcial que luego falle al compilar.
    /// </para>
    /// </summary>
    private static bool TryCloseGenericFactory(
        GenericFactoryTemplate template,
        ITypeSymbol requested,
        out IMethodSymbol closedFactory,
        out ITypeParameterSymbol? unsatisfied)
    {
        closedFactory = null!;
        unsatisfied = null;

        if (requested is not INamedTypeSymbol { IsGenericType: true } requestedNamed
            || !SymbolEqualityComparer.Default.Equals(
                requestedNamed.OriginalDefinition,
                template.OpenReturnType.OriginalDefinition))
        {
            return false;
        }

        var typeParameters = template.Factory.TypeParameters;
        var openArguments = template.OpenReturnType.TypeArguments;
        var requestedArguments = requestedNamed.TypeArguments;

        if (openArguments.Length != requestedArguments.Length) return false;

        var inferred = new ITypeSymbol[typeParameters.Length];

        for (var i = 0; i < openArguments.Length; i++)
        {
            if (openArguments[i] is not ITypeParameterSymbol openParameter)
            {
                // Posicion fija en la plantilla (por ejemplo `ILogger<int, T>`): debe casar
                // exactamente, porque ahi no hay nada que inferir.
                if (!SymbolEqualityComparer.Default.Equals(openArguments[i], requestedArguments[i]))
                    return false;

                continue;
            }

            var position = typeParameters.IndexOf(openParameter);

            if (position < 0) return false;

            // El mismo parametro de tipo aparecido dos veces debe recibir el mismo argumento.
            if (inferred[position] is { } already
                && !SymbolEqualityComparer.Default.Equals(already, requestedArguments[i]))
            {
                return false;
            }

            inferred[position] = requestedArguments[i];
        }

        foreach (var argument in inferred)
            if (argument is null) return false;

        // Las restricciones son el criterio de seleccion, no una validacion posterior: una
        // candidata que no las satisface simplemente no es candidata, porque puede haber
        // otra que si. Solo cuando ninguna encaja se informa, y entonces interesa saber que
        // restriccion fallo.
        for (var i = 0; i < typeParameters.Length; i++)
        {
            if (!SatisfiesConstraints(typeParameters[i], inferred[i]))
            {
                unsatisfied = typeParameters[i];
                return false;
            }
        }

        closedFactory = template.Factory.Construct(inferred);
        return true;
    }

    /// <summary>
    /// Comprueba las restricciones declaradas de un parametro de tipo contra el argumento
    /// que se le quiere dar.
    /// </summary>
    private static bool SatisfiesConstraints(ITypeParameterSymbol typeParameter, ITypeSymbol argument)
    {
        if (typeParameter.HasReferenceTypeConstraint && !argument.IsReferenceType) return false;

        if (typeParameter.HasValueTypeConstraint
            && (!argument.IsValueType || IsNullableValueType(argument)))
        {
            return false;
        }

        if (typeParameter.HasUnmanagedTypeConstraint && argument is not { IsUnmanagedType: true }) return false;

        if (typeParameter.HasNotNullConstraint && argument.NullableAnnotation is NullableAnnotation.Annotated) return false;

        if (typeParameter.HasConstructorConstraint
            && argument is not INamedTypeSymbol { IsAbstract: false, InstanceConstructors: { } ctors })
        {
            return false;
        }
        else if (typeParameter.HasConstructorConstraint
            && argument is INamedTypeSymbol { InstanceConstructors: { } instanceCtors }
            && !instanceCtors.Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility is Accessibility.Public))
        {
            return false;
        }

        foreach (var constraint in typeParameter.ConstraintTypes)
        {
            // Un constraint que a su vez depende de otro parametro de tipo (`where T : U`)
            // no se verifica aqui: exigiria resolver el orden de inferencia. Se acepta y, si
            // no encaja, el compilador lo dira sobre la llamada construida, que sigue siendo
            // codigo del usuario.
            if (constraint is ITypeParameterSymbol) continue;

            if (!IsAssignableTo(argument, constraint)) return false;
        }

        return true;

        static bool IsNullableValueType(ITypeSymbol type)
            => type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
    }

    /// <summary>Conversion de identidad, herencia o implementacion de interfaz.</summary>
    private static bool IsAssignableTo(ITypeSymbol source, ITypeSymbol target)
    {
        if (SymbolEqualityComparer.Default.Equals(source, target)) return true;

        for (var baseType = source.BaseType; baseType is not null; baseType = baseType.BaseType)
            if (SymbolEqualityComparer.Default.Equals(baseType, target)) return true;

        foreach (var iface in source.AllInterfaces)
            if (SymbolEqualityComparer.Default.Equals(iface, target)) return true;

        return false;
    }

}

internal record struct MarkedParameter(IParameterSymbol Parameter, AttributeArgumentSyntax? Attribute);
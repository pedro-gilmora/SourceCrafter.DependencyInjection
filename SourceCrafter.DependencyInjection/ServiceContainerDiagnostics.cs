using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using System;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("SourceCrafter.DependencyInjection")]
namespace SourceCrafter.DependencyInjection;

internal static class ServiceContainerDiagnostics
{
    internal static Diagnostic DuplicateService(Lifetime lifetime, string? key, AttributeSyntax attrSyntax, string typeName, string exportTypeFullName)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI01",
            title: $"[{lifetime},{exportTypeFullName}, {key}] is already present in this container",
            messageFormat: "'{0}' is duplicate",
            category: "SourceCrafter.DependencyInjection.Usage",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: $"[{lifetime},{exportTypeFullName}, {key}] is already present and it should be removed in order to properly compile the project."
        );

        return Diagnostic.Create(rule, attrSyntax.GetLocation(), typeName);
    }

    internal static Diagnostic PrimitiveDependencyMustBeKeyed(
        Lifetime lifetime,
        SyntaxNode? node,
        string typeName,
        string exportTypeFullName)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI02",
            title: $"[{lifetime}, {exportTypeFullName}] must be keyed",
            messageFormat: "'{0}' should be properly keyed as service to provide multiple primitive value as dependency",
            category: "SourceCrafter.DependencyInjection.Usage",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: $"[{lifetime}, {exportTypeFullName}] should be properly keyed as service to provide multiple primitive value as dependency"
        );

        return Diagnostic.Create(rule, node?.GetLocation(), typeName);
    }

    internal static Diagnostic UnresolvedDependency(
        SyntaxNode invExpr,
        string providerClassName,
        string? typeFullName)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI03",
            title: "Type not registered in container",
            messageFormat: "'{0}' is not registered in [{1}] container",
            category: "SourceCrafter.DependencyInjection.Usage",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Type is not registered in container"
        );

        return Diagnostic.Create(
            rule,
            invExpr.GetLocation(),
            $@"[{typeFullName ?? "[Unknown Type]"}]",
            providerClassName);
    }

    internal static Diagnostic InvalidKeyType(ExpressionSyntax arg)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI05",
            title: "Not valid key type",
            messageFormat: "Invalid key type. Only enum keys are allowed",
            category: "SourceCrafter.DependencyInjection.Usage",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Invalid key type. Only enum keys are allowed"
        );

        return Diagnostic.Create(rule, arg.GetLocation());
    }

    internal static Diagnostic InterfaceRequiresFactory(AttributeSyntax node)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI06",
            title: "Container-internal interface-only resolver requires factory method",
            messageFormat: "Container-internal interface-only resolver requires factory method in order to provide as dependency",
            category: "SourceCrafter.DependencyInjection.Usage",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Please provide a value for [source] parameter"
        );

        return Diagnostic.Create(rule, node.GetLocation());
    }

    internal static Diagnostic DependencyWithUnresolvedParameters(
        SyntaxNode invExpr,
        string providerClassName)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI07",
            title: "Dependency has unresolved types",
            messageFormat: "'{0}' has some unresolved types.",
            category: "SourceCrafter.DependencyInjection.Usage",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Dependency has unresolved types. Make sure to register properly the required types for minimal parameterized constructors"
        );

        return Diagnostic.Create(
            rule,
            invExpr.GetLocation(),
            providerClassName);
    }

    internal static Diagnostic InterfaceWithNoImplementation(
        Location attrLocation,
        ITypeSymbol interfeis,
        string providerClassName,
        Lifetime lifetime)
    {
        var interfaceName = interfeis.NameOnly;
        if(interfeis.TypeKind == TypeKind.Interface) interfaceName = interfaceName.TrimStart('I');
        DiagnosticDescriptor rule = new(
            id: "SCDI08",
            title: "Dependency has unresolved types",
            messageFormat: "Interface {0} has not specified implementation at container {2}.",
            category: "SourceCrafter.DependencyInjection.Definition",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Define or fix a [LifeTime<I{1}, {1}>] as decorator attribute over type {1} definition"
        );

        return Diagnostic.Create(
            rule,
            attrLocation,
            interfeis.GlobalNamespaced,
            providerClassName,
            interfaceName,
            lifetime);
    }

    internal static Diagnostic BaseAndImplementationMissmatch(
        Location attrLocation,
        ITypeSymbol type,
        ITypeSymbol interfeis)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI09",
            title: "Implementation does not derive from the declared service type",
            messageFormat: "Type '{0}' is not an implementation of '{1}'.",
            category: "SourceCrafter.DependencyInjection.Definition",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        return Diagnostic.Create(
            rule,
            attrLocation,
            type.GlobalNamespaced,
            interfeis.GlobalNamespaced);
    }

    //internal static Diagnostic DependencyCallMustBeScoped(string providerName, IdentifierNameSyntax methodNameSyntax)
    //{
    //    DiagnosticDescriptor rule = new(
    //        id: "SCDI09",
    //        title: "Resolver factorySource called on non-scoped instance.",
    //        messageFormat: $"Method [{methodNameSyntax.Identifier.ValueText}] must be called from scoped instance using [{providerName}.CreateScope()].",
    //        category: "SourceCrafter.DependencyInjection.Usage",
    //        defaultSeverity: DiagnosticSeverity.Error,
    //        isEnabledByDefault: true,
    //        description: "Please, just use a CreateScope reference to call the indicated factorySource"
    //    );

    //    return Diagnostic.Create(
    //        rule,
    //        methodNameSyntax.GetLocation());
    //}

    internal static Diagnostic FactoryReturnMismatch(ISymbol factorySource, ITypeSymbol type, ITypeSymbol returnType, AttributeSyntax attrSyntax)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI10",
            title: "Factory return type doesn't match the service type",
            messageFormat: "{0} {1} as return type for method {2}, should match {3} as service base {4}.",
            category: "SourceCrafter.DependencyInjection.Design",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        return Diagnostic.Create(
            rule,
            attrSyntax.GetLocation(),
            returnType.TypeKind,
            returnType.ToDisplayString(),
            factorySource.ToDisplayString(),
            type.ToDisplayString(),
            type.TypeKind is TypeKind.Interface || type.IsAbstract ? "base" : type.Kind.ToString().ToLower());
    }

    internal static Diagnostic UncoveredGenericResolver(Location location, string type, string providerFullTypeName, bool isScopedCall)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI11",
            title: "Generic service resolver support couldn't cover this call",
            messageFormat: "No dependency resolver was found for '{0}' at '{1}' container{2}.",
            category: "SourceCrafter.DependencyInjection.Design",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        return Diagnostic.Create(
            rule,
            location,
            type,
            providerFullTypeName,
            isScopedCall ? " scope" : null);
    }

    internal static Diagnostic ThrowInnerFactorySpecs(string name, Location location)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI12",
            title: "Internal factory must be private and name must have underscore leading (Eg: _Name)",
            messageFormat: "Internal factory '{0}' must be private and name must have underscore leading (Eg: _{0}).",
            category: "SourceCrafter.DependencyInjection.Design",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        return Diagnostic.Create(rule, location, name);
    }

    internal static Diagnostic AmbiguousContainerForCall(Location location, string methodName, string containerTypeFullName)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI13",
            title: "Ambiguous container for interceptable call",
            messageFormat: "More than one service container can resolve '{0}' on '{1}'. Call it on the concrete container type so the generator can pick one.",
            category: "SourceCrafter.DependencyInjection.Usage",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Two or more generated containers matched the same call location, which would emit duplicated [InterceptsLocation] attributes."
        );

        return Diagnostic.Create(rule, location, methodName, containerTypeFullName);
    }

    internal static Diagnostic InvalidAsyncTypeArgument(Location location, AsyncKind AsyncKind, string methodName)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI14",
            title: "IServiceProvider-like method must not use Task<T> or ValueTask<T> as generic argument",
            messageFormat: "IServiceProvider-like '{0}' method must not use {1}<T> as generic argument.",
            category: "SourceCrafter.DependencyInjection.Design",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        return Diagnostic.Create(rule, location, methodName, AsyncKind);
    }

    internal static Diagnostic ConflictingParameterlessConstructor(Location location, string containerClassName)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI15",
            title: "Service container declares a parameterless constructor",
            messageFormat: "'{0}' declares a parameterless constructor, which collides with the one the generator emits to initialize its cancellation source.",
            category: "SourceCrafter.DependencyInjection.Design",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Move the initialization logic to a field initializer or to a constructor taking parameters."
        );

        return Diagnostic.Create(rule, location, containerClassName);
    }

    /// <summary>
    /// Una fabrica asincrona debe declarar exactamente el tipo expuesto por el servicio.
    /// <para>
    /// No es una limitacion del generador sino de la plataforma: <c>Task&lt;T&gt;</c> y
    /// <c>ValueTask&lt;T&gt;</c> son <b>invariantes</b>, asi que un <c>Task&lt;Impl&gt;</c> no
    /// se convierte a <c>Task&lt;IService&gt;</c> aunque <c>Impl</c> implemente
    /// <c>IService</c>. Sin este diagnostico la incompatibilidad no se detecta al analizar y
    /// reaparece como un CS0029 dentro de codigo generado, que es donde peor se lee.
    /// </para>
    /// <para>
    /// Salvarlo desde la generacion exigiria esperar y reenvolver el resultado, es decir una
    /// maquina de estados o una asignacion extra por resolucion. Cambiar el tipo declarado de
    /// la fabrica no cuesta nada y deja el codigo emitido como un paso directo.
    /// </para>
    /// </summary>
    internal static Diagnostic AsyncFactoryMustDeclareServiceType(
        Location location,
        string factoryName,
        string asyncTypeName,
        string factoryTypeArgument,
        string serviceTypeName)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI16",
            title: "Async factory must declare the service type as its task argument",
            messageFormat: "Async factory '{0}' returns '{1}<{2}>' but the service is exposed as '{3}'. Declare it as '{1}<{3}>'.",
            category: "SourceCrafter.DependencyInjection.Design",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Task<T> and ValueTask<T> are invariant, so Task<Implementation> is not convertible to Task<IService>. Declaring the exposed type on the factory keeps the generated resolver a direct pass-through instead of an extra await-and-rewrap."
        );

        return Diagnostic.Create(rule, location, factoryName, asyncTypeName, factoryTypeArgument, serviceTypeName);
    }

    /// <summary>
    /// Dos parametros del mismo tipo de servicio sin forma de distinguirlos.
    /// <para>
    /// Se permite <b>uno</b> sin clave; a partir del segundo hay que desambiguar, bien
    /// nombrando el parametro igual que una clave registrada, bien anotandolo con el atributo
    /// de lifetime y clave que corresponda. Antes este caso no se diagnosticaba: los dos
    /// parametros recibian en silencio el mismo servicio, o se emitia una referencia a un
    /// local nunca declarado y salia un CS0103 dentro del codigo generado.
    /// </para>
    /// </summary>
    internal static Diagnostic AmbiguousUnkeyedParameters(
        Location location,
        string parameterName,
        string serviceTypeName,
        string firstParameterName)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI17",
            title: "Ambiguous parameters of the same service type",
            messageFormat: "Parameter '{0}' and '{2}' both resolve '{1}' with no key. Name '{0}' after a registered key, or annotate it with the matching lifetime attribute and key.",
            category: "SourceCrafter.DependencyInjection.Design",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Only one parameter of a given service type may go unkeyed. Any further parameter of the same type must be disambiguated, otherwise the generator cannot tell which registration each one wants."
        );

        return Diagnostic.Create(rule, location, parameterName, serviceTypeName, firstParameterName);
    }

    /// <summary>
    /// <c>LockOptions.Global</c> declara un candado <c>static</c> del contenedor, compartido
    /// por todas sus instancias. Un servicio <c>Scoped</c> tiene un campo de respaldo por
    /// ambito, asi que vigilarlo con un candado global serializaria ambitos independientes
    /// sin aportar exclusion adicional: el alcance del candado debe coincidir con el del
    /// campo. El valor correcto es <c>Instance</c> (el predeterminado) o <c>Dedicated</c>.
    /// </summary>
    internal static Diagnostic GlobalLockNotAllowedForScoped(Location location)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI18",
            title: "Global lock is not compatible with scoped dependencies",
            messageFormat: "LockOptions.Global cannot be used on a scoped dependency. Use LockOptions.Instance or LockOptions.Dedicated.",
            category: "SourceCrafter.DependencyInjection.Usage",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "A scoped dependency is backed by a per-scope field, so a container-wide static lock would serialize unrelated scopes without adding exclusion. The lock scope must match the field scope."
        );

        return Diagnostic.Create(rule, location);
    }

    /// <summary>
    /// Una fabrica generica debe restringir cada uno de sus parametros de tipo.
    /// <para>
    /// Sin restricciones, <c>T</c> es cualquier cosa: el emparejado no puede descartar
    /// candidatas y la fabrica se vuelve aplicable a todo servicio construido del mismo nombre
    /// generico, que es justo lo que hace imposible decidir cual usar. La restriccion no es
    /// decorativa, es el criterio con el que el generador elige.
    /// </para>
    /// <para>
    /// Basta con una: un tipo base, una interfaz, <c>class</c> o <c>struct</c>.
    /// </para>
    /// </summary>
    internal static Diagnostic GenericFactoryTypeParameterNeedsConstraint(
        Location location,
        string factoryName,
        string typeParameterName)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI19",
            title: "Generic factory type parameter must be constrained",
            messageFormat: "Type parameter '{1}' of generic factory '{0}' has no constraints. Add at least a base type, an interface, 'class' or 'struct'.",
            category: "SourceCrafter.DependencyInjection.Design",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Constraints are what the generator matches against when choosing a generic factory. An unconstrained type parameter makes the factory applicable to every constructed type, which removes any basis for picking one candidate over another."
        );

        return Diagnostic.Create(rule, location, factoryName, typeParameterName);
    }

    /// <summary>
    /// Se pidio un tipo construido que ninguna fabrica generica registrada puede producir,
    /// porque ninguna de sus restricciones lo admite.
    /// <para>
    /// Se informa el tipo pedido y la restriccion incumplida de la candidata mas cercana, que
    /// es casi siempre lo que hay que corregir. Sin esto el fallo aparece como un servicio no
    /// registrado, que es un mensaje cierto pero inutil: la fabrica existe, simplemente no
    /// acepta ese argumento.
    /// </para>
    /// </summary>
    internal static Diagnostic NoGenericFactorySatisfiesType(
        Location location,
        string requestedTypeName,
        string factoryName,
        string unsatisfiedConstraint)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI20",
            title: "No generic factory accepts the requested type argument",
            messageFormat: "No registered generic factory can produce '{0}'. The closest candidate '{1}' requires '{2}'.",
            category: "SourceCrafter.DependencyInjection.Design",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "The generic factory is registered but its constraints exclude the requested type argument. Either widen the constraint or register a concrete service for that constructed type."
        );

        return Diagnostic.Create(rule, location, requestedTypeName, factoryName, unsatisfiedConstraint);
    }

    /// <summary>
    /// Dos fabricas genericas pueden producir el mismo tipo construido y ninguna es mas
    /// especifica que la otra.
    /// <para>
    /// <b>La salida es la clave, no un orden de desempate.</b> Inventar una regla de
    /// precedencia -- la declarada primero, la del ensamblado actual -- resolveria el caso a
    /// costa de que el servicio elegido dependa de algo que no se lee en el sitio de registro.
    /// Una clave es explicita y ya es el mecanismo de desambiguacion del resto del generador,
    /// asi que no añade vocabulario nuevo.
    /// </para>
    /// </summary>
    internal static Diagnostic AmbiguousGenericFactories(
        Location location,
        string requestedTypeName,
        string firstFactoryName,
        string secondFactoryName)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI21",
            title: "Ambiguous generic factories for the same constructed type",
            messageFormat: "Generic factories '{1}' and '{2}' both produce '{0}' and neither is more specific. Give them distinct keys and request the service by key.",
            category: "SourceCrafter.DependencyInjection.Design",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "When two generic factories match equally well, the generator does not guess. Keys are the existing disambiguation mechanism and make the choice explicit at the registration site instead of depending on declaration order."
        );

        return Diagnostic.Create(rule, location, requestedTypeName, firstFactoryName, secondFactoryName);
    }

    /// <summary>
    /// Una fabrica generica solo puede registrarse como <c>Transient</c>.
    /// <para>
    /// Un lifetime cacheado necesita un campo de respaldo <b>por tipo construido</b>, y ese
    /// conjunto no se conoce al registrar la fabrica sino al recorrer a sus consumidores. Con
    /// pocos tipos el coste es un campo por cada uno; con una familia amplia es una superficie
    /// de memoria que crece sin que se vea en el sitio de registro.
    /// </para>
    /// <para>
    /// Si un tipo construido concreto necesita cachearse, registrarlo por separado con su
    /// lifetime: ahi el campo es uno, explicito y visible.
    /// </para>
    /// </summary>
    internal static Diagnostic GenericFactoryMustBeTransient(
        Location location,
        string factoryName,
        string lifetimeName)
    {
        DiagnosticDescriptor rule = new(
            id: "SCDI22",
            title: "Generic factories can only be registered as transient",
            messageFormat: "Generic factory '{0}' cannot be registered as '{1}'. Use Transient, or register the specific constructed type separately if it needs caching.",
            category: "SourceCrafter.DependencyInjection.Design",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "A cached lifetime needs one backing field per constructed type, and that set is only known from the consumers rather than from the registration. Keeping generic factories transient prevents a memory footprint that is invisible at the registration site."
        );

        return Diagnostic.Create(rule, location, factoryName, lifetimeName);
    }
}

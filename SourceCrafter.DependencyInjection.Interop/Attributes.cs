#pragma warning disable CS9113

using SourceCrafter.DependencyInjection.Interop;

using System;

namespace SourceCrafter.DependencyInjection.Attributes
{
    [global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Interface | global::System.AttributeTargets.Parameter, AllowMultiple = true)]
    public class ServiceContainerAttribute : Attribute;

    [global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Interface, AllowMultiple = true)]
    public class SingletonAttribute<TImplementation>(object? key = null, string? factoryOrInstance = null, bool cache = true) : SingletonAttribute(key, typeof(TImplementation), null, factoryOrInstance, cache);

    [global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Interface, AllowMultiple = true)]
    public class SingletonAttribute<T, TImplementation>(object? key = null, string? factoryOrInstance = null, bool cache = true) : SingletonAttribute(key, typeof(TImplementation), typeof(T), factoryOrInstance, cache);

    [global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Interface | global::System.AttributeTargets.Parameter, AllowMultiple = true)]
    public class SingletonAttribute(object? key = null, global::System.Type? impl = null, global::System.Type? iface = null, string? factoryOrInstance = null, bool cache = true) : Attribute;

    [global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Interface, AllowMultiple = true)]
    public class ScopedAttribute<TImplementation>(object? key = null, string? factoryOrInstance = null, bool cache = true) : SingletonAttribute(key, typeof(TImplementation), null, factoryOrInstance, cache);

    [global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Interface, AllowMultiple = true)]
    public class ScopedAttribute<T, TImplementation>(object? key = null, string? factoryOrInstance = null, bool cache = true) : SingletonAttribute(key, typeof(TImplementation), typeof(T), factoryOrInstance, cache);

    [global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Interface | global::System.AttributeTargets.Parameter, AllowMultiple = true)]
    public class ScopedAttribute(object? key = null, global::System.Type? impl = null, global::System.Type? iface = null, string? factoryOrInstance = null, bool cache = true) : Attribute;

    [global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Interface, AllowMultiple = true)]
    public class TransientAttribute<TImplementation>(object? key = null, string? factoryOrInstance = null, bool cache = true) : SingletonAttribute(key, typeof(TImplementation), null, factoryOrInstance, cache);

    [global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Interface, AllowMultiple = true)]
    public class TransientAttribute<T, TImplementation>(object? key = null, string? factoryOrInstance = null, bool cache = true) : SingletonAttribute(key, typeof(TImplementation), typeof(T), factoryOrInstance, cache);

    [global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Interface | global::System.AttributeTargets.Parameter, AllowMultiple = true)]
    public class TransientAttribute(object? key = null, global::System.Type? impl = null, global::System.Type? iface = null, string? factoryOrInstance = null, bool cache = true) : Attribute;

    [global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Interface | global::System.AttributeTargets.Parameter, AllowMultiple = true)]
    public class DependencyAttribute(Lifetime lifetime, object? key = null, global::System.Type? impl = null, global::System.Type? iface = null, string? factoryOrInstance = null, bool cache = true) : Attribute;
}

namespace SourceCrafter.DependencyInjection
{
    public interface IDependencyResolver
    {

    }
}


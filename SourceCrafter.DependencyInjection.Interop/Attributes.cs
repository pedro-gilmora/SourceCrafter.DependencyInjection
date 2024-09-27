#pragma warning disable CS9113

using SourceCrafter.DependencyInjection.Interop;

using System;

namespace SourceCrafter.DependencyInjection.Attributes
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Parameter, AllowMultiple = true)]
    public class ServiceContainerAttribute : Attribute;

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true)]
    public class SingletonAttribute<TImplementation>(string? key = null, string? factoryOrInstance = null, bool cache = true, string? nameFormat = null) : SingletonAttribute(key, typeof(TImplementation), null, factoryOrInstance, cache, nameFormat);

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true)]
    public class SingletonAttribute<T, TImplementation>(string? key = null, string? factoryOrInstance = null, bool cache = true, string? nameFormat = null) : SingletonAttribute(key, typeof(TImplementation), typeof(T), factoryOrInstance, cache, nameFormat);

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Parameter, AllowMultiple = true)]
    public class SingletonAttribute(string? key = null, Type? impl = null, Type? iface = null, string? factoryOrInstance = null, bool cache = true, string? nameFormat = null) : Attribute;

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true)]
    public class ScopedAttribute<TImplementation>(string? key = null, string? factoryOrInstance = null, bool cache = true, string? nameFormat = null) : ScopedAttribute(key, typeof(TImplementation), null, factoryOrInstance, cache, nameFormat);

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true)]
    public class ScopedAttribute<T, TImplementation>(string? key = null, string? factoryOrInstance = null, bool cache = true, string? nameFormat = null) : ScopedAttribute(key, typeof(TImplementation), typeof(T), factoryOrInstance, cache, nameFormat);

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Parameter, AllowMultiple = true)]
    public class ScopedAttribute(string? key = null, Type? impl = null, Type? iface = null, string? factoryOrInstance = null, bool cache = true, string? nameFormat = null) : Attribute;

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true)]
    public class TransientAttribute<TImplementation>(string? key = null, string? factoryOrInstance = null, bool cache = true, string? nameFormat = null) : TransientAttribute(key, typeof(TImplementation), null, factoryOrInstance, cache, nameFormat);

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true)]
    public class TransientAttribute<T, TImplementation>(string? key = null, string? factoryOrInstance = null, bool cache = true, string? nameFormat = null) : TransientAttribute(key, typeof(TImplementation), typeof(T), factoryOrInstance, cache, nameFormat);

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Parameter, AllowMultiple = true)]
    public class TransientAttribute(string? key = null, Type? impl = null, Type? iface = null, string? factoryOrInstance = null, bool cache = true, string? nameFormat = null) : Attribute;
    public abstract class DependencyAttribute(Lifetime lifetime, string? key = null, string? factoryOrInstance = null, bool cache = true, string? nameFormat = null) : Attribute;
}


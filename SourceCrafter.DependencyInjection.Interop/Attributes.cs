#pragma warning disable CS9113

using SourceCrafter.DependencyInjection.Interop;

using System;

namespace SourceCrafter.DependencyInjection.Attributes
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct, AllowMultiple = true)]
    public class ServiceContainerAttribute : Attribute;

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct | AttributeTargets.Parameter, AllowMultiple = true)]
    public class SingletonAttribute<TImplementation>(string key = "", string? factoryOrInstance = null, string? nameFormat = null, Disposability disposability = Disposability.None) : Attribute;

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct, AllowMultiple = true)]
    public class SingletonAttribute<T, TImplementation>(string key = "", string? factoryOrInstance = null, string? nameFormat = null, Disposability disposability = Disposability.None) : Attribute;

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct | AttributeTargets.Parameter, AllowMultiple = true)]
    public class SingletonAttribute(string key = "", Type? impl = null, Type? iface = null, string? factoryOrInstance = null, string? nameFormat = null, Disposability disposability = Disposability.None) : Attribute;

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct | AttributeTargets.Parameter, AllowMultiple = true)]
    public class ScopedAttribute<TImplementation>(string key = "", string? factoryOrInstance = null, string? nameFormat = null, Disposability disposability = Disposability.None) : Attribute;

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct, AllowMultiple = true)]
    public class ScopedAttribute<T, TImplementation>(string key = "", string? factoryOrInstance = null, string? nameFormat = null, Disposability disposability = Disposability.None) : Attribute;

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct | AttributeTargets.Parameter, AllowMultiple = true)]
    public class ScopedAttribute(string key = "", Type? impl = null, Type? iface = null, string? factoryOrInstance = null, string? nameFormat = null, Disposability disposability = Disposability.None) : Attribute;

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct | AttributeTargets.Parameter, AllowMultiple = true)]
    public class TransientAttribute<TImplementation>(string key = "", string? factoryOrInstance = null, string? nameFormat = null, Disposability disposability = Disposability.None) : Attribute;

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct, AllowMultiple = true)]
    public class TransientAttribute<T, TImplementation>(string key = "", string? factoryOrInstance = null, string? nameFormat = null, Disposability disposability = Disposability.None) : Attribute;

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct | AttributeTargets.Parameter, AllowMultiple = true)]
    public class TransientAttribute(string key = "", Type? impl = null, Type? iface = null, string? factoryOrInstance = null, string? nameFormat = null, Disposability disposability = Disposability.None) : Attribute;
 
    public abstract class DependencyAttribute(Lifetime lifetime, string key = "", string? factoryOrInstance = null, string? nameFormat = null, Disposability disposability = Disposability.None) : Attribute;
}


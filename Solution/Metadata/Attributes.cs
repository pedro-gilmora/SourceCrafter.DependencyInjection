#pragma warning disable CS9113
using System;

namespace SourceCrafter.DependencyInjection
{
    internal enum Lifetime : byte { Singleton, Scoped, Transient }
    internal enum Disposability : byte { None, Disposable, AsyncDisposable }

    namespace Attributes
    {
        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct, AllowMultiple = true)]
        internal class ServiceContainerAttribute : Attribute;

        [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct | AttributeTargets.Parameter, AllowMultiple = true)]
        internal class SingletonAttribute<TImplementation>(string key = "", string? source = null, string? nameFormat = null, Disposability disposability = Disposability.None) : Attribute;

        [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct, AllowMultiple = true)]
        internal class SingletonAttribute<T, TImplementation>(string key = "", string? source = null, string? nameFormat = null, Disposability disposability = Disposability.None) : Attribute;

        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct | AttributeTargets.Parameter, AllowMultiple = true)]
        internal class SingletonAttribute(string key = "", Type? impl = null, Type? iface = null, string? source = null, string? nameFormat = null, Disposability disposability = Disposability.None) : Attribute;

        [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct | AttributeTargets.Parameter, AllowMultiple = true)]
        internal class ScopedAttribute<TImplementation>(string key = "", string? source = null, string? nameFormat = null, Disposability disposability = Disposability.None) : Attribute;

        [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct, AllowMultiple = true)]
        internal class ScopedAttribute<T, TImplementation>(string key = "", string? source = null, string? nameFormat = null, Disposability disposability = Disposability.None) : Attribute;

        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct | AttributeTargets.Parameter, AllowMultiple = true)]
        internal class ScopedAttribute(string key = "", Type? impl = null, Type? iface = null, string? source = null, string? nameFormat = null, Disposability disposability = Disposability.None) : Attribute;

        [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct | AttributeTargets.Parameter, AllowMultiple = true)]
        internal class TransientAttribute<TImplementation>(string key = "", string? source = null, string? nameFormat = null, Disposability disposability = Disposability.None) : Attribute;

        [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct, AllowMultiple = true)]
        internal class TransientAttribute<T, TImplementation>(string key = "", string? source = null, string? nameFormat = null, Disposability disposability = Disposability.None) : Attribute;

        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct | AttributeTargets.Parameter, AllowMultiple = true)]
        internal class TransientAttribute(string key = "", Type? impl = null, Type? iface = null, string? source = null, string? nameFormat = null, Disposability disposability = Disposability.None) : Attribute;

        internal abstract class DependencyAttribute(Lifetime lifetime, string key = "", string? source = null, string? nameFormat = null, Disposability disposability = Disposability.None) : Attribute;
    }
}
#pragma warning disable CS9113
using SourceCrafter.DependencyInjection.Constants;
using System;
using System.Diagnostics;

namespace SourceCrafter.DependencyInjection
{
    namespace Attributes
    {
        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct, AllowMultiple = false)]
#if DISG_META
        public
#else
        internal 
#endif
        class ServiceContainerAttribute(string envName = "DOTNET_ENVIRONMENT") : Attribute;

        [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = true)]
#if DISG_META
        public
#else
        internal 
#endif
        class RootAttribute : Attribute;

        [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct | AttributeTargets.Parameter, AllowMultiple = true)]
#if DISG_META
        public
#else
        internal 
#endif
        class SingletonAttribute<TImplementation>(string key = "", string? source = null, string? nameFormat = null, Disposability disposability = Disposability.None) : Attribute;

        [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct, AllowMultiple = true)]
#if DISG_META
        public
#else
        internal 
#endif
        class SingletonAttribute<T, TImplementation>(string key = "", string? source = null, string? nameFormat = null, Disposability disposability = Disposability.None) : Attribute;

        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct | AttributeTargets.Parameter, AllowMultiple = true)]
#if DISG_META
        public
#else
        internal 
#endif
        class SingletonAttribute(string key = "", Type? impl = null, Type? iface = null, string? source = null, string? nameFormat = null, Disposability disposability = Disposability.None) : Attribute;

        [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct | AttributeTargets.Parameter, AllowMultiple = true)]
#if DISG_META
        public
#else
        internal 
#endif
        class ScopedAttribute<TImplementation>(string key = "", string? source = null, string? nameFormat = null, Disposability disposability = Disposability.None) : Attribute;

        [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct, AllowMultiple = true)]
#if DISG_META
        public
#else
        internal 
#endif
        class ScopedAttribute<T, TImplementation>(string key = "", string? source = null, string? nameFormat = null, Disposability disposability = Disposability.None) : Attribute;

        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct | AttributeTargets.Parameter, AllowMultiple = true)]
#if DISG_META
        public
#else
        internal 
#endif
        class ScopedAttribute(string key = "", Type? impl = null, Type? iface = null, string? source = null, string? nameFormat = null, Disposability disposability = Disposability.None) : Attribute;

        [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct | AttributeTargets.Parameter, AllowMultiple = true)]
#if DISG_META
        public
#else
        internal 
#endif
            class TransientAttribute<TImplementation>(string key = "", string? source = null, string? nameFormat = null, Disposability disposability = Disposability.None) : Attribute;

        [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct, AllowMultiple = true)]
#if DISG_META
        public
#else
        internal 
#endif
        class TransientAttribute<T, TImplementation>(string key = "", string? source = null, string? nameFormat = null, Disposability disposability = Disposability.None) : Attribute;

        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct | AttributeTargets.Parameter, AllowMultiple = true)]
#if DISG_META
        public
#else
        internal 
#endif
        class TransientAttribute(string key = "", Type? impl = null, Type? iface = null, string? source = null, string? nameFormat = null, Disposability disposability = Disposability.None) : Attribute;

#if DISG_META
        public
#else
        internal
#endif
        abstract class DependencyAttribute(Lifetime lifetime, string key = "", string? source = null, string? nameFormat = null, Disposability disposability = Disposability.None) : Attribute;
    }
}
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
        class ServiceProviderAttribute(string envName = "DOTNET_ENVIRONMENT", bool genericApi = false, bool exportTransients = false) : Attribute;

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
        class SingletonAttribute<TImplementation>(string key = "", string? source = null, string? nameFormat = null, LockOptions locks = LockOptions.Default) : Attribute;

        [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct, AllowMultiple = true)]
#if DISG_META
        public
#else
        internal 
#endif
        class SingletonAttribute<T, TImplementation>(string key = "", string? source = null, string? nameFormat = null, LockOptions locks = LockOptions.Default) : Attribute where TImplementation : T where T : notnull;

        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct | AttributeTargets.Parameter, AllowMultiple = true)]
#if DISG_META
        public
#else
        internal 
#endif
        class SingletonAttribute(string key = "", Type? impl = null, Type? iface = null, string? source = null, string? nameFormat = null, LockOptions locks = LockOptions.Default) : Attribute;

        [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct | AttributeTargets.Parameter, AllowMultiple = true)]
#if DISG_META
        public
#else
        internal 
#endif
        class ScopedAttribute<TImplementation>(string key = "", string? source = null, string? nameFormat = null, LockOptions locks = LockOptions.Default) : Attribute;

        [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct, AllowMultiple = true)]
#if DISG_META
        public
#else
        internal 
#endif
        class ScopedAttribute<T, TImplementation>(string key = "", string? source = null, string? nameFormat = null, LockOptions locks = LockOptions.Default) : Attribute where TImplementation : T where T : notnull;

        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct | AttributeTargets.Parameter, AllowMultiple = true)]
#if DISG_META
        public
#else
        internal 
#endif
        class ScopedAttribute(string key = "", Type? impl = null, Type? iface = null, string? source = null, string? nameFormat = null, LockOptions locks = LockOptions.Default) : Attribute;

        [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct | AttributeTargets.Parameter, AllowMultiple = true)]
#if DISG_META
        public
#else
        internal 
#endif
            class TransientAttribute<TImplementation>(string key = "", string? source = null, string? nameFormat = null) : Attribute;

        [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct, AllowMultiple = true)]
#if DISG_META
        public
#else
        internal 
#endif
        class TransientAttribute<T, TImplementation>(string key = "", string? source = null, string? nameFormat = null) : Attribute where TImplementation : T where T : notnull;

        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct | AttributeTargets.Parameter, AllowMultiple = true)]
#if DISG_META
        public
#else
        internal 
#endif
        class TransientAttribute(string key = "", Type? impl = null, Type? iface = null, string? source = null, string? nameFormat = null) : Attribute;

#if DISG_META
        public
#else
        internal
#endif
        abstract class DependencyAttribute(Lifetime lifetime, string key = "", string? source = null, string? nameFormat = null, LockOptions locks = LockOptions.Default) : Attribute;

        /// <summary>
        /// <typeparamref name="T"/> pertenece al host que aloja el contenedor: se pide a <c>HostServices</c> en cada resolucion.
        /// El contenedor no lo construye, no lo cachea (ya lo hace el host) ni lo libera.
        /// <see cref="Lifetime.Scoped"/>: se pide a un ambito del host que cada ambito del contenedor abre al usarlo y libera con el.
        /// </summary>
        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct, AllowMultiple = true)]
#if DISG_META
        public
#else
        internal
#endif
        class HostedAttribute<T>(Lifetime lifetime = Lifetime.Singleton) : Attribute where T : notnull;
    }
}
using SourceCrafter.DependencyInjection.Attributes;

using System;

namespace SourceCrafter.DependencyInjection.MsConfiguration.Metadata
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct | AttributeTargets.Assembly, AllowMultiple = false)]
#pragma warning disable CS9113 // Parameter is unread.

#if DISG_MSCONF_META
    public
#else
    internal
#endif
    sealed class JsonConfigurationAttribute(
        string fileName = "appsettings",
        string key = "",
        bool optional = true,
        bool reloadOnChange = true,
        string nameFormat = "Get{0}Configuration",
        bool handleEnviroments = true,
        Disposability disposability = Disposability.Disposable
    ) : Attribute;
    //: SingletonAttribute<IConfiguration>(source: nameof(ConfigurationResolver.GetJsonConfiguration));

    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]

#if DISG_MSCONF_META
    public
#else
    internal
#endif
    sealed class JsonSettingAttribute(
        string path,
        Lifetime lifetime = Lifetime.Singleton,
        //DI service key
        string key = "",
        string nameFormat = "Get{0}Settings",
        string configKey = "",
        bool nullable = false
    ) : Attribute;

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct | AttributeTargets.Parameter, AllowMultiple = true)]
    
#if DISG_MSCONF_META
    public
#else
    internal 
#endif
    sealed class JsonSettingAttribute<T>(
        string path,
        Lifetime lifetime = Lifetime.Singleton,
        //DI service key
        string key = "",
        string nameFormat = "Get{0}Settings",
        string configKey = "",
        bool nullable = false
    ) : Attribute;
#pragma warning restore CS9113
}

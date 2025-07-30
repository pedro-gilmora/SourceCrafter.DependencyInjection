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
        string key = "",
        string nameFormat = "Get{0}Settings",
        string configKey = ""
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
        string key = "",
        string nameFormat = "Get{0}Settings",
        string configKey = ""
    ) : Attribute;
#pragma warning restore CS9113 // Parameter is unread.

    //public class ConfigurationResolver
    //{
    //    static readonly Map<string, IConfiguration> configurations = new(StringComparer.Ordinal);
    //    static readonly object _locker = new();

    //    public static IConfiguration GetJsonConfiguration(
    //        IHostEnvironment env,
    //        string filePath,
    //        bool optional,
    //        bool reloadOnChange)
    //    {
    //        filePath = Path.GetFullPath(filePath);

    //        ref var existingOrNew = ref configurations.GetValueOrAddDefault(filePath, out var exists);

    //        if (exists) return existingOrNew!;

    //        lock (_locker)
    //        {
    //            return existingOrNew ??= new ConfigurationBuilder()
    //                .AddJsonFile($"{filePath}.{env.EnvironmentName}.json", optional, reloadOnChange)
    //                .AddJsonFile(filePath, optional, reloadOnChange)
    //                .Build();
    //        }
    //    }

    //    public static TSetting GetJsonSetting<TSetting>(string key, IConfiguration? config = null) where TSetting : new()
    //    {
    //        TSetting inst = new();

    //        config?.Bind(key, inst);

    //        return inst;
    //    }
    //}
}

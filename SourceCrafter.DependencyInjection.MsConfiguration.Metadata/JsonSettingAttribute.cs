using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

using SourceCrafter.DependencyInjection.Attributes;
using SourceCrafter.DependencyInjection.Interop;
using SourceCrafter.DependencyInjection.MsConfiguration.Metadata;

using System;
using System.IO;

[assembly: Use<JsonSettingAttribute>]

namespace SourceCrafter.DependencyInjection.MsConfiguration.Metadata
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Assembly, AllowMultiple = false)]
#pragma warning disable CS9113 // Parameter is unread.
    public sealed class JsonConfigurationAttribute(
        string fileName = "appsettings",
        string key = "",
        bool optional = true,
        bool reloadOnChange = true,
        string nameFormat = "Get{0}Configuration",
        bool handleEnviroments = true
    )
        : SingletonAttribute<IConfiguration>(nameFormat);
    //: SingletonAttribute<IConfiguration>(factoryOrInstance: nameof(ConfigurationResolver.GetJsonConfiguration));

    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
    public sealed class JsonSettingAttribute(
        string path, 
        Lifetime lifetime = Lifetime.Singleton,
        string key = "",
        string nameFormat = "Get{0}Settings",
        string configKey = ""
    ) 
        : DependencyAttribute(lifetime, nameFormat);
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

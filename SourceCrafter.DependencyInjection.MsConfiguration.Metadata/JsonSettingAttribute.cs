using Microsoft.Extensions.Configuration;

using SourceCrafter.DependencyInjection.Interop;

using System;
using System.IO;

[assembly: DependencyResolver<SourceCrafter.DependencyInjection.MsConfiguration.Metadata.ConfigurationResolver>]

namespace SourceCrafter.DependencyInjection.MsConfiguration.Metadata
{
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
#pragma warning disable CS9113 // Parameter is unread.
    public sealed class JsonSettingAttribute(
        string key, 
        string filePath = "appsettings.json",
        bool optional = true,
        bool reloadOnChange = true) : Attribute;
#pragma warning restore CS9113 // Parameter is unread.

    public class ConfigurationResolver
    {
        static readonly Map<string, IConfiguration> configurations = new(StringComparer.Ordinal);
        static readonly object _locker = new();
        public static IConfiguration GetJsonConfiguration(
            string filePath = "appsettings.json",
            bool optional = true,
            bool reloadOnChange = true)
        {
            filePath = Path.GetFullPath(filePath);

            ref var existingOrNew = ref configurations.GetValueOrAddDefault(filePath, out var exists);

            if (exists && existingOrNew != null) return existingOrNew;

            lock (_locker)
            {
                return existingOrNew ??= new ConfigurationBuilder()
                    .AddJsonFile(filePath, optional, reloadOnChange)
                    .Build();
            }
        }
    }
}

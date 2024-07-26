using System;

namespace SourceCrafter.DependencyInjection.MsConfiguration.Metadata
{
#pragma warning disable CS9113 // Parameter is unread.
    public sealed class SettingAttribute(string key): Attribute;
#pragma warning restore CS9113 // Parameter is unread.
}

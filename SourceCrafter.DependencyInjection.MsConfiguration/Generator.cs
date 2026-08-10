using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using SourceCrafter.DependencyInjection;
using SourceCrafter.DependencyInjection.Attributes;
using SourceCrafter.DependencyInjection.Constants;
using SourceCrafter.DependencyInjection.MsConfiguration.Metadata;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Xml.Linq;

namespace Containers.Configuration;

[Generator(LanguageNames.CSharp)]
public sealed class Partials : IIncrementalGenerator
{
    private const string IConfigurationType = "global::Microsoft.Extensions.Configuration.IConfiguration";
    private const string FullyQualifiedJsonConfigMetaName = "global::SourceCrafter.DependencyInjection.MsConfiguration.Metadata.JsonConfigurationAttribute";
    private const string FullyQualifiedJsonSettingMetaName = "global::SourceCrafter.DependencyInjection.MsConfiguration.Metadata.JsonSettingAttribute";
    //private const string FullyQualifiedJsonSettingMetaName = "SourceCrafter.DependencyInjection.MsConfiguration.Metadata.JsonSettingAttribute";
    internal readonly static string generatedCodeAttribute = ParseToolAndVersion();

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
#if DEBUG_SG
        //System.Diagnostics.Debugger.Launch();
#endif

        var compilation = context.CompilationProvider.Select((c, t) => new CompilationMeta(IsMsConfigInstalled(c), new Dictionary<string, ContainerConfigPartialEmmiter>(StringComparer.OrdinalIgnoreCase)));

        var servicesContainers = context.SyntaxProvider
            .ForAttributeWithMetadataName("SourceCrafter.DependencyInjection.Attributes.ServiceContainerAttribute",
                (node, a) => true,
                (t, c) =>
                {
                    if (!IsMsConfigInstalled(t.SemanticModel.Compilation)) return null!;

                    ContainerConfigPartialEmmiter emitter = null!;
                    List<SettingsMeta> settings = [];
                    var model = t.SemanticModel;
                    var cls = (INamedTypeSymbol)t.TargetSymbol;
                    Map<string, string> methods = new(StringComparer.Ordinal);

                    HashSet<string> keys = new(StringComparer.Ordinal);

                    foreach (var attr in cls.GetAttributes())
                    {
                        switch (attr.AttributeClass?.ToGlobalNonGenericNamespace())
                        {
                            case FullyQualifiedJsonConfigMetaName:
                                if (attr.ConstructorArguments[0].Value is string { Length: > 0 } fileName)
                                {
                                    var key = (attr.ConstructorArguments[1].Value?.ToString() ?? "").Trim();
                                    ref var configMethodName = ref methods.GetValueRefOrAddDefault(key, out var fileExists);

                                    if (fileExists) continue;

                                    var nameFormat = (string)attr.ConstructorArguments[4].Value!;

                                    configMethodName = nameFormat.Replace("{0}", key).RemoveDuplicates();

                                    var fieldName = configMethodName.Camelize();
                                    var optional = attr.ConstructorArguments[2].Value?.ToString()?.ToLower();
                                    var reloadOnChange = attr.ConstructorArguments[3].Value?.ToString()?.ToLower();
                                    var handleEnviroments = (bool)attr.ConstructorArguments[5].Value!;
                                    var containerTypeName = cls.ToGlobalNamespaced();
                                    var nameSpace = cls.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : null;

                                    var (modifiers, typeName) = cls.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() switch
                                    {
                                        ClassDeclarationSyntax { Modifiers: var mods, Keyword: { } keyword, Identifier: { } identifier2, TypeParameterList: var argList } =>
                                            ($"{mods} {keyword}".TrimStart(), $"{identifier2}{argList}"),
                                        InterfaceDeclarationSyntax { Modifiers: var mods, Keyword: { } keyword, Identifier: { } identifier2, TypeParameterList: var argList } =>
                                            ($"{mods} partial class".TrimStart(), $"{identifier2.ValueText[1..]}{argList}"),
                                        _ => ("", "")
                                    };

                                    emitter = new(nameSpace, containerTypeName, modifiers, typeName, cls.MetadataName, fileName, configMethodName, fieldName, optional, reloadOnChange, handleEnviroments);
                                }
                                break;
                            case FullyQualifiedJsonSettingMetaName
                                when attr.ConstructorArguments[0].Value is string { Length: > 0 } settingPath
                                    && keys.Add(settingPath)
                                    && (attr.ConstructorArguments[4].Value?.ToString() ?? "").Trim() is { } configKey
                                    && methods.TryGetValue(configKey, out var configMethodName2):

                                var type = attr.AttributeClass!.TypeArguments[0];
                                var isPrimitive = type.IsPrimitive();

                                var lifetime = (Lifetime)(byte)attr.ConstructorArguments[1].Value!;
                                var nameFormat2 = (string)attr.ConstructorArguments[3].Value!;
                                var settingType = type.ToGlobalNamespaced();
                                var shortName = type.ToTypeNameFormat();
                                var key2 = attr.ConstructorArguments[2].Value?.ToString() ?? "";
                                var identifier = nameFormat2.Replace("{0}", key2.Pascalize()).RemoveDuplicates()!;
                                var fieldIdentifier = "_" + (key2 is { Length: > 0 } ? key2 : char.ToLower(shortName[0]) + shortName[1..]);
                                bool nullable = (bool)attr.ConstructorArguments[5].Value!;

#if DEBUG_SG
                                Trace.WriteLine($"SCMSCONFDI: Building {shortName}{key2.GetHashCode()}{SymbolEqualityComparer.Default.GetHashCode(type)}");
#endif
                                //#if DEBUG_SG || DEBUG
                                //            var method = Dependencies.GetDependency(identity, containerTypeName, Lifetime.Singleton, settingType, key);
                                //
                                settings.Add(new SettingsMeta(settingPath, configMethodName2, type.AllowsNull(), isPrimitive, lifetime, settingType, identifier, fieldIdentifier, nullable));

                                break;
                        }
                    }

                    emitter?.Settings = settings;

                    return emitter!;
                })
            .Where(w => w is not null)
            .Collect();

        context.RegisterSourceOutput(
            servicesContainers,
            static (context, emitters) =>
            {
                Dictionary<string, byte> uniqueFileNames = [with(StringComparer.Ordinal)];
                foreach (var emitter in emitters)
                {
                    emitter.Emit(context.AddSource, uniqueFileNames);
                }
            });
    }

//    private static void OnCompile(
//        SourceProductionContext context,
//        Compilation compilation,
//        ImmutableArray<(SemanticModel, INamedTypeSymbol)> containers,
//        ImmutableArray<(ImmutableArray<AttributeData>, IParameterSymbol)> settings,
//        ImmutableArray<(ImmutableArray<AttributeData>, ISymbol)> settings2,
//        ImmutableArray<(ImmutableArray<AttributeData>, ISymbol)> configs)
//    {
//        if (!IsMsConfigInstalled(compilation)) return;
//#if DISG
//        Trace.WriteLine($"SCMSCONFDI: Building {"key".GetHashCode()}");
//#endif

//        foreach (var (model, container) in containers)
//        {
//            Map<string, string> methods = new(StringComparer.Ordinal);

//            HashSet<string> keys = new(StringComparer.Ordinal);

//            var attrs = container.GetAttributes();

//            var containerTypeName = container.ToGlobalNamespaced();

//            var nameSpace = container.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : null;


//            var (modifiers, typeName) = container.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() switch
//            {
//                ClassDeclarationSyntax { Modifiers: var mods, Keyword: { } keyword, Identifier: { } identifier, TypeParameterList: var argList } =>
//                    ($"{mods} {keyword}".TrimStart(), $"{identifier}{argList}"),
//                InterfaceDeclarationSyntax { Modifiers: var mods, Keyword: { } keyword, Identifier: { } identifier, TypeParameterList: var argList } =>
//                    ($"{mods} partial class".TrimStart(), $"{identifier.ValueText[1..]}{argList}"),
//                _ => ("", "")
//            };

//            foreach (var (configAttrs, target) in configs)
//            {
//                foreach (var configAttr in configAttrs)
//                {
//                    if (configAttr.ConstructorArguments[0].Value is string { Length: > 0 } fileName
//                    && (target is IAssemblySymbol
//                    || SymbolEqualityComparer.Default.Equals(target, container)))
//                    {
//                        var key = (configAttr.ConstructorArguments[1].Value?.ToString() ?? "").Trim();
//                        (bool flowControl, string configMethodName) = Exist(methods, key);
//                        if (!flowControl)
//                        {
//                            continue;
//                        }

//                        var nameFormat = (string)configAttr.ConstructorArguments[4].Value!;

//                        configMethodName = nameFormat.Replace("{0}", key).RemoveDuplicates();

//                        var fieldName = configMethodName.Camelize();
//                        var optional = configAttr.ConstructorArguments[2].Value?.ToString().ToLower();
//                        var reloadOnChange = configAttr.ConstructorArguments[3].Value?.ToString().ToLower();
//                        var handleEnviroments = (bool)configAttr.ConstructorArguments[5].Value!;
//                    }
//                }
//            }

//            StringBuilder code = new(@"#nullable enable
//using global::Microsoft.Extensions.Configuration;

//");
//            var providerClassName = container.ToNameOnly();

//            if (nameSpace is not null)
//            {
//                code.Append("namespace ")
//                    .Append(nameSpace)
//                    .Append(@";

//");
//            }

//            if (typeName.Length == 0) return;

//            code.Append(modifiers)
//                .AddSpace()
//                .Append(typeName)
//                .Append(@"
//{");

//            foreach (var (configAttrs, target) in configs)
//            {
//                foreach (var configAttr in configAttrs)
//                {
//                    if (configAttr.ConstructorArguments[0].Value is string { Length: > 0 } fileName
//                    && (target is IAssemblySymbol
//                    || SymbolEqualityComparer.Default.Equals(target, container)))
//                    {
//                        var key = (configAttr.ConstructorArguments[1].Value?.ToString() ?? "").Trim();
//                        ref var configMethodName = ref methods.GetValueRefOrAddDefault(key, out var fileExists);

//                        if (fileExists) continue;

//                        var nameFormat = (string)configAttr.ConstructorArguments[4].Value!;
//                        configMethodName = nameFormat.Replace("{0}", key).RemoveDuplicates();
//                        var fieldName = configMethodName.Camelize();
//                        var optional = configAttr.ConstructorArguments[2].Value?.ToString().ToLower();
//                        var reloadOnChange = configAttr.ConstructorArguments[3].Value?.ToString().ToLower();
//                        var handleEnviroments = (bool)configAttr.ConstructorArguments[5].Value!;
//                        ContainerConfigPartialEmmiter emitter = new(fileName, configMethodName, fieldName, optional, reloadOnChange, handleEnviroments);
//                        emitter.EmitCode(code);
//                    }
//                }
//            }

//            foreach (var (settingAttrs, target) in settings2)
//            {
//                foreach (var settingAttr in settingAttrs)
//                {
//                    if (settingAttr.ConstructorArguments[0].Value is not string { Length: > 0 } settingPath
//                        || !keys.Add(settingPath)
//                        || (settingAttr.ConstructorArguments[4].Value?.ToString() ?? "").Trim() is not { } configKey
//                        || !methods.TryGetValue(configKey, out var configMethodName)
//                        || !(target is IAssemblySymbol || SymbolEqualityComparer.Default.Equals(target, container)))

//                        continue;

//                    BuildSetting(containerTypeName, code, settingAttr, settingPath, configMethodName, settingAttr.AttributeClass!.TypeArguments[0]);
//                }
//            }

//            foreach (var (settingAttrs, parameter) in settings)
//            {
//                foreach (var settingAttr in settingAttrs)
//                {
//                    if (settingAttr.ConstructorArguments[0].Value is not string { Length: > 0 } settingPath
//                        || !keys.Add(settingPath)
//                        || (settingAttr.ConstructorArguments[4].Value?.ToString() ?? "").Trim() is not { } configKey
//                        || !methods.TryGetValue(configKey, out var configMethodName))

//                        continue;

//                    BuildSetting(containerTypeName, code, settingAttr, settingPath, configMethodName, parameter.Type);
//                }
//            }

//            code.Append(@"
//}");

//            context.AddSource($"{container.MetadataName}.msConfig", code.ToString());
//        }
//    }


//    static void BuildSetting(string containerTypeName, StringBuilder code, AttributeData settingAttr, string settingPath, string configMethodName, ITypeSymbol type)
//    {
//        var isPrimitive = type.IsPrimitive();

//        var lifetime = (Lifetime)(byte)settingAttr.ConstructorArguments[1].Value!;
//        var nameFormat = (string)settingAttr.ConstructorArguments[3].Value!;
//        var settingType = type.ToGlobalNamespaced();
//        var shortName = type.ToTypeNameFormat();
//        var key = settingAttr.ConstructorArguments[2].Value?.ToString() ?? "";
//        var identifier = nameFormat.Replace("{0}", key.Pascalize()).RemoveDuplicates()!;
//        var fieldIdentifier = "_" + (key is { Length: > 0 } ? key : char.ToLower(shortName[0]) + shortName[1..]);
//        bool nullable = (bool)settingAttr.ConstructorArguments[5].Value!;

//#if DEBUG_SG
//        Trace.WriteLine($"SCMSCONFDI: Building {shortName}{key.GetHashCode()}{SymbolEqualityComparer.Default.GetHashCode(type)}");
//#endif
//        //#if DEBUG_SG || DEBUG
//        //            var method = Dependencies.GetDependency(identity, containerTypeName, Lifetime.Singleton, settingType, key);
//        //

//        BuildSettings(code, );
//    }
#nullable enable

    private static bool IsMsConfigInstalled(Compilation compilation)
    {
        return compilation.GetTypeByMetadataName("Microsoft.Extensions.Configuration.IConfiguration") is not null;
    }

    private static string ParseToolAndVersion()
    {
        string name = "SourceCrafter.DependencyInjection";

        int i = 0;

        foreach (var item in Assembly.GetExecutingAssembly().FullName.Split(','))
        {
            switch (item.Split('='))
            {
                case [{ } _name] when i <= 1: name = _name; break;
                case [" Version", { } version]: return $@"[global::System.CodeDom.Compiler.GeneratedCode(""{name}"", ""{version}"")]";
            }
            i++;
        }

        return $@"[global::System.CodeDom.Compiler.GeneratedCode(""{name}"", ""1.0.0"")]";
    }

    internal sealed class ContainerConfigPartialEmmiter(
        string? nameSpace,
        string containerTypeName,
        string modifiers,
        string typeName,
        string metadataName,
        string fileName,
        string configMethodName,
        string fieldName,
        string? optional,
        string? reloadOnChange,
        bool handleEnviroments)
    {
        internal List<SettingsMeta> Settings = null!;

        string GetFileName(Dictionary<string, byte> uniqueName)
        {
            ref var count = ref CollectionsMarshal.GetValueRefOrAddDefault(uniqueName, metadataName, out var exists);
            if (exists) count += 1;
            return exists ? metadataName + "_" + count : metadataName;
        }

        internal void Emit(Action<string, string> addSource, Dictionary<string, byte> uniqueFileNames)
        {

            StringBuilder code = new(@"#nullable enable
using global::Microsoft.Extensions.Configuration;

");

            if (nameSpace is not null)
            {
                code.Append("namespace ")
                    .Append(nameSpace)
                    .Append(@";

");
            }

            if (containerTypeName.Length == 0) return;

            code.Append(modifiers)
                .AddSpace()
                .Append(typeName)
                .Append(@"
{
    private static ")
                .Append(IConfigurationType)
                .Append(@"? _")
                .Append(fieldName)
                .Append(@" = null;

    internal ")
                .Append(IConfigurationType)
                .Append(@" ")
                .Append(configMethodName)
                .Append(@"
    {
        get
        {
            if(")
                .Append(@"_")
                .Append(fieldName)
                .Append(@" is not null) return ")
                .Append(@"_")
                .Append(fieldName)
                .Append(@";

            lock (this)
            {
                var fileName = global::System.IO.Path.GetFullPath(""").Append(fileName).Append(@""");

                return ")
                .Append(@"_")
                .Append(fieldName)
                .Append(@" ??= new global::Microsoft.Extensions.Configuration.ConfigurationBuilder()");

            if (handleEnviroments)
            {
                code.Append(@"
                    .AddJsonFile($""{fileName}.{EnvironmentName}.json"", true, ").Append(reloadOnChange).Append(")");
            }

            code.Append(@"
                    .AddJsonFile($""{fileName}.json"", ")
                .Append(optional)
                .Append(@", ")
                .Append(reloadOnChange)
                .Append(@")
                    .Build();
            }
        }
    }
");
            foreach (var handle in Settings) handle.AppendSetting(code);

            code.Append('}');

            addSource($"{GetFileName(uniqueFileNames)}.msConfig.g", code.ToString());
        }
    }
}

internal class JsonConfigProviderComparer : IEqualityComparer<(ImmutableArray<AttributeData> attrs, ISymbol target)>
{
    public bool Equals((ImmutableArray<AttributeData> attrs, ISymbol target) x, (ImmutableArray<AttributeData> attrs, ISymbol target) y)
    {
        if (x.attrs.Length != y.attrs.Length) return false;

        if (x.attrs.Length == 0) return true;

        int count = x.attrs.Length;

        foreach (var xx in x.attrs)
            foreach (var yy in y.attrs)
                count += Equals(xx.ConstructorArguments[0].Value?.ToString(), yy.ConstructorArguments[0].Value?.ToString()) ? -1 : 0;

        return count == 0;
    }

    public int GetHashCode((ImmutableArray<AttributeData> attrs, ISymbol target) obj)
    {
        return obj.attrs.Sum(x => (x.ConstructorArguments[0].Value ?? "").GetHashCode());
    }
}

internal class CompilationMeta(bool isMsConfigInstalled, Dictionary<string, Partials.ContainerConfigPartialEmmiter> containers)
{
    public bool IsMsConfigInstalled { get; } = isMsConfigInstalled;
    public Dictionary<string, Partials.ContainerConfigPartialEmmiter> Containers { get; } = containers;

    public override bool Equals(object? obj)
    {
        return obj is CompilationMeta other &&
               IsMsConfigInstalled == other.IsMsConfigInstalled &&
               EqualityComparer<Dictionary<string, Partials.ContainerConfigPartialEmmiter>>.Default.Equals(Containers, other.Containers);
    }

    public override int GetHashCode()
    {
        int hashCode = -1932092628;
        hashCode = hashCode * -1521134295 + IsMsConfigInstalled.GetHashCode();
        hashCode = hashCode * -1521134295 + EqualityComparer<Dictionary<string, Partials.ContainerConfigPartialEmmiter>>.Default.GetHashCode(Containers);
        return hashCode;
    }
}

internal sealed class SettingsMeta(string settingPath, object configMethodName, bool typeAllowsNull, bool isPrimitive, Lifetime lifetime, string settingType, string identifier, string fieldIdentifier, bool nullable)
{
    internal void AppendSetting(StringBuilder code)
    {
        if (!isPrimitive)
        {
            code.Append(@"
    private ");


            if (lifetime is Lifetime.Singleton)
                code.Append("static ");

            code
                .Append(settingType)
                .Append(@"? ")
                .Append(fieldIdentifier)
                .Append(@" = default;
");
        }

        code.Append(@"
    internal ");

        //if (lifetime is Lifetime.Singleton)
        //    code.EmitCode("static ");

        code.Append(settingType)
            .AddSpace()
            .Append(identifier);

        if (isPrimitive)
        {
            code.Append(@" => ")
                .Append(configMethodName)
                .Append(@".GetValue<")
                .Append(settingType)
                .Append(@">(""")
                .Append(settingPath)
                .Append(@""")");

            if (typeAllowsNull && !nullable) code.Append('!');

            code.Append(';');
        }
        else
        {
            code
                .Append(@"
    {
        get
        {
            if (")
                .Append(fieldIdentifier)
                .Append(@" is not null) return ")
                .Append(fieldIdentifier)
                .Append(@";
            
            lock (this)     

            return ")
                .Append(fieldIdentifier)
                .Append(@" ??= BuildSetting();

            ")
                .Append(settingType)
                .Append(@" BuildSetting()
            {
                ")
                .Append(settingType)
                .Append(" setting = new ")
                .Append(settingType)
                .Append(@"();");

            code.Append(@"

                ").Append(configMethodName).Append(@"
                    .GetSection(""").Append(settingPath).Append(@""")                
                    .Bind(setting);

                return setting;
            }
        }
    }
");
        }
    }
}
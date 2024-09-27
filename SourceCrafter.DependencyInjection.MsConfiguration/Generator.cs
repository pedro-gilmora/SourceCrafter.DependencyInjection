using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System;
using System.Linq;
using System.Reflection;
using SourceCrafter.DependencyInjection;
using System.Text;
using System.Collections.Generic;
using SourceCrafter.DependencyInjection.Interop;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Data;
using System.IO;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices.ComTypes;


[Generator]
public class Generator : IIncrementalGenerator
{
    internal readonly static string generatedCodeAttribute = ParseToolAndVersion();
    readonly static Guid generatorGuid = new("54B00B9C-7CF8-45B2-81FC-361B7F5026EB");

    static volatile bool isMsConfigInstalled = false;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
#if DEBUG_SG
        System.Diagnostics.Debugger.Launch();
#endif
        var servicesContainers = context.SyntaxProvider
                .ForAttributeWithMetadataName("SourceCrafter.DependencyInjection.Attributes.ServiceContainerAttribute",
                    (node, a) => true,
                    (t, c) => (t.SemanticModel, Class: (INamedTypeSymbol)t.TargetSymbol))
                .Collect();

        var settings = context.SyntaxProvider
                .ForAttributeWithMetadataName("SourceCrafter.DependencyInjection.MsConfiguration.Metadata.JsonSettingAttribute",
                    (node, a) => true,
                    (t, c) => (t.Attributes[0], (IParameterSymbol)t.TargetSymbol))
                .Collect();

        var configs = context.SyntaxProvider
                .ForAttributeWithMetadataName("SourceCrafter.DependencyInjection.MsConfiguration.Metadata.JsonConfigurationAttribute",
                    (node, a) => true,
                    (t, c) => (t.Attributes[0], t.TargetSymbol))
                .WithComparer(new JsonConfigProviderComparer())
                .Collect();

        context.RegisterSourceOutput(
            context.CompilationProvider
                .Combine(servicesContainers)
                .Combine(settings)
                .Combine(configs),
            static (context, collectedInfo) =>
            {
                var (((compilation, containers), settings), configs) = collectedInfo;

                OnCompile(context, compilation, containers, settings, configs);
            });
    }

    static readonly Regex remover = new("^Get|[Ss]ettings?|[Cc]onfig(?:uration)?", RegexOptions.Compiled);

    private static void OnCompile(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<(SemanticModel, INamedTypeSymbol)> containers,
        ImmutableArray<(AttributeData, IParameterSymbol)> settings,
        ImmutableArray<(AttributeData, ISymbol)> configs)
    {
        //Map<string, ValueBuilder> resolvedTypes = new (StringComparer.Ordinal);

        //InteropServices.RegisterDependencyResolvers(generatorGuid, Resolve);

        //void Resolve(Compilation compilation, ITypeSymbol serviceContainer, DependencyMap servicesDescriptors)
        //{
        //    servicesDescriptors.ForEach((DependencyKey key, ref ServiceDescriptor item) =>
        //    {
        //        if (resolvedTypes.TryGetValue(item.ExportTypeName, out var existing) && !item.IsResolved)
        //        {
        //            item.IsResolved = true;
        //            item.ResolvedBy = generatorGuid;
        //            item.GenerateValue = existing!;
        //            return;
        //        }
        //    });
        //}

        if (!IsMsConfigInstalled(compilation, out var iConfigTypeSymbol)) return;

        Map<(int, Lifetime, string?), string> dependencyRegistry = new(EqualityComparer<(int, Lifetime, string?)>.Default);

        HashSet<string> methodsRegistry = new(StringComparer.Ordinal);

        Map<string, string> files = new(StringComparer.Ordinal);
        HashSet<string> keys = new(StringComparer.Ordinal);

        foreach (var (model, container) in containers)
        {
            var attrs = container.GetAttributes();

            StringBuilder code = new(@"#nullable enable
using global::Microsoft.Extensions.Configuration;

");
            var providerClassName = container.ToNameOnly();

            if (container.ContainingNamespace is { IsGlobalNamespace: false } ns)
            {
                code.Append("namespace ")
                    .Append(ns.ToDisplayString()!)
                    .Append(@";

");
            }

            code.Append("public partial class ")
                .Append(providerClassName)
                .Append(@"
{");

            foreach (var (configAttr, target) in configs)
            {
                if (configAttr.ConstructorArguments[0].Value is string { Length: > 0} fileName
                    && (target is IAssemblySymbol 
                    || SymbolEqualityComparer.Default.Equals(target, container)))
                {
                    var key = (configAttr.ConstructorArguments[1].Value?.ToString() ?? "").Trim();

                    ref var configMethodName = ref files.GetValueOrAddDefault(key, out var fileExists);

                    if (fileExists) continue;

                    fileName = Path.GetFileNameWithoutExtension(fileName);

                    var nameFormat = (string)configAttr.ConstructorArguments[4].Value!;

                    configMethodName = nameFormat.Replace("{0}", key).RemoveDuplicates();

                    var fieldName = configMethodName is ['G', 'e', 't', ..] ? configMethodName[3..] : configMethodName;
                    var optional = configAttr.ConstructorArguments[2].Value?.ToString().ToLower();
                    var reloadOnChange = configAttr.ConstructorArguments[3].Value?.ToString().ToLower();
                    var handleEnviroments = (bool)configAttr.ConstructorArguments[5].Value!;

                    //Register key to provide to settings

                    code.Append(@"
    ").Append(generatedCodeAttribute).Append(@"
    private static ")
                        .Append("global::Microsoft.Extensions.Configuration.IConfiguration")
                        .Append(@"? _f")
                        .Append(fieldName)
                        .Append(@";

    ").Append(generatedCodeAttribute).Append(@"
    private static ")
                        .Append("global::Microsoft.Extensions.Configuration.IConfiguration")
                        .Append(@" ")
                        .Append(configMethodName)
                        .Append(@"()
    {
        if(")
                        .Append(@"_f")
                        .Append(fieldName)
                        .Append(@" is not null) return ")
                        .Append(@"_f")
                        .Append(fieldName)
                        .Append(@";

        lock (__lock)
        {
            return ")
                        .Append(@"_f")
                        .Append(fieldName)
                        .Append(@" ??= new global::Microsoft.Extensions.Configuration.ConfigurationBuilder()");

                    if (handleEnviroments)
                    {
                        code.Append(@"
                .AddJsonFile($""{(global::System.IO.Path.GetFullPath(""")
                            .Append(fileName)
                            .Append(@"""))}.{Environment}.json"", true, ")
                            .Append(reloadOnChange)
                            .Append(")");
                    }

                    code.Append(@"
                .AddJsonFile(global::System.IO.Path.GetFullPath(""")
                        .Append(fileName)
                        .Append(@".json""), ")
                        .Append(optional)
                        .Append(@", ")
                        .Append(reloadOnChange)
                        .Append(@")
                .Build();
        }
    }
");
                }
            }

            foreach (var (settingAttr, parameter) in settings)
            {
                if (settingAttr.ConstructorArguments[0].Value is not string { Length: > 0 } settingPath 
                    || !keys.Add(settingPath)
                    || (settingAttr.ConstructorArguments[2].Value?.ToString() ?? "").Trim() is not { } configKey
                    || !files.TryGetValue(configKey, out var configMethodName)) continue;

                var lifetime = (Lifetime)(byte)settingAttr.ConstructorArguments[1].Value!;
                var nameFormat = (string)settingAttr.ConstructorArguments[3].Value!;
                var settingType = parameter.Type.ToGlobalNamespaced();
                var identifier = nameFormat.Replace("{0}", configKey).RemoveDuplicates()!;
                var fieldIdentifier = "_" + identifier.Camelize();

                code.Append(@"
    ").Append(generatedCodeAttribute)
    .Append(@"
    private ");

                if(lifetime is Lifetime.Singleton)
                    code.Append("static ");
                
                code
                    .Append(settingType)
                    .Append(@"? ")
                    .Append(fieldIdentifier)
                    .Append(@";

    ").Append(generatedCodeAttribute)
    .Append(@"
    private ");

                if (lifetime is Lifetime.Singleton)
                    code.Append("static ");

                code.Append(settingType)
                    .AddSpace()
                    .Append(identifier)
                    .Append(@"()
    {
        if (")
                    .Append(fieldIdentifier)
                    .Append(@" is null)
            lock (__lock)     
                return ")
                    .Append(fieldIdentifier)
                    .Append(@" ??= BuildSetting();

        return ")
                    .Append(fieldIdentifier)
                    .Append(@";

        ")
                    .Append(settingType)
                    .Append(@" BuildSetting()
        {
            var setting = new ")
                    .Append(settingType)
                    .Append(@"();

            ").Append(configMethodName).Append(@"() 
                .GetSection(""").Append(settingPath).Append(@""")                
                .Bind(setting);

            return setting;
        }
    }
");
            }

            code.Append(@"
}");

            context.AddSource($"{container.MetadataName}.msConfig", code.ToString());
        }
    }
#nullable enable
    //private static bool HasRegisteredType(ImmutableArray<AttributeData> attrs, ITypeSymbol type, SemanticModel _model)
    //{
    //    foreach (var item in attrs)
    //    {
    //        if (item.AttributeClass?.ToGlobalNonGenericNamespace()
    //            is ServiceDescriptor.SingletonAttr
    //            or ServiceDescriptor.ScopedAttr
    //            or ServiceDescriptor.TransientAttr
    //            && (item.AttributeClass.TypeParameters switch
    //               {
    //                   [_, { } _type] => SymbolEqualityComparer.Default.Equals(_type, type),
    //                   [{ } _type] => SymbolEqualityComparer.Default.Equals(_type, type),
    //                   _ => item.ApplicationSyntaxReference?.GetSyntax() 
    //                       is AttributeSyntax { ArgumentList.Arguments: [_, { Expression: TypeOfExpressionSyntax {Type:{ } _type } }] }
    //                       && SymbolEqualityComparer.Default.Equals(_model!.GetSymbolInfo(_type).Symbol, type)
    //               })) return true;
    //    }
    //    return false;
    //}

    //    private static (string, string)? ResolveDependency(Compilation compilation, ITypeSymbol serviceContainer, Set<ServiceDescriptor> servicesDescriptors)
    //    {
    //        if (!IsMsConfigInstalled(compilation, out var iConfigTypeSymbol)) return null;

    //        StringBuilder extraCode = new(@"

    //using global::Microsoft.Extensions.Configuration;

    //");

    //        string iConfigFullTypeName = iConfigTypeSymbol.ToGlobalNamespaced();

    //        servicesDescriptors.TryAdd(new(iConfigTypeSymbol, iConfigFullTypeName, iConfigFullTypeName, null)
    //        {
    //            Resolved = true,
    //            NotRegistered = true
    //        });

    //        var providerClassName = serviceContainer.ToNameOnly();

    //        if (serviceContainer.ContainingNamespace is { IsGlobalNamespace: false } ns)
    //        {
    //            extraCode.Append("namespace ")
    //                .Append(ns.ToDisplayString()!)
    //                .Append(@";

    //");
    //        }

    //        extraCode.Append("public partial class ")
    //            .Append(providerClassName)
    //            .Append(@" : global::SourceCrafter.DependencyInjection.IServiceProvider<global::Microsoft.Extensions.Configuration.IConfiguration>
    //{
    //    private static global::Microsoft.Extensions.Configuration.IConfiguration? __appConfig__;

    //    ").Append(generatedCodeAttribute).Append(@"
    //    private static global::Microsoft.Extensions.Configuration.IConfiguration __APPCONFIG__()
    //    {
    //        if (__appConfig__ is null)
    //			lock (__lock)
    //				return __appConfig__ ??= new ConfigurationBuilder()
    //                    .SetBasePath(global::System.IO.Directory.GetCurrentDirectory())
    //                    .AddJsonFile(""appsettings.json"", optional: true, reloadOnChange: true)
    //                    .Build();

    //        return __appConfig__;
    //    }

    //    ").Append(generatedCodeAttribute).Append(@"
    //    global::Microsoft.Extensions.Configuration.IConfiguration 
    //		    global::SourceCrafter.DependencyInjection.IServiceProvider<global::Microsoft.Extensions.Configuration.IConfiguration>
    //			    .GetService()
    //    {
    //		return __APPCONFIG__();
    //	}
    //");
    //        Map<string, VarNameBuilder> keys = new(StringComparer.Ordinal);

    //        servicesDescriptors.ForEach((ref ServiceDescriptor item) =>
    //        {
    //            if (item.Resolved) return;

    //            foreach (var attr in item.Attributes)
    //            {
    //                if (attr.AttributeClass?.ToNameOnly() == "SettingAttribute")
    //                {
    //                    item.Resolved = true;
    //                    item.ResolvedBy = generatorGuid;

    //                    var key = attr.ConstructorArguments.FirstOrDefault().Value as string ?? "";

    //                    if (key.Length == 0)
    //                    {
    //                        continue;
    //                    }

    //                    ref var existingOrNew = ref keys.GetOrAddDefault(key, out var exists)!;

    //                    if (exists)
    //                    {
    //                        item.GenerateValue = existingOrNew;
    //                        continue;
    //                    }

    //                    var suffix = key.Replace(':', '_').ToUpper();

    //                    var identifier = item.CacheMethodName = @"__APPCONFIG__" + suffix;

    //                    item.GenerateValue = existingOrNew = code => code.Append(identifier).Append("()");

    //                    extraCode.Append(@"
    //    ").Append(generatedCodeAttribute).Append(@"
    //    private static ")
    //                        .Append(item.FullTypeName)
    //                        .Append(@"? __appConfig__")
    //                        .Append(suffix)
    //                        .Append(@";

    //    ").Append(generatedCodeAttribute).Append(@"
    //    private static ")
    //                        .Append(item.FullTypeName)
    //                        .AddSpace()
    //                        .Append(identifier)
    //                        .Append(@"()
    //    {
    //        if (__appConfig__")
    //                        .Append(suffix)
    //                        .Append(@" is null)
    //			lock (__lock)     
    //				return __appConfig__")
    //                        .Append(suffix)
    //                        .Append(@" ??= BuildSetting();

    //        return __appConfig__")
    //                        .Append(suffix)
    //                        .Append(@";

    //        ")
    //                        .Append(item.FullTypeName)
    //                        .Append(@" BuildSetting()
    //        {
    //            var setting = new ")
    //                        .Append(item.FullTypeName)
    //                        .Append(@"();

    //            __APPCONFIG__().GetSection(""").Append(key).Append(@""").Bind(setting);

    //            return setting;            
    //        }
    //    }
    //");
    //                }
    //            }
    //        });

    //        extraCode.Append(@"
    //}");


    //        return ("msConfig", extraCode.ToString());
    //    }

    private static bool IsMsConfigInstalled(Compilation compilation, out INamedTypeSymbol type)
    {
        type = compilation.GetTypeByMetadataName("Microsoft.Extensions.Configuration.IConfiguration")!;

        return isMsConfigInstalled || (isMsConfigInstalled = type is not null);
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
}

internal class JsonConfigProviderComparer : IEqualityComparer<(AttributeData attr, ISymbol target)>
{
    public bool Equals((AttributeData attr, ISymbol target) x, (AttributeData attr, ISymbol target) y)
    {
        return (x is { attr.ConstructorArguments: [{ Value: string { Length: > 0 } xFilePath }] }
            && y is { attr.ConstructorArguments: [{ Value: string { Length: > 0 } yFilePath }] })
            && ((xFilePath, yFilePath) is (null or "", null or "")
                || (xFilePath, yFilePath) is ({ Length: > 0 }, { Length: > 0 })
                    && xFilePath.Equals(yFilePath, StringComparison.OrdinalIgnoreCase));
    }

    public int GetHashCode((AttributeData attr, ISymbol target) obj)
    {
        return (obj.attr.ConstructorArguments[0].Value ?? "").GetHashCode();
    }
}
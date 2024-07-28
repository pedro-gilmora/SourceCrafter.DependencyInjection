using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System;
using System.Linq;
using System.Reflection;
using SourceCrafter.DependencyInjection.Interop;
using System.Text;
using System.Collections.Generic;

//[assembly: InternalsVisibleTo("SourceCrafter.MappingGenerator.UnitTests")]
namespace SourceCrafter.DependencyInjection.MsConfiguration;

[Generator]
public class Generator : IIncrementalGenerator
{
    internal readonly static string generatedCodeAttribute = ParseToolAndVersion();
    static volatile bool isSet = false;
    static volatile bool isMsConfigInstalled = false;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        //context.RegisterPostInitializationOutput(OnCompile);
        if (isSet) return;

        isSet = true;

        DependencyInjectionPartsGenerator.OnContainerRegistered += OnContainerCreated;

    }

    private static (string, string)? OnContainerCreated(Compilation compilation, ITypeSymbol serviceContainer, Set<ServiceDescriptor> servicesDescriptors)
    {
        if (!IsMsConfigInstalled(compilation)) return null;

        StringBuilder extraCode = new(@"#nullable enable
using Microsoft.Extensions.Configuration;

");

        var providerClassName = serviceContainer.ToNameOnly();

        if (serviceContainer.ContainingNamespace is { IsGlobalNamespace: false } ns)
        {
            extraCode.Append("namespace ")
                .Append(ns.ToDisplayString()!)
                .Append(@";

");
        }

        extraCode.Append("public partial class ")
            .Append(providerClassName)
            .Append(@" : global::SourceCrafter.DependencyInjection.IServiceProvider<global::Microsoft.Extensions.Configuration.IConfiguration>
{
    private static global::Microsoft.Extensions.Configuration.IConfiguration? __appConfig__;

    ").Append(generatedCodeAttribute).Append(@"
    private static global::Microsoft.Extensions.Configuration.IConfiguration? __APPCONFIG__
    {
        get 
        {
            if (__appConfig__ is null)
			    lock (__lock)
				    return __appConfig__ ??= new ConfigurationBuilder()
                        .SetBasePath(global::System.IO.Directory.GetCurrentDirectory())
                        .AddJsonFile(""appsettings.json"", optional: true, reloadOnChange: true)
                        .Build();

            return __appConfig__;
        }
    }

    ").Append(generatedCodeAttribute).Append(@"
    global::Microsoft.Extensions.Configuration.IConfiguration 
		    global::SourceCrafter.DependencyInjection.IServiceProvider<global::Microsoft.Extensions.Configuration.IConfiguration>
			    .GetService()
    {
		return __APPCONFIG__;
	}
");
        Map<string, VarNameBuilder> keys = new(StringComparer.Ordinal);

        servicesDescriptors.ForEach((ref ServiceDescriptor item) =>
        {
            if (item.Resolved) return;

            foreach (var attr in item.Attributes)
            {
                if (attr.AttributeClass?.ToNameOnly() == "SettingAttribute")
                {
                    item.Resolved = true;

                    var key = attr.ConstructorArguments.FirstOrDefault().Value as string ?? "";

                    if (key.Length == 0)
                    {
                        continue;
                    }

                    ref var existingOrNew = ref keys.GetOrAddDefault(key, out var exists)!;

                    if (exists)
                    {
                        item.GenerateValue = existingOrNew;
                        continue;
                    }

                    var suffix = key.Replace(':', '_').ToUpper();

                    var identifier = item.CacheMethodName = @"__APPCONFIG__" + suffix;

                    item.GenerateValue = existingOrNew = code => code.Append(identifier);

                    extraCode.Append(@"
    ").Append(generatedCodeAttribute).Append(@"
    private static ")
                        .Append(item.FullTypeName)
                        .Append(@"? __appConfig__")
                        .Append(suffix)
                        .Append(@";

    ").Append(generatedCodeAttribute).Append(@"
    private static ")
                        .Append(item.FullTypeName)
                        .AddSpace()
                        .Append(identifier)
                        .Append(@"
    {
        get 
        {
            if (__appConfig__")
                        .Append(suffix)
                        .Append(@" is null)
			    lock (__lock)     
				    return __appConfig__")
                        .Append(suffix)
                        .Append(@" ??= BuildSetting();

            return __appConfig__")
                        .Append(suffix)
                        .Append(@";
                
            ")
                        .Append(item.FullTypeName)
                        .Append(@" BuildSetting()
            {
                var setting = new ")
                        .Append(item.FullTypeName)
                        .Append(@"();
                
                __APPCONFIG__.GetSection(""").Append(key).Append(@""").Bind(setting);

                return setting;            
            }
        }
    }
");
                }
            }
        });       

        extraCode.Append(@"
}");


        return ("msConfig", extraCode.ToString());
    }

    private static bool IsMsConfigInstalled(Compilation compilation)
    {
        return isMsConfigInstalled
            || (isMsConfigInstalled = 
            compilation.GetTypeByMetadataName("Microsoft.Extensions.Configuration.IConfiguration") is not null);

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

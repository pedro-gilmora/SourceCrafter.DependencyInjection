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
    readonly static Guid generatorGuid = new("54B00B9C-7CF8-45B2-81FC-361B7F5026EB");
    static volatile bool isSet = false;
    static volatile bool isMsConfigInstalled = false;
    public Generator()
    {
        if (isSet) return;

        isSet = true;

        DependencyInjectionPartsGenerator.RegisterDependencyResolvers(generatorGuid, ResolveDependency);
    }

    ~Generator()
    {
        if (!isSet) return;

        isSet = false;

        DependencyInjectionPartsGenerator.UnregisterDependencyResolvers(generatorGuid);
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        //context.RegisterPostInitializationOutput(OnCompile);
    }

    private static (string, string)? ResolveDependency(Compilation compilation, ITypeSymbol serviceContainer, Set<ServiceDescriptor> servicesDescriptors)
    {
        if (!IsMsConfigInstalled(compilation, out var iConfigTypeSymbol)) return null;

        StringBuilder extraCode = new(@"#nullable enable
using global::Microsoft.Extensions.Configuration;

");

        string iConfigFullTypeName = iConfigTypeSymbol.ToGlobalNamespaced();

        servicesDescriptors.TryAdd(new(iConfigTypeSymbol, iConfigFullTypeName, iConfigFullTypeName, null)
        {
            Resolved = true,
            NotRegistered = true
        });

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
    private static global::Microsoft.Extensions.Configuration.IConfiguration __APPCONFIG__()
    {
        if (__appConfig__ is null)
			lock (__lock)
				return __appConfig__ ??= new ConfigurationBuilder()
                    .SetBasePath(global::System.IO.Directory.GetCurrentDirectory())
                    .AddJsonFile(""appsettings.json"", optional: true, reloadOnChange: true)
                    .Build();

        return __appConfig__;
    }

    ").Append(generatedCodeAttribute).Append(@"
    global::Microsoft.Extensions.Configuration.IConfiguration 
		    global::SourceCrafter.DependencyInjection.IServiceProvider<global::Microsoft.Extensions.Configuration.IConfiguration>
			    .GetService()
    {
		return __APPCONFIG__();
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
                    item.ResolvedBy = generatorGuid;

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

                    item.GenerateValue = existingOrNew = code => code.Append(identifier).Append("()");

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
                        .Append(@"()
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
                
            __APPCONFIG__().GetSection(""").Append(key).Append(@""").Bind(setting);

            return setting;            
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

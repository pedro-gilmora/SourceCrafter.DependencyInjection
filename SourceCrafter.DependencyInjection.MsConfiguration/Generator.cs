using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System;
using System.Linq;
using System.Reflection;
using SourceCrafter.DependencyInjection;
using System.Text;
using System.Collections.Generic;
using SourceCrafter.DependencyInjection.Interop;
using static SourceCrafter.DependencyInjection.Extensions;
using Microsoft.CodeAnalysis.CSharp.Syntax;

[Generator]
public class Generator : IIncrementalGenerator
{
    private const string IConfigurationType = "global::Microsoft.Extensions.Configuration.IConfiguration";
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
                    (t, c) => (t.Attributes, (IParameterSymbol)t.TargetSymbol))
                .Collect();

        var settings2 = context.SyntaxProvider
                .ForAttributeWithMetadataName("SourceCrafter.DependencyInjection.MsConfiguration.Metadata.JsonSettingAttribute`1",
                    (node, a) => true,
                    (t, c) => (t.Attributes, t.TargetSymbol))
                .Collect();

        var configs = context.SyntaxProvider
                .ForAttributeWithMetadataName("SourceCrafter.DependencyInjection.MsConfiguration.Metadata.JsonConfigurationAttribute",
                    (node, a) => true,
                    (t, c) => (t.Attributes, t.TargetSymbol))
                .WithComparer(new JsonConfigProviderComparer())
                .Collect();

        context.RegisterSourceOutput(
            context.CompilationProvider
                .Combine(servicesContainers)
                .Combine(settings)
                .Combine(settings2)
                .Combine(configs),
            static (context, collectedInfo) =>
            {
                var ((((compilation, containers), settings), settings2), configs) = collectedInfo;

                OnCompile(context, compilation, containers, settings, settings2, configs);
            });
    }

    private static void OnCompile(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<(SemanticModel, INamedTypeSymbol)> containers,
        ImmutableArray<(ImmutableArray<AttributeData>, IParameterSymbol)> settings,
        ImmutableArray<(ImmutableArray<AttributeData>, ISymbol)> settings2,
        ImmutableArray<(ImmutableArray<AttributeData>, ISymbol)> configs)
    {
        if (!IsMsConfigInstalled(compilation, out var iConfigTypeSymbol)) return;

        var configTypeName = iConfigTypeSymbol.ToGlobalNamespaced();

        Map<(int, Lifetime, string?), string> dependencyRegistry = new(EqualityComparer<(int, Lifetime, string?)>.Default);

        HashSet<string> methodsRegistry = new(StringComparer.Ordinal);

        Map<string, string> files = new(StringComparer.Ordinal);
        HashSet<string> keys = new(StringComparer.Ordinal);

        foreach (var (model, container) in containers)
        {
            var attrs = container.GetAttributes();

            var containerTypeName = container.ToGlobalNamespaced();

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

            var (modifiers, typeName) = container.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() switch
            {
                ClassDeclarationSyntax { Modifiers: var mods, Keyword: { } keyword, Identifier: { } identifier, TypeParameterList: var argList } =>
                    ($"{mods} {keyword}".TrimStart(), $"{identifier}{argList}"),
                StructDeclarationSyntax { Modifiers: var mods, Keyword: { } keyword, Identifier: { } identifier, TypeParameterList: var argList } =>
                    ($"{mods} {keyword}".TrimStart(), $"{identifier}{argList}"),
                _ => ("partial class ", "")
            };

            code.Append(modifiers)
                .AddSpace()
                .Append(typeName)
                .Append(@"
{");

            foreach (var (configAttrs, target) in configs)
            {
                foreach (var configAttr in configAttrs)
                {
                    if (configAttr.ConstructorArguments[0].Value is string { Length: > 0 } fileName
                    && (target is IAssemblySymbol
                    || SymbolEqualityComparer.Default.Equals(target, container)))
                    {
                        var key = (configAttr.ConstructorArguments[1].Value?.ToString() ?? "").Trim();

                        ref var configMethodName = ref files.GetValueOrAddDefault(key, out var fileExists);

                        if (fileExists) continue;

#if DEBUG_SG || DEBUG
                        var method = Dependencies.GetDependency(containerTypeName, Lifetime.Singleton, configTypeName, key);

#endif
                        var nameFormat = (string)configAttr.ConstructorArguments[4].Value!;

                        configMethodName = nameFormat.Replace("{0}", key).RemoveDuplicates();

                        var fieldName = configMethodName.Camelize();
                        var optional = configAttr.ConstructorArguments[2].Value?.ToString().ToLower();
                        var reloadOnChange = configAttr.ConstructorArguments[3].Value?.ToString().ToLower();
                        var handleEnviroments = (bool)configAttr.ConstructorArguments[5].Value!;

                        //Register key to provide to settings

                        code.Append(@"
    ").Append(generatedCodeAttribute).Append(@"
    private static ")
                            .Append(IConfigurationType)
                            .Append(@"? _")
                            .Append(fieldName)
                            .Append(@" = null;

    ").Append(generatedCodeAttribute).Append(@"
    private static ")
                            .Append(IConfigurationType)
                            .Append(@" ")
                            .Append(configMethodName)
                            .Append(@"()
    {
        if(")
                            .Append(@"_")
                            .Append(fieldName)
                            .Append(@" is not null) return ")
                            .Append(@"_")
                            .Append(fieldName)
                            .Append(@";

        lock (__lock)
        {
            return ")
                            .Append(@"_")
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
            }

            foreach (var (settingAttrs, target) in settings2)
            {
                foreach (var settingAttr in settingAttrs)
                {
                    if (settingAttr.ConstructorArguments[0].Value is not string { Length: > 0 } settingPath
                        || !keys.Add(settingPath)
                        || (settingAttr.ConstructorArguments[4].Value?.ToString() ?? "").Trim() is not { } configKey
                        || !files.TryGetValue(configKey, out var configMethodName)
                        || !(target is IAssemblySymbol || SymbolEqualityComparer.Default.Equals(target, container))) continue;

                    BuildSetting(code, settingAttr, settingPath, configMethodName, settingAttr.AttributeClass!.TypeArguments[0]);
                }
            }
            
            foreach (var (settingAttrs, parameter) in settings)
            {
                foreach (var settingAttr in settingAttrs)
                {
                    if (settingAttr.ConstructorArguments[0].Value is not string { Length: > 0 } settingPath
                    || !keys.Add(settingPath)
                    || (settingAttr.ConstructorArguments[4].Value?.ToString() ?? "").Trim() is not { } configKey
                    || !files.TryGetValue(configKey, out var configMethodName)) continue;


                    BuildSetting(code, settingAttr, settingPath, configMethodName, parameter.Type);
                }
            }

            code.Append(@"
}");

            context.AddSource($"{container.MetadataName}.msConfig", code.ToString());
        }

        static void BuildSetting(StringBuilder code, AttributeData settingAttr, string settingPath, string configMethodName, ITypeSymbol type)
        {
            var isPrimitive = type.IsPrimitive();

            var lifetime = (Lifetime)(byte)settingAttr.ConstructorArguments[1].Value!;
            var nameFormat = (string)settingAttr.ConstructorArguments[3].Value!;
            var settingType = type.ToGlobalNamespaced();
            var identifier = nameFormat.Replace("{0}", settingAttr.ConstructorArguments[2].Value?.ToString().Pascalize() ?? "").RemoveDuplicates()!;
            var fieldIdentifier = "_" + identifier.Camelize();

            if (!isPrimitive)
            {
                code.Append(@"
    ")
                    .Append(generatedCodeAttribute)
                    .Append(@"
    private ");


                if (lifetime is Lifetime.Singleton)
                    code.Append("static ");

                code
                    .Append(settingType)
                    .Append(@"? ")
                    .Append(fieldIdentifier)
                    .Append(@" = null;
");
            }
            code.Append(@"
    ").Append(generatedCodeAttribute)
.Append(@"
    private ");

            if (lifetime is Lifetime.Singleton)
                code.Append("static ");

            code.Append(settingType)
                .AddSpace()
                .Append(identifier);

            if (isPrimitive)
            {
                code.Append(@"() => ")
                    .Append(configMethodName)
                    .Append(@"().GetValue<")
                    .Append(settingType)
                    .Append(@">(""")
                    .Append(settingPath)
                    .Append(@""");");
            }
            else
            {
                code
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
            ")
                    .Append(settingType)
                    .Append(" setting = new ")
                    .Append(settingType)
                    .Append(@"();");

                code.Append(@"

            ").Append(configMethodName).Append(@"() 
                .GetSection(""").Append(settingPath).Append(@""")                
                .Bind(setting);

            return setting;
        }
    }
");
            }
        }
    }
#nullable enable

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

internal class JsonConfigProviderComparer : IEqualityComparer<(ImmutableArray<AttributeData> attrs, ISymbol target)>
{
    public bool Equals((ImmutableArray<AttributeData> attrs, ISymbol target) x, (ImmutableArray<AttributeData> attrs, ISymbol target) y)
    {
        if(x.attrs.Length != y.attrs.Length) return false;

        if(x.attrs.Length == 0) return true;

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
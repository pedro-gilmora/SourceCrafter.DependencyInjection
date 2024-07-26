using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System;
using System.Linq;
using System.Reflection;
using SourceCrafter.DependencyInjection.Interop;
using System.Text;
using Microsoft.CodeAnalysis.Text;

//[assembly: InternalsVisibleTo("SourceCrafter.MappingGenerator.UnitTests")]
namespace SourceCrafter.DependencyInjection;

[Generator]
public class Generator : IIncrementalGenerator
{

    internal readonly static string generatedCodeAttribute = ParseToolAndVersion();

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
#if DEBUG_SG
        System.Diagnostics.Debugger.Launch();
#endif

        context.RegisterPostInitializationOutput(GenerateAbstracts);

        var serviceHostType = context.SyntaxProvider
                .ForAttributeWithMetadataName("SourceCrafter.DependencyInjection.Attributes.ServiceContainerAttribute",
                    (node, a) => true,
                    (t, c) => (t.SemanticModel, Class: (INamedTypeSymbol)t.TargetSymbol)).Collect();

        context.RegisterSourceOutput(context.CompilationProvider.Combine(serviceHostType), static (p, info) =>
        {
            Map<string, byte> uniqueName = new(StringComparer.Ordinal);

            var sb = new StringBuilder("/*").AppendLine();
            int start = sb.Length;

            try
            {
                foreach (var item in info.Right)
                {
                    new ServiceContainerGenerator(item.Class, info.Left, item.SemanticModel)
                              .TryBuild(uniqueName, p.AddSource);

                }
            }
            catch (Exception e)
            {
                sb.AppendLine(e.ToString());
            }

            if (sb.Length > start)
            {
                p.AddSource("errors", SourceText.From(sb.ToString()));
            }
        });
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

    private static void GenerateAbstracts(IncrementalGeneratorPostInitializationContext producer)
    {
        producer.AddSource("SourceCrafter.DependencyInjection.Attributes", @"#pragma warning disable CS9113

namespace SourceCrafter.DependencyInjection.Attributes
{
	public class ServiceContainerAttribute : Attribute;
	public class SingletonAttribute<TImplementation>(string? name = null, string? factory = null) : SingletonAttribute(typeof(TImplementation), null, name, factory);
	public class SingletonAttribute<T, TImplementation>(string? name = null, string? factory = null) : SingletonAttribute(typeof(TImplementation), typeof(T), name, factory);
	public class SingletonAttribute(global::System.Type impl, global::System.Type? iface = null, string? name = null, string? factory = null) : Attribute;
	public class ScopedAttribute<TImplementation>(string? name = null, string? factory = null) : SingletonAttribute(typeof(TImplementation), null, factory, name);
	public class ScopedAttribute<T, TImplementation>(string? name = null, string? factory = null) : SingletonAttribute(typeof(TImplementation), typeof(T), name, factory);
	public class ScopedAttribute(global::System.Type impl, global::System.Type? iface = null, string? name = null, string? factory = null) : Attribute;
	public class TransientAttribute<TImplementation>(string? name = null, string? factory = null) : SingletonAttribute(typeof(TImplementation), null, name, factory);
	public class TransientAttribute<T, TImplementation>(string? name = null, string? factory = null) : SingletonAttribute(typeof(TImplementation), typeof(T), name, factory);
	public class TransientAttribute(global::System.Type impl, global::System.Type? iface = null, string? name = null, string? factory = null) : Attribute;
	public class NamedSingletonServiceAttribute(string name) : Attribute;
	public class NamedScopedServiceAttribute(string name) : Attribute;
	public class NamedTransientServiceAttribute(string name) : Attribute;
	public class NamedServiceAttribute(string name) : Attribute;
}

namespace SourceCrafter.DependencyInjection
{
	public interface IServiceProvider<T> { T GetService(); }
	public interface IKeyedServiceProvider<T> { T GetService(string key); }
	public interface IServiceProvider { T GetService<T>(); }
	public interface IKeyedServiceProvider { T GetService<T>(string key); }
}

#pragma warning restore CS9113");
    }

}

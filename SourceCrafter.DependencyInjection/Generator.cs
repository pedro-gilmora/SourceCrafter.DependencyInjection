using Microsoft.CodeAnalysis;
using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;

//[assembly: InternalsVisibleTo("SourceCrafter.MappingGenerator.UnitTests")]
namespace SourceCrafter.DependencyInjection;

[AttributeUsage(AttributeTargets.Assembly)]
public class DependencyResolverAttribute<IDependencyResolver> : Attribute;

[Generator]
public class Generator : IIncrementalGenerator
{
    internal readonly static string generatedCodeAttribute = ParseToolAndVersion();
    internal readonly static Guid generatorGuid = new("31C54896-DE65-4FDC-8EBA-5A169A6E3CBB");

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
#if DEBUG_SG
        System.Diagnostics.Debugger.Launch();
#endif

        context.RegisterPostInitializationOutput(GenerateAbstracts);

        var resolvers = context.CompilationProvider.SelectMany((comp, _) => GetResolvers(comp)).Collect();

        var getServiceCheck = context.SyntaxProvider
                .CreateSyntaxProvider(
                    (syntax, _) => syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name: GenericNameSyntax { TypeArgumentList.Arguments: [var a], Identifier.ValueText: "GetService" } } },
                    (gsc, _) => (InvocationExpressionSyntax)gsc.Node)
                .Collect();

        var generatedResolvers = context.SyntaxProvider
                .CreateSyntaxProvider(
                    (syntax, _) => syntax is AttributeSyntax {Name : GenericNameSyntax { Identifier.ValueText: { } id} } && id.StartsWith("DependencyResolver"),
                    (gsc, _) => (AttributeSyntax)gsc.Node)
                .Collect();

        var servicesContainers = context.SyntaxProvider
                .ForAttributeWithMetadataName("SourceCrafter.DependencyInjection.Attributes.ServiceContainerAttribute",
                    (node, a) => true,
                    (t, c) => (t.SemanticModel, Class: (INamedTypeSymbol)t.TargetSymbol))
                .Collect();

        context.RegisterSourceOutput(context.CompilationProvider
            .Combine(servicesContainers)
            .Combine(getServiceCheck)
            .Combine(resolvers), static (p, info) =>
        {
            var (((compilation, servicesContainers), serviceCheck), resolvers) = info;

            Map<string, byte> uniqueName = new(StringComparer.Ordinal);

            var sb = new StringBuilder("/*").AppendLine();
            int start = sb.Length;

            Set<Diagnostic> diagnostics = new(e => e.Location.ToString());

            try
            {
                foreach (var item in servicesContainers)
                {
                    new ServiceContainerGenerator(compilation, item.SemanticModel, item.Class, diagnostics)
                        .TryBuild(serviceCheck, uniqueName, p.AddSource);
                }
            }
            catch (Exception e)
            {
                sb.AppendLine(e.ToString());
            }

            diagnostics.ForEach(item => p.ReportDiagnostic(item));

            if (sb.Length > start)
            {
                p.AddSource("errors", SourceText.From(sb.ToString()));
            }
        });
    }

    private static IEnumerable<INamedTypeSymbol> GetResolvers(Compilation comp)
    {
        foreach (var referencedAssembly in comp.ExternalReferences)
        {
            if (comp.GetAssemblyOrModuleSymbol(referencedAssembly) is not IAssemblySymbol assemblySymbol) continue;

            foreach (var attribute in assemblySymbol.GetAttributes())
            {
                if (attribute.AttributeClass is INamedTypeSymbol cls && cls?.ToGlobalNonGenericNamespace() is "global::SourceCrafter.DependencyInjection.Interop.DependencyResolverAttribute")
                {
                    yield return cls;
                }
            }
        }
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
        const string Source = @"#nullable enable
#pragma warning disable CS9113

namespace SourceCrafter.DependencyInjection.Attributes
{
	[global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Interface | global::System.AttributeTargets.Parameter, AllowMultiple = true)]
	public class ServiceContainerAttribute : Attribute;

	[global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Interface, AllowMultiple = true)]
	public class SingletonAttribute<TImplementation>(object? key = null, string? factoryOrInstance = null, bool cache = true) : SingletonAttribute(key, typeof(TImplementation), null, factoryOrInstance, cache);

	[global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Interface, AllowMultiple = true)]
	public class SingletonAttribute<T, TImplementation>(object? key = null, string? factoryOrInstance = null, bool cache = true) : SingletonAttribute(key, typeof(TImplementation), typeof(T), factoryOrInstance, cache);

	[global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Interface | global::System.AttributeTargets.Parameter, AllowMultiple = true)]
	public class SingletonAttribute(object? key = null, global::System.Type? impl = null, global::System.Type? iface = null, string? factoryOrInstance = null, bool cache = true) : Attribute;

	[global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Interface, AllowMultiple = true)]
	public class ScopedAttribute<TImplementation>(object? key = null, string? factoryOrInstance = null, bool cache = true) : SingletonAttribute(key, typeof(TImplementation), null, factoryOrInstance, cache);

	[global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Interface, AllowMultiple = true)]
	public class ScopedAttribute<T, TImplementation>(object? key = null, string? factoryOrInstance = null, bool cache = true) : SingletonAttribute(key, typeof(TImplementation), typeof(T), factoryOrInstance, cache);

	[global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Interface | global::System.AttributeTargets.Parameter, AllowMultiple = true)]
	public class ScopedAttribute(object? key = null, global::System.Type? impl = null, global::System.Type? iface = null, string? factoryOrInstance = null, bool cache = true) : Attribute;

	[global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Interface, AllowMultiple = true)]
	public class TransientAttribute<TImplementation>(object? key = null, string? factoryOrInstance = null, bool cache = true) : SingletonAttribute(key, typeof(TImplementation), null, factoryOrInstance, cache);

	[global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Interface, AllowMultiple = true)]
	public class TransientAttribute<T, TImplementation>(object? key = null, string? factoryOrInstance = null, bool cache = true) : SingletonAttribute(key, typeof(TImplementation), typeof(T), factoryOrInstance, cache);

	[global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Interface | global::System.AttributeTargets.Parameter, AllowMultiple = true)]
	public class TransientAttribute(object? key = null, global::System.Type? impl = null, global::System.Type? iface = null, string? factoryOrInstance = null, bool cache = true) : Attribute;
}

namespace SourceCrafter.DependencyInjection
{
	public interface IServiceProvider<TDependency> : IServiceProvider
	{
		TDependency GetService();
	}
	public interface IKeyedServiceProvider<TEnumKey, TDependency> : IKeyedServiceProvider<TEnumKey> where TEnumKey : struct, Enum
	{
		TDependency GetService(TEnumKey key);
	}
	public interface IAsyncServiceProvider<TDependency>
	{
		global::System.Threading.Tasks.ValueTask<TDependency> GetServiceAsync(global::System.Threading.CancellationToken cancellationToken = default);
	}
	public interface IKeyedAsyncServiceProvider<TEnumKey, TDependency> : IKeyedServiceProvider<TEnumKey> where TEnumKey : struct, Enum
	{
		global::System.Threading.Tasks.ValueTask<TDependency> GetServiceAsync(TEnumKey key, global::System.Threading.CancellationToken cancellationToken = default);
	}
	public interface IServiceProvider
	{
		TDependency GetService<TDependency>();
	}
	public interface IKeyedServiceProvider<TEnumKey> where TEnumKey : struct, Enum
	{
		TDependency GetService<TDependency>(TEnumKey key); 
    }
	public interface IAsyncServiceProvider 
    { 
        global::System.Threading.Tasks.ValueTask<TDependency> GetServiceAsync<TDependency>(global::System.Threading.CancellationToken cancellationToken = default);
	}
	public interface IKeyedAsyncServiceProvider<TEnumKey> where TEnumKey : struct, Enum
	{
		global::System.Threading.Tasks.ValueTask<TDependency> GetServiceAsync<TDependency>(TEnumKey key, global::System.Threading.CancellationToken cancellationToken = default);
	}
}

#pragma warning restore CS9113";

        producer.AddSource("SourceCrafter.DependencyInjection.Attributes", Source);
    }

}

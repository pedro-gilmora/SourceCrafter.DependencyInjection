
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SourceCrafter.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

#pragma warning disable RS1041 // Las extensiones del compilador deben implementarse en ensamblados que tengan como destino netstandard2.0
[Generator(LanguageNames.CSharp)]
#pragma warning restore RS1041 // Las extensiones del compilador deben implementarse en ensamblados que tengan como destino netstandard2.0
#pragma warning disable CA1050 // Declarar tipos en espacios de nombres
internal sealed partial class Containers : IIncrementalGenerator
#pragma warning restore CA1050 // Declarar tipos en espacios de nombres
{
    internal const string
        BaseAttributesNS = "SourceCrafter.DependencyInjection.Attributes",
        GlobalBaseAttributeNS = $"global::{BaseAttributesNS}",
        ServiceContainerFullTypeName = $"{BaseAttributesNS}.ServiceContainerAttribute",
        CancelTokenFQMetaName = "global::System.Threading.CancellationToken",
        EnumFQMetaName = "global::System.Enum",
        KeyParamName = "key",
        NameFormatParamName = "nameFormat",
        SourceParamName = "source",
        ImplParamName = "impl",
        IfaceParamName = "iface",
        SingletonAttr = $"{GlobalBaseAttributeNS}.SingletonAttribute",
        ScopedAttr = $"{GlobalBaseAttributeNS}.ScopedAttribute",
        TransientAttr = $"{GlobalBaseAttributeNS}.TransientAttribute",
        DependencyAttr = $"{GlobalBaseAttributeNS}.DependencyAttribute",
        ServiceContainerAttr = $"global::{ServiceContainerFullTypeName}",
        DefaultEnvName = @"""DOTNET_ENVIRONMENT""";


    private const string serviceContainerFullTypeName = "SourceCrafter.DependencyInjection.Attributes.ServiceContainerAttribute";
    internal readonly static string generatedCodeAttribute = ParseToolAndVersion();
    internal readonly static Guid generatorGuid = new("31C54896-DE65-4FDC-8EBA-5A169A6E3CBB");

    //static DependenciesServer? server;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
#if DEBUG_SG
        System.Diagnostics.Debugger.Launch();
#endif

        var msResolverCalls = context.SyntaxProvider
                .CreateSyntaxProvider(
                    ScanMsDIGerServiceCalls,
                    GetInvokeInfos)
                .Where(static info => info is not null)
                .Collect()
                .WithTrackingName("AssemblyExternalProvidersRegistrations");

        var servicesContainers = context.SyntaxProvider
                .ForAttributeWithMetadataName(serviceContainerFullTypeName,
                    static (node, a) => true,
                    static (t, c) => TryParseContainer(t.SemanticModel, (INamedTypeSymbol)t.TargetSymbol)!)
                .Where(e => e is not null)
                .Combine(msResolverCalls)
                .Select(InterceptorsAppender)
                .WithComparer(new EmitterEqualityComparer())
                .Collect()
                .WithTrackingName("SourceCrafterEmitters");

        context.RegisterSourceOutput(
            servicesContainers
                .Combine(msResolverCalls),
            static (context, artifacts) =>
                {
                    try
                    {
                        var (emitters, msCalls) = artifacts;

                        if (msCalls.Length > 0)
                        {
                            foreach (var item in msCalls)
                            {
                                if (item.InvalidAsyncTypeArg)
                                    context.ReportDiagnostic(
                                        ServiceContainerDiagnostics.InvalidAsyncTypeArgument(item.Location, item.AsyncKind, item.MethodName));
                                if (!item.Acknowledged)
                                    context.ReportDiagnostic(
                                        ServiceContainerDiagnostics.UncoveredGenericResolver(item.Location, item.ReturnType, item.ContainerTypeFullName, item.IsScopedCall));
                            }
                        }

                        msCalls.Clear();
                        msCalls = default;

                        var requiresTaskExtensions = false;
                        var interceptorsCount = 0;
                        Dictionary<string, byte> countedNames = [];

                        foreach (Emitter emitter in emitters)
                        {
                            using (emitter)
                            {
                                emitter.Emit(countedNames, ref requiresTaskExtensions, ref interceptorsCount, out var file, out var code);
                                context.AddSource(file + ".g", code);

                                foreach (var item in emitter.Diagnostics)
                                {
                                    context.ReportDiagnostic(item);
                                }
                            }
                        }

                        emitters.Clear();
                        emitters = default;

                        if (interceptorsCount > 0)
                            context.AddSource("Utils.g", UtilsFileContent);

                        if (requiresTaskExtensions)
                            context.AddSource("TaskExtensions.g", TaskExtensionsFileContent);
                    }
                    catch (Exception e)
                    {
                        DiagnosticDescriptor rule = new(
                            id: "SCDIE00",
                            title: "Error at SourceCrafter.DependencyInjection generation time: " + e.ToString(),
                            messageFormat: e.Message,
                            category: "SourceCrafter.DependencyInjection.FatalError",
                            defaultSeverity: DiagnosticSeverity.Error,
                            isEnabledByDefault: true,
                            description: e.ToString()
                        );

                        context.ReportDiagnostic(Diagnostic.Create(rule, null));
                    }
                });
    }

    private const string UtilsFileContent = @"
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class InterceptsLocationAttribute(int version, string data) : global::System.Attribute;
}";

    private const string TaskExtensionsFileContent = @"#nullable enable
using global::System.Threading.Tasks;

namespace SourceCrafter.DepedencyInjection.Extensions;

internal static class Extensions
{
	internal static async ValueTask TryDisposeAsync<T>(this ValueTask<T>? vTask) where T : IAsyncDisposable
	{
		if (vTask is { } task && (task.IsCompletedSuccessfully ? task.GetAwaiter().GetResult() : await task) is IAsyncDisposable result)
		{
			await result.DisposeAsync();
		}
	}
	internal static async ValueTask TryDispose<T>(this ValueTask<T>? vTask) where T : IDisposable
	{
		if (vTask is { } task && (task.IsCompletedSuccessfully ? task.GetAwaiter().GetResult() : await task) is IDisposable result)
		{
			result.Dispose();
		}
	}
	internal static async ValueTask TryDisposeAsync<T>(this Task<T>? task) where T : IAsyncDisposable
	{
		if (task is not null && (task.IsCompletedSuccessfully ? task.GetAwaiter().GetResult() : await task) is IAsyncDisposable result)
		{
			await result.DisposeAsync();
		}
	}
	internal static async ValueTask TryDispose<T>(this Task<T>? task) where T : IDisposable
	{
		if (task is not null && (task.IsCompletedSuccessfully ? task.GetAwaiter().GetResult() : await task) is IDisposable result)
		{
			result.Dispose();
		}
	}
}";

    private static Emitter InterceptorsAppender((Emitter Left, ImmutableArray<InvokeInfo> Right) context, CancellationToken c)
    {
        var (emitter, msCalls) = context;

        foreach (var serviceCall in msCalls)
            if (!serviceCall.Acknowledged
                && ((serviceCall.IsScopedCall && serviceCall.ContainerTypeFullName is "global::Microsoft.Extensions.DependencyInjection.IServiceScope")
                    || serviceCall.ContainerTypeFullName == emitter.ContainerFullTypeName
                    || serviceCall.ContainerTypeNameOnly == emitter.ClassName
                    || serviceCall.IsScopedCall))
            {
                if (emitter.DependencyValueBuilders.TryGetValue((serviceCall.ReturnType, serviceCall.Key), out var resolvers))
                {
                    if (serviceCall.IsMultiple)
                    {
                        foreach (var resolver in resolvers.Values)
                        {
                            emitter.AddOrUpdateIntercerceptor(
                                serviceCall,
                                true,
                                resolver.AsyncKind,
                                resolver.PassCancelToken,
                                resolver.AppendValue);
                        }
                    }
                    else if (resolvers.Values.LastOrDefault(e => e.Key.key == serviceCall.Key) is { } resolver2)
                    {
                        emitter.AddOrUpdateIntercerceptor(
                            serviceCall,
                            false,
                            resolver2.AsyncKind,
							resolver2.PassCancelToken,
                            resolver2.AppendValue);
                    }
                }
                else if (serviceCall.Key == "" && serviceCall.IsMultiple 
                    && emitter.DependencyValueBuilders.SelectMany(dvb => dvb.Value.Where(rb => rb.Key.type == serviceCall.ReturnType).Select(e => e.Value)).ToArray() is { Length: > 0 } items)
                {
                    foreach (var resolver in items)
                    {
                        emitter.AddOrUpdateIntercerceptor(
                            serviceCall,
                            true,
							resolver.AsyncKind,
							resolver.PassCancelToken,
                            resolver.AppendValue);
                    }
                }
            }

        return emitter;
    }

    [GeneratedRegex("^GetRequired(?:Keyed)?(?:Value)?Services?(?:Async)?$")]
    internal static partial Regex MEDI_MethodDetector { get; }

    private static bool ScanMsDIGerServiceCalls(SyntaxNode syntax, CancellationToken _) =>
        syntax is MemberAccessExpressionSyntax
        {
            Parent: InvocationExpressionSyntax,
            Name.Identifier.ValueText: string name
        } && MEDI_MethodDetector.IsMatch(name);

    readonly static ImmutableArray<Lifetime> lifeTimes = [Lifetime.Singleton, Lifetime.Scoped, Lifetime.Transient];

    private static void AppendCancelToken(StringBuilder code, bool _, bool __)
    {
        code.Append("cancellationToken");
    }

    static bool TryGetLifetime(AttributeSyntax attrSyntax, ref INamedTypeSymbol attrClass, ref bool isExternal, out Lifetime lifetime)
    {
        if (GetLifetimeFromSyntax(attrSyntax, out lifetime)) return true;

        bool found;
        do
        {
            (isExternal, (found, lifetime)) = attrClass.FullGlobalQualifiedNonGenericName switch
            {
                SingletonAttr => (isExternal, (true, Lifetime.Singleton)),
                ScopedAttr => (isExternal, (true, Lifetime.Scoped)),
                TransientAttr => (isExternal, (true, Lifetime.Transient)),
                { } val => (val is not DependencyAttr, GetFromCtorSymbol(attrClass))
            };

            if (found) return true;

            isExternal = true;
        }
        while ((attrClass = attrClass?.BaseType!) is not null);

        return false;

        static (bool, Lifetime) GetFromCtorSymbol(INamedTypeSymbol attrClass)
        {
            foreach (var ctor in attrClass.Constructors)
                foreach (var param in ctor.Parameters)
                    if (param.Name.ToLower() is "lifetime" && param.HasExplicitDefaultValue)
                        return (true, (Lifetime)(byte)param.ExplicitDefaultValue!);

            return (false, default);
        }

        static bool GetLifetimeFromSyntax(AttributeSyntax attribute, out Lifetime lifetime)
        {
            foreach (var arg in attribute.ArgumentList?.Arguments ?? [])
            {
                if (arg is { NameColon.Name.Identifier.ValueText: "lifetime", Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: { } memberName } }
                    && Enum.TryParse(memberName, out lifetime))
                {
                    return true;
                }
            }

            lifetime = default;
            return false;
        }
    }

    private static InvokeInfo GetInvokeInfos(GeneratorSyntaxContext gsc, CancellationToken cancellationToken)
    {
        if (gsc.Node is not MemberAccessExpressionSyntax
            {
                Parent: InvocationExpressionSyntax invocation,
                Name: GenericNameSyntax { TypeArgumentList.Arguments: [{ } typeArgSyntax], Identifier.ValueText: { } methodName } method,
                Expression: IdentifierNameSyntax { } refVar
            } memberAccess
        ) 
            
            return null!;

        var model = gsc.SemanticModel;

        if (model.GetInterceptableLocation(invocation, cancellationToken) is not { } interceptor
            || model.GetTypeInfo(typeArgSyntax, cancellationToken).Type is not ITypeSymbol { } depTypeResult
            || model.GetSymbolInfo(refVar, cancellationToken).Symbol switch
            {
                ILocalSymbol local => (local, local.Type),
                IParameterSymbol parameter => (parameter, parameter.Type),
                IFieldSymbol field => (field, field.Type),
                IPropertySymbol property => (property, property.Type),
                _ => (default(ISymbol)!, default(ITypeSymbol)!)
            } is not ({ } _ref, var type)) 
            
            return null!;

        bool scopedInference = type.AsNonNullable().ToDisplayString().Equals("Microsoft.Extensions.DependencyInjection.IServiceScope");

        (bool isCtor, type) = GetContainerSource(model, _ref, type, cancellationToken);

        var callContainerType = (type.Name is "Scoped" && type.ContainingType is not null
            ? type.ContainingType
            : type).AsNonNullable();

        var keyHash = invocation.ArgumentList.Arguments switch
        {
            [{ Expression: LiteralExpressionSyntax { Token.RawKind: (int)SyntaxKind.StringLiteralToken, Token.ValueText: string text } }, ..]
                => text,

            [{ Expression: IdentifierNameSyntax id }, ..]
                when TryGetIdentifier(model, id, cancellationToken, out string text) => text,

            [{ Expression: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax id } }, ..]
                when TryGetIdentifier(model, id, cancellationToken, out string text) => text,

            _ => ""
        };


        var AsyncKind = depTypeResult.TryGetAsyncType(out depTypeResult);
        var methodWithTaskTypeArg = AsyncKind > 0;

        if (AsyncKind is 0 && methodName.EndsWith("Async")) AsyncKind = AsyncKind.Task;

        return new(
            callContainerType.AllInterfaces.Any(i => i.GetAttributes().Any(IsGeneratedServiceContainer)),
            callContainerType.NameOnly,
            callContainerType.FullGlobalQualifiedName,
            methodName,
            scopedInference || (callContainerType.Name is "Scoped" && callContainerType.ContainingType is not null),
            AsyncKind,
            depTypeResult.FullGlobalQualifiedName,
            interceptor,
            keyHash,
            isCtor,
            methodName.Contains("Keyed", StringComparison.Ordinal),
            methodName.Contains("Services", StringComparison.Ordinal))
        {
            InvalidAsyncTypeArg = methodWithTaskTypeArg,
            Location = memberAccess.GetLocation()
        };
    }

    private static (bool isCtor, ITypeSymbol finalType) GetContainerSource(SemanticModel model, ISymbol _ref, ITypeSymbol type, CancellationToken token)
    {
        foreach (var declaration in _ref.DeclaringSyntaxReferences.Reverse())
        {
            switch ((declaration.GetSyntax(token) as VariableDeclaratorSyntax)?.Initializer?.Value)
            {
                case ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax:
                    return (true, type);

                case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax source } }:
                    return (false, model.GetSymbolInfo(source, token).Symbol switch
                    {
                        ILocalSymbol local => local.Type,
                        IParameterSymbol parameter => parameter.Type,
                        IFieldSymbol field => field.Type,
                        IPropertySymbol property => property.Type,
                        _ => type,
                    });
            }
        }
        return (false, type);
    }

    private static bool TryGetIdentifier(SemanticModel model, IdentifierNameSyntax id, CancellationToken cancellationToken, out string text)
    {
        if (model.GetSymbolInfo(id, cancellationToken).Symbol is IFieldSymbol { IsConst: true, HasConstantValue: true, ConstantValue: string _text })
        {
            text = _text;
            return true;
        }
        text = null!;
        return false;
    }

    static bool IsGeneratedServiceContainer(AttributeData attrData) =>
        attrData.AttributeClass?.FullGlobalQualifiedName.EndsWith(serviceContainerFullTypeName) ?? false;

    private static string ParseToolAndVersion()
    {
        string name = "SourceCrafter.DependencyInjection";

        int i = 0;

        if (Assembly.GetExecutingAssembly().FullName?.Trim() is not { Length: > 0 } fullName) return "";

        foreach (var item in fullName.Split(','))
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
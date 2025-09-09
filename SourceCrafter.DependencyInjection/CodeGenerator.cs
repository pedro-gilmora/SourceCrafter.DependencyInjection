//namespace System.Runtime.CompilerServices;

using global::System.ComponentModel;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using SourceCrafter.DependencyInjection;

using System;
//using SourceCrafter.DependencyInjection;
//using SourceCrafter.DependencyInjection.Attributes;

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

[Generator]
public sealed class CodeGenerator : IIncrementalGenerator
{

    //private readonly DependencyMapDictionary containers = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource cancellationTokenSource = new();

    ~CodeGenerator()
    {
        //containers.Clear();
        cancellationTokenSource.Cancel();
    }

    private const string serviceContainerFullTypeName = "SourceCrafter.DependencyInjection.Attributes.ServiceContainerAttribute";
    internal readonly static string generatedCodeAttribute = ParseToolAndVersion();
    internal readonly static Guid generatorGuid = new("31C54896-DE65-4FDC-8EBA-5A169A6E3CBB");
    //static DependenciesServer? server;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
#if DEBUG_SG
        System.Diagnostics.Debugger.Launch();
#endif

        var getExternal = context.SyntaxProvider
                .CreateSyntaxProvider(
                    (syntax, _) => syntax is AttributeSyntax { Parent: AttributeListSyntax { Target.Identifier.ValueText: "assembly" } },
                    (gsc, _) =>
                    {
                        if (gsc.SemanticModel.GetTypeInfo(gsc.Node).Type is { BaseType.Name: "SingletonAttribute" or "ScopedAttribute" or "TransientAttribute" } type)
                            return gsc.SemanticModel.Compilation.Assembly.GetAttributes().Where(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, type));
                        return [];
                    })
                .SelectMany((info, _) => info)
                .Collect();
        var scopedUsage = context.SyntaxProvider
                .CreateSyntaxProvider(
                    (syntax, _) => syntax is MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax },
                    GetInvokeInfos)
                .Where(info => info is not null)
                .Collect();

        var servicesContainers = context.SyntaxProvider
                .ForAttributeWithMetadataName(serviceContainerFullTypeName,
                    (node, a) => true,
                    (t, c) => (t.SemanticModel, Class: (INamedTypeSymbol)t.TargetSymbol))
                .Collect();

        // Different containers registry for dependencies providers
        //var isServerRunning = false;

        context.RegisterSourceOutput(context.CompilationProvider
            .Combine(servicesContainers)
            .Combine(getExternal)
            .Combine(scopedUsage)
            , (context, info) =>
            {
                try
                {
                    var (((compilation, servicesContainers), externals), serviceCall) = info;

                    HashSet<Diagnostic> diagnostics = new(new DiagnosticLocationComparer());
                    Map<string, byte> uniqueNames = new(StringComparer.Ordinal);
                    bool addExtensions = false;

                    var cancelTokenType = compilation.GetTypeByMetadataName(Helpers.CancelTokenFQMetaName)!;

                    foreach (var (model, cls) in servicesContainers)
                    {
                        TryGenerateContainer(compilation, model, cls, diagnostics, uniqueNames, ref addExtensions, cancelTokenType, context.AddSource);
                    }

                    if (addExtensions)
                    {
                        context.AddSource("TaskExtensions.g", @"using global::System.Threading.Tasks;

namespace SourceCrafter.DepedencyInjection.Extensions;

internal static class Extensions
{
	internal static async ValueTask TryDisposeAsync<T>(this ValueTask<T>? vTask) where T : IAsyncDisposable
	{
		if (vTask is { } task && (task.IsCompletedSuccessfully ? task.GetAwaiter().GetResult() : await task) is { } result)
		{
			await result.DisposeAsync();
		}
	}
	internal static async ValueTask TryDispose<T>(this ValueTask<T>? vTask) where T : IDisposable
	{
		if (vTask is { } task && (task.IsCompletedSuccessfully ? task.GetAwaiter().GetResult() : await task) is { } result)
		{
			result.Dispose();
		}
	}
	internal static async ValueTask TryDisposeAsync<T>(this Task<T>? task) where T : IAsyncDisposable
	{
		if (task is not null && (task.IsCompletedSuccessfully ? task.GetAwaiter().GetResult() : await task) is { } result)
		{
			await result.DisposeAsync();
		}
	}
	internal static async ValueTask TryDispose<T>(this Task<T>? task) where T : IDisposable
	{
		if (task is not null && (task.IsCompletedSuccessfully ? task.GetAwaiter().GetResult() : await task) is { } result)
		{
			result.Dispose();
		}
	}
}");
                    }
                }
                catch (Exception e)
                {
                    DiagnosticDescriptor rule = new(
                        id: "SCDIE00",
                        title: e.Message,
                        messageFormat: e.Message,
                        category: "SourceCrafter.DependencyInjection.FatalError",
                        defaultSeverity: DiagnosticSeverity.Error,
                        isEnabledByDefault: true,
                        description: e.Message.ToString()
                    );

                    context.ReportDiagnostic(Diagnostic.Create(rule, null));
                }
            });
    }

    private static void TryGenerateContainer(
        Compilation compilation,
        SemanticModel model,
        INamedTypeSymbol providerType,
        HashSet<Diagnostic> diagnostics,
        Map<string, byte> uniqueNames,
        ref bool addExtensions,
        INamedTypeSymbol cancelTokenType,
        Action<string, string> addSource)
    {
        var declaration = providerType.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        var providerTypeId = SymbolEqualityComparer.Default.GetHashCode(providerType);
        var providerId = SymbolEqualityComparer.Default.GetHashCode(providerType);
        var compilationId = compilation.GetHashCode();
        var providerTypeName = providerType.ToGlobalNamespaced();
        var attributes = providerType.GetAttributes();
        var hasScopedDependencies = false;

        HashSet<string> externalAssemblies = [];
        StringBuilder code = new();
        ResolverBuilder cancelDepInfo = new("Scoped CancellationToken token")
        {
            Key = (Lifetime.Transient, SymbolEqualityComparer.Default.GetHashCode(cancelTokenType), Helpers.EmptyStringHashCode),
            BuildValue = _ => code.Append("cancellationToken")
        };
        var defaultKeyComparer = EqualityComparer<(Lifetime lifetime, int typeHash, int keyHash)>.Default;
        Map<(Lifetime lifetime, int typeHash, int keyHash), ResolverBuilder> dependencyAsValueBuilders = new(defaultKeyComparer);
        Map<(Lifetime lifetime, int typeHash, int keyHash), string> methodNamesMap = new(defaultKeyComparer);
        HashSet<string> methodsRegistry = [];
        ImmutableArray<Lifetime> lifeTimes = [Lifetime.Singleton, Lifetime.Scoped, Lifetime.Transient];
        List<Action<List<Action>, List<DisposeBuilder>, List<DisposeBuilder>>> dependencyBuilders = [];

        Disposability
            containerDisposability = Disposability.None,
            scopedDisposability = Disposability.None;
        int
            asyncScopedDisposable = 0,
            asyncSingletonDisposable = 0,
            asyncScopedAsyncDisposable = 0,
            asyncSingletonAsyncDisposable = 0,
            scopedDisposable = 0,
            singletonDisposable = 0,
            scopedAsyncDisposable = 0,
            singletonAsyncDisposable = 0;

        foreach (var attr in attributes)
        {
            TryBuildService(attr, out _);

            bool TryBuildService(AttributeData? attr, out ResolverBuilder resolver, IParameterSymbol? param = null, ChildDependencyHandler? validateAsChildDependency = null)
            {
                bool
                    isSimpleTransient = false,
                     isExternal = false,
                     isCached = false,
                     isValid = false,
                    isFactory = false,
                    hasAsyncDependencies = false,
                    //isTransient = false,
                    useWhenAll = false,
                    needsCancelToken = false;

                int
                    keyHashCode = Helpers.EmptyStringHashCode,
                    typeHashCode = 0;

                string
                    name = string.Empty,
                    whenAll = string.Empty;

                string?
                    nameOrFormat = null;

                AsyncType
                    asyncType = default,
                    initialAsyncType = default;

                (Lifetime lifetime, int typeHash, int keyHash) key = default;

                ImmutableArray<IParameterSymbol>
                    defaultParamValues = [],
                    prms = [];
                Disposability
                    disposability = default;
                AttributeSyntax
                    attrSyntax = null!;
                INamedTypeSymbol?
                    attrClass;
                Lifetime
                    lifetime = default;
                ITypeSymbol?
                    interfaceType = null;
                ISymbol?
                    factory = null;
                SymbolKind
                    factoryKind = default;
                Map<(Lifetime lifetime, int typeHash, int keyHash), AsyncLocalResolver>
                    asyncLocalResolvers = new(defaultKeyComparer);
                List<ParamBuildOptions>
                    appendParams = [];
                ITypeSymbol
                    exportType = null!,
                    type = null!;

                resolver = null!;

                ref var existingOrNewValueBuilder = ref Unsafe.AsRef(in resolver);

                if (!IsValidServiceAttribute(attr, param))
                {
                    //(lifetime, exportType?.ToDisplayString(), type?.ToDisplayString(), name, false).Dump("Checking:");
                    return false;
                }

                //(lifetime, interfaceType?.ToDisplayString(), type.ToDisplayString(), name).Dump("Checking:");

                var deepParamsCount = 0;

                resolver = existingOrNewValueBuilder = ref dependencyAsValueBuilders.GetValueRefOrAddDefault(key, out var exists)!;

                var (backingFieldName, methodName) = ("", "");

                var typeFullName = type.ToGlobalNamespaced();
                var exportTypeFullName = exportType.ToGlobalNamespaced();

                if (validateAsChildDependency?.Invoke(
                    exists,
                    isValid,
                    lifetime,
                    existingOrNewValueBuilder?.AsyncType ?? asyncType,
                    existingOrNewValueBuilder?.ParamsLength ?? prms.Length,
                    BuildValue,
                    IsNull(type),
                    isExternal && name is "") is false)
                {
                    return false;
                }


                if (exists)
                {
                    //new { lifetime, name, exportType }.Dump("Duplicated service:");
                    diagnostics.Add(
                        ServiceContainerGeneratorDiagnostics
                            .DuplicateService(lifetime, name, attrSyntax, typeFullName, exportTypeFullName));

                    return false;
                }

                existingOrNewValueBuilder = new($"{lifetime} {(exportTypeFullName + (exportTypeFullName == typeFullName ? null : $"<{typeFullName}>"))} {name}".Trim())
                {
                    Key = key,
                    ExportTypeFullName = exportTypeFullName,
                    BuildValue = BuildValue,
                    AsyncType = asyncType,
                    ParamsLength = prms.Length
                };

                asyncLocalResolvers = existingOrNewValueBuilder.AsyncLocalResolvers;

                disposability = type.GetDisposability();

                if (isCached)
                {
                    if (lifetime is Lifetime.Scoped && disposability > scopedDisposability)
                        scopedDisposability = disposability;
                    else if (disposability > containerDisposability)
                        containerDisposability = disposability;
                }

                if (!isExternal && (param?.Type ?? type).IsPrimitive() && name is "")
                {
                    //exportTypeFullName.Dump($"Primitive {lifetime} type should be keyed:");
                    diagnostics.Add(
                        ServiceContainerGeneratorDiagnostics
                            .PrimitiveDependencyShouldBeKeyed(lifetime, attrSyntax, typeFullName, exportTypeFullName));
                }

                if (!(isExternal || (isFactory && !isCached) || isSimpleTransient)) dependencyBuilders.Add(BuildMethod);

                //$"{key}: {existingOrNewValueBuilder}".Dump();

                if (isSimpleTransient) return true;

                var paramsToResolve = prms.Length;

                byte paramPos = 0/*, valueTaskCount = 0, asyncParamCount = 0*/;

                Dictionary<int, HashSet<AsyncLocalResolver>> asyncParams = [];

                foreach (var prm in prms)
                {
                    var paramIndex = paramPos++;
                    var paramAsyncType = prm.Type.TryGetAsyncType(out var paramType);
                    var paramTypeHashCode = paramType.ToGlobalNamespaced().GetHashCode();

                    if (SymbolEqualityComparer.Default.Equals(prm.Type, cancelTokenType))
                    {
                        needsCancelToken = true;
                        appendParams.Add(new(cancelDepInfo.Key, AppendCancelToken));
                        continue;
                    }

                    var isPrimitiveParamType = paramType.IsPrimitive();

                    ResolverBuilder foundService = null!;
                    var foundAsyncType = AsyncType.None;
                    HashSet<(int, bool)> existingAsyncDepsCalls = [];
                    (Lifetime lifetime, int typeHash, int keyHash) resolvedKey = default;

                    if (prm.GetAttributes() is { Length: > 0 } paramAttrs)
                    {
                        foreach (var paramAttr in paramAttrs)
                        {
                            if (TryBuildService(paramAttr, out foundService, prm, ValidateChild))
                            {
                                break;
                            }
                        }
                    }

                    var keyHash = prm.Name.GetHashCode();
                    resolvedKey = foundService?.Key ?? default;

                    if (foundService is not null || lifeTimes.Any(t =>
                        dependencyAsValueBuilders.TryGetValue(resolvedKey = (t, paramTypeHashCode, keyHash), out foundService!)
                        || dependencyAsValueBuilders.TryGetValue(resolvedKey = (t, paramTypeHashCode, Helpers.EmptyStringHashCode), out foundService!)))
                    {
                        var buildParam = foundService!.BuildValue;
                        var foundExportTypeFullName = foundService.ExportTypeFullName;

                        appendParams.Add(new(resolvedKey, asyncContext =>
                        {
                            var awaits = asyncContext && foundService.AsyncType is not 0 && paramAsyncType is 0;

                            if (awaits)
                            {
                                code.Append("await ");
                                buildParam(asyncContext);
                            }
                            else if (paramAsyncType is not 0 && foundService.AsyncType is 0)
                            {
                                if (paramAsyncType is AsyncType.Task)
                                {
                                    code.Append("global::System.Threading.Tasks.Task.FromResult<")
                                        .Append(foundExportTypeFullName)
                                        .Append(">(");
                                    buildParam(asyncContext);
                                    code.Append(")");
                                }
                                else
                                {
                                    code.Append("new global::System.Threading.Tasks.ValueTask<")
                                        .Append(foundExportTypeFullName)
                                        .Append(">(");
                                    buildParam(asyncContext);
                                    code.Append(")");
                                }
                            }
                            else if (foundService.AsyncType is AsyncType.ValueTask && paramAsyncType is AsyncType.Task)
                            {
                                buildParam(asyncContext);
                                code.Append(".AsTask()");
                            }
                            else if (foundService.AsyncType is AsyncType.Task && paramAsyncType is AsyncType.ValueTask)
                            {
                                code.Append("new global::System.Threading.Tasks.ValueTask<")
                                .Append(foundExportTypeFullName)
                                .Append(">(");
                                buildParam(asyncContext);
                                code.Append(')');
                            }
                            else
                            {
                                buildParam(asyncContext);
                            }
                        }
                        ));

                        deepParamsCount += foundService.ParamsLength;
                        foundAsyncType = foundService.AsyncType;

                        if (foundAsyncType is not 0)
                        {
                            if (asyncType is 0)
                                asyncType = AsyncType.Task;

                            if (!hasAsyncDependencies)
                                hasAsyncDependencies = true;

                            AsyncLocalResolver resolved = null!;

                            if (paramAsyncType is 0)
                            {
                                if (asyncLocalResolvers.TryGetValue(resolvedKey, out resolved!))
                                {
                                    var isCachedResolved = resolvedKey.lifetime is not Lifetime.Transient;
                                    resolved.BuildAsyncLocal = BuildAsyncLocalResolver;

                                    if (resolved.ParamIndex == -1 || isCachedResolved)
                                        resolved.ParamIndex = paramIndex;

                                    if (!resolved.ResolvedBefore && resolved.ParamIndex > resolved.ResolvedByParamIndex)
                                        resolved.ResolvedBefore = isCachedResolved;
                                }
                                else
                                {
                                    asyncLocalResolvers.TryAdd(resolvedKey, resolved = new(resolvedKey)
                                    {
                                        ResolvedByParamIndex = paramIndex,
                                        ParamIndex = paramIndex,
                                        IsValueTask = foundAsyncType is AsyncType.ValueTask,
                                        ResolverDep = resolvedKey,
                                        BuildAsyncLocal = BuildAsyncLocalResolver
                                    });
                                }
                            }

                            foreach (var childValue in foundService.AsyncLocalResolvers.Values)
                            {
                                asyncLocalResolvers.TryAdd(childValue.Dep, resolved = new(childValue.Dep)
                                {
                                    IsValueTask = childValue.IsValueTask,
                                    ResolvedByParamIndex = paramIndex,
                                    ResolverDep = resolvedKey,
                                    ResolvedBefore = !childValue.IsValueTask && childValue.Dep.lifetime is not Lifetime.Transient,
                                    BuildAsyncLocal = BuildAsyncLocalResolver
                                });
                            }

                            AsyncLocalResolver BuildAsyncLocalResolver()
                            {
                                code.Append(@"
			var __v").Append(paramIndex).Append(" = ");

                                buildParam(false);

                                code.Append(";");

                                return resolved!;
                            }

                        }

                        continue;
                    }

                    if (paramType.TypeKind is not TypeKind.Interface)
                    {
                        TryBuildService(null, out _, prm, ValidateChild);
                    }

                    bool ValidateChild(bool childExists, bool isChildValid, Lifetime childLifetime, AsyncType childAsyncType, int childParamCount, Action<bool> buildParam, bool isNullChildType, bool isUnkeyedInternalPrimitive)
                    {
                        if (!hasAsyncDependencies && childAsyncType > 0) hasAsyncDependencies = true;

                        if (paramAsyncType is 0)
                        {
                            if (asyncLocalResolvers.TryGetValue(resolvedKey, out var resolved))
                            {
                                resolved.BuildAsyncLocal = BuildAsyncLocalResolver;
                            }
                            else
                            {
                                asyncLocalResolvers.TryAdd(resolvedKey, resolved = new(resolvedKey)
                                {
                                    ResolvedByParamIndex = paramIndex,
                                    IsValueTask = foundAsyncType is AsyncType.ValueTask,
                                    ResolverDep = resolvedKey,
                                    BuildAsyncLocal = BuildAsyncLocalResolver
                                });
                            }

                            AsyncLocalResolver BuildAsyncLocalResolver()
                            {
                                code.Append(@"
				var __v").Append(paramIndex).Append(" = ");

                                buildParam(false);

                                code.Append(";");

                                return resolved!;
                            }
                        }

                        if (childAsyncType > asyncType)
                        {
                            asyncType = childAsyncType;
                        }

                        if (childExists)
                        {
                            childParamCount += childParamCount;
                            appendParams.Add(new(resolvedKey, buildParam));
                            return false;
                        }
                        else if ((isNullChildType && isPrimitiveParamType) || (isUnkeyedInternalPrimitive && prm.Type.IsPrimitive()) || !isChildValid)
                        {
                            deepParamsCount += 1;
                            appendParams.Add(new(resolvedKey, AppendDefault));
                            return false;
                        }

                        return true;
                    }
                }

                if (asyncLocalResolvers.Count > 0)
                {
                    asyncLocalResolvers = asyncLocalResolvers.Values
                        .Where(lr => lr.ParamIndex > -1).OrderBy(lr => lr.ParamIndex)
                        .ToMap(i => i.Dep);

                    if (asyncLocalResolvers.Values.Where(i => !i.IsValueTask && !i.ResolvedBefore && i.ParamIndex > -1)
                                          .Select(i => "__v" + i.ParamIndex)
                                          .ToArray() is { Length: > 1 } vars)
                    {
                        whenAll = string.Join(", ", vars);
                    }

                    useWhenAll = asyncLocalResolvers.Values.Count(i => i.ParamIndex > -1 && !i.IsValueTask && !i.ResolvedBefore) > 2;
                }

                //if (asyncParamCount > 0 && valueTaskCount == asyncParamCount) asyncType = AsyncType.ValueTask;

                (backingFieldName, methodName) = GetResolverName();

                //existingOrNewValueBuilder.AsyncNestedDeps.Dump($"Async deps for {existingOrNewValueBuilder}");

                if (disposability is not 0 && isCached)
                {
                    switch (asyncType is not 0, lifetime, disposability)
                    {
                        case (true, Lifetime.Scoped, Disposability.Disposable): asyncScopedDisposable++; break;
                        case (true, Lifetime.Singleton, Disposability.Disposable): asyncSingletonDisposable++; break;
                        case (true, Lifetime.Scoped, Disposability.AsyncDisposable): asyncScopedAsyncDisposable++; break;
                        case (true, Lifetime.Singleton, Disposability.AsyncDisposable): asyncSingletonAsyncDisposable++; break;
                        case (false, Lifetime.Scoped, Disposability.Disposable): scopedDisposable++; break;
                        case (false, Lifetime.Singleton, Disposability.Disposable): singletonDisposable++; break;
                        case (false, Lifetime.Scoped, Disposability.AsyncDisposable): scopedAsyncDisposable++; break;
                        case (false, Lifetime.Singleton, Disposability.AsyncDisposable): singletonAsyncDisposable++; break;
                    }
                }

                existingOrNewValueBuilder.AsyncType = asyncType;

                return true;

                void BuildParams(
                    bool completeAsyncContext,
                    string newIndentedLine)
                {
                    var needsComma = false;
                    byte pos = 0;

                    foreach (var item in appendParams)
                    {
                        if (needsComma.Exchange(true)) code.Append(',');

                        code.Append(newIndentedLine);

                        if (asyncLocalResolvers.TryGetValue(item.Key, out var task))
                        {
                            if (completeAsyncContext && (!task.ResolvedBefore && !whenAll.Contains("__v" + pos)))
                            {
                                code.Append("await ");
                                code.Append("__v").Append(pos).Append(".ConfigureAwait(false)");
                            }
                            else
                            {
                                code.Append("__v").Append(pos).Append(".Result");
                                if (completeAsyncContext && task.ResolvedBefore)
                                    code.Append($" /* resolved previously by param {task.ResolvedByParamIndex} */");
                            }
                        }
                        else
                        {
                            item.Build(completeAsyncContext);
                        }

                        pos++;
                    }
                }

                void BuildMethod(List<Action> scopedExposers, List<DisposeBuilder> singletonDisposers, List<DisposeBuilder> scopedDisposers)
                {
                    //hasAsyncDependencies.Dump($"Has [{typeFullName}] async dependencies?");

                    if (isCached)
                    {
                        code.Append(@"
    private ");
                        if (lifetime is Lifetime.Singleton) code.Append("static ");

                        code.Append(GetTypeName(typeFullName)).Append("? ").Append(backingFieldName).Append(";");

                        if (disposability is not 0)
                        {
                            if (lifetime is Lifetime.Scoped)
                                scopedDisposers
                                    .Add(disposability is Disposability.Disposable ? BuildDisposerStatement : BuildAsyncDisposerStatment);
                            else if (lifetime is Lifetime.Singleton)
                                singletonDisposers
                                    .Add(disposability is Disposability.Disposable ? BuildDisposerStatement : BuildAsyncDisposerStatment);
                        }

                        void BuildDisposerStatement(bool awaits = true)
                        {
                            code.Append(@"
        ");

                            code.Append(backingFieldName);

                            code.Append("?.");

                            //if (asyncType > 0) code.Append("Try");

                            code.Append("Dispose();");
                        }

                        void BuildAsyncDisposerStatment(bool awaits = true)
                        {
                            code.Append(@"
        ");

                            if (asyncType > 0)
                            {
                                code.Append(awaits ? "await " : "return ")
                                    .Append(backingFieldName).Append(".TryDisposeAsync();");
                            }
                            else
                            {
                                if (awaits)
                                {

                                    code.Append("if(").Append(backingFieldName)
                                        .Append(type.IsValueType ? ".HasValue) " : " is not null) ")
                                        .Append("await ")
                                        .Append(backingFieldName);

                                    if (type.IsValueType) code.Append(".Value");

                                    code.Append(".DisposeAsync();");
                                }
                                else
                                {
                                    code.Append("return ")
                                        .Append(backingFieldName)
                                        .Append("?.DisposeAsync() ?? default!;");
                                }
                            }
                        }
                    }

                    code.Append(@"
    ").Append(lifetime is Lifetime.Scoped ? "private " : "public ");

                    if (!isCached && (hasAsyncDependencies/* || (asyncType is not 0 && factory is not null)*/)) code.Append("async ");

                    BuildSignature();

                    [MethodImpl(MethodImplOptions.AggressiveInlining)]
                    void BuildSignature()
                    {
                        code.Append(GetTypeName(exportTypeFullName)).Append(' ').Append(methodName);

                        if (asyncType is not 0 && (hasAsyncDependencies || needsCancelToken))
                        {
                            code.Append("(");
                            if (needsCancelToken) code.Append("global::System.Threading.CancellationToken cancellationToken = default");
                            code.Append(")");
                        }
                    }

                    if (lifetime is Lifetime.Scoped) scopedExposers.Add(BuildExposedSignature);

                    void BuildExposedSignature()
                    {
                        code.Append(@"
		public new ");

                        BuildSignature();

                        code.Append(@" 
			=> base.").Append(methodName);

                        if (asyncType is not 0 && (hasAsyncDependencies || needsCancelToken))
                        {
                            code.Append("(");
                            if (needsCancelToken) code.Append("cancellationToken");
                            code.Append(")");
                        }

                        code.Append(@";
");
                    }

                    if (!isCached)
                    {
                        code.Append(@" 
        => ");

                        if (isFactory)
                        {
                            BuildFactoryCaller(true);
                        }
                        else
                        {
                            BuildInstance(true);
                        }
                        code.Append(@";
");
                    }
                    else
                    {
                        code.Append(@"
    {");

                        if (asyncType is 0 || !(hasAsyncDependencies || needsCancelToken))
                        {
                            var isValueType = asyncType is 0 ? type.IsValueType : asyncType is AsyncType.ValueTask;

                            code.Append(@"
        get
        {
            if(").Append(backingFieldName).Append(isValueType ? ".HasValue" : " is not null").Append(") return ").Append(backingFieldName);

                            if (isValueType) code.Append(".Value");

                            code.Append(@";
				
            lock(this)
			
			return ").Append(backingFieldName).Append(@" ??= ");

                            if (isFactory)
                            {
                                BuildFactoryCaller(false);
                            }
                            else
                            {
                                BuildInstance(false);
                            }

                            code.Append(@";
        }");
                        }
                        else
                        {
                            var isAsyncEmptyFactory = factory is not null && !needsCancelToken;

                            code.Append(@"
        if(").Append(backingFieldName).Append(asyncType is AsyncType.ValueTask ? ".HasValue" : " is not null").Append(") return ").Append(backingFieldName);

                            if (asyncType is AsyncType.ValueTask) code.Append(".Value");

                            code.Append(';');

                            if (isAsyncEmptyFactory)
                            {
                                code.Append(@"
					
		lock(this)
		
		return ").Append(backingFieldName).Append(" ??= ");

                                BuildFactoryCaller(false);

                                code.Append(";");
                            }
                            else
                            {
                                code.Append(@"
					
		lock(this)
		{
			if(").Append(backingFieldName).Append(" is not null) return ").Append(backingFieldName).Append(@";
");
                                //ct = global::System.Threading.CancellationTokenSource.CreateLinkedTokenSource(ct, __scopedCancellationTokenSrc.Token).Token;

                                foreach (var resolver in asyncLocalResolvers.Values)
                                {
                                    if (resolver.ParamIndex > -1) resolver.BuildAsyncLocal();
                                }

                                if (asyncLocalResolvers.Count > 0)
                                {
                                    code.Append(@"
");
                                }

                                code.Append(@"
			return ").Append(backingFieldName).Append(" = ");

                                var useAnd = false;

                                foreach (var param in asyncLocalResolvers.Values)
                                {
                                    if (param.ParamIndex == -1) continue;
                                    if (useAnd.Exchange(true)) code.Append(@"
					&& ");

                                    code.Append("__v").Append(param.ParamIndex).Append(".IsCompletedSuccessfully");
                                }

                                code.Append(@"
				? global::System.Threading.Tasks.Task.FromResult<").Append(exportTypeFullName).Append(@">(
					");

                                if (isFactory)
                                {
                                    BuildFactoryCaller(false, @"
						");
                                }
                                else
                                {
                                    BuildInstance(false, @"
						");
                                }

                                code.Append(@")
				: CompleteAsync();

			async global::System.Threading.Tasks.Task<").Append(exportTypeFullName).Append(@"> CompleteAsync()
			{");

                                if (whenAll.Length > 1)
                                {
                                    code.Append(@"
				await global::System.Threading.Tasks.Task.WhenAll(")
                                        .Append(whenAll)
                                        .Append(@");
");
                                }

                                code.Append(@"				
				return ");

                                if (isFactory)
                                {
                                    BuildFactoryCaller(true, @"
					");
                                }
                                else
                                {
                                    BuildInstance(true, @"
					");
                                }

                                code.Append(@";
			}
		}");
                            }
                        }
                        code.Append(@"
    }
");
                    }
                }

                string GetTypeName(string typeName)
                {
                    //{(initialAsyncType > 0 && asyncType > initialAsyncType ? "Value" : "")}
                    return asyncType > 0
                        ? $"global::System.Threading.Tasks.{(initialAsyncType is AsyncType.ValueTask ? "Value" : null)}Task<{exportTypeFullName}>"
                        : typeName;
                }

                void BuildValue(bool asyncContext = false)
                {
                    if (!isCached && isFactory)
                    {
                        //if (asyncContext && asyncType is not 0) code.Append("await ");

                        BuildFactoryCaller(asyncContext);
                    }
                    else if (!isCached && !isExternal)
                    {
                        BuildInstance(asyncContext);
                    }
                    else
                    {
                        //if (asyncContext && (asyncType is not 0)) code.Append("await ");

                        BuildCachedCaller();
                    }
                }

                void BuildCachedCaller(string newIndentedLine = @"
				")
                {
                    code.Append(methodName);

                    if (asyncType is not 0 && (hasAsyncDependencies || needsCancelToken))
                    {
                        code.Append("(");
                        if (needsCancelToken) code.Append("cancellationToken");
                        code.Append(")");
                    }
                }

                void BuildInstance(bool isAsyncContext, string newIndentedLine = @"
				")
                {
                    code.Append("new ")
                        .Append(typeFullName)
                        .Append('(');

                    BuildParams(isAsyncContext, newIndentedLine);

                    code.Append(')');
                }

                void BuildFactoryCaller(bool allowAwait, string newIndentedLine = @"
				")
                {
                    switch (factory)
                    {
                        case IMethodSymbol { ContainingType: { } containingType, IsStatic: { } isStatic } method:

                            if (isStatic) AppendFactoryContainingType(code, containingType);

                            if (method is { ReturnType.Name: "Task" or "ValueTask", TypeArguments: { IsDefaultOrEmpty: false } and [{ } argType] }
                                && SymbolEqualityComparer.Default.Equals(argType, type))
                            {
                                code.Append(method.Name)
                                    .Append('<')
                                    .Append(exportTypeFullName)
                                    .Append(">(");

                                BuildParams(allowAwait, newIndentedLine);

                                code.Append(')');
                            }
                            else
                            {
                                code.Append(method.Name)
                                    .Append('(');

                                BuildParams(allowAwait, newIndentedLine);

                                code.Append(')');
                            }

                            break;

                        case IPropertySymbol { IsIndexer: bool isIndexer, ContainingType: { } containingType, IsStatic: { } isStatic } prop:

                            if (isStatic) AppendFactoryContainingType(code, containingType);

                            if (isIndexer)
                            {
                                code.Append(prop.Name)
                                    .Append('[');

                                BuildParams(allowAwait, newIndentedLine);

                                code.Append(']');
                            }
                            else
                            {
                                code.Append(prop.Name);
                            }

                            break;


                        case IFieldSymbol { ContainingType: { } containingType, IsStatic: { } isStatic } field:

                            if (isStatic) AppendFactoryContainingType(code, containingType);

                            code.Append(field.Name);

                            break;

                        default:

                            AppendDefault();

                            break;
                    }
                }


                void AppendFactoryContainingType(StringBuilder code, INamedTypeSymbol containingType)
                {
                    if (SymbolEqualityComparer.Default.Equals(containingType, providerType))
                        return;

                    code.Append(containingType.ToGlobalNamespaced()).Append('.');
                }

                void AppendDefault(bool _ = false)
                {
                    code.Append("default");

                    if (type?.IsNullable() is false) code.Append('!');
                }

                (string, string) GetResolverName()
                {
                    var methodName = nameOrFormat is not null
                        ? string.Format(nameOrFormat, name.Pascalize()!).RemoveDuplicates()
                        : SanitizeTypeName(type ?? exportType, lifetime, name.Pascalize()!);

                    methodName = isExternal ? methodName : factory?.Name ?? methodName;

                    if (factory != null && isCached && !methodName.EndsWith("Cached") && !methodName.EndsWith("Cache"))
                        methodName += "Cached";

                    var fieldName = "_" + methodName.Camelize();

                    if (!(methodName.Contains("Async") || methodName.Contains("Task")) && asyncType is not 0)
                        (methodName, fieldName) = ((!isExternal && factory is null ? "Get" : "") + methodName + "Async", fieldName + "Task");

                    //if (!isExternal && factory is null) methodName = "Get" + methodName;

                    return (fieldName, methodName);
                }

                bool IsValidServiceAttribute(AttributeData? attr, IParameterSymbol? fromParameter = null)
                {
                    if (attr is not { AttributeClass: { } _attrClass, ApplicationSyntaxReference: { } attrSyntaxRef }
                        || _attrClass.ToGlobalNamespaced() is Helpers.ServiceContainerAttr
                        || attrSyntaxRef.GetSyntax() is not AttributeSyntax { } _attrSyntax
                        || !TryGetAttributeParamsDefinition(model.GetSymbolInfo(_attrSyntax), out ImmutableArray<IParameterSymbol> attrParams)
                        || !TryGetLifetime(_attrSyntax, ref _attrClass, ref isExternal, out lifetime))
                    {
                        return false;
                    }

                    attrSyntax = _attrSyntax;
                    attrClass = _attrClass;

                    if (isExternal = _attrClass.ContainingNamespace.ToDisplayString() != Helpers.BaseAttributesNS)
                        externalAssemblies.Add(_attrClass.ContainingAssembly.MetadataName.Replace(".Metadata", ""));

                    if (attr.AttributeClass!.TypeArguments.Length > 0 is { } isGeneric)
                    {
                        switch (_attrClass!.TypeArguments)
                        {
                            case [{ } t1, { } t2, ..]:

                                interfaceType = t1;
                                type = t2;

                                break;

                            case [{ } t1]:

                                type = t1;

                                break;
                        }
                    }

                    foreach (var (param, arg) in GetAttrParamsMap(attrParams, _attrSyntax.ArgumentList?.Arguments ?? []))
                    {
                        switch (param.Name)
                        {
                            case Helpers.ImplParamName when !isGeneric && arg is { Expression: TypeOfExpressionSyntax { Type: { } _type } }:

                                type = (ITypeSymbol)model!.GetSymbolInfo(_type).Symbol!;

                                continue;

                            case Helpers.IfaceParamName when fromParameter?.Type?.TypeKind is not TypeKind.Interface && !isGeneric && arg is { Expression: TypeOfExpressionSyntax { Type: { } type } }:

                                interfaceType = (ITypeSymbol)model!.GetSymbolInfo(type).Symbol!;

                                continue;

                            case Helpers.KeyParamName when GetStringExpressionOrValue(model, param!, arg, out var keyValue):

                                keyHashCode = (name = keyValue).GetHashCode();

                                continue;

                            case Helpers.NameFormatParamName when GetStringExpressionOrValue(model, param, arg, out var keyValue):

                                nameOrFormat = keyValue;

                                continue;

                            case Helpers.SourceParamName

                                when arg?.Expression is InvocationExpressionSyntax
                                {
                                    Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" },
                                    ArgumentList.Arguments: [{ } methodRef]
                                }:

                                switch (model.GetSymbolInfo(methodRef.Expression))
                                {
                                    case { Symbol: (IFieldSymbol or IPropertySymbol) and { Kind: var kind } fieldOrProp }:

                                        factory = fieldOrProp;
                                        factoryKind = kind;
                                        isFactory = true;
                                        initialAsyncType = asyncType = ((fieldOrProp as IFieldSymbol)?.Type ?? ((IPropertySymbol)fieldOrProp).Type).TryGetAsyncType(out var returnType);

                                        if (returnType.TypeKind is TypeKind.Interface || returnType.IsAbstract)
                                            interfaceType ??= returnType;
                                        else
                                            type ??= returnType;


                                        continue;

                                    case { CandidateReason: CandidateReason.MemberGroup, CandidateSymbols: [IMethodSymbol { ReturnsVoid: false, IsStatic: true } method] }:

                                        factory = method;
                                        factoryKind = SymbolKind.Method;
                                        defaultParamValues = method.Parameters;
                                        initialAsyncType = asyncType = method.ReturnType.TryGetAsyncType(out returnType);
                                        isFactory = true;

                                        if (returnType.TypeKind is TypeKind.Interface || returnType.IsAbstract)
                                            interfaceType ??= returnType;
                                        else
                                            type ??= returnType;

                                        continue;
                                }

                                continue;

                            case "disposability" when param.HasExplicitDefaultValue:

                                disposability = (Disposability)(byte)param.ExplicitDefaultValue!;

                                continue;
                        }
                    }

                    exportType ??= interfaceType ?? type!;

                    if (!(isValid = exportType is not null && type is not null && attrClass is not null && _attrSyntax is not null)) return false;

                    if (!hasScopedDependencies && lifetime is Lifetime.Scoped)
                    {
                        hasScopedDependencies = true;
                    }

                    if (asyncType is not 0 && factoryKind is SymbolKind.Method && !((IMethodSymbol)factory!).Parameters.Any(p => p.Type.ToDisplayString() is Helpers.CancelTokenFQMetaName))
                    {
                        //factory.ToDisplayString().Dump("Cancellation token should be provided");
                        diagnostics.Add(ServiceContainerGeneratorDiagnostics.CancellationTokenShouldBeProvided(factory, attrSyntax));
                    }

                    if (keyHashCode == Helpers.EmptyStringHashCode && fromParameter?.Name is { } paramName)
                    {
                        keyHashCode = (name = paramName).GetHashCode();
                    }

                    typeHashCode = (interfaceType ?? type!).ToGlobalNamespaced().GetHashCode();

                    key = (lifetime, typeHashCode, keyHashCode);

                    isCached = isValid && lifetime is not Lifetime.Transient;

                    if (factory switch
                    {
                        IMethodSymbol factoryMethod => factoryMethod.Parameters,
                        IPropertySymbol { IsIndexer: true } factoryProperty => factoryProperty.Parameters,
                        IFieldSymbol => [],
                        _ => GetParameters(type)
                    }
                        is { IsDefaultOrEmpty: false, Length: > 0 } parameters)
                    {
                        prms = parameters;
                    }
                    else if (!isSimpleTransient && !isCached)
                    {
                        isSimpleTransient = true;
                    }

                    return true;

                    static ImmutableArray<IParameterSymbol> GetParameters(ITypeSymbol? implType)
                    {
                        if (implType is not INamedTypeSymbol { Constructors: var ctor, InstanceConstructors: var insCtor } || ctor.IsDefaultOrEmpty || insCtor.IsDefaultOrEmpty) return [];

                        ImmutableArray<IParameterSymbol> parameters = [];
                        int min = int.MaxValue;

                        foreach (var item in ctor.Concat(insCtor).Distinct(SymbolEqualityComparer.Default).Cast<IMethodSymbol>())
                        {
                            if (item.Parameters.IsDefaultOrEmpty || item.Parameters.Length >= min) continue;
                            min = (parameters = item.Parameters).Length;
                        }

                        return parameters;
                    }

                    static bool TryGetAttributeParamsDefinition(SymbolInfo info, out ImmutableArray<IParameterSymbol> prms)
                    {
                        if (info.Symbol is IMethodSymbol { Parameters: { } _prms })
                        {
                            prms = _prms;
                            return true;
                        }
                        foreach (var item in info.CandidateSymbols)
                        {
                            if (item is IMethodSymbol { Parameters: { } _prms2 })
                            {
                                prms = _prms2;
                                return true;
                            }
                        }
                        prms = [];
                        return false;
                    }

                    static Span<(IParameterSymbol, AttributeArgumentSyntax?)> GetAttrParamsMap(
                       ImmutableArray<IParameterSymbol> paramSymbols,
                       SeparatedSyntaxList<AttributeArgumentSyntax> argsSyntax)
                    {
                        int i = -1;
                        Span<(IParameterSymbol, AttributeArgumentSyntax?)> result = new (IParameterSymbol, AttributeArgumentSyntax?)[paramSymbols.Length];

                        foreach (var param in paramSymbols)
                        {
                            result[++i] = argsSyntax.Count > i && argsSyntax[i] is { NameColon: null, NameEquals: null } argSyntax
                                ? (param, argSyntax)
                                : (param, argsSyntax.FirstOrDefault(arg => param.Name == arg.NameColon?.Name.Identifier.ValueText));
                        }

                        return result;
                    }

                    static bool GetStringExpressionOrValue(SemanticModel model, IParameterSymbol paramSymbol, AttributeArgumentSyntax? arg, out string value)
                    {
                        value = null!;

                        if (arg is not null)
                        {
                            if (model.GetSymbolInfo(arg.Expression).Symbol is IFieldSymbol
                                {
                                    IsConst: true,
                                    Type.SpecialType: SpecialType.System_String,
                                    ConstantValue: { } val
                                })
                            {
                                return (value = val.ToString()!) != "";
                            }
                            else if (arg.Expression is LiteralExpressionSyntax { Token.ValueText: { } valueText } e
                                && e.IsKind(SyntaxKind.StringLiteralExpression))
                            {
                                return (value = valueText) != "";
                            }
                        }
                        else if (paramSymbol.HasExplicitDefaultValue)
                        {
                            value = paramSymbol.ExplicitDefaultValue?.ToString()!;
                            return value != "";
                        }

                        return false;
                    }
                }

                void AppendCancelToken(bool _)
                {
                    code.Append("cancellationToken");
                }
            }
        }

        containerDisposability = (Disposability)Math.Max((byte)scopedDisposability, (byte)containerDisposability);

        if (asyncSingletonAsyncDisposable + asyncScopedAsyncDisposable > 0)
        {
            if (!addExtensions) addExtensions = true;

            code.Append(@"using global::SourceCrafter.DepedencyInjection.Extensions;

");
        }

        var fileName = providerType.ToMetadataLongName(uniqueNames) + ".g";

        if (providerType.ContainingNamespace is { IsGlobalNamespace: false } ns)
        {
            code.Append("namespace ")
                .Append(ns.ToDisplayString()!)
                .Append(@";

");
        }

        var (modifiers, typeName) = declaration switch
        {
            ClassDeclarationSyntax { Modifiers: var mods, Keyword: { } keyword, Identifier: { } identifier, TypeParameterList: var typeParamsList } =>
                ($"{mods} {keyword}".TrimStart(), $"{identifier}{typeParamsList}"),
            InterfaceDeclarationSyntax { Modifiers: var mods, Keyword: { } keyword, Identifier: { } identifier, TypeParameterList: var typeParamsList } =>
                ($"{mods} {keyword}".TrimStart(), $"{identifier.ValueText[1..]}{typeParamsList}"),
            _ => ("", "")
        };

        code.AppendLine(generatedCodeAttribute)
            .Append(modifiers)
            .AddSpace()
            .Append(typeName);

        var disposeMethodName = containerDisposability switch
        {
            Disposability.AsyncDisposable => "DisposeAsync",
            Disposability.Disposable => "Dispose",
            _ => null
        };

        AddDisposabilityInterface(containerDisposability);

        code.Append(@"
    public static string EnvironmentName => global::System.Environment.GetEnvironmentVariable(""DOTNET_ENVIRONMENT"") ?? ""Development"";
");

        List<Action> scopedExposers = [];
        List<DisposeBuilder> scopedDisposers = [], singletonDisposers = [];

        foreach (var buildMethod in dependencyBuilders)
        {
            buildMethod(scopedExposers, singletonDisposers, scopedDisposers);
        }

        var scopedDisposeMethodName = scopedDisposability switch
        {
            Disposability.AsyncDisposable => "DisposeAsync",
            Disposability.Disposable => "Dispose",
            _ => null
        };

        var asyncSingletonCount = asyncSingletonAsyncDisposable + asyncSingletonDisposable + singletonAsyncDisposable;
        var asyncScopedCount = asyncScopedAsyncDisposable + asyncScopedDisposable + scopedAsyncDisposable;
        var singletonDisposableCount = asyncSingletonCount + singletonDisposable;
        var scopedDisposableCount = asyncScopedCount + scopedDisposable;
        var useAsync = asyncSingletonCount > 1;
        var hasOnlyScoped = scopedDisposableCount > 0 && singletonDisposableCount is 0;

        //if (!hasOnlyScoped)
        //	scopedDisposeMethodName = "Scoped" + scopedDisposeMethodName;

        if (scopedExposers.Count > 0)
        {

            code.Append(@"
	public Scoped CreateScope() => new();
	
	public class Scoped : ").Append(providerType.ToNameOnly());

            AddDisposabilityInterface(containerDisposability == scopedDisposability ? 0 : scopedDisposability, true);

            foreach (var scopedExposer in scopedExposers)
            {
                scopedExposer();
            }

            if (scopedDisposableCount > 0)
            {
                if (scopedDisposeMethodName is not null)
                    code.Append(@"
		public ");

                if (hasOnlyScoped && scopedDisposability == containerDisposability)
                    code.Append("override ");

                code.Append(scopedDisposability > Disposability.Disposable ? "global::System.Threading.Tasks.ValueTask " : "void ")
                    .Append(scopedDisposeMethodName).Append("() => ");

                if (!hasOnlyScoped) code.Append("Scoped");

                code.Append(scopedDisposeMethodName).Append(@"();");

                code.Append(@"
	}
");
                if (scopedDisposers.Count > 0 && scopedDisposeMethodName is not null)
                {
                    code.Append(@"
	");

                    if (hasOnlyScoped)
                    {
                        code.Append("public ");

                        if (scopedDisposability == containerDisposability)
                            code.Append("virtual ");
                    }

                    if (asyncSingletonCount > 1) code.Append("async ");

                    code.Append(scopedDisposability > Disposability.Disposable ? "global::System.Threading.Tasks.ValueTask " : "void ");

                    if (!hasOnlyScoped) code.Append("Scoped");

                    code.Append(scopedDisposeMethodName).Append(@"()
	{");

                    if (scopedDisposers is [{ } scopeDisposer])
                    {
                        scopeDisposer(false);
                    }
                    else
                    {
                        foreach (var scopeDisposer2 in scopedDisposers) scopeDisposer2();
                    }
                }
                code.Append(@"
	}
");
            }
            else
            {
                code.Append(@"	}
");
            }
        }

        if (!hasOnlyScoped && containerDisposability > 0)
        {
            var returnDefaultValueTask = (asyncSingletonCount, scopedDisposableCount) is (1, 0);
            //var useAsync = containerDisposability is Disposability.AsyncDisposable && scopedDisposability is not 0;
            code.Append(@"
	public ");

            if (!returnDefaultValueTask && asyncSingletonCount > 0)
                code.Append("async ");

            code.Append(containerDisposability > Disposability.Disposable ? "global::System.Threading.Tasks.ValueTask " : "void ")
                .Append(disposeMethodName).Append(@"()
	{");

            if (singletonDisposers is [{ } singletonDisposer])
            {
                singletonDisposer(!returnDefaultValueTask);
            }
            else
            {
                foreach (var singletonDisposer2 in singletonDisposers) singletonDisposer2();
            }


            if (scopedDisposableCount > 0)
            {
                code.Append(@"
		");

                if (scopedDisposability is Disposability.AsyncDisposable)
                    if (asyncSingletonCount == 0)
                        code.Append("return ");
                    else
                        code.Append("await ");

                code.Append("Scoped").Append(scopedDisposeMethodName).Append("();");
            }

            code.Append(@"
	}
");
        }

        code.Append(@"
}");

        addSource(fileName, code.ToString());

        string SanitizeTypeName(ITypeSymbol type, Lifetime lifeTime, string key)
        {
            int typeHashCode = SymbolEqualityComparer.Default.GetHashCode(type),
                keyHashCode = key.GetHashCode();

            var sanitizedTypeName = Sanitize(type).Replace(" ", "").Capitalize();

            ref var idOut = ref methodNamesMap.GetValueRefOrAddDefault((lifeTime, typeHashCode, keyHashCode), out var exists);

            if (exists)
            {
                return idOut!;
            }

            key = key.Capitalize();

            if (key is "")
            {
                if (!methodsRegistry.Add(idOut = sanitizedTypeName)) methodsRegistry.Add(idOut = $"{lifeTime}{sanitizedTypeName}");
            }
            else if (!(methodsRegistry.Add(idOut = key)
                || methodsRegistry.Add(idOut = $"{key}{sanitizedTypeName}")
                || methodsRegistry.Add(idOut = $"{lifeTime}{key}")))
            {
                methodsRegistry.Add(idOut = $"{lifeTime}{key}{sanitizedTypeName}");
            }

            return idOut;

            static string Sanitize(ITypeSymbol type)
            {
                switch (type)
                {
                    case INamedTypeSymbol { IsTupleType: true, TupleElements: { Length: > 0 } els }:

                        return "TupleOf" + string.Join("", els.Select(f => Sanitize(f.Type)));

                    case INamedTypeSymbol { IsGenericType: true, TypeParameters: { } args }:

                        return type.Name + "Of" + string.Join("", args.Select(Sanitize));

                    default:

                        string typeName = type.ToTypeNameFormat();

                        if (type is IArrayTypeSymbol { ElementType: { } elType })
                            typeName = Sanitize(elType) + "Array";

                        return char.ToUpperInvariant(typeName[0]) + typeName[1..].TrimEnd('?', '_');
                }
            }
        }

        void AddDisposabilityInterface(Disposability disposability, bool isScoped = false)
        {
            var indent = isScoped ? "	" : null;

            switch (disposability)
            {
                case Disposability.Disposable:

                    code.Append(isScoped ? "," : " :").Append(@" global::System.IDisposable	
").Append(indent).Append("{");

                    break;

                case Disposability.AsyncDisposable:

                    code.Append(isScoped ? "," : " :").Append(@" global::System.IAsyncDisposable	
").Append(indent).Append("{");

                    break;

                default:
                    code.Append(@"	
").Append(indent).Append("{");
                    break;
            }
        }
    }
    static void UpdateParentAsyncDepsCount(HashSet<(int, bool)> parent, HashSet<(int, bool)> child)
    {
        foreach (var item in child)
        {
            parent.Add(item);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool IsNull<T>(T x) => x is null;

    static bool TryGetLifetime(AttributeSyntax attrSyntax, ref INamedTypeSymbol attrClass, ref bool isExternal, out Lifetime lifetime)
    {
        if (GetLifetimeFromSyntax(attrSyntax, out lifetime)) return true;

        bool found;
        do
        {
            (isExternal, (found, lifetime)) = attrClass.ToGlobalNonGenericNamespace() switch
            {
                Helpers.SingletonAttr => (isExternal, (true, Lifetime.Singleton)),
                Helpers.ScopedAttr => (isExternal, (true, Lifetime.Scoped)),
                Helpers.TransientAttr => (isExternal, (true, Lifetime.Transient)),
                { } val => (val is not Helpers.DependencyAttr, GetFromCtorSymbol(attrClass))
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
    private static InvokeInfo GetInvokeInfos(GeneratorSyntaxContext gsc, CancellationToken _)
    {
        if (gsc.Node is MemberAccessExpressionSyntax
            {
                Name: IdentifierNameSyntax
                { Identifier.ValueText: { } name } method,
                Expression: IdentifierNameSyntax { } refVar
            }
            && gsc.SemanticModel.GetSymbolInfo(refVar).Symbol switch
            {
                ILocalSymbol local => (_ref: local, type: local.Type),
                IParameterSymbol parameter => (_ref: parameter, type: parameter.Type),
                _ => (_ref: default(ISymbol)!, type: default(ITypeSymbol)!)
            }
                is ({ } _ref, var type)
            && type.GetAttributes().Any(IsGeneratedServiceContainer))
        {
            var clsId = SymbolEqualityComparer.Default.GetHashCode(type);

            var isCtor = _ref.DeclaringSyntaxReferences.Any(s => s.GetSyntax() is VariableDeclaratorSyntax
            {
                Initializer.Value: ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax
            });

            return new(clsId, name, method, isCtor);
        }

        return null!;

        static bool IsGeneratedServiceContainer(AttributeData attrData) =>
            attrData.AttributeClass?.ToGlobalNamespaced().EndsWith(serviceContainerFullTypeName) ?? false;
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

internal class DiagnosticLocationComparer : IEqualityComparer<Diagnostic>
{
    public bool Equals(Diagnostic x, Diagnostic y)
    {
        return GetHashCode(x) == GetHashCode(y);
    }

    public int GetHashCode(Diagnostic obj)
    {
        return (obj.Id, obj.Location.GetHashCode()).GetHashCode();
    }
}

internal record InvokeInfo(int ContainerTypeId, string Name, IdentifierNameSyntax MethodSyntax, bool NotFromScopedInstance);

internal delegate void CommaSeparateBuilder(ref bool useIComma, int deepParamCount, string baseIndent);

public enum Lifetime : byte { Singleton, Scoped, Transient }

public enum AsyncType : byte { None, ValueTask, Task }

public enum Disposability : byte { None, Disposable, AsyncDisposable }

internal delegate bool ChildDependencyHandler(bool childExists, bool isChildValid, Lifetime childLifetime, AsyncType isChildAsync, int childParamCount, Action<bool> buildParam, bool isNullChildType, bool isUnkeyedInternalPrimitive);

internal delegate void DisposeBuilder(bool await = true);

class AsyncLocalResolver((Lifetime lifetime, int typeHash, int keyHash) dep)
{
    public int ParamIndex = -1, ResolvedByParamIndex;
    public bool IsValueTask, ResolvedBefore;
    public (Lifetime lifetime, int typeHash, int keyHash) Dep = dep, ResolverDep;
    internal Func<AsyncLocalResolver> BuildAsyncLocal = null!;

    public override bool Equals(object? obj)
    {
        return ((AsyncLocalResolver)obj!).GetHashCode() == GetHashCode();
    }
    public override int GetHashCode()
    {
        return Dep.GetHashCode();
    }
}

internal class ResolverBuilder(string toStr)
{
    internal (Lifetime lifetime, int typeHash, int keyHash) Key;
    internal AsyncType AsyncType;
    internal HashSet<(int, bool)> AsyncNestedDeps = [];
    internal Map<(Lifetime lifetime, int typeHash, int keyHash), AsyncLocalResolver> AsyncLocalResolvers = new(EqualityComparer<(Lifetime, int, int)>.Default);
    internal Action<bool> BuildValue = null!;
    internal string ExportTypeFullName = null!;
    internal int ParamsLength;
    public override string ToString()
    {
        return toStr;
    }
}


static class Helpers
{
    //internal static async ValueTask TryDisposeAsync<T>(this ValueTask<T>? vTask) where T : IAsyncDisposable
    //{
    //    if (vTask is { } task && (task.IsCompletedSuccessfully ? task.GetAwaiter().GetResult() : await task) is { } result)
    //    {
    //        await result.DisposeAsync();
    //    }
    //}
    //internal static async ValueTask TryDispose<T>(this ValueTask<T> vTask) where T : IDisposable
    //{
    //    if (vTask is { } task && (task.IsCompletedSuccessfully ? task.GetAwaiter().GetResult() : await task) is { } result)
    //    {
    //        result.Dispose();
    //    }
    //}
    //internal static async ValueTask TryDisposeAsync<T>(this Task<T>? task) where T : IAsyncDisposable
    //{
    //    if (task is not null && (task.IsCompletedSuccessfully ? task.GetAwaiter().GetResult() : await task) is { } result)
    //    {
    //        await result.DisposeAsync();
    //    }
    //}
    //internal static async ValueTask TryDispose<T>(this Task<T>? task) where T : IDisposable
    //{
    //    if (task is not null && (task.IsCompletedSuccessfully ? task.GetAwaiter().GetResult() : await task) is { } result)
    //    {
    //        result.Dispose();
    //    }
    //}

    internal static readonly int EmptyStringHashCode = "".GetHashCode();

    internal static AsyncType TryGetAsyncType(this ITypeSymbol typeSymbol, out ITypeSymbol factoryType)
    {
        switch (typeSymbol.ToGlobalNonGenericNamespace())
        {
            case "global::System.Threading.Tasks.ValueTask" or "global::System.Threading.Tasks.Task"
                when typeSymbol is INamedTypeSymbol { TypeArguments: [{ } firstTypeArg] }:

                factoryType = firstTypeArg;
                return typeSymbol!.Name is "ValueTask" ? AsyncType.ValueTask : AsyncType.Task;

            default:
                // TODO: if there's a case of inheriting from task, a recursive approach should be taken here 
                factoryType = typeSymbol;
                return AsyncType.None;
        }
    }


    internal const string
            BaseAttributesNS = "SourceCrafter.DependencyInjection.Attributes",
            ServiceContainerFullTypeName = $"{BaseAttributesNS}.ServiceContainerAttribute",
            CancelTokenFQMetaName = "System.Threading.CancellationToken",
            EnumFQMetaName = "global::System.Enum",
            KeyParamName = "key",
            NameFormatParamName = "nameFormat",
            SourceParamName = "source",
            ImplParamName = "impl",
            IfaceParamName = "iface",
            SingletonAttr = $"global::{BaseAttributesNS}.SingletonAttribute",
            ScopedAttr = $"global::{BaseAttributesNS}.ScopedAttribute",
            TransientAttr = $"global::{BaseAttributesNS}.TransientAttribute",
            DependencyAttr = $"global::{BaseAttributesNS}.DependencyAttribute",
            ServiceContainerAttr = $"global::{ServiceContainerFullTypeName}";

    internal static Disposability GetDisposability(this ITypeSymbol type)
    {
        if (type is null) return Disposability.None;

        Disposability disposability = Disposability.None;

        foreach (var iFace in type.AllInterfaces)
        {
            switch (iFace.ToGlobalNonGenericNamespace())
            {
                case "global::System.IDisposable" when disposability is Disposability.None:
                    disposability = Disposability.Disposable;
                    break;
                case "global::System.IAsyncDisposable" when disposability < Disposability.AsyncDisposable:
                    return Disposability.AsyncDisposable;
            }
        }

        return disposability;
    }
}

record ParamBuildOptions((Lifetime lifetime, int typeHash, int keyHash) Key, Action<bool> Build);

[Flags]
internal enum LockerTypes
{
    None,
    StaticLock,
    Lock,
    Semaphore,
    StaticSemaphore
}

using Microsoft.CodeAnalysis;
using SourceCrafter.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

#pragma warning disable CA1050 // Declarar tipos en espacios de nombres
internal partial class ServiceProviders
#pragma warning restore CA1050 // Declarar tipos en espacios de nombres
{
    private sealed class Emitter(
        string metadataLongName,
        string? nameSpace,
        string containerFullTypeName,
        string modifiers,
        bool isInterfaceProvider,
        bool useInterceptors,
        string className,
        string typeName,
        string envName,
        int asyncScopedDisposable,
        int asyncSingletonDisposable,
        int asyncScopedAsyncDisposable,
        int asyncSingletonAsyncDisposable,
        int scopedDisposable,
        int singletonDisposable,
        int scopedAsyncDisposable,
        int singletonAsyncDisposable,
        Disposability containerDisposability,
        Disposability scopedDisposability,
        DependencyDictionary dependencyValueBuilders,
        Dictionary<DependencyKey, MemberBuilder> dependencyMemberBuilder,
        Dictionary<FirstLevelDependencyKey, Interceptor> interceptors,
        HashSet<Diagnostic> diagnostics,
        HashSet<ResolverBuilder> genericResolvers) : IDisposable, IEquatable<Emitter>
    {
#pragma warning disable CS9124 // El parámetro se captura en el estado del tipo envolvente y su valor también se usa para inicializar un campo, propiedad o evento.
        internal HashSet<Diagnostic> Diagnostics = diagnostics;
        internal DependencyDictionary DependencyValueBuilders = dependencyValueBuilders;
#pragma warning restore CS9124 // El parámetro se captura en el estado del tipo envolvente y su valor también se usa para inicializar un campo, propiedad o evento.
        internal readonly string ContainerFullTypeName = containerFullTypeName;
        internal readonly string ClassName = className;

        string GetFileName(Dictionary<string, byte> uniqueName)
        {
            var existing = metadataLongName;
            ref var count = ref CollectionsMarshal.GetValueRefOrAddDefault(uniqueName, metadataLongName, out var exists);
            if (exists) count += 1;
            return exists ? existing + "_" + count : existing;
        }

        internal void Emit(Dictionary<string, byte> uniqueNames, ref bool addTasksExtensions, ref int interceptorsCount, out string fileName, out string codeStr)
        {
            StringBuilder code = new("#nullable enable\n");

            fileName = GetFileName(uniqueNames);

            containerDisposability = (Disposability)Math.Max((byte)scopedDisposability, (byte)containerDisposability);

            if (asyncSingletonAsyncDisposable + asyncScopedAsyncDisposable > 0)
            {
                if (!addTasksExtensions) addTasksExtensions = true;

                code.Append(@"using global::SourceCrafter.DepedencyInjection.Extensions;

");
            }

            if (nameSpace is { } ns)
            {
                code.Append("namespace ").Append(ns).Append(@";

");
            }

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

            AddDisposabilityInterface(code, containerDisposability, isInterfaceProvider);

            code.Append(@"
    public static string EnvironmentName => global::System.Environment.GetEnvironmentVariable(").Append(envName).Append(@") ?? ""Development"";
");


            List<Action> scopedExposers = [];
            List<DisposeBuilder> scopedDisposers = [], singletonDisposers = [];

            var hasAsync = false;
            foreach (var memberResolver in dependencyMemberBuilder.Values)
            {
                hasAsync = memberResolver.AsyncKind > 0;
                memberResolver.BuildAndExpose(code, scopedExposers, singletonDisposers, scopedDisposers);
            }

            var scopedDisposeMethodName = scopedDisposability switch
            {
                Disposability.AsyncDisposable => "DisposeAsync",
                Disposability.Disposable => "Dispose",
                _ => null
            };

            int asyncSingletonCount = asyncSingletonAsyncDisposable + asyncSingletonDisposable + singletonAsyncDisposable,
                asyncScopedCount = asyncScopedAsyncDisposable + asyncScopedDisposable + scopedAsyncDisposable,
                singletonDisposableCount = asyncSingletonCount + singletonDisposable,
                scopedDisposableCount = asyncScopedCount + scopedDisposable;

            bool useSingletonAsync = asyncSingletonCount > 1,
                hasOnlyScoped = scopedDisposableCount > 0 && singletonDisposableCount is 0;

            if (scopedExposers.Count > 0)
            {
                code.Append(@"
	public Scoped CreateScope() => new() { _root = this };

	private ").Append(typeName).Append(@" _root = default!;

	public ").Append(typeName).Append(@" Root => this;    
	
	public class Scoped : ").Append(typeName);

                AddDisposabilityInterface(code, containerDisposability == scopedDisposability ? 0 : scopedDisposability, false, true);

                //foreach (var scopedExposer in scopedExposers)
                //{
                //    scopedExposer();
                //}

                if (scopedDisposableCount > 0)
                {
                    if (scopedDisposeMethodName is not null)
                        code.Append(@"
		public ");

                    if (hasOnlyScoped && scopedDisposability == containerDisposability)
                        code.Append("override ");

                    code.Append(scopedDisposability > Disposability.Disposable ? "global::System.Threading.Tasks.ValueTask " : "void ")
                        .Append(scopedDisposeMethodName).Append("() => ");

                    code.Append(hasOnlyScoped ? "base." : "Scoped");

                    code.Append(scopedDisposeMethodName).Append(@"();");

                    code.Append(@"

		public new ").Append(typeName).Append(@" Root => _root;

	    public new Scoped CreateScope() => new() { _root = _root };
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
                            scopeDisposer(code, false);
                        }
                        else
                        {
                            foreach (var scopeDisposer2 in scopedDisposers) scopeDisposer2(code);
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
                //var useSingletonAsync = containerDisposability is Disposability.AsyncDisposable && scopedDisposability is not 0;
                code.Append(@"
	public ");

                if (!returnDefaultValueTask && asyncSingletonCount > 0)
                    code.Append("async ");

                code.Append(containerDisposability > Disposability.Disposable ? "global::System.Threading.Tasks.ValueTask " : "void ")
                    .Append(disposeMethodName).Append(@"()
	{");

                if (singletonDisposers is [{ } singletonDisposer])
                {
                    singletonDisposer(code, !returnDefaultValueTask);
                }
                else
                {
                    foreach (var singletonDisposer2 in singletonDisposers) singletonDisposer2(code);
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

            if (useInterceptors && genericResolvers.Count > 0)
            {
                code.Append(@"
    #region IServiceProvider compatibility

    object? global::System.IServiceProvider.GetService(global::System.Type serviceType) => throw new global::System.NotImplementedException();
");

                foreach (var genericResolver in genericResolvers)
                    genericResolver.GenericMemberSignature(code);


                code.Append(@"
    #endregion");
            }
            code.Append(@"
}
");

            if (useInterceptors && interceptors.Count > 0)
            {
                code.Append(@" 
public static class ").Append(typeName).Append(@"Extensions
{");

                foreach (var item in interceptors.Values)
                {
                    item.Append(code, ClassName, ref interceptorsCount);
                }

                code.Append('}');
            }

            codeStr = code.ToString();
        }


        void AddDisposabilityInterface(StringBuilder code, Disposability disposability, bool isInterface = false, bool isScoped = false)
        {
            var indent = isScoped ? "	" : null;

            switch (disposability)
            {
                case Disposability.Disposable:

                    code.Append(isScoped ? "," : " :");

                    if (isInterface) code.Append(" I").Append(typeName).Append(',');

                    code.Append(@" global::System.IDisposable	
").Append(indent).Append('{');

                    break;

                case Disposability.AsyncDisposable:

                    code.Append(isScoped ? "," : " :");

                    if (isInterface) code.Append(" I").Append(typeName).Append(',');

                    code.Append(@" global::System.IAsyncDisposable	
").Append(indent).Append('{');

                    break;

                default:

                    code.Append(@"	
").Append(indent);

                    if (isInterface) code.Append(": I").Append(typeName);

                    code.Append('{');

                    break;
            }
        }

        internal void AddOrUpdateIntercerceptor(
            InvokeInfo serviceCall,
            bool multiple,
            AsyncKind asyncKind,
            bool passCancelToken,
            AppendValue AppendValue)
        {
            var key = (serviceCall.ReturnType, serviceCall.Key);

            var interceptor = CollectionsMarshal.GetValueRefOrAddDefault(interceptors, key, out var exists) ??=
                new(key,
                    serviceCall.MethodName,
                    multiple,
                    passCancelToken,
                    serviceCall.AsyncKind,
                    serviceCall.ReturnType,
                    serviceCall.IsKeyed,
                    (serviceCall.AsyncKind > 0, AppendDependency));

            if (!serviceCall.Acknowledged) serviceCall.Acknowledged = true;

            interceptor.Locations.Add(serviceCall.Interceptor);

            if (exists && multiple)
            {
                if (interceptor.AsyncKind < asyncKind) 
                    interceptor.AsyncKind = asyncKind;
                interceptor.IsKeyed = serviceCall.IsKeyed;
                interceptor.AppendInterceptorValue.Add((serviceCall.AsyncKind > 0, AppendDependency));
            }

            void AppendDependency(StringBuilder code, bool useAsync)
            {
                AppendValue(code, useAsync, true);
            }
        }

        public void Dispose()
        {
            Diagnostics.Clear();
            Diagnostics = diagnostics = null!;
            DependencyValueBuilders.Clear();
            DependencyValueBuilders = dependencyValueBuilders = null!;
            dependencyMemberBuilder.Clear();
            dependencyMemberBuilder = null!;
            interceptors.Clear();
            interceptors = null!;
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as Emitter);
        }

        public bool Equals(Emitter? other)
        {
            return other is not null
                    && (ReferenceEquals(other, this)
                        || other.EqualsTo(this, containerDisposability, isInterfaceProvider, useInterceptors, nameSpace, typeName, modifiers, envName, dependencyMemberBuilder));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool EqualsTo(
            Emitter other,
            Disposability _containerDisposability,
            bool _isInterfaceProvider,
            bool _useInterceptors,
            string? _nameSpace,
            string _typeName,
            string _modifiers,
            string _envName,
            Dictionary<DependencyKey, MemberBuilder> _dependencyMemberBuilder)
        {
            return _containerDisposability == containerDisposability
                && _isInterfaceProvider == isInterfaceProvider
                && _useInterceptors == useInterceptors
                && _nameSpace == nameSpace
                && _typeName == typeName
                && other.ClassName == ClassName
                && _modifiers == modifiers
                && _envName == envName
                && dependencyMemberBuilder.All(kv => _dependencyMemberBuilder.TryGetValue(kv.Key, out var found) && found.Equals(kv.Value));
        }

        public override int GetHashCode()
        {
            HashCode hashCode = new();

            hashCode.Add(containerDisposability);
            hashCode.Add(isInterfaceProvider);
            hashCode.Add(useInterceptors);
            hashCode.Add(nameSpace);
            hashCode.Add(typeName);
            hashCode.Add(ClassName);
            hashCode.Add(modifiers);
            hashCode.Add(envName);

            foreach (var item in dependencyMemberBuilder.Values)
            {
                hashCode.Add(item.GetHashCode());
            }

            return hashCode.ToHashCode();
        }
    }
    sealed class EmitterEqualityComparer : IEqualityComparer<Emitter>
    {
        public static EmitterEqualityComparer Default
        {
            get
            {
                if (field is not null) return field;

                lock (typeof(EmitterEqualityComparer)) return field ??= new();
            }
        }
        public bool Equals(Emitter? x, Emitter? y)
        {
            return x?.Equals(y) is true;
        }

        public int GetHashCode([DisallowNull] Emitter other)
        {
            return other.GetHashCode();
        }
    }
}

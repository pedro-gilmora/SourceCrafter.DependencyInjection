using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using SourceCrafter.DependencyInjection.Interop;

using static SourceCrafter.DependencyInjection.Interop.ServiceDescriptor;


[assembly: InternalsVisibleTo("SourceCrafter.Bindings.UnitTests")]
namespace SourceCrafter.DependencyInjection
{
    public static class Extensions
    {
        internal readonly static SymbolDisplayFormat
            _globalizedNamespace = new(
                memberOptions:
                    SymbolDisplayMemberOptions.IncludeType |
                    SymbolDisplayMemberOptions.IncludeModifiers |
                    SymbolDisplayMemberOptions.IncludeExplicitInterface |
                    SymbolDisplayMemberOptions.IncludeParameters |
                    SymbolDisplayMemberOptions.IncludeContainingType |
                    SymbolDisplayMemberOptions.IncludeConstantValue |
                    SymbolDisplayMemberOptions.IncludeRef,
                globalNamespaceStyle:
                    SymbolDisplayGlobalNamespaceStyle.Included,
                typeQualificationStyle:
                    SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                genericsOptions:
                    SymbolDisplayGenericsOptions.IncludeTypeParameters |
                    SymbolDisplayGenericsOptions.IncludeVariance,
                miscellaneousOptions:
                    SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                    SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier,
                parameterOptions:
                    SymbolDisplayParameterOptions.IncludeType |
                    SymbolDisplayParameterOptions.IncludeModifiers |
                    SymbolDisplayParameterOptions.IncludeName |
                    SymbolDisplayParameterOptions.IncludeDefaultValue),
            _globalizedNonGenericNamespace = new(
                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes),
            _symbolNameOnly = new(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly),
            _typeNameFormat = new(
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters | SymbolDisplayGenericsOptions.IncludeVariance,
                miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

        public static string ToGlobalNamespaced(this ISymbol t) => t.ToDisplayString(_globalizedNamespace);

        public static string ToGlobalNonGenericNamespace(this ISymbol t) => t.ToDisplayString(_globalizedNonGenericNamespace);

        public static string ToTypeNameFormat(this ITypeSymbol t) => t.ToDisplayString(_typeNameFormat);

        public static string ToNameOnly(this ISymbol t) => t.ToDisplayString(_symbolNameOnly);
        internal static bool TryGetDependencyInfo(
            this SemanticModel model,
            AttributeData attrData,
            ref bool isExternal,
            string paramName,
            ITypeSymbol? fallbackType,
            out Lifetime lifetime,
            out ITypeSymbol finalType,
            out ITypeSymbol? iFaceType,
            out ITypeSymbol implType,
            out ISymbol? factory,
            out SymbolKind factoryKind,
            out string outKey,
            out string? nameFormat,
            out ImmutableArray<IParameterSymbol> defaultParamValues,
            out bool isCached,
            out Disposability disposability,
            out bool isValid,
            out AttributeSyntax attrSyntaxOut)
        {
            finalType = iFaceType = implType = default!;
            factoryKind = default!;
            isCached = isValid = default!;
            defaultParamValues = [];
            outKey = nameFormat = null!;
            factory = default!;
            disposability = Disposability.None;
            lifetime = Lifetime.Transient;

            attrSyntaxOut = null!;

            if (attrData is { AttributeClass: { } attrClass, ApplicationSyntaxReference: { } attrSyntaxRef }
                && attrSyntaxRef.GetSyntax() is AttributeSyntax { } attrSyntax
                && model.GetSymbolInfo(attrSyntax).Symbol is IMethodSymbol { Parameters: var attrParams }
                && !attrClass.Name.Equals("ServiceContainer")
                && GetLifetimeFromCtor(ref attrClass, ref isExternal, attrSyntax, out lifetime))
            {
                attrSyntaxOut = attrSyntax;

                if (attrData.AttributeClass!.TypeArguments.Length > 0 is { } isGeneric)
                {
                    switch (attrClass!.TypeArguments)
                    {
                        case [{ } t1, { } t2, ..]:

                            iFaceType = t1;
                            implType = t2;

                            break;

                        case [{ } t1]:

                            implType = t1;

                            break;
                    }
                }

                foreach (var (param, arg) in GetAttrParamsMap(attrParams, attrSyntax.ArgumentList?.Arguments ?? []))
                {
                    switch (param.Name)
                    {
                        case ImplParamName when !isGeneric && arg is { Expression: TypeOfExpressionSyntax { Type: { } type } }:

                            implType = (ITypeSymbol)model!.GetSymbolInfo(type).Symbol!;

                            continue;

                        case IfaceParamName when fallbackType?.TypeKind is not TypeKind.Interface && !isGeneric && arg is { Expression: TypeOfExpressionSyntax { Type: { } type } }:

                            iFaceType = (ITypeSymbol)model!.GetSymbolInfo(type).Symbol!;

                            continue;

                        case KeyParamName when GetStrExpressionOrValue(model, param!, arg, out var keyValue):

                            outKey = keyValue;

                            continue;

                        case NameFormatParamName when GetStrExpressionOrValue(model, param, arg, out var keyValue):

                            nameFormat = keyValue;

                            continue;

                        case FactoryOrInstanceParamName

                            when arg?.Expression is InvocationExpressionSyntax
                            {
                                Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" },
                                ArgumentList.Arguments: [{ } methodRef]
                            }:

                            switch (model.GetSymbolInfo(methodRef.Expression))
                            {
                                case { Symbol: (IFieldSymbol or IPropertySymbol) and { IsStatic: true, Kind: { } kind } fieldOrProp }:
                                    factory = fieldOrProp;
                                    factoryKind = kind;
                                    break;

                                case { CandidateReason: CandidateReason.MemberGroup, CandidateSymbols: [IMethodSymbol { ReturnsVoid: false, IsStatic: true } method] }:
                                    factory = method;
                                    factoryKind = SymbolKind.Method;
                                    defaultParamValues = method.Parameters;
                                    break;
                            }

                            continue;

                        case "disposability" when param.HasExplicitDefaultValue:

                            disposability = (Disposability)(byte)param.ExplicitDefaultValue!;

                            continue;
                    }
                }

                finalType = factoryKind switch
                {
                    SymbolKind.Method => ((IMethodSymbol)factory!).ReturnType,
                    SymbolKind.Field => ((IFieldSymbol)factory!).Type,
                    SymbolKind.Property => ((IPropertySymbol)factory!).Type,
                    _ => iFaceType ?? implType ?? fallbackType!
                };

                if (fallbackType is { })
                {
                    if (fallbackType.TypeKind == TypeKind.Interface)
                        iFaceType ??= fallbackType;
                    else 
                        implType ??= fallbackType;
                }

                outKey ??= paramName ?? "";

                return isValid = finalType is not null && implType is not null;
            }
            return false;
        }

        static IEnumerable<(IParameterSymbol, AttributeArgumentSyntax?)> GetAttrParamsMap(
            ImmutableArray<IParameterSymbol> paramSymbols,
            SeparatedSyntaxList<AttributeArgumentSyntax> argsSyntax)
        {
            int i = 0;
            foreach (var param in paramSymbols)
            {
                if (argsSyntax.Count > i && argsSyntax[i] is { NameColon: null, NameEquals: null } argSyntax)
                {
                    yield return (param, argSyntax);
                }
                else
                {
                    yield return (param, argsSyntax.FirstOrDefault(arg => param.Name == arg.NameColon?.Name.Identifier.ValueText));
                }

                i++;
            }
        }

        private static bool GetStrExpressionOrValue(SemanticModel model, IParameterSymbol paramSymbol, AttributeArgumentSyntax? arg, out string value)
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
                    value = val.ToString();
                    return true;
                }
                else if (arg.Expression is LiteralExpressionSyntax { Token.ValueText: { } valueText } e
                    && e.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    value = valueText;
                    return true;
                }
            }
            else if (paramSymbol.HasExplicitDefaultValue)
            {
                value = paramSymbol.ExplicitDefaultValue?.ToString()!;
                return value != null;
            }

            return false;
        }

        public static bool GetLifetimeFromCtor(ref INamedTypeSymbol attrClass, ref bool isExternal, AttributeSyntax attrSyntax, out Lifetime lifetime)
        {
            if (GetLifetimeFromSyntax(attrSyntax, out lifetime)) return true;

            bool found;
            do
            {
                (isExternal, (found, lifetime)) = attrClass.ToGlobalNonGenericNamespace() switch
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
                        if (param.Name is "lifetime" && param.HasExplicitDefaultValue)
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ToMetadataLongName(this ISymbol symbol)
        {
            var ret = new StringBuilder();

            foreach (var part in symbol.ToDisplayParts(_typeNameFormat))
            {
                if (part.Symbol is { Name: string name })
                    ret.Append(name.Capitalize());
                else
                    switch (part.ToString())
                    {
                        case ",": ret.Append("And"); break;
                        case "<": ret.Append("Of"); break;
                        case "[": ret.Append("Array"); break;
                    }
            }

            return ret.ToString();
        }

        public static string ToMetadataLongName(this ISymbol symbol, Map<string, byte> uniqueName)
        {
            var existing = ToMetadataLongName(symbol);

            ref var count = ref uniqueName.GetValueOrAddDefault(existing, out var exists);

            if (exists)
            {
                return existing + "_" + (++count);
            }

            return existing;
        }

        public static string Capitalize(this string str)
        {
            return (str is [{ } f, .. { } rest] ? char.ToUpper(f) + rest : str);
        }

        public static string Camelize(this string str)
        {
            return (str is [{ } f, .. { } rest] ? char.ToLower(f) + rest : str);
        }

        public static string? Pascalize(this string str)
        {
            if (string.IsNullOrEmpty(str))
                return str;

            ReadOnlySpan<char> span = str.AsSpan();
            Span<char> result = stackalloc char[span.Length];
            int resultIndex = 0;
            bool newWord = true;

            foreach (char c in span)
            {
                if (char.IsWhiteSpace(c) || c == '-' || c == '_')
                {
                    newWord = true;
                }
                else
                {
                    if (newWord)
                    {
                        result[resultIndex++] = char.ToUpperInvariant(c);
                        newWord = false;
                    }
                    else
                    {
                        result[resultIndex++] = c;
                    }
                }
            }

            return result[0..resultIndex].ToString();
        }

        public static ImmutableArray<IParameterSymbol> GetParameters(this ITypeSymbol implType)
        {
            return implType is INamedTypeSymbol { Constructors: var ctor, InstanceConstructors: var insCtor }
                ? ctor.OrderBy(d => !d.Parameters.IsDefaultOrEmpty).FirstOrDefault()?.Parameters
                    ?? insCtor.OrderBy(d => !d.Parameters.IsDefaultOrEmpty).FirstOrDefault()?.Parameters
                    ?? []
                : [];
        }

        public static bool IsPrimitive(this ITypeSymbol target, bool includeObject = true) =>
            (includeObject && target.SpecialType is SpecialType.System_Object)
                || target.SpecialType is SpecialType.System_Enum
                    or SpecialType.System_Boolean
                    or SpecialType.System_Byte
                    or SpecialType.System_SByte
                    or SpecialType.System_Char
                    or SpecialType.System_DateTime
                    or SpecialType.System_Decimal
                    or SpecialType.System_Double
                    or SpecialType.System_Int16
                    or SpecialType.System_Int32
                    or SpecialType.System_Int64
                    or SpecialType.System_Single
                    or SpecialType.System_UInt16
                    or SpecialType.System_UInt32
                    or SpecialType.System_UInt64
                    or SpecialType.System_String
                || target.Name is "DateTimeOffset" or "Guid"
                || (target.SpecialType is SpecialType.System_Nullable_T
                    && IsPrimitive(((INamedTypeSymbol)target).TypeArguments[0]));

        public static ITypeSymbol AsNonNullable(this ITypeSymbol type) =>
            type.Name == "Nullable"
                ? ((INamedTypeSymbol)type).TypeArguments[0]
                : type.WithNullableAnnotation(NullableAnnotation.None);

        public static void TryGetNullable(this ITypeSymbol type, out ITypeSymbol outType, out bool outIsNullable)
                    => (outType, outIsNullable) = type.SpecialType is SpecialType.System_Nullable_T
                        || type is INamedTypeSymbol { Name: "Nullable" }
                            ? (((INamedTypeSymbol)type).TypeArguments[0], true)
                            : type.NullableAnnotation == NullableAnnotation.Annotated
                                ? (type.WithNullableAnnotation(NullableAnnotation.None), true)
                                : (type, false);

        public static bool IsNullable(this ITypeSymbol typeSymbol)
            => typeSymbol.SpecialType is SpecialType.System_Nullable_T
                || typeSymbol.NullableAnnotation == NullableAnnotation.Annotated
                || typeSymbol is INamedTypeSymbol { Name: "Nullable" };

        public static bool AllowsNull(this ITypeSymbol typeSymbol)
            => typeSymbol is { IsValueType: false, IsTupleType: false, IsReferenceType: true };
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static StringBuilder AddSpace(this StringBuilder sb, int count = 1) => sb.Append(new string(' ', count));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static StringBuilder CaptureGeneratedString(this StringBuilder code, Action action, out string expression)
        {
            int start = code.Length, end;
            action();
            end = code.Length;
            char[] e = new char[end - start];
            code.CopyTo(start, e, 0, end - start);
            expression = new(e, 0, e.Length);
            return code;
        }

        public static bool TryGetAsyncType(this ITypeSymbol? typeSymbol, out ITypeSymbol? factoryType)
        {
            switch ((factoryType = typeSymbol)?.ToGlobalNonGenericNamespace())
            {
                case "global::System.Threading.Tasks.ValueTask" or "global::System.Threading.Tasks.Task"
                    when factoryType is INamedTypeSymbol { TypeArguments: [{ } firstTypeArg] }:

                    factoryType = firstTypeArg;
                    return true;

                default:

                    return false;
            };
        }

        public static Disposability GetDisposability(this ITypeSymbol type)
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

        public static string RemoveDuplicates(this string? input)
        {
            if ((input = input?.Trim()) is null or "")
                return "";

            var result = "";
            int wordStart = 0;

            for (int i = 1; i < input.Length; i++)
            {
                if (char.IsUpper(input[i]))
                {
                    string word = input[wordStart..i];

                    if (!result.EndsWith(word))
                    {
                        result += word;
                    }

                    wordStart = i;
                }
            }

            string lastWord = input[wordStart..];

            if (!result.EndsWith(lastWord, StringComparison.OrdinalIgnoreCase))
            {
                result += lastWord;
            }

            return result;
        }

        public static string SanitizeTypeName(
            ITypeSymbol type,
            HashSet<string> methodsRegistry,
            DependencyNamesMap dependencyRegistry,
            Lifetime lifeTime,
            string key)
        {
            int hashCode = SymbolEqualityComparer.Default.GetHashCode(type);

            string id = Sanitize(type).Replace(" ", "").Capitalize();

            ref var idOut = ref dependencyRegistry.GetValueOrAddDefault((lifeTime, hashCode, key), out var exists);

            if (exists)
            {
                return idOut!;
            }

            if (key is "")
            {
                if (!methodsRegistry.Add(idOut = id)) methodsRegistry.Add(idOut = $"{lifeTime}{id}");
            }
            else if (!(methodsRegistry.Add(idOut = key)
                || methodsRegistry.Add(idOut = $"{key}{id}")
                || methodsRegistry.Add(idOut = $"{lifeTime}{key}")))
            {
                methodsRegistry.Add(idOut = $"{lifeTime}{key}{id}");
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
                };
            }
        }

        public static T Exchange<T>(ref this T oldVal, T newVal) where T : struct =>
                    oldVal.Equals(newVal) ? oldVal : ((oldVal, _) = (newVal, oldVal)).Item2;
    }
}


namespace SourceCrafter.Bindings
{
    public static class CollectionExtensions<T>
    {
        public static Collection<T> EmptyCollection => [];
        public static ReadOnlyCollection<T> EmptyReadOnlyCollection => new([]);
    }
}

#if NETSTANDARD2_0 || NETSTANDARD2_1 || NETCOREAPP2_0 || NETCOREAPP2_1 || NETCOREAPP2_2 || NETCOREAPP3_0 || NETCOREAPP3_1 || NET45 || NET451 || NET452 || NET6 || NET461 || NET462 || NET47 || NET471 || NET472 || NET48


// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Reserved to be used by the compiler for tracking metadata.
    /// This class should not be used by developers in source code.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit
    {
    }
}

#endif
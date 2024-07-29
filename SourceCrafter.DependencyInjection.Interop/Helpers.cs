using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

using Microsoft.CodeAnalysis;

using SourceCrafter.DependencyInjection.Interop;


[assembly: InternalsVisibleTo("SourceCrafter.Bindings.UnitTests")]
namespace SourceCrafter.DependencyInjection
{
    internal static class Extensions
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
            var e = ToMetadataLongName(symbol);

            ref var count = ref uniqueName.GetOrAddDefault(e, out var exists);

            if (exists)
            {
                return e + "_" + (++count);
            }

            return e;
        }

        internal static string Capitalize(this string str)
        {
            return str is [{ } f, .. { } rest] ? char.ToUpper(f) + rest : str;
        }

        internal static string Camelize(this string str)
        {
            return str is [{ } f, .. { } rest] ? char.ToLower(f) + rest : str;
        }

        public static bool IsPrimitive(this ITypeSymbol target, bool includeObject = true) =>
            (includeObject && target.SpecialType is SpecialType.System_Object) || target.SpecialType is SpecialType.System_Enum
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
#if DEBUG
            => typeSymbol.BaseType?.ToGlobalNonGenericNamespace() is not ("global::System.ValueType" or "global::System.ValueTuple");
#else
            => typeSymbol is { IsValueType: false, IsTupleType: false, IsReferenceType: true };
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string Wordify(this string identifier, short upper = 0)
            => ToJoined(identifier, " ", upper);

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

        public static string ToJoined(string identifier, string separator = "-", short casing = 0)
        {
            var buffer = new char[identifier.Length * (separator.Length + 1)];
            var bufferIndex = 0;

            for (int i = 0; i < identifier.Length; i++)
            {
                char ch = identifier[i];
                bool isLetterOrDigit = char.IsLetterOrDigit(ch), isUpper = char.IsUpper(ch);

                if (i > 0 && isUpper && char.IsLower(identifier[i - 1]))
                {
                    separator.CopyTo(0, buffer, bufferIndex, separator.Length);
                    bufferIndex += separator.Length;
                }
                if (isLetterOrDigit)
                {
                    buffer[bufferIndex++] = (casing, isUpper) switch
                    {
                        (1, false) => char.ToUpperInvariant(ch),
                        (-1, true) => char.ToLowerInvariant(ch),
                        _ => ch
                    };
                }
            }
            return new string(buffer, 0, bufferIndex);
        }

        public static string GenKey(ServiceDescriptor serviceDescriptor)
        {
            return $"{serviceDescriptor.Lifetime}|{serviceDescriptor.ExportTypeName}|{serviceDescriptor.EnumKeyTypeName}";
        }

        public static string GenKey(Lifetime lifetime, string typeFullName, IFieldSymbol? name)
        {
            return $"{lifetime}|{typeFullName}|{name?.Type?.ToGlobalNamespaced()}";
        }


        public static T Exchange<T>(ref this T oldVal, T newVal) where T : struct => 
            oldVal.Equals(newVal) ? oldVal :((oldVal, _) = (newVal, oldVal)).Item2;
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
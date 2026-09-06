using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using Xunit;

namespace SourceCrafter.DependencyInjection.Tests;

/// <summary>
/// Guarda contra la familia de bugs "Equals se llama a si mismo".
///
/// Caso real detectado en <c>MemberBuilder</c>: <c>Equals(MemberBuilder? other)</c>
/// empezaba con <c>return this == other || ...</c>. Para un <c>record</c>, el
/// compilador sintetiza
/// <code>op_Equality(l, r) =&gt; ReferenceEquals(l, r) || (l is not null &amp;&amp; l.Equals(r));</code>
/// asi que con dos instancias distintas se produce la alternancia infinita
/// <c>Equals -&gt; op_Equality -&gt; Equals</c>, un StackOverflowException y la muerte del
/// proceso host. En Visual Studio 2026 eso tumbaba DevHub.exe (codigo de salida
/// -1073741571 / 0xC00000FD) llevandose por delante el analizador de diagnosticos,
/// la busqueda de simbolos y la sincronizacion de recursos.
///
/// El bug solo se dispara cuando Roslyn compara dos instancias cacheadas entre
/// pasadas incrementales, por eso una compilacion de linea de comandos nunca lo veia.
///
/// La comprobacion se hace sobre los metadatos del ensamblado (sin cargarlo), de modo
/// que no depende de resolver las dependencias de Roslyn del generador.
/// </summary>
public class EqualityContractTests
{
    public static TheoryData<string> GeneratorAssemblies()
    {
        var data = new TheoryData<string>();

        foreach (var path in FindGeneratorAssemblies())
            data.Add(path);

        return data;
    }

    [Theory]
    [MemberData(nameof(GeneratorAssemblies))]
    public void Equals_Implementations_DoNotRecurseThroughEqualityOperator(string assemblyPath)
    {
        var offenders = FindSelfRecursiveEqualityMethods(assemblyPath);

        Assert.True(
            offenders.Count == 0,
            $"En '{Path.GetFileName(assemblyPath)}' hay implementaciones de Equals que llaman al "
            + "operador == de su propio tipo, lo que provoca recursion infinita y StackOverflow:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders.Select(o => "  - " + o))
            + Environment.NewLine
            + "Usa ReferenceEquals(this, other) en su lugar.");
    }

    [Fact]
    public void TheGeneratorAssemblyWasFound()
    {
        Assert.NotEmpty(FindGeneratorAssemblies());
    }

    static IReadOnlyList<string> FindGeneratorAssemblies()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SourceCrafter.DependencyInjection.slnx")))
            root = root.Parent;

        if (root is null) return [];

        string[] projects =
        [
            "SourceCrafter.DependencyInjection",
            "SourceCrafter.DependencyInjection.MsConfiguration"
        ];

        List<string> found = [];

        foreach (var project in projects)
        {
            var binDir = Path.Combine(root.FullName, project, "bin");

            if (!Directory.Exists(binDir)) continue;

            var dll = Directory
                .EnumerateFiles(binDir, project + ".dll", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (dll is not null) found.Add(dll);
        }

        return found;
    }

    static List<string> FindSelfRecursiveEqualityMethods(string assemblyPath)
    {
        List<string> offenders = [];

        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);

        var md = pe.GetMetadataReader();

        foreach (var typeHandle in md.TypeDefinitions)
        {
            var type = md.GetTypeDefinition(typeHandle);

            foreach (var methodHandle in type.GetMethods())
            {
                var method = md.GetMethodDefinition(methodHandle);

                if (md.GetString(method.Name) is not "Equals") continue;

                if (method.RelativeVirtualAddress == 0) continue;

                var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();

                if (il is null) continue;

                foreach (var callee in EnumerateCallTargets(il))
                {
                    if (!CallsOwnEqualityOperator(md, callee, typeHandle)) continue;

                    offenders.Add($"{md.GetString(type.Name)}.Equals");
                    break;
                }
            }
        }

        return offenders;
    }

    static bool CallsOwnEqualityOperator(MetadataReader md, EntityHandle callee, TypeDefinitionHandle owner)
    {
        switch (callee.Kind)
        {
            case HandleKind.MethodDefinition:
                var target = md.GetMethodDefinition((MethodDefinitionHandle)callee);

                return md.GetString(target.Name) is "op_Equality" or "op_Inequality"
                    && target.GetDeclaringType() == owner;

            case HandleKind.MemberReference:
                var reference = md.GetMemberReference((MemberReferenceHandle)callee);

                return md.GetString(reference.Name) is "op_Equality" or "op_Inequality"
                    && reference.Parent.Kind is HandleKind.TypeDefinition
                    && (TypeDefinitionHandle)reference.Parent == owner;

            default:
                return false;
        }
    }

    /// <summary>
    /// Recorre el IL respetando el tamano real de cada operando (no basta con buscar
    /// el byte 0x28 a ciegas: un operando podria contener ese valor y dar un falso
    /// positivo) y devuelve los tokens de todas las instrucciones call/callvirt.
    /// </summary>
    static IEnumerable<EntityHandle> EnumerateCallTargets(byte[] il)
    {
        var opCodes = BuildOpCodeTable();

        for (var i = 0; i < il.Length;)
        {
            short value = il[i];
            i++;

            if (value == 0xFE && i < il.Length)
            {
                value = (short)(0xFE00 | il[i]);
                i++;
            }

            if (!opCodes.TryGetValue(value, out var opCode)) yield break;

            if (opCode.OperandType is OperandType.InlineSwitch)
            {
                if (i + 4 > il.Length) yield break;

                var count = BitConverter.ToInt32(il, i);
                i += 4 + (count * 4);
                continue;
            }

            var operandSize = OperandSize(opCode.OperandType);

            if (i + operandSize > il.Length) yield break;

            if (opCode == OpCodes.Call || opCode == OpCodes.Callvirt)
                yield return MetadataTokens.EntityHandle(BitConverter.ToInt32(il, i));

            i += operandSize;
        }
    }

    static int OperandSize(OperandType operandType) => operandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        _ => 4
    };

    static Dictionary<short, OpCode> BuildOpCodeTable()
    {
        Dictionary<short, OpCode> table = [];

        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is OpCode opCode) table[opCode.Value] = opCode;
        }

        return table;
    }
}

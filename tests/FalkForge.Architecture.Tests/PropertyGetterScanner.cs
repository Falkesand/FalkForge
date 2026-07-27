using System.Collections.Frozen;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace FalkForge.Architecture.Tests;

/// <summary>
/// Answers one question about a compiled assembly: which property getters does its IL actually
/// call? Reads are found in the emitted IL rather than in source text, because a source scan
/// cannot tell <c>msixModel.Scope</c> from <c>bundleModel.Scope</c> — the exact confusion that
/// let four accepted-then-ignored properties through code review.
/// </summary>
/// <remarks>
/// <para>
/// Calls made from within the declaring type itself are ignored: a model that reads its own
/// property (a computed member, or the compiler-generated <c>Equals</c>/<c>ToString</c> of a
/// record) does not make that property part of any compiler's behaviour.
/// </para>
/// <para>
/// The IL walk asserts it lands exactly on the end of every method body. A mistake in the
/// operand-size table therefore fails loudly on the first affected method instead of silently
/// mis-reading tokens.
/// </para>
/// </remarks>
internal static class PropertyGetterScanner
{
    private const byte TwoBytePrefix = 0xFE;
    private const byte Call = 0x28;
    private const byte CallVirt = 0x6F;
    private const byte Switch = 0x45;
    private const byte LdFtn = 0x06;      // 0xFE06, preceded by the two-byte prefix
    private const byte LdVirtFtn = 0x07;  // 0xFE07

    private static readonly byte[] OperandSizes = BuildOperandSizes();

    /// <summary>
    /// Returns every <c>(declaring type full name, property name)</c> pair whose getter is called
    /// by IL in <paramref name="assemblyPath"/>, limited to the types in
    /// <paramref name="typesOfInterest"/>.
    /// </summary>
    public static HashSet<(string Type, string Property)> FindGetterCalls(
        string assemblyPath, FrozenSet<string> typesOfInterest)
    {
        var found = new HashSet<(string, string)>();

        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        foreach (var methodHandle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (method.RelativeVirtualAddress == 0)
                continue;

            // Self-reads do not count — see the type remarks.
            var owner = GetTypeFullName(reader, method.GetDeclaringType());
            if (typesOfInterest.Contains(owner))
                continue;

            var body = peReader.GetMethodBody(method.RelativeVirtualAddress);
            ScanMethodBody(reader, body.GetILBytes(), typesOfInterest, found,
                describeMethod: () => owner + "." + reader.GetString(method.Name));
        }

        return found;
    }

    private static void ScanMethodBody(
        MetadataReader reader,
        byte[]? il,
        FrozenSet<string> typesOfInterest,
        HashSet<(string, string)> found,
        Func<string> describeMethod)
    {
        if (il is null)
            return;

        var position = 0;
        while (position < il.Length)
        {
            var opcode = il[position++];
            int operandSize;
            var isCall = false;

            if (opcode == TwoBytePrefix)
            {
                var second = il[position++];
                operandSize = TwoByteOperandSize(second);
                isCall = second is LdFtn or LdVirtFtn;
            }
            else if (opcode == Switch)
            {
                var targetCount = BitConverter.ToInt32(il, position);
                operandSize = 4 + (4 * targetCount);
            }
            else
            {
                operandSize = OperandSizes[opcode];
                isCall = opcode is Call or CallVirt;
            }

            if (position + operandSize > il.Length)
                throw new InvalidOperationException(
                    $"IL walk overran the body of {describeMethod()} at offset {position} — the operand-size table is wrong.");

            if (isCall)
            {
                var target = ResolveMember(reader, BitConverter.ToInt32(il, position));
                if (target is { } member &&
                    member.Name.StartsWith("get_", StringComparison.Ordinal) &&
                    typesOfInterest.Contains(member.Type))
                {
                    found.Add((member.Type, member.Name["get_".Length..]));
                }
            }

            position += operandSize;
        }

        if (position != il.Length)
            throw new InvalidOperationException(
                $"IL walk desynchronised in {describeMethod()} — the operand-size table is wrong.");
    }

    private static (string Type, string Name)? ResolveMember(MetadataReader reader, int token)
    {
        var handle = MetadataTokens.EntityHandle(token);
        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
            {
                var method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                return (GetTypeFullName(reader, method.GetDeclaringType()), reader.GetString(method.Name));
            }

            case HandleKind.MemberReference:
            {
                var member = reader.GetMemberReference((MemberReferenceHandle)handle);
                if (member.Parent.Kind != HandleKind.TypeReference)
                    return null;

                var typeRef = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
                var ns = reader.GetString(typeRef.Namespace);
                var name = reader.GetString(typeRef.Name);
                return (ns.Length == 0 ? name : ns + "." + name, reader.GetString(member.Name));
            }

            default:
                // MethodSpecification (generic instantiation) and everything else: the model
                // types under guard are non-generic, so they cannot be reached this way.
                return null;
        }
    }

    private static string GetTypeFullName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        var ns = reader.GetString(type.Namespace);
        var name = reader.GetString(type.Name);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    private static byte TwoByteOperandSize(byte second) => second switch
    {
        0x06 or 0x07 or 0x15 or 0x16 or 0x1C => 4, // ldftn, ldvirtftn, initobj, constrained., sizeof
        0x09 or 0x0A or 0x0B or 0x0C or 0x0D or 0x0E => 2, // ldarg, ldarga, starg, ldloc, ldloca, stloc
        0x12 or 0x19 => 1, // unaligned., no.
        _ => 0
    };

    // ECMA-335 III.1.2 operand sizes for the single-byte opcodes. Everything not listed takes no
    // operand; 0x45 (switch) is variable and handled by the walker.
    private static byte[] BuildOperandSizes()
    {
        var sizes = new byte[256];

        byte[] oneByte =
        [
            0x0E, 0x0F, 0x10, 0x11, 0x12, 0x13, // ldarg.s .. stloc.s
            0x1F,                               // ldc.i4.s
            0xDE                                // leave.s
        ];
        foreach (var opcode in oneByte)
            sizes[opcode] = 1;

        // Short-form branches br.s .. blt.un.s
        for (var opcode = 0x2B; opcode <= 0x37; opcode++)
            sizes[opcode] = 1;

        // Long-form branches br .. blt.un
        for (var opcode = 0x38; opcode <= 0x44; opcode++)
            sizes[opcode] = 4;

        byte[] fourByte =
        [
            0x20, 0x22,                                     // ldc.i4, ldc.r4
            0x27, 0x28, 0x29,                               // jmp, call, calli
            0x6F, 0x70, 0x71, 0x72, 0x73, 0x74, 0x75,       // callvirt, cpobj, ldobj, ldstr, newobj, castclass, isinst
            0x79,                                           // unbox
            0x7B, 0x7C, 0x7D, 0x7E, 0x7F, 0x80, 0x81,       // ldfld .. stobj
            0x8C, 0x8D, 0x8F,                               // box, newarr, ldelema
            0xA3, 0xA4, 0xA5,                               // ldelem, stelem, unbox.any
            0xC2, 0xC6,                                     // refanyval, mkrefany
            0xD0,                                           // ldtoken
            0xDD                                            // leave
        ];
        foreach (var opcode in fourByte)
            sizes[opcode] = 4;

        sizes[0x21] = 8; // ldc.i8
        sizes[0x23] = 8; // ldc.r8

        return sizes;
    }
}

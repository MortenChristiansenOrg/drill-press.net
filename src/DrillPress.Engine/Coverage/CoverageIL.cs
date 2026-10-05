using System.Reflection.Metadata;

namespace DrillPress.Engine;

internal static class CoverageIL
{
    internal static CoverageInstruction[]? Read(MethodBodyBlock body)
    {
        try
        {
            var reader = body.GetILReader();
            var instructions = new List<CoverageInstruction>();
            while (reader.RemainingBytes > 0)
            {
                var offset = reader.Offset;
                var code = (ILOpCode)reader.ReadByte();
                if ((byte)code == 0xfe)
                    code = (ILOpCode)(0xfe00 | reader.ReadByte());
                if (!Enum.IsDefined(code) || code == ILOpCode.Tail)
                    return null;
                var targets = Array.Empty<int>();
                var operand = 0;
                if (code == ILOpCode.Switch)
                {
                    var count = reader.ReadInt32();
                    if (count < 0 || count > reader.RemainingBytes / 4)
                        return null;
                    var end = reader.Offset + count * 4;
                    targets = new int[count];
                    for (var i = 0; i < count; i++)
                        targets[i] = reader.ReadInt32() + end;
                }
                else if (code.IsBranch())
                {
                    operand =
                        code.GetBranchOperandSize() == 1 ? reader.ReadSByte() : reader.ReadInt32();
                    targets = [reader.Offset + operand];
                }
                else
                {
                    var size = OperandSize(code);
                    if (size == 4)
                        operand = reader.ReadInt32();
                    else
                        reader.ReadBytes(size);
                }
                instructions.Add(new(offset, reader.Offset, code, operand, targets));
            }
            var result = instructions.ToArray();
            var offsets = result.Select(instruction => instruction.Offset).ToHashSet();
            return result.Any(instruction =>
                instruction.Targets.Any(target => !offsets.Contains(target))
            )
                ? null
                : result;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    internal static int[] Blocks(CoverageInstruction[] instructions, MethodBodyBlock body)
    {
        var starts = new HashSet<int> { 0 };
        foreach (var instruction in instructions)
        {
            starts.UnionWith(instruction.Targets);
            if (EndsBlock(instruction.OpCode) && instruction.End < body.GetILReader().Length)
                starts.Add(instruction.End);
        }
        foreach (var region in body.ExceptionRegions)
        {
            starts.Add(region.TryOffset);
            starts.Add(region.HandlerOffset);
            if (region.Kind == ExceptionRegionKind.Filter)
                starts.Add(region.FilterOffset);
        }
        return starts.Order().ToArray();
    }

    internal static int Ordinal(int[] offsets, int offset)
    {
        var result = Array.BinarySearch(offsets, offset);
        return result >= 0 ? result : ~result - 1;
    }

    private static bool EndsBlock(ILOpCode code) =>
        code.IsBranch()
        || code
            is ILOpCode.Break
                or ILOpCode.Switch
                or ILOpCode.Ret
                or ILOpCode.Endfinally
                or ILOpCode.Endfilter
                or ILOpCode.Jmp
                or ILOpCode.Call
                or ILOpCode.Calli
                or ILOpCode.Callvirt
                or ILOpCode.Newobj
                or ILOpCode.Throw
                or ILOpCode.Rethrow;

    private static int OperandSize(ILOpCode code) =>
        code switch
        {
            ILOpCode.Ldarg_s
            or ILOpCode.Ldarga_s
            or ILOpCode.Starg_s
            or ILOpCode.Ldloc_s
            or ILOpCode.Ldloca_s
            or ILOpCode.Stloc_s
            or ILOpCode.Ldc_i4_s
            or ILOpCode.Unaligned => 1,
            ILOpCode.Ldarg
            or ILOpCode.Ldarga
            or ILOpCode.Starg
            or ILOpCode.Ldloc
            or ILOpCode.Ldloca
            or ILOpCode.Stloc => 2,
            ILOpCode.Ldc_i4
            or ILOpCode.Ldc_r4
            or ILOpCode.Jmp
            or ILOpCode.Call
            or ILOpCode.Calli
            or ILOpCode.Callvirt
            or ILOpCode.Cpobj
            or ILOpCode.Ldobj
            or ILOpCode.Ldstr
            or ILOpCode.Newobj
            or ILOpCode.Castclass
            or ILOpCode.Isinst
            or ILOpCode.Unbox
            or ILOpCode.Ldfld
            or ILOpCode.Ldflda
            or ILOpCode.Stfld
            or ILOpCode.Ldsfld
            or ILOpCode.Ldsflda
            or ILOpCode.Stsfld
            or ILOpCode.Stobj
            or ILOpCode.Box
            or ILOpCode.Newarr
            or ILOpCode.Ldelema
            or ILOpCode.Ldelem
            or ILOpCode.Stelem
            or ILOpCode.Unbox_any
            or ILOpCode.Refanyval
            or ILOpCode.Mkrefany
            or ILOpCode.Ldtoken
            or ILOpCode.Ldftn
            or ILOpCode.Ldvirtftn
            or ILOpCode.Initobj
            or ILOpCode.Constrained
            or ILOpCode.Sizeof => 4,
            ILOpCode.Ldc_i8 or ILOpCode.Ldc_r8 => 8,
            _ => 0,
        };
}

using System.Reflection.Metadata;

namespace DrillPress.Engine;

internal static class CoverageCallPrefix
{
    internal static bool IsSafe(IEnumerable<CoverageInstruction> instructions)
    {
        return instructions.All(instruction => IsPure(instruction.OpCode));
    }

    private static bool IsPure(ILOpCode code) =>
        code
            is ILOpCode.Nop
                or ILOpCode.Ldarg_0
                or ILOpCode.Ldarg_1
                or ILOpCode.Ldarg_2
                or ILOpCode.Ldarg_3
                or ILOpCode.Ldarg
                or ILOpCode.Ldarg_s
                or ILOpCode.Ldarga
                or ILOpCode.Ldarga_s
                or ILOpCode.Ldloc_0
                or ILOpCode.Ldloc_1
                or ILOpCode.Ldloc_2
                or ILOpCode.Ldloc_3
                or ILOpCode.Ldloc
                or ILOpCode.Ldloc_s
                or ILOpCode.Ldloca
                or ILOpCode.Ldloca_s
                or ILOpCode.Stloc_0
                or ILOpCode.Stloc_1
                or ILOpCode.Stloc_2
                or ILOpCode.Stloc_3
                or ILOpCode.Stloc
                or ILOpCode.Stloc_s
                or ILOpCode.Ldnull
                or ILOpCode.Dup
                or ILOpCode.Pop
                or ILOpCode.Ldc_i4_m1
                or ILOpCode.Ldc_i4_0
                or ILOpCode.Ldc_i4_1
                or ILOpCode.Ldc_i4_2
                or ILOpCode.Ldc_i4_3
                or ILOpCode.Ldc_i4_4
                or ILOpCode.Ldc_i4_5
                or ILOpCode.Ldc_i4_6
                or ILOpCode.Ldc_i4_7
                or ILOpCode.Ldc_i4_8
                or ILOpCode.Ldc_i4
                or ILOpCode.Ldc_i4_s
                or ILOpCode.Ldc_i8
                or ILOpCode.Ldc_r4
                or ILOpCode.Ldc_r8
                or ILOpCode.Conv_i
                or ILOpCode.Conv_i1
                or ILOpCode.Conv_i2
                or ILOpCode.Conv_i4
                or ILOpCode.Conv_i8
                or ILOpCode.Conv_u
                or ILOpCode.Conv_u1
                or ILOpCode.Conv_u2
                or ILOpCode.Conv_u4
                or ILOpCode.Conv_u8
                or ILOpCode.Conv_r4
                or ILOpCode.Conv_r8
                or ILOpCode.Conv_r_un
                or ILOpCode.Add
                or ILOpCode.Sub
                or ILOpCode.Mul
                or ILOpCode.And
                or ILOpCode.Or
                or ILOpCode.Xor
                or ILOpCode.Neg
                or ILOpCode.Not
                or ILOpCode.Shl
                or ILOpCode.Shr
                or ILOpCode.Shr_un
                or ILOpCode.Ceq
                or ILOpCode.Clt
                or ILOpCode.Clt_un
                or ILOpCode.Cgt
                or ILOpCode.Cgt_un
                or ILOpCode.Constrained
                or ILOpCode.Readonly;
}

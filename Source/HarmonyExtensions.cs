using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace DoorsExpanded
{
    public static class HarmonyExtensions
    {
        // Slightly faster overload of OperandIs for MemberInfo operands (such as FieldInfo, MethodInfo, Type).
        public static bool OperandIs(this CodeInstruction instruction, MemberInfo member) =>
            Equals(instruction.operand, member);

        public static bool Is(this CodeInstruction instruction, OpCode opcode, MemberInfo member) =>
            instruction.opcode == opcode && instruction.OperandIs(member);
    }
}

// Ported from luadec-rust (https://github.com/ItsLucas/luadecng-rs), MIT License.
using System;

namespace LuaDecompiler.Lua51
{
    /// <summary>
    /// A decoded Lua 5.1 instruction.
    /// Layout (32-bit): B(9) | C(9) | A(8) | OpCode(6); Bx = B and C merged (18 bits).
    /// </summary>
    public readonly struct Instruction : IEquatable<Instruction>
    {
        private const int SizeOp = 6, SizeA = 8, SizeC = 9, SizeB = 9, SizeBx = SizeC + SizeB;
        private const int PosOp = 0, PosA = PosOp + SizeOp, PosC = PosA + SizeA, PosB = PosC + SizeC, PosBx = PosC;
        private const uint MaxArgBx = (1u << SizeBx) - 1;
        private const int MaxArgSBx = (int)(MaxArgBx >> 1);

        /// <summary>Bit 8 of a B/C field indicates a constant index rather than a register.</summary>
        public const uint BitRK = 1u << (SizeB - 1);

        /// <summary>Number of list items to accumulate before a SETLIST instruction.</summary>
        public const uint LFieldsPerFlush = 50;

        public readonly uint Raw;
        public readonly OpCode Op;
        public readonly uint A;
        private readonly uint _b, _c, _bx;
        private readonly int _sbx;
        public readonly OpMode Mode;

        private Instruction(uint raw, OpCode op, uint a, OpMode mode, uint b, uint c, uint bx, int sbx)
        {
            Raw = raw; Op = op; A = a; Mode = mode; _b = b; _c = c; _bx = bx; _sbx = sbx;
        }

        public static bool IsK(uint x) => (x & BitRK) != 0;

        public static uint IndexK(uint x) => x & ~BitRK;

        private static uint Mask(int n) => (1u << n) - 1;

        /// <summary>Decode a raw 32-bit value; returns null for an unknown opcode.</summary>
        public static Instruction? Decode(uint raw)
        {
            uint opVal = (raw >> PosOp) & Mask(SizeOp);
            if (opVal >= OpCodes.NumOpcodes)
                return null;
            var op = (OpCode)opVal;
            uint a = (raw >> PosA) & Mask(SizeA);
            switch (op.GetProps().Mode)
            {
                case OpMode.ABC:
                    return new Instruction(raw, op, a, OpMode.ABC, (raw >> PosB) & Mask(SizeB), (raw >> PosC) & Mask(SizeC), 0, 0);
                case OpMode.ABx:
                    return new Instruction(raw, op, a, OpMode.ABx, 0, 0, (raw >> PosBx) & Mask(SizeBx), 0);
                default:
                    {
                        uint bx = (raw >> PosBx) & Mask(SizeBx);
                        return new Instruction(raw, op, a, OpMode.AsBx, 0, 0, 0, (int)bx - MaxArgSBx);
                    }
            }
        }

        public uint B => Mode == OpMode.ABC ? _b : throw new InvalidOperationException("B on non-ABC instruction");
        public uint C => Mode == OpMode.ABC ? _c : throw new InvalidOperationException("C on non-ABC instruction");
        public uint Bx => Mode == OpMode.ABx ? _bx : throw new InvalidOperationException("Bx on non-ABx instruction");
        public int SBx => Mode == OpMode.AsBx ? _sbx : throw new InvalidOperationException("sBx on non-AsBx instruction");

        public bool Equals(Instruction other) => Raw == other.Raw;
        public override bool Equals(object obj) => obj is Instruction i && Equals(i);
        public override int GetHashCode() => (int)Raw;

        public override string ToString()
        {
            switch (Mode)
            {
                case OpMode.ABC: return $"{Op.GetName(),-12} {A} {_b} {_c}";
                case OpMode.ABx: return $"{Op.GetName(),-12} {A} {_bx}";
                default: return $"{Op.GetName(),-12} {A} {_sbx}";
            }
        }

        // Encode helpers (useful for testing)
        public static uint EncodeABC(OpCode op, uint a, uint b, uint c) =>
            ((uint)op << PosOp) | (a << PosA) | (b << PosB) | (c << PosC);

        public static uint EncodeABx(OpCode op, uint a, uint bx) =>
            ((uint)op << PosOp) | (a << PosA) | (bx << PosBx);

        public static uint EncodeAsBx(OpCode op, uint a, int sbx) =>
            ((uint)op << PosOp) | (a << PosA) | ((uint)(sbx + MaxArgSBx) << PosBx);
    }
}

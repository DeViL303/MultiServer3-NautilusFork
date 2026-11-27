// Ported from luadec-rust (https://github.com/ItsLucas/luadecng-rs), MIT License.
using System.Collections.Generic;

namespace LuaDecompiler.Lua51
{
    /// <summary>Per-instruction register usage info.</summary>
    public sealed class RegUsage
    {
        public List<uint> Defs = new List<uint>();
        public List<uint> Uses = new List<uint>();
    }

    /// <summary>Liveness information for a CFG.</summary>
    public sealed class LivenessInfo
    {
        public bool[][] LiveIn;
        public bool[][] LiveOut;
        public int MaxReg;
    }

    public static class Liveness
    {
        /// <summary>
        /// For an instruction with a "variable" count (B == 0 on CALL/TAILCALL/RETURN/SETLIST), the values run
        /// up to the stack top set by the open CALL (C == 0) or VARARG (B == 0) just before it.
        /// Returns that register, or null if the previous instruction did not leave an open result.
        /// (Port fix: the original treated B == 0 as "no values", dropping things like print(f(x)).)
        /// </summary>
        public static uint? OpenTop(Instruction? prev)
        {
            if (prev == null) return null;
            var p = prev.Value;
            if (p.Op == OpCode.Call && p.C == 0) return p.A;
            if (p.Op == OpCode.VarArg && p.B == 0) return p.A;
            return null;
        }

        /// <summary>Previous instruction (pc - 1) or null.</summary>
        public static Instruction? Prev(ControlFlowGraph cfg, int pc) => pc > 0 ? cfg.Instructions[pc - 1] : (Instruction?)null;

        /// <summary>Compute register defs/uses for a single instruction.</summary>
        public static RegUsage InstructionRegUsage(Instruction inst, Instruction? prev = null)
        {
            var u = new RegUsage();
            uint a = inst.A;
            switch (inst.Op)
            {
                case OpCode.Move:
                    u.Defs.Add(a); u.Uses.Add(inst.B);
                    break;
                case OpCode.LoadK:
                case OpCode.LoadBool:
                    u.Defs.Add(a);
                    break;
                case OpCode.LoadNil:
                    for (uint r = a; r <= inst.B; r++) u.Defs.Add(r);
                    break;
                case OpCode.GetUpval:
                case OpCode.GetGlobal:
                    u.Defs.Add(a);
                    break;
                case OpCode.GetTable:
                    u.Defs.Add(a); u.Uses.Add(inst.B);
                    if (!Instruction.IsK(inst.C)) u.Uses.Add(inst.C);
                    break;
                case OpCode.SetGlobal:
                case OpCode.SetUpval:
                    u.Uses.Add(a);
                    break;
                case OpCode.SetTable:
                    u.Uses.Add(a);
                    if (!Instruction.IsK(inst.B)) u.Uses.Add(inst.B);
                    if (!Instruction.IsK(inst.C)) u.Uses.Add(inst.C);
                    break;
                case OpCode.NewTable:
                    u.Defs.Add(a);
                    break;
                case OpCode.Self_:
                    u.Defs.Add(a); u.Defs.Add(a + 1); u.Uses.Add(inst.B);
                    if (!Instruction.IsK(inst.C)) u.Uses.Add(inst.C);
                    break;
                case OpCode.Add:
                case OpCode.Sub:
                case OpCode.Mul:
                case OpCode.Div:
                case OpCode.Mod:
                case OpCode.Pow:
                    u.Defs.Add(a);
                    if (!Instruction.IsK(inst.B)) u.Uses.Add(inst.B);
                    if (!Instruction.IsK(inst.C)) u.Uses.Add(inst.C);
                    break;
                case OpCode.Unm:
                case OpCode.Not:
                case OpCode.Len:
                    u.Defs.Add(a); u.Uses.Add(inst.B);
                    break;
                case OpCode.Concat:
                    u.Defs.Add(a);
                    for (uint r = inst.B; r <= inst.C; r++) u.Uses.Add(r);
                    break;
                case OpCode.Jmp:
                    break;
                case OpCode.Eq:
                case OpCode.Lt:
                case OpCode.Le:
                    if (!Instruction.IsK(inst.B)) u.Uses.Add(inst.B);
                    if (!Instruction.IsK(inst.C)) u.Uses.Add(inst.C);
                    break;
                case OpCode.Test:
                    u.Uses.Add(a);
                    break;
                case OpCode.TestSet:
                    u.Defs.Add(a); u.Uses.Add(inst.B);
                    break;
                case OpCode.Call:
                    {
                        uint numArgs = inst.B == 0 ? OpenCount(a + 1, prev) : inst.B - 1;
                        u.Uses.Add(a);
                        for (uint i = 1; i <= numArgs; i++) u.Uses.Add(a + i);
                        uint numResults = inst.C == 0 ? 1 : inst.C - 1;
                        for (uint i = 0; i < numResults; i++) u.Defs.Add(a + i);
                        break;
                    }
                case OpCode.TailCall:
                    {
                        uint numArgs = inst.B == 0 ? OpenCount(a + 1, prev) : inst.B - 1;
                        u.Uses.Add(a);
                        for (uint i = 1; i <= numArgs; i++) u.Uses.Add(a + i);
                        break;
                    }
                case OpCode.Return:
                    {
                        uint numRet = inst.B == 0 ? OpenCount(a, prev) : inst.B - 1;
                        for (uint i = 0; i < numRet; i++) u.Uses.Add(a + i);
                        break;
                    }
                case OpCode.ForLoop:
                    u.Uses.Add(a); u.Uses.Add(a + 1); u.Uses.Add(a + 2);
                    u.Defs.Add(a); u.Defs.Add(a + 3);
                    break;
                case OpCode.ForPrep:
                    u.Uses.Add(a); u.Uses.Add(a + 2); u.Defs.Add(a);
                    break;
                case OpCode.TForLoop:
                    {
                        u.Uses.Add(a); u.Uses.Add(a + 1); u.Uses.Add(a + 2);
                        uint numVars = inst.C;
                        for (uint i = 0; i <= numVars; i++) u.Defs.Add(a + 3 + i);
                        break;
                    }
                case OpCode.SetList:
                    {
                        uint num = inst.B == 0 ? OpenCount(a + 1, prev) : inst.B;
                        u.Uses.Add(a);
                        for (uint i = 1; i <= num; i++) u.Uses.Add(a + i);
                        break;
                    }
                case OpCode.Close:
                    break;
                case OpCode.Closure:
                    u.Defs.Add(a);
                    break;
                case OpCode.VarArg:
                    {
                        uint num = inst.B == 0 ? 1 : inst.B - 1;
                        for (uint i = 0; i < num; i++) u.Defs.Add(a + i);
                        break;
                    }
            }
            return u;
        }

        /// <summary>Number of registers from <paramref name="first"/> up to the open top (inclusive), or 0.</summary>
        public static uint OpenCount(uint first, Instruction? prev)
        {
            var top = OpenTop(prev);
            return top.HasValue && top.Value >= first ? top.Value - first + 1 : 0;
        }

        /// <summary>Compute per-block live-in and live-out sets.</summary>
        public static LivenessInfo ComputeLiveness(ControlFlowGraph cfg, int maxStack)
        {
            int n = cfg.NumBlocks;
            int nregs = maxStack;
            if (n == 0)
                return new LivenessInfo { LiveIn = new bool[0][], LiveOut = new bool[0][], MaxReg = nregs };

            var useSet = NewMatrix(n, nregs);
            var kill = NewMatrix(n, nregs);

            for (int bid = 0; bid < n; bid++)
            {
                var block = cfg.Blocks[bid];
                for (int pc = block.Start; pc <= block.End; pc++)
                {
                    var usage = InstructionRegUsage(cfg.Instructions[pc], Prev(cfg, pc));
                    foreach (uint ru in usage.Uses)
                    {
                        long r = ru;
                        if (r < nregs && !kill[bid][r]) useSet[bid][r] = true;
                    }
                    foreach (uint rd in usage.Defs)
                    {
                        long r = rd;
                        if (r < nregs) kill[bid][r] = true;
                    }
                }
            }

            var liveIn = NewMatrix(n, nregs);
            var liveOut = NewMatrix(n, nregs);

            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int bid = n - 1; bid >= 0; bid--)
                {
                    var succs = cfg.Blocks[bid].Successors;
                    for (int r = 0; r < nregs; r++)
                    {
                        bool o = false;
                        foreach (int succ in succs)
                        {
                            if (succ < n && liveIn[succ][r]) { o = true; break; }
                        }
                        liveOut[bid][r] = o;
                    }
                    for (int r = 0; r < nregs; r++)
                    {
                        bool newIn = useSet[bid][r] || (liveOut[bid][r] && !kill[bid][r]);
                        if (newIn != liveIn[bid][r])
                        {
                            liveIn[bid][r] = newIn;
                            changed = true;
                        }
                    }
                }
            }

            return new LivenessInfo { LiveIn = liveIn, LiveOut = liveOut, MaxReg = nregs };
        }

        /// <summary>Check if a register is live after the given pc (local scan within its block).</summary>
        public static bool IsRegLiveAfter(ControlFlowGraph cfg, LivenessInfo liveness, int pc, uint reg)
        {
            long r = reg;
            if (r >= liveness.MaxReg) return false;
            int blockId = cfg.BlockOf(pc);
            var block = cfg.Blocks[blockId];
            bool live = liveness.LiveOut[blockId][r];
            for (int scanPc = block.End; scanPc >= pc + 1; scanPc--)
            {
                var usage = InstructionRegUsage(cfg.Instructions[scanPc], Prev(cfg, scanPc));
                if (usage.Defs.Contains(reg)) live = false;
                if (usage.Uses.Contains(reg)) live = true;
            }
            return live;
        }

        private static bool[][] NewMatrix(int n, int m)
        {
            var x = new bool[n][];
            for (int i = 0; i < n; i++) x[i] = new bool[m];
            return x;
        }
    }
}

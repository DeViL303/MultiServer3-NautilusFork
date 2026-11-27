// Ported from luadec-rust (https://github.com/ItsLucas/luadecng-rs), MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LuaDecompiler.Lua51
{
    /// <summary>Type of edge connecting two basic blocks.</summary>
    public enum EdgeKind
    {
        Fallthrough,
        Jump,
        ConditionalTrue,
        ConditionalFalse,
        ForLoopBack,
        ForLoopExit,
        ForPrep,
        TForLoopBack,
        TForLoopExit,
    }

    public readonly struct Edge
    {
        public readonly int From;
        public readonly int To;
        public readonly EdgeKind Kind;
        public Edge(int from, int to, EdgeKind kind) { From = from; To = to; Kind = kind; }
    }

    /// <summary>A basic block: a maximal sequence of instructions with single entry / single exit.</summary>
    public sealed class BasicBlock
    {
        public int Id;
        /// <summary>First instruction index (inclusive).</summary>
        public int Start;
        /// <summary>Last instruction index (inclusive).</summary>
        public int End;
        public List<int> Successors = new List<int>();
        public List<int> Predecessors = new List<int>();
    }

    /// <summary>Control flow graph for a single Lua function/chunk.</summary>
    public sealed class ControlFlowGraph
    {
        public List<BasicBlock> Blocks;
        public List<Edge> Edges;
        public List<Instruction> Instructions;
        private int[] _instToBlock;

        public static ControlFlowGraph Build(IReadOnlyList<uint> rawInstructions)
        {
            var instructions = new List<Instruction>(rawInstructions.Count);
            foreach (var raw in rawInstructions)
            {
                var inst = Instruction.Decode(raw);
                if (inst == null)
                    throw new InvalidOperationException("invalid instruction");
                instructions.Add(inst.Value);
            }

            if (instructions.Count == 0)
            {
                return new ControlFlowGraph
                {
                    Blocks = new List<BasicBlock>(),
                    Edges = new List<Edge>(),
                    Instructions = instructions,
                    _instToBlock = Array.Empty<int>(),
                };
            }

            var leaders = FindLeaders(instructions);
            var blocks = CreateBlocks(leaders, instructions.Count);

            var instToBlock = new int[instructions.Count];
            for (int blockId = 0; blockId < blocks.Count; blockId++)
            {
                for (int pc = blocks[blockId].Start; pc <= blocks[blockId].End; pc++)
                    instToBlock[pc] = blockId;
            }

            var edges = BuildEdges(blocks, instructions, instToBlock);
            foreach (var edge in edges)
            {
                blocks[edge.From].Successors.Add(edge.To);
                blocks[edge.To].Predecessors.Add(edge.From);
            }

            return new ControlFlowGraph { Blocks = blocks, Edges = edges, Instructions = instructions, _instToBlock = instToBlock };
        }

        public int BlockOf(int pc) => _instToBlock[pc];

        public BasicBlock Block(int id) => Blocks[id];

        public int NumBlocks => Blocks.Count;

        private static bool InRange(long target, int len) => target >= 0 && target < len;

        private static SortedSet<int> FindLeaders(List<Instruction> instructions)
        {
            int len = instructions.Count;
            var leaders = new SortedSet<int> { 0 };

            for (int pc = 0; pc < len; pc++)
            {
                var inst = instructions[pc];
                switch (inst.Op)
                {
                    case OpCode.Jmp:
                        {
                            long target = (long)pc + 1 + inst.SBx;
                            if (InRange(target, len)) leaders.Add((int)target);
                            if (pc + 1 < len) leaders.Add(pc + 1);
                            break;
                        }
                    case OpCode.Eq:
                    case OpCode.Lt:
                    case OpCode.Le:
                    case OpCode.Test:
                    case OpCode.TestSet:
                        {
                            if (pc + 1 < len)
                            {
                                var next = instructions[pc + 1];
                                if (next.Op == OpCode.Jmp)
                                {
                                    long jmpTarget = (long)pc + 2 + next.SBx;
                                    if (InRange(jmpTarget, len)) leaders.Add((int)jmpTarget);
                                }
                            }
                            if (pc + 2 < len) leaders.Add(pc + 2);
                            break;
                        }
                    case OpCode.ForLoop:
                        {
                            long target = (long)pc + 1 + inst.SBx;
                            if (InRange(target, len)) leaders.Add((int)target);
                            if (pc + 1 < len) leaders.Add(pc + 1);
                            break;
                        }
                    case OpCode.ForPrep:
                        {
                            long target = (long)pc + 1 + inst.SBx;
                            if (InRange(target, len)) leaders.Add((int)target);
                            break;
                        }
                    case OpCode.TForLoop:
                        {
                            if (pc + 1 < len)
                            {
                                var next = instructions[pc + 1];
                                if (next.Op == OpCode.Jmp)
                                {
                                    long jmpTarget = (long)pc + 2 + next.SBx;
                                    if (InRange(jmpTarget, len)) leaders.Add((int)jmpTarget);
                                }
                            }
                            if (pc + 2 < len) leaders.Add(pc + 2);
                            break;
                        }
                    case OpCode.Return:
                    case OpCode.TailCall:
                        if (pc + 1 < len) leaders.Add(pc + 1);
                        break;
                }
            }
            return leaders;
        }

        private static List<BasicBlock> CreateBlocks(SortedSet<int> leaders, int numInstructions)
        {
            var leaderVec = leaders.ToList();
            var blocks = new List<BasicBlock>(leaderVec.Count);
            for (int i = 0; i < leaderVec.Count; i++)
            {
                int start = leaderVec[i];
                int end = i + 1 < leaderVec.Count ? leaderVec[i + 1] - 1 : numInstructions - 1;
                blocks.Add(new BasicBlock { Id = i, Start = start, End = end });
            }
            return blocks;
        }

        private static bool IsTestOp(OpCode op) =>
            op == OpCode.Eq || op == OpCode.Lt || op == OpCode.Le || op == OpCode.Test || op == OpCode.TestSet;

        private static List<Edge> BuildEdges(List<BasicBlock> blocks, List<Instruction> instructions, int[] instToBlock)
        {
            var edges = new List<Edge>();
            int len = instructions.Count;

            foreach (var block in blocks)
            {
                int lastPc = block.End;
                var last = instructions[lastPc];
                int blockId = block.Id;

                switch (last.Op)
                {
                    case OpCode.Jmp:
                        {
                            long target = (long)lastPc + 1 + last.SBx;
                            bool isConditional = lastPc > block.Start && IsTestOp(instructions[lastPc - 1].Op);
                            bool isTForLoop = lastPc > block.Start && instructions[lastPc - 1].Op == OpCode.TForLoop;

                            if (isConditional)
                            {
                                if (InRange(target, len))
                                    edges.Add(new Edge(blockId, instToBlock[target], EdgeKind.ConditionalFalse));
                                if (lastPc + 1 < len)
                                    edges.Add(new Edge(blockId, instToBlock[lastPc + 1], EdgeKind.ConditionalTrue));
                            }
                            else if (isTForLoop)
                            {
                                if (InRange(target, len))
                                    edges.Add(new Edge(blockId, instToBlock[target], EdgeKind.TForLoopBack));
                                if (lastPc + 1 < len)
                                    edges.Add(new Edge(blockId, instToBlock[lastPc + 1], EdgeKind.TForLoopExit));
                            }
                            else if (InRange(target, len))
                            {
                                edges.Add(new Edge(blockId, instToBlock[target], EdgeKind.Jump));
                            }
                            break;
                        }
                    case OpCode.Eq:
                    case OpCode.Lt:
                    case OpCode.Le:
                    case OpCode.Test:
                    case OpCode.TestSet:
                        if (lastPc + 1 < len)
                            edges.Add(new Edge(blockId, instToBlock[lastPc + 1], EdgeKind.Fallthrough));
                        break;
                    case OpCode.ForLoop:
                        {
                            long target = (long)lastPc + 1 + last.SBx;
                            if (InRange(target, len))
                                edges.Add(new Edge(blockId, instToBlock[target], EdgeKind.ForLoopBack));
                            if (lastPc + 1 < len)
                                edges.Add(new Edge(blockId, instToBlock[lastPc + 1], EdgeKind.ForLoopExit));
                            break;
                        }
                    case OpCode.ForPrep:
                        {
                            long target = (long)lastPc + 1 + last.SBx;
                            if (InRange(target, len))
                                edges.Add(new Edge(blockId, instToBlock[target], EdgeKind.ForPrep));
                            break;
                        }
                    case OpCode.TForLoop:
                        if (lastPc + 1 < len)
                            edges.Add(new Edge(blockId, instToBlock[lastPc + 1], EdgeKind.Fallthrough));
                        break;
                    case OpCode.Return:
                    case OpCode.TailCall:
                        break;
                    default:
                        if (lastPc + 1 < len)
                            edges.Add(new Edge(blockId, instToBlock[lastPc + 1], EdgeKind.Fallthrough));
                        break;
                }
            }
            return edges;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            foreach (var block in Blocks)
            {
                sb.Append($"Block {block.Id} [pc {block.Start}..{block.End}] pred=[{string.Join(", ", block.Predecessors)}] succ=[{string.Join(", ", block.Successors)}]\n");
                for (int pc = block.Start; pc <= block.End; pc++)
                    sb.Append($"  [{pc,4}] {Instructions[pc]}\n");
            }
            if (Edges.Count > 0)
            {
                sb.Append("Edges:\n");
                foreach (var e in Edges)
                    sb.Append($"  B{e.From} -> B{e.To} ({e.Kind})\n");
            }
            return sb.ToString();
        }
    }
}

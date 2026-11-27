// Ported from luadec-rust (https://github.com/ItsLucas/luadecng-rs), MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using LuaDecompiler.Parser;

namespace LuaDecompiler.Lua51
{
    /// <summary>Context for decompiling a single Lua function.</summary>
    public sealed partial class Lifter
    {
        private readonly LuaChunk _chunk;
        private readonly ControlFlowGraph _cfg;
        private readonly DominatorTree _dom;
        private readonly List<NaturalLoop> _loops;
        private readonly LivenessInfo _liveness;
        /// <summary>What expression each register currently holds (null = unknown).</summary>
        private readonly Expr[] _regs;
        /// <summary>Pending tables being constructed (register -> accumulated fields).</summary>
        private readonly Dictionary<uint, EqList<TableField>> _pendingTables = new Dictionary<uint, EqList<TableField>>();
        /// <summary>Stable references a register value has been assigned into.</summary>
        private readonly Dictionary<uint, Expr> _captureAliases = new Dictionary<uint, Expr>();
        /// <summary>Registers updated from their own previous value across branches.</summary>
        private HashSet<uint> _accumulatorRegs = new HashSet<uint>();
        /// <summary>Blocks already visited to prevent infinite recursion.</summary>
        private readonly HashSet<int> _visitedBlocks = new HashSet<int>();
        /// <summary>Local variable names assigned to registers.</summary>
        private readonly Dictionary<uint, string> _localNames = new Dictionary<uint, string>();
        /// <summary>Registers that have been declared as local.</summary>
        private readonly HashSet<uint> _declaredLocals = new HashSet<uint>();
        private readonly uint _numParams;
        private readonly bool _hasDebugInfo;
        /// <summary>Upvalue expressions resolved from the parent closure site (null entries = unknown).</summary>
        private readonly List<Expr> _resolvedUpvalues;
        private readonly List<int> _activeLoopHeaders = new List<int>();
        private readonly List<int> _activeLoopExits = new List<int>();

        private Lifter(LuaChunk chunk, List<Expr> resolvedUpvalues)
        {
            _chunk = chunk;
            _cfg = ControlFlowGraph.Build(chunk.Instructions);
            _dom = DominatorTree.Build(_cfg);
            _loops = Loops.FindLoops(_cfg, _dom);
            _liveness = Liveness.ComputeLiveness(_cfg, chunk.MaxStack);
            _hasDebugInfo = chunk.Locals.Count > 0;
            _regs = new Expr[Math.Max((int)chunk.MaxStack, 256)];
            _numParams = chunk.NumParams;
            _resolvedUpvalues = resolvedUpvalues;
        }

        public static Function Decompile(LuaChunk chunk) => DecompileWithUpvalues(chunk, new List<Expr>());

        private static Function DecompileWithUpvalues(LuaChunk chunk, List<Expr> resolvedUpvalues)
        {
            var lifter = new Lifter(chunk, resolvedUpvalues);
            lifter._accumulatorRegs = lifter.FindAccumulatorRegs();

            var paramNames = new EqList<string>();
            for (uint i = 0; i < chunk.NumParams; i++)
            {
                string name = lifter.LocalName(i, 0);
                lifter._localNames[i] = name;
                lifter._declaredLocals.Add(i);
                lifter.SetReg(i, new Expr.Name(name));
                paramNames.Add(name);
            }

            var body = lifter._cfg.NumBlocks > 0 ? lifter.LiftBlockRange(0, lifter._cfg.NumBlocks) : new EqList<Stat>();
            return new Function(paramNames, chunk.IsVararg, body);
        }

        /// <summary>Lift a range of blocks into a statement list, handling control structures.</summary>
        private EqList<Stat> LiftBlockRange(int startBlock, int endBlock)
        {
            var stmts = new EqList<Stat>();
            int blockIdx = startBlock;

            while (blockIdx < endBlock && blockIdx < _cfg.NumBlocks)
            {
                if (_visitedBlocks.Contains(blockIdx))
                {
                    blockIdx++;
                    continue;
                }

                var lp = FindLoopAt(blockIdx);
                if (lp != null)
                {
                    int next = LiftLoop(lp, stmts);
                    blockIdx = next <= blockIdx ? blockIdx + 1 : next;
                    continue;
                }

                _visitedBlocks.Add(blockIdx);
                var block = _cfg.Blocks[blockIdx];

                if (IsConditionalBlock(block))
                {
                    int next = LiftConditional(blockIdx, stmts);
                    if (next <= blockIdx)
                    {
                        LiftInstructions(block.Start, block.End, stmts);
                        blockIdx++;
                    }
                    else
                    {
                        blockIdx = next;
                    }
                    continue;
                }

                LiftInstructions(block.Start, block.End, stmts);

                // Does this block end with a JMP to a loop exit (break)?
                var lastInst = _cfg.Instructions[block.End];
                if (lastInst.Op == OpCode.Jmp && block.Successors.Count == 1)
                {
                    int target = block.Successors[0];
                    if (CurrentLoopExit() == target)
                        stmts.Add(new Stat.Break());
                    // Follow unconditional JMP: skip ahead to target block
                    if (target > blockIdx + 1)
                    {
                        blockIdx = target;
                        continue;
                    }
                }

                blockIdx++;
            }

            return stmts;
        }

        private int LiftLoop(NaturalLoop lp, EqList<Stat> stmts)
        {
            switch (lp.Kind)
            {
                case LoopKind.NumericFor: return LiftNumericFor(lp, stmts);
                case LoopKind.GenericFor: return LiftGenericFor(lp, stmts);
                default: return LiftWhile(lp, stmts);
            }
        }

        private int LiftNumericFor(NaturalLoop lp, EqList<Stat> stmts)
        {
            var header = _cfg.Blocks[lp.Header];
            int loopExit = MaxLoopBlock(lp) + 1;

            int? forprepBlock = FindForprepBlock(lp.Header);
            var forprepInst = forprepBlock.HasValue
                ? _cfg.Instructions[_cfg.Blocks[forprepBlock.Value].End]
                : _cfg.Instructions[header.Start];

            uint baseReg = forprepInst.A;
            string varName = LocalName(baseReg + 3, header.Start);

            // Lift the pre-loop setup to get init/limit/step
            if (forprepBlock.HasValue && !_visitedBlocks.Contains(forprepBlock.Value))
            {
                var b = _cfg.Blocks[forprepBlock.Value];
                _visitedBlocks.Add(forprepBlock.Value);
                if (b.End > b.Start)
                    LiftInstructions(b.Start, b.End - 1, stmts);
            }

            var startExpr = RegExpr(baseReg);
            var limitExpr = RegExpr(baseReg + 1);
            var stepExpr = RegExpr(baseReg + 2);
            // Port tweak: Lua 5.1 stores numbers as floats, so also drop a step of 1.0.
            bool stepIsOne = stepExpr is Expr.Number n
                && ((n.Value is NumLit.Int i && i.V == 1) || (n.Value is NumLit.Float f && f.V == 1.0));
            Expr step = stepIsOne ? null : stepExpr;

            // Port fix: register the loop variable (R(A+3)) so the body refers to it by name.
            // The original never did this, so loop bodies showed `r3` instead of the variable.
            _declaredLocals.Add(baseReg + 3);
            SetReg(baseReg + 3, new Expr.Name(varName));

            _activeLoopHeaders.Add(lp.Header);
            _activeLoopExits.Add(loopExit);
            var body = LiftBlockRange(lp.Header, lp.Latch + 1);
            _activeLoopExits.RemoveAt(_activeLoopExits.Count - 1);
            _activeLoopHeaders.RemoveAt(_activeLoopHeaders.Count - 1);

            stmts.Add(new Stat.NumericFor(varName, startExpr, limitExpr, step, body));
            return loopExit;
        }

        private int LiftGenericFor(NaturalLoop lp, EqList<Stat> stmts)
        {
            var header = _cfg.Blocks[lp.Header];

            Instruction? tforloopInst = null;
            for (int pc = header.Start; pc <= header.End; pc++)
            {
                if (_cfg.Instructions[pc].Op == OpCode.TForLoop) { tforloopInst = _cfg.Instructions[pc]; break; }
            }
            if (tforloopInst == null)
            {
                var latchBlock = _cfg.Blocks[lp.Latch];
                for (int pc = latchBlock.Start; pc <= latchBlock.End; pc++)
                {
                    if (_cfg.Instructions[pc].Op == OpCode.TForLoop) { tforloopInst = _cfg.Instructions[pc]; break; }
                }
            }
            var tfl = tforloopInst ?? _cfg.Instructions[header.End];

            uint baseReg = tfl.A;
            uint numVars = tfl.C;

            var names = new EqList<string>();
            for (uint i = 0; i < numVars; i++)
                names.Add(LocalName(baseReg + 3 + i, header.Start));

            // Register loop variable names so the body can reference them
            for (int i = 0; i < names.Count; i++)
            {
                uint r = baseReg + 3 + (uint)i;
                _localNames[r] = names[i];
                _declaredLocals.Add(r);
                SetReg(r, new Expr.Name(names[i]));
            }

            var iterExpr = RegExpr(baseReg);

            var bodyBlocks = lp.Body.Where(b => b != lp.Header).OrderBy(b => b).ToList();

            _activeLoopHeaders.Add(lp.Header);
            _activeLoopExits.Add(MaxLoopBlock(lp) + 1);
            var body = bodyBlocks.Count > 0 ? LiftBlockRange(bodyBlocks[0], bodyBlocks[bodyBlocks.Count - 1] + 1) : new EqList<Stat>();
            _activeLoopExits.RemoveAt(_activeLoopExits.Count - 1);
            _activeLoopHeaders.RemoveAt(_activeLoopHeaders.Count - 1);

            stmts.Add(new Stat.GenericFor(names, new EqList<Expr> { iterExpr }, body));
            return MaxLoopBlock(lp) + 1;
        }

        private int LiftWhile(NaturalLoop lp, EqList<Stat> stmts)
        {
            var cond = ExtractCondition(lp.Header) ?? new Expr.Bool(true);

            _activeLoopHeaders.Add(lp.Header);
            _activeLoopExits.Add(MaxLoopBlock(lp) + 1);
            var body = LiftBlockRange(lp.Header + 1, lp.Latch + 1);
            _activeLoopExits.RemoveAt(_activeLoopExits.Count - 1);
            _activeLoopHeaders.RemoveAt(_activeLoopHeaders.Count - 1);

            stmts.Add(new Stat.While(cond, body));
            return MaxLoopBlock(lp) + 1;
        }

        /// <summary>Lift an if/elseif/else chain.</summary>
        private int LiftConditional(int blockIdx, EqList<Stat> stmts)
        {
            int? orAnd = TryLiftOrAndChain(blockIdx, stmts);
            if (orAnd.HasValue)
                return orAnd.Value;

            var block = _cfg.Blocks[blockIdx];

            // Lift any instructions before the test/JMP at the end of this block.
            int? testPc = FindTestPc(block);
            if (testPc.HasValue && testPc.Value > block.Start)
                LiftInstructions(block.Start, testPc.Value - 1, stmts);

            var cond = ExtractCondition(blockIdx) ?? new Expr.Bool(true);

            var succs = block.Successors.ToList();
            if (succs.Count != 2)
            {
                LiftInstructions(block.Start, block.End, stmts);
                return blockIdx + 1;
            }

            // Edge order: ConditionalFalse (JMP target) first, ConditionalTrue (fallthrough) second.
            int falseTarget = succs[0];
            int trueTarget = succs[1];

            // Guard clause: `if not cond then return end`
            if (IsReturnBlock(trueTarget) && falseTarget > trueTarget
                && _cfg.Blocks[trueTarget].Predecessors.Count <= 1)
            {
                var guardBody = LiftBlockRange(trueTarget, trueTarget + 1);
                stmts.Add(new Stat.If(cond, guardBody, new EqList<ElseIfClause>(), null));
                return falseTarget;
            }

            // Inverted guard: `if cond then <continue> else return end`
            if (IsReturnBlock(falseTarget) && trueTarget < falseTarget
                && _cfg.Blocks[falseTarget].Predecessors.Count <= 1)
            {
                var guardBody = LiftBlockRange(falseTarget, falseTarget + 1);
                stmts.Add(new Stat.If(NegateExpr(cond), guardBody, new EqList<ElseIfClause>(), null));
                return trueTarget;
            }

            int? merge = FindMergePoint(blockIdx, trueTarget, falseTarget);
            int thenEnd = merge ?? falseTarget;
            var thenBlock = LiftBlockRange(trueTarget, thenEnd);

            EqList<Stat> elseBlock = null;
            if (merge.HasValue && falseTarget < merge.Value)
            {
                var eb = LiftBlockRange(falseTarget, merge.Value);
                if (eb.Count > 0) elseBlock = eb;
            }

            stmts.Add(new Stat.If(cond, thenBlock, new EqList<ElseIfClause>(), elseBlock));
            return merge ?? Math.Max(falseTarget, trueTarget) + 1;
        }
    }
}

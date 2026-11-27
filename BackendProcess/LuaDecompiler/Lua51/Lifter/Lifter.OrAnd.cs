// Ported from luadec-rust (https://github.com/ItsLucas/luadecng-rs), MIT License.
using System;
using System.Collections.Generic;

namespace LuaDecompiler.Lua51
{
    public sealed partial class Lifter
    {
        /// <summary>
        /// Detect and lift OR/AND short-circuit conditional chains.
        /// OR  (`if a or b then T end`): intermediate blocks jump (false edge) to T; the last block's true edge goes to T.
        /// AND (`if a and b then body end`): every block's false edge goes to END; true edges chain to the body.
        /// </summary>
        private int? TryLiftOrAndChain(int start, EqList<Stat> stmts)
        {
            var block = _cfg.Blocks[start];
            if (block.Successors.Count != 2) return null;
            if (BlockContainsTestset(start)) return null;

            int true0 = block.Successors[1];
            if (true0 >= _cfg.NumBlocks) return null;
            if (!IsConditionalBlock(_cfg.Blocks[true0])) return null;
            if (BlockContainsTestset(true0)) return null;

            int? r = TryOrChain(start, stmts);
            if (r.HasValue) return r;
            return TryAndChain(start, stmts);
        }

        private int? TryOrChain(int start, EqList<Stat> stmts)
        {
            var block = _cfg.Blocks[start];
            int bodyTarget = block.Successors[0];
            var chain = new List<int> { start };
            int current = block.Successors[1];

            while (true)
            {
                if (current >= _cfg.NumBlocks) return null;
                if (!IsConditionalBlock(_cfg.Blocks[current])) return null;
                if (BlockContainsTestset(current)) return null;

                var cur = _cfg.Blocks[current];
                int curFalse = cur.Successors[0];
                int curTrue = cur.Successors[1];

                if (curFalse == bodyTarget)
                {
                    chain.Add(current);
                    current = curTrue;
                }
                else if (curTrue == bodyTarget)
                {
                    chain.Add(current);
                    return EmitOrChain(chain, bodyTarget, curFalse, stmts);
                }
                else
                {
                    return null;
                }
            }
        }

        private int EmitOrChain(List<int> chain, int bodyTarget, int continuation, EqList<Stat> stmts)
        {
            var parts = new List<Expr>();
            for (int i = 0; i < chain.Count; i++)
            {
                int blockIdx = chain[i];
                var block = _cfg.Blocks[blockIdx];
                int? testPc = FindTestPc(block);
                if (testPc.HasValue && testPc.Value > block.Start)
                    LiftInstructions(block.Start, testPc.Value - 1, stmts);

                var cond = ExtractCondition(blockIdx) ?? new Expr.Bool(true);
                _visitedBlocks.Add(blockIdx);

                bool isLast = i == chain.Count - 1;
                parts.Add(isLast ? cond : NegateExpr(cond));
            }

            var combined = Reduce(parts, BinOpKind.Or);

            if (IsReturnBlock(bodyTarget))
            {
                var guardThen = LiftBlockRange(bodyTarget, bodyTarget + 1);
                stmts.Add(new Stat.If(combined, guardThen, new EqList<ElseIfClause>(), null));
                return continuation;
            }

            int? merge = BlockFlowsTo(bodyTarget, continuation) ? continuation : FindMergePoint(chain[0], bodyTarget, continuation);
            int thenEnd = merge ?? continuation;
            var thenBlock = LiftBlockRange(bodyTarget, thenEnd);

            EqList<Stat> elseBlock = null;
            if (merge.HasValue && continuation < merge.Value)
            {
                var eb = LiftBlockRange(continuation, merge.Value);
                if (eb.Count > 0) elseBlock = eb;
            }

            stmts.Add(new Stat.If(combined, thenBlock, new EqList<ElseIfClause>(), elseBlock));
            return merge ?? Math.Max(continuation, bodyTarget) + 1;
        }

        private int? TryAndChain(int start, EqList<Stat> stmts)
        {
            var block = _cfg.Blocks[start];
            int endTarget = block.Successors[0];
            var chain = new List<int> { start };
            int current = block.Successors[1];

            while (true)
            {
                if (current >= _cfg.NumBlocks) break;                         // body is current
                if (!IsConditionalBlock(_cfg.Blocks[current])) break;         // non-conditional = body
                if (BlockContainsTestset(current)) return null;

                var cur = _cfg.Blocks[current];
                if (cur.Successors[0] == endTarget)
                {
                    chain.Add(current);
                    current = cur.Successors[1];
                }
                else
                {
                    return null;
                }
            }

            if (chain.Count < 2) return null;
            int bodyTarget = current;

            var parts = new List<Expr>();
            foreach (int blockIdx in chain)
            {
                var b = _cfg.Blocks[blockIdx];
                int? testPc = FindTestPc(b);
                if (testPc.HasValue && testPc.Value > b.Start)
                    LiftInstructions(b.Start, testPc.Value - 1, stmts);
                parts.Add(ExtractCondition(blockIdx) ?? new Expr.Bool(true));
                _visitedBlocks.Add(blockIdx);
            }

            var combined = Reduce(parts, BinOpKind.And);

            int? merge = BlockFlowsTo(bodyTarget, endTarget) ? endTarget : FindMergePoint(chain[0], bodyTarget, endTarget);
            int thenEnd = merge ?? endTarget;
            var thenBlock = LiftBlockRange(bodyTarget, thenEnd);

            EqList<Stat> elseBlock = null;
            if (merge.HasValue && endTarget < merge.Value)
            {
                var eb = LiftBlockRange(endTarget, merge.Value);
                if (eb.Count > 0) elseBlock = eb;
            }

            stmts.Add(new Stat.If(combined, thenBlock, new EqList<ElseIfClause>(), elseBlock));
            return merge ?? Math.Max(endTarget, bodyTarget) + 1;
        }

        private static Expr Reduce(List<Expr> parts, BinOpKind op)
        {
            if (parts.Count == 0) return new Expr.Bool(true);
            var acc = parts[0];
            for (int i = 1; i < parts.Count; i++) acc = new Expr.BinOp(op, acc, parts[i]);
            return acc;
        }
    }
}

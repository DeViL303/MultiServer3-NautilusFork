// Ported from luadec-rust (https://github.com/ItsLucas/luadecng-rs), MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LuaDecompiler.Lua51
{
    public sealed partial class Lifter
    {
        /// <summary>Flush the pending table construction for a specific register.</summary>
        private void FlushPendingTable(uint reg)
        {
            if (_pendingTables.TryGetValue(reg, out var fields))
            {
                _pendingTables.Remove(reg);
                SetReg(reg, new Expr.Table(fields));
            }
        }

        /// <summary>Check if a block consists of only a RETURN (or RETURN with values).</summary>
        private bool IsReturnBlock(int blockIdx)
        {
            if (blockIdx >= _cfg.NumBlocks) return false;
            var block = _cfg.Blocks[blockIdx];
            var last = _cfg.Instructions[block.End];
            return (last.Op == OpCode.Return || last.Op == OpCode.TailCall) && block.Successors.Count == 0;
        }

        private static bool IsTestOp(OpCode op) =>
            op == OpCode.Eq || op == OpCode.Lt || op == OpCode.Le || op == OpCode.Test || op == OpCode.TestSet;

        /// <summary>Find the PC of the test instruction in a conditional block.</summary>
        private int? FindTestPc(BasicBlock block)
        {
            for (int pc = block.Start; pc <= block.End; pc++)
                if (IsTestOp(_cfg.Instructions[pc].Op)) return pc;
            return null;
        }

        private NaturalLoop FindLoopAt(int blockIdx)
        {
            if (_activeLoopHeaders.Contains(blockIdx)) return null;
            return _loops.FirstOrDefault(l => l.Header == blockIdx);
        }

        private int? CurrentLoopExit() => _activeLoopExits.Count > 0 ? _activeLoopExits[_activeLoopExits.Count - 1] : (int?)null;

        private int? FindForprepBlock(int header)
        {
            foreach (int pred in _cfg.Blocks[header].Predecessors)
            {
                if (_cfg.Instructions[_cfg.Blocks[pred].End].Op == OpCode.ForPrep) return pred;
            }
            if (header > 0 && _cfg.Instructions[_cfg.Blocks[header - 1].End].Op == OpCode.ForPrep)
                return header - 1;
            return null;
        }

        private int MaxLoopBlock(NaturalLoop lp) => lp.Body.Count > 0 ? lp.Body.Max() : lp.Header;

        private bool IsConditionalBlock(BasicBlock block) =>
            block.Successors.Count == 2
            && _cfg.Edges.Any(e => e.From == block.Id && (e.Kind == EdgeKind.ConditionalTrue || e.Kind == EdgeKind.ConditionalFalse));

        private bool BlockContainsTestset(int blockIdx)
        {
            if (blockIdx >= _cfg.NumBlocks) return false;
            var block = _cfg.Blocks[blockIdx];
            for (int pc = block.Start; pc <= block.End; pc++)
                if (_cfg.Instructions[pc].Op == OpCode.TestSet) return true;
            return false;
        }

        private bool BlockFlowsTo(int fromBlock, int targetBlock)
        {
            if (fromBlock >= _cfg.NumBlocks || targetBlock >= _cfg.NumBlocks) return false;
            return _cfg.Blocks[fromBlock].Successors.All(s => s == targetBlock);
        }

        private HashSet<uint> FindAccumulatorRegs()
        {
            var regs = new HashSet<uint>();
            foreach (var inst in _cfg.Instructions)
            {
                switch (inst.Op)
                {
                    case OpCode.Add:
                    case OpCode.Sub:
                    case OpCode.Mul:
                    case OpCode.Div:
                    case OpCode.Mod:
                    case OpCode.Pow:
                        {
                            bool usesTarget = (!Instruction.IsK(inst.B) && inst.A == inst.B)
                                || (!Instruction.IsK(inst.C) && inst.A == inst.C);
                            if (usesTarget) regs.Add(inst.A);
                            break;
                        }
                    case OpCode.Concat:
                        if (inst.A >= inst.B && inst.A <= inst.C) regs.Add(inst.A);
                        break;
                }
            }
            return regs;
        }

        private Expr ExtractCondition(int blockIdx)
        {
            var block = _cfg.Blocks[blockIdx];
            for (int pc = block.Start; pc <= block.End; pc++)
            {
                var inst = _cfg.Instructions[pc];
                switch (inst.Op)
                {
                    case OpCode.Eq:
                        return new Expr.BinOp(inst.A == 0 ? BinOpKind.Eq : BinOpKind.Ne, RkExpr(inst.B), RkExpr(inst.C));
                    case OpCode.Lt:
                        return new Expr.BinOp(inst.A == 0 ? BinOpKind.Lt : BinOpKind.Ge, RkExpr(inst.B), RkExpr(inst.C));
                    case OpCode.Le:
                        return new Expr.BinOp(inst.A == 0 ? BinOpKind.Le : BinOpKind.Gt, RkExpr(inst.B), RkExpr(inst.C));
                    case OpCode.Test:
                        {
                            var expr = RegExpr(inst.A);
                            return inst.C == 0 ? expr : new Expr.UnOp(UnOpKind.Not, expr);
                        }
                    case OpCode.TestSet:
                        {
                            var expr = RegExpr(inst.B);
                            return inst.C == 0 ? expr : new Expr.UnOp(UnOpKind.Not, expr);
                        }
                }
            }
            return null;
        }

        private int? FindMergePoint(int condBlock, int trueBlock, int falseBlock)
        {
            if (falseBlock < _cfg.NumBlocks)
            {
                var falsePreds = _cfg.Blocks[falseBlock].Predecessors;
                if (falsePreds.Count >= 3
                    && falsePreds.Contains(condBlock)
                    && falsePreds.Any(p => p != condBlock && p >= trueBlock && p < falseBlock))
                {
                    return falseBlock;
                }
            }

            int maxBranch = Math.Max(trueBlock, falseBlock);
            for (int b = maxBranch + 1; b < _cfg.NumBlocks; b++)
            {
                var preds = _cfg.Blocks[b].Predecessors;
                if (preds.Count >= 2) return b;
                if (!preds.All(p => p >= trueBlock && p <= maxBranch) && preds.Any(p => p >= trueBlock))
                    return b;
            }

            if (falseBlock > trueBlock && falseBlock > condBlock) return falseBlock;
            return null;
        }

        // ---- free functions (util.rs) ----

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>Decode bytes as strict UTF-8 (like Rust's str::from_utf8).</summary>
        internal static bool TryUtf8(byte[] bytes, out string text)
        {
            try
            {
                text = StrictUtf8.GetString(bytes);
                return true;
            }
            catch (DecoderFallbackException)
            {
                text = null;
                return false;
            }
        }

        /// <summary>Build a field access or index expression.</summary>
        internal static Expr MakeIndex(Expr table, Expr key)
        {
            if (key is Expr.StringLit s && TryUtf8(s.Value.Bytes, out var name) && IsIdentifier(name))
                return new Expr.Field(table, name);
            return new Expr.Index(table, key);
        }

        private static bool IsAsciiAlpha(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        private static bool IsAsciiAlnum(char c) => IsAsciiAlpha(c) || (c >= '0' && c <= '9');

        /// <summary>Check if a string is a valid Lua identifier.</summary>
        internal static bool IsIdentifier(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (!IsAsciiAlpha(s[0]) && s[0] != '_') return false;
            for (int i = 1; i < s.Length; i++)
                if (!IsAsciiAlnum(s[i]) && s[i] != '_') return false;
            return !IsLuaKeyword(s);
        }

        private static readonly HashSet<string> Keywords = new HashSet<string>
        {
            "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "if", "in",
            "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while",
        };

        internal static bool IsLuaKeyword(string s) => Keywords.Contains(s);

        /// <summary>Negate an expression (for inverting conditions).</summary>
        internal static Expr NegateExpr(Expr expr)
        {
            switch (expr)
            {
                case Expr.UnOp u when u.Op == UnOpKind.Not: return u.Operand;
                case Expr.BinOp b when b.Op == BinOpKind.Eq: return new Expr.BinOp(BinOpKind.Ne, b.Lhs, b.Rhs);
                case Expr.BinOp b when b.Op == BinOpKind.Ne: return new Expr.BinOp(BinOpKind.Eq, b.Lhs, b.Rhs);
                case Expr.BinOp b when b.Op == BinOpKind.Lt: return new Expr.BinOp(BinOpKind.Ge, b.Lhs, b.Rhs);
                case Expr.BinOp b when b.Op == BinOpKind.Ge: return new Expr.BinOp(BinOpKind.Lt, b.Lhs, b.Rhs);
                case Expr.BinOp b when b.Op == BinOpKind.Le: return new Expr.BinOp(BinOpKind.Gt, b.Lhs, b.Rhs);
                case Expr.BinOp b when b.Op == BinOpKind.Gt: return new Expr.BinOp(BinOpKind.Le, b.Lhs, b.Rhs);
                case Expr.Bool v: return new Expr.Bool(!v.Value);
                default: return new Expr.UnOp(UnOpKind.Not, expr);
            }
        }

        /// <summary>Rust `f64 as i64` (saturating, NaN -> 0).</summary>
        internal static long SaturatingToLong(double v)
        {
            if (double.IsNaN(v)) return 0;
            if (v >= 9223372036854775807.0) return long.MaxValue;
            if (v <= -9223372036854775808.0) return long.MinValue;
            return (long)v;
        }

        /// <summary>Rust `f64::fract() == 0.0`.</summary>
        internal static bool HasNoFraction(double v) => !double.IsNaN(v) && !double.IsInfinity(v) && v - Math.Truncate(v) == 0.0;
    }
}

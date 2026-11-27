// Ported from luadec-rust (https://github.com/ItsLucas/luadecng-rs), MIT License.
using System.Collections.Generic;
using System.Linq;

namespace LuaDecompiler.Lua51
{
    internal static class Normalizer
    {
        public static EqList<Stat> NormalizeBlock(IReadOnlyList<Stat> block)
        {
            var output = new EqList<Stat>();
            foreach (var stat in block) output.AddRange(NormalizeStat(stat));
            var cached = Cache.CacheSharedNegatedGetterCalls(output);
            return CollapseNestedTableAssignments(cached);
        }

        // ---- collapse `t = {}; t.a = {...}` into one constructor ----

        private static EqList<Stat> CollapseNestedTableAssignments(EqList<Stat> block)
        {
            var output = new EqList<Stat>();
            int index = 0;
            while (index < block.Count)
            {
                if (!ExtractTableAssignmentSeed(block[index], out var seedTarget, out var seedLocal, out var baseExpr, out var baseFields))
                {
                    output.Add(block[index]);
                    index++;
                    continue;
                }

                int next = index + 1;
                while (next < block.Count)
                {
                    if (!ExtractNestedTableAssignment(block[next], baseExpr, out var fieldName, out var value))
                        break;
                    baseFields.Add(new TableField.NameField(fieldName, value));
                    next++;
                }

                if (next == index + 1)
                {
                    output.Add(block[index]);
                    index++;
                    continue;
                }

                output.Add(seedLocal != null
                    ? new Stat.LocalAssign(new EqList<string> { seedLocal }, new EqList<Expr> { new Expr.Table(baseFields) })
                    : (Stat)new Stat.Assign(new EqList<Expr> { seedTarget }, new EqList<Expr> { new Expr.Table(baseFields) }));
                index = next;
            }
            return output;
        }

        private static bool ExtractTableAssignmentSeed(Stat stat, out Expr assignTarget, out string localName, out Expr baseExpr, out EqList<TableField> fields)
        {
            assignTarget = null; localName = null; baseExpr = null; fields = null;
            switch (stat)
            {
                case Stat.Assign a when a.Targets.Count == 1 && a.Values.Count == 1:
                    if (!(a.Values[0] is Expr.Table t)) return false;
                    assignTarget = a.Targets[0];
                    baseExpr = a.Targets[0];
                    fields = new EqList<TableField>(t.Fields);
                    return true;
                case Stat.LocalAssign l when l.Names.Count == 1 && l.Exprs.Count == 1:
                    if (!(l.Exprs[0] is Expr.Table lt)) return false;
                    localName = l.Names[0];
                    baseExpr = new Expr.Name(localName);
                    fields = new EqList<TableField>(lt.Fields);
                    return true;
                default:
                    return false;
            }
        }

        private static bool ExtractNestedTableAssignment(Stat stat, Expr baseExpr, out string fieldName, out Expr value)
        {
            fieldName = null; value = null;
            if (!(stat is Stat.Assign a) || a.Targets.Count != 1 || a.Values.Count != 1) return false;
            if (!(a.Values[0] is Expr.Table)) return false;

            switch (a.Targets[0])
            {
                case Expr.Field f when Equals(f.Target, baseExpr):
                    fieldName = f.FieldName;
                    break;
                case Expr.Index ix when Equals(ix.Target, baseExpr):
                    if (!(ix.Key is Expr.StringLit s) || !Lifter.TryUtf8(s.Value.Bytes, out var name) || !Lifter.IsIdentifier(name))
                        return false;
                    fieldName = name;
                    break;
                default:
                    return false;
            }
            value = a.Values[0];
            return true;
        }

        // ---- per-statement normalisation ----

        private static EqList<Expr> SimplifyAll(IEnumerable<Expr> exprs) => exprs.Select(SimplifyExpr).ToEqList();

        private static EqList<Stat> NormalizeStat(Stat stat)
        {
            switch (stat)
            {
                case Stat.LocalAssign la:
                    return new EqList<Stat> { new Stat.LocalAssign(new EqList<string>(la.Names), SimplifyAll(la.Exprs)) };
                case Stat.Assign a:
                    return new EqList<Stat> { new Stat.Assign(SimplifyAll(a.Targets), SimplifyAll(a.Values)) };
                case Stat.Call c:
                    return new EqList<Stat> { new Stat.Call(SimplifyCall(c.Invocation)) };
                case Stat.DoBlock d:
                    return new EqList<Stat> { new Stat.DoBlock(NormalizeBlock(d.Body)) };
                case Stat.While w:
                    return new EqList<Stat> { new Stat.While(SimplifyExpr(w.Cond), NormalizeBlock(w.Body)) };
                case Stat.Repeat r:
                    return new EqList<Stat> { new Stat.Repeat(NormalizeBlock(r.Body), SimplifyExpr(r.Cond)) };
                case Stat.If s:
                    return NormalizeIf(s.Cond, s.ThenBlock, s.ElseifClauses, s.ElseBlock);
                case Stat.NumericFor nf:
                    return new EqList<Stat> { new Stat.NumericFor(nf.Var, SimplifyExpr(nf.Start), SimplifyExpr(nf.Limit),
                        nf.Step == null ? null : SimplifyExpr(nf.Step), NormalizeBlock(nf.Body)) };
                case Stat.GenericFor gf:
                    return new EqList<Stat> { new Stat.GenericFor(new EqList<string>(gf.Names), SimplifyAll(gf.Iterators), NormalizeBlock(gf.Body)) };
                case Stat.Return ret:
                    return new EqList<Stat> { new Stat.Return(SimplifyAll(ret.Values)) };
                default:
                    return new EqList<Stat> { stat }; // Break, Comment
            }
        }

        private static EqList<Stat> NormalizeIf(Expr condIn, IReadOnlyList<Stat> thenIn, IReadOnlyList<ElseIfClause> elseifsIn, IReadOnlyList<Stat> elseIn)
        {
            var cond = SimplifyExpr(condIn);
            var thenBlock = NormalizeBlock(thenIn);
            var elseifClauses = elseifsIn.Select(c => new ElseIfClause(SimplifyExpr(c.Cond), NormalizeBlock(c.Block))).ToEqList();
            var elseBlock = elseIn == null ? null : NormalizeBlock(elseIn);

            // Flatten `else if ... end` chains into elseif clauses
            while (elseBlock != null && elseBlock.Count == 1 && elseBlock[0] is Stat.If inner)
            {
                elseifClauses.Add(new ElseIfClause(SimplifyExpr(inner.Cond), inner.ThenBlock));
                elseifClauses.AddRange(inner.ElseifClauses);
                elseBlock = inner.ElseBlock;
            }

            // `if c then else X end` -> `if not c then X end`
            if (thenBlock.Count == 0 && elseifClauses.Count == 0 && elseBlock != null && elseBlock.Count > 0)
                return new EqList<Stat> { new Stat.If(NegateCondition(cond), elseBlock, new EqList<ElseIfClause>(), null) };

            // `if a then if b then X end end` -> `if a and b then X end`
            if (elseifClauses.Count == 0 && elseBlock == null && thenBlock.Count == 1 && thenBlock[0] is Stat.If inner1)
            {
                int mergedTerms = CountAndTerms(cond) + CountAndTerms(inner1.Cond);
                if (inner1.ElseifClauses.Count == 0 && inner1.ElseBlock == null && inner1.ThenBlock.Count > 0 && mergedTerms <= 3)
                {
                    return new EqList<Stat> { new Stat.If(SimplifyExpr(new Expr.BinOp(BinOpKind.And, cond, inner1.Cond)),
                        inner1.ThenBlock, new EqList<ElseIfClause>(), null) };
                }
            }

            // Leading guard in a longer then-block
            if (elseifClauses.Count == 0 && elseBlock == null && thenBlock.Count > 1 && thenBlock[0] is Stat.If inner2)
            {
                int mergedTerms = CountAndTerms(cond) + CountAndTerms(inner2.Cond);
                if (inner2.ElseifClauses.Count == 0 && inner2.ElseBlock == null && BlockEndsWithTerminator(inner2.ThenBlock) && mergedTerms <= 3)
                {
                    var result = new EqList<Stat> { new Stat.If(SimplifyExpr(new Expr.BinOp(BinOpKind.And, cond, inner2.Cond)),
                        inner2.ThenBlock, new EqList<ElseIfClause>(), null) };
                    result.AddRange(thenBlock.Skip(1));
                    return result;
                }
            }

            // All branches identical -> one `or` condition
            if (elseBlock == null && thenBlock.Count > 0 && elseifClauses.Count > 0 && elseifClauses.All(c => Equals(c.Block, thenBlock)))
            {
                Expr merged = cond;
                foreach (var c in elseifClauses)
                    merged = SimplifyExpr(new Expr.BinOp(BinOpKind.Or, merged, c.Cond));
                return new EqList<Stat> { new Stat.If(merged, thenBlock, new EqList<ElseIfClause>(), null) };
            }

            if (elseifClauses.Count == 0 && elseBlock == null)
            {
                var rewritten = Cache.CacheRepeatedTrailingGetterCall(cond, thenBlock);
                if (rewritten != null) return rewritten;
            }

            SplitTrailingTerminatorTail(thenBlock, out var trimmedThen, out var trailingThen);
            if (trailingThen.Count == 0)
                return new EqList<Stat> { new Stat.If(cond, thenBlock, elseifClauses, elseBlock) };

            if (elseifClauses.Count > 0 || elseBlock != null)
                return new EqList<Stat> { new Stat.If(cond, trimmedThen, elseifClauses, elseBlock) };

            var output = new EqList<Stat> { new Stat.If(cond, trimmedThen, elseifClauses, elseBlock) };
            output.AddRange(trailingThen);
            return output;
        }

        private static void SplitTrailingTerminatorTail(EqList<Stat> block, out EqList<Stat> prefix, out EqList<Stat> tail)
        {
            for (int i = 0; i < block.Count; i++)
            {
                if (IsTerminator(block[i]))
                {
                    prefix = block.Take(i + 1).ToEqList();
                    tail = block.Skip(i + 1).ToEqList();
                    return;
                }
            }
            prefix = new EqList<Stat>(block);
            tail = new EqList<Stat>();
        }

        private static bool BlockEndsWithTerminator(IReadOnlyList<Stat> block) => block.Count > 0 && IsTerminator(block[block.Count - 1]);

        private static bool IsTerminator(Stat stat) => stat is Stat.Return || stat is Stat.Break;

        private static int CountAndTerms(Expr expr) =>
            expr is Expr.BinOp b && b.Op == BinOpKind.And ? CountAndTerms(b.Lhs) + CountAndTerms(b.Rhs) : 1;

        public static Expr SimplifyExpr(Expr expr)
        {
            switch (expr)
            {
                case Expr.Index ix: return new Expr.Index(SimplifyExpr(ix.Target), SimplifyExpr(ix.Key));
                case Expr.Field f: return new Expr.Field(SimplifyExpr(f.Target), f.FieldName);
                case Expr.MethodCall mc: return new Expr.MethodCall(SimplifyCall(mc.Invocation));
                case Expr.FuncCall fc: return new Expr.FuncCall(SimplifyCall(fc.Invocation));
                case Expr.BinOp b:
                    {
                        var lhs = SimplifyExpr(b.Lhs);
                        var rhs = SimplifyExpr(b.Rhs);
                        if (lhs is Expr.Bool lb)
                        {
                            if (b.Op == BinOpKind.And) return lb.Value ? rhs : new Expr.Bool(false);
                            if (b.Op == BinOpKind.Or) return lb.Value ? new Expr.Bool(true) : rhs;
                        }
                        return new Expr.BinOp(b.Op, lhs, rhs);
                    }
                case Expr.UnOp u when u.Op == UnOpKind.Not:
                    {
                        var operand = SimplifyExpr(u.Operand);
                        if (operand is Expr.Bool ob) return new Expr.Bool(!ob.Value);
                        // Port fix: the original turned `not not x` into `not x`, which flips its meaning.
                        return new Expr.UnOp(UnOpKind.Not, operand);
                    }
                case Expr.UnOp u2: return new Expr.UnOp(u2.Op, SimplifyExpr(u2.Operand));
                case Expr.FunctionDef fd:
                    return new Expr.FunctionDef(new Function(new EqList<string>(fd.Func.Params), fd.Func.IsVararg, NormalizeBlock(fd.Func.Body)));
                case Expr.Table t:
                    return new Expr.Table(t.Fields.Select(field =>
                    {
                        switch (field)
                        {
                            case TableField.IndexField ix: return (TableField)new TableField.IndexField(SimplifyExpr(ix.Key), SimplifyExpr(ix.Val));
                            case TableField.NameField nf: return new TableField.NameField(nf.FieldName, SimplifyExpr(nf.Val));
                            case TableField.Value v: return new TableField.Value(SimplifyExpr(v.Val));
                            default: return field;
                        }
                    }).ToEqList());
                default:
                    return expr;
            }
        }

        public static CallExpr SimplifyCall(CallExpr call) => new CallExpr(SimplifyExpr(call.Func), SimplifyAll(call.Args));

        private static Expr NegateCondition(Expr expr)
        {
            switch (expr)
            {
                case Expr.Bool b: return new Expr.Bool(!b.Value);
                case Expr.UnOp u when u.Op == UnOpKind.Not: return SimplifyExpr(u.Operand);
                default: return new Expr.UnOp(UnOpKind.Not, expr);
            }
        }
    }
}

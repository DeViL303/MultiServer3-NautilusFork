// Ported from luadec-rust (https://github.com/ItsLucas/luadecng-rs), MIT License.
// Caches repeated getter calls (GetBuff/GetBuffByOwner) into locals. These names come from the
// game the original was written for; on other scripts this pass simply never matches.
using System.Collections.Generic;
using System.Linq;

namespace LuaDecompiler.Lua51
{
    internal static class Cache
    {
        // ---- repeated trailing getter caching ----

        public static EqList<Stat> CacheRepeatedTrailingGetterCall(Expr cond, EqList<Stat> thenBlock)
        {
            var terms = new List<Expr>();
            CollectAndTerms(cond, terms);
            if (terms.Count == 0) return null;
            var lastTerm = terms[terms.Count - 1];
            if (!(lastTerm is Expr.FuncCall fc) || !IsCacheableGetterCall(fc.Invocation)) return null;
            var callExpr = lastTerm;

            if (!BlockContainsExpr(thenBlock, callExpr)) return null;

            string cacheName = UniquifyCacheName(SuggestCachedCallName(callExpr), cond, thenBlock);
            var outerTerms = terms.Take(terms.Count - 1).ToList();
            var rewrittenThen = ReplaceExprsInBlock(thenBlock, callExpr, new Expr.Name(cacheName));
            var innerIf = new Stat.If(new Expr.Name(cacheName), rewrittenThen, new EqList<ElseIfClause>(), null);
            var cachedLocal = new Stat.LocalAssign(new EqList<string> { cacheName }, new EqList<Expr> { callExpr });

            if (outerTerms.Count == 0)
                return new EqList<Stat> { new Stat.DoBlock(new EqList<Stat> { cachedLocal, innerIf }) };

            return new EqList<Stat>
            {
                new Stat.If(CombineTerms(outerTerms, BinOpKind.And), new EqList<Stat> { cachedLocal, innerIf }, new EqList<ElseIfClause>(), null),
            };
        }

        // ---- shared negated getter caching ----

        public static EqList<Stat> CacheSharedNegatedGetterCalls(EqList<Stat> block)
        {
            var output = new EqList<Stat>();
            int index = 0;
            while (index < block.Count)
            {
                var group = CollectNegatedGetterGroup(block, index, out var sharedCall);
                if (group == null || group.Count < 2)
                {
                    output.Add(block[index]);
                    index++;
                    continue;
                }

                int groupLen = group.Count;
                string cacheName = UniquifyCacheName(SuggestCachedCallName(sharedCall), new Expr.Bool(false), block);
                var replacement = new Expr.UnOp(UnOpKind.Not, new Expr.Name(cacheName));
                var prefixes = group.Where(g => g.prefix != null).Select(g => g.prefix).ToList();
                var rewrittenIfs = group.Select(g => RewriteGroupedIf(g.prefix, g.stat, replacement)).ToList();

                var outerBody = new EqList<Stat> { new Stat.LocalAssign(new EqList<string> { cacheName }, new EqList<Expr> { sharedCall }) };
                outerBody.AddRange(rewrittenIfs);

                if (prefixes.Count == groupLen && prefixes.Count > 0)
                    output.Add(new Stat.If(CombineTerms(prefixes, BinOpKind.Or), outerBody, new EqList<ElseIfClause>(), null));
                else
                    output.Add(new Stat.DoBlock(outerBody));

                index += groupLen;
            }
            return output;
        }

        private static List<(Expr prefix, Stat stat)> CollectNegatedGetterGroup(EqList<Stat> block, int start, out Expr sharedCall)
        {
            sharedCall = null;
            if (start >= block.Count || !ExtractNegatedTrailingGetterFromIf(block[start], out sharedCall, out var firstPrefix))
                return null;
            var group = new List<(Expr, Stat)> { (firstPrefix, block[start]) };
            int index = start + 1;
            while (index < block.Count && ExtractNegatedTrailingGetterFromIf(block[index], out var otherCall, out var otherPrefix))
            {
                if (!Equals(otherCall, sharedCall)) break;
                group.Add((otherPrefix, block[index]));
                index++;
            }
            return group;
        }

        private static bool ExtractNegatedTrailingGetterFromIf(Stat stat, out Expr call, out Expr prefix)
        {
            call = null; prefix = null;
            if (!(stat is Stat.If s) || s.ElseifClauses.Count > 0 || s.ElseBlock != null) return false;

            switch (s.Cond)
            {
                case Expr.BinOp b when b.Op == BinOpKind.And:
                    if (b.Rhs is Expr.UnOp u && u.Op == UnOpKind.Not && u.Operand is Expr.FuncCall fc && IsCacheableGetterCall(fc.Invocation))
                    {
                        call = u.Operand;
                        prefix = b.Lhs;
                        return true;
                    }
                    return false;
                case Expr.UnOp u2 when u2.Op == UnOpKind.Not:
                    if (u2.Operand is Expr.FuncCall fc2 && IsCacheableGetterCall(fc2.Invocation))
                    {
                        call = u2.Operand;
                        return true;
                    }
                    return false;
                default:
                    return false;
            }
        }

        private static Stat RewriteGroupedIf(Expr prefix, Stat stat, Expr replacement)
        {
            var s = (Stat.If)stat;
            var cond = prefix != null ? Normalizer.SimplifyExpr(new Expr.BinOp(BinOpKind.And, prefix, replacement)) : replacement;
            return new Stat.If(cond, s.ThenBlock, new EqList<ElseIfClause>(), null);
        }

        // ---- term helpers ----

        private static Expr CombineTerms(List<Expr> terms, BinOpKind op)
        {
            if (terms.Count == 0) return new Expr.Bool(true);
            var acc = terms[0];
            for (int i = 1; i < terms.Count; i++)
                acc = Normalizer.SimplifyExpr(new Expr.BinOp(op, acc, terms[i]));
            return acc;
        }

        private static void CollectAndTerms(Expr expr, List<Expr> output)
        {
            if (expr is Expr.BinOp b && b.Op == BinOpKind.And)
            {
                CollectAndTerms(b.Lhs, output);
                CollectAndTerms(b.Rhs, output);
            }
            else
            {
                output.Add(expr);
            }
        }

        // ---- cacheable getter detection ----

        private static bool IsCacheableGetterCall(CallExpr call) =>
            call.Func is Expr.Field f && (f.FieldName == "GetBuffByOwner" || f.FieldName == "GetBuff");

        private static string SuggestCachedCallName(Expr expr)
        {
            if (!(expr is Expr.FuncCall fc) || !(fc.Invocation.Func is Expr.Field f))
                return "cached_value";

            if ((f.FieldName == "GetBuffByOwner" || f.FieldName == "GetBuff") && fc.Invocation.Args.Count > 0 && fc.Invocation.Args[0] is Expr.Number n)
            {
                if (n.Value is NumLit.Int i) return $"buff_{i.V}";
                if (n.Value is NumLit.Float fl && Lifter.HasNoFraction(fl.V)) return $"buff_{Lifter.SaturatingToLong(fl.V)}";
            }
            return Lifter.CamelToSnake(f.FieldName);
        }

        private static string UniquifyCacheName(string baseName, Expr cond, IReadOnlyList<Stat> block)
        {
            if (!ExprContainsName(cond, baseName) && !BlockContainsName(block, baseName)) return baseName;
            for (int suffix = 1; ; suffix++)
            {
                string candidate = $"{baseName}_{suffix}";
                if (!ExprContainsName(cond, candidate) && !BlockContainsName(block, candidate)) return candidate;
            }
        }

        // ---- contains-name checks ----

        private static bool BlockContainsName(IReadOnlyList<Stat> block, string name) => block.Any(s => StatContainsName(s, name));

        private static bool StatContainsName(Stat stat, string name)
        {
            switch (stat)
            {
                case Stat.LocalAssign la: return la.Names.Contains(name) || la.Exprs.Any(e => ExprContainsName(e, name));
                case Stat.Assign a: return a.Targets.Any(e => ExprContainsName(e, name)) || a.Values.Any(e => ExprContainsName(e, name));
                case Stat.Call c: return CallContainsName(c.Invocation, name);
                case Stat.DoBlock d: return BlockContainsName(d.Body, name);
                case Stat.While w: return ExprContainsName(w.Cond, name) || BlockContainsName(w.Body, name);
                case Stat.Repeat r: return BlockContainsName(r.Body, name) || ExprContainsName(r.Cond, name);
                case Stat.If s:
                    return ExprContainsName(s.Cond, name)
                        || BlockContainsName(s.ThenBlock, name)
                        || s.ElseifClauses.Any(c => ExprContainsName(c.Cond, name) || BlockContainsName(c.Block, name))
                        || (s.ElseBlock != null && BlockContainsName(s.ElseBlock, name));
                case Stat.NumericFor nf:
                    return nf.Var == name || ExprContainsName(nf.Start, name) || ExprContainsName(nf.Limit, name)
                        || (nf.Step != null && ExprContainsName(nf.Step, name)) || BlockContainsName(nf.Body, name);
                case Stat.GenericFor gf:
                    return gf.Names.Contains(name) || gf.Iterators.Any(e => ExprContainsName(e, name)) || BlockContainsName(gf.Body, name);
                case Stat.Return ret: return ret.Values.Any(e => ExprContainsName(e, name));
                default: return false;
            }
        }

        private static bool ExprContainsName(Expr expr, string name)
        {
            switch (expr)
            {
                case Expr.Name n: return n.Id == name;
                case Expr.Index ix: return ExprContainsName(ix.Target, name) || ExprContainsName(ix.Key, name);
                case Expr.Field f: return ExprContainsName(f.Target, name);
                case Expr.MethodCall mc: return CallContainsName(mc.Invocation, name);
                case Expr.FuncCall fc: return CallContainsName(fc.Invocation, name);
                case Expr.BinOp b: return ExprContainsName(b.Lhs, name) || ExprContainsName(b.Rhs, name);
                case Expr.UnOp u: return ExprContainsName(u.Operand, name);
                case Expr.FunctionDef fd: return BlockContainsName(fd.Func.Body, name);
                case Expr.Table t: return t.Fields.Any(field => FieldExprs(field).Any(e => ExprContainsName(e, name)));
                default: return false;
            }
        }

        private static bool CallContainsName(CallExpr call, string name) =>
            ExprContainsName(call.Func, name) || call.Args.Any(a => ExprContainsName(a, name));

        private static IEnumerable<Expr> FieldExprs(TableField field)
        {
            switch (field)
            {
                case TableField.IndexField ix: yield return ix.Key; yield return ix.Val; break;
                case TableField.NameField nf: yield return nf.Val; break;
                case TableField.Value v: yield return v.Val; break;
            }
        }

        // ---- contains-expr checks ----

        private static bool BlockContainsExpr(IReadOnlyList<Stat> block, Expr target) => block.Any(s => StatContainsExpr(s, target));

        private static bool StatContainsExpr(Stat stat, Expr target)
        {
            switch (stat)
            {
                case Stat.LocalAssign la: return la.Exprs.Any(e => ExprContainsExpr(e, target));
                case Stat.Assign a: return a.Targets.Any(e => ExprContainsExpr(e, target)) || a.Values.Any(e => ExprContainsExpr(e, target));
                case Stat.Call c: return ExprContainsExpr(new Expr.FuncCall(c.Invocation), target);
                case Stat.DoBlock d: return BlockContainsExpr(d.Body, target);
                case Stat.While w: return ExprContainsExpr(w.Cond, target) || BlockContainsExpr(w.Body, target);
                case Stat.Repeat r: return BlockContainsExpr(r.Body, target) || ExprContainsExpr(r.Cond, target);
                case Stat.If s:
                    return ExprContainsExpr(s.Cond, target)
                        || BlockContainsExpr(s.ThenBlock, target)
                        || s.ElseifClauses.Any(c => ExprContainsExpr(c.Cond, target) || BlockContainsExpr(c.Block, target))
                        || (s.ElseBlock != null && BlockContainsExpr(s.ElseBlock, target));
                case Stat.NumericFor nf:
                    return ExprContainsExpr(nf.Start, target) || ExprContainsExpr(nf.Limit, target)
                        || (nf.Step != null && ExprContainsExpr(nf.Step, target)) || BlockContainsExpr(nf.Body, target);
                case Stat.GenericFor gf: return gf.Iterators.Any(e => ExprContainsExpr(e, target)) || BlockContainsExpr(gf.Body, target);
                case Stat.Return ret: return ret.Values.Any(e => ExprContainsExpr(e, target));
                default: return false;
            }
        }

        private static bool ExprContainsExpr(Expr expr, Expr target)
        {
            if (Equals(expr, target)) return true;
            switch (expr)
            {
                case Expr.Index ix: return ExprContainsExpr(ix.Target, target) || ExprContainsExpr(ix.Key, target);
                case Expr.Field f: return ExprContainsExpr(f.Target, target);
                case Expr.MethodCall mc: return ExprContainsExpr(mc.Invocation.Func, target) || mc.Invocation.Args.Any(a => ExprContainsExpr(a, target));
                case Expr.FuncCall fc: return ExprContainsExpr(fc.Invocation.Func, target) || fc.Invocation.Args.Any(a => ExprContainsExpr(a, target));
                case Expr.BinOp b: return ExprContainsExpr(b.Lhs, target) || ExprContainsExpr(b.Rhs, target);
                case Expr.UnOp u: return ExprContainsExpr(u.Operand, target);
                case Expr.FunctionDef fd: return BlockContainsExpr(fd.Func.Body, target);
                case Expr.Table t: return t.Fields.Any(field => FieldExprs(field).Any(e => ExprContainsExpr(e, target)));
                default: return false;
            }
        }

        // ---- replacement ----

        private static EqList<Stat> ReplaceExprsInBlock(IReadOnlyList<Stat> block, Expr target, Expr replacement) =>
            block.Select(s => ReplaceExprsInStat(s, target, replacement)).ToEqList();

        private static EqList<Expr> ReplaceAll(IEnumerable<Expr> exprs, Expr target, Expr replacement) =>
            exprs.Select(e => ReplaceExpr(e, target, replacement)).ToEqList();

        private static Stat ReplaceExprsInStat(Stat stat, Expr target, Expr replacement)
        {
            switch (stat)
            {
                case Stat.LocalAssign la: return new Stat.LocalAssign(new EqList<string>(la.Names), ReplaceAll(la.Exprs, target, replacement));
                case Stat.Assign a: return new Stat.Assign(ReplaceAll(a.Targets, target, replacement), ReplaceAll(a.Values, target, replacement));
                case Stat.Call c:
                    {
                        var replaced = ReplaceExpr(new Expr.FuncCall(c.Invocation), target, replacement);
                        return replaced is Expr.FuncCall updated
                            ? new Stat.Call(updated.Invocation)
                            : new Stat.Call(new CallExpr(replaced, new EqList<Expr>()));
                    }
                case Stat.DoBlock d: return new Stat.DoBlock(ReplaceExprsInBlock(d.Body, target, replacement));
                case Stat.While w: return new Stat.While(ReplaceExpr(w.Cond, target, replacement), ReplaceExprsInBlock(w.Body, target, replacement));
                case Stat.Repeat r: return new Stat.Repeat(ReplaceExprsInBlock(r.Body, target, replacement), ReplaceExpr(r.Cond, target, replacement));
                case Stat.If s:
                    return new Stat.If(
                        ReplaceExpr(s.Cond, target, replacement),
                        ReplaceExprsInBlock(s.ThenBlock, target, replacement),
                        s.ElseifClauses.Select(c => new ElseIfClause(ReplaceExpr(c.Cond, target, replacement), ReplaceExprsInBlock(c.Block, target, replacement))).ToEqList(),
                        s.ElseBlock == null ? null : ReplaceExprsInBlock(s.ElseBlock, target, replacement));
                case Stat.NumericFor nf:
                    return new Stat.NumericFor(nf.Var, ReplaceExpr(nf.Start, target, replacement), ReplaceExpr(nf.Limit, target, replacement),
                        nf.Step == null ? null : ReplaceExpr(nf.Step, target, replacement), ReplaceExprsInBlock(nf.Body, target, replacement));
                case Stat.GenericFor gf:
                    return new Stat.GenericFor(new EqList<string>(gf.Names), ReplaceAll(gf.Iterators, target, replacement), ReplaceExprsInBlock(gf.Body, target, replacement));
                case Stat.Return ret: return new Stat.Return(ReplaceAll(ret.Values, target, replacement));
                default: return stat;
            }
        }

        private static Expr ReplaceExpr(Expr expr, Expr target, Expr replacement)
        {
            if (Equals(expr, target)) return replacement;
            switch (expr)
            {
                case Expr.Index ix: return new Expr.Index(ReplaceExpr(ix.Target, target, replacement), ReplaceExpr(ix.Key, target, replacement));
                case Expr.Field f: return new Expr.Field(ReplaceExpr(f.Target, target, replacement), f.FieldName);
                case Expr.MethodCall mc: return new Expr.MethodCall(ReplaceCall(mc.Invocation, target, replacement));
                case Expr.FuncCall fc: return new Expr.FuncCall(ReplaceCall(fc.Invocation, target, replacement));
                case Expr.BinOp b: return new Expr.BinOp(b.Op, ReplaceExpr(b.Lhs, target, replacement), ReplaceExpr(b.Rhs, target, replacement));
                case Expr.UnOp u: return new Expr.UnOp(u.Op, ReplaceExpr(u.Operand, target, replacement));
                case Expr.FunctionDef fd:
                    return new Expr.FunctionDef(new Function(new EqList<string>(fd.Func.Params), fd.Func.IsVararg, ReplaceExprsInBlock(fd.Func.Body, target, replacement)));
                case Expr.Table t:
                    return new Expr.Table(t.Fields.Select(field =>
                    {
                        switch (field)
                        {
                            case TableField.IndexField ix2: return (TableField)new TableField.IndexField(ReplaceExpr(ix2.Key, target, replacement), ReplaceExpr(ix2.Val, target, replacement));
                            case TableField.NameField nf: return new TableField.NameField(nf.FieldName, ReplaceExpr(nf.Val, target, replacement));
                            case TableField.Value v: return new TableField.Value(ReplaceExpr(v.Val, target, replacement));
                            default: return field;
                        }
                    }).ToEqList());
                default: return expr;
            }
        }

        private static CallExpr ReplaceCall(CallExpr call, Expr target, Expr replacement) =>
            new CallExpr(ReplaceExpr(call.Func, target, replacement), ReplaceAll(call.Args, target, replacement));
    }
}

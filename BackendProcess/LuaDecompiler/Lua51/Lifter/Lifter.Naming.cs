// Ported from luadec-rust (https://github.com/ItsLucas/luadecng-rs), MIT License.
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LuaDecompiler.Lua51
{
    public sealed partial class Lifter
    {
        private static string NormalizeCallName(string name)
        {
            string snake = CamelToSnake(name);
            if (snake.StartsWith("get_", System.StringComparison.Ordinal)) return snake.Substring(4);
            if (snake.StartsWith("is_", System.StringComparison.Ordinal)) return snake.Substring(3);
            return snake;
        }

        internal static string CamelToSnake(string name)
        {
            var sb = new StringBuilder();
            for (int idx = 0; idx < name.Length; idx++)
            {
                char ch = name[idx];
                if (ch >= 'A' && ch <= 'Z')
                {
                    if (idx != 0) sb.Append('_');
                    sb.Append((char)(ch + 32));
                }
                else
                {
                    sb.Append(ch);
                }
            }
            return sb.ToString();
        }

        private static bool IsUpperIdent(string s) =>
            s.Length > 0 && s.All(c => c == '_' || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'));

        private string LocalName(uint reg, int pc)
        {
            // Debug info: the i-th local is treated as register i for its scope range.
            var locals = _chunk.Locals;
            if (reg < locals.Count)
            {
                var local = locals[(int)reg];
                if (local.StartPc <= (ulong)pc + 1 && (ulong)pc < local.EndPc
                    && local.Name.Length > 0 && !local.Name.StartsWith("(", System.StringComparison.Ordinal))
                {
                    return local.Name;
                }
            }
            // Fall back to matching by position in the locals array
            if (reg < locals.Count)
            {
                string name = locals[(int)reg].Name;
                if (name.Length > 0 && !name.StartsWith("(", System.StringComparison.Ordinal))
                    return name;
            }
            return MakeLocalName(reg);
        }

        private string MakeLocalName(uint reg)
        {
            var current = reg < _regs.Length ? _regs[reg] : null;
            return MakeLocalNameFromKnownExpr(reg, current);
        }

        private string MakeLocalNameForExpr(uint reg, Expr expr) => MakeLocalNameFromKnownExpr(reg, expr);

        private string MakeLocalNameFromKnownExpr(uint reg, Expr currentExpr)
        {
            if (reg < _numParams)
            {
                // Port fix: the original called LocalName here, which calls back into this method when the
                // parameter has no debug entry - endless recursion (an uncatchable stack overflow in .NET).
                if (_hasDebugInfo && reg < _chunk.Locals.Count)
                {
                    string dbg = _chunk.Locals[(int)reg].Name;
                    if (dbg.Length > 0 && !dbg.StartsWith("(", System.StringComparison.Ordinal))
                        return dbg;
                }
                return $"a{reg}";
            }

            string acc = InferAccumulatorName(reg, currentExpr);
            if (acc != null) return UniquifyLocalName(acc);

            if (_pendingTables.TryGetValue(reg, out var pending) && pending.Count > 0)
            {
                string n = InferTableLocalName(pending);
                if (n != null) return UniquifyLocalName(n);
            }

            switch (currentExpr)
            {
                case Expr.Table t:
                    {
                        string n = InferTableLocalName(t.Fields);
                        if (n != null) return UniquifyLocalName(n);
                        break;
                    }
                case Expr.FuncCall fc:
                    {
                        var call = fc.Invocation;
                        string n = InferCallLocalName(call);
                        if (n != null) return UniquifyLocalName(n);
                        if (call.Func is Expr.Global g)
                        {
                            string shortName = NormalizeCallName(g.GlobalName);
                            if (Encoding.UTF8.GetByteCount(shortName) <= 20)
                                return UniquifyLocalName(shortName);
                        }
                        if (call.Func is Expr.Field f)
                            return UniquifyLocalName(NormalizeCallName(f.FieldName));
                        break;
                    }
            }
            return $"l_{reg}";
        }

        private string InferAccumulatorName(uint reg, Expr expr)
        {
            if (!_accumulatorRegs.Contains(reg) || !IsReturnedReg(reg)) return null;
            return expr == null || expr is Expr.Number ? "result" : null;
        }

        private static string InferCallLocalName(CallExpr call)
        {
            string method;
            if (call.Func is Expr.Field f) method = f.FieldName;
            else if (call.Func is Expr.Global g) method = g.GlobalName;
            else return null;

            long? firstIntArg = null;
            if (call.Args.Count > 0 && call.Args[0] is Expr.Number num)
            {
                if (num.Value is NumLit.Int i) firstIntArg = i.V;
                else if (num.Value is NumLit.Float fl && HasNoFraction(fl.V)) firstIntArg = SaturatingToLong(fl.V);
            }

            switch (method)
            {
                case "IsHaveBuff": return firstIntArg.HasValue ? $"has_buff_{firstIntArg.Value}" : "has_buff";
                case "GetBuff":
                case "GetBuffByOwner": return firstIntArg.HasValue ? $"buff_{firstIntArg.Value}" : "buff";
                case "GetSkillLevel": return firstIntArg.HasValue ? $"skill_{firstIntArg.Value}" : null;
                case "GetEndTime": return "end_time";
                case "GetLogicFrameCount": return "logic_frame_count";
                default: return null;
            }
        }

        private bool IsReturnedReg(uint reg) =>
            _cfg.Instructions.Any(i => i.Op == OpCode.Return && i.A == reg && i.B == 2);

        private string UniquifyLocalName(string baseName)
        {
            if (!_localNames.Values.Contains(baseName)) return baseName;
            for (int suffix = 1; ; suffix++)
            {
                string candidate = $"{baseName}_{suffix}";
                if (!_localNames.Values.Contains(candidate)) return candidate;
            }
        }

        private static string InferTableLocalName(IReadOnlyList<TableField> fields)
        {
            if (fields.Count == 0) return null;

            if (fields.All(f => f is TableField.IndexField ix && ix.Val is Expr.Bool b && b.Value))
            {
                if (fields.Any(f => f is TableField.IndexField ix && ExprMentionsField(ix.Key, "ENUM")))
                    return "enum_lookup";
                return "lookup";
            }

            if (fields.All(f => f is TableField.NameField nf && nf.Val is Expr.Number))
            {
                var keys = fields.OfType<TableField.NameField>().Select(nf => nf.FieldName).ToList();
                if (keys.Any(k => k.Contains("NOT_"))) return "penalties";
                if (keys.All(IsUpperIdent)) return "modifiers";
            }
            return null;
        }

        private static bool ExprMentionsField(Expr expr, string fieldName)
        {
            switch (expr)
            {
                case Expr.Field f: return f.FieldName == fieldName || ExprMentionsField(f.Target, fieldName);
                case Expr.Index ix: return ExprMentionsField(ix.Target, fieldName) || ExprMentionsField(ix.Key, fieldName);
                case Expr.MethodCall mc: return CallMentionsField(mc.Invocation, fieldName);
                case Expr.FuncCall fc: return CallMentionsField(fc.Invocation, fieldName);
                case Expr.BinOp b: return ExprMentionsField(b.Lhs, fieldName) || ExprMentionsField(b.Rhs, fieldName);
                case Expr.UnOp u: return ExprMentionsField(u.Operand, fieldName);
                case Expr.Table t:
                    return t.Fields.Any(field =>
                    {
                        switch (field)
                        {
                            case TableField.IndexField ix: return ExprMentionsField(ix.Key, fieldName) || ExprMentionsField(ix.Val, fieldName);
                            case TableField.NameField nf: return ExprMentionsField(nf.Val, fieldName);
                            case TableField.Value v: return ExprMentionsField(v.Val, fieldName);
                            default: return false;
                        }
                    });
                default: return false;
            }
        }

        private static bool CallMentionsField(CallExpr call, string fieldName) =>
            ExprMentionsField(call.Func, fieldName) || call.Args.Any(a => ExprMentionsField(a, fieldName));
    }
}

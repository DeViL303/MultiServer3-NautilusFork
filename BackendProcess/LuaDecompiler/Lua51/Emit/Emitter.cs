// Ported from luadec-rust (https://github.com/ItsLucas/luadecng-rs), MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LuaDecompiler.Lua51
{
    public static class Emitter
    {
        /// <summary>Emit a Lua function body as source code.</summary>
        public static string EmitFunction(Function func)
        {
            var sb = new StringBuilder();
            var body = FoldElseifChain(Normalizer.NormalizeBlock(func.Body));
            EmitBlock(body, sb, 0);
            return sb.ToString();
        }

        /// <summary>Emit a complete chunk (wrapping the main function body).</summary>
        public static string EmitChunk(Function func)
        {
            var sb = new StringBuilder();
            var body = FoldElseifChain(Normalizer.NormalizeBlock(func.Body));
            EmitBlock(body, sb, 0);
            // Remove trailing blank lines
            while (sb.Length >= 2 && sb[sb.Length - 1] == '\n' && sb[sb.Length - 2] == '\n')
                sb.Length--;
            if (sb.Length == 0 || sb[sb.Length - 1] != '\n')
                sb.Append('\n');
            return sb.ToString();
        }

        // ---- elseif folding ----

        private static EqList<Stat> FoldElseifChain(EqList<Stat> block) => block.Select(FoldStat).ToEqList();

        private static Stat FoldStat(Stat stat)
        {
            switch (stat)
            {
                case Stat.If s:
                    {
                        var thenBlock = FoldElseifChain(s.ThenBlock);
                        var newElseifs = s.ElseifClauses.Select(c => new ElseIfClause(c.Cond, FoldElseifChain(c.Block))).ToEqList();
                        EqList<Stat> newElse = null;
                        if (s.ElseBlock != null)
                        {
                            var folded = FoldElseifChain(s.ElseBlock);
                            if (folded.Count == 1 && folded[0] is Stat.If inner)
                            {
                                newElseifs.Add(new ElseIfClause(inner.Cond, inner.ThenBlock));
                                newElseifs.AddRange(inner.ElseifClauses);
                                newElse = inner.ElseBlock;
                            }
                            else
                            {
                                newElse = folded;
                            }
                        }
                        return new Stat.If(s.Cond, thenBlock, newElseifs, newElse);
                    }
                case Stat.While w: return new Stat.While(w.Cond, FoldElseifChain(w.Body));
                case Stat.Repeat r: return new Stat.Repeat(FoldElseifChain(r.Body), r.Cond);
                case Stat.NumericFor nf: return new Stat.NumericFor(nf.Var, nf.Start, nf.Limit, nf.Step, FoldElseifChain(nf.Body));
                case Stat.GenericFor gf: return new Stat.GenericFor(gf.Names, gf.Iterators, FoldElseifChain(gf.Body));
                case Stat.DoBlock d: return new Stat.DoBlock(FoldElseifChain(d.Body));
                default: return stat;
            }
        }

        // ---- statements ----

        private static void EmitBlock(IReadOnlyList<Stat> block, StringBuilder sb, int indent)
        {
            for (int i = 0; i < block.Count; i++)
            {
                // Blank line around top-level function definitions
                if (indent == 0 && i > 0 && (IsFuncDef(block[i - 1]) || IsFuncDef(block[i])))
                    sb.Append('\n');
                EmitStat(block[i], sb, indent);
            }
        }

        private static bool IsFuncDef(Stat stat)
        {
            switch (stat)
            {
                case Stat.Assign a: return a.Values.Count == 1 && a.Values[0] is Expr.FunctionDef;
                case Stat.LocalAssign l: return l.Exprs.Count == 1 && l.Exprs[0] is Expr.FunctionDef;
                default: return false;
            }
        }

        private static string Pad(int indent) => new string(' ', indent * 2);

        private static string ParamList(Function func)
        {
            var p = string.Join(", ", func.Params);
            if (func.IsVararg)
                p = p.Length > 0 ? p + ", ..." : "...";
            return p;
        }

        private static void EmitStat(Stat stat, StringBuilder sb, int indent)
        {
            string pad = Pad(indent);
            switch (stat)
            {
                case Stat.LocalAssign la:
                    sb.Append(pad).Append("local ").Append(string.Join(", ", la.Names));
                    if (la.Exprs.Count > 0)
                    {
                        sb.Append(" = ");
                        EmitExprList(la.Exprs, sb);
                    }
                    sb.Append('\n');
                    break;

                case Stat.Assign a:
                    {
                        // Pretty-print `name = function(...) end` as `function name(...) end`
                        if (a.Targets.Count == 1 && a.Values.Count == 1 && a.Values[0] is Expr.FunctionDef fd)
                        {
                            string fname = null;
                            switch (a.Targets[0])
                            {
                                case Expr.Global g: fname = g.GlobalName; break;
                                case Expr.Name n: fname = n.Id; break;
                                case Expr.Field f:
                                    {
                                        var tmp = new StringBuilder();
                                        EmitExpr(f.Target, tmp, 10);
                                        fname = tmp.Append('.').Append(f.FieldName).ToString();
                                        break;
                                    }
                            }
                            if (fname != null)
                            {
                                sb.Append(pad).Append("function ").Append(fname).Append('(').Append(ParamList(fd.Func)).Append(")\n");
                                EmitBlock(fd.Func.Body, sb, indent + 1);
                                sb.Append(pad).Append("end\n");
                                return;
                            }
                        }
                        sb.Append(pad);
                        EmitExprList(a.Targets, sb);
                        sb.Append(" = ");
                        EmitExprList(a.Values, sb);
                        sb.Append('\n');
                        break;
                    }

                case Stat.Call c:
                    sb.Append(pad);
                    EmitCall(c.Invocation, sb);
                    sb.Append('\n');
                    break;

                case Stat.DoBlock d:
                    sb.Append(pad).Append("do\n");
                    EmitBlock(d.Body, sb, indent + 1);
                    sb.Append(pad).Append("end\n");
                    break;

                case Stat.While w:
                    sb.Append(pad).Append("while ");
                    EmitExpr(w.Cond, sb, 0);
                    sb.Append(" do\n");
                    EmitBlock(w.Body, sb, indent + 1);
                    sb.Append(pad).Append("end\n");
                    break;

                case Stat.Repeat r:
                    sb.Append(pad).Append("repeat\n");
                    EmitBlock(r.Body, sb, indent + 1);
                    sb.Append(pad).Append("until ");
                    EmitExpr(r.Cond, sb, 0);
                    sb.Append('\n');
                    break;

                case Stat.If s:
                    sb.Append(pad).Append("if ");
                    EmitExpr(s.Cond, sb, 0);
                    sb.Append(" then\n");
                    EmitBlock(s.ThenBlock, sb, indent + 1);
                    foreach (var ec in s.ElseifClauses)
                    {
                        sb.Append(pad).Append("elseif ");
                        EmitExpr(ec.Cond, sb, 0);
                        sb.Append(" then\n");
                        EmitBlock(ec.Block, sb, indent + 1);
                    }
                    if (s.ElseBlock != null)
                    {
                        sb.Append(pad).Append("else\n");
                        EmitBlock(s.ElseBlock, sb, indent + 1);
                    }
                    sb.Append(pad).Append("end\n");
                    break;

                case Stat.NumericFor nf:
                    sb.Append(pad).Append("for ").Append(nf.Var).Append(" = ");
                    EmitExpr(nf.Start, sb, 0);
                    sb.Append(", ");
                    EmitExpr(nf.Limit, sb, 0);
                    if (nf.Step != null)
                    {
                        sb.Append(", ");
                        EmitExpr(nf.Step, sb, 0);
                    }
                    sb.Append(" do\n");
                    EmitBlock(nf.Body, sb, indent + 1);
                    sb.Append(pad).Append("end\n");
                    break;

                case Stat.GenericFor gf:
                    sb.Append(pad).Append("for ").Append(string.Join(", ", gf.Names)).Append(" in ");
                    EmitExprList(gf.Iterators, sb);
                    sb.Append(" do\n");
                    EmitBlock(gf.Body, sb, indent + 1);
                    sb.Append(pad).Append("end\n");
                    break;

                case Stat.Return ret:
                    sb.Append(pad).Append("return");
                    if (ret.Values.Count > 0)
                    {
                        sb.Append(' ');
                        EmitExprList(ret.Values, sb);
                    }
                    sb.Append('\n');
                    break;

                case Stat.Break _:
                    sb.Append(pad).Append("break\n");
                    break;

                case Stat.Comment cm:
                    sb.Append(pad).Append("-- ").Append(cm.Text).Append('\n');
                    break;
            }
        }

        // ---- expressions ----

        private static void EmitExprList(IReadOnlyList<Expr> exprs, StringBuilder sb)
        {
            for (int i = 0; i < exprs.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                EmitExpr(exprs[i], sb, 0);
            }
        }

        /// <summary>Emit an expression; parentPrec is the enclosing operator's precedence (0 = none).</summary>
        private static void EmitExpr(Expr expr, StringBuilder sb, int parentPrec)
        {
            switch (expr)
            {
                case Expr.Nil _: sb.Append("nil"); break;
                case Expr.Bool b: sb.Append(b.Value ? "true" : "false"); break;
                case Expr.Number n: EmitNumber(n.Value, sb); break;
                case Expr.StringLit s: EmitString(s.Value.Bytes, sb); break;
                case Expr.VarArg _: sb.Append("..."); break;
                case Expr.Name n: sb.Append(n.Id); break;
                case Expr.Global g: sb.Append(g.GlobalName); break;
                case Expr.Register r: sb.Append('r').Append(r.Reg); break;
                case Expr.Upvalue u: sb.Append("upval").Append(u.Idx); break;
                case Expr.Index ix:
                    EmitExpr(ix.Target, sb, 10);
                    sb.Append('[');
                    EmitExpr(ix.Key, sb, 0);
                    sb.Append(']');
                    break;
                case Expr.Field f:
                    EmitExpr(f.Target, sb, 10);
                    sb.Append('.').Append(f.FieldName);
                    break;
                case Expr.BinOp bo:
                    {
                        int prec = bo.Op.Precedence();
                        bool parens = prec < parentPrec;
                        if (parens) sb.Append('(');
                        EmitExpr(bo.Lhs, sb, prec);
                        sb.Append(' ').Append(bo.Op.Symbol()).Append(' ');
                        // Right-associative: right child needs prec (not prec+1) to avoid extra parens
                        EmitExpr(bo.Rhs, sb, bo.Op.IsRightAssoc() ? prec : prec + 1);
                        if (parens) sb.Append(')');
                        break;
                    }
                case Expr.UnOp uo:
                    {
                        int prec = uo.Op.Precedence();
                        bool parens = prec < parentPrec;
                        if (parens) sb.Append('(');
                        sb.Append(uo.Op.Symbol());
                        EmitExpr(uo.Operand, sb, prec);
                        if (parens) sb.Append(')');
                        break;
                    }
                case Expr.FuncCall fc: EmitCall(fc.Invocation, sb); break;
                case Expr.MethodCall mc: EmitCall(mc.Invocation, sb); break;
                case Expr.FunctionDef fd: EmitFunctionDef(fd.Func, sb); break;
                case Expr.Table t: EmitTable(t.Fields, sb); break;
            }
        }

        private static void EmitCall(CallExpr call, StringBuilder sb)
        {
            EmitExpr(call.Func, sb, 10);
            sb.Append('(');
            EmitExprList(call.Args, sb);
            sb.Append(')');
        }

        private static void EmitFunctionDef(Function func, StringBuilder sb)
        {
            sb.Append("function(").Append(ParamList(func)).Append(")\n");
            int currentIndent = CountTrailingIndent(sb);
            EmitBlock(func.Body, sb, currentIndent + 1);
            sb.Append(Pad(currentIndent)).Append("end");
        }

        private static void EmitTableField(TableField field, StringBuilder sb)
        {
            switch (field)
            {
                case TableField.IndexField ix:
                    sb.Append('[');
                    EmitExpr(ix.Key, sb, 0);
                    sb.Append("] = ");
                    EmitExpr(ix.Val, sb, 0);
                    break;
                case TableField.NameField nf:
                    sb.Append(nf.FieldName).Append(" = ");
                    EmitExpr(nf.Val, sb, 0);
                    break;
                case TableField.Value v:
                    EmitExpr(v.Val, sb, 0);
                    break;
            }
        }

        private static void EmitTable(IReadOnlyList<TableField> fields, StringBuilder sb)
        {
            if (fields.Count == 0)
            {
                sb.Append("{}");
                return;
            }

            int currentIndent = CountTrailingIndent(sb);
            string inline = EmitTableInline(fields);
            int inlineLen = Encoding.UTF8.GetByteCount(inline); // Rust str::len is in bytes
            bool multiline = inlineLen > 80 || (fields.Count > 4 && inlineLen > 60);

            if (!multiline)
            {
                sb.Append(inline);
                return;
            }

            string innerPad = Pad(currentIndent + 1);
            sb.Append("{\n");
            for (int i = 0; i < fields.Count; i++)
            {
                sb.Append(innerPad);
                EmitTableField(fields[i], sb);
                if (i + 1 < fields.Count) sb.Append(',');
                sb.Append('\n');
            }
            sb.Append(Pad(currentIndent)).Append('}');
        }

        /// <summary>Emit a table on one line (also used for length estimation).</summary>
        private static string EmitTableInline(IReadOnlyList<TableField> fields)
        {
            var s = new StringBuilder("{");
            for (int i = 0; i < fields.Count; i++)
            {
                if (i > 0) s.Append(", ");
                EmitTableField(fields[i], s);
            }
            return s.Append('}').ToString();
        }

        // ---- literals ----

        private static void EmitNumber(NumLit n, StringBuilder sb)
        {
            switch (n)
            {
                case NumLit.Int i:
                    sb.Append(i.V.ToString(CultureInfo.InvariantCulture));
                    break;
                case NumLit.Float f:
                    if (Lifter.HasNoFraction(f.V) && Math.Abs(f.V) < 1e15)
                        sb.Append(((long)f.V).ToString(CultureInfo.InvariantCulture));
                    else
                        sb.Append(FormatRustF64(f.V));
                    break;
            }
        }

        /// <summary>Format a double like Rust's `{}` Display: shortest round-trip digits, never exponent notation.</summary>
        public static string FormatRustF64(double v)
        {
            if (double.IsNaN(v)) return "NaN";
            if (double.IsPositiveInfinity(v)) return "inf";
            if (double.IsNegativeInfinity(v)) return "-inf";

            string r = v.ToString("R", CultureInfo.InvariantCulture);
            int e = r.IndexOfAny(new[] { 'E', 'e' });
            if (e < 0) return r;

            bool neg = r[0] == '-';
            string mant = r.Substring(neg ? 1 : 0, e - (neg ? 1 : 0));
            int exp = int.Parse(r.Substring(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            int dot = mant.IndexOf('.');
            string digits = dot < 0 ? mant : mant.Remove(dot, 1);
            int pointPos = (dot < 0 ? mant.Length : dot) + exp; // position of the decimal point within digits

            string result;
            if (pointPos <= 0)
                result = "0." + new string('0', -pointPos) + digits;
            else if (pointPos >= digits.Length)
                result = digits + new string('0', pointPos - digits.Length);
            else
                result = digits.Substring(0, pointPos) + "." + digits.Substring(pointPos);
            return neg ? "-" + result : result;
        }

        private static Encoding _gbk;
        private static bool _gbkTried;

        /// <summary>GBK/GB18030 decoder that throws on invalid input, or null if unavailable.</summary>
        private static Encoding Gbk
        {
            get
            {
                if (!_gbkTried)
                {
                    _gbkTried = true;
                    try
                    {
                        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                        _gbk = Encoding.GetEncoding(54936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                    }
                    catch (Exception)
                    {
                        _gbk = null;
                    }
                }
                return _gbk;
            }
        }

        /// <summary>Emit a string literal: UTF-8 if valid, then GBK, else byte escapes.</summary>
        private static void EmitString(byte[] bytes, StringBuilder sb)
        {
            if (Lifter.TryUtf8(bytes, out var text))
            {
                EmitStringContent(text, sb);
                return;
            }

            var gbk = Gbk;
            if (gbk != null)
            {
                try
                {
                    EmitStringContent(gbk.GetString(bytes), sb);
                    return;
                }
                catch (DecoderFallbackException)
                {
                }
            }

            sb.Append('"');
            foreach (byte b in bytes) EmitByteEscaped(b, sb);
            sb.Append('"');
        }

        private static void EmitStringContent(string text, StringBuilder sb)
        {
            bool hasNewlines = text.IndexOf('\n') >= 0;
            bool hasLongBracketClose = text.Contains("]]");
            bool isPrintable = text.All(c => !char.IsControl(c) || c == '\n' || c == '\r' || c == '\t');

            if (hasNewlines && !hasLongBracketClose && isPrintable)
            {
                sb.Append("[[");
                if (text.StartsWith("\n", StringComparison.Ordinal)) sb.Append('\n');
                sb.Append(text).Append("]]");
                return;
            }

            sb.Append('"');
            foreach (char ch in text)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\0': sb.Append("\\0"); break;
                    case '\a': sb.Append("\\a"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\v': sb.Append("\\v"); break;
                    default:
                        if ((ch >= ' ' && ch <= '~') || !char.IsControl(ch))
                        {
                            sb.Append(ch);
                        }
                        else
                        {
                            foreach (byte b in Encoding.UTF8.GetBytes(new[] { ch }))
                                sb.Append('\\').Append(b);
                        }
                        break;
                }
            }
            sb.Append('"');
        }

        private static void EmitByteEscaped(byte b, StringBuilder sb)
        {
            switch (b)
            {
                case (byte)'\\': sb.Append("\\\\"); break;
                case (byte)'"': sb.Append("\\\""); break;
                case (byte)'\n': sb.Append("\\n"); break;
                case (byte)'\r': sb.Append("\\r"); break;
                case (byte)'\t': sb.Append("\\t"); break;
                case 0: sb.Append("\\0"); break;
                case 0x07: sb.Append("\\a"); break;
                case 0x08: sb.Append("\\b"); break;
                case 0x0C: sb.Append("\\f"); break;
                case 0x0B: sb.Append("\\v"); break;
                default:
                    if (b >= 0x20 && b <= 0x7E) sb.Append((char)b);
                    else sb.Append('\\').Append(b);
                    break;
            }
        }

        /// <summary>Indent level of the text after the last newline (leading whitespace / 2).</summary>
        private static int CountTrailingIndent(StringBuilder sb)
        {
            int i = sb.Length - 1;
            while (i >= 0 && sb[i] != '\n') i--;
            if (i < 0) return 0;
            int spaces = 0;
            for (int j = i + 1; j < sb.Length && char.IsWhiteSpace(sb[j]); j++) spaces++;
            return spaces / 2;
        }
    }
}

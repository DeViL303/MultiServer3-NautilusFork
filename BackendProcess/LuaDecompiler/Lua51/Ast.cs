// Ported from luadec-rust (https://github.com/ItsLucas/luadecng-rs), MIT License.
//
// AST nodes are immutable records. Lists inside nodes use EqList, which compares by content
// (like Rust's Vec PartialEq), so `==` on nodes is structural just like the original.
// Treat every list stored in a node as read-only; copy it (new EqList<T>(list)) before changing it.
using System;
using System.Collections.Generic;
using System.Linq;

namespace LuaDecompiler.Lua51
{
    /// <summary>A List with structural (element-wise) equality.</summary>
    public sealed class EqList<T> : List<T>
    {
        public EqList() { }
        public EqList(IEnumerable<T> items) : base(items) { }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(this, obj)) return true;
            if (!(obj is EqList<T> other) || other.Count != Count) return false;
            var cmp = EqualityComparer<T>.Default;
            for (int i = 0; i < Count; i++)
                if (!cmp.Equals(this[i], other[i])) return false;
            return true;
        }

        public override int GetHashCode() => Count;
    }

    public static class EqListExtensions
    {
        public static EqList<T> ToEqList<T>(this IEnumerable<T> items) => new EqList<T>(items);
    }

    /// <summary>Raw string bytes with structural equality.</summary>
    public sealed class ByteStr : IEquatable<ByteStr>
    {
        public readonly byte[] Bytes;
        public ByteStr(byte[] bytes) { Bytes = bytes ?? Array.Empty<byte>(); }
        public bool Equals(ByteStr other) => other != null && Bytes.AsSpan().SequenceEqual(other.Bytes);
        public override bool Equals(object obj) => obj is ByteStr b && Equals(b);
        public override int GetHashCode() => Bytes.Length;
    }

    /// <summary>A complete Lua function body (chunk).</summary>
    public sealed record Function(EqList<string> Params, bool IsVararg, EqList<Stat> Body);

    public sealed record ElseIfClause(Expr Cond, EqList<Stat> Block);

    /// <summary>Lua statement.</summary>
    public abstract record Stat
    {
        /// <summary>local var1, var2 = expr1, expr2</summary>
        public sealed record LocalAssign(EqList<string> Names, EqList<Expr> Exprs) : Stat;
        /// <summary>var1, var2 = expr1, expr2</summary>
        public sealed record Assign(EqList<Expr> Targets, EqList<Expr> Values) : Stat;
        /// <summary>expr(args) as a statement</summary>
        public sealed record Call(CallExpr Invocation) : Stat;
        /// <summary>do ... end</summary>
        public sealed record DoBlock(EqList<Stat> Body) : Stat;
        /// <summary>while cond do ... end</summary>
        public sealed record While(Expr Cond, EqList<Stat> Body) : Stat;
        /// <summary>repeat ... until cond</summary>
        public sealed record Repeat(EqList<Stat> Body, Expr Cond) : Stat;
        /// <summary>if cond then ... elseif ... else ... end (ElseBlock may be null)</summary>
        public sealed record If(Expr Cond, EqList<Stat> ThenBlock, EqList<ElseIfClause> ElseifClauses, EqList<Stat> ElseBlock) : Stat;
        /// <summary>for name = start, limit, step do ... end (Step may be null)</summary>
        public sealed record NumericFor(string Var, Expr Start, Expr Limit, Expr Step, EqList<Stat> Body) : Stat;
        /// <summary>for name1, name2 in iter do ... end</summary>
        public sealed record GenericFor(EqList<string> Names, EqList<Expr> Iterators, EqList<Stat> Body) : Stat;
        /// <summary>return expr1, expr2, ...</summary>
        public sealed record Return(EqList<Expr> Values) : Stat;
        /// <summary>break</summary>
        public sealed record Break() : Stat;
        /// <summary>-- comment</summary>
        public sealed record Comment(string Text) : Stat;
    }

    /// <summary>Lua expression.</summary>
    public abstract record Expr
    {
        public sealed record Nil() : Expr;
        public sealed record Bool(bool Value) : Expr;
        public sealed record Number(NumLit Value) : Expr;
        /// <summary>String literal (raw bytes; emitter handles encoding).</summary>
        public sealed record StringLit(ByteStr Value) : Expr;
        public sealed record VarArg() : Expr;
        /// <summary>Variable reference by name.</summary>
        public sealed record Name(string Id) : Expr;
        /// <summary>table[key]</summary>
        public sealed record Index(Expr Target, Expr Key) : Expr;
        /// <summary>table.name</summary>
        public sealed record Field(Expr Target, string FieldName) : Expr;
        /// <summary>prefix:method(args)</summary>
        public sealed record MethodCall(CallExpr Invocation) : Expr;
        /// <summary>f(args)</summary>
        public sealed record FuncCall(CallExpr Invocation) : Expr;
        public sealed record BinOp(BinOpKind Op, Expr Lhs, Expr Rhs) : Expr;
        public sealed record UnOp(UnOpKind Op, Expr Operand) : Expr;
        /// <summary>function(...) ... end</summary>
        public sealed record FunctionDef(Function Func) : Expr;
        /// <summary>Table constructor { ... }</summary>
        public sealed record Table(EqList<TableField> Fields) : Expr;
        /// <summary>Placeholder for registers not yet resolved.</summary>
        public sealed record Register(uint Reg) : Expr;
        /// <summary>Upvalue reference by index.</summary>
        public sealed record Upvalue(uint Idx) : Expr;
        /// <summary>Global variable reference.</summary>
        public sealed record Global(string GlobalName) : Expr;

        public static Expr StringFromText(string s) => new StringLit(new ByteStr(System.Text.Encoding.UTF8.GetBytes(s)));
    }

    /// <summary>Numeric literal (preserves integer vs float distinction).</summary>
    public abstract record NumLit
    {
        public sealed record Int(long V) : NumLit;
        public sealed record Float(double V) : NumLit;
    }

    /// <summary>Function/method call.</summary>
    public sealed record CallExpr(Expr Func, EqList<Expr> Args);

    /// <summary>Table constructor field.</summary>
    public abstract record TableField
    {
        /// <summary>[expr] = expr</summary>
        public sealed record IndexField(Expr Key, Expr Val) : TableField;
        /// <summary>name = expr</summary>
        public sealed record NameField(string FieldName, Expr Val) : TableField;
        /// <summary>Positional value</summary>
        public sealed record Value(Expr Val) : TableField;
    }

    public enum BinOpKind { Add, Sub, Mul, Div, Mod, Pow, Concat, Eq, Ne, Lt, Le, Gt, Ge, And, Or }

    public enum UnOpKind { Neg, Not, Len }

    public static class OperatorInfo
    {
        /// <summary>Operator precedence (higher = binds tighter).</summary>
        public static int Precedence(this BinOpKind op)
        {
            switch (op)
            {
                case BinOpKind.Or: return 1;
                case BinOpKind.And: return 2;
                case BinOpKind.Eq: case BinOpKind.Ne: case BinOpKind.Lt: case BinOpKind.Le: case BinOpKind.Gt: case BinOpKind.Ge: return 3;
                case BinOpKind.Concat: return 4;
                case BinOpKind.Add: case BinOpKind.Sub: return 5;
                case BinOpKind.Mul: case BinOpKind.Div: case BinOpKind.Mod: return 6;
                default: return 7; // Pow
            }
        }

        public static bool IsRightAssoc(this BinOpKind op) => op == BinOpKind.Pow || op == BinOpKind.Concat;

        public static string Symbol(this BinOpKind op)
        {
            switch (op)
            {
                case BinOpKind.Add: return "+";
                case BinOpKind.Sub: return "-";
                case BinOpKind.Mul: return "*";
                case BinOpKind.Div: return "/";
                case BinOpKind.Mod: return "%";
                case BinOpKind.Pow: return "^";
                case BinOpKind.Concat: return "..";
                case BinOpKind.Eq: return "==";
                case BinOpKind.Ne: return "~=";
                case BinOpKind.Lt: return "<";
                case BinOpKind.Le: return "<=";
                case BinOpKind.Gt: return ">";
                case BinOpKind.Ge: return ">=";
                case BinOpKind.And: return "and";
                default: return "or";
            }
        }

        /// <summary>All unary ops bind tighter than any binary op.</summary>
        public static int Precedence(this UnOpKind op) => 8;

        public static string Symbol(this UnOpKind op)
        {
            switch (op)
            {
                case UnOpKind.Neg: return "-";
                case UnOpKind.Not: return "not ";
                default: return "#";
            }
        }
    }
}

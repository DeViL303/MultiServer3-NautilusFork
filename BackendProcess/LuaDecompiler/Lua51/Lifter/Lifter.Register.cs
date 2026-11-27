// Ported from luadec-rust (https://github.com/ItsLucas/luadecng-rs), MIT License.
using System.Collections.Generic;
using System.Text;
using LuaDecompiler.Parser;

namespace LuaDecompiler.Lua51
{
    public sealed partial class Lifter
    {
        private void SetReg(uint reg, Expr expr)
        {
            if (reg < _regs.Length) _regs[reg] = expr;
            // The register is overwritten: forget its local name, pending table and alias.
            _localNames.Remove(reg);
            _pendingTables.Remove(reg);
            _captureAliases.Remove(reg);
        }

        /// <summary>Set a register and emit a `local` declaration if this is a new local variable.</summary>
        private void SetRegLocal(uint reg, Expr expr, EqList<Stat> stmts)
        {
            if (reg >= _numParams && !_declaredLocals.Contains(reg))
            {
                _declaredLocals.Add(reg);
                string name = MakeLocalNameForExpr(reg, expr);
                _localNames[reg] = name;
                if (reg < _regs.Length) _regs[reg] = new Expr.Name(name);
                stmts.Add(new Stat.LocalAssign(new EqList<string> { name }, new EqList<Expr> { expr }));
            }
            else if (_localNames.TryGetValue(reg, out var name))
            {
                if (reg < _regs.Length) _regs[reg] = new Expr.Name(name);
                stmts.Add(new Stat.Assign(new EqList<Expr> { new Expr.Name(name) }, new EqList<Expr> { expr }));
            }
            else
            {
                if (reg < _regs.Length) _regs[reg] = expr;
            }
        }

        private void AssignRegExpr(int pc, uint reg, Expr expr, EqList<Stat> stmts)
        {
            int blockId = _cfg.BlockOf(pc);
            bool liveOutOfBlock = reg < _liveness.MaxReg && _liveness.LiveOut[blockId][reg];

            if (_accumulatorRegs.Contains(reg) && reg >= _numParams && liveOutOfBlock)
                SetRegLocal(reg, expr, stmts);
            else
                SetReg(reg, expr);
        }

        private List<Expr> ResolveClosureUpvalues(int pc, LuaChunk subChunk, EqList<Stat> stmts)
        {
            int numUpvalues = subChunk.NumUpvalues;
            var resolved = new List<Expr>(numUpvalues);
            for (int offset = 0; offset < numUpvalues; offset++)
            {
                int capturePc = pc + 1 + offset;
                if (capturePc >= _cfg.Instructions.Count)
                {
                    resolved.Add(null);
                    continue;
                }
                var capture = _cfg.Instructions[capturePc];
                switch (capture.Op)
                {
                    case OpCode.Move: resolved.Add(CaptureStackUpvalue(capture.B, capturePc, stmts)); break;
                    case OpCode.GetUpval: resolved.Add(UpvalueExpr(capture.B)); break;
                    default: resolved.Add(null); break;
                }
            }
            return resolved;
        }

        private Expr CaptureStackUpvalue(uint reg, int pc, EqList<Stat> stmts)
        {
            if (_localNames.TryGetValue(reg, out var existing))
                return new Expr.Name(existing);

            if (reg < _numParams)
            {
                string name = LocalName(reg, pc);
                _localNames[reg] = name;
                if (reg < _regs.Length) _regs[reg] = new Expr.Name(name);
                return new Expr.Name(name);
            }

            if (_captureAliases.TryGetValue(reg, out var alias))
                return alias;

            var expr = RegExpr(reg);
            switch (expr)
            {
                case Expr.Name _:
                case Expr.Global _:
                case Expr.Field _:
                    return expr;
                case Expr.Upvalue u:
                    return UpvalueExpr(u.Idx);
                default:
                    {
                        string name = LocalName(reg, pc);
                        _localNames[reg] = name;
                        _declaredLocals.Add(reg);
                        if (reg < _regs.Length) _regs[reg] = new Expr.Name(name);
                        _pendingTables.Remove(reg);
                        _captureAliases.Remove(reg);
                        stmts.Add(new Stat.LocalAssign(new EqList<string> { name }, new EqList<Expr> { expr }));
                        return new Expr.Name(name);
                    }
            }
        }

        private Expr RegExpr(uint reg)
        {
            if (_localNames.TryGetValue(reg, out var name))
                return new Expr.Name(name);
            if (_pendingTables.TryGetValue(reg, out var fields) && fields.Count > 0)
                return new Expr.Table(new EqList<TableField>(fields));
            if (reg < _regs.Length)
                return _regs[reg] ?? new Expr.Register(reg);
            return new Expr.Register(reg);
        }

        private Expr RkExpr(uint rk) => Instruction.IsK(rk) ? ConstExpr(Instruction.IndexK(rk)) : RegExpr(rk);

        private Expr ConstExpr(uint idx)
        {
            if (idx >= _chunk.Constants.Count) return new Expr.Nil();
            var k = _chunk.Constants[(int)idx];
            switch (k.Kind)
            {
                case LuaConstantKind.Bool: return new Expr.Bool(k.BoolValue);
                case LuaConstantKind.Number:
                    return k.IsInteger ? new Expr.Number(new NumLit.Int(k.IntValue)) : new Expr.Number(new NumLit.Float(k.FloatValue));
                case LuaConstantKind.String: return new Expr.StringLit(new ByteStr(k.StringValue));
                default: return new Expr.Nil();
            }
        }

        private string ConstString(uint idx)
        {
            if (idx < _chunk.Constants.Count)
            {
                var k = _chunk.Constants[(int)idx];
                if (k.Kind == LuaConstantKind.String)
                    return Encoding.UTF8.GetString(k.StringValue);
            }
            return $"_K{idx}";
        }

        private Expr UpvalueExpr(uint idx)
        {
            if (idx < _resolvedUpvalues.Count && _resolvedUpvalues[(int)idx] != null)
                return _resolvedUpvalues[(int)idx];
            if (idx < _chunk.UpvalueNames.Count)
            {
                string name = Encoding.UTF8.GetString(_chunk.UpvalueNames[(int)idx]);
                if (name.Length > 0) return new Expr.Name(name);
            }
            return new Expr.Upvalue(idx);
        }
    }
}

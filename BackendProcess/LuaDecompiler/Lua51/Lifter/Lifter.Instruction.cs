// Ported from luadec-rust (https://github.com/ItsLucas/luadecng-rs), MIT License.
using System;
using System.Collections.Generic;

namespace LuaDecompiler.Lua51
{
    public sealed partial class Lifter
    {
        private Expr Arith(BinOpKind op, Instruction inst) => new Expr.BinOp(op, RkExpr(inst.B), RkExpr(inst.C));

        /// <summary>Number of values for a "B == 0" (up to stack top) operand starting at register <paramref name="first"/>.</summary>
        private uint OpenCountAt(int pc, uint first) => Liveness.OpenCount(first, Liveness.Prev(_cfg, pc));

        /// <summary>Lift a range of instructions into statements.</summary>
        private void LiftInstructions(int startPc, int endPc, EqList<Stat> stmts)
        {
            int pc = startPc;
            while (pc <= endPc)
            {
                var inst = _cfg.Instructions[pc];
                switch (inst.Op)
                {
                    case OpCode.Move:
                        AssignRegExpr(pc, inst.A, RegExpr(inst.B), stmts);
                        break;
                    case OpCode.LoadK:
                        AssignRegExpr(pc, inst.A, ConstExpr(inst.Bx), stmts);
                        break;
                    case OpCode.LoadBool:
                        AssignRegExpr(pc, inst.A, new Expr.Bool(inst.B != 0), stmts);
                        if (inst.C != 0) pc++; // skip next instruction
                        break;
                    case OpCode.LoadNil:
                        for (uint r = inst.A; r <= inst.B; r++)
                            AssignRegExpr(pc, r, new Expr.Nil(), stmts);
                        break;
                    case OpCode.GetUpval:
                        AssignRegExpr(pc, inst.A, UpvalueExpr(inst.B), stmts);
                        break;
                    case OpCode.GetGlobal:
                        AssignRegExpr(pc, inst.A, new Expr.Global(ConstString(inst.Bx)), stmts);
                        break;
                    case OpCode.GetTable:
                        AssignRegExpr(pc, inst.A, MakeIndex(RegExpr(inst.B), RkExpr(inst.C)), stmts);
                        break;
                    case OpCode.SetGlobal:
                        {
                            FlushPendingTable(inst.A);
                            string name = ConstString(inst.Bx);
                            var val = RegExpr(inst.A);
                            stmts.Add(new Stat.Assign(new EqList<Expr> { new Expr.Global(name) }, new EqList<Expr> { val }));
                            _captureAliases[inst.A] = new Expr.Global(ConstString(inst.Bx));
                            break;
                        }
                    case OpCode.SetUpval:
                        {
                            var val = RegExpr(inst.A);
                            var uv = UpvalueExpr(inst.B);
                            stmts.Add(new Stat.Assign(new EqList<Expr> { uv }, new EqList<Expr> { val }));
                            break;
                        }
                    case OpCode.SetTable:
                        {
                            if (_pendingTables.TryGetValue(inst.A, out var fields))
                            {
                                // Part of a table constructor
                                var key = RkExpr(inst.B);
                                var v = RkExpr(inst.C);
                                if (key is Expr.StringLit s && TryUtf8(s.Value.Bytes, out var fname) && IsIdentifier(fname))
                                    fields.Add(new TableField.NameField(fname, v));
                                else
                                    fields.Add(new TableField.IndexField(key, v));
                                pc++;
                                continue;
                            }
                            FlushPendingTable(inst.A);
                            var table = RegExpr(inst.A);
                            var k = RkExpr(inst.B);
                            var value = RkExpr(inst.C);
                            var target = MakeIndex(table, k);
                            stmts.Add(new Stat.Assign(new EqList<Expr> { target }, new EqList<Expr> { value }));
                            if (!Instruction.IsK(inst.C))
                                _captureAliases[inst.C] = target;
                            break;
                        }
                    case OpCode.NewTable:
                        AssignRegExpr(pc, inst.A, new Expr.Table(new EqList<TableField>()), stmts);
                        _pendingTables[inst.A] = new EqList<TableField>();
                        break;
                    case OpCode.Self_:
                        {
                            var table = RegExpr(inst.B);
                            var method = RkExpr(inst.C);
                            var methodRef = MakeIndex(table, method);
                            AssignRegExpr(pc, inst.A + 1, table, stmts);
                            AssignRegExpr(pc, inst.A, methodRef, stmts);
                            break;
                        }
                    case OpCode.Add: AssignRegExpr(pc, inst.A, Arith(BinOpKind.Add, inst), stmts); break;
                    case OpCode.Sub: AssignRegExpr(pc, inst.A, Arith(BinOpKind.Sub, inst), stmts); break;
                    case OpCode.Mul: AssignRegExpr(pc, inst.A, Arith(BinOpKind.Mul, inst), stmts); break;
                    case OpCode.Div: AssignRegExpr(pc, inst.A, Arith(BinOpKind.Div, inst), stmts); break;
                    case OpCode.Mod: AssignRegExpr(pc, inst.A, Arith(BinOpKind.Mod, inst), stmts); break;
                    case OpCode.Pow: AssignRegExpr(pc, inst.A, Arith(BinOpKind.Pow, inst), stmts); break;
                    case OpCode.Unm: AssignRegExpr(pc, inst.A, new Expr.UnOp(UnOpKind.Neg, RegExpr(inst.B)), stmts); break;
                    case OpCode.Not: AssignRegExpr(pc, inst.A, new Expr.UnOp(UnOpKind.Not, RegExpr(inst.B)), stmts); break;
                    case OpCode.Len: AssignRegExpr(pc, inst.A, new Expr.UnOp(UnOpKind.Len, RegExpr(inst.B)), stmts); break;
                    case OpCode.Concat:
                        {
                            uint b = inst.B, c = inst.C;
                            var expr = RegExpr(b);
                            for (uint r = b + 1; r <= c; r++)
                                expr = new Expr.BinOp(BinOpKind.Concat, expr, RegExpr(r));
                            AssignRegExpr(pc, inst.A, expr, stmts);
                            break;
                        }
                    case OpCode.Jmp:
                        break; // handled by the CFG
                    case OpCode.Eq:
                    case OpCode.Lt:
                    case OpCode.Le:
                    case OpCode.Test:
                    case OpCode.TestSet:
                        pc++; // handled at block level; skip the following JMP
                        break;
                    case OpCode.Call:
                        {
                            var func = RegExpr(inst.A);
                            // Port fix: B == 0 means "arguments up to the stack top" (e.g. print(f(x))).
                            uint numArgs = inst.B == 0 ? OpenCountAt(pc, inst.A + 1) : inst.B - 1;
                            var args = new EqList<Expr>();
                            for (uint i = 0; i < numArgs; i++) args.Add(RegExpr(inst.A + 1 + i));
                            var call = new CallExpr(func, args);

                            if (inst.C == 1)
                            {
                                // No return values -> statement call
                                stmts.Add(new Stat.Call(call));
                            }
                            else if (inst.C == 0)
                            {
                                // Variable return values (used by the next CALL/RETURN/SETLIST)
                                SetReg(inst.A, new Expr.FuncCall(call));
                            }
                            else
                            {
                                uint numResults = inst.C - 1;
                                if (numResults == 1)
                                {
                                    var callExpr = new Expr.FuncCall(call);
                                    if (Liveness.IsRegLiveAfter(_cfg, _liveness, pc, inst.A))
                                        SetRegLocal(inst.A, callExpr, stmts);
                                    else
                                        stmts.Add(new Stat.Call(call)); // result unused
                                }
                                else
                                {
                                    var names = new EqList<string>();
                                    for (uint i = 0; i < numResults; i++)
                                    {
                                        uint r = inst.A + i;
                                        string name = MakeLocalName(r);
                                        _localNames[r] = name;
                                        _declaredLocals.Add(r);
                                        names.Add(name);
                                    }
                                    stmts.Add(new Stat.LocalAssign(new EqList<string>(names), new EqList<Expr> { new Expr.FuncCall(call) }));
                                    for (int i = 0; i < names.Count; i++)
                                    {
                                        uint r = inst.A + (uint)i;
                                        if (r < _regs.Length) _regs[r] = new Expr.Name(names[i]);
                                    }
                                }
                            }
                            break;
                        }
                    case OpCode.TailCall:
                        {
                            var func = RegExpr(inst.A);
                            uint numArgs = inst.B == 0 ? OpenCountAt(pc, inst.A + 1) : inst.B - 1;
                            var args = new EqList<Expr>();
                            for (uint i = 0; i < numArgs; i++) args.Add(RegExpr(inst.A + 1 + i));
                            stmts.Add(new Stat.Return(new EqList<Expr> { new Expr.FuncCall(new CallExpr(func, args)) }));
                            break;
                        }
                    case OpCode.Return:
                        {
                            uint numRet = inst.B == 0 ? OpenCountAt(pc, inst.A) : inst.B - 1;
                            if (numRet == 0 && inst.A == 0)
                            {
                                // `return` with no values at the end of a function may be implicit
                                if (pc != endPc || endPc != _cfg.Instructions.Count - 1)
                                    stmts.Add(new Stat.Return(new EqList<Expr>()));
                            }
                            else
                            {
                                var vals = new EqList<Expr>();
                                for (uint i = 0; i < numRet; i++) vals.Add(RegExpr(inst.A + i));
                                stmts.Add(new Stat.Return(vals));
                            }
                            break;
                        }
                    case OpCode.ForLoop:
                    case OpCode.ForPrep:
                    case OpCode.TForLoop:
                        break; // handled by loop lifting
                    case OpCode.SetList:
                        {
                            uint tableReg = inst.A;
                            uint num = inst.B == 0 ? OpenCountAt(pc, tableReg + 1) : inst.B;
                            for (uint i = 1; i <= num; i++) FlushPendingTable(tableReg + i);
                            var values = new List<Expr>();
                            for (uint i = 1; i <= num; i++) values.Add(RegExpr(tableReg + i));
                            if (_pendingTables.TryGetValue(tableReg, out var fields))
                            {
                                foreach (var v in values) fields.Add(new TableField.Value(v));
                            }
                            // SETLIST with C == 0 keeps the real C in the next instruction word.
                            if (inst.C == 0) pc++;
                            break;
                        }
                    case OpCode.Close:
                        break; // no visible effect in source
                    case OpCode.Closure:
                        {
                            int closurePc = pc;
                            int protoIdx = (int)inst.Bx;
                            Function subFunc;
                            if (protoIdx < _chunk.Prototypes.Count)
                            {
                                var subChunk = _chunk.Prototypes[protoIdx];
                                var resolved = ResolveClosureUpvalues(pc, subChunk, stmts);
                                subFunc = DecompileNested(subChunk, resolved);
                                pc += subChunk.NumUpvalues;
                            }
                            else
                            {
                                subFunc = new Function(new EqList<string>(), false, new EqList<Stat>());
                            }
                            AssignRegExpr(closurePc, inst.A, new Expr.FunctionDef(subFunc), stmts);
                            break;
                        }
                    case OpCode.VarArg:
                        AssignRegExpr(pc, inst.A, new Expr.VarArg(), stmts);
                        break;
                }
                pc++;
            }
        }

        /// <summary>
        /// Decompile a nested function. Port addition: if one function fails, it becomes a stub with a
        /// comment instead of failing the whole file.
        /// </summary>
        private static Function DecompileNested(Parser.LuaChunk subChunk, List<Expr> resolved)
        {
            try
            {
                return DecompileWithUpvalues(subChunk, resolved);
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                var ps = new EqList<string>();
                for (int i = 0; i < subChunk.NumParams; i++) ps.Add($"a{i}");
                return new Function(ps, subChunk.IsVararg,
                    new EqList<Stat> { new Stat.Comment($"luadec: could not decompile this function ({ex.GetType().Name}: {ex.Message})") });
            }
        }
    }
}

// Self-tests for the LuaDecompiler library.
//
//   dotnet run --project BackendProcess/LuaDecompiler.Tests
//       runs the built-in tests
//   dotnet run --project BackendProcess/LuaDecompiler.Tests -- <file.luac | folder> [output folder]
//       also decompiles real files and reports any that fail (output defaults to ./decompiled)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using LuaDecompiler;
using LuaDecompiler.Lua51;

internal static class Program
{
    private static int _passed, _failed;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        RunEmitterTests();
        RunBytecodeTests();

        Console.WriteLine();
        Console.WriteLine($"Built-in tests: {_passed} passed, {_failed} failed.");

        if (args.Length > 0)
            DecompileRealFiles(args[0], args.Length > 1 ? args[1] : "decompiled");

        return _failed == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ helpers

    private static void Check(string name, bool ok, string output)
    {
        if (ok)
        {
            _passed++;
            Console.WriteLine($"  PASS  {name}");
        }
        else
        {
            _failed++;
            Console.WriteLine($"  FAIL  {name}\n------ output ------\n{output}--------------------");
        }
    }

    private static void Run(string name, Func<string> produce, Func<string, bool> verify)
    {
        string output;
        try
        {
            output = produce();
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine($"  FAIL  {name}: {ex}");
            return;
        }
        Check(name, verify(output), output);
    }

    private static EqList<T> L<T>(params T[] items) => new EqList<T>(items);
    private static Expr N(string name) => new Expr.Name(name);
    private static Stat CallStat(string fn, params Expr[] args) => new Stat.Call(new CallExpr(N(fn), L(args)));
    private static Stat If(Expr cond, EqList<Stat> then, EqList<ElseIfClause> elseifs = null, EqList<Stat> els = null) =>
        new Stat.If(cond, then, elseifs ?? new EqList<ElseIfClause>(), els);
    private static string EmitSingle(Stat stat) => Emitter.EmitChunk(new Function(new EqList<string>(), false, L(stat)));

    // ------------------------------------------------------------------ emitter tests (ported from luadec-rust)

    private static void RunEmitterTests()
    {
        Console.WriteLine("Emitter tests:");

        Run("flattens linear nested ifs",
            () => EmitSingle(If(N("a"), L<Stat>(If(N("b"), L<Stat>(new Stat.Return(L<Expr>())))))),
            s => s.Contains("if a and b then") && !s.Contains("if b then"));

        Run("inverts empty then branch",
            () => EmitSingle(If(N("a"), L<Stat>(), null, L<Stat>(new Stat.Return(L<Expr>())))),
            s => s.Contains("if not a then") && !s.Contains("else"));

        Run("simplifies `true and` condition",
            () => EmitSingle(If(new Expr.BinOp(BinOpKind.And, new Expr.Bool(true), N("ready")), L<Stat>(new Stat.Return(L<Expr>())))),
            s => s.Contains("if ready then") && !s.Contains("true and"));

        Run("hoists guard tail out of outer if",
            () => Emitter.EmitChunk(new Function(L<string>(), false, L<Stat>(
                If(N("a"), L<Stat>(If(N("b"), L<Stat>(new Stat.Return(L<Expr>()))), CallStat("next_step")))))),
            s => s.Contains("if a and b then") && s.Contains("end\nnext_step()"));

        Run("folds else-if into elseif",
            () => EmitSingle(If(N("a"), L(CallStat("left")), null, L<Stat>(If(N("b"), L(CallStat("right")))))),
            s => s.Contains("elseif b then") && !s.Contains("else\n  if b then"));

        Run("merges identical elseif bodies",
            () =>
            {
                var body = L(CallStat("apply"));
                return EmitSingle(If(N("left"), body, L(new ElseIfClause(N("right"), L(CallStat("apply"))), new ElseIfClause(N("extra"), L(CallStat("apply"))))));
            },
            s => s.Contains("if left or right or extra then") && !s.Contains("elseif"));

        Expr BuffCall(long id, long arg) => new Expr.FuncCall(new CallExpr(
            new Expr.Field(N("a1"), "GetBuffByOwner"),
            L<Expr>(new Expr.Number(new NumLit.Int(id)), new Expr.Number(new NumLit.Int(arg)), new Expr.Field(N("a0"), "dwID"))));

        Run("caches repeated getter call inside if",
            () => EmitSingle(If(new Expr.BinOp(BinOpKind.And, N("ready"), BuffCall(30349, 0)),
                L<Stat>(new Stat.Assign(L(N("damage")), L<Expr>(new Expr.Field(BuffCall(30349, 0), "nStackNum")))))),
            s => s.Contains("if ready then")
                 && s.Contains("local buff_30349 = a1.GetBuffByOwner(30349, 0, a0.dwID)")
                 && s.Contains("if buff_30349 then")
                 && s.Contains("damage = buff_30349.nStackNum"));

        Run("caches shared negated getter across sibling ifs",
            () => Emitter.EmitChunk(new Function(L<string>(), false, L<Stat>(
                If(new Expr.BinOp(BinOpKind.And, N("left"), new Expr.UnOp(UnOpKind.Not, BuffCall(4706, 1))), L(CallStat("first"))),
                If(new Expr.BinOp(BinOpKind.And, N("right"), new Expr.UnOp(UnOpKind.Not, BuffCall(4706, 1))), L(CallStat("second")))))),
            s => s.Contains("if left or right then")
                 && s.Contains("local buff_4706 = a1.GetBuffByOwner(4706, 1, a0.dwID)")
                 && s.Contains("if left and not buff_4706 then")
                 && s.Contains("if right and not buff_4706 then"));

        Run("keeps `not not x` (port fix)",
            () => EmitSingle(new Stat.Return(L<Expr>(new Expr.UnOp(UnOpKind.Not, new Expr.UnOp(UnOpKind.Not, N("x")))))),
            s => s == "return not not x\n");

        Run("formats floats like Rust",
            () => string.Join(" ", Emitter.FormatRustF64(0.5), Emitter.FormatRustF64(1e20), Emitter.FormatRustF64(1.5e-7), Emitter.FormatRustF64(-2.25)),
            s => s == "0.5 100000000000000000000 0.00000015 -2.25");
    }

    // ------------------------------------------------------------------ bytecode tests

    private static uint ABC(OpCode op, uint a, uint b, uint c) => Instruction.EncodeABC(op, a, b, c);
    private static uint ABx(OpCode op, uint a, uint bx) => Instruction.EncodeABx(op, a, bx);
    private static uint AsBx(OpCode op, uint a, int sbx) => Instruction.EncodeAsBx(op, a, sbx);
    private static uint K(uint idx) => idx | Instruction.BitRK;

    private static void RunBytecodeTests()
    {
        Console.WriteLine("Bytecode tests:");

        foreach (bool bigEndian in new[] { false, true })
        {
            string tag = bigEndian ? " (big-endian)" : "";

            // print("hi")
            Run("simple call" + tag,
                () => Luadec.Decompile(new ChunkWriter(bigEndian).Main(
                    code: new[] { ABx(OpCode.GetGlobal, 0, 0), ABx(OpCode.LoadK, 1, 1), ABC(OpCode.Call, 0, 2, 1), ABC(OpCode.Return, 0, 1, 0) },
                    constants: new object[] { "print", "hi" },
                    maxStack: 2)),
                s => s == "print(\"hi\")\n");

            // print(f(x)) -- CALL with B == 0 (port fix)
            Run("call with open arguments" + tag,
                () => Luadec.Decompile(new ChunkWriter(bigEndian).Main(
                    code: new[]
                    {
                        ABx(OpCode.GetGlobal, 0, 0), ABx(OpCode.GetGlobal, 1, 1), ABx(OpCode.GetGlobal, 2, 2),
                        ABC(OpCode.Call, 1, 2, 0), ABC(OpCode.Call, 0, 0, 1), ABC(OpCode.Return, 0, 1, 0),
                    },
                    constants: new object[] { "print", "f", "x" },
                    maxStack: 3)),
                s => s == "print(f(x))\n");

            // return ... -- RETURN with B == 0 (port fix)
            Run("return varargs" + tag,
                () => Luadec.Decompile(new ChunkWriter(bigEndian).Main(
                    code: new[] { ABC(OpCode.VarArg, 0, 0, 0), ABC(OpCode.Return, 0, 0, 0), ABC(OpCode.Return, 0, 1, 0) },
                    constants: new object[0],
                    maxStack: 2,
                    isVararg: true)),
                s => s == "return ...\n");

            // if x then print(1) end
            Run("if statement" + tag,
                () => Luadec.Decompile(new ChunkWriter(bigEndian).Main(
                    code: new[]
                    {
                        ABx(OpCode.GetGlobal, 0, 0), ABC(OpCode.Test, 0, 0, 0), AsBx(OpCode.Jmp, 0, 3),
                        ABx(OpCode.GetGlobal, 0, 1), ABx(OpCode.LoadK, 1, 2), ABC(OpCode.Call, 0, 2, 1),
                        ABC(OpCode.Return, 0, 1, 0),
                    },
                    constants: new object[] { "x", "print", 1.0 },
                    maxStack: 2)),
                s => s == "if x then\n  print(1)\nend\n");

            // for i = 1, 10 do print(i) end   (with debug info naming the loop variable)
            Run("numeric for loop" + tag,
                () => Luadec.Decompile(new ChunkWriter(bigEndian).Main(
                    code: new[]
                    {
                        ABx(OpCode.LoadK, 0, 0), ABx(OpCode.LoadK, 1, 1), ABx(OpCode.LoadK, 2, 0),
                        AsBx(OpCode.ForPrep, 0, 3),
                        ABx(OpCode.GetGlobal, 4, 2), ABC(OpCode.Move, 5, 3, 0), ABC(OpCode.Call, 4, 2, 1),
                        AsBx(OpCode.ForLoop, 0, -4),
                        ABC(OpCode.Return, 0, 1, 0),
                    },
                    constants: new object[] { 1.0, 10.0, "print" },
                    maxStack: 6,
                    locals: new[] { ("(for index)", 3, 8), ("(for limit)", 3, 8), ("(for step)", 3, 8), ("i", 4, 7) })),
                s => s == "for i = 1, 10 do\n  print(i)\nend\n");

            // x = { 1, 2, n = 3 }
            Run("table constructor" + tag,
                () => Luadec.Decompile(new ChunkWriter(bigEndian).Main(
                    code: new[]
                    {
                        ABC(OpCode.NewTable, 0, 2, 1), ABx(OpCode.LoadK, 1, 0), ABx(OpCode.LoadK, 2, 1),
                        ABC(OpCode.SetTable, 0, K(2), K(3)), ABC(OpCode.SetList, 0, 2, 1),
                        ABx(OpCode.SetGlobal, 0, 4), ABC(OpCode.Return, 0, 1, 0),
                    },
                    constants: new object[] { 1.0, 2.0, "n", 3.0, "x" },
                    maxStack: 3)),
                s => s == "x = {n = 3, 1, 2}\n");
        }

        Run("rejects non-bytecode",
            () =>
            {
                try { Luadec.Decompile(Encoding.ASCII.GetBytes("print('not bytecode')")); return "no error"; }
                catch (LuaDecompiler.Parser.LuacParseException) { return "rejected"; }
            },
            s => s == "rejected");
    }

    /// <summary>Writes a minimal Lua 5.1 bytecode file (int/size_t = 4 bytes, double numbers).</summary>
    private sealed class ChunkWriter
    {
        private readonly bool _be;
        private readonly List<byte> _b = new List<byte>();

        public ChunkWriter(bool bigEndian) { _be = bigEndian; }

        private void U32(uint v)
        {
            var bytes = BitConverter.GetBytes(v);
            if (BitConverter.IsLittleEndian == _be) Array.Reverse(bytes);
            _b.AddRange(bytes);
        }

        private void F64(double v)
        {
            var bytes = BitConverter.GetBytes(v);
            if (BitConverter.IsLittleEndian == _be) Array.Reverse(bytes);
            _b.AddRange(bytes);
        }

        private void Str(string s)
        {
            if (s == null) { U32(0); return; }
            var bytes = Encoding.UTF8.GetBytes(s);
            U32((uint)bytes.Length + 1);
            _b.AddRange(bytes);
            _b.Add(0);
        }

        public byte[] Main(uint[] code, object[] constants, byte maxStack, bool isVararg = false, (string name, int start, int end)[] locals = null)
        {
            // header: ESC "Lua" 0x51 format endian int size_t instr number integral
            _b.AddRange(new byte[] { 0x1B, (byte)'L', (byte)'u', (byte)'a', 0x51, 0, (byte)(_be ? 0 : 1), 4, 4, 4, 8, 0 });
            Str("@test.lua");
            U32(0); U32(0);                       // line defined / last line defined
            _b.Add(0);                            // upvalues
            _b.Add(0);                            // params
            _b.Add((byte)(isVararg ? 2 : 0));     // is_vararg
            _b.Add(maxStack);
            U32((uint)code.Length);
            foreach (var c in code) U32(c);
            U32((uint)constants.Length);
            foreach (var k in constants)
            {
                switch (k)
                {
                    case null: _b.Add(0); break;
                    case bool bv: _b.Add(1); _b.Add((byte)(bv ? 1 : 0)); break;
                    case double d: _b.Add(3); F64(d); break;
                    case string s: _b.Add(4); Str(s); break;
                }
            }
            U32(0);                               // prototypes
            U32(0);                               // line info
            locals = locals ?? new (string, int, int)[0];
            U32((uint)locals.Length);
            foreach (var (name, start, end) in locals) { Str(name); U32((uint)start); U32((uint)end); }
            U32(0);                               // upvalue names
            return _b.ToArray();
        }
    }

    // ------------------------------------------------------------------ real files

    private static void DecompileRealFiles(string input, string outputDir)
    {
        var files = Directory.Exists(input)
            ? Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories).Where(f => f.EndsWith(".luac", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)).ToList()
            : new List<string> { input };

        Directory.CreateDirectory(outputDir);
        Console.WriteLine();
        Console.WriteLine($"Decompiling {files.Count} file(s) into {Path.GetFullPath(outputDir)}");

        int ok = 0, failed = 0, skipped = 0, stubbed = 0;
        var sw = Stopwatch.StartNew();
        foreach (var file in files)
        {
            byte[] data = File.ReadAllBytes(file);
            if (data.Length < 4 || data[0] != 0x1B)
            {
                skipped++; // plain-text Lua, not bytecode
                continue;
            }
            try
            {
                string source = Luadec.DecompileAsync(data, TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
                string outPath = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(file) + ".lua");
                File.WriteAllText(outPath, source);
                ok++;
                if (source.Contains("-- luadec: could not decompile"))
                {
                    stubbed++;
                    Console.WriteLine($"  PARTIAL {file} (some functions could not be decompiled)");
                }
                if (source.Contains(" r") && System.Text.RegularExpressions.Regex.IsMatch(source, @"\br\d+\b"))
                    Console.WriteLine($"  NOTE    {file} has unresolved registers (r#) - output may be incomplete");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"  FAIL    {file}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        Console.WriteLine($"Done in {sw.Elapsed.TotalSeconds:0.0}s: {ok} decompiled ({stubbed} partial), {failed} failed, {skipped} skipped (not bytecode).");
    }
}

// Ported from luadec-rust (https://github.com/ItsLucas/luadecng-rs), MIT License.
namespace LuaDecompiler.Lua51
{
    /// <summary>Lua 5.1 opcodes, matching the enum order in lopcodes.h.</summary>
    public enum OpCode : byte
    {
        Move = 0, LoadK = 1, LoadBool = 2, LoadNil = 3, GetUpval = 4, GetGlobal = 5, GetTable = 6,
        SetGlobal = 7, SetUpval = 8, SetTable = 9, NewTable = 10, Self_ = 11, Add = 12, Sub = 13,
        Mul = 14, Div = 15, Mod = 16, Pow = 17, Unm = 18, Not = 19, Len = 20, Concat = 21, Jmp = 22,
        Eq = 23, Lt = 24, Le = 25, Test = 26, TestSet = 27, Call = 28, TailCall = 29, Return = 30,
        ForLoop = 31, ForPrep = 32, TForLoop = 33, SetList = 34, Close = 35, Closure = 36, VarArg = 37,
    }

    public enum OpMode { ABC, ABx, AsBx }

    public enum OpArgMask { N, U, R, K }

    public readonly struct OpProp
    {
        public readonly OpMode Mode;
        public readonly OpArgMask BMode;
        public readonly OpArgMask CMode;
        public readonly bool SetsA;
        public readonly bool IsTest;

        public OpProp(bool isTest, bool setsA, OpArgMask b, OpArgMask c, OpMode mode)
        {
            Mode = mode; BMode = b; CMode = c; SetsA = setsA; IsTest = isTest;
        }
    }

    public static class OpCodes
    {
        public const int NumOpcodes = 38;

        public static readonly string[] Names =
        {
            "MOVE", "LOADK", "LOADBOOL", "LOADNIL", "GETUPVAL", "GETGLOBAL", "GETTABLE", "SETGLOBAL",
            "SETUPVAL", "SETTABLE", "NEWTABLE", "SELF", "ADD", "SUB", "MUL", "DIV", "MOD", "POW", "UNM",
            "NOT", "LEN", "CONCAT", "JMP", "EQ", "LT", "LE", "TEST", "TESTSET", "CALL", "TAILCALL",
            "RETURN", "FORLOOP", "FORPREP", "TFORLOOP", "SETLIST", "CLOSE", "CLOSURE", "VARARG",
        };

        private const OpArgMask N = OpArgMask.N, U = OpArgMask.U, R = OpArgMask.R, K = OpArgMask.K;
        private const OpMode ABC = OpMode.ABC, ABx = OpMode.ABx, AsBx = OpMode.AsBx;

        /// <summary>Opcode property table, matching luaP_opmodes in lopcodes.c.</summary>
        public static readonly OpProp[] Props =
        {
            new OpProp(false, true,  R, N, ABC),  // MOVE
            new OpProp(false, true,  K, N, ABx),  // LOADK
            new OpProp(false, true,  U, U, ABC),  // LOADBOOL
            new OpProp(false, true,  R, N, ABC),  // LOADNIL
            new OpProp(false, true,  U, N, ABC),  // GETUPVAL
            new OpProp(false, true,  K, N, ABx),  // GETGLOBAL
            new OpProp(false, true,  R, K, ABC),  // GETTABLE
            new OpProp(false, false, K, N, ABx),  // SETGLOBAL
            new OpProp(false, false, U, N, ABC),  // SETUPVAL
            new OpProp(false, false, K, K, ABC),  // SETTABLE
            new OpProp(false, true,  U, U, ABC),  // NEWTABLE
            new OpProp(false, true,  R, K, ABC),  // SELF
            new OpProp(false, true,  K, K, ABC),  // ADD
            new OpProp(false, true,  K, K, ABC),  // SUB
            new OpProp(false, true,  K, K, ABC),  // MUL
            new OpProp(false, true,  K, K, ABC),  // DIV
            new OpProp(false, true,  K, K, ABC),  // MOD
            new OpProp(false, true,  K, K, ABC),  // POW
            new OpProp(false, true,  R, N, ABC),  // UNM
            new OpProp(false, true,  R, N, ABC),  // NOT
            new OpProp(false, true,  R, N, ABC),  // LEN
            new OpProp(false, true,  R, R, ABC),  // CONCAT
            new OpProp(false, false, R, N, AsBx), // JMP
            new OpProp(true,  false, K, K, ABC),  // EQ
            new OpProp(true,  false, K, K, ABC),  // LT
            new OpProp(true,  false, K, K, ABC),  // LE
            new OpProp(true,  true,  R, U, ABC),  // TEST
            new OpProp(true,  true,  R, U, ABC),  // TESTSET
            new OpProp(false, true,  U, U, ABC),  // CALL
            new OpProp(false, true,  U, U, ABC),  // TAILCALL
            new OpProp(false, false, U, N, ABC),  // RETURN
            new OpProp(false, true,  R, N, AsBx), // FORLOOP
            new OpProp(false, true,  R, N, AsBx), // FORPREP
            new OpProp(true,  false, N, U, ABC),  // TFORLOOP
            new OpProp(false, false, U, U, ABC),  // SETLIST
            new OpProp(false, false, N, N, ABC),  // CLOSE
            new OpProp(false, true,  U, N, ABx),  // CLOSURE
            new OpProp(false, true,  U, N, ABC),  // VARARG
        };

        public static OpProp GetProps(this OpCode op) => Props[(int)op];

        public static string GetName(this OpCode op) => Names[(int)op];
    }
}

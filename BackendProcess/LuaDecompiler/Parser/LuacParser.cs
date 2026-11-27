// Lua 5.1 bytecode reader, ported from metaworm's luac-parser-rs
// (https://github.com/metaworm/luac-parser-rs, MIT License). Only the Lua 5.1 format is
// supported, which is what PlayStation Home uses. Little- and big-endian files both work.
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace LuaDecompiler.Parser
{
    public sealed class LuaHeader
    {
        public byte LuaVersion;
        public byte FormatVersion;
        public bool BigEndian;
        public byte IntSize;
        public byte SizeTSize;
        public byte InstructionSize;
        public byte NumberSize;
        public bool NumberIntegral;
    }

    public enum LuaConstantKind { Null, Bool, Number, String }

    public sealed class LuaConstant
    {
        public LuaConstantKind Kind;
        public bool BoolValue;
        /// <summary>Set when the file stores integer numbers (number_integral).</summary>
        public bool IsInteger;
        public long IntValue;
        public double FloatValue;
        public byte[] StringValue;
    }

    public sealed class LuaLocal
    {
        public string Name = "";
        public ulong StartPc;
        public ulong EndPc;
    }

    public sealed class LuaChunk
    {
        public byte[] Name = Array.Empty<byte>();
        public ulong LineDefined;
        public ulong LastLineDefined;
        public byte NumUpvalues;
        public byte NumParams;
        public byte MaxStack;
        /// <summary>True when the function is vararg (Lua 5.1 VARARG_ISVARARG flag).</summary>
        public bool IsVararg;
        public List<uint> Instructions = new List<uint>();
        public List<LuaConstant> Constants = new List<LuaConstant>();
        public List<LuaChunk> Prototypes = new List<LuaChunk>();
        public List<uint> SourceLines = new List<uint>();
        public List<LuaLocal> Locals = new List<LuaLocal>();
        public List<byte[]> UpvalueNames = new List<byte[]>();
    }

    public sealed class LuaBytecode
    {
        public LuaHeader Header;
        public LuaChunk MainChunk;
    }

    public sealed class LuacParseException : Exception
    {
        public LuacParseException(string message) : base(message) { }
    }

    public static class LuacParser
    {
        public static LuaBytecode Parse(byte[] data)
        {
            var r = new Reader(data);
            var header = ReadHeader(r);
            r.Header = header;
            var main = ReadChunk(r, 0);
            return new LuaBytecode { Header = header, MainChunk = main };
        }

        private static LuaHeader ReadHeader(Reader r)
        {
            if (r.Remaining < 12 || r.U8() != 0x1B || r.U8() != (byte)'L' || r.U8() != (byte)'u' || r.U8() != (byte)'a')
                throw new LuacParseException("Not a Lua bytecode file (missing \\x1BLua signature).");
            byte version = r.U8();
            if (version != 0x51)
                throw new LuacParseException($"Unsupported Lua version 0x{version:X2}; only Lua 5.1 bytecode is supported.");
            var h = new LuaHeader
            {
                LuaVersion = version,
                FormatVersion = r.U8(),
                BigEndian = r.U8() != 1,
                IntSize = r.U8(),
                SizeTSize = r.U8(),
                InstructionSize = r.U8(),
                NumberSize = r.U8(),
                NumberIntegral = r.U8() != 0,
            };
            if (h.InstructionSize != 4)
                throw new LuacParseException($"Unsupported instruction size {h.InstructionSize}.");
            if (h.IntSize != 1 && h.IntSize != 2 && h.IntSize != 4 && h.IntSize != 8)
                throw new LuacParseException($"Unsupported int size {h.IntSize}.");
            if (h.SizeTSize != 1 && h.SizeTSize != 2 && h.SizeTSize != 4 && h.SizeTSize != 8)
                throw new LuacParseException($"Unsupported size_t size {h.SizeTSize}.");
            bool numOk = h.NumberIntegral ? (h.NumberSize == 1 || h.NumberSize == 2 || h.NumberSize == 4 || h.NumberSize == 8)
                                          : (h.NumberSize == 4 || h.NumberSize == 8);
            if (!numOk)
                throw new LuacParseException($"Unsupported number format (size {h.NumberSize}, integral {h.NumberIntegral}).");
            return h;
        }

        private static LuaChunk ReadChunk(Reader r, int depth)
        {
            if (depth > 200)
                throw new LuacParseException("Functions are nested too deeply.");

            var c = new LuaChunk();
            c.Name = r.LuaString();
            c.LineDefined = r.Int();
            c.LastLineDefined = r.Int();
            c.NumUpvalues = r.U8();
            c.NumParams = r.U8();
            byte isVararg = r.U8();
            c.MaxStack = r.U8();
            c.IsVararg = (isVararg & 2) != 0;

            int n = r.Count();
            for (int i = 0; i < n; i++) c.Instructions.Add(r.U32());

            n = r.Count();
            for (int i = 0; i < n; i++)
            {
                byte type = r.U8();
                var k = new LuaConstant();
                switch (type)
                {
                    case 0: k.Kind = LuaConstantKind.Null; break;
                    case 1: k.Kind = LuaConstantKind.Bool; k.BoolValue = r.U8() != 0; break;
                    case 3: k.Kind = LuaConstantKind.Number; r.Number(k); break;
                    case 4: k.Kind = LuaConstantKind.String; k.StringValue = r.LuaString(); break;
                    default: throw new LuacParseException($"Unknown constant type {type} at offset {r.Pos - 1}.");
                }
                c.Constants.Add(k);
            }

            n = r.Count();
            for (int i = 0; i < n; i++) c.Prototypes.Add(ReadChunk(r, depth + 1));

            n = r.Count();
            for (int i = 0; i < n; i++) c.SourceLines.Add((uint)r.Int());

            n = r.Count();
            for (int i = 0; i < n; i++)
            {
                var name = r.LuaString();
                c.Locals.Add(new LuaLocal { Name = Encoding.UTF8.GetString(name), StartPc = r.Int(), EndPc = r.Int() });
            }

            n = r.Count();
            for (int i = 0; i < n; i++) c.UpvalueNames.Add(r.LuaString());

            return c;
        }

        private sealed class Reader
        {
            private readonly byte[] _d;
            public int Pos;
            public LuaHeader Header;

            public Reader(byte[] d) { _d = d; }

            public int Remaining => _d.Length - Pos;

            private ReadOnlySpan<byte> Take(int n)
            {
                if (n < 0 || n > Remaining)
                    throw new LuacParseException($"Unexpected end of file at offset {Pos} (needed {n} more bytes).");
                var s = new ReadOnlySpan<byte>(_d, Pos, n);
                Pos += n;
                return s;
            }

            public byte U8() => Take(1)[0];

            private bool Be => Header != null && Header.BigEndian;

            public uint U32()
            {
                var s = Take(4);
                return Be ? BinaryPrimitives.ReadUInt32BigEndian(s) : BinaryPrimitives.ReadUInt32LittleEndian(s);
            }

            private ulong Unsigned(int size)
            {
                var s = Take(size);
                switch (size)
                {
                    case 1: return s[0];
                    case 2: return Be ? BinaryPrimitives.ReadUInt16BigEndian(s) : BinaryPrimitives.ReadUInt16LittleEndian(s);
                    case 4: return Be ? BinaryPrimitives.ReadUInt32BigEndian(s) : BinaryPrimitives.ReadUInt32LittleEndian(s);
                    default: return Be ? BinaryPrimitives.ReadUInt64BigEndian(s) : BinaryPrimitives.ReadUInt64LittleEndian(s);
                }
            }

            /// <summary>Lua "int" (header.int_size).</summary>
            public ulong Int() => Unsigned(Header.IntSize);

            /// <summary>An element count; guarded so corrupt files fail cleanly instead of allocating huge lists.</summary>
            public int Count()
            {
                ulong v = Int();
                if (v > (ulong)Remaining)
                    throw new LuacParseException($"Corrupt count {v} at offset {Pos}.");
                return (int)v;
            }

            /// <summary>Lua string: size_t length (including trailing NUL), then bytes. NUL is dropped.</summary>
            public byte[] LuaString()
            {
                ulong len = Unsigned(Header.SizeTSize);
                if (len > (ulong)Remaining)
                    throw new LuacParseException($"Corrupt string length {len} at offset {Pos}.");
                var bytes = Take((int)len);
                return bytes.Length == 0 ? Array.Empty<byte>() : bytes.Slice(0, bytes.Length - 1).ToArray();
            }

            public void Number(LuaConstant k)
            {
                int size = Header.NumberSize;
                var s = Take(size);
                if (Header.NumberIntegral)
                {
                    k.IsInteger = true;
                    switch (size)
                    {
                        case 1: k.IntValue = (sbyte)s[0]; break;
                        case 2: k.IntValue = Be ? BinaryPrimitives.ReadInt16BigEndian(s) : BinaryPrimitives.ReadInt16LittleEndian(s); break;
                        case 4: k.IntValue = Be ? BinaryPrimitives.ReadInt32BigEndian(s) : BinaryPrimitives.ReadInt32LittleEndian(s); break;
                        default: k.IntValue = Be ? BinaryPrimitives.ReadInt64BigEndian(s) : BinaryPrimitives.ReadInt64LittleEndian(s); break;
                    }
                }
                else if (size == 8)
                {
                    long bits = Be ? BinaryPrimitives.ReadInt64BigEndian(s) : BinaryPrimitives.ReadInt64LittleEndian(s);
                    k.FloatValue = BitConverter.Int64BitsToDouble(bits);
                }
                else
                {
                    int bits = Be ? BinaryPrimitives.ReadInt32BigEndian(s) : BinaryPrimitives.ReadInt32LittleEndian(s);
                    k.FloatValue = BitConverter.Int32BitsToSingle(bits);
                }
            }
        }
    }
}

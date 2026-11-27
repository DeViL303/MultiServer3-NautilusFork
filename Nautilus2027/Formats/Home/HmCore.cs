#nullable disable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

// PlayStation Home .MDL (HM 0x0101-0x0104) reader / writer and the geometry helpers needed to build one.
// C# port of the HOME_DEV_KIT "hm core" (ws/mdlb/src/00.js). Layout recovered from Hub::HomModelDef::Create (HDK 1.82.09).
// All multi-byte values in an MDL are big-endian; pointers are signed 32-bit offsets relative to where they are stored.
namespace Nautilus2027.Formats.Home
{
    // ------------------------------------------------------------------ small vector types

    public struct V3
    {
        public double X, Y, Z;
        public V3(double x, double y, double z) { X = x; Y = y; Z = z; }

        public double this[int i]
        {
            get => i == 0 ? X : i == 1 ? Y : Z;
            set { if (i == 0) X = value; else if (i == 1) Y = value; else Z = value; }
        }

        public static V3 operator +(V3 a, V3 b) => new V3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static V3 operator -(V3 a, V3 b) => new V3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static V3 operator -(V3 a) => new V3(-a.X, -a.Y, -a.Z);
        public static V3 operator *(V3 a, double s) => new V3(a.X * s, a.Y * s, a.Z * s);
        public static double Dot(V3 a, V3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static V3 Cross(V3 a, V3 b) => new V3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

        /// <summary>Unit vector, or <paramref name="fallback"/> (default +Y) for a zero vector.</summary>
        public V3 Normalized(V3? fallback = null)
        {
            double l = Length;
            return l > 1e-20 ? this * (1 / l) : (fallback ?? new V3(0, 1, 0));
        }

        public override string ToString() => $"({X}, {Y}, {Z})";
    }

    public struct V2
    {
        public double U, V;
        public V2(double u, double v) { U = u; V = v; }
    }

    // ------------------------------------------------------------------ model data

    public sealed class HmField
    {
        public uint Name;
        public byte Offset, Type, Count, Normalised;
    }

    public sealed class HmTexture
    {
        public uint Slot;
        public string Filename = "";
        public uint WrapBits, BorderColour;
        public float LodMin, LodMax, LodBias;
    }

    public sealed class HmMaterial
    {
        public string SourceFile = "";
        public uint Type, Instance;
        public List<(uint Hash, float[] Value)> Constants = new List<(uint, float[])>();
        public List<HmTexture> Textures = new List<HmTexture>();
        public uint Flags;
        public ushort BlendSrc, BlendDst;
        public uint AlphaFunc;
        public float AlphaRef;
        public List<(uint Hash, uint Value)> Extra;   // null = none
        public byte[] Reserved = new byte[16];
    }

    public sealed class HmMesh
    {
        public uint Name, Prim;
        public ushort[] Indices = Array.Empty<ushort>();
        public int VertexCount, Stride;
        public byte[] Vertices = Array.Empty<byte>();
        public List<HmField> Fields = new List<HmField>();
        public int Material = -1;
        public uint Flags;
        public float[] Bound = new float[4];
        public ushort W60 = 0xFFFF, W62;
        /// <summary>True when the mesh header points at skinning / blend shape / cloth data (not rewritten by this code).</summary>
        public bool HasDeformers;
    }

    public sealed class HmModel
    {
        public ushort Version = 0x0103;
        public uint Skeleton, Word8;
        public float[] ModelBound;        // null = none
        public List<HmMaterial> Materials = new List<HmMaterial>();
        public List<HmMesh> Meshes = new List<HmMesh>();
    }

    /// <summary>Skin weights for <see cref="HmCore.BuildMesh"/>: up to 4 joint indices and weights per vertex.</summary>
    public sealed class HmSkin
    {
        public List<int[]> J = new List<int[]>();
        public List<double[]> W = new List<double[]>();
    }

    public sealed class HmBuildOptions
    {
        public ushort Version = 0x0103;
        public uint Prim = HmCore.PrimStrip;
        public string Name = "mesh";
        public uint? NameHash;
        public int Material;
        public HmSkin Skin;
        public List<double[]> Tangents;
    }

    public sealed class HmPreset
    {
        public string Label;
        public uint Type;
        public uint Flags;
        public ushort BlendSrc, BlendDst;
        public float AlphaRef;
        public string[] Slots;
        public (uint Hash, float[] Value)[] Constants;
    }

    public static class HmCore
    {
        // ------------------------------------------------------------------ hashing

        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[n] = c;
            }
            return t;
        }

        /// <summary>Standard zlib crc32.</summary>
        public static uint Crc32(byte[] bytes, int offset = 0, int count = -1)
        {
            if (count < 0) count = bytes.Length - offset;
            uint c = 0xFFFFFFFF;
            for (int i = offset; i < offset + count; i++)
                c = CrcTable[(c ^ bytes[i]) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFF;
        }

        public static byte[] Latin1(string s)
        {
            var b = new byte[s.Length];
            for (int i = 0; i < s.Length; i++) b[i] = (byte)(s[i] & 0xFF);
            return b;
        }

        /// <summary>FwHashedString = ~crc32(name).</summary>
        public static uint HashName(string s) => string.IsNullOrEmpty(s) ? 0u : ~Crc32(Latin1(s));

        /// <summary>"0x1234abcd" is taken as a hash; anything else is hashed.</summary>
        public static uint ParseHash(string v)
        {
            if (v != null && v.Length > 2 && v.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                && uint.TryParse(v.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out uint h))
                return h;
            return HashName(v);
        }

        private static readonly string[] Known =
        {
            "IN.position", "IN.texCoord", "IN.normal", "IN.tangent", "IN.blendWeights", "IN.blendIndices",
            "default", "emissive", "blend", "skin", "hair", "colourMap", "normalMap", "specularMap", "emissiveMap", "envMap", "alphaMap",
            "fresnelPower", "fresnelBias", "fresnelScale", "textureScrollU", "textureScrollV",
        };

        private static readonly Dictionary<uint, string> HashNames = BuildHashNames();

        private static Dictionary<uint, string> BuildHashNames()
        {
            var d = new Dictionary<uint, string>();
            foreach (var n in Known) d[HashName(n)] = n;
            return d;
        }

        /// <summary>Known name for a hash, or "0x........".</summary>
        public static string NameOf(uint h) => HashNames.TryGetValue(h, out var n) ? n : "0x" + h.ToString("x8");

        public static readonly uint HPosition = HashName("IN.position");
        public static readonly uint HTexCoord = HashName("IN.texCoord");
        public static readonly uint HNormal = HashName("IN.normal");
        public static readonly uint HTangent = HashName("IN.tangent");

        // vertex field storage types
        public const byte StShortNorm = 1, StFloat = 2, StHalf = 3, StUByteNorm = 4, StShort = 5, StPacked = 6, StUByte = 7;
        public const uint PrimTriangles = 5, PrimStrip = 6;
        private const int MeshRec = 76, MatRec = 72, TexRec = 24;

        /// <summary>Math.round as JavaScript does it (halves go up).</summary>
        internal static double JsRound(double v) => Math.Floor(v + 0.5);

        // ------------------------------------------------------------------ big-endian helpers

        private static uint U32(byte[] d, int o) => BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(o, 4));
        private static int S32(byte[] d, int o) => BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(o, 4));
        private static ushort U16(byte[] d, int o) => BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(o, 2));
        private static short S16(byte[] d, int o) => BinaryPrimitives.ReadInt16BigEndian(d.AsSpan(o, 2));
        private static float F32(byte[] d, int o) => BitConverter.Int32BitsToSingle(S32(d, o));

        internal static void PutU32(byte[] d, int o, uint v) => BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(o, 4), v);
        internal static void PutS32(byte[] d, int o, int v) => BinaryPrimitives.WriteInt32BigEndian(d.AsSpan(o, 4), v);
        internal static void PutU16(byte[] d, int o, ushort v) => BinaryPrimitives.WriteUInt16BigEndian(d.AsSpan(o, 2), v);
        internal static void PutS16(byte[] d, int o, short v) => BinaryPrimitives.WriteInt16BigEndian(d.AsSpan(o, 2), v);
        internal static void PutF32(byte[] d, int o, float v) => PutS32(d, o, BitConverter.SingleToInt32Bits(v));

        // ------------------------------------------------------------------ reader

        public static bool LooksLikeMdl(byte[] d) => d != null && d.Length >= 0x30 && d[0] == 0x48 && d[1] == 0x4D;

        /// <summary>Parse an HM model. With <paramref name="strict"/>, meshes that use skinning / blend shapes / cloth are rejected.</summary>
        public static HmModel ReadMdl(byte[] d, bool strict = false)
        {
            if (!LooksLikeMdl(d))
                throw new InvalidDataException("not a PlayStation Home HM model");
            try
            {
                return ReadMdlUnchecked(d, strict);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new InvalidDataException("the model is truncated or corrupt (a table points past the end of the file)");
            }
        }

        private static HmModel ReadMdlUnchecked(byte[] d, bool strict)
        {
            int Rel(int o)
            {
                int v = S32(d, o);
                if (v == 0) return 0;
                long t = (long)o + v;
                if (t < 0 || t > d.Length) throw new InvalidDataException($"offset at 0x{o:x} points outside the file");
                return (int)t;
            }
            string CStr(int o)
            {
                int e = o;
                while (e < d.Length && d[e] != 0) e++;
                var sb = new StringBuilder(e - o);
                for (int i = o; i < e; i++) sb.Append((char)d[i]);
                return sb.ToString();
            }

            var m = new HmModel { Version = U16(d, 2), Skeleton = U32(d, 4), Word8 = U32(d, 8) };
            if (m.Version < 0x0101 || m.Version > 0x0104)
                throw new InvalidDataException($"unsupported MDL version 0x{m.Version:x}");
            int nmesh = (int)U32(d, 12), meshTab = Rel(16), nmat = (int)U32(d, 20), matTab = Rel(24);
            if (nmesh < 0 || nmesh > 100000 || nmat < 0 || nmat > 100000)
                throw new InvalidDataException("mesh / material counts are not plausible");
            if (Rel(0x1C) != 0)
            {
                int b = Rel(0x1C);
                m.ModelBound = new[] { F32(d, b), F32(d, b + 4), F32(d, b + 8), F32(d, b + 12) };
            }

            for (int i = 0; i < nmat; i++)
            {
                int b = matTab + i * MatRec;
                var mt = NewMaterial();
                mt.SourceFile = Rel(b) != 0 ? CStr(Rel(b)) : "";
                mt.Type = U32(d, b + 4);
                mt.Instance = U32(d, b + 8);
                int nc = (int)U32(d, b + 12);
                if (nc > 0)
                {
                    int hs = Rel(b + 16), vs = Rel(b + 20);
                    for (int k = 0; k < nc; k++)
                        mt.Constants.Add((U32(d, hs + 4 * k), new[] { F32(d, vs + 16 * k), F32(d, vs + 16 * k + 4), F32(d, vs + 16 * k + 8), F32(d, vs + 16 * k + 12) }));
                }
                int nt = (int)U32(d, b + 24), slots = Rel(b + 28), td = Rel(b + 32);
                for (int t = 0; t < nt; t++)
                {
                    int tb = td + TexRec * t;
                    mt.Textures.Add(new HmTexture
                    {
                        Slot = U32(d, slots + 4 * t),
                        Filename = Rel(tb) != 0 ? CStr(Rel(tb)) : "",
                        WrapBits = U32(d, tb + 4),
                        BorderColour = U32(d, tb + 8),
                        LodMin = F32(d, tb + 12),
                        LodMax = F32(d, tb + 16),
                        LodBias = F32(d, tb + 20),
                    });
                }
                mt.Flags = U32(d, b + 36);
                mt.BlendSrc = U16(d, b + 40);
                mt.BlendDst = U16(d, b + 42);
                mt.AlphaFunc = U32(d, b + 44);
                mt.AlphaRef = F32(d, b + 48);
                int e = Rel(b + 52);
                if (e != 0)
                {
                    mt.Extra = new List<(uint, uint)>();
                    int n = (int)U32(d, e);
                    for (int k = 0; k < n; k++)
                        mt.Extra.Add((U32(d, e + 4 + 8 * k), U32(d, e + 8 + 8 * k)));
                }
                mt.Reserved = d.AsSpan(b + 56, 16).ToArray();
                m.Materials.Add(mt);
            }

            for (int i = 0; i < nmesh; i++)
            {
                int b = meshTab + i * MeshRec;
                int nidx = (int)U32(d, b + 8), io = Rel(b + 12);
                var me = new HmMesh
                {
                    Name = U32(d, b),
                    Prim = U32(d, b + 4),
                    Indices = new ushort[nidx],
                    VertexCount = (int)U32(d, b + 16),
                    Stride = (int)U32(d, b + 20),
                    Flags = U32(d, b + 52),
                    W60 = U16(d, b + 60),
                    W62 = U16(d, b + 62),
                };
                for (int k = 0; k < nidx; k++)
                    me.Indices[k] = U16(d, io + 2 * k);
                int vo = Rel(b + 24);
                me.Vertices = d.AsSpan(vo, me.VertexCount * me.Stride).ToArray();
                int nf = (int)U32(d, b + 28), fo = Rel(b + 32);
                for (int k = 0; k < nf; k++)
                    me.Fields.Add(new HmField { Name = U32(d, fo + 8 * k), Offset = d[fo + 8 * k + 4], Type = d[fo + 8 * k + 5], Count = d[fo + 8 * k + 6], Normalised = d[fo + 8 * k + 7] });
                int mp = Rel(b + 36);
                me.Material = mp != 0 ? (mp - matTab) / MatRec : -1;
                int bp = Rel(b + 56);
                if (bp != 0)
                    me.Bound = new[] { F32(d, bp), F32(d, bp + 4), F32(d, bp + 8), F32(d, bp + 12) };
                foreach (int k in new[] { 40, 44, 48, 64, 68, 72 })
                    if (U32(d, b + k) != 0)
                        me.HasDeformers = true;
                if (strict && me.HasDeformers)
                    throw new InvalidDataException($"mesh {i} uses skinning / blend shapes / cloth (unsupported)");
                m.Meshes.Add(me);
            }
            return m;
        }

        // ------------------------------------------------------------------ writer

        private sealed class Buf
        {
            public byte[] B = new byte[1 << 16];
            public int N;

            private void Grow(int n)
            {
                if (N + n <= B.Length) return;
                int s = B.Length;
                while (N + n > s) s *= 2;
                Array.Resize(ref B, s);
            }

            public int Align(int a)
            {
                while (N % a != 0) { Grow(1); B[N++] = 0; }
                return N;
            }

            public int Put(byte[] bytes, int a = 1)
            {
                int o = Align(a);
                Grow(bytes.Length);
                Buffer.BlockCopy(bytes, 0, B, N, bytes.Length);
                N += bytes.Length;
                return o;
            }

            public int Reserve(int n, int a = 1)
            {
                int o = Align(a);
                Grow(n);
                Array.Clear(B, N, n);
                N += n;
                return o;
            }

            public void U32(int o, uint v) => PutU32(B, o, v);
            public void U16(int o, ushort v) => PutU16(B, o, v);
            public void F32(int o, float v) => PutF32(B, o, v);
            public void Ptr(int at, int target) => PutS32(B, at, target != 0 ? target - at : 0);
            public byte[] Bytes() => B.AsSpan(0, N).ToArray();
        }

        private static byte[] Be32(IEnumerable<uint> vals)
        {
            var list = vals.ToList();
            var o = new byte[list.Count * 4];
            for (int i = 0; i < list.Count; i++) PutU32(o, i * 4, list[i]);
            return o;
        }

        private static byte[] Be32F(IEnumerable<float> vals)
        {
            var list = vals.ToList();
            var o = new byte[list.Count * 4];
            for (int i = 0; i < list.Count; i++) PutF32(o, i * 4, list[i]);
            return o;
        }

        private static byte[] CBytes(string s)
        {
            var b = Latin1(s);
            Array.Resize(ref b, b.Length + 1);
            return b;
        }

        /// <summary>Serialize a model (meshes must not need skinning data beyond blend weights/indices in the vertices).</summary>
        public static byte[] WriteMdl(HmModel m)
        {
            for (int i = 0; i < m.Meshes.Count; i++)
            {
                var me = m.Meshes[i];
                if (!(me.Material >= 0 && me.Material < m.Materials.Count)) throw new InvalidDataException($"mesh {i} bad material");
                if (me.VertexCount > 65536) throw new InvalidDataException($"mesh {i} has too many vertices");
                if (me.Vertices.Length != me.VertexCount * me.Stride) throw new InvalidDataException($"mesh {i} vertex size mismatch");
            }
            var w = new Buf();
            w.Reserve(0x30);
            w.B[0] = 0x48; w.B[1] = 0x4D;
            w.U16(2, m.Version);
            w.U32(4, m.Skeleton);
            w.U32(8, m.Word8);
            w.U32(12, (uint)m.Meshes.Count);
            w.U32(20, (uint)m.Materials.Count);
            int meshTab = w.Reserve(MeshRec * m.Meshes.Count, 16);
            int matTab = w.Reserve(MatRec * m.Materials.Count, 16);
            w.Ptr(16, meshTab);
            w.Ptr(24, matTab);
            for (int i = 0; i < m.Meshes.Count; i++)
                w.Ptr(meshTab + i * MeshRec + 56, w.Put(Be32F(m.Meshes[i].Bound), 16));
            if (m.ModelBound != null)
                w.Ptr(0x1C, w.Put(Be32F(m.ModelBound), 16));
            for (int i = 0; i < m.Meshes.Count; i++)
            {
                var me = m.Meshes[i];
                var fb = new byte[8 * me.Fields.Count];
                for (int k = 0; k < me.Fields.Count; k++)
                {
                    var f = me.Fields[k];
                    PutU32(fb, 8 * k, f.Name);
                    fb[8 * k + 4] = f.Offset; fb[8 * k + 5] = f.Type; fb[8 * k + 6] = f.Count; fb[8 * k + 7] = f.Normalised;
                }
                w.Ptr(meshTab + i * MeshRec + 32, w.Put(fb, 16));
            }
            for (int i = 0; i < m.Materials.Count; i++)
            {
                var mt = m.Materials[i];
                int b = matTab + i * MatRec;
                if (!string.IsNullOrEmpty(mt.SourceFile))
                    w.Ptr(b, w.Put(CBytes(mt.SourceFile), 4));
                w.U32(b + 4, mt.Type);
                w.U32(b + 8, mt.Instance);
                w.U32(b + 12, (uint)mt.Constants.Count);
                if (mt.Constants.Count > 0)
                {
                    w.Ptr(b + 16, w.Put(Be32(mt.Constants.Select(c => c.Hash)), 16));
                    w.Ptr(b + 20, w.Put(Be32F(mt.Constants.SelectMany(c => c.Value.Take(4))), 16));
                }
                w.U32(b + 24, (uint)mt.Textures.Count);
                if (mt.Textures.Count > 0)
                {
                    w.Ptr(b + 28, w.Put(Be32(mt.Textures.Select(t => t.Slot)), 4));
                    int td = w.Reserve(TexRec * mt.Textures.Count, 4);
                    w.Ptr(b + 32, td);
                    for (int k = 0; k < mt.Textures.Count; k++)
                    {
                        var t = mt.Textures[k];
                        int tb = td + TexRec * k;
                        w.U32(tb + 4, t.WrapBits); w.U32(tb + 8, t.BorderColour);
                        w.F32(tb + 12, t.LodMin); w.F32(tb + 16, t.LodMax); w.F32(tb + 20, t.LodBias);
                    }
                    for (int k = 0; k < mt.Textures.Count; k++)
                        if (!string.IsNullOrEmpty(mt.Textures[k].Filename))
                            w.Ptr(td + TexRec * k, w.Put(CBytes(mt.Textures[k].Filename)));
                }
                w.U32(b + 36, mt.Flags);
                w.U16(b + 40, mt.BlendSrc);
                w.U16(b + 42, mt.BlendDst);
                w.U32(b + 44, mt.AlphaFunc);
                w.F32(b + 48, mt.AlphaRef);
                if (mt.Extra != null)
                {
                    var vals = new List<uint> { (uint)mt.Extra.Count };
                    foreach (var e in mt.Extra) { vals.Add(e.Hash); vals.Add(e.Value); }
                    w.Ptr(b + 52, w.Put(Be32(vals), 4));
                }
                if (mt.Reserved != null)
                    Buffer.BlockCopy(mt.Reserved, 0, w.B, b + 56, Math.Min(16, mt.Reserved.Length));
            }
            for (int i = 0; i < m.Meshes.Count; i++)
            {
                var me = m.Meshes[i];
                int b = meshTab + i * MeshRec;
                w.U32(b, me.Name);
                w.U32(b + 4, me.Prim);
                w.U32(b + 8, (uint)me.Indices.Length);
                w.U32(b + 16, (uint)me.VertexCount);
                w.U32(b + 20, (uint)me.Stride);
                w.U32(b + 28, (uint)me.Fields.Count);
                w.Ptr(b + 36, matTab + MatRec * me.Material);
                w.U32(b + 52, me.Flags);
                w.U16(b + 60, me.W60);
                w.U16(b + 62, me.W62);
                if (me.Indices.Length > 0)
                {
                    var ib = new byte[me.Indices.Length * 2];
                    for (int k = 0; k < me.Indices.Length; k++) PutU16(ib, 2 * k, me.Indices[k]);
                    w.Ptr(b + 12, w.Put(ib, 128));
                }
                w.Ptr(b + 24, w.Put(me.Vertices, 128));
            }
            int end = w.Align(16);
            w.Ptr(0x24, end);
            return w.Bytes();
        }

        // ------------------------------------------------------------------ geometry

        /// <summary>Triangles of a mesh (strips are decoded with alternating winding; degenerates dropped).</summary>
        public static List<int[]> DecodeTriangles(ushort[] idx, uint prim)
        {
            var r = new List<int[]>();
            if (prim == PrimTriangles)
            {
                for (int k = 0; k + 2 < idx.Length; k += 3)
                {
                    int a = idx[k], b = idx[k + 1], c = idx[k + 2];
                    if (a != b && b != c && a != c) r.Add(new[] { a, b, c });
                }
                return r;
            }
            for (int k = 0; k + 2 < idx.Length; k++)
            {
                int a = idx[k], b = idx[k + 1], c = idx[k + 2];
                if (a == b || b == c || a == c) continue;
                r.Add(k % 2 == 0 ? new[] { a, b, c } : new[] { b, a, c });
            }
            return r;
        }

        /// <summary>Rotation-independent key of an oriented triangle (smallest index first).</summary>
        private static long Canon(int[] t)
        {
            int a = t[0], b = t[1], c = t[2];
            int m = Math.Min(a, Math.Min(b, c));
            while (a != m) { int x = a; a = b; b = c; c = x; }
            return ((long)a << 40) | ((long)b << 20) | (long)c;
        }

        private static bool Degenerate(int[] t) => t[0] == t[1] || t[1] == t[2] || t[0] == t[2];

        /// <summary>Oriented triangles -> one stitched strip (even triangles CCW, as in shipped assets).</summary>
        public static List<int> Stripify(List<int[]> trisIn)
        {
            var tris = trisIn.Where(t => !Degenerate(t)).ToList();
            var edge = new Dictionary<long, List<int>>();
            long Ek(int a, int b) => (long)a * 1048576 + b;
            void AddEdge(long e, int ti)
            {
                if (!edge.TryGetValue(e, out var l)) edge[e] = l = new List<int>();
                l.Add(ti);
            }
            for (int ti = 0; ti < tris.Count; ti++)
            {
                var t = tris[ti];
                AddEdge(Ek(t[0], t[1]), ti);
                AddEdge(Ek(t[1], t[2]), ti);
                AddEdge(Ek(t[2], t[0]), ti);
            }
            var used = new bool[tris.Count];
            int Third(int ti, int p, int q)
            {
                var t = tris[ti];
                if (t[0] == p && t[1] == q) return t[2];
                if (t[1] == p && t[2] == q) return t[0];
                return t[1];
            }
            int Grow(List<int> seq, bool mark)
            {
                var taken = new HashSet<int>();
                for (; ; )
                {
                    int k = seq.Count - 2, p = seq[seq.Count - 2], q = seq[seq.Count - 1];
                    int n0 = k % 2 == 0 ? p : q, n1 = k % 2 == 0 ? q : p;
                    int nxt = -1;
                    if (edge.TryGetValue(Ek(n0, n1), out var list))
                        foreach (int ti in list)
                            if (!used[ti] && !taken.Contains(ti)) { nxt = ti; break; }
                    if (nxt < 0) break;
                    seq.Add(Third(nxt, n0, n1));
                    taken.Add(nxt);
                }
                if (mark) foreach (int ti in taken) used[ti] = true;
                return taken.Count;
            }
            int Neighbours(int ti)
            {
                var t = tris[ti];
                int n = 0;
                foreach (long e in new[] { Ek(t[1], t[0]), Ek(t[2], t[1]), Ek(t[0], t[2]) })
                    if (edge.TryGetValue(e, out var l))
                        foreach (int x in l) if (x != ti) n++;
                return n;
            }
            var nb = new int[tris.Count];
            for (int i = 0; i < tris.Count; i++) nb[i] = Neighbours(i);
            var seeds = Enumerable.Range(0, tris.Count).OrderBy(i => nb[i]).ThenBy(i => i).ToList();
            var strips = new List<List<int>>();
            foreach (int ti in seeds)
            {
                if (used[ti]) continue;
                var t = tris[ti];
                int bestN = -1;
                int[] bestRot = null;
                foreach (var rot in new[] { new[] { t[0], t[1], t[2] }, new[] { t[1], t[2], t[0] }, new[] { t[2], t[0], t[1] } })
                {
                    used[ti] = true;
                    int n = Grow(new List<int>(rot), false);
                    used[ti] = false;
                    if (bestRot == null || n > bestN) { bestN = n; bestRot = rot; }
                }
                used[ti] = true;
                var seq = new List<int>(bestRot);
                Grow(seq, true);
                strips.Add(seq);
            }
            var outL = new List<int>();
            foreach (var s in strips)
            {
                if (outL.Count > 0)
                {
                    outL.Add(outL[outL.Count - 1]);
                    if (outL.Count % 2 == 0) outL.Add(outL[outL.Count - 1]);
                    outL.Add(s[0]);
                }
                outL.AddRange(s);
            }
            return outL;
        }

        /// <summary>Tangent = dP/du; w = sign(dot(cross(T,N), dP/dv)) -- Home convention (stored in normal.w).</summary>
        public static List<double[]> ComputeTangents(List<V3> P, List<V2> U, List<V3> N, List<int[]> tris)
        {
            int n = P.Count;
            var T = new V3[n];
            var B = new V3[n];
            foreach (var tri in tris)
            {
                int a = tri[0], b = tri[1], c = tri[2];
                V3 e1 = P[b] - P[a], e2 = P[c] - P[a];
                double du1 = U[b].U - U[a].U, dv1 = U[b].V - U[a].V, du2 = U[c].U - U[a].U, dv2 = U[c].V - U[a].V;
                double den = du1 * dv2 - du2 * dv1;
                if (Math.Abs(den) < 1e-20) continue;
                double r = 1 / den;
                V3 t = (e1 * dv2 - e2 * dv1) * r, bt = (e2 * du1 - e1 * du2) * r;
                foreach (int v in tri) { T[v] += t; B[v] += bt; }
            }
            var outL = new List<double[]>(n);
            for (int i = 0; i < n; i++)
            {
                V3 nr = N[i], ti = T[i];
                V3 t = ti - nr * V3.Dot(nr, ti);
                if (t.Length < 1e-12) t = V3.Cross(Math.Abs(nr.X) < 0.9 ? new V3(1, 0, 0) : new V3(0, 1, 0), nr);
                t = t.Normalized();
                double wsign = V3.Dot(V3.Cross(t, nr), B[i]) >= 0 ? 1 : -1;
                outL.Add(new[] { t.X, t.Y, t.Z, wsign });
            }
            return outL;
        }

        // minimum enclosing sphere (incremental Welzl) with Ritter fallback
        private static bool Circ3(V3 a, V3 b, V3 c, out V3 centre, out double radius)
        {
            V3 ab = b - a, ac = c - a, n = V3.Cross(ab, ac);
            double d = 2 * V3.Dot(n, n);
            centre = a; radius = 0;
            if (d < 1e-24) return false;
            V3 o = (V3.Cross(n, ab) * V3.Dot(ac, ac) + V3.Cross(ac, n) * V3.Dot(ab, ab)) * (1 / d);
            centre = a + o;
            radius = o.Length;
            return true;
        }

        private static double Det3(V3 r0, V3 r1, V3 r2) => V3.Dot(r0, V3.Cross(r1, r2));

        private static bool Circ4(V3 a, V3 b, V3 c, V3 e, out V3 centre, out double radius)
        {
            var m = new[] { (b - a) * 2, (c - a) * 2, (e - a) * 2 };
            var r = new[] { V3.Dot(b, b) - V3.Dot(a, a), V3.Dot(c, c) - V3.Dot(a, a), V3.Dot(e, e) - V3.Dot(a, a) };
            double det = Det3(m[0], m[1], m[2]);
            centre = a; radius = 0;
            if (Math.Abs(det) < 1e-24) return false;
            var x = new V3();
            for (int k = 0; k < 3; k++)
            {
                var col = new V3[3];
                for (int i = 0; i < 3; i++) { col[i] = m[i]; col[i][k] = r[i]; }
                x[k] = Det3(col[0], col[1], col[2]) / det;
            }
            centre = x;
            radius = (a - x).Length;
            return true;
        }

        private static bool MinSphere(List<V3> ptsIn, out V3 centre, out double radius)
        {
            var seen = new HashSet<(double, double, double)>();
            var pts = new List<V3>();
            foreach (var p in ptsIn)
                if (seen.Add((p.X, p.Y, p.Z))) pts.Add(p);
            long s = 1234;
            double Rnd() { s = (s * 1103515245 + 12345) % 2147483648; return s / 2147483648.0; }
            for (int i = pts.Count - 1; i > 0; i--)
            {
                int j = (int)Math.Floor(Rnd() * (i + 1));
                (pts[i], pts[j]) = (pts[j], pts[i]);
            }
            const double eps = 1e-7;
            V3 c = pts[0];
            double r = 0;
            bool Out(V3 p) => (p - c).Length > r * (1 + eps) + eps;
            centre = c; radius = 0;
            for (int i = 1; i < pts.Count; i++)
            {
                if (!Out(pts[i])) continue;
                c = pts[i]; r = 0;
                for (int j = 0; j < i; j++)
                {
                    if (!Out(pts[j])) continue;
                    c = (pts[i] + pts[j]) * 0.5;
                    r = (pts[i] - c).Length;
                    for (int k = 0; k < j; k++)
                    {
                        if (!Out(pts[k])) continue;
                        if (!Circ3(pts[i], pts[j], pts[k], out c, out r)) return false;
                        for (int l = 0; l < k; l++)
                        {
                            if (!Out(pts[l])) continue;
                            if (!Circ4(pts[i], pts[j], pts[k], pts[l], out c, out r)) return false;
                        }
                    }
                }
            }
            centre = c; radius = r;
            return true;
        }

        private static void Ritter(List<V3> pts, out V3 centre, out double radius)
        {
            V3 Far(V3 q)
            {
                V3 best = pts[0];
                double bd = V3.Dot(best - q, best - q);
                foreach (var p in pts)
                {
                    double dd = V3.Dot(p - q, p - q);
                    if (dd > bd) { bd = dd; best = p; }
                }
                return best;
            }
            V3 y = Far(pts[0]), z = Far(y);
            V3 c = (y + z) * 0.5;
            double r = (z - y).Length / 2;
            foreach (var p in pts)
            {
                double d = (p - c).Length;
                if (d > r) { double nr = (r + d) / 2; c = c + (p - c) * ((nr - r) / d); r = nr; }
            }
            centre = c; radius = r;
        }

        /// <summary>Bounding sphere [cx, cy, cz, r] that contains every point (slightly padded).</summary>
        public static float[] BoundingSphere(List<V3> pts)
        {
            if (pts.Count == 0) return new float[4];
            V3 c;
            double r;
            bool ok;
            try { ok = MinSphere(pts, out c, out r) && !double.IsNaN(r) && !double.IsInfinity(r); }
            catch (Exception) { ok = false; c = default; r = 0; }
            if (!ok) Ritter(pts, out c, out r);
            r = 0;
            foreach (var p in pts) r = Math.Max(r, (p - c).Length);
            r *= 1 + 1e-5;
            return new[] { (float)c.X, (float)c.Y, (float)c.Z, (float)r };
        }

        private static short Q16(double v) => (short)Math.Max(-32767, Math.Min(32767, JsRound(v * 32767)));

        /// <summary>Build one static element (stride 36: pos f32x3, uv f32x2, tangent s16n x4, normal s16n x4; +8 when skinned).</summary>
        public static HmMesh BuildMesh(List<V3> P, List<V2> U, List<V3> nIn, List<int[]> tris, HmBuildOptions opt)
        {
            opt ??= new HmBuildOptions();
            ushort version = opt.Version;
            uint prim = opt.Prim;
            int n = P.Count;
            if (n > 65536) throw new InvalidDataException("too many vertices for one element");
            var N = nIn.Select(v => v.Normalized()).ToList();
            var tg = opt.Tangents ?? ComputeTangents(P, U, N, tris);
            var skin = opt.Skin;
            int stride = skin != null ? 44 : 36;
            var vb = new byte[n * stride];
            short tanW = (short)(version >= 0x0104 ? 32767 : 0);
            for (int i = 0; i < n; i++)
            {
                int o = i * stride;
                if (skin != null)
                {
                    // Home layout: 3 weights packed 11:11:10 (4th = 1 - sum) + 4 UByte joint indices
                    var wt = skin.W[i];
                    var j = skin.J[i];
                    uint C(double x, int max) => (uint)Math.Max(0, Math.Min(max, JsRound(x * max)));
                    uint packed = (C(wt[0], 1023) & 0x7FF) | ((C(wt[1], 1023) & 0x7FF) << 11) | ((C(wt[2], 511) & 0x3FF) << 22);
                    PutU32(vb, o + 36, packed);
                    for (int k = 0; k < 4; k++) vb[o + 40 + k] = (byte)j[k];
                }
                PutF32(vb, o, (float)P[i].X); PutF32(vb, o + 4, (float)P[i].Y); PutF32(vb, o + 8, (float)P[i].Z);
                PutF32(vb, o + 12, (float)U[i].U); PutF32(vb, o + 16, (float)U[i].V);
                PutS16(vb, o + 20, Q16(tg[i][0])); PutS16(vb, o + 22, Q16(tg[i][1])); PutS16(vb, o + 24, Q16(tg[i][2])); PutS16(vb, o + 26, tanW);
                PutS16(vb, o + 28, Q16(N[i].X)); PutS16(vb, o + 30, Q16(N[i].Y)); PutS16(vb, o + 32, Q16(N[i].Z)); PutS16(vb, o + 34, (short)(tg[i][3] >= 0 ? 32767 : -32767));
            }
            List<int> indices;
            if (prim == PrimStrip)
            {
                indices = Stripify(tris);
                var got = DecodeTriangles(indices.Select(x => (ushort)x).ToArray(), PrimStrip).Select(Canon).OrderBy(x => x).ToList();
                var want = tris.Where(t => !Degenerate(t)).Select(Canon).OrderBy(x => x).ToList();
                if (!got.SequenceEqual(want)) throw new InvalidOperationException("internal: stripifier mismatch");
            }
            else
            {
                indices = new List<int>(tris.Count * 3);
                foreach (var t in tris) indices.AddRange(t);
            }
            var usedPts = new HashSet<int>(indices).Select(i => P[i]).ToList();
            var fields = new List<HmField>
            {
                new HmField { Name = HPosition, Offset = 0, Type = StFloat, Count = 3, Normalised = 0 },
                new HmField { Name = HTexCoord, Offset = 12, Type = StFloat, Count = 2, Normalised = 0 },
                new HmField { Name = HTangent, Offset = 20, Type = StShortNorm, Count = 4, Normalised = 1 },
                new HmField { Name = HNormal, Offset = 28, Type = StShortNorm, Count = 4, Normalised = 1 },
            };
            if (skin != null)
            {
                fields.Add(new HmField { Name = HashName("IN.blendWeights"), Offset = 36, Type = StPacked, Count = 1, Normalised = 1 });
                fields.Add(new HmField { Name = HashName("IN.blendIndices"), Offset = 40, Type = StUByte, Count = 4, Normalised = 0 });
            }
            return new HmMesh
            {
                Name = opt.NameHash ?? HashName(string.IsNullOrEmpty(opt.Name) ? "mesh" : opt.Name),
                Prim = prim,
                Indices = indices.Select(x => (ushort)x).ToArray(),
                VertexCount = n,
                Stride = stride,
                Vertices = vb,
                Fields = fields,
                Material = opt.Material,
                Flags = skin != null ? 1u : 0u,
                Bound = BoundingSphere(usedPts),
                W60 = 0xFFFF,
                W62 = 0,
            };
        }

        /// <summary>Indexed geometry of any size -> elements of at most 65535 vertices.</summary>
        public static List<HmMesh> BuildMeshes(List<V3> P, List<V2> U, List<V3> N, List<int[]> tris, HmBuildOptions opt)
        {
            var outL = new List<HmMesh>();
            int start = 0;
            while (start < tris.Count)
            {
                var remap = new Dictionary<int, int>();
                var p = new List<V3>();
                var u = new List<V2>();
                var nn = new List<V3>();
                var t2 = new List<int[]>();
                var skin = opt?.Skin != null ? new HmSkin() : null;
                int i = start;
                for (; i < tris.Count; i++)
                {
                    var t = tris[i];
                    int fresh = t.Distinct().Count(v => !remap.ContainsKey(v));
                    if (p.Count + fresh > 65535) break;
                    var nt = new int[3];
                    for (int k = 0; k < 3; k++)
                    {
                        int v = t[k];
                        if (!remap.TryGetValue(v, out int r))
                        {
                            r = p.Count;
                            remap[v] = r;
                            p.Add(P[v]); u.Add(U[v]); nn.Add(N[v]);
                            if (skin != null) { skin.J.Add(opt.Skin.J[v]); skin.W.Add(opt.Skin.W[v]); }
                        }
                        nt[k] = r;
                    }
                    t2.Add(nt);
                }
                if (i == start) throw new InvalidDataException("a triangle could not be placed in an element");
                start = i;
                var o = new HmBuildOptions
                {
                    Version = opt?.Version ?? 0x0103,
                    Prim = opt?.Prim ?? PrimStrip,
                    Name = opt?.Name ?? "mesh",
                    NameHash = opt?.NameHash,
                    Material = opt?.Material ?? 0,
                    Skin = skin,
                };
                outL.Add(BuildMesh(p, u, nn, t2, o));
            }
            return outL;
        }

        private static double HalfToDouble(ushort h)
        {
            int s = (h >> 15) & 1, e = (h >> 10) & 31, f = h & 1023;
            double v = e == 0 ? f / 1024.0 * Math.Pow(2, -14) : e == 31 ? (f != 0 ? double.NaN : double.PositiveInfinity) : (1 + f / 1024.0) * Math.Pow(2, e - 15);
            return s != 0 ? -v : v;
        }

        /// <summary>Decode one vertex attribute of a mesh (null if missing or in a packed format).</summary>
        public static List<double[]> MeshAttribute(HmMesh me, uint hash)
        {
            var f = me.Fields.FirstOrDefault(x => x.Name == hash);
            if (f == null) return null;
            var d = me.Vertices;
            var outL = new List<double[]>(me.VertexCount);
            for (int i = 0; i < me.VertexCount; i++)
            {
                int o = i * me.Stride + f.Offset;
                var v = new double[f.Count];
                for (int k = 0; k < f.Count; k++)
                {
                    switch (f.Type)
                    {
                        case StFloat: v[k] = F32(d, o + 4 * k); break;
                        case StShortNorm: v[k] = S16(d, o + 2 * k) / 32767.0; break;
                        case StShort: v[k] = S16(d, o + 2 * k); break;
                        case StHalf: v[k] = HalfToDouble(U16(d, o + 2 * k)); break;
                        case StUByteNorm: v[k] = d[o + k] / 255.0; break;
                        case StUByte: v[k] = d[o + k]; break;
                        default: return null;
                    }
                }
                outL.Add(v);
            }
            return outL;
        }

        public static List<double[]> MeshAttribute(HmMesh me, string name) => MeshAttribute(me, ParseHash(name));

        // ------------------------------------------------------------------ materials

        public static HmMaterial NewMaterial() => new HmMaterial
        {
            SourceFile = "\\config\\materials\\atg_materials.xml",
            Type = HashName("default"),
            Instance = 0,
            Flags = 0xC,
            BlendSrc = 0x0302,
            BlendDst = 0x0303,
            AlphaFunc = 0x0204,
            AlphaRef = 0.75f,
            Extra = null,
            Reserved = new byte[16],
        };

        private const string Generic = "\\generic_shared_textures\\";

        public static readonly Dictionary<string, HmPreset> Presets = new Dictionary<string, HmPreset>
        {
            ["default"] = new HmPreset
            {
                Label = "Opaque (default)", Type = HashName("default"), Flags = 0xC, BlendSrc = 0x0302, BlendDst = 0x0303, AlphaRef = 0.75f,
                Slots = new[] { "colourMap", "normalMap", "specularMap" }, Constants = Array.Empty<(uint, float[])>(),
            },
            ["alphatest"] = new HmPreset
            {
                Label = "Cut-out (alpha test)", Type = HashName("default"), Flags = 0xE, BlendSrc = 0x0302, BlendDst = 0x0303, AlphaRef = 0.5f,
                Slots = new[] { "colourMap", "normalMap", "specularMap" }, Constants = Array.Empty<(uint, float[])>(),
            },
            ["emissive"] = new HmPreset
            {
                Label = "Emissive (additive glow)", Type = HashName("emissive"), Flags = 0xD, BlendSrc = 0x0302, BlendDst = 0x0001, AlphaRef = 0.75f,
                Slots = new[] { "emissiveMap" },
                Constants = new[] { (0xAD9B5E7Du, new float[] { 1, 0, 0, 0 }), (0xF43CBE62u, new float[] { 0.1f, 0, 0, 0 }) },
            },
            ["glass"] = new HmPreset
            {
                Label = "Glass (env + fresnel)", Type = 0x7F05531A, Flags = 0xD, BlendSrc = 0x0001, BlendDst = 0x0303, AlphaRef = 0.75f,
                Slots = new[] { "colourMap", "normalMap", "specularMap", "envMap" },
                Constants = new[]
                {
                    (HashName("fresnelPower"), new float[] { 1, 0, 0, 0 }),
                    (HashName("fresnelBias"), new float[] { 0, 0, 0, 0 }),
                    (HashName("fresnelScale"), new float[] { 1, 0, 0, 0 }),
                },
            },
        };

        public static readonly Dictionary<string, string> DefaultTextures = new Dictionary<string, string>
        {
            ["normalMap"] = Generic + "placeholder_n.dds",
            ["specularMap"] = Generic + "placeholder_s.dds",
            ["envMap"] = Generic + "placeholder_env.dds",
        };

        /// <summary>Material from a preset. <paramref name="textures"/>: slot name -> Home texture path.</summary>
        public static HmMaterial MakeMaterial(string preset, Dictionary<string, string> textures, string name = null,
            double? alphaRef = null, uint borderColour = 0xFF000000, (float U, float V)? scroll = null)
        {
            if (!Presets.TryGetValue(preset, out var p)) throw new ArgumentException("unknown preset " + preset);
            var mt = NewMaterial();
            mt.Type = p.Type;
            mt.Flags = p.Flags;
            mt.BlendSrc = p.BlendSrc;
            mt.BlendDst = p.BlendDst;
            mt.AlphaRef = p.AlphaRef;
            mt.Constants = p.Constants.Select(c => (c.Hash, (float[])c.Value.Clone())).ToList();
            foreach (var slot in p.Slots)
            {
                string path = null;
                if (textures == null || !textures.TryGetValue(slot, out path) || string.IsNullOrEmpty(path))
                    DefaultTextures.TryGetValue(slot, out path);
                if (string.IsNullOrEmpty(path)) throw new InvalidDataException($"preset \"{preset}\" needs a {slot} texture");
                mt.Textures.Add(new HmTexture { Slot = HashName(slot), Filename = path, WrapBits = 0x2849, BorderColour = borderColour, LodMin = 0, LodMax = 15, LodBias = 0 });
            }
            if (!string.IsNullOrEmpty(name)) mt.Instance = HashName(name);
            if (alphaRef.HasValue) mt.AlphaRef = (float)alphaRef.Value;
            if (scroll.HasValue)
            {
                mt.Extra = new List<(uint, uint)>
                {
                    (HashName("textureScrollU"), (uint)BitConverter.SingleToInt32Bits(scroll.Value.U)),
                    (HashName("textureScrollV"), (uint)BitConverter.SingleToInt32Bits(scroll.Value.V)),
                };
            }
            return mt;
        }

        // ------------------------------------------------------------------ verify

        /// <summary>Sanity checks on written MDL bytes; returns the problems found (empty = OK).</summary>
        public static List<string> Verify(byte[] bytes)
        {
            var errs = new List<string>();
            HmModel m;
            try { m = ReadMdl(bytes, false); }
            catch (Exception e) { return new List<string> { "parse error: " + e.Message }; }
            if (bytes.Length % 16 != 0) errs.Add("file size not a multiple of 16");
            int meshTab = 16 + S32(bytes, 16), matTab = 24 + S32(bytes, 24);
            for (int i = 0; i < m.Meshes.Count; i++)
            {
                var me = m.Meshes[i];
                int b = meshTab + MeshRec * i;
                int io = b + 12 + S32(bytes, b + 12), vo = b + 24 + S32(bytes, b + 24), mo = b + 36 + S32(bytes, b + 36);
                if ((mo - matTab) % MatRec != 0 || (mo - matTab) / MatRec >= m.Materials.Count) errs.Add($"mesh {i}: bad material pointer");
                if (io % 16 != 0 || vo % 16 != 0) errs.Add($"mesh {i}: buffers not 16-byte aligned");
                if (me.Indices.Length > 0 && me.Indices.Max() >= me.VertexCount) { errs.Add($"mesh {i}: index out of range"); continue; }
                foreach (var h in new[] { HPosition, HNormal, HTangent, HTexCoord })
                    if (!me.Fields.Any(f => f.Name == h)) errs.Add($"mesh {i}: missing {NameOf(h)}");
                var P = MeshAttribute(me, HPosition);
                var N = MeshAttribute(me, HNormal);
                if (P == null || N == null) continue;
                V3 Pv(int k) => new V3(P[k][0], P[k][1], P[k][2]);
                V3 Nv(int k) => new V3(N[k][0], N[k][1], N[k][2]);
                var c = new V3(me.Bound[0], me.Bound[1], me.Bound[2]);
                double worst = 0;
                foreach (int k in new HashSet<int>(me.Indices.Select(x => (int)x))) worst = Math.Max(worst, (Pv(k) - c).Length);
                if (worst > me.Bound[3] * 1.001 + 1e-6) errs.Add($"mesh {i}: bounding sphere too small");
                if (Enumerable.Range(0, N.Count).Any(k => Math.Abs(Nv(k).Length - 1) > 0.01)) errs.Add($"mesh {i}: non-unit normals");
                var tris = DecodeTriangles(me.Indices, me.Prim);
                if (tris.Count == 0) errs.Add($"mesh {i}: no triangles");
                int ag = 0, tot = 0;
                foreach (var t in tris)
                {
                    V3 fn = V3.Cross(Pv(t[1]) - Pv(t[0]), Pv(t[2]) - Pv(t[0])), sn = Nv(t[0]) + Nv(t[1]) + Nv(t[2]);
                    double lf = fn.Length, ls = sn.Length;
                    if (lf < 1e-12 || ls < 1e-6) continue;
                    double cs = V3.Dot(fn, sn) / (lf * ls);
                    if (Math.Abs(cs) < 0.3) continue;   // faces with bent normals (e.g. grass cards lit as if facing up) do not count
                    tot++;
                    if (cs > 0) ag++;
                }
                if (tot > 0 && ag < 0.5 * tot) errs.Add($"mesh {i}: mostly faces away from its normals (inverted winding?)");
            }
            for (int i = 0; i < m.Materials.Count; i++)
                foreach (var t in m.Materials[i].Textures)
                    if (!t.Filename.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)) errs.Add($"material {i}: texture is not .dds: {t.Filename}");
            return errs;
        }
    }
}

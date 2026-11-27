// Mdl2Hkx.Core - shared helpers: big-endian IO, name hashing, small vector / box / grid types.
using System;
using System.Collections.Generic;
using System.Text;

namespace Mdl2Hkx
{
    /// <summary>Big-endian reads from a byte array (PS3 data).</summary>
    public static class BE
    {
        public static uint U32(byte[] d, int o) => (uint)(d[o] << 24 | d[o + 1] << 16 | d[o + 2] << 8 | d[o + 3]);
        public static int S32(byte[] d, int o) => (int)U32(d, o);
        public static ushort U16(byte[] d, int o) => (ushort)(d[o] << 8 | d[o + 1]);
        public static float F32(byte[] d, int o) => BitConverter.Int32BitsToSingle(S32(d, o));
        public static string CStr(byte[] d, int o)
        {
            int e = o; while (e < d.Length && d[e] != 0) e++;
            return Encoding.Latin1.GetString(d, o, e - o);
        }
        public static void PutU32(byte[] d, int o, uint v) { d[o] = (byte)(v >> 24); d[o + 1] = (byte)(v >> 16); d[o + 2] = (byte)(v >> 8); d[o + 3] = (byte)v; }
        public static void PutF32(byte[] d, int o, float v) => PutU32(d, o, (uint)BitConverter.SingleToInt32Bits(v));
        public static void PutU32(List<byte> d, int o, uint v) { d[o] = (byte)(v >> 24); d[o + 1] = (byte)(v >> 16); d[o + 2] = (byte)(v >> 8); d[o + 3] = (byte)v; }
        public static void AddU32(List<byte> d, uint v) { d.Add((byte)(v >> 24)); d.Add((byte)(v >> 16)); d.Add((byte)(v >> 8)); d.Add((byte)v); }
        public static void AddF32(List<byte> d, float v) => AddU32(d, (uint)BitConverter.SingleToInt32Bits(v));
        public static void PutF32(List<byte> d, int o, float v) => PutU32(d, o, (uint)BitConverter.SingleToInt32Bits(v));
        public static void Pad(List<byte> d, int n = 16) { while (d.Count % n != 0) d.Add(0); }
    }

    /// <summary>Home name hashes: ~crc32(latin1(name)) with the standard zlib CRC-32.</summary>
    public static class HomeHash
    {
        static readonly uint[] Table = MakeTable();
        static uint[] MakeTable()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; t[n] = c; }
            return t;
        }
        public static uint Crc32(byte[] b) { uint c = 0xFFFFFFFF; foreach (var x in b) c = Table[(c ^ x) & 0xFF] ^ (c >> 8); return c ^ 0xFFFFFFFF; }
        public static uint Name(string s) => string.IsNullOrEmpty(s) ? 0 : ~Crc32(Encoding.Latin1.GetBytes(s));
        static readonly string[] Known = { "IN.position", "IN.texCoord", "IN.normal", "IN.tangent", "IN.blendWeights", "IN.blendIndices",
            "default", "emissive", "blend", "skin", "hair", "colourMap", "normalMap", "specularMap", "emissiveMap", "envMap", "alphaMap" };
        static readonly Dictionary<uint, string> Names = BuildNames();
        static Dictionary<uint, string> BuildNames() { var d = new Dictionary<uint, string>(); foreach (var s in Known) d[Name(s)] = s; return d; }
        /// <summary>Readable name for a hash if known, else "0x########".</summary>
        public static string NameOf(uint h) => Names.TryGetValue(h, out var s) ? s : "0x" + h.ToString("x8");
    }

    public struct Vec3
    {
        public double X, Y, Z;
        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public double this[int a] { get => a == 0 ? X : a == 1 ? Y : Z; set { if (a == 0) X = value; else if (a == 1) Y = value; else Z = value; } }
        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator *(Vec3 a, double s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
    }

    /// <summary>Axis-aligned box in world space (metres, Y up).</summary>
    public struct Box
    {
        public Vec3 Min, Max;
        public Box(Vec3 min, Vec3 max) { Min = min; Max = max; }
        public Vec3 Center => (Min + Max) * 0.5;
        public Vec3 HalfExtents => (Max - Min) * 0.5;
        public override string ToString() => $"{Min} .. {Max}";
    }

    /// <summary>Box in voxel indices, half-open: [X0,X1) x [Y0,Y1) x [Z0,Z1).</summary>
    public struct VBox
    {
        public int X0, Y0, Z0, X1, Y1, Z1;
        public VBox(int x0, int y0, int z0, int x1, int y1, int z1) { X0 = x0; Y0 = y0; Z0 = z0; X1 = x1; Y1 = y1; Z1 = z1; }
        public int Lo(int a) => a == 0 ? X0 : a == 1 ? Y0 : Z0;
        public int Hi(int a) => a == 0 ? X1 : a == 1 ? Y1 : Z1;
        public void SetLo(int a, int v) { if (a == 0) X0 = v; else if (a == 1) Y0 = v; else Z0 = v; }
        public void SetHi(int a, int v) { if (a == 0) X1 = v; else if (a == 1) Y1 = v; else Z1 = v; }
        public long Volume => (long)(X1 - X0) * (Y1 - Y0) * (Z1 - Z0);
        public VBox Offset(int dx, int dy, int dz) => new VBox(X0 + dx, Y0 + dy, Z0 + dz, X1 + dx, Y1 + dy, Z1 + dz);
        public override string ToString() => $"[{X0},{Y0},{Z0} .. {X1},{Y1},{Z1})";
    }

    /// <summary>Dense 3-D grid of bytes (0/1 for masks), C order: index = (i*Ny + j)*Nz + k, j is up.</summary>
    public sealed class Grid3
    {
        public readonly int Nx, Ny, Nz; public readonly byte[] D;
        public Grid3(int nx, int ny, int nz) { Nx = nx; Ny = ny; Nz = nz; D = new byte[checked(nx * ny * nz)]; }
        public int Idx(int i, int j, int k) => (i * Ny + j) * Nz + k;
        public bool In(int i, int j, int k) => i >= 0 && j >= 0 && k >= 0 && i < Nx && j < Ny && k < Nz;
        public bool this[int i, int j, int k] { get => D[Idx(i, j, k)] != 0; set => D[Idx(i, j, k)] = (byte)(value ? 1 : 0); }
        public Grid3 Clone() { var g = new Grid3(Nx, Ny, Nz); Buffer.BlockCopy(D, 0, g.D, 0, D.Length); return g; }
        public Grid3 Empty() => new Grid3(Nx, Ny, Nz);
        public long Count() { long c = 0; foreach (var b in D) if (b != 0) c++; return c; }
        public int Dim(int a) => a == 0 ? Nx : a == 1 ? Ny : Nz;
    }

    /// <summary>3-D summed-volume table: count of set voxels in any box in O(1).</summary>
    public sealed class Sat3
    {
        readonly int nx, ny, nz; readonly int[] S;
        public Sat3(Grid3 g) : this(g, 0, 0, 0, g.Nx, g.Ny, g.Nz) { }
        /// <summary>Table over the sub-box [x0,x1) x [y0,y1) x [z0,z1) of g; queries use sub-box local indices.</summary>
        public Sat3(Grid3 g, int x0, int y0, int z0, int x1, int y1, int z1)
        {
            nx = x1 - x0; ny = y1 - y0; nz = z1 - z0; S = new int[checked((nx + 1) * (ny + 1) * (nz + 1))];
            for (int i = 1; i <= nx; i++)
                for (int j = 1; j <= ny; j++)
                    for (int k = 1; k <= nz; k++)
                    {
                        int v = g.D[g.Idx(x0 + i - 1, y0 + j - 1, z0 + k - 1)] != 0 ? 1 : 0;
                        S[I(i, j, k)] = v + S[I(i - 1, j, k)] + S[I(i, j - 1, k)] + S[I(i, j, k - 1)]
                                      - S[I(i - 1, j - 1, k)] - S[I(i - 1, j, k - 1)] - S[I(i, j - 1, k - 1)] + S[I(i - 1, j - 1, k - 1)];
                    }
        }
        int I(int i, int j, int k) => (i * (ny + 1) + j) * (nz + 1) + k;
        public int Count(int x0, int y0, int z0, int x1, int y1, int z1)
        {
            x0 = Math.Clamp(x0, 0, nx); x1 = Math.Clamp(x1, 0, nx); y0 = Math.Clamp(y0, 0, ny); y1 = Math.Clamp(y1, 0, ny); z0 = Math.Clamp(z0, 0, nz); z1 = Math.Clamp(z1, 0, nz);
            if (x1 <= x0 || y1 <= y0 || z1 <= z0) return 0;
            return S[I(x1, y1, z1)] - S[I(x0, y1, z1)] - S[I(x1, y0, z1)] - S[I(x1, y1, z0)] + S[I(x0, y0, z1)] + S[I(x0, y1, z0)] + S[I(x1, y0, z0)] - S[I(x0, y0, z0)];
        }
        public int Count(VBox b) => Count(b.X0, b.Y0, b.Z0, b.X1, b.Y1, b.Z1);
    }
}

// Reader for PlayStation Home .MDL models (HM 0x0101-0x0104), the parts the collision converter needs.
// Layout: docs/02_MDL_FORMAT.md. Big-endian; pointers are signed offsets relative to the pointer field itself.
using System;
using System.Collections.Generic;
using System.IO;

namespace Mdl2Hkx
{
    public sealed class MdlTexture { public uint SlotHash; public string FileName = ""; public string Slot => HomeHash.NameOf(SlotHash);
        public string ShortName { get { var s = FileName.Replace('/', '\\'); int p = s.LastIndexOf('\\'); return p >= 0 ? s[(p + 1)..] : s; } } }

    public sealed class MdlMaterial
    {
        public string SourceFile = ""; public uint TypeHash, InstanceHash, Flags, AlphaFunc; public float AlphaRef;
        public List<MdlTexture> Textures = new();
        public string TypeName => HomeHash.NameOf(TypeHash);          // "emissive", "default", or "0x7f05531a" (glass) ...
        public string InstanceName => HomeHash.NameOf(InstanceHash);
        public const uint GlassType = 0x7F05531A;
    }

    public struct VertexField { public uint NameHash; public byte Offset, Type, Count, Normalised; }

    public sealed class MdlMesh
    {
        public int Index; public uint NameHash, Prim, Flags; public ushort[] Indices = Array.Empty<ushort>();
        public int VertexCount, Stride, MaterialIndex = -1; public byte[] Vertices = Array.Empty<byte>();
        public List<VertexField> Fields = new();
        public bool Skinned;
        public const uint PrimTriangles = 5, PrimStrip = 6;
        const byte TypeFloat = 2;

        /// <summary>World-space positions (metres, Y up). Field "IN.position", else the first float x3 field.</summary>
        public Vec3[] Positions()
        {
            uint hp = HomeHash.Name("IN.position"), hp2 = HomeHash.Name("position");
            VertexField? f = null;
            foreach (var x in Fields) if (x.NameHash == hp || x.NameHash == hp2) { f = x; break; }
            if (f == null) foreach (var x in Fields) if (x.Type == TypeFloat && x.Count == 3) { f = x; break; }
            if (f == null) throw new InvalidDataException($"mesh {Index}: no float3 position field");
            var P = new Vec3[VertexCount];
            for (int i = 0; i < VertexCount; i++)
            {
                int o = i * Stride + f.Value.Offset;
                P[i] = new Vec3(BE.F32(Vertices, o), BE.F32(Vertices, o + 4), BE.F32(Vertices, o + 8));
            }
            return P;
        }

        /// <summary>Triangles as vertex index triples. Lists: every 3 indices. Strips: sliding window, degenerates skipped,
        /// odd triangles flipped to keep the winding.</summary>
        public List<(int a, int b, int c)> Triangles()
        {
            var r = new List<(int, int, int)>(); var I = Indices;
            if (Prim == PrimTriangles)
            {
                for (int k = 0; k + 2 < I.Length; k += 3) { int a = I[k], b = I[k + 1], c = I[k + 2]; if (a != b && b != c && a != c) r.Add((a, b, c)); }
                return r;
            }
            for (int k = 0; k + 2 < I.Length; k++)
            {
                int a = I[k], b = I[k + 1], c = I[k + 2];
                if (a == b || b == c || a == c) continue;
                r.Add(k % 2 == 0 ? (a, b, c) : (b, a, c));
            }
            return r;
        }
    }

    public sealed class MdlModel
    {
        public ushort Version; public List<MdlMesh> Meshes = new(); public List<MdlMaterial> Materials = new();

        const int MeshRec = 76, MatRec = 72, TexRec = 24;

        public static MdlModel Load(string path) => Read(File.ReadAllBytes(path));

        public static MdlModel Read(byte[] d)
        {
            if (d.Length < 0x30 || d[0] != (byte)'H' || d[1] != (byte)'M') throw new InvalidDataException("not a PlayStation Home HM model");
            int Rel(int o) { int v = BE.S32(d, o); if (v == 0) return 0; int t = o + v; if (t < 0 || t > d.Length) throw new InvalidDataException($"pointer at 0x{o:x} outside file"); return t; }
            var m = new MdlModel { Version = BE.U16(d, 2) };
            if (m.Version < 0x0101 || m.Version > 0x0104) throw new InvalidDataException($"unsupported MDL version 0x{m.Version:x}");
            int nmesh = (int)BE.U32(d, 12), meshTab = Rel(16), nmat = (int)BE.U32(d, 20), matTab = Rel(24);
            for (int i = 0; i < nmat; i++)
            {
                int b = matTab + i * MatRec; var mt = new MdlMaterial();
                if (Rel(b) != 0) mt.SourceFile = BE.CStr(d, Rel(b));
                mt.TypeHash = BE.U32(d, b + 4); mt.InstanceHash = BE.U32(d, b + 8);
                int nt = (int)BE.U32(d, b + 24), slots = Rel(b + 28), td = Rel(b + 32);
                for (int t = 0; t < nt; t++)
                {
                    int tb = td + TexRec * t;
                    mt.Textures.Add(new MdlTexture { SlotHash = BE.U32(d, slots + 4 * t), FileName = Rel(tb) != 0 ? BE.CStr(d, Rel(tb)) : "" });
                }
                mt.Flags = BE.U32(d, b + 36); mt.AlphaFunc = BE.U32(d, b + 44); mt.AlphaRef = BE.F32(d, b + 48);
                m.Materials.Add(mt);
            }
            for (int i = 0; i < nmesh; i++)
            {
                int b = meshTab + i * MeshRec;
                var me = new MdlMesh { Index = i, NameHash = BE.U32(d, b), Prim = BE.U32(d, b + 4), VertexCount = (int)BE.U32(d, b + 16), Stride = (int)BE.U32(d, b + 20), Flags = BE.U32(d, b + 52) };
                int nidx = (int)BE.U32(d, b + 8), io = Rel(b + 12);
                me.Indices = new ushort[nidx]; for (int k = 0; k < nidx; k++) me.Indices[k] = BE.U16(d, io + 2 * k);
                int vo = Rel(b + 24); me.Vertices = new byte[me.VertexCount * me.Stride]; Buffer.BlockCopy(d, vo, me.Vertices, 0, me.Vertices.Length);
                int nf = (int)BE.U32(d, b + 28), fo = Rel(b + 32);
                for (int k = 0; k < nf; k++)
                    me.Fields.Add(new VertexField { NameHash = BE.U32(d, fo + 8 * k), Offset = d[fo + 8 * k + 4], Type = d[fo + 8 * k + 5], Count = d[fo + 8 * k + 6], Normalised = d[fo + 8 * k + 7] });
                int mp = Rel(b + 36); me.MaterialIndex = mp != 0 ? (mp - matTab) / MatRec : -1;
                foreach (int k in new[] { 40, 44, 48, 64, 68, 72 }) if (BE.U32(d, b + k) != 0) me.Skinned = true;
                m.Meshes.Add(me);
            }
            return m;
        }
    }

    /// <summary>One triangle in world space plus the mesh it came from.</summary>
    public struct Tri { public Vec3 A, B, C; public int Mesh; }
}

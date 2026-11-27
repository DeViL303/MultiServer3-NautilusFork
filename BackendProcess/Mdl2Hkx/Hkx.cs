// Havok 5 packfile (PS3, big-endian) writer and reader for PlayStation Home collision.
// Format: docs/01_HKX_FORMAT.md. The writer reuses the fixed header / containers of a one-body template HKX
// (template/HKX_TEMPLATE.HKX, exported by the HDK) and appends one rigid body + convex box shape per box.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Mdl2Hkx
{
    public static class HkxWriter
    {
        // offsets inside the template (see docs/01 and template/HKX_TEMPLATE_annotated_dump.txt)
        const int DataStart = 0x6D0;          // absolute file offset of __data__
        const int PrefixSize = 0x500;         // containers we copy unchanged
        const int BodyTpl = 0x500, BodySize = 0x200;
        const int ShapeTpl = 0x720, ShapeSize = 0x60;
        const int TplLocalTable = 0x830, TplLocalEnd = 0x960;
        const int ClsRigidBody = 0xB1, ClsConvexVerticesShape = 0x24C;
        const uint ArrayFlags = 0xC0000000;

        static readonly (int, int)[] PrefixGlobal = { (0x18, 0xB0), (0x28, 0x3A0), (0x38, 0x450), (0x470, 0x480) };
        static readonly (int, int)[] PrefixVirtual = { (0x0, 0x155), (0xB0, 0x5F1), (0x3A0, 0x42C), (0x450, 0x319), (0x480, 0x351) };

        /// <summary>Write boxes (world space, metres, Y up) as a Home collision HKX. Returns the file size.</summary>
        public static int Write(string path, IList<Box> boxes, byte[] template, string namePrefix = "box")
        {
            var bytes = Build(boxes, template, namePrefix); File.WriteAllBytes(path, bytes); return bytes.Length;
        }

        public static byte[] Build(IList<Box> boxes, byte[] T, string namePrefix = "box")
        {
            if (T.Length < DataStart + TplLocalEnd) throw new InvalidDataException("template HKX too small");
            var D = new byte[TplLocalEnd]; Buffer.BlockCopy(T, DataStart, D, 0, D.Length);   // template __data__ incl. its tables
            var data = new List<byte>(PrefixSize + boxes.Count * 1024);
            for (int i = 0; i < PrefixSize; i++) data.Add(D[i]);
            var loc = new List<(int src, int dst)>(); var glo = new List<(int src, int dst)>(); var vir = new List<(int obj, int cls)>();
            // keep the template's local fixups that belong to the prefix, except the rigid-body array pointer (0x488)
            for (int o = TplLocalTable; o + 8 <= TplLocalEnd; o += 8)
            {
                int s = (int)BE.U32(D, o), t = (int)BE.U32(D, o + 4);
                if ((uint)s != 0xFFFFFFFF && s < PrefixSize && s != 0x488) loc.Add((s, t));
            }
            glo.AddRange(PrefixGlobal); vir.AddRange(PrefixVirtual);

            int n = boxes.Count, arr = data.Count;
            loc.Add((0x488, arr)); for (int i = 0; i < 4 * n; i++) data.Add(0); BE.Pad(data);
            BE.PutU32(data, 0x48C, (uint)n); BE.PutU32(data, 0x490, ArrayFlags | (uint)n);   // physics system: rigid body count
            for (int i = 0x4D0; i < 0x4E0; i++) data[i] = 0;                                   // template's old 1-entry array

            for (int i = 0; i < n; i++)
            {
                var bx = boxes[i]; var c = bx.Center; var he = bx.HalfExtents;
                // 8 corners, order x outer, y, z inner (same as the Python writer), relative to the centre
                var lv = new Vec3[8]; int q = 0;
                foreach (var x in new[] { bx.Min.X, bx.Max.X }) foreach (var y in new[] { bx.Min.Y, bx.Max.Y }) foreach (var z in new[] { bx.Min.Z, bx.Max.Z })
                    lv[q++] = new Vec3(x, y, z) - c;

                int body = data.Count;
                for (int k = 0; k < BodySize; k++) data.Add(D[BodyTpl + k]);
                foreach (int o in new[] { 0x110, 0x120, 0x130 })
                { BE.PutF32(data, body + o, (float)c.X); BE.PutF32(data, body + o + 4, (float)c.Y); BE.PutF32(data, body + o + 8, (float)c.Z); }
                BE.PutF32(data, body + 0x180, (float)he.Length);                                  // object radius

                int name = data.Count;
                string nm = namePrefix.Length > 10 ? namePrefix[..10] : namePrefix;
                data.AddRange(Encoding.ASCII.GetBytes($"Collision|{nm}_{i:00000}")); data.Add(0); BE.Pad(data);

                int shp = data.Count;
                for (int k = 0; k < ShapeSize; k++) data.Add(D[ShapeTpl + k]);
                // planes: outward normals, n.p + d = 0, sorted like numpy.unique of the rounded qhull equations
                double r5(double v) => Math.Round(v, 5);
                var planes = new (double, double, double, double)[] {
                    (-1, 0, 0, r5(-he.X)), (0, -1, 0, r5(-he.Y)), (0, 0, -1, r5(-he.Z)),
                    (0, 0, 1, r5(-he.Z)), (0, 1, 0, r5(-he.Y)), (1, 0, 0, r5(-he.X)) };
                int nb = (lv.Length + 3) / 4;
                BE.PutF32(data, shp + 0x20, (float)he.X); BE.PutF32(data, shp + 0x24, (float)he.Y); BE.PutF32(data, shp + 0x28, (float)he.Z); BE.PutF32(data, shp + 0x2C, 0);
                for (int k = 0; k < 4; k++) BE.PutF32(data, shp + 0x30 + 4 * k, 0);
                BE.PutU32(data, shp + 0x44, (uint)nb); BE.PutU32(data, shp + 0x48, ArrayFlags | (uint)nb); BE.PutU32(data, shp + 0x4C, (uint)lv.Length);
                BE.PutU32(data, shp + 0x54, (uint)planes.Length); BE.PutU32(data, shp + 0x58, ArrayFlags | (uint)planes.Length);

                int vo = data.Count;                                                              // FourVectors: x0..x3 y0..y3 z0..z3
                for (int b = 0; b < nb; b++)
                    for (int a = 0; a < 3; a++)
                        for (int k = 0; k < 4; k++) { int vi = Math.Min(b * 4 + k, lv.Length - 1); BE.AddF32(data, (float)lv[vi][a]); }
                int po = data.Count;
                foreach (var p in planes) { BE.AddF32(data, (float)p.Item1); BE.AddF32(data, (float)p.Item2); BE.AddF32(data, (float)p.Item3); BE.AddF32(data, (float)p.Item4); }
                BE.Pad(data);

                loc.Add((body + 0x70, name)); loc.Add((shp + 0x40, vo)); loc.Add((shp + 0x50, po));
                glo.Add((arr + 4 * i, body)); glo.Add((body + 0x10, shp));
                vir.Add((body, ClsRigidBody)); vir.Add((shp, ClsConvexVerticesShape));
            }

            var lt = new List<byte>(); foreach (var (s, t) in loc) { BE.AddU32(lt, (uint)s); BE.AddU32(lt, (uint)t); } PadFF(lt);
            var gt = new List<byte>(); foreach (var (s, t) in glo) { BE.AddU32(gt, (uint)s); BE.AddU32(gt, 1); BE.AddU32(gt, (uint)t); } PadFF(gt);
            var vt = new List<byte>(); foreach (var (o, c) in vir) { BE.AddU32(vt, (uint)o); BE.AddU32(vt, 0); BE.AddU32(vt, (uint)c); } PadFF(vt);
            int o1 = data.Count, o2 = o1 + lt.Count, o3 = o2 + gt.Count, o4 = o3 + vt.Count;

            var outp = new byte[DataStart + o4]; Buffer.BlockCopy(T, 0, outp, 0, DataStart);
            int h = 0x70 + 20; foreach (var v in new[] { DataStart, o1, o2, o3, o4, o4, o4 }) { BE.PutU32(outp, h, (uint)v); h += 4; }
            h = 0xA0 + 20; foreach (var v in new[] { DataStart + o4, 0, 0, 0, 0, 0, 0 }) { BE.PutU32(outp, h, (uint)v); h += 4; }
            data.CopyTo(outp, DataStart); lt.CopyTo(outp, DataStart + o1); gt.CopyTo(outp, DataStart + o2); vt.CopyTo(outp, DataStart + o3);
            return outp;
        }

        static void PadFF(List<byte> b) { while (b.Count % 16 != 0) b.Add(0xFF); }

        /// <summary>Predicted file size for n boxes (template prefix + ~913 bytes per box).</summary>
        public static long EstimateSize(int n) => DataStart + PrefixSize + 913L * n;
    }

    public sealed class HkxBody
    {
        public string Kind = "";                // "convex" or "mesh"
        public string Name = "";
        public Vec3[] Verts = Array.Empty<Vec3>(); // world space (rotation + translation applied)
        public (int, int, int)[] Tris = Array.Empty<(int, int, int)>();
        public (Vec3 n, double d)[] Planes = Array.Empty<(Vec3, double)>();   // convex only, world space, inside: n.p + d <= 0
        public bool Contains(Vec3 p, double eps = 1e-4) { foreach (var (n, d) in Planes) if (n.X * p.X + n.Y * p.Y + n.Z * p.Z + d > eps) return false; return Planes.Length > 0; }
        /// <summary>Triangles for drawing / exporting (outward winding): the mesh's own, or a convex hull's faces from its planes.</summary>
        public List<(int, int, int)> SurfaceTriangles()
        {
            if (Kind == "mesh") return new List<(int, int, int)>(Tris);
            var outp = new List<(int, int, int)>();
            static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
            foreach (var (n, d) in Planes)
            {
                double nl = n.Length; if (nl < 1e-9) continue;
                var nn = n * (1 / nl);
                var on = new List<int>();
                for (int i = 0; i < Verts.Length; i++)
                {
                    var v = Verts[i];
                    if (Math.Abs(Dot(nn, v) + d / nl) > 2e-3) continue;
                    bool dup = false; foreach (int j in on) if ((Verts[j] - v).Length < 1e-6) { dup = true; break; }
                    if (!dup) on.Add(i);
                }
                if (on.Count < 3) continue;
                var c = new Vec3(0, 0, 0); foreach (int i in on) c = c + Verts[i]; c = c * (1.0 / on.Count);
                var u = Vec3.Cross(nn, Math.Abs(nn.X) < 0.9 ? new Vec3(1, 0, 0) : new Vec3(0, 1, 0)); u = u * (1 / u.Length);
                var w = Vec3.Cross(nn, u);
                on.Sort((p, q) => Math.Atan2(Dot(Verts[p] - c, w), Dot(Verts[p] - c, u)).CompareTo(Math.Atan2(Dot(Verts[q] - c, w), Dot(Verts[q] - c, u))));
                for (int k = 1; k + 1 < on.Count; k++)
                {
                    int a = on[0], b = on[k], e = on[k + 1];
                    if (Dot(Vec3.Cross(Verts[b] - Verts[a], Verts[e] - Verts[a]), nn) < 0) (b, e) = (e, b);
                    outp.Add((a, b, e));
                }
            }
            return outp;
        }

        public Box Bounds { get { var a = new Vec3(1e30, 1e30, 1e30); var b = new Vec3(-1e30, -1e30, -1e30);
                foreach (var v in Verts) for (int k = 0; k < 3; k++) { a[k] = Math.Min(a[k], v[k]); b[k] = Math.Max(b[k], v[k]); } return new Box(a, b); } }
    }

    public static class HkxReader
    {
        /// <summary>Every rigid body with a convex-vertices shape or a MOPP -> simple-mesh shape, in world space.</summary>
        public static List<HkxBody> Read(string path) => Read(File.ReadAllBytes(path));

        public static List<HkxBody> Read(byte[] d)
        {
            int CB = (int)BE.U32(d, 0x40 + 20);
            int B0 = (int)BE.U32(d, 0x70 + 20), o1 = (int)BE.U32(d, 0x70 + 24), o2 = (int)BE.U32(d, 0x70 + 28), o3 = (int)BE.U32(d, 0x70 + 32), o4 = (int)BE.U32(d, 0x70 + 36);
            var loc = new Dictionary<int, int>(); var glo = new Dictionary<int, int>(); var cls = new SortedDictionary<int, string>();
            for (int o = o1; o + 8 <= o2; o += 8) { uint s = BE.U32(d, B0 + o); if (s != 0xFFFFFFFF) loc[(int)s] = (int)BE.U32(d, B0 + o + 4); }
            for (int o = o2; o + 12 <= o3; o += 12) { uint s = BE.U32(d, B0 + o); if (s != 0xFFFFFFFF) glo[(int)s] = (int)BE.U32(d, B0 + o + 8); }
            for (int o = o3; o + 12 <= o4; o += 12) { uint s = BE.U32(d, B0 + o); if (s != 0xFFFFFFFF) cls[(int)s] = BE.CStr(d, CB + (int)BE.U32(d, B0 + o + 8)); }
            float F(int o) => BE.F32(d, B0 + o);
            var outp = new List<HkxBody>();
            foreach (var (a, cn) in cls)
            {
                if (cn != "hkpRigidBody") continue;
                // rotation: columns at +0xE0, +0xF0, +0x100; translation +0x110
                var R = new double[3, 3]; for (int col = 0; col < 3; col++) for (int row = 0; row < 3; row++) R[row, col] = F(a + 0xE0 + 0x10 * col + 4 * row);
                double det = R[0, 0] * (R[1, 1] * R[2, 2] - R[1, 2] * R[2, 1]) - R[0, 1] * (R[1, 0] * R[2, 2] - R[1, 2] * R[2, 0]) + R[0, 2] * (R[1, 0] * R[2, 1] - R[1, 1] * R[2, 0]);
                if (double.IsNaN(det) || Math.Abs(det) < .5) { R = new double[3, 3]; R[0, 0] = R[1, 1] = R[2, 2] = 1; }
                var t = new Vec3(F(a + 0x110), F(a + 0x114), F(a + 0x118));
                Vec3 W(Vec3 v) => new Vec3(R[0, 0] * v.X + R[0, 1] * v.Y + R[0, 2] * v.Z + t.X, R[1, 0] * v.X + R[1, 1] * v.Y + R[1, 2] * v.Z + t.Y, R[2, 0] * v.X + R[2, 1] * v.Y + R[2, 2] * v.Z + t.Z);
                if (!glo.TryGetValue(a + 0x10, out int shp)) continue;
                string name = loc.TryGetValue(a + 0x70, out int no) ? BE.CStr(d, B0 + no) : "";
                cls.TryGetValue(shp, out var kind);
                if (kind == "hkpConvexVerticesShape")
                {
                    int src = loc[shp + 0x40]; int nb = (int)BE.U32(d, B0 + shp + 0x44), nv = (int)BE.U32(d, B0 + shp + 0x4C);
                    var V = new List<Vec3>();
                    for (int b = 0; b < nb; b++) for (int k = 0; k < 4; k++)
                        V.Add(new Vec3(F(src + 48 * b + 4 * k), F(src + 48 * b + 16 + 4 * k), F(src + 48 * b + 32 + 4 * k)));
                    var Vw = new Vec3[nv]; for (int k = 0; k < nv; k++) Vw[k] = W(V[k]);
                    // plane equations (local n.p + d = 0) -> world: n' = R n, d' = d - n'.t
                    var planes = new List<(Vec3 n, double d)>();
                    if (loc.TryGetValue(shp + 0x50, out int pp))
                    {
                        int np = (int)BE.U32(d, B0 + shp + 0x54);
                        for (int k = 0; k < np; k++)
                        {
                            var nl = new Vec3(F(pp + 16 * k), F(pp + 16 * k + 4), F(pp + 16 * k + 8)); double dl = F(pp + 16 * k + 12);
                            var nw = new Vec3(R[0, 0] * nl.X + R[0, 1] * nl.Y + R[0, 2] * nl.Z, R[1, 0] * nl.X + R[1, 1] * nl.Y + R[1, 2] * nl.Z, R[2, 0] * nl.X + R[2, 1] * nl.Y + R[2, 2] * nl.Z);
                            planes.Add((nw, dl - (nw.X * t.X + nw.Y * t.Y + nw.Z * t.Z)));
                        }
                    }
                    outp.Add(new HkxBody { Kind = "convex", Name = name, Verts = Vw, Planes = planes.ToArray() });
                }
                else if (kind == "hkpMoppBvTreeShape")
                {
                    if (!glo.TryGetValue(shp + 0x34, out int ms) || !cls.TryGetValue(ms, out var mk) || mk != "hkpSimpleMeshShape") continue;
                    int vp = loc[ms + 0x18], nv = (int)BE.U32(d, B0 + ms + 0x1C), tp = loc[ms + 0x24], nt = (int)BE.U32(d, B0 + ms + 0x28);
                    var Vw = new Vec3[nv]; for (int k = 0; k < nv; k++) Vw[k] = W(new Vec3(F(vp + 16 * k), F(vp + 16 * k + 4), F(vp + 16 * k + 8)));
                    var Tr = new (int, int, int)[nt];
                    for (int k = 0; k < nt; k++) Tr[k] = (BE.S32(d, B0 + tp + 16 * k), BE.S32(d, B0 + tp + 16 * k + 4), BE.S32(d, B0 + tp + 16 * k + 8));
                    outp.Add(new HkxBody { Kind = "mesh", Name = name, Verts = Vw, Tris = Tr });
                }
            }
            return outp;
        }
    }
}

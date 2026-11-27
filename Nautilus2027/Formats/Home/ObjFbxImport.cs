#nullable disable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

// OBJ (+MTL) and binary FBX (6.1-7.7) importers producing the same SourceDoc as the glTF loader.
// C# port of the HOME_DEV_KIT importers (ws/mdlb/src/04.js).
namespace Nautilus2027.Formats.Home
{
    public static class ObjImport
    {
        private static readonly char[] Ws = { ' ', '\t' };

        private static double Num(string[] t, int i) =>
            i < t.Length && double.TryParse(t[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;

        private sealed class ObjPart
        {
            public string Name, Mat;
            public List<V3> P = new List<V3>();
            public List<V3?> N = new List<V3?>();
            public List<V2> U = new List<V2>();
            public List<int[]> Tris = new List<int[]>();
            public Dictionary<string, int> Lut = new Dictionary<string, int>();
            public bool AnyN = true;
        }

        private sealed class MtlEntry
        {
            public string Name;
            public Dictionary<string, string> Tex = new Dictionary<string, string>();
            public double[] Kd = { 1, 1, 1 };
            public double D = 1;
            public bool MapD;
        }

        private static readonly Dictionary<string, int> OptionArgs = new Dictionary<string, int>
        {
            ["-o"] = 4, ["-s"] = 4, ["-t"] = 4, ["-mm"] = 3, ["-bm"] = 2, ["-boost"] = 2, ["-texres"] = 2,
            ["-clamp"] = 2, ["-blendu"] = 2, ["-blendv"] = 2, ["-imfchan"] = 2, ["-cc"] = 2,
        };

        public static SourceDoc Parse(byte[] bytes, FileResolver files)
        {
            string text = Encoding.UTF8.GetString(bytes);
            var v = new List<V3>();
            var vt = new List<V2>();
            var vn = new List<V3>();
            var parts = new Dictionary<string, ObjPart>();
            var order = new List<string>();
            string obj = "object", mat = "";
            var mtlNames = new List<string>();
            int skippedFaces = 0;
            ObjPart Part()
            {
                string k = obj + "\u0000" + mat;
                if (!parts.TryGetValue(k, out var p))
                {
                    p = new ObjPart { Name = obj, Mat = mat };
                    parts[k] = p;
                    order.Add(k);
                }
                return p;
            }
            bool Ix(string s, int n, out int idx)
            {
                idx = -1;
                if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) || i == 0) return false;
                idx = i < 0 ? n + i : i - 1;
                return idx >= 0 && idx < n;
            }

            foreach (var raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                while (line.EndsWith("\\")) line = line.Substring(0, line.Length - 1);
                var t = line.Trim().Split(Ws, StringSplitOptions.RemoveEmptyEntries);
                if (t.Length == 0) continue;
                switch (t[0])
                {
                    case "v": v.Add(new V3(Num(t, 1), Num(t, 2), Num(t, 3))); break;
                    case "vt": vt.Add(new V2(Num(t, 1), t.Length > 2 ? Num(t, 2) : 0)); break;
                    case "vn": vn.Add(new V3(Num(t, 1), Num(t, 2), Num(t, 3))); break;
                    case "o":
                    case "g":
                        if (t.Length > 1) obj = string.Join(" ", t.Skip(1));
                        break;
                    case "usemtl": mat = string.Join(" ", t.Skip(1)); break;
                    case "mtllib":
                        if (t.Length > 1) mtlNames.Add(line.Trim().Substring(t[0].Length).Trim());
                        break;
                    case "f":
                    {
                        var p = Part();
                        var cs = new List<int>();
                        bool bad = false;
                        foreach (var c in t.Skip(1))
                        {
                            var s = c.Split('/');
                            if (!Ix(s[0], v.Count, out int a)) { bad = true; break; }
                            int b = -1, nn = -1;
                            if (s.Length > 1 && s[1].Length > 0 && !Ix(s[1], vt.Count, out b)) b = -1;
                            if (s.Length > 2 && s[2].Length > 0 && !Ix(s[2], vn.Count, out nn)) nn = -1;
                            if (nn < 0) p.AnyN = false;
                            string key = a + "/" + b + "/" + nn;
                            if (!p.Lut.TryGetValue(key, out int vi))
                            {
                                vi = p.P.Count;
                                p.Lut[key] = vi;
                                p.P.Add(v[a]);
                                p.U.Add(b >= 0 ? vt[b] : new V2(0, 0));
                                p.N.Add(nn >= 0 ? vn[nn] : (V3?)null);
                            }
                            cs.Add(vi);
                        }
                        if (bad || cs.Count < 3) { skippedFaces++; break; }
                        for (int k = 1; k + 1 < cs.Count; k++) p.Tris.Add(new[] { cs[0], cs[k], cs[k + 1] });
                        break;
                    }
                }
            }

            // materials
            var img = new ImageTable(files);
            var materials = new List<SourceMaterial>();
            var matIdx = new Dictionary<string, int>();
            var mtl = new Dictionary<string, MtlEntry>();
            foreach (var name in mtlNames)
            {
                byte[] mb = files != null ? files.Find(name, out _, false) : null;
                if (mb == null) { img.Notes.Add("material library not found: " + FileResolver.BaseName(name)); continue; }
                MtlEntry cur = null;
                foreach (var rawLine in Encoding.UTF8.GetString(mb).Split('\n'))
                {
                    var t = rawLine.Trim().Split(Ws, StringSplitOptions.RemoveEmptyEntries);
                    if (t.Length == 0) continue;
                    string k = t[0].ToLowerInvariant();
                    string Arg()
                    {
                        // texture file is the last token after any -options
                        var toks = t.Skip(1).ToArray();
                        int i = 0;
                        while (i < toks.Length - 1 && toks[i].StartsWith("-"))
                            i += OptionArgs.TryGetValue(toks[i], out int na) ? na : 1;
                        return string.Join(" ", toks.Skip(i));
                    }
                    if (k == "newmtl")
                    {
                        cur = new MtlEntry { Name = string.Join(" ", t.Skip(1)) };
                        mtl[cur.Name] = cur;
                    }
                    else if (cur == null) continue;
                    else if (k == "kd") cur.Kd = new[] { Num(t, 1), Num(t, 2), Num(t, 3) };
                    else if (k == "d") cur.D = Num(t, 1);
                    else if (k == "tr") cur.D = 1 - Num(t, 1);
                    else if (k == "map_kd") cur.Tex["colourMap"] = Arg();
                    else if (k == "map_bump" || k == "bump" || k == "norm" || k == "map_norm") cur.Tex["normalMap"] = Arg();
                    else if (k == "map_ks") cur.Tex["specularMap"] = Arg();
                    else if (k == "map_ke") cur.Tex["emissiveMap"] = Arg();
                    else if (k == "map_d") cur.MapD = true;
                }
            }
            int MatFor(string name)
            {
                if (matIdx.TryGetValue(name, out int existing)) return existing;
                if (!mtl.TryGetValue(name, out var m))
                    m = new MtlEntry { Name = string.IsNullOrEmpty(name) ? "default" : name, Kd = new[] { 0.8, 0.8, 0.8 }, D = 1 };
                var textures = new Dictionary<string, int>();
                foreach (var kv in m.Tex)
                {
                    var i = img.Get(kv.Value);
                    if (i.HasValue) textures[kv.Key] = i.Value;
                }
                double d = double.IsNaN(m.D) ? 1 : m.D;
                var kd = m.Kd.Select(x => double.IsNaN(x) ? 1 : x).ToArray();
                materials.Add(new SourceMaterial
                {
                    Index = materials.Count,
                    Name = string.IsNullOrEmpty(m.Name) ? "default" : m.Name,
                    AlphaMode = m.MapD ? "MASK" : d < 0.999 ? "BLEND" : "OPAQUE",
                    AlphaCutoff = 0.5,
                    Textures = textures,
                    BaseColor = new[] { kd[0], kd[1], kd[2], d },
                });
                matIdx[name] = materials.Count - 1;
                return materials.Count - 1;
            }
            var outParts = order.Select(k => parts[k]).Where(p => p.Tris.Count > 0).Select(p => new SourcePart
            {
                Name = p.Name,
                Material = MatFor(p.Mat),
                P = p.P,
                N = p.AnyN ? p.N.Select(x => x.Value).ToList() : null,
                U = p.U,
                Tris = p.Tris,
            }).ToList();
            if (skippedFaces > 0) img.Notes.Add($"{skippedFaces} face(s) with missing or invalid vertex indices were skipped");
            var doc = new SourceDoc { Format = "OBJ", Images = img.Images, Materials = materials, DefaultFlipV = true, Notes = img.Notes };
            doc.Collect = _ => outParts;
            return doc;
        }
    }

    public static partial class FbxImport
    {
        public sealed class FbxNode
        {
            public string Name;
            public List<object> Props = new List<object>();
            public List<FbxNode> Children = new List<FbxNode>();
        }

        private static readonly Encoding Latin = Encoding.Latin1;

        private static byte[] Inflate(byte[] z, int offset, int count)
        {
            using (var src = new MemoryStream(z, offset, count, false))
            using (var zs = new ZLibStream(src, CompressionMode.Decompress))
            using (var dst = new MemoryStream())
            {
                zs.CopyTo(dst);
                return dst.ToArray();
            }
        }

        public static (int Version, List<FbxNode> Top) ReadNodes(byte[] u8)
        {
            if (u8.Length < 27 || Latin.GetString(u8, 0, 18) != "Kaydara FBX Binary")
            {
                if (Latin.GetString(u8, 0, Math.Min(u8.Length, 4096)).Contains("FBXHeaderExtension"))
                    throw new NotSupportedException("ASCII FBX is not supported; re-export it as binary FBX (or use OBJ/GLB)");
                throw new InvalidDataException("not an FBX file");
            }
            int version = (int)BinaryPrimitives.ReadUInt32LittleEndian(u8.AsSpan(23, 4));
            bool big = version >= 7500;
            int depth = 0;

            (FbxNode, int) Node(int o)
            {
                long end = big ? (long)BinaryPrimitives.ReadUInt64LittleEndian(u8.AsSpan(o, 8)) : BinaryPrimitives.ReadUInt32LittleEndian(u8.AsSpan(o, 4));
                long np = big ? (long)BinaryPrimitives.ReadUInt64LittleEndian(u8.AsSpan(o + 8, 8)) : BinaryPrimitives.ReadUInt32LittleEndian(u8.AsSpan(o + 4, 4));
                int hl = big ? 25 : 13;
                int nl = u8[o + hl - 1];
                if (end == 0) return (null, o + hl);
                if (end > u8.Length || end <= o) throw new InvalidDataException("FBX node runs past the end of the file");
                int p = o + hl;
                var node = new FbxNode { Name = Latin.GetString(u8, p, nl) };
                p += nl;
                for (long i = 0; i < np; i++)
                {
                    char t = (char)u8[p++];
                    switch (t)
                    {
                        case 'C': node.Props.Add(u8[p] != 0); p += 1; break;
                        case 'Y': node.Props.Add(BinaryPrimitives.ReadInt16LittleEndian(u8.AsSpan(p, 2))); p += 2; break;
                        case 'I': node.Props.Add(BinaryPrimitives.ReadInt32LittleEndian(u8.AsSpan(p, 4))); p += 4; break;
                        case 'F': node.Props.Add(BinaryPrimitives.ReadSingleLittleEndian(u8.AsSpan(p, 4))); p += 4; break;
                        case 'D': node.Props.Add(BinaryPrimitives.ReadDoubleLittleEndian(u8.AsSpan(p, 8))); p += 8; break;
                        case 'L': node.Props.Add(BinaryPrimitives.ReadInt64LittleEndian(u8.AsSpan(p, 8))); p += 8; break;
                        case 'S':
                        case 'R':
                        {
                            int l = (int)BinaryPrimitives.ReadUInt32LittleEndian(u8.AsSpan(p, 4));
                            if (t == 'S') node.Props.Add(Latin.GetString(u8, p + 4, l));
                            else node.Props.Add(u8.AsSpan(p + 4, l).ToArray());
                            p += 4 + l;
                            break;
                        }
                        case 'f':
                        case 'd':
                        case 'l':
                        case 'i':
                        case 'b':
                        {
                            int n = (int)BinaryPrimitives.ReadUInt32LittleEndian(u8.AsSpan(p, 4));
                            int e = (int)BinaryPrimitives.ReadUInt32LittleEndian(u8.AsSpan(p + 4, 4));
                            int cl = (int)BinaryPrimitives.ReadUInt32LittleEndian(u8.AsSpan(p + 8, 4));
                            byte[] data;
                            int doff;
                            if (e != 0) { data = Inflate(u8, p + 12, cl); doff = 0; }
                            else { data = u8; doff = p + 12; }
                            int sz = t == 'd' || t == 'l' ? 8 : t == 'b' ? 1 : 4;
                            if (doff + (long)n * sz > data.Length) throw new InvalidDataException("FBX array is truncated");
                            var a = new double[n];
                            for (int k = 0; k < n; k++)
                            {
                                int q = doff + k * sz;
                                a[k] = t switch
                                {
                                    'f' => BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(q, 4)),
                                    'd' => BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(q, 8)),
                                    'l' => BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(q, 8)),
                                    'i' => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(q, 4)),
                                    _ => data[q],
                                };
                            }
                            node.Props.Add(a);
                            p += 12 + cl;
                            break;
                        }
                        default:
                            throw new InvalidDataException($"unknown FBX property type '{t}'");
                    }
                }
                if (++depth > 256) throw new InvalidDataException("FBX nodes are nested too deeply");
                while (p < end)
                {
                    var (c, np2) = Node(p);
                    p = np2;
                    if (c == null) break;
                    node.Children.Add(c);
                }
                depth--;
                return (node, (int)end);
            }

            var top = new List<FbxNode>();
            int pos = 27;
            while (pos < u8.Length - 13)
            {
                var (n, next) = Node(pos);
                pos = next;
                if (n == null) break;
                top.Add(n);
            }
            return (version, top);
        }

        private static FbxNode Child(FbxNode n, string name) => n?.Children.FirstOrDefault(c => c.Name == name);

        private static object ChildVal(FbxNode n, string name, object def)
        {
            var c = Child(n, name);
            return c != null && c.Props.Count > 0 ? c.Props[0] : def;
        }

        internal static double Dbl(object o, double def = 0)
        {
            switch (o)
            {
                case double d: return d;
                case float f: return f;
                case int i: return i;
                case long l: return l;
                case short s: return s;
                case bool b: return b ? 1 : 0;
                case string str: return double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : def;
                default: return def;
            }
        }

        private static string Key(object o) => o switch
        {
            long l => l.ToString(CultureInfo.InvariantCulture),
            int i => i.ToString(CultureInfo.InvariantCulture),
            null => "",
            _ => Convert.ToString(o, CultureInfo.InvariantCulture),
        };

        private static string Text(object o) => o as string ?? "";

        private static Dictionary<string, List<object>> Props70(FbxNode n)
        {
            var outD = new Dictionary<string, List<object>>();
            var p = Child(n, "Properties70") ?? Child(n, "Properties60");
            if (p != null)
                foreach (var c in p.Children)
                    if ((c.Name == "P" || c.Name == "Property") && c.Props.Count > 0)
                        outD[Text(c.Props[0])] = c.Props.Skip(c.Name == "P" ? 4 : 3).ToList();
            return outD;
        }

        private static string ObjName(object s)
        {
            string str = Text(s).Split('\u0000')[0];
            return Regex.Replace(str, "^(Model|Material|Geometry|Texture|Video)::", "");
        }

        // 4x4 column-major helpers
        private static double[] T(double[] v) { var m = Mat4.Identity(); m[12] = v[0]; m[13] = v[1]; m[14] = v[2]; return m; }
        private static double[] Sc(double[] v) { var m = Mat4.Identity(); m[0] = v[0]; m[5] = v[1]; m[10] = v[2]; return m; }
        private static double[] Neg(double[] v) => v.Select(x => -x).ToArray();

        private static double[] Rxyz(double[] deg, int order)
        {
            double x = deg[0] * Math.PI / 180, y = deg[1] * Math.PI / 180, z = deg[2] * Math.PI / 180;
            var rx = new[] { 1, 0, 0, 0, 0, Math.Cos(x), Math.Sin(x), 0, 0, -Math.Sin(x), Math.Cos(x), 0, 0, 0, 0, 1 };
            var ry = new[] { Math.Cos(y), 0, -Math.Sin(y), 0, 0, 1, 0, 0, Math.Sin(y), 0, Math.Cos(y), 0, 0, 0, 0, 1 };
            var rz = new[] { Math.Cos(z), Math.Sin(z), 0, 0, -Math.Sin(z), Math.Cos(z), 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
            var orders = new[] { "XYZ", "XZY", "YZX", "YXZ", "ZXY", "ZYX" };
            string ord = order >= 0 && order < orders.Length ? orders[order] : "XYZ";   // first axis applied first
            double[] R(char a) => a == 'X' ? rx : a == 'Y' ? ry : rz;
            return Mat4.Mul(R(ord[2]), Mat4.Mul(R(ord[1]), R(ord[0])));
        }

        private static double[] InvRot(double[] deg)
        {
            var r = Rxyz(deg, 0);
            return new[] { r[0], r[4], r[8], 0, r[1], r[5], r[9], 0, r[2], r[6], r[10], 0, 0, 0, 0, 1 };
        }

        private static string SlotOf(string p)
        {
            string s = (p ?? "").ToLowerInvariant();
            if (Regex.IsMatch(s, "normal|bump")) return "normalMap";
            if (Regex.IsMatch(s, "specular|reflection|metal|rough|gloss")) return "specularMap";
            if (Regex.IsMatch(s, "emissive|emission|incandescence")) return "emissiveMap";
            if (Regex.IsMatch(s, "transparen|opacity")) return "alpha";
            if (Regex.IsMatch(s, "diffuse|base|albedo|color|colour")) return "colourMap";
            return null;
        }

        private sealed class Layer
        {
            public string Map, Ref;
            public double[] Data, Index;
        }

        private sealed class Link
        {
            public string Id, Type, Prop;
        }

        public static SourceDoc Parse(byte[] bytes, FileResolver files)
        {
            var (version, topList) = ReadNodes(bytes);
            var top = new Dictionary<string, FbxNode>();
            foreach (var n in topList) if (!top.ContainsKey(n.Name)) top[n.Name] = n;
            top.TryGetValue("Objects", out var objects);
            top.TryGetValue("Connections", out var conns);
            if (objects == null || conns == null) throw new InvalidDataException("FBX has no Objects/Connections (FBX before 6.0?)");

            // axis system + units -> Y-up metres
            top.TryGetValue("GlobalSettings", out var globalSettings);
            var gs = Props70(globalSettings);
            double G(string k, double d) => gs.TryGetValue(k, out var l) && l.Count > 0 ? Dbl(l[0], d) : d;
            double[] AxisVec(double a, double s)
            {
                var v = new double[3];
                int ai = (int)a;
                if (ai >= 0 && ai < 3) v[ai] = s;
                return v;
            }
            var up = AxisVec(G("UpAxis", 1), G("UpAxisSign", 1));
            var front = AxisVec(G("FrontAxis", 2), G("FrontAxisSign", 1));
            var right = AxisVec(G("CoordAxis", 0), G("CoordAxisSign", 1));
            double unit = G("UnitScaleFactor", 1) / 100;
            // world <- file: rows are right/up/front expressed in file coords
            var AX = new[] { right[0], up[0], front[0], 0, right[1], up[1], front[1], 0, right[2], up[2], front[2], 0, 0, 0, 0, 1 };
            for (int i = 0; i < 12; i++) if (i % 4 < 3) AX[i] *= unit;

            var byId = new Dictionary<string, FbxNode>();
            foreach (var o in objects.Children)
                if (o.Props.Count > 0) byId[Key(o.Props[0])] = o;
            var parentsOf = new Dictionary<string, List<Link>>();
            var childrenOf = new Dictionary<string, List<Link>>();
            foreach (var c in conns.Children)
            {
                if (c.Props.Count < 3) continue;
                string type = Text(c.Props[0]), k = Key(c.Props[1]), pk = Key(c.Props[2]), prop = c.Props.Count > 3 ? Text(c.Props[3]) : null;
                if (!childrenOf.TryGetValue(pk, out var cl)) childrenOf[pk] = cl = new List<Link>();
                cl.Add(new Link { Id = k, Type = type, Prop = prop });
                if (!parentsOf.TryGetValue(k, out var pl)) parentsOf[k] = pl = new List<Link>();
                pl.Add(new Link { Id = pk, Type = type, Prop = prop });
            }
            List<(FbxNode Node, string Prop)> Kids(object id, string name)
            {
                var outL = new List<(FbxNode, string)>();
                if (childrenOf.TryGetValue(Key(id), out var l))
                    foreach (var c in l)
                        if (byId.TryGetValue(c.Id, out var node) && (name == null || node.Name == name))
                            outL.Add((node, c.Prop));
                return outL;
            }

            // node transforms
            var localCache = new Dictionary<string, double[]>();
            double[] Local(FbxNode model)
            {
                var pr = Props70(model);
                double[] V(string k, double[] d) =>
                    pr.TryGetValue(k, out var l) && l.Count >= 3 ? new[] { Dbl(l[0]), Dbl(l[1]), Dbl(l[2]) } : d;
                int ord = pr.TryGetValue("RotationOrder", out var ro) && ro.Count > 0 ? (int)Dbl(ro[0]) : 0;
                var zero = new double[] { 0, 0, 0 };
                var one = new double[] { 1, 1, 1 };
                var rotPivot = V("RotationPivot", zero);
                var scalePivot = V("ScalingPivot", zero);
                return Mat4.Mul(T(V("Lcl Translation", zero)), Mat4.Mul(T(V("RotationOffset", zero)), Mat4.Mul(T(rotPivot),
                    Mat4.Mul(Rxyz(V("PreRotation", zero), 0), Mat4.Mul(Rxyz(V("Lcl Rotation", zero), ord), Mat4.Mul(InvRot(V("PostRotation", zero)),
                        Mat4.Mul(T(Neg(rotPivot)), Mat4.Mul(T(V("ScalingOffset", zero)), Mat4.Mul(T(scalePivot), Mat4.Mul(Sc(V("Lcl Scaling", one)), T(Neg(scalePivot))))))))))));
            }
            double[] World(FbxNode model, string mode, int depth = 0)
            {
                string key = Key(model.Props[0]) + mode;
                if (localCache.TryGetValue(key, out var cached)) return cached;
                var L = mode == "none" ? Mat4.Identity() : Local(model);
                if (mode == "noscale")
                    for (int c = 0; c < 3; c++)
                    {
                        double l = Math.Sqrt(L[4 * c] * L[4 * c] + L[4 * c + 1] * L[4 * c + 1] + L[4 * c + 2] * L[4 * c + 2]);
                        if (l == 0) l = 1;
                        for (int r = 0; r < 3; r++) L[4 * c + r] /= l;
                    }
                FbxNode par = null;
                if (depth < 64 && parentsOf.TryGetValue(Key(model.Props[0]), out var pl))
                    foreach (var p in pl)
                        if (byId.TryGetValue(p.Id, out var pn) && pn.Name == "Model") { par = pn; break; }
                var W = par != null ? Mat4.Mul(World(par, mode, depth + 1), L) : L;
                localCache[key] = W;
                return W;
            }
            double[] Geometric(FbxNode model)
            {
                var pr = Props70(model);
                double[] V(string k, double[] d) =>
                    pr.TryGetValue(k, out var l) && l.Count >= 3 ? new[] { Dbl(l[0]), Dbl(l[1]), Dbl(l[2]) } : d;
                return Mat4.Mul(T(V("GeometricTranslation", new double[] { 0, 0, 0 })), Mat4.Mul(Rxyz(V("GeometricRotation", new double[] { 0, 0, 0 }), 0), Sc(V("GeometricScaling", new double[] { 1, 1, 1 }))));
            }

            // materials + textures
            var img = new ImageTable(files);
            var materials = new List<SourceMaterial>();
            var matIndex = new Dictionary<string, int>();
            var texCache = new Dictionary<string, int?>();
            int? TexImage(FbxNode tex)
            {
                string tk = Key(tex.Props[0]);
                if (texCache.TryGetValue(tk, out var r)) return r;
                var vids = Kids(tex.Props[0], "Video");
                FbxNode vid = vids.Count > 0 ? vids[0].Node : null;
                var content = Child(vid, "Content");
                string fname = Text(ChildVal(tex, "RelativeFilename", null));
                if (fname.Length == 0) fname = Text(ChildVal(tex, "FileName", null));
                if (fname.Length == 0 && vid != null) fname = Text(ChildVal(vid, "RelativeFilename", null));
                if (fname.Length == 0 && vid != null) fname = Text(ChildVal(vid, "Filename", null));
                if (content != null && content.Props.Count > 0 && content.Props[0] is byte[] embedded && embedded.Length > 0)
                {
                    string b = FileResolver.BaseName(fname);
                    r = img.Embedded(b.Length > 0 ? b : ObjName(tex.Props.Count > 1 ? tex.Props[1] : "") + ".png", embedded);
                }
                else r = img.Get(fname);
                texCache[tk] = r;
                return r;
            }
            int Material(FbxNode mnode)
            {
                string id = Key(mnode.Props[0]);
                if (matIndex.TryGetValue(id, out int existing)) return existing;
                var pr = Props70(mnode);
                var textures = new Dictionary<string, int>();
                bool alpha = false;
                foreach (var (node, prop) in Kids(mnode.Props[0], "Texture"))
                {
                    string slot = SlotOf(string.IsNullOrEmpty(prop) ? "DiffuseColor" : prop);
                    if (slot == "alpha") { alpha = true; continue; }
                    if (slot == null || textures.ContainsKey(slot)) continue;
                    var i = TexImage(node);
                    if (i.HasValue) textures[slot] = i.Value;
                }
                List<object> dcl = pr.TryGetValue("DiffuseColor", out var d1) ? d1 : pr.TryGetValue("Diffuse", out var d2) ? d2 : null;
                var dc = dcl != null && dcl.Count >= 3 ? new[] { Dbl(dcl[0]), Dbl(dcl[1]), Dbl(dcl[2]) } : new[] { 0.8, 0.8, 0.8 };
                double op = pr.TryGetValue("Opacity", out var opl) && opl.Count > 0 ? Dbl(opl[0], 1) : 1;
                string nm = ObjName(mnode.Props.Count > 1 ? mnode.Props[1] : "");
                materials.Add(new SourceMaterial
                {
                    Index = materials.Count,
                    Name = nm.Length > 0 ? nm : "material",
                    AlphaMode = alpha ? "MASK" : op < 0.999 ? "BLEND" : "OPAQUE",
                    AlphaCutoff = 0.5,
                    Textures = textures,
                    BaseColor = new[] { dc[0], dc[1], dc[2], op },
                    HomeJson = UserStr(pr, "home_material"),
                });
                matIndex[id] = materials.Count - 1;
                return materials.Count - 1;
            }
            int fallbackMat = -1;
            int DefaultMat()
            {
                if (fallbackMat < 0)
                {
                    materials.Add(new SourceMaterial { Index = materials.Count, Name = "default", BaseColor = new[] { 0.8, 0.8, 0.8, 1 } });
                    fallbackMat = materials.Count - 1;
                }
                return fallbackMat;
            }

            // geometry layers
            Layer GetLayer(FbxNode geo, string name, string dataName, string idxName)
            {
                var l = Child(geo, name);
                if (l == null) return null;
                return new Layer
                {
                    Map = Text(ChildVal(l, "MappingInformationType", "ByPolygonVertex")),
                    Ref = Text(ChildVal(l, "ReferenceInformationType", "Direct")),
                    Data = ChildVal(l, dataName, null) as double[] ?? Array.Empty<double>(),
                    Index = idxName != null ? ChildVal(l, idxName, null) as double[] : null,
                };
            }
            var models = objects.Children.Where(o => o.Name == "Model" && o.Props.Count > 0).ToList();
            var meshModels = new List<(FbxNode M, FbxNode Geo)>();
            foreach (var m in models)
            {
                FbxNode geo = Kids(m.Props[0], "Geometry").Select(k => k.Node)
                    .FirstOrDefault(gn => (gn.Props.Count > 2 && Text(gn.Props[2]).Contains("Mesh")) || Child(gn, "Vertices") != null);
                if (geo == null && Child(m, "Vertices") != null) geo = m;   // FBX 6: geometry stored on the model itself
                if (geo != null) meshModels.Add((m, geo));
            }
            if (meshModels.Count == 0) throw new InvalidDataException("FBX contains no mesh geometry");
            // preload materials so indices are stable
            var modelMats = new Dictionary<FbxNode, List<int>>();
            foreach (var (m, _) in meshModels)
                modelMats[m] = Kids(m.Props[0], "Material").Select(k => Material(k.Node)).ToList();
            // every texture's image, so Home data can find textures in slots FBX has no link for (lightmaps etc.)
            foreach (var tex in objects.Children.Where(o => o.Name == "Texture" && o.Props.Count > 0)) TexImage(tex);
            // skin weights (joint numbers in the order of the skeleton made from this file)
            var skinNotes = new List<string>();
            var skins = ReadSkins(bytes, objects, conns, out uint skeletonKey, out int skeletonJoints, skinNotes);

            string F4(double x) => x.ToString("F4", CultureInfo.InvariantCulture);
            string F5(double x) => x.ToString("F5", CultureInfo.InvariantCulture);

            List<SourcePart> Collect(string mode)
            {
                localCache.Clear();
                var parts = new List<SourcePart>();
                foreach (var (m, geo) in meshModels)
                {
                    var M = Mat4.Mul(AX, Mat4.Mul(World(m, mode), Geometric(m)));
                    bool flip = Mat4.Det3(M) < 0;
                    var NT = Mat4.Inv3T(M);
                    var V = ChildVal(geo, "Vertices", null) as double[] ?? Array.Empty<double>();
                    var PVI = ChildVal(geo, "PolygonVertexIndex", null) as double[] ?? Array.Empty<double>();
                    var nL = GetLayer(geo, "LayerElementNormal", "Normals", "NormalsIndex");
                    var uL = GetLayer(geo, "LayerElementUV", "UV", "UVIndex");
                    var mL = GetLayer(geo, "LayerElementMaterial", "Materials", null);
                    double[] Pick(Layer L, int comps, int cornerIdx, int ctrl, int poly)
                    {
                        if (L == null || L.Data.Length == 0) return null;
                        bool byV = L.Map.IndexOf("Vert", StringComparison.OrdinalIgnoreCase) >= 0 && L.Map.IndexOf("Polygon", StringComparison.OrdinalIgnoreCase) < 0;
                        bool byPoly = L.Map == "ByPolygon", same = L.Map == "AllSame";
                        int i = same ? 0 : byPoly ? poly : byV ? ctrl : cornerIdx;
                        if (L.Ref.Contains("Index") && L.Index != null)
                        {
                            if (i < 0 || i >= L.Index.Length) return null;
                            i = (int)L.Index[i];
                        }
                        if (i < 0 || (i + 1) * comps > L.Data.Length) return null;
                        var r = new double[comps];
                        Array.Copy(L.Data, i * comps, r, 0, comps);
                        return r;
                    }
                    var mats = modelMats[m];
                    string partName0 = ObjName(m.Props.Count > 1 ? m.Props[1] : "");
                    skins.TryGetValue(Key(geo.Props[0]), out var geoSkin);
                    string homeMesh = UserStr(Props70(m), "home_mesh");
                    if (homeMesh != null && mats.Count <= 1)
                    {
                        var exact = ExactPart(geo, M, NT, flip, partName0, mats.Count > 0 ? mats[0] : DefaultMat(), geoSkin);
                        if (exact != null)
                        {
                            exact.HomeJson = homeMesh;
                            parts.Add(exact);
                            continue;
                        }
                    }
                    var perMat = new Dictionary<int, (SourcePart Part, List<V3?> N, Dictionary<string, int> Lut, List<int> Ctrl)>();
                    var order = new List<int>();
                    var poly = new List<(int K, int Ci)>();
                    int polyIdx = 0;
                    string partName = ObjName(m.Props.Count > 1 ? m.Props[1] : "");
                    for (int k = 0; k < PVI.Length; k++)
                    {
                        int ci = (int)PVI[k];
                        bool last = ci < 0;
                        if (last) ci = ~ci;
                        poly.Add((k, ci));
                        if (!last) continue;
                        int slot = 0;
                        if (mL != null && mL.Data.Length > 0)
                            slot = mL.Map == "AllSame" ? (int)mL.Data[0] : polyIdx < mL.Data.Length ? (int)mL.Data[polyIdx] : 0;
                        int mi = mats.Count > 0 ? (slot >= 0 && slot < mats.Count ? mats[slot] : mats[0]) : DefaultMat();
                        if (!perMat.TryGetValue(mi, out var entry))
                        {
                            entry = (new SourcePart { Name = partName, Material = mi }, new List<V3?>(), new Dictionary<string, int>(), new List<int>());
                            perMat[mi] = entry;
                            order.Add(mi);
                        }
                        var ids = new List<int>(poly.Count);
                        foreach (var (kk, c) in poly)
                        {
                            if (3 * c + 2 >= V.Length || c < 0) { ids = null; break; }
                            var n = Pick(nL, 3, kk, c, polyIdx);
                            var uv = Pick(uL, 2, kk, c, polyIdx) ?? new double[] { 0, 0 };
                            string key = c + "|" + (n != null ? F4(n[0]) + "," + F4(n[1]) + "," + F4(n[2]) : "") + "|" + F5(uv[0]) + "," + F5(uv[1]);
                            if (!entry.Lut.TryGetValue(key, out int vi))
                            {
                                vi = entry.Part.P.Count;
                                entry.Lut[key] = vi;
                                entry.Part.P.Add(Mat4.Point(M, V[3 * c], V[3 * c + 1], V[3 * c + 2]));
                                entry.N.Add(n != null ? Mat4.Normal(NT, n[0], n[1], n[2]) : (V3?)null);
                                entry.Part.U.Add(new V2(uv[0], uv[1]));
                                entry.Ctrl.Add(c);
                            }
                            ids.Add(vi);
                        }
                        if (ids != null)
                            for (int t = 1; t + 1 < ids.Count; t++)
                                entry.Part.Tris.Add(flip ? new[] { ids[0], ids[t + 1], ids[t] } : new[] { ids[0], ids[t], ids[t + 1] });
                        poly.Clear();
                        polyIdx++;
                    }
                    foreach (int mi in order)
                    {
                        var e = perMat[mi];
                        if (e.Part.Tris.Count == 0) continue;
                        e.Part.N = nL != null && e.N.All(x => x.HasValue) ? e.N.Select(x => x.Value).ToList() : null;
                        if (geoSkin != null && e.Ctrl.All(c => c < geoSkin.Length))
                            e.Part.Skin = new SourceSkin
                            {
                                J = e.Ctrl.Select(c => (int[])geoSkin[c].J.Clone()).ToList(),
                                W = e.Ctrl.Select(c => (double[])geoSkin[c].W.Clone()).ToList(),
                            };
                        parts.Add(e.Part);
                    }
                }
                return parts;
            }

            var notes = img.Notes;
            notes.AddRange(skinNotes);
            if (skins.Count > 0)
                notes.Add($"skin weights read for {skins.Count} mesh(es) (skeleton key 0x{skeletonKey:x8}, {skeletonJoints} joints - make the .SKN / .ANI from this same file on the ANI / SKN tab)");
            if (objects.Children.Any(o => o.Name == "Geometry" && o.Props.Count > 2 && Text(o.Props[2]) == "Shape"))
                notes.Add("FBX contains blend shapes, which Home models don't have; only the base mesh is converted.");
            string homeModel = meshModels.Select(mm => UserStr(Props70(mm.M), "home_model")).FirstOrDefault(x => x != null);
            top.TryGetValue("FBXHeaderExtension", out var hdr);
            string creator = Text(ChildVal(hdr, "Creator", ""));
            if (creator.Length == 0) creator = Text(ChildVal(new FbxNode { Children = topList }, "Creator", ""));
            var doc = new SourceDoc
            {
                Format = "FBX " + version,
                Images = img.Images,
                Materials = materials,
                DefaultFlipV = true,
                Notes = notes,
                Generator = creator,
                HomeJson = homeModel,
                SkeletonKey = skins.Count > 0 ? skeletonKey : (uint?)null,
                SkeletonJoints = skins.Count > 0 ? skeletonJoints : 0,
            };
            doc.Collect = Collect;
            return doc;
        }
    }
}

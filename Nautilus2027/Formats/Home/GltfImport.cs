#nullable disable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

// glTF 2.0 / GLB loader -> flat list of triangle parts in world space.
// C# port of the HOME_DEV_KIT glTF loader (ws/mdlb/src/01.js).
namespace Nautilus2027.Formats.Home
{
    public static class GltfImport
    {
        private static bool Has(JsonElement e, string name, out JsonElement v)
        {
            if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out v) && v.ValueKind != JsonValueKind.Null) return true;
            v = default;
            return false;
        }

        private static int Int(JsonElement e, string name, int def = -1) => Has(e, name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : def;
        private static double Num(JsonElement e, string name, double def) => Has(e, name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : def;
        private static string Str(JsonElement e, string name) => Has(e, name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        private static bool Bool(JsonElement e, string name) => Has(e, name, out var v) && v.ValueKind == JsonValueKind.True;

        /// <summary>Raw JSON of extras.home on an element (Home round-trip data written by the exporter), or null.</summary>
        private static string HomeExtras(JsonElement e) =>
            Has(e, "extras", out var x) && Has(x, "home", out var h) && h.ValueKind == JsonValueKind.Object ? h.GetRawText() : null;

        private static double[] Nums(JsonElement e, string name)
        {
            if (!Has(e, name, out var v) || v.ValueKind != JsonValueKind.Array) return null;
            return v.EnumerateArray().Select(x => x.GetDouble()).ToArray();
        }

        private static List<JsonElement> Arr(JsonElement e, string name) =>
            Has(e, name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().ToList() : new List<JsonElement>();

        private static int ComponentSize(int ct) => ct switch { 5120 => 1, 5121 => 1, 5122 => 2, 5123 => 2, 5125 => 4, 5126 => 4, _ => throw new InvalidDataException("unknown accessor component type " + ct) };
        private static double NormMax(int ct) => ct switch { 5120 => 127, 5121 => 255, 5122 => 32767, 5123 => 65535, _ => 0 };
        private static int Components(string type) => type switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT2" => 4, "MAT3" => 9, "MAT4" => 16, _ => throw new InvalidDataException("unknown accessor type " + type) };

        private static double[] Trs(JsonElement n, string mode)
        {
            double[] m;
            var mat = Nums(n, "matrix");
            if (mat != null && mat.Length == 16)
            {
                m = (double[])mat.Clone();
                if (mode == "noscale")
                    for (int c = 0; c < 3; c++)
                    {
                        double l = Math.Sqrt(m[4 * c] * m[4 * c] + m[4 * c + 1] * m[4 * c + 1] + m[4 * c + 2] * m[4 * c + 2]);
                        if (l == 0) l = 1;
                        for (int r = 0; r < 3; r++) m[4 * c + r] /= l;
                    }
                return m;
            }
            var t = Nums(n, "translation") ?? new double[] { 0, 0, 0 };
            var q = Nums(n, "rotation") ?? new double[] { 0, 0, 0, 1 };
            var s = Nums(n, "scale") ?? new double[] { 1, 1, 1 };
            if (mode == "noscale") s = s.Select(v => v < 0 ? -1.0 : 1.0).ToArray();
            double x = q[0], y = q[1], z = q[2], w = q[3];
            m = new[]
            {
                1 - 2 * (y * y + z * z), 2 * (x * y + z * w), 2 * (x * z - y * w), 0,
                2 * (x * y - z * w), 1 - 2 * (x * x + z * z), 2 * (y * z + x * w), 0,
                2 * (x * z + y * w), 2 * (y * z - x * w), 1 - 2 * (x * x + y * y), 0,
                t[0], t[1], t[2], 1,
            };
            for (int c = 0; c < 3; c++)
                for (int r = 0; r < 3; r++) m[r + 4 * c] *= s[c];
            return m;
        }

        /// <summary>Parse .glb or .gltf bytes. <paramref name="files"/> finds external buffers / images.</summary>
        public static SourceDoc Parse(byte[] u8, FileResolver files)
        {
            JsonDocument jdoc = null;
            ArraySegment<byte> bin = default;
            bool hasBin = false;
            if (u8.Length >= 12 && BinaryPrimitives.ReadUInt32LittleEndian(u8.AsSpan(0, 4)) == 0x46546C67)
            {
                int off = 12;
                while (off + 8 <= u8.Length)
                {
                    int ln = (int)BinaryPrimitives.ReadUInt32LittleEndian(u8.AsSpan(off, 4));
                    uint ty = BinaryPrimitives.ReadUInt32LittleEndian(u8.AsSpan(off + 4, 4));
                    if (ln < 0 || off + 8 + ln > u8.Length) throw new InvalidDataException("GLB chunk runs past the end of the file");
                    if (ty == 0x4E4F534A) jdoc = JsonDocument.Parse(new ReadOnlyMemory<byte>(u8, off + 8, ln));
                    else if (ty == 0x004E4942) { bin = new ArraySegment<byte>(u8, off + 8, ln); hasBin = true; }
                    off += 8 + ln;
                }
            }
            else
            {
                int start = u8.Length >= 3 && u8[0] == 0xEF && u8[1] == 0xBB && u8[2] == 0xBF ? 3 : 0;   // UTF-8 BOM
                jdoc = JsonDocument.Parse(new ReadOnlyMemory<byte>(u8, start, u8.Length - start));
            }
            if (jdoc == null) throw new InvalidDataException("no glTF JSON found");
            var g = jdoc.RootElement;

            byte[] FetchUri(string uri)
            {
                if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    int comma = uri.IndexOf(',');
                    return Convert.FromBase64String(uri.Substring(comma + 1));
                }
                string decoded = Uri.UnescapeDataString(uri);
                byte[] f = null;
                if (files != null) f = files.Find(decoded, out _, false);
                if (f == null) throw new FileNotFoundException($"missing external file \"{decoded}\" - put it next to the .gltf");
                return f;
            }

            var buffers = Arr(g, "buffers").Select(b =>
            {
                string uri = Str(b, "uri");
                if (uri == null)
                {
                    if (!hasBin) throw new InvalidDataException("GLB has no binary chunk");
                    return bin;
                }
                return new ArraySegment<byte>(FetchUri(uri));
            }).ToList();
            var bufferViews = Arr(g, "bufferViews");
            var accessors = Arr(g, "accessors");

            ArraySegment<byte> View(int i)
            {
                var bv = bufferViews[i];
                var b = buffers[Int(bv, "buffer", 0)];
                int bo = Int(bv, "byteOffset", 0), bl = Int(bv, "byteLength", 0);
                return new ArraySegment<byte>(b.Array, b.Offset + bo, bl);
            }

            double[][] ReadRaw(int bufferView, int byteOffset, int componentType, int count, int nc, bool normalized)
            {
                var outA = new double[count][];
                if (bufferView < 0)
                {
                    for (int k = 0; k < count; k++) outA[k] = new double[nc];
                    return outA;
                }
                var bv = bufferViews[bufferView];
                var buf = buffers[Int(bv, "buffer", 0)];
                int sz = ComponentSize(componentType);
                int baseOff = buf.Offset + Int(bv, "byteOffset", 0) + byteOffset;
                int stride = Int(bv, "byteStride", 0);
                if (stride <= 0) stride = sz * nc;
                var d = buf.Array;
                int limit = buf.Offset + buf.Count;
                double nm = normalized ? NormMax(componentType) : 0;
                for (int k = 0; k < count; k++)
                {
                    var v = new double[nc];
                    for (int c = 0; c < nc; c++)
                    {
                        int o = baseOff + k * stride + c * sz;
                        if (o + sz > limit) throw new InvalidDataException("accessor reads past the end of its buffer");
                        double val;
                        switch (componentType)
                        {
                            case 5120: val = (sbyte)d[o]; break;
                            case 5121: val = d[o]; break;
                            case 5122: val = BinaryPrimitives.ReadInt16LittleEndian(d.AsSpan(o, 2)); break;
                            case 5123: val = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(o, 2)); break;
                            case 5125: val = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(o, 4)); break;
                            default: val = BinaryPrimitives.ReadSingleLittleEndian(d.AsSpan(o, 4)); break;
                        }
                        v[c] = nm > 0 ? Math.Max(-1, val / nm) : val;
                    }
                    outA[k] = v;
                }
                return outA;
            }

            double[][] Accessor(int i)
            {
                var a = accessors[i];
                int ct = Int(a, "componentType", 5126), count = Int(a, "count", 0), nc = Components(Str(a, "type") ?? "SCALAR");
                bool normalized = Bool(a, "normalized");
                var outA = ReadRaw(Int(a, "bufferView", -1), Int(a, "byteOffset", 0), ct, count, nc, normalized);
                if (Has(a, "sparse", out var sp))
                {
                    int sc = Int(sp, "count", 0);
                    Has(sp, "indices", out var si);
                    Has(sp, "values", out var sv);
                    var I = ReadRaw(Int(si, "bufferView", -1), Int(si, "byteOffset", 0), Int(si, "componentType", 5125), sc, 1, false);
                    var V = ReadRaw(Int(sv, "bufferView", -1), Int(sv, "byteOffset", 0), ct, sc, nc, normalized);
                    for (int k = 0; k < sc; k++)
                    {
                        int ix = (int)I[k][0];
                        if (ix >= 0 && ix < outA.Length) outA[ix] = V[k];
                    }
                }
                return outA;
            }

            var doc = new SourceDoc { Format = "glTF", DefaultFlipV = false };
            if (Has(g, "asset", out var asset))
            {
                doc.Generator = Str(asset, "generator") ?? "";
                doc.HomeJson = HomeExtras(asset);
            }

            // images (bytes + mime + name)
            var imgs = Arr(g, "images");
            for (int i = 0; i < imgs.Count; i++)
            {
                var im = imgs[i];
                byte[] data = null;
                string mime = Str(im, "mimeType") ?? "";
                string uri = Str(im, "uri");
                string err = null;
                try
                {
                    if (Int(im, "bufferView", -1) >= 0) data = View(Int(im, "bufferView", -1)).ToArray();
                    else if (uri != null) data = FetchUri(uri);
                }
                catch (Exception ex)
                {
                    err = ex.Message;
                }
                string nm = Str(im, "name");
                if (string.IsNullOrEmpty(nm))
                    nm = uri != null && !uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? FileResolver.Stem(Uri.UnescapeDataString(uri)) : "image" + i;
                var si = new SourceImage { Name = nm, Mime = mime, Bytes = data, FileName = uri != null && !uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? FileResolver.BaseName(uri) : null };
                if (data == null) { si.Error = err ?? "no image data"; doc.Notes.Add($"image {nm}: {si.Error}"); }
                doc.Images.Add(si);
            }

            var textures = Arr(g, "textures");
            int? TexImage(JsonElement info)
            {
                int ti = Int(info, "index", -1);
                if (ti < 0 || ti >= textures.Count) return null;
                var t = textures[ti];
                int src = Int(t, "source", -1);
                if (src >= 0) return src;
                if (Has(t, "extensions", out var ext) && ext.ValueKind == JsonValueKind.Object)
                    foreach (var p in ext.EnumerateObject())
                    {
                        int s = Int(p.Value, "source", -1);
                        if (s >= 0) return s;
                    }
                return null;
            }

            var mats = Arr(g, "materials");
            for (int i = 0; i < mats.Count; i++)
            {
                var m = mats[i];
                Has(m, "pbrMetallicRoughness", out var pbr);
                Has(m, "extensions", out var ext);
                var sm = new SourceMaterial
                {
                    Index = i,
                    Name = Str(m, "name") ?? "material_" + i,
                    AlphaMode = Str(m, "alphaMode") ?? "OPAQUE",
                    AlphaCutoff = Num(m, "alphaCutoff", 0.5),
                    DoubleSided = Bool(m, "doubleSided"),
                    BaseColor = Nums(pbr, "baseColorFactor") ?? new double[] { 1, 1, 1, 1 },
                    HomeJson = HomeExtras(m),
                };
                void Set(string slot, JsonElement parent, string key)
                {
                    if (!Has(parent, key, out var info)) return;
                    var img = TexImage(info);
                    if (img.HasValue && img.Value < doc.Images.Count && !sm.Textures.ContainsKey(slot)) sm.Textures[slot] = img.Value;
                }
                Set("colourMap", pbr, "baseColorTexture");
                if (Has(ext, "KHR_materials_pbrSpecularGlossiness", out var sg))
                {
                    Set("colourMap", sg, "diffuseTexture");
                    Set("specularMap", sg, "specularGlossinessTexture");
                }
                Set("normalMap", m, "normalTexture");
                if (Has(ext, "KHR_materials_specular", out var spec))
                {
                    Set("specularMap", spec, "specularTexture");
                    Set("specularMap", spec, "specularColorTexture");
                }
                Set("emissiveMap", m, "emissiveTexture");
                Set("lightMap", m, "occlusionTexture");
                doc.Materials.Add(sm);
            }

            // node walk
            var nodes = Arr(g, "nodes");
            var meshes = Arr(g, "meshes");
            var skins = Arr(g, "skins");
            List<int> roots;
            var scenes = Arr(g, "scenes");
            if (scenes.Count > 0)
            {
                int si = Int(g, "scene", 0);
                if (si < 0 || si >= scenes.Count) si = 0;
                roots = Arr(scenes[si], "nodes").Select(x => x.GetInt32()).ToList();
            }
            else
            {
                var isChild = new HashSet<int>(nodes.SelectMany(n => Arr(n, "children").Select(c => c.GetInt32())));
                roots = Enumerable.Range(0, nodes.Count).Where(i => !isChild.Contains(i)).ToList();
            }
            string NodeName(int ni) => Str(nodes[ni], "name");

            List<int[]> TrisFor(int mode, int[] I)
            {
                var tris = new List<int[]>();
                if (mode == 4) for (int k = 0; k + 2 < I.Length; k += 3) tris.Add(new[] { I[k], I[k + 1], I[k + 2] });
                else if (mode == 5) for (int k = 0; k + 2 < I.Length; k++) tris.Add(k % 2 != 0 ? new[] { I[k + 1], I[k], I[k + 2] } : new[] { I[k], I[k + 1], I[k + 2] });
                else if (mode == 6) for (int k = 1; k + 1 < I.Length; k++) tris.Add(new[] { I[0], I[k], I[k + 1] });
                return tris;
            }

            List<SourcePart> Collect(string mode)
            {
                var parts = new List<SourcePart>();
                var worlds = new Dictionary<int, double[]>();
                var visiting = new HashSet<int>();
                void Pre(int ni, double[] parent)
                {
                    if (ni < 0 || ni >= nodes.Count || !visiting.Add(ni)) return;
                    var M = Mat4.Mul(parent, Trs(nodes[ni], mode));
                    worlds[ni] = M;
                    foreach (var c in Arr(nodes[ni], "children")) Pre(c.GetInt32(), M);
                    visiting.Remove(ni);
                }
                foreach (int ni in roots) Pre(ni, Mat4.Identity());

                void Walk(int ni, double[] parent)
                {
                    if (ni < 0 || ni >= nodes.Count || !visiting.Add(ni)) return;
                    var n = nodes[ni];
                    var M = Mat4.Mul(parent, Trs(n, mode));
                    int meshIndex = Int(n, "mesh", -1);
                    if (meshIndex >= 0 && meshIndex < meshes.Count)
                    {
                        var mesh = meshes[meshIndex];
                        string name = NodeName(ni) ?? Str(mesh, "name") ?? "mesh_" + meshIndex;
                        double det = Mat4.Det3(M);
                        var NT = Mat4.Inv3T(M);
                        int skinIndex = Int(n, "skin", -1);
                        foreach (var pr in Arr(mesh, "primitives"))
                        {
                            int primMode = Int(pr, "mode", 4);
                            if (primMode < 4) continue;
                            Has(pr, "attributes", out var at);
                            int aPos = Int(at, "POSITION", -1);
                            if (aPos < 0) continue;
                            int aNrm = Int(at, "NORMAL", -1), aUv = Int(at, "TEXCOORD_0", -1), aJ = Int(at, "JOINTS_0", -1), aW = Int(at, "WEIGHTS_0", -1);
                            int material = Int(pr, "material", -1);
                            if (material >= doc.Materials.Count) material = -1;
                            var P0 = Accessor(aPos);
                            var N0 = aNrm >= 0 ? Accessor(aNrm) : null;
                            var part = new SourcePart { Name = name, Material = material };
                            int pi = Int(pr, "indices", -1);
                            int[] I = pi >= 0 ? Accessor(pi).Select(v => (int)v[0]).ToArray() : Enumerable.Range(0, P0.Length).ToArray();
                            part.Tris = TrisFor(primMode, I);
                            part.U = aUv >= 0 ? Accessor(aUv).Select(v => new V2(v[0], v[1])).ToList() : P0.Select(_ => new V2(0, 0)).ToList();

                            if (skinIndex >= 0 && skinIndex < skins.Count && aJ >= 0 && aW >= 0)
                            {
                                // skinned: bind-pose positions = sum(w * jointWorld * inverseBind) * p (the mesh node's own transform is ignored, as glTF specifies)
                                var sk = skins[skinIndex];
                                var joints = Arr(sk, "joints").Select(x => x.GetInt32()).ToList();
                                int ibIndex = Int(sk, "inverseBindMatrices", -1);
                                var IB = ibIndex >= 0 ? Accessor(ibIndex) : joints.Select(_ => Mat4.Identity()).ToArray();
                                var JM = joints.Select((jn, k) => Mat4.Mul(worlds.TryGetValue(jn, out var wm) ? wm : Mat4.Identity(), k < IB.Length ? IB[k] : Mat4.Identity())).ToList();
                                var JJ = Accessor(aJ);
                                var WW = Accessor(aW);
                                // at the bind pose every skin matrix is the identity: keep the stored positions exactly
                                bool atBind = JM.All(mm => Enumerable.Range(0, 16).All(e => Math.Abs(mm[e] - (e % 5 == 0 ? 1 : 0)) < 1e-5));
                                part.P = new List<V3>(P0.Length);
                                part.N = N0 != null ? new List<V3>(P0.Length) : null;
                                for (int k = 0; k < P0.Length; k++)
                                {
                                    var m = new double[16];
                                    double tw = 0;
                                    for (int c = 0; c < 4; c++)
                                    {
                                        double wgt = k < WW.Length && c < WW[k].Length ? WW[k][c] : 0;
                                        int j = k < JJ.Length && c < JJ[k].Length ? (int)JJ[k][c] : -1;
                                        if (wgt == 0 || j < 0 || j >= JM.Count) continue;
                                        tw += wgt;
                                        var s = JM[j];
                                        for (int e = 0; e < 16; e++) m[e] += s[e] * wgt;
                                    }
                                    if (tw == 0 || atBind) m = Mat4.Identity();
                                    else if (Math.Abs(tw - 1) > 1e-4) for (int e = 0; e < 16; e++) m[e] /= tw;
                                    part.P.Add(Mat4.Point(m, P0[k][0], P0[k][1], P0[k][2]));
                                    if (part.N != null)
                                    {
                                        var nt = Mat4.Inv3T(m);
                                        var nv = k < N0.Length ? N0[k] : new double[3];
                                        part.N.Add(Mat4.Normal(nt, nv[0], nv[1], nv[2]));
                                    }
                                }
                                var skin = new SourceSkin { Names = joints.Select(jn => jn >= 0 && jn < nodes.Count ? (NodeName(jn) ?? "joint" + jn) : "joint" + jn).ToList() };
                                for (int k = 0; k < P0.Length; k++)
                                {
                                    var jj = k < JJ.Length ? JJ[k] : new double[4];
                                    var ww = k < WW.Length ? WW[k] : new double[4];
                                    skin.J.Add(Enumerable.Range(0, 4).Select(c => c < jj.Length ? SkeletonSlot(joints, (int)jj[c]) : 0).ToArray());
                                    skin.W.Add(Enumerable.Range(0, 4).Select(c => c < ww.Length ? ww[c] : 0).ToArray());
                                }
                                part.Skin = skin;
                                ReadExtraAttributes(part, at, mesh, Mat4.Identity(), 1);
                                if (primMode == 5) part.StripIndices = I;
                                parts.Add(part);
                                continue;
                            }

                            part.P = P0.Select(v => Mat4.Point(M, v[0], v[1], v[2])).ToList();
                            part.N = N0?.Select(v => Mat4.Normal(NT, v[0], v[1], v[2])).ToList();
                            if (det < 0) part.Tris = part.Tris.Select(t => new[] { t[0], t[2], t[1] }).ToList();
                            ReadExtraAttributes(part, at, mesh, M, det);
                            if (primMode == 5 && det >= 0) part.StripIndices = I;
                            parts.Add(part);
                        }
                    }
                    foreach (var c in Arr(n, "children")) Walk(c.GetInt32(), M);
                    visiting.Remove(ni);
                }
                foreach (int ni in roots) Walk(ni, Mat4.Identity());
                return parts;
            }

            void ReadExtraAttributes(SourcePart part, JsonElement at, JsonElement mesh, double[] M, double det)
            {
                int n = part.P.Count;
                int aUv1 = Int(at, "TEXCOORD_1", -1), aCol = Int(at, "COLOR_0", -1), aTan = Int(at, "TANGENT", -1);
                if (aUv1 >= 0)
                {
                    var u1 = Accessor(aUv1);
                    if (u1.Length == n) part.U1 = u1.Select(v => new V2(v[0], v[1])).ToList();
                }
                if (aCol >= 0)
                {
                    var c = Accessor(aCol);
                    if (c.Length == n) part.Colour = c.Select(v => new[] { v[0], v[1], v[2], v.Length > 3 ? v[3] : 1.0 }).ToList();
                }
                if (aTan >= 0)
                {
                    var t = Accessor(aTan);
                    if (t.Length == n)
                        part.Tangent = t.Select(v =>
                        {
                            double x = M[0] * v[0] + M[4] * v[1] + M[8] * v[2], y = M[1] * v[0] + M[5] * v[1] + M[9] * v[2], z = M[2] * v[0] + M[6] * v[1] + M[10] * v[2];
                            // keep the stored length (Home tangents aren't exactly unit length after quantisation)
                            double l = Math.Sqrt(x * x + y * y + z * z), l0 = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
                            if (l > 1e-20 && Math.Abs(l - l0) > 1e-7) { double f = l0 / l; x *= f; y *= f; z *= f; }
                            double w = v.Length > 3 ? v[3] : 1;
                            return new[] { x, y, z, det < 0 ? -w : w };
                        }).ToList();
                }
                if (at.ValueKind == JsonValueKind.Object)
                    foreach (var p in at.EnumerateObject())
                        if (p.Name.StartsWith("_") && p.Value.ValueKind == JsonValueKind.Number)
                        {
                            var vals = Accessor(p.Value.GetInt32());
                            if (vals.Length == n) part.Custom[p.Name] = vals.ToList();
                        }
                part.HomeJson = HomeExtras(mesh);
            }

            // the skeleton the ANI / SKN tab makes from this file: skin joints are renumbered into its order
            if (skins.Count > 0)
            {
                try
                {
                    var ad = GltfAnimImport.Parse(u8, files);
                    doc.SkeletonKey = ad.Skeleton.Key;
                    doc.SkeletonJoints = ad.Skeleton.Joints.Count;
                    doc.SkeletonNodes = ad.JointNodes;
                }
                catch (Exception) { }
            }
            int SkeletonSlot(List<int> skinJoints, int slot)
            {
                if (doc.SkeletonNodes == null || slot < 0 || slot >= skinJoints.Count) return slot;
                int r = doc.SkeletonNodes.IndexOf(skinJoints[slot]);
                return r >= 0 ? r : 0;
            }
            doc.Collect = Collect;
            doc.JointNames = skins.SelectMany(s => Arr(s, "joints").Select(x => x.GetInt32()))
                .Where(jn => jn >= 0 && jn < nodes.Count)
                .Select(jn => NodeName(jn) ?? "")
                .Where(s => s.Length > 0)
                .Distinct()
                .ToList();
            return doc;
        }
    }
}

#nullable disable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

// glTF / GLB -> Home skeleton (SKN) and animation clips (ANI).
// Joints come from the first skin (or, without one, from every animated node); each glTF animation becomes one clip.
// CUBICSPLINE samplers map one to one onto Home's Hermite keys; LINEAR / STEP samplers get tangents from the key slopes.
// Data this tool wrote in extras.home (joint hashes, flags, skeleton key, clip duration / flags) is used when present.
namespace Nautilus2027.Formats.Home
{
    public sealed class GltfAnimDoc
    {
        public HmSkeletonFile Skeleton;
        /// <summary>glTF node index of each skeleton joint.</summary>
        public List<int> JointNodes = new List<int>();
        public List<(string Name, HmAnimClip Clip)> Clips = new List<(string, HmAnimClip)>();
        public List<string> Notes = new List<string>();
        public bool HadSkin;
    }

    public static class GltfAnimImport
    {
        private static bool Has(JsonElement e, string name, out JsonElement v)
        {
            if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out v) && v.ValueKind != JsonValueKind.Null) return true;
            v = default;
            return false;
        }

        private static int Int(JsonElement e, string name, int def = -1) => Has(e, name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : def;
        private static string Str(JsonElement e, string name) => Has(e, name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        private static List<JsonElement> Arr(JsonElement e, string name) => Has(e, name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().ToList() : new List<JsonElement>();
        private static double[] Nums(JsonElement e, string name) => Has(e, name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x => x.GetDouble()).ToArray() : null;

        private static JsonElement HomeExtras(JsonElement e) => Has(e, "extras", out var x) && Has(x, "home", out var h) && h.ValueKind == JsonValueKind.Object ? h : default;

        private static double? Num(JsonElement e, string name) => Has(e, name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : (double?)null;

        public static GltfAnimDoc Parse(byte[] u8, FileResolver files)
        {
            JsonDocument jdoc = null;
            ArraySegment<byte> bin = default;
            if (u8.Length >= 12 && BinaryPrimitives.ReadUInt32LittleEndian(u8.AsSpan(0, 4)) == 0x46546C67)
            {
                int off = 12;
                while (off + 8 <= u8.Length)
                {
                    int ln = (int)BinaryPrimitives.ReadUInt32LittleEndian(u8.AsSpan(off, 4));
                    uint ty = BinaryPrimitives.ReadUInt32LittleEndian(u8.AsSpan(off + 4, 4));
                    if (ln < 0 || off + 8 + ln > u8.Length) throw new InvalidDataException("GLB chunk runs past the end of the file");
                    if (ty == 0x4E4F534A) jdoc = JsonDocument.Parse(new ReadOnlyMemory<byte>(u8, off + 8, ln));
                    else if (ty == 0x004E4942) bin = new ArraySegment<byte>(u8, off + 8, ln);
                    off += 8 + ln;
                }
            }
            else
            {
                int start = u8.Length >= 3 && u8[0] == 0xEF && u8[1] == 0xBB && u8[2] == 0xBF ? 3 : 0;
                jdoc = JsonDocument.Parse(new ReadOnlyMemory<byte>(u8, start, u8.Length - start));
            }
            if (jdoc == null) throw new InvalidDataException("no glTF JSON found");
            var g = jdoc.RootElement;

            var buffers = Arr(g, "buffers").Select(b =>
            {
                string uri = Str(b, "uri");
                if (uri == null) return bin;
                if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return new ArraySegment<byte>(Convert.FromBase64String(uri.Substring(uri.IndexOf(',') + 1)));
                byte[] f = files != null ? files.Find(Uri.UnescapeDataString(uri), out _, false) : null;
                if (f == null) throw new FileNotFoundException($"missing external file \"{uri}\" - put it next to the .gltf");
                return new ArraySegment<byte>(f);
            }).ToList();
            var views = Arr(g, "bufferViews");
            var accessors = Arr(g, "accessors");

            double[][] Accessor(int i)
            {
                var a = accessors[i];
                int ct = Int(a, "componentType", 5126), count = Int(a, "count", 0);
                string type = Str(a, "type") ?? "SCALAR";
                int nc = type switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16, _ => 1 };
                bool norm = Has(a, "normalized", out var nv) && nv.ValueKind == JsonValueKind.True;
                int sz = ct switch { 5120 => 1, 5121 => 1, 5122 => 2, 5123 => 2, 5125 => 4, _ => 4 };
                var outA = new double[count][];
                int bvi = Int(a, "bufferView", -1);
                if (bvi < 0) { for (int k = 0; k < count; k++) outA[k] = new double[nc]; return outA; }
                var bv = views[bvi];
                var buf = buffers[Int(bv, "buffer", 0)];
                int baseOff = buf.Offset + Int(bv, "byteOffset", 0) + Int(a, "byteOffset", 0);
                int stride = Int(bv, "byteStride", 0);
                if (stride <= 0) stride = sz * nc;
                var d = buf.Array;
                for (int k = 0; k < count; k++)
                {
                    var v = new double[nc];
                    for (int c = 0; c < nc; c++)
                    {
                        int o = baseOff + k * stride + c * sz;
                        double val = ct switch
                        {
                            5120 => (sbyte)d[o],
                            5121 => d[o],
                            5122 => BinaryPrimitives.ReadInt16LittleEndian(d.AsSpan(o, 2)),
                            5123 => BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(o, 2)),
                            5125 => BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(o, 4)),
                            _ => BinaryPrimitives.ReadSingleLittleEndian(d.AsSpan(o, 4)),
                        };
                        if (norm) val = ct switch { 5120 => Math.Max(-1, val / 127), 5121 => val / 255, 5122 => Math.Max(-1, val / 32767), 5123 => val / 65535, _ => val };
                        v[c] = val;
                    }
                    outA[k] = v;
                }
                return outA;
            }

            var nodes = Arr(g, "nodes");
            var parentOf = new int[nodes.Count];
            for (int i = 0; i < parentOf.Length; i++) parentOf[i] = -1;
            for (int i = 0; i < nodes.Count; i++)
                foreach (var c in Arr(nodes[i], "children"))
                {
                    int ci = c.GetInt32();
                    if (ci >= 0 && ci < nodes.Count) parentOf[ci] = i;
                }

            var doc = new GltfAnimDoc();
            var skins = Arr(g, "skins");
            var anims = Arr(g, "animations");

            // ---- joint set
            var jointSet = new HashSet<int>();
            JsonElement skinEl = default;
            if (skins.Count > 0)
            {
                skinEl = skins[0];
                foreach (var j in Arr(skinEl, "joints")) jointSet.Add(j.GetInt32());
                doc.HadSkin = true;
                if (skins.Count > 1) doc.Notes.Add($"the file has {skins.Count} skins; the first one was used");
            }
            else
            {
                foreach (var an in anims)
                    foreach (var ch in Arr(an, "channels"))
                        if (Has(ch, "target", out var tg) && Int(tg, "node") >= 0) jointSet.Add(Int(tg, "node"));
                foreach (var n in jointSet.ToList())   // keep the animated nodes' ancestors so the hierarchy holds
                    for (int p = parentOf[n]; p >= 0; p = parentOf[p]) if (Int(nodes[p], "mesh") < 0) jointSet.Add(p);
            }
            jointSet.RemoveWhere(n => n < 0 || n >= nodes.Count);
            if (jointSet.Count == 0) throw new InvalidDataException("the file has no skin and no animated nodes, so there is no skeleton in it");

            // order: stored Home index when every joint has one, else depth-first from the roots (parents first)
            int? HomeIndex(int n) { var h = HomeExtras(nodes[n]); var v = h.ValueKind == JsonValueKind.Object ? Num(h, "index") : null; return v.HasValue ? (int)v.Value : (int?)null; }
            List<int> order;
            if (jointSet.All(n => HomeIndex(n).HasValue) && jointSet.Select(n => HomeIndex(n).Value).Distinct().Count() == jointSet.Count)
                order = jointSet.OrderBy(n => HomeIndex(n).Value).ToList();
            else
            {
                order = new List<int>();
                void Visit(int n)
                {
                    if (jointSet.Contains(n) && !order.Contains(n)) order.Add(n);
                    foreach (var c in Arr(nodes[n], "children")) { int ci = c.GetInt32(); if (ci >= 0 && ci < nodes.Count) Visit(ci); }
                }
                for (int i = 0; i < nodes.Count; i++) if (parentOf[i] < 0) Visit(i);
                foreach (var n in jointSet) if (!order.Contains(n)) order.Add(n);
            }
            int JointParent(int n)
            {
                for (int p = parentOf[n]; p >= 0; p = parentOf[p]) { int k = order.IndexOf(p); if (k >= 0) return k; }
                return -1;
            }
            // parents must come first: repair any order that breaks that
            bool moved = true;
            for (int guard = 0; moved && guard < order.Count * 2; guard++)
            {
                moved = false;
                for (int k = 0; k < order.Count; k++)
                {
                    int p = JointParent(order[k]);
                    if (p > k) { int n = order[p]; order.RemoveAt(p); order.Insert(k, n); moved = true; break; }
                }
            }

            var sk = new HmSkeletonFile();
            var skinHome = skinEl.ValueKind == JsonValueKind.Object ? HomeExtras(skinEl) : default;
            foreach (int n in order)
            {
                var node = nodes[n];
                var he = HomeExtras(node);
                string name = Str(node, "name") ?? "joint" + n;
                double? hv = he.ValueKind == JsonValueKind.Object ? Num(he, "hash") : null;
                var j = new SkJoint
                {
                    Index = sk.Joints.Count,
                    Hash = hv.HasValue ? (uint)hv.Value : HmSkeleton.LabelHash(HmRoundTrip.BaseName(name)),
                    Parent = (short)JointParent(n),
                    Flags = (short)(he.ValueKind == JsonValueKind.Object ? Num(he, "flags") ?? 0 : 0),
                    TW = (float)(he.ValueKind == JsonValueKind.Object ? Num(he, "tw") ?? 0 : 0),
                    SW = (float)(he.ValueKind == JsonValueKind.Object ? Num(he, "sw") ?? 0 : 0),
                };
                var (t, q, s) = NodeTrs(node);
                j.T = t; j.Q = q; j.S = s;
                sk.Joints.Add(j);
                doc.JointNodes.Add(n);
            }
            // duplicate hashes (two joints with the same name): make them unique so the skeleton can still be written
            var seen = new HashSet<uint>();
            foreach (var j in sk.Joints)
            {
                if (seen.Add(j.Hash)) continue;
                uint h = j.Hash;
                while (!seen.Add(h)) h = HmCore.HashName(HmSkeleton.JointLabel(h) + "_dup");
                doc.Notes.Add($"two joints are called {HmSkeleton.JointLabel(j.Hash)}; the second was renamed {HmSkeleton.JointLabel(h)}");
                j.Hash = h;
            }
            double? key = skinHome.ValueKind == JsonValueKind.Object ? Num(skinHome, "key") : null;
            sk.Key = key.HasValue ? (uint)key.Value : HmSkeleton.NewKey(sk);
            sk.OwnsMemory = (short)(skinHome.ValueKind == JsonValueKind.Object ? Num(skinHome, "ownsMemory") ?? 0 : 0);
            doc.Skeleton = sk;
            if (!key.HasValue) doc.Notes.Add($"new skeleton key 0x{sk.Key:x8} (an MDL made for this skeleton must use the same key)");

            // ---- animations
            var nodeToJoint = new Dictionary<int, int>();
            for (int k = 0; k < order.Count; k++) nodeToJoint[order[k]] = k;
            for (int ai = 0; ai < anims.Count; ai++)
            {
                var an = anims[ai];
                var samplers = Arr(an, "samplers");
                var ah = HomeExtras(an);
                var clip = new HmAnimClip { Flags = (int)(ah.ValueKind == JsonValueKind.Object ? Num(ah, "flags") ?? 0 : 0) };
                var items = new Dictionary<uint, AniItem>();
                double end = 0;
                int stepCount = 0;
                foreach (var ch in Arr(an, "channels"))
                {
                    if (!Has(ch, "target", out var tg)) continue;
                    int node = Int(tg, "node");
                    string path = Str(tg, "path");
                    if (node < 0 || !nodeToJoint.TryGetValue(node, out int jIdx)) continue;
                    uint nameHash = path == "rotation" ? HmAnimation.HRotate : path == "translation" ? HmAnimation.HTranslate : path == "scale" ? HmAnimation.HScale : 0;
                    if (nameHash == 0) continue;   // morph target weights: no Home equivalent
                    int si = Int(ch, "sampler");
                    if (si < 0 || si >= samplers.Count) continue;
                    var smp = samplers[si];
                    string interp = Str(smp, "interpolation") ?? "LINEAR";
                    var T = Accessor(Int(smp, "input"));
                    var O = Accessor(Int(smp, "output"));
                    int nk = T.Length;
                    if (nk == 0) continue;
                    var keys = new List<AniKey>();
                    if (interp == "CUBICSPLINE" && O.Length == 3 * nk)
                    {
                        for (int k = 0; k < nk; k++) keys.Add(new AniKey { T = T[k][0], V = Pad4(O[3 * k + 1], path), G = Pad4Tangent(O[3 * k + 2]) });
                    }
                    else
                    {
                        if (O.Length < nk) continue;
                        if (interp == "STEP") stepCount++;
                        for (int k = 0; k < nk; k++) keys.Add(new AniKey { T = T[k][0], V = Pad4(O[k], path) });
                        if (path == "rotation")
                            for (int k = 1; k < nk; k++)
                            {
                                double dot = 0;
                                for (int q = 0; q < 4; q++) dot += keys[k].V[q] * keys[k - 1].V[q];
                                if (dot < 0) for (int q = 0; q < 4; q++) keys[k].V[q] = -keys[k].V[q];
                            }
                        for (int k = 0; k < nk; k++)
                        {
                            var g2 = new double[4];
                            if (interp != "STEP" && nk > 1)
                            {
                                int a = Math.Max(0, k - 1), b = Math.Min(nk - 1, k + 1);
                                double dt = keys[b].T - keys[a].T;
                                if (dt > 0) for (int q = 0; q < 4; q++) g2[q] = (keys[b].V[q] - keys[a].V[q]) / dt;
                            }
                            keys[k].G = g2;
                        }
                    }
                    // strictly increasing times
                    keys = keys.Where((k, i) => i == 0 || k.T > keys[i - 1].T).ToList();
                    end = Math.Max(end, keys[keys.Count - 1].T);
                    var chn = new AniChannel { Name = nameHash };
                    if (keys.All(k => Enumerable.Range(0, 4).All(q => Math.Abs(k.V[q] - keys[0].V[q]) < 1e-7)))
                        chn.Constant = keys[0].V;
                    else
                        chn.Keys = keys;
                    uint h = sk.Joints[jIdx].Hash;
                    if (!items.TryGetValue(h, out var item)) items[h] = item = new AniItem { Hash = h };
                    item.Channels.RemoveAll(c => c.Name == nameHash);
                    item.Channels.Add(chn);
                }
                if (items.Count == 0) continue;
                double? dur = ah.ValueKind == JsonValueKind.Object ? Num(ah, "duration") : null;
                clip.Duration = (float)(dur.HasValue && dur.Value >= end - 1e-4 ? dur.Value : end);
                // Home channel order per item: translate, rotate, scale (as in shipped clips)
                foreach (var it in items.Values)
                    it.Channels = it.Channels.OrderBy(c => c.Name == HmAnimation.HTranslate ? 0 : c.Name == HmAnimation.HRotate ? 1 : 2).ToList();
                clip.Items = items.Values.ToList();
                if (stepCount > 0) doc.Notes.Add($"{Str(an, "name") ?? "animation " + ai}: {stepCount} stepped channel(s) became smooth curves (Home has no stepped keys)");
                doc.Clips.Add((Str(an, "name") ?? "animation_" + ai, clip));
            }
            return doc;
        }

        private static double[] Pad4(double[] v, string path)
        {
            var r = new double[4];
            for (int i = 0; i < 4 && i < v.Length; i++) r[i] = v[i];
            if (path == "rotation")
            {
                double l = Math.Sqrt(r.Sum(x => x * x));
                if (l > 0) for (int i = 0; i < 4; i++) r[i] /= l;
                else r[3] = 1;
            }
            // Home stores translate / scale with w = 0 in shipped clips
            return r;
        }

        private static double[] Pad4Tangent(double[] v)
        {
            var r = new double[4];
            for (int i = 0; i < 4 && i < v.Length; i++) r[i] = v[i];
            return r;
        }

        private static (double[] T, double[] Q, double[] S) NodeTrs(JsonElement n)
        {
            var m = Nums(n, "matrix");
            if (m != null && m.Length == 16) return HmSkeleton.Decompose(m);
            var t = Nums(n, "translation") ?? new double[] { 0, 0, 0 };
            var q = Nums(n, "rotation") ?? new double[] { 0, 0, 0, 1 };
            var s = Nums(n, "scale") ?? new double[] { 1, 1, 1 };
            double l = Math.Sqrt(q.Sum(x => x * x));
            if (l > 0) q = q.Select(x => x / l).ToArray();
            return (t, q, s);
        }
    }
}

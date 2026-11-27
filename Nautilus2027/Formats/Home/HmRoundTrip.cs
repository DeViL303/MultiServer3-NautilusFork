#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

// MDL -> GLB -> MDL round trip.
//
// When an MDL is exported, everything a GLB can't express natively (material type, flags, blend modes, constants and
// extras, every texture slot with its original Home path / DDS format, per-mesh name hashes, flags and the exact vertex
// layout) is written as glTF "extras" (key "home") on the asset, each material and each mesh, and also to a
// NAME.home.json file next to the export (Blender drops extras unless "Custom Properties" is ticked, but keeps names).
// Per-vertex data that MDLs have and plain glTF viewers don't use travels as standard attributes where possible
// (TEXCOORD_1 = lightmap UVs, COLOR_0 = baked vertex lighting, TANGENT) and as "_HOME_..." custom attributes otherwise.
//
// On import, parts that carry this data are rebuilt field for field instead of going through the generic conversion.
namespace Nautilus2027.Formats.Home
{
    public sealed class HomeMetaField
    {
        public uint Name;
        public int Offset, Type, Count, Normalised;
    }

    public sealed class HomeMetaTexture
    {
        public uint Slot;
        public string SlotName;
        public string Path;
        public uint WrapBits, BorderColour;
        public float LodMin, LodMax, LodBias;
        /// <summary>Name of the image in the exported file (null when it wasn't exported, e.g. cube maps).</summary>
        public string Image;
        public string Format;
        public int Width, Height, Mips;
        public bool Cube;
        /// <summary>crc32 of the exported RGBA pixels: unchanged pixels mean the original DDS can be reused as it is.</summary>
        public uint Crc;
        /// <summary>Where the original DDS was found when exporting (sidecar file only).</summary>
        public string SourceDds;
    }

    public sealed class HomeMetaMaterial
    {
        public int Index;
        public string Name;
        public string SourceFile;
        public uint Type, Instance, Flags, AlphaFunc;
        public int BlendSrc, BlendDst;
        public float AlphaRef;
        public List<uint> ConstantHashes = new List<uint>();
        public List<float[]> ConstantValues = new List<float[]>();
        public List<HomeMetaTexture> Textures = new List<HomeMetaTexture>();
        /// <summary>Material extras as [hash, value] pairs; null when the material had no extras block.</summary>
        public List<uint[]> Extra;
        public string Reserved;
    }

    public sealed class HomeMetaMesh
    {
        public int Index;
        public string Name;
        public uint NameHash, Prim, Flags;
        public int W60, W62, Material, Stride;
        public List<HomeMetaField> Fields = new List<HomeMetaField>();
        public bool HasDeformers;
        /// <summary>Original bounding sphere (kept when the geometry still fits inside it).</summary>
        public float[] Bound;
        /// <summary>Tangent w when it is the same for every vertex (otherwise _HOME_TANGENT_W carries it).</summary>
        public float TangentW;
        /// <summary>Field hash -> custom glTF attribute holding it, for fields with no standard glTF attribute.</summary>
        public Dictionary<string, string> CustomFields;
    }

    public sealed class HomeMetaModel
    {
        public int Format = 1;
        public int Version;
        public uint Skeleton, Word8;
        public float[] ModelBound;
        public string SourceMdl;
        public List<HomeMetaMaterial> Materials;
        public List<HomeMetaMesh> Meshes;
    }

    public static class HmRoundTrip
    {
        public static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            IncludeFields = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public static readonly JsonSerializerOptions JsonIndented = new JsonSerializerOptions
        {
            IncludeFields = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
        };

        /// <summary>Texture slot used by the baked-lighting (LOM) material for its lightmap (second UV set).</summary>
        public const uint HLightMap = 0xDD3912D2;
        /// <summary>Vertex colour field (UByte x4) of the baked-lighting material: RGB = indirect light, A = sun visibility.</summary>
        public const uint HColour = 0x51EF6314;

        public const string TangentWAttribute = "_HOME_TANGENT_W";
        public static string CustomAttribute(uint field) => "_HOME_" + field.ToString("X8");

        public static string SlotName(uint slot) => slot == HLightMap ? "lightMap" : HmCore.NameOf(slot);

        public static T Parse<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonSerializer.Deserialize<T>(json, Json); }
            catch (Exception) { return null; }
        }

        // ------------------------------------------------------------------ export side

        public static HomeMetaMaterial Describe(HmMaterial mt, int index, string name) => new HomeMetaMaterial
        {
            Index = index,
            Name = name,
            SourceFile = mt.SourceFile,
            Type = mt.Type,
            Instance = mt.Instance,
            Flags = mt.Flags,
            AlphaFunc = mt.AlphaFunc,
            BlendSrc = mt.BlendSrc,
            BlendDst = mt.BlendDst,
            AlphaRef = mt.AlphaRef,
            ConstantHashes = mt.Constants.Select(c => c.Hash).ToList(),
            ConstantValues = mt.Constants.Select(c => c.Value.ToArray()).ToList(),
            Extra = mt.Extra?.Select(e => new[] { e.Hash, e.Value }).ToList(),
            Reserved = Convert.ToHexString(mt.Reserved ?? new byte[16]),
        };

        public static HomeMetaMesh Describe(HmMesh me, int index, string name) => new HomeMetaMesh
        {
            Index = index,
            Name = name,
            NameHash = me.Name,
            Prim = me.Prim,
            Flags = me.Flags,
            W60 = me.W60,
            W62 = me.W62,
            Material = me.Material,
            Stride = me.Stride,
            HasDeformers = me.HasDeformers,
            Bound = me.Bound?.ToArray(),
            Fields = me.Fields.Select(f => new HomeMetaField { Name = f.Name, Offset = f.Offset, Type = f.Type, Count = f.Count, Normalised = f.Normalised }).ToList(),
        };

        public static HmMaterial ToMaterial(HomeMetaMaterial m, Func<HomeMetaTexture, string> pathFor = null)
        {
            var mt = new HmMaterial
            {
                SourceFile = m.SourceFile ?? "",
                Type = m.Type,
                Instance = m.Instance,
                Flags = m.Flags,
                BlendSrc = (ushort)m.BlendSrc,
                BlendDst = (ushort)m.BlendDst,
                AlphaFunc = m.AlphaFunc,
                AlphaRef = m.AlphaRef,
                Extra = m.Extra?.Where(e => e != null && e.Length >= 2).Select(e => (e[0], e[1])).ToList(),
                Reserved = new byte[16],
            };
            try
            {
                var r = Convert.FromHexString(m.Reserved ?? "");
                Buffer.BlockCopy(r, 0, mt.Reserved, 0, Math.Min(16, r.Length));
            }
            catch (FormatException) { }
            int nc = Math.Min(m.ConstantHashes?.Count ?? 0, m.ConstantValues?.Count ?? 0);
            for (int i = 0; i < nc; i++)
            {
                var v = new float[4];
                var src = m.ConstantValues[i] ?? Array.Empty<float>();
                Array.Copy(src, v, Math.Min(4, src.Length));
                mt.Constants.Add((m.ConstantHashes[i], v));
            }
            foreach (var t in m.Textures ?? new List<HomeMetaTexture>())
                mt.Textures.Add(new HmTexture
                {
                    Slot = t.Slot,
                    Filename = pathFor != null ? pathFor(t) : t.Path ?? "",
                    WrapBits = t.WrapBits,
                    BorderColour = t.BorderColour,
                    LodMin = t.LodMin,
                    LodMax = t.LodMax,
                    LodBias = t.LodBias,
                });
            return mt;
        }

        /// <summary>Copy without local file paths (for data embedded in a file that may be shared).</summary>
        public static HomeMetaMaterial WithoutLocalPaths(HomeMetaMaterial m)
        {
            var c = Parse<HomeMetaMaterial>(JsonSerializer.Serialize(m, Json));
            if (c?.Textures != null)
                foreach (var t in c.Textures) t.SourceDds = null;
            return c;
        }

        // ------------------------------------------------------------------ import side

        private static readonly Regex BlenderSuffix = new Regex("\\.\\d{3}$");
        private static readonly Regex LeadingIndex = new Regex("^(?:mesh_|material_)?(\\d+)_");

        /// <summary>Strip Blender's ".001" duplicate suffix.</summary>
        public static string BaseName(string name) => BlenderSuffix.Replace(name ?? "", "");

        /// <summary>Find a material or mesh description by exact name, then by the index the exporter puts at the start of names.</summary>
        public static T FindByName<T>(IList<T> list, string name, Func<T, string> nameOf, Func<T, int> indexOf) where T : class
        {
            if (list == null || string.IsNullOrEmpty(name)) return null;
            string n = BaseName(name);
            var hit = list.FirstOrDefault(x => string.Equals(nameOf(x), n, StringComparison.OrdinalIgnoreCase));
            if (hit != null) return hit;
            var m = LeadingIndex.Match(n);
            if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx))
                return list.FirstOrDefault(x => indexOf(x) == idx);
            return null;
        }

        private static int TypeSize(int type) => type switch
        {
            HmCore.StShortNorm => 2,
            HmCore.StFloat => 4,
            HmCore.StHalf => 2,
            HmCore.StUByteNorm => 1,
            HmCore.StShort => 2,
            HmCore.StPacked => 4,
            HmCore.StUByte => 1,
            _ => 4,
        };

        /// <summary>IEEE 754 half from a double (round to nearest even), without needing System.Half helpers.</summary>
        private static ushort ToHalf(double v)
        {
            uint x = (uint)BitConverter.SingleToInt32Bits((float)v);
            uint sign = (x >> 16) & 0x8000;
            int exp = (int)((x >> 23) & 0xFF) - 127 + 15;
            uint mant = x & 0x7FFFFF;
            if (((x >> 23) & 0xFF) == 0xFF) return (ushort)(sign | 0x7C00 | (mant != 0 ? 0x200u : 0));
            if (exp >= 31) return (ushort)(sign | 0x7C00);
            if (exp <= 0)
            {
                if (exp < -10) return (ushort)sign;
                mant |= 0x800000;
                int shift = 14 - exp;
                uint h = mant >> shift;
                uint rem = mant & ((1u << shift) - 1), halfway = 1u << (shift - 1);
                if (rem > halfway || (rem == halfway && (h & 1) != 0)) h++;
                return (ushort)(sign | h);
            }
            uint hv = ((uint)exp << 10) | (mant >> 13);
            uint r = mant & 0x1FFF;
            if (r > 0x1000 || (r == 0x1000 && (hv & 1) != 0)) hv++;
            return (ushort)(sign | hv);
        }

        private static void WriteComponent(byte[] vb, int o, int type, int k, double v)
        {
            switch (type)
            {
                case HmCore.StFloat: HmCore.PutF32(vb, o + 4 * k, (float)v); break;
                case HmCore.StShortNorm: HmCore.PutS16(vb, o + 2 * k, (short)Math.Max(-32767, Math.Min(32767, HmCore.JsRound(v * 32767)))); break;
                case HmCore.StShort: HmCore.PutS16(vb, o + 2 * k, (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, HmCore.JsRound(v)))); break;
                case HmCore.StHalf: HmCore.PutU16(vb, o + 2 * k, ToHalf(v)); break;
                case HmCore.StUByteNorm: vb[o + k] = (byte)Math.Max(0, Math.Min(255, HmCore.JsRound(v * 255))); break;
                case HmCore.StUByte: vb[o + k] = (byte)Math.Max(0, Math.Min(255, HmCore.JsRound(v))); break;
                default: break;   // packed formats: left as zero
            }
        }

        /// <summary>
        /// Rebuild one MDL element exactly as described by <paramref name="meta"/> from an imported part.
        /// Returns null when the part can't be written that way (the caller then uses the generic conversion).
        /// </summary>
        public static HmMesh BuildExact(SourcePart part, HomeMetaMesh meta, int material, List<string> log)
        {
            int n = part.P.Count;
            string label = part.Name ?? meta.Name ?? "mesh";
            if (n == 0 || n > 65536)
            {
                log.Add($"{label}: {n:N0} vertices doesn't fit one element; converted the generic way");
                return null;
            }
            var tris = part.Tris.Where(t => t[0] >= 0 && t[1] >= 0 && t[2] >= 0 && t[0] < n && t[1] < n && t[2] < n && t[0] != t[1] && t[1] != t[2] && t[0] != t[2]).ToList();
            if (tris.Count == 0) return null;

            var fields = meta.Fields ?? new List<HomeMetaField>();
            int stride = meta.Stride;
            int need = fields.Count == 0 ? 0 : fields.Max(f => f.Offset + TypeSize(f.Type) * Math.Max(1, f.Count));
            if (fields.Count == 0 || stride < need || !fields.Any(f => f.Name == HmCore.HPosition))
            {
                log.Add($"{label}: stored vertex layout is unusable; converted the generic way");
                return null;
            }

            // normals and tangents
            var N = part.N != null && part.N.Count == n ? part.N : SmoothVertexNormals(part.P, tris);
            var U = part.U.Count == n ? part.U : Enumerable.Repeat(new V2(0, 0), n).ToList();
            List<double[]> T;
            if (part.Tangent != null && part.Tangent.Count == n) T = part.Tangent;
            else
            {
                T = HmCore.ComputeTangents(part.P, U, N.Select(v => v.Normalized()).ToList(), tris);
                if (fields.Any(f => f.Name == HmCore.HTangent)) log.Add($"{label}: no tangents in the file; recalculated them");
            }
            part.Custom.TryGetValue(TangentWAttribute, out var tanW);
            if (tanW != null && tanW.Count != n) tanW = null;

            var vb = new byte[n * stride];
            var missing = new HashSet<string>();
            foreach (var f in fields)
            {
                string custom = null;
                if (meta.CustomFields != null) meta.CustomFields.TryGetValue(f.Name.ToString("X8"), out custom);
                List<double[]> customVals = null;
                if (custom != null && !part.Custom.TryGetValue(custom, out customVals)) customVals = null;
                if (customVals != null && customVals.Count != n) customVals = null;
                for (int i = 0; i < n; i++)
                {
                    int o = i * stride + f.Offset;
                    double[] v;
                    if (f.Name == HmCore.HPosition) v = new[] { part.P[i].X, part.P[i].Y, part.P[i].Z };
                    else if (f.Name == HmCore.HNormal) v = new[] { N[i].X, N[i].Y, N[i].Z, T[i][3] >= 0 ? 1.0 : -1.0 };
                    else if (f.Name == HmCore.HTangent) v = new[] { T[i][0], T[i][1], T[i][2], tanW != null ? tanW[i][0] : meta.TangentW };
                    else if (f.Name == HmCore.HTexCoord)
                    {
                        var u1 = part.U1 != null && part.U1.Count == n ? part.U1[i] : new V2(0, 0);
                        if (f.Count > 2 && part.U1 == null) missing.Add("second UV set (lightmap UVs)");
                        v = new[] { U[i].U, U[i].V, u1.U, u1.V };
                    }
                    else if (customVals != null) v = customVals[i];
                    else if (f.Name == HmSkinning.HBlendWeights && part.Skin != null && part.Skin.W.Count == n) v = part.Skin.W[i];
                    else if (f.Name == HmSkinning.HBlendIndices && part.Skin != null && part.Skin.J.Count == n) v = part.Skin.J[i].Select(x => (double)x).ToArray();
                    else if (f.Name == HColour || (f.Type == HmCore.StUByteNorm && f.Count == 4))
                    {
                        if (part.Colour != null && part.Colour.Count == n) v = part.Colour[i];
                        else { v = new double[] { 1, 1, 1, 1 }; missing.Add("vertex colours (baked lighting; written as white)"); }
                    }
                    else
                    {
                        v = new double[4];
                        missing.Add("field " + HmCore.NameOf(f.Name) + " (written as zero)");
                    }
                    if (f.Type == HmCore.StPacked && f.Count == 1 && v.Length >= 3)
                        HmCore.PutU32(vb, o, HmSkinning.PackWeights(v[0], v[1], v[2]));
                    else
                        for (int k = 0; k < f.Count && k < v.Length; k++) WriteComponent(vb, o, f.Type, k, v[k]);
                }
            }
            foreach (var m in missing) log.Add($"{label}: the file has no {m}");

            List<int> indices;
            if (meta.Prim == HmCore.PrimTriangles)
            {
                indices = new List<int>(tris.Count * 3);
                foreach (var t in tris) indices.AddRange(t);
            }
            else if (part.StripIndices != null && part.StripIndices.Length >= 3 && part.StripIndices.All(x => x >= 0 && x < n))
            {
                indices = part.StripIndices.ToList();   // unedited: the original index buffer
            }
            else
            {
                indices = HmCore.Stripify(tris);
            }
            uint flags = meta.Flags;
            if (meta.HasDeformers)
            {
                flags &= ~1u;
                log.Add($"{label}: skinning / blend shape data isn't carried through; written as a static element");
            }
            return new HmMesh
            {
                Name = meta.NameHash != 0 ? meta.NameHash : HmCore.HashName(label),
                Prim = meta.Prim == HmCore.PrimTriangles ? HmCore.PrimTriangles : HmCore.PrimStrip,
                Indices = indices.Select(x => (ushort)x).ToArray(),
                VertexCount = n,
                Stride = stride,
                Vertices = vb,
                Fields = fields.Select(f => new HmField { Name = f.Name, Offset = (byte)f.Offset, Type = (byte)f.Type, Count = (byte)f.Count, Normalised = (byte)f.Normalised }).ToList(),
                Material = material,
                Flags = flags,
                Bound = KeepOrCompute(meta.Bound, new HashSet<int>(indices).Select(i => part.P[i]).ToList()),
                W60 = (ushort)meta.W60,
                W62 = (ushort)meta.W62,
            };
        }

        /// <summary>The stored sphere if every point is still inside it, else a new one.</summary>
        public static float[] KeepOrCompute(float[] stored, List<V3> pts)
        {
            if (stored != null && stored.Length == 4 && stored[3] > 0)
            {
                var c = new V3(stored[0], stored[1], stored[2]);
                double lim = stored[3] * 1.0001 + 1e-6;
                if (pts.All(p => (p - c).Length <= lim)) return stored.ToArray();
            }
            return HmCore.BoundingSphere(pts);
        }

        /// <summary>Area-weighted vertex normals (no vertex splitting), for files that lost their normals.</summary>
        private static List<V3> SmoothVertexNormals(List<V3> P, List<int[]> tris)
        {
            var acc = new V3[P.Count];
            foreach (var t in tris)
            {
                var fn = V3.Cross(P[t[1]] - P[t[0]], P[t[2]] - P[t[0]]);
                acc[t[0]] += fn; acc[t[1]] += fn; acc[t[2]] += fn;
            }
            return acc.Select(v => v.Normalized()).ToList();
        }

        // ------------------------------------------------------------------ textures

        /// <summary>
        /// DDS bytes for one texture of a round-tripped material. Unchanged pixels reuse the original DDS when it can be
        /// found; otherwise the image is encoded the way the original was (format, size, mip count).
        /// Returns null (and logs why) when there is nothing to write; the MDL keeps pointing at the original path.
        /// </summary>
        public static byte[] TextureBytes(HomeMetaTexture t, RgbaImage img, int maxSize, out string note)
        {
            note = null;
            string original = !string.IsNullOrEmpty(t.SourceDds) && File.Exists(t.SourceDds) ? t.SourceDds : null;
            bool tooBig = maxSize > 0 && (t.Width > maxSize || t.Height > maxSize);
            if (img == null)
            {
                if (original != null && !tooBig) return File.ReadAllBytes(original);
                if (original != null)
                {
                    try { img = ImageCodec.Decode(File.ReadAllBytes(original), original); }
                    catch (Exception) { img = null; }
                }
                if (img == null)
                {
                    note = original == null ? "not in the file and the original DDS wasn't found; the MDL still points at the game's copy" : "the original can't be resized; copied as it is";
                    return original != null ? File.ReadAllBytes(original) : null;
                }
            }
            bool unchanged = t.Crc != 0 && HmCore.Crc32(img.Data) == t.Crc;
            if (unchanged && original != null && !tooBig && !t.Cube) return File.ReadAllBytes(original);
            if (t.Cube)
            {
                note = "cube map: kept as it was";
                return original != null ? File.ReadAllBytes(original) : null;
            }
            string fmt = t.Format == "DXT1" || t.Format == "DXT5" || t.Format == "ARGB" ? t.Format : t.Format == "DXT3" ? "DXT5" : "auto";
            bool sameSize = img.Width == t.Width && img.Height == t.Height;
            int mipLimit = t.Mips > 0 && sameSize && t.Mips < Dds.FullMipCount(t.Width, t.Height) ? t.Mips : 0;
            var e = Dds.Encode(img, fmt, t.Mips != 1, true, maxSize, mipLimit);
            note = unchanged ? (original == null ? "re-encoded (original DDS not found)" : "re-encoded to fit the size limit") : "edited; encoded as " + e.Format + " " + e.Width + "x" + e.Height;
            return e.Bytes;
        }
    }
}

#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// Exporters: GLB (embedded textures), OBJ + MTL (+ textures folder) and binary FBX 7.4 (embedded textures),
// all from a common scene built from an HM model. C# port of the HOME_DEV_KIT exporters (ws/mdlb/src/14.js).
namespace Nautilus2027.Formats.Home
{
    public sealed class ExportTexture
    {
        public string Name, Ext, Mime;
        public byte[] Bytes;
        /// <summary>crc32 of the RGBA pixels the PNG was made from (0 if the bytes were passed through).</summary>
        public uint RgbaCrc;
    }

    public sealed class ExportMaterial
    {
        public string Name;
        /// <summary>default | alphatest | emissive | glass</summary>
        public string Kind = "default";
        public double AlphaRef = 0.5;
        public int? Colour, Normal, Specular, Emissive;
        /// <summary>Lightmap (written as a glTF occlusion texture on the second UV set).</summary>
        public int? Occlusion;
        public HomeMetaMaterial Home;
    }

    public sealed class ExportMesh
    {
        public string Name;
        public int Material = -1;
        public float[] P, N, UV;     // UV: v = 0 at the top (Home / glTF convention)
        public int[] Tris;
        public float[] UV1;          // second UV set, or null
        public int[] Strip;          // original triangle strip (written as glTF mode 5), or null
        public byte[] Colour;        // RGBA8 per vertex, or null
        public float[] Tangent;      // xyzw per vertex, or null
        /// <summary>Custom glTF attributes (name starts with "_"): values and component count.</summary>
        public Dictionary<string, (float[] Values, int Comps)> Custom = new Dictionary<string, (float[], int)>();
        public HomeMetaMesh Home;
        /// <summary>Skinning: 4 joint indices and 4 weights per vertex (null = not skinned).</summary>
        public int[] Joints;
        public float[] Weights;
        /// <summary>Index of the MDL element this mesh came from (-1 if none).</summary>
        public int SourceIndex = -1;
    }

    public sealed class ExportScene
    {
        public string Name;
        public List<ExportMesh> Meshes = new List<ExportMesh>();
        public List<ExportMaterial> Materials = new List<ExportMaterial>();
        public List<ExportTexture> Textures = new List<ExportTexture>();
        public List<string> Notes = new List<string>();
        /// <summary>Home round-trip description (null when not exporting one).</summary>
        public HomeMetaModel Home;
        /// <summary>Joints (parents before children), or empty.</summary>
        public List<ExportJoint> Joints = new List<ExportJoint>();
        public List<ExportAnimation> Animations = new List<ExportAnimation>();
        /// <summary>Skeleton facts kept for a round trip (glTF extras on the skin / skeleton root).</summary>
        public Dictionary<string, object> SkeletonExtras;
        /// <summary>Extras on the glTF scene (kept by Blender as scene custom properties).</summary>
        public Dictionary<string, object> SceneExtras;
    }

    public sealed class ExportJoint
    {
        public string Name;
        public int Parent = -1;
        public double[] T = { 0, 0, 0 }, R = { 0, 0, 0, 1 }, S = { 1, 1, 1 };
        /// <summary>Inverse bind matrix (column-major), for skinned meshes.</summary>
        public double[] InverseBind;
        public Dictionary<string, object> Extras;
    }

    public sealed class ExportAnimChannel
    {
        public int Joint;
        /// <summary>translation | rotation | scale</summary>
        public string Path;
        public float[] Times;
        /// <summary>Values (3 or 4 per key).</summary>
        public float[] Values;
        /// <summary>Tangents per second (same layout as Values) for CUBICSPLINE; null = LINEAR.</summary>
        public float[] Tangents;
    }

    public sealed class ExportAnimation
    {
        public string Name;
        public List<ExportAnimChannel> Channels = new List<ExportAnimChannel>();
        public Dictionary<string, object> Extras;
    }

    /// <summary>A texture for <see cref="HmExport.SceneFromModel"/>: either ready PNG/JPEG bytes or RGBA pixels.</summary>
    public sealed class ExportTextureSource
    {
        public string Name;
        public byte[] Bytes;
        public string Ext = "png", Mime = "image/png";
        public RgbaImage Rgba;
        /// <summary>The original DDS file and its header, when the texture came from one.</summary>
        public string SourcePath;
        public DdsInfo Info;
    }

    public static class HmExport
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public static string KindOf(HmMaterial mt)
        {
            if (HmCore.NameOf(mt.Type) == "emissive") return "emissive";
            if (mt.Type == 0x7F05531A) return "glass";
            if (mt.Flags == 0xE) return "alphatest";
            return "default";
        }

        /// <summary>
        /// Scene from an HM model. <paramref name="texFor"/>(Home texture path) returns the texture or null.
        /// With <paramref name="homeData"/>, every texture slot, the extra vertex data (lightmap UVs, vertex colours,
        /// tangents, other fields) and a full Home description for rebuilding the MDL are included.
        /// </summary>
        public static ExportScene SceneFromModel(string name, HmModel model, Func<string, ExportTextureSource> texFor,
            IList<string> meshNames = null, IList<string> matNames = null, bool homeData = false)
        {
            var scene = new ExportScene { Name = name };
            var texCache = new Dictionary<string, (int? Index, ExportTextureSource Src)>(StringComparer.OrdinalIgnoreCase);
            (int? Index, ExportTextureSource Src) GetTex(string path)
            {
                if (string.IsNullOrEmpty(path)) return (null, null);
                if (texCache.TryGetValue(path, out var cached)) return cached;
                ExportTextureSource t = null;
                try { t = texFor(path); }
                catch (Exception ex) { scene.Notes.Add($"texture {path}: {ex.Message}"); }
                int? index = null;
                if (t != null && (t.Bytes != null || t.Rgba != null))
                {
                    var outT = t.Bytes != null
                        ? new ExportTexture { Name = t.Name, Ext = t.Ext, Mime = t.Mime, Bytes = t.Bytes }
                        : new ExportTexture { Name = t.Name, Ext = "png", Mime = "image/png", Bytes = ImageCodec.EncodePng(t.Rgba), RgbaCrc = HmCore.Crc32(t.Rgba.Data) };
                    var used = new HashSet<string>(scene.Textures.Select(x => (x.Name + "." + x.Ext).ToLowerInvariant()));
                    string nm = outT.Name;
                    int n2 = 2;
                    while (used.Contains((nm + "." + outT.Ext).ToLowerInvariant())) nm = outT.Name + "_" + (n2++);
                    outT.Name = nm;
                    scene.Textures.Add(outT);
                    index = scene.Textures.Count - 1;
                }
                var r = (index, t);
                texCache[path] = r;
                return r;
            }
            if (homeData)
                scene.Home = new HomeMetaModel
                {
                    Version = model.Version,
                    Skeleton = model.Skeleton,
                    Word8 = model.Word8,
                    ModelBound = model.ModelBound,
                    Materials = new List<HomeMetaMaterial>(),
                    Meshes = new List<HomeMetaMesh>(),
                };
            for (int i = 0; i < model.Materials.Count; i++)
            {
                var mt = model.Materials[i];
                string matName = matNames != null && i < matNames.Count && !string.IsNullOrEmpty(matNames[i]) ? matNames[i] : DefaultMaterialName(mt, i);
                var em = new ExportMaterial { Name = matName, Kind = KindOf(mt), AlphaRef = mt.AlphaRef };
                if (homeData) em.Home = HmRoundTrip.Describe(mt, i, matName);
                foreach (var tex in mt.Textures)
                {
                    string slot = HmRoundTrip.SlotName(tex.Slot);
                    bool mapped = slot == "colourMap" || slot == "normalMap" || slot == "specularMap" || slot == "emissiveMap" || (homeData && tex.Slot == HmRoundTrip.HLightMap);
                    if (!mapped && !homeData) continue;
                    var (index, src) = GetTex(tex.Filename);
                    switch (slot)
                    {
                        case "colourMap": em.Colour ??= index; break;
                        case "normalMap": em.Normal ??= index; break;
                        case "specularMap": em.Specular ??= index; break;
                        case "emissiveMap": em.Emissive ??= index; break;
                        case "lightMap": em.Occlusion ??= index; break;
                    }
                    if (em.Home != null)
                        em.Home.Textures.Add(new HomeMetaTexture
                        {
                            Slot = tex.Slot,
                            SlotName = slot,
                            Path = tex.Filename,
                            WrapBits = tex.WrapBits,
                            BorderColour = tex.BorderColour,
                            LodMin = tex.LodMin,
                            LodMax = tex.LodMax,
                            LodBias = tex.LodBias,
                            Image = index.HasValue ? scene.Textures[index.Value].Name : null,
                            Crc = index.HasValue ? scene.Textures[index.Value].RgbaCrc : 0,
                            Format = src?.Info?.Format,
                            Width = src?.Info?.Width ?? 0,
                            Height = src?.Info?.Height ?? 0,
                            Mips = src?.Info?.Mips ?? 0,
                            Cube = src?.Info?.Cube ?? false,
                            SourceDds = src?.SourcePath,
                        });
                }
                scene.Materials.Add(em);
                if (em.Home != null) scene.Home.Materials.Add(em.Home);
            }
            for (int i = 0; i < model.Meshes.Count; i++)
            {
                var me = model.Meshes[i];
                var P = HmCore.MeshAttribute(me, HmCore.HPosition);
                if (P == null) { scene.Notes.Add($"mesh {i}: positions are in a format that can't be read; skipped"); continue; }
                var N = HmCore.MeshAttribute(me, HmCore.HNormal);
                var U = HmCore.MeshAttribute(me, HmCore.HTexCoord);
                var tris = HmCore.DecodeTriangles(me.Indices, me.Prim).Where(t => t.All(x => x < me.VertexCount)).ToList();
                int n = me.VertexCount;
                string meshName = meshNames != null && i < meshNames.Count && !string.IsNullOrEmpty(meshNames[i]) ? meshNames[i] : "mesh_" + i.ToString("00") + "_" + me.Name.ToString("x8");
                var m = new ExportMesh
                {
                    Name = meshName,
                    Material = me.Material >= 0 && me.Material < scene.Materials.Count ? me.Material : -1,
                    P = new float[n * 3],
                    N = new float[n * 3],
                    UV = new float[n * 2],
                    Tris = new int[tris.Count * 3],
                    SourceIndex = i,
                };
                for (int k = 0; k < n; k++)
                {
                    for (int c = 0; c < 3; c++) m.P[3 * k + c] = (float)P[k][c];
                    if (N != null && N[k].Length >= 3) { m.N[3 * k] = (float)N[k][0]; m.N[3 * k + 1] = (float)N[k][1]; m.N[3 * k + 2] = (float)N[k][2]; }
                    else m.N[3 * k + 1] = 1;
                    if (U != null && U[k].Length >= 2) { m.UV[2 * k] = (float)U[k][0]; m.UV[2 * k + 1] = (float)U[k][1]; }
                }
                for (int k = 0; k < tris.Count; k++) { m.Tris[3 * k] = tris[k][0]; m.Tris[3 * k + 1] = tris[k][1]; m.Tris[3 * k + 2] = tris[k][2]; }
                if (homeData)
                {
                    AddHomeVertexData(m, me, i, meshName, N, U, scene.Notes);
                    if (me.Prim == HmCore.PrimStrip && me.Indices.Length >= 3 && me.Indices.All(x => x < n))
                        m.Strip = me.Indices.Select(x => (int)x).ToArray();
                }
                scene.Meshes.Add(m);
                if (m.Home != null) scene.Home.Meshes.Add(m.Home);
            }
            return scene;
        }

        private static string DefaultMaterialName(HmMaterial mt, int i)
        {
            // readable and unique: index + the colour (or emissive) texture's name, e.g. 03_polished_concrete_c
            var t = mt.Textures.FirstOrDefault(x => HmCore.NameOf(x.Slot) == "colourMap") ?? mt.Textures.FirstOrDefault(x => HmCore.NameOf(x.Slot) == "emissiveMap");
            string stem = t != null ? Path.GetFileNameWithoutExtension(FileResolver.BaseName(t.Filename)) : null;
            if (string.IsNullOrEmpty(stem)) stem = Regex.Replace(HmCore.NameOf(mt.Type), "^0x", "t");
            return i.ToString("00") + "_" + Regex.Replace(stem, "[^A-Za-z0-9_\\-]+", "_").ToLowerInvariant();
        }

        /// <summary>Lightmap UVs, vertex colours, original tangents and any other fields of an MDL element.</summary>
        private static void AddHomeVertexData(ExportMesh m, HmMesh me, int index, string name, List<double[]> N, List<double[]> U, List<string> notes)
        {
            int n = me.VertexCount;
            m.Home = HmRoundTrip.Describe(me, index, name);
            if (U != null && U.Count == n && U[0].Length >= 4)
            {
                m.UV1 = new float[n * 2];
                for (int k = 0; k < n; k++) { m.UV1[2 * k] = (float)U[k][2]; m.UV1[2 * k + 1] = (float)U[k][3]; }
            }
            var T = HmCore.MeshAttribute(me, HmCore.HTangent);
            if (T != null && T.Count == n && T[0].Length >= 3)
            {
                m.Tangent = new float[n * 4];
                var tw = new float[n];
                bool varies = false;
                for (int k = 0; k < n; k++)
                {
                    double nw = N != null && N[k].Length >= 4 ? N[k][3] : 1;
                    m.Tangent[4 * k] = (float)T[k][0]; m.Tangent[4 * k + 1] = (float)T[k][1]; m.Tangent[4 * k + 2] = (float)T[k][2];
                    m.Tangent[4 * k + 3] = nw >= 0 ? 1f : -1f;
                    tw[k] = T[k].Length >= 4 ? (float)T[k][3] : 0f;
                    if (tw[k] != tw[0]) varies = true;
                }
                m.Home.TangentW = tw[0];
                if (varies) m.Custom[HmRoundTrip.TangentWAttribute] = (tw, 1);
            }
            foreach (var f in me.Fields)
            {
                if (f.Name == HmCore.HPosition || f.Name == HmCore.HNormal || f.Name == HmCore.HTangent || f.Name == HmCore.HTexCoord) continue;
                bool colour = (f.Name == HmRoundTrip.HColour || f.Type == HmCore.StUByteNorm) && f.Count == 4 && m.Colour == null;
                if (colour)
                {
                    m.Colour = new byte[n * 4];
                    for (int k = 0; k < n; k++)
                        for (int c = 0; c < 4; c++) m.Colour[4 * k + c] = me.Vertices[k * me.Stride + f.Offset + c];
                    continue;
                }
                var vals = f.Type == HmCore.StPacked && f.Count == 1 ? HmSkinning.DecodePackedWeights(me, f) : HmCore.MeshAttribute(me, f.Name);
                if (vals == null) { notes.Add($"mesh {index}: field {HmCore.NameOf(f.Name)} is packed and isn't exported"); continue; }
                int comps = vals.Count > 0 ? vals[0].Length : f.Count;
                string attr = HmRoundTrip.CustomAttribute(f.Name);
                var flat = new float[n * comps];
                for (int k = 0; k < n; k++)
                    for (int c = 0; c < comps; c++) flat[k * comps + c] = (float)vals[k][c];
                m.Custom[attr] = (flat, comps);
                m.Home.CustomFields ??= new Dictionary<string, string>();
                m.Home.CustomFields[f.Name.ToString("X8")] = attr;
            }
        }

        // ------------------------------------------------------------------ GLB

        public static byte[] WriteGlb(ExportScene scene)
        {
            var bin = new MemoryStream();
            var views = new List<Dictionary<string, object>>();
            var accessors = new List<Dictionary<string, object>>();
            void Pad4()
            {
                while (bin.Length % 4 != 0) bin.WriteByte(0);
            }
            int AddView(byte[] bytes, int? target)
            {
                Pad4();
                var v = new Dictionary<string, object> { ["buffer"] = 0, ["byteOffset"] = (int)bin.Length, ["byteLength"] = bytes.Length };
                if (target.HasValue) v["target"] = target.Value;
                views.Add(v);
                bin.Write(bytes, 0, bytes.Length);
                return views.Count - 1;
            }
            int AddFloats(float[] arr, int comps, string type, int? target, bool minmax)
            {
                var b = new byte[arr.Length * 4];
                Buffer.BlockCopy(arr, 0, b, 0, b.Length);   // little-endian on Windows/x86
                var a = new Dictionary<string, object> { ["bufferView"] = AddView(b, target), ["componentType"] = 5126, ["count"] = arr.Length / comps, ["type"] = type };
                if (minmax && arr.Length >= comps)
                {
                    var mn = new double[comps];
                    var mx = new double[comps];
                    for (int c = 0; c < comps; c++) { mn[c] = double.PositiveInfinity; mx[c] = double.NegativeInfinity; }
                    for (int i = 0; i < arr.Length; i++)
                    {
                        double v = arr[i];
                        if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                        int c = i % comps;
                        if (v < mn[c]) mn[c] = v;
                        if (v > mx[c]) mx[c] = v;
                    }
                    for (int c = 0; c < comps; c++) if (double.IsInfinity(mn[c])) { mn[c] = 0; mx[c] = 0; }
                    a["min"] = mn;
                    a["max"] = mx;
                }
                accessors.Add(a);
                return accessors.Count - 1;
            }
            int AddBytes(byte[] arr, int comps, string type, bool normalized)
            {
                var a = new Dictionary<string, object> { ["bufferView"] = AddView(arr, 34962), ["componentType"] = 5121, ["count"] = arr.Length / comps, ["type"] = type };
                if (normalized) a["normalized"] = true;
                accessors.Add(a);
                return accessors.Count - 1;
            }
            int AddU16(int[] arr, int comps, string type)
            {
                var b = new byte[arr.Length * 2];
                for (int i = 0; i < arr.Length; i++) { b[2 * i] = (byte)(arr[i] & 255); b[2 * i + 1] = (byte)((arr[i] >> 8) & 255); }
                accessors.Add(new Dictionary<string, object> { ["bufferView"] = AddView(b, 34962), ["componentType"] = 5123, ["count"] = arr.Length / comps, ["type"] = type });
                return accessors.Count - 1;
            }
            int AddIndices(int[] tris, int vertexCount)
            {
                byte[] b;
                int ct;
                if (vertexCount > 65535)
                {
                    b = new byte[tris.Length * 4];
                    Buffer.BlockCopy(tris, 0, b, 0, b.Length);
                    ct = 5125;
                }
                else
                {
                    b = new byte[tris.Length * 2];
                    for (int i = 0; i < tris.Length; i++) { b[2 * i] = (byte)(tris[i] & 255); b[2 * i + 1] = (byte)((tris[i] >> 8) & 255); }
                    ct = 5123;
                }
                accessors.Add(new Dictionary<string, object> { ["bufferView"] = AddView(b, 34963), ["componentType"] = ct, ["count"] = tris.Length, ["type"] = "SCALAR" });
                return accessors.Count - 1;
            }

            var images = scene.Textures.Select(t => (object)new Dictionary<string, object> { ["name"] = t.Name, ["mimeType"] = t.Mime, ["bufferView"] = AddView(t.Bytes, null) }).ToList();
            var textures = scene.Textures.Select((t, i) => (object)new Dictionary<string, object> { ["sampler"] = 0, ["source"] = i, ["name"] = t.Name }).ToList();
            bool usesSpec = false;
            var materials = new List<object>();
            foreach (var m in scene.Materials)
            {
                var pbr = new Dictionary<string, object> { ["metallicFactor"] = 0, ["roughnessFactor"] = 0.6 };
                var o = new Dictionary<string, object> { ["name"] = m.Name, ["pbrMetallicRoughness"] = pbr };
                if (m.Kind == "emissive")
                {
                    int? e = m.Emissive ?? m.Colour;
                    pbr["baseColorFactor"] = new[] { 0, 0, 0, 1 };
                    if (e.HasValue)
                    {
                        o["emissiveTexture"] = new Dictionary<string, object> { ["index"] = e.Value };
                        pbr["baseColorTexture"] = new Dictionary<string, object> { ["index"] = e.Value };
                    }
                    o["emissiveFactor"] = new[] { 1, 1, 1 };
                    o["alphaMode"] = "BLEND";
                }
                else
                {
                    if (m.Colour.HasValue) pbr["baseColorTexture"] = new Dictionary<string, object> { ["index"] = m.Colour.Value };
                    if (m.Kind == "alphatest") { o["alphaMode"] = "MASK"; o["alphaCutoff"] = m.AlphaRef > 0 ? m.AlphaRef : 0.5; }
                    if (m.Kind == "glass") o["alphaMode"] = "BLEND";
                }
                if (m.Normal.HasValue) o["normalTexture"] = new Dictionary<string, object> { ["index"] = m.Normal.Value };
                if (m.Occlusion.HasValue) o["occlusionTexture"] = new Dictionary<string, object> { ["index"] = m.Occlusion.Value, ["texCoord"] = 1 };
                if (m.Home != null) o["extras"] = new Dictionary<string, object> { ["home"] = HmRoundTrip.WithoutLocalPaths(m.Home) };
                if (m.Specular.HasValue)
                {
                    o["extensions"] = new Dictionary<string, object>
                    {
                        ["KHR_materials_specular"] = new Dictionary<string, object> { ["specularColorTexture"] = new Dictionary<string, object> { ["index"] = m.Specular.Value } },
                    };
                    usesSpec = true;
                }
                materials.Add(o);
            }
            var meshes = new List<object>();
            var nodes = new List<object>();
            foreach (var m in scene.Meshes)
            {
                int n = m.P.Length / 3;
                if (n == 0 || m.Tris.Length == 0) continue;
                var attributes = new Dictionary<string, object>
                {
                    ["POSITION"] = AddFloats(m.P, 3, "VEC3", 34962, true),
                    ["NORMAL"] = AddFloats(m.N, 3, "VEC3", 34962, false),
                    ["TEXCOORD_0"] = AddFloats(m.UV, 2, "VEC2", 34962, false),
                };
                if (m.UV1 != null && m.UV1.Length == n * 2) attributes["TEXCOORD_1"] = AddFloats(m.UV1, 2, "VEC2", 34962, false);
                if (m.Tangent != null && m.Tangent.Length == n * 4) attributes["TANGENT"] = AddFloats(m.Tangent, 4, "VEC4", 34962, false);
                if (m.Colour != null && m.Colour.Length == n * 4) attributes["COLOR_0"] = AddBytes(m.Colour, 4, "VEC4", true);
                bool skinned = m.Joints != null && m.Weights != null && m.Joints.Length == n * 4 && m.Weights.Length == n * 4 && scene.Joints.Count > 0;
                if (skinned)
                {
                    attributes["JOINTS_0"] = AddU16(m.Joints, 4, "VEC4");
                    attributes["WEIGHTS_0"] = AddFloats(m.Weights, 4, "VEC4", 34962, false);
                }
                foreach (var kv in m.Custom)
                {
                    int comps = kv.Value.Comps;
                    if (kv.Value.Values.Length != n * comps || comps < 1 || comps > 4) continue;
                    attributes[kv.Key] = AddFloats(kv.Value.Values, comps, comps == 1 ? "SCALAR" : "VEC" + comps, 34962, false);
                }
                var prim = m.Strip != null
                    ? new Dictionary<string, object> { ["attributes"] = attributes, ["indices"] = AddIndices(m.Strip, n), ["mode"] = 5 }
                    : new Dictionary<string, object> { ["attributes"] = attributes, ["indices"] = AddIndices(m.Tris, n), ["mode"] = 4 };
                if (m.Material >= 0 && m.Material < materials.Count) prim["material"] = m.Material;
                var meshJson = new Dictionary<string, object> { ["name"] = m.Name, ["primitives"] = new List<object> { prim } };
                if (m.Home != null) meshJson["extras"] = new Dictionary<string, object> { ["home"] = m.Home };
                meshes.Add(meshJson);
                var meshNode = new Dictionary<string, object> { ["name"] = m.Name, ["mesh"] = meshes.Count - 1 };
                if (skinned) meshNode["skin"] = 0;
                nodes.Add(meshNode);
            }
            int meshNodes = nodes.Count;
            var rootNodes = Enumerable.Range(0, meshNodes).ToList();
            List<object> skins = null, animations = null;
            if (scene.Joints.Count > 0)
            {
                int jb = nodes.Count;
                for (int j = 0; j < scene.Joints.Count; j++)
                {
                    var jt = scene.Joints[j];
                    double ql = Math.Sqrt(jt.R.Sum(x => x * x));
                    if (ql == 0) ql = 1;
                    var node = new Dictionary<string, object>
                    {
                        ["name"] = jt.Name,
                        ["translation"] = jt.T,
                        ["rotation"] = jt.R.Select(x => x / ql).ToArray(),
                        ["scale"] = jt.S,
                    };
                    var kids = Enumerable.Range(0, scene.Joints.Count).Where(k => scene.Joints[k].Parent == j).Select(k => jb + k).ToArray();
                    if (kids.Length > 0) node["children"] = kids;
                    if (jt.Extras != null) node["extras"] = new Dictionary<string, object> { ["home"] = jt.Extras };
                    nodes.Add(node);
                    if (jt.Parent < 0) rootNodes.Add(jb + j);
                }
                var skin = new Dictionary<string, object> { ["name"] = scene.Name + "_skeleton", ["joints"] = Enumerable.Range(jb, scene.Joints.Count).ToArray() };
                int root = scene.Joints.FindIndex(x => x.Parent < 0);
                if (root >= 0) skin["skeleton"] = jb + root;
                if (scene.Joints.All(x => x.InverseBind != null && x.InverseBind.Length == 16))
                    skin["inverseBindMatrices"] = AddFloats(scene.Joints.SelectMany(x => x.InverseBind.Select(v => (float)v)).ToArray(), 16, "MAT4", null, false);
                if (scene.SkeletonExtras != null) skin["extras"] = new Dictionary<string, object> { ["home"] = scene.SkeletonExtras };
                skins = new List<object> { skin };

                foreach (var an in scene.Animations)
                {
                    var samplers = new List<object>();
                    var chans = new List<object>();
                    foreach (var c in an.Channels)
                    {
                        if (c.Times == null || c.Times.Length == 0 || c.Joint < 0 || c.Joint >= scene.Joints.Count) continue;
                        int comps = c.Path == "rotation" ? 4 : 3;
                        int input = AddFloats(c.Times, 1, "SCALAR", null, true);
                        int output;
                        string interp;
                        if (c.Tangents != null && c.Tangents.Length == c.Values.Length)
                        {
                            // glTF CUBICSPLINE: in-tangent, value, out-tangent per key; Home uses one tangent per key (per second)
                            var o = new float[c.Values.Length * 3];
                            int nk = c.Times.Length;
                            for (int k = 0; k < nk; k++)
                                for (int q = 0; q < comps; q++)
                                {
                                    o[(3 * k) * comps + q] = c.Tangents[k * comps + q];
                                    o[(3 * k + 1) * comps + q] = c.Values[k * comps + q];
                                    o[(3 * k + 2) * comps + q] = c.Tangents[k * comps + q];
                                }
                            output = AddFloats(o, comps, comps == 4 ? "VEC4" : "VEC3", null, false);
                            interp = "CUBICSPLINE";
                        }
                        else
                        {
                            output = AddFloats(c.Values, comps, comps == 4 ? "VEC4" : "VEC3", null, false);
                            interp = "LINEAR";
                        }
                        samplers.Add(new Dictionary<string, object> { ["input"] = input, ["output"] = output, ["interpolation"] = interp });
                        chans.Add(new Dictionary<string, object> { ["sampler"] = samplers.Count - 1, ["target"] = new Dictionary<string, object> { ["node"] = jb + c.Joint, ["path"] = c.Path } });
                    }
                    if (chans.Count == 0) continue;
                    var aj = new Dictionary<string, object> { ["name"] = an.Name, ["samplers"] = samplers, ["channels"] = chans };
                    if (an.Extras != null) aj["extras"] = new Dictionary<string, object> { ["home"] = an.Extras };
                    (animations ??= new List<object>()).Add(aj);
                }
            }
            Pad4();
            var json = new Dictionary<string, object>
            {
                ["asset"] = AssetJson(scene),
                ["scene"] = 0,
                ["scenes"] = new List<object> { SceneJson(scene, rootNodes) },
                ["nodes"] = nodes,
                ["meshes"] = meshes,
                ["buffers"] = new List<object> { new Dictionary<string, object> { ["byteLength"] = (int)bin.Length } },
                ["bufferViews"] = views,
                ["accessors"] = accessors,
            };
            if (materials.Count > 0) json["materials"] = materials;
            if (meshes.Count == 0) json.Remove("meshes");
            if (skins != null) json["skins"] = skins;
            if (animations != null) json["animations"] = animations;
            if (images.Count > 0)
            {
                json["images"] = images;
                json["textures"] = textures;
                json["samplers"] = new List<object> { new Dictionary<string, object> { ["magFilter"] = 9729, ["minFilter"] = 9987, ["wrapS"] = 10497, ["wrapT"] = 10497 } };
            }
            if (usesSpec) json["extensionsUsed"] = new[] { "KHR_materials_specular" };
            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(json, HmRoundTrip.Json);
            int jpad = (4 - jsonBytes.Length % 4) % 4;
            if (jpad > 0)
            {
                var padded = new byte[jsonBytes.Length + jpad];
                Buffer.BlockCopy(jsonBytes, 0, padded, 0, jsonBytes.Length);
                for (int i = jsonBytes.Length; i < padded.Length; i++) padded[i] = 0x20;
                jsonBytes = padded;
            }
            var binAll = bin.ToArray();
            int total = 12 + 8 + jsonBytes.Length + 8 + binAll.Length;
            using (var outS = new MemoryStream(total))
            using (var w = new BinaryWriter(outS))
            {
                w.Write(0x46546C67u); w.Write(2u); w.Write((uint)total);
                w.Write((uint)jsonBytes.Length); w.Write(0x4E4F534Au); w.Write(jsonBytes);
                w.Write((uint)binAll.Length); w.Write(0x004E4942u); w.Write(binAll);
                w.Flush();
                return outS.ToArray();
            }
        }

        private static Dictionary<string, object> SceneJson(ExportScene scene, List<int> rootNodes)
        {
            var sc = new Dictionary<string, object> { ["name"] = scene.Name, ["nodes"] = rootNodes.ToArray() };
            if (scene.SceneExtras != null && scene.SceneExtras.Count > 0) sc["extras"] = scene.SceneExtras;
            return sc;
        }

        private static Dictionary<string, object> AssetJson(ExportScene scene)
        {
            var asset = new Dictionary<string, object> { ["version"] = "2.0", ["generator"] = "Nautilus2027 (Home MDL Builder port)" };
            if (scene.Home != null)
            {
                // model-level facts only; materials and meshes carry their own descriptions
                asset["extras"] = new Dictionary<string, object>
                {
                    ["home"] = new HomeMetaModel { Format = scene.Home.Format, Version = scene.Home.Version, Skeleton = scene.Home.Skeleton, Word8 = scene.Home.Word8, ModelBound = scene.Home.ModelBound },
                };
            }
            return asset;
        }

        // ------------------------------------------------------------------ OBJ + MTL

        private static string ObjSafe(string s) => Regex.Replace(s ?? "", "\\s+", "_");

        private static string Fmt(double v)
        {
            double r = Math.Round(v, 6, MidpointRounding.AwayFromZero);
            if (r == 0) r = 0;   // no "-0"
            return r.ToString("0.######", CultureInfo.InvariantCulture);
        }

        /// <summary>OBJ and MTL text; textures are referenced as <paramref name="texDir"/>/name.ext.</summary>
        public static (string Obj, string Mtl) WriteObj(ExportScene scene, string texDir)
        {
            var L = new StringBuilder();
            L.Append("# exported by Nautilus2027\n");
            if (scene.Materials.Count > 0) L.Append("mtllib ").Append(scene.Name).Append(".mtl\n");
            int baseIdx = 1;
            foreach (var m in scene.Meshes)
            {
                int n = m.P.Length / 3;
                L.Append("o ").Append(ObjSafe(m.Name)).Append('\n');
                for (int i = 0; i < n; i++) L.Append("v ").Append(Fmt(m.P[3 * i])).Append(' ').Append(Fmt(m.P[3 * i + 1])).Append(' ').Append(Fmt(m.P[3 * i + 2])).Append('\n');
                for (int i = 0; i < n; i++) L.Append("vt ").Append(Fmt(m.UV[2 * i])).Append(' ').Append(Fmt(1 - m.UV[2 * i + 1])).Append('\n');
                for (int i = 0; i < n; i++) L.Append("vn ").Append(Fmt(m.N[3 * i])).Append(' ').Append(Fmt(m.N[3 * i + 1])).Append(' ').Append(Fmt(m.N[3 * i + 2])).Append('\n');
                if (m.Material >= 0 && m.Material < scene.Materials.Count) L.Append("usemtl ").Append(ObjSafe(scene.Materials[m.Material].Name)).Append('\n');
                for (int k = 0; k + 2 < m.Tris.Length; k += 3)
                {
                    int a = m.Tris[k] + baseIdx, b = m.Tris[k + 1] + baseIdx, c = m.Tris[k + 2] + baseIdx;
                    L.Append($"f {a}/{a}/{a} {b}/{b}/{b} {c}/{c}/{c}\n");
                }
                baseIdx += n;
            }
            var M = new StringBuilder();
            M.Append("# exported by Nautilus2027\n");
            string Tp(int i) => texDir + "/" + scene.Textures[i].Name + "." + scene.Textures[i].Ext;
            foreach (var m in scene.Materials)
            {
                M.Append('\n').Append("newmtl ").Append(ObjSafe(m.Name)).Append('\n').Append("Ka 0 0 0\n")
                 .Append(m.Kind == "emissive" ? "Kd 0 0 0\n" : "Kd 1 1 1\n").Append("Ks 0.2 0.2 0.2\nNs 32\nd 1\nillum 2\n");
                var col = m.Colour;
                if (m.Kind == "emissive")
                {
                    var e = m.Emissive ?? col;
                    M.Append("Ke 1 1 1\n");
                    if (e.HasValue) M.Append("map_Ke ").Append(Tp(e.Value)).Append('\n');
                }
                else if (col.HasValue)
                {
                    M.Append("map_Kd ").Append(Tp(col.Value)).Append('\n');
                    if (m.Kind == "alphatest") M.Append("map_d ").Append(Tp(col.Value)).Append('\n');
                }
                if (m.Normal.HasValue) M.Append("map_Bump ").Append(Tp(m.Normal.Value)).Append('\n').Append("norm ").Append(Tp(m.Normal.Value)).Append('\n');
                if (m.Specular.HasValue) M.Append("map_Ks ").Append(Tp(m.Specular.Value)).Append('\n');
            }
            return (L.ToString(), M.ToString());
        }

        // ------------------------------------------------------------------ FBX (binary 7.4)
        // Fixed ids as written by Blender's exporter; the FBX SDK validates FileId/CreationTime/footer together.

        private static readonly byte[] FileId = { 0x28, 0xb3, 0x2a, 0xeb, 0xb6, 0x24, 0xcc, 0xc2, 0xbf, 0xc8, 0xb0, 0x2a, 0xa9, 0x2b, 0xfc, 0xf1 };
        private static readonly byte[] FootId = { 0xfa, 0xbc, 0xab, 0x09, 0xd0, 0xc8, 0xd4, 0x66, 0xb1, 0x76, 0xfb, 0x83, 0x1c, 0xf7, 0x26, 0x7e };
        private static readonly byte[] FootMagic = { 0xf8, 0x5a, 0x8c, 0x6a, 0xde, 0xf5, 0xd9, 0x7e, 0xec, 0xe9, 0x0c, 0xe3, 0x75, 0x8f, 0x29, 0x0b };
        private const string TimeId = "1970-01-01 10:00:00:000";
        private const int FbxVersion = 7400;

        private sealed class FProp
        {
            public char T;
            public object V;
        }

        private sealed class FNode
        {
            public string Name;
            public List<FProp> Props = new List<FProp>();
            public List<FNode> Children = new List<FNode>();
        }

        private static FNode N(string name, IEnumerable<FProp> props = null, IEnumerable<FNode> children = null) =>
            new FNode { Name = name, Props = props?.ToList() ?? new List<FProp>(), Children = children?.ToList() ?? new List<FNode>() };

        private static FProp[] Ps(params FProp[] p) => p;
        private static FNode[] Ns(params FNode[] n) => n;
        private static FProp S(string v) => new FProp { T = 'S', V = v };
        private static FProp I(int v) => new FProp { T = 'I', V = v };
        private static FProp L_(long v) => new FProp { T = 'L', V = v };
        private static FProp D(double v) => new FProp { T = 'D', V = v };
        private static FProp C(bool v) => new FProp { T = 'C', V = v };
        private static FProp R(byte[] v) => new FProp { T = 'R', V = v };
        private static FProp AD(double[] v) => new FProp { T = 'd', V = v };
        private static FProp AI(int[] v) => new FProp { T = 'i', V = v };
        private static FProp AF(float[] v) => new FProp { T = 'f', V = v };
        private static FProp AL(long[] v) => new FProp { T = 'l', V = v };
        private static string Cls(string name, string c) => name + "\u0000\u0001" + c;   // binary FBX "Name\x00\x01Class"

        private static FNode Prop(string name, string type, string label, string flags, params FProp[] vals) =>
            N("P", new[] { S(name), S(type), S(label), S(flags) }.Concat(vals));

        private static byte[] PropBytes(FProp p)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                switch (p.T)
                {
                    case 'C': w.Write((byte)'C'); w.Write((byte)((bool)p.V ? 1 : 0)); break;
                    case 'I': w.Write((byte)'I'); w.Write((int)p.V); break;
                    case 'L': w.Write((byte)'L'); w.Write((long)p.V); break;
                    case 'D': w.Write((byte)'D'); w.Write((double)p.V); break;
                    case 'S':
                    case 'R':
                    {
                        byte[] s = p.V is string str ? Utf8.GetBytes(str) : (byte[])p.V;
                        w.Write((byte)p.T); w.Write((uint)s.Length); w.Write(s);
                        break;
                    }
                    case 'd':
                    {
                        var a = (double[])p.V;
                        w.Write((byte)'d'); w.Write((uint)a.Length); w.Write(0u); w.Write((uint)(a.Length * 8));
                        foreach (var x in a) w.Write(x);
                        break;
                    }
                    case 'i':
                    {
                        var a = (int[])p.V;
                        w.Write((byte)'i'); w.Write((uint)a.Length); w.Write(0u); w.Write((uint)(a.Length * 4));
                        foreach (var x in a) w.Write(x);
                        break;
                    }
                    case 'f':
                    {
                        var a = (float[])p.V;
                        w.Write((byte)'f'); w.Write((uint)a.Length); w.Write(0u); w.Write((uint)(a.Length * 4));
                        foreach (var x in a) w.Write(x);
                        break;
                    }
                    case 'l':
                    {
                        var a = (long[])p.V;
                        w.Write((byte)'l'); w.Write((uint)a.Length); w.Write(0u); w.Write((uint)(a.Length * 8));
                        foreach (var x in a) w.Write(x);
                        break;
                    }
                    default: throw new InvalidOperationException("bad FBX property type " + p.T);
                }
                w.Flush();
                return ms.ToArray();
            }
        }

        private static void SerializeNode(FNode node, Stream outS)
        {
            long start = outS.Position;
            var name = Utf8.GetBytes(node.Name);
            outS.Write(new byte[13], 0, 13);
            outS.Write(name, 0, name.Length);
            long plen = 0;
            foreach (var p in node.Props)
            {
                var b = PropBytes(p);
                outS.Write(b, 0, b.Length);
                plen += b.Length;
            }
            if (node.Children.Count > 0 || node.Props.Count == 0)
            {
                foreach (var c in node.Children) SerializeNode(c, outS);
                outS.Write(new byte[13], 0, 13);
            }
            long end = outS.Position;
            var h = new byte[13];
            BitConverter.GetBytes((uint)end).CopyTo(h, 0);
            BitConverter.GetBytes((uint)node.Props.Count).CopyTo(h, 4);
            BitConverter.GetBytes((uint)plen).CopyTo(h, 8);
            h[12] = (byte)name.Length;
            outS.Position = start;
            outS.Write(h, 0, 13);
            outS.Position = end;
        }

        private static byte[] FbxBytes(IEnumerable<FNode> rootNodes)
        {
            using (var ms = new MemoryStream())
            {
                var head = Encoding.ASCII.GetBytes("Kaydara FBX Binary  ");
                ms.Write(head, 0, head.Length);
                ms.Write(new byte[] { 0, 0x1A, 0 }, 0, 3);
                ms.Write(BitConverter.GetBytes((uint)FbxVersion), 0, 4);
                foreach (var n in rootNodes) SerializeNode(n, ms);
                ms.Write(new byte[13], 0, 13);
                ms.Write(FootId, 0, FootId.Length);
                ms.Write(new byte[4], 0, 4);
                long cur = ms.Position;
                long pad = ((cur + 15) & ~15L) - cur;
                if (pad == 0) pad = 16;
                ms.Write(new byte[pad], 0, (int)pad);
                ms.Write(BitConverter.GetBytes((uint)FbxVersion), 0, 4);
                ms.Write(new byte[120], 0, 120);
                ms.Write(FootMagic, 0, FootMagic.Length);
                return ms.ToArray();
            }
        }

        // ------------------------------------------------------------------ FBX skeleton + animation

        private const long KTimePerSecond = 46186158000L;

        /// <summary>Quaternion (xyzw) -> FBX Euler XYZ in degrees (X applied first: R = Rz * Ry * Rx).</summary>
        internal static double[] QuatToEulerXyz(double x, double y, double z, double w)
        {
            double sinp = 2 * (w * y - z * x);
            double ex = Math.Atan2(2 * (w * x + y * z), 1 - 2 * (x * x + y * y));
            double ey = Math.Abs(sinp) >= 1 ? Math.CopySign(Math.PI / 2, sinp) : Math.Asin(sinp);
            double ez = Math.Atan2(2 * (w * z + x * y), 1 - 2 * (y * y + z * z));
            return new[] { ex * 180 / Math.PI, ey * 180 / Math.PI, ez * 180 / Math.PI };
        }

        private static bool ExtraNum(Dictionary<string, object> x, string k, out double v)
        {
            v = 0;
            if (x == null || !x.TryGetValue(k, out var o) || o == null) return false;
            try { v = Convert.ToDouble(o, CultureInfo.InvariantCulture); return true; }
            catch (Exception) { return false; }
        }

        /// <summary>Home round-trip data as FBX user properties (read back by FbxImport.ParseAnimation).</summary>
        private static List<FNode> HomeUserProps(Dictionary<string, object> x, params (string Key, bool Hex, bool Int)[] keys)
        {
            var outL = new List<FNode>();
            foreach (var (k, hex, isInt) in keys)
            {
                if (!ExtraNum(x, k, out double v)) continue;
                string name = "home_" + k;
                if (hex) outL.Add(Prop(name, "KString", "", "U", S("0x" + ((uint)v).ToString("x8", CultureInfo.InvariantCulture))));
                else if (isInt) outL.Add(Prop(name, "int", "Integer", "U", I((int)v)));
                else outL.Add(Prop(name, "double", "Number", "U", D(v)));
            }
            return outL;
        }

        /// <summary>World matrices of the scene's joints at bind (from the inverse bind matrices, else the joints' own TRS).</summary>
        private static double[][] JointBindWorld(ExportScene scene)
        {
            var w = new double[scene.Joints.Count][];
            for (int j = 0; j < w.Length; j++)
            {
                var jt = scene.Joints[j];
                if (jt.InverseBind != null && jt.InverseBind.Length == 16) { w[j] = HmSkeleton.Inverse(jt.InverseBind); continue; }
                var l = HmSkeleton.FromQts(jt.R, jt.T, jt.S);
                w[j] = jt.Parent >= 0 && jt.Parent < j && w[jt.Parent] != null ? Mat4.Mul(w[jt.Parent], l) : l;
            }
            return w;
        }

        /// <summary>
        /// Joints as LimbNode models, and each animation baked to 30 fps curves (FBX curves can't hold Home's Hermite keys
        /// exactly; use GLB for a lossless round trip). Returns the joints' model ids.
        /// </summary>
        private static long[] AddFbxSkeleton(ExportScene scene, List<FNode> objects, List<FNode> conns, Func<long> Id)
        {
            var modelIds = new long[scene.Joints.Count];
            for (int j = 0; j < scene.Joints.Count; j++)
            {
                var jt = scene.Joints[j];
                long attr = Id(), mid = Id();
                modelIds[j] = mid;
                var e = QuatToEulerXyz(jt.R[0], jt.R[1], jt.R[2], jt.R[3]);
                objects.Add(N("NodeAttribute", Ps(L_(attr), S(Cls(jt.Name, "NodeAttribute")), S("LimbNode")), Ns(
                    N("Properties70", null, Ns(Prop("Size", "double", "Number", "", D(10)))), N("TypeFlags", Ps(S("Skeleton"))))));
                var props = new List<FNode>
                {
                    Prop("InheritType", "enum", "", "", I(1)),
                    Prop("Lcl Translation", "Lcl Translation", "", "A", D(jt.T[0]), D(jt.T[1]), D(jt.T[2])),
                    Prop("Lcl Rotation", "Lcl Rotation", "", "A", D(e[0]), D(e[1]), D(e[2])),
                    Prop("Lcl Scaling", "Lcl Scaling", "", "A", D(jt.S[0]), D(jt.S[1]), D(jt.S[2])),
                };
                props.AddRange(HomeUserProps(jt.Extras, ("hash", true, false), ("index", false, true), ("flags", false, true), ("tw", false, false), ("sw", false, false)));
                if (jt.Parent < 0) props.AddRange(HomeUserProps(scene.SkeletonExtras, ("key", true, false), ("ownsMemory", false, true)));
                objects.Add(N("Model", Ps(L_(mid), S(Cls(jt.Name, "Model")), S("LimbNode")), Ns(
                    N("Version", Ps(I(232))),
                    N("Properties70", null, props),
                    N("MultiLayer", Ps(I(0))), N("MultiTake", Ps(I(0))), N("Shading", Ps(C(true))), N("Culling", Ps(S("CullingOff"))))));
                conns.Add(N("C", Ps(S("OO"), L_(attr), L_(mid))));
            }
            for (int j = 0; j < scene.Joints.Count; j++)
            {
                int p = scene.Joints[j].Parent;
                conns.Add(N("C", Ps(S("OO"), L_(modelIds[j]), L_(p >= 0 && p < modelIds.Length ? modelIds[p] : 0))));
            }

            foreach (var an in scene.Animations)
            {
                float end = an.Channels.Where(c => c.Times != null && c.Times.Length > 0).Select(c => c.Times[c.Times.Length - 1]).DefaultIfEmpty(0).Max();
                if (an.Extras != null && an.Extras.TryGetValue("duration", out var dv) && dv is float df && df > end) end = df;
                int frames = Math.Max(1, (int)Math.Ceiling(end * 30 - 1e-6));
                var times = Enumerable.Range(0, frames + 1).Select(f => Math.Min(end, f / 30.0)).ToArray();
                var ktimes = times.Select(t => (long)Math.Round(t * KTimePerSecond)).ToArray();
                long stack = Id(), layer = Id();
                var sprops = Ns(Prop("LocalStop", "KTime", "Time", "", L_(ktimes[ktimes.Length - 1])), Prop("ReferenceStop", "KTime", "Time", "", L_(ktimes[ktimes.Length - 1]))).ToList();
                sprops.AddRange(HomeUserProps(an.Extras, ("duration", false, false), ("flags", false, true)));
                objects.Add(N("AnimationStack", Ps(L_(stack), S(Cls(an.Name, "AnimStack")), S("")), Ns(N("Properties70", null, sprops))));
                objects.Add(N("AnimationLayer", Ps(L_(layer), S(Cls("BaseLayer", "AnimLayer")), S(""))));
                conns.Add(N("C", Ps(S("OO"), L_(layer), L_(stack))));
                foreach (var grp in an.Channels.Where(c => c.Times != null && c.Times.Length > 0 && c.Joint >= 0 && c.Joint < modelIds.Length).GroupBy(c => (c.Joint, c.Path)))
                {
                    var c = grp.First();
                    int comps = c.Path == "rotation" ? 4 : 3;
                    var sampled = times.Select(t => SampleChannel(c, comps, t)).ToList();
                    double[][] vals;
                    if (c.Path == "rotation")
                    {
                        vals = sampled.Select(q => QuatToEulerXyz(q[0], q[1], q[2], q[3])).ToArray();
                        for (int k = 1; k < vals.Length; k++)
                            for (int a = 0; a < 3; a++)
                                while (vals[k][a] - vals[k - 1][a] > 180) vals[k][a] -= 360;
                        for (int k = 1; k < vals.Length; k++)
                            for (int a = 0; a < 3; a++)
                                while (vals[k][a] - vals[k - 1][a] < -180) vals[k][a] += 360;
                    }
                    else vals = sampled.Select(v => v.Take(3).ToArray()).ToArray();
                    string prop = c.Path == "rotation" ? "Lcl Rotation" : c.Path == "translation" ? "Lcl Translation" : "Lcl Scaling";
                    string tag = c.Path == "rotation" ? "R" : c.Path == "translation" ? "T" : "S";
                    long cn = Id();
                    objects.Add(N("AnimationCurveNode", Ps(L_(cn), S(Cls(tag, "AnimCurveNode")), S("")), Ns(N("Properties70", null, Ns(
                        Prop("d|X", "Number", "", "A", D(vals[0][0])), Prop("d|Y", "Number", "", "A", D(vals[0][1])), Prop("d|Z", "Number", "", "A", D(vals[0][2])))))));
                    conns.Add(N("C", Ps(S("OO"), L_(cn), L_(layer))));
                    conns.Add(N("C", Ps(S("OP"), L_(cn), L_(modelIds[c.Joint]), S(prop))));
                    for (int a = 0; a < 3; a++)
                    {
                        long cv = Id();
                        objects.Add(N("AnimationCurve", Ps(L_(cv), S(Cls("", "AnimCurve")), S("")), Ns(
                            N("Default", Ps(D(vals[0][a]))), N("KeyVer", Ps(I(4009))),
                            N("KeyTime", Ps(AL(ktimes))), N("KeyValueFloat", Ps(AF(vals.Select(v => (float)v[a]).ToArray()))),
                            N("KeyAttrFlags", Ps(AI(new[] { 0x104 }))),            // linear interpolation
                            N("KeyAttrDataFloat", Ps(AF(new float[] { 0, 0, 0, 0 }))),
                            N("KeyAttrRefCount", Ps(AI(new[] { ktimes.Length }))))));
                        conns.Add(N("C", Ps(S("OP"), L_(cv), L_(cn), S(a == 0 ? "d|X" : a == 1 ? "d|Y" : "d|Z"))));
                    }
                }
            }
            return modelIds;
        }

        /// <summary>
        /// Skin deformers for the skinned meshes: one Skin per mesh, one Cluster per joint it uses (control point indices,
        /// weights, Transform = inverse bind, TransformLink = joint world at bind), and a BindPose for the whole rig.
        /// Meshes are written in world space, so their own world matrix is the identity.
        /// </summary>
        private static void AddFbxSkin(ExportScene scene, List<(ExportMesh Mesh, long Geo, long Model)> meshes, long[] jointIds,
                                       List<FNode> objects, List<FNode> conns, Func<long> Id)
        {
            var bindW = JointBindWorld(scene);
            var ident = Mat4.Identity();
            var poseNodes = new List<FNode>();
            var posed = new HashSet<long>();
            void PoseNode(long id, double[] m)
            {
                if (posed.Add(id)) poseNodes.Add(N("PoseNode", null, Ns(N("Node", Ps(L_(id))), N("Matrix", Ps(AD((double[])m.Clone()))))));
            }
            int skinned = 0;
            foreach (var (m, geo, model) in meshes)
            {
                if (m.Joints == null || m.Weights == null) continue;
                int n = m.P.Length / 3;
                if (m.Joints.Length < n * 4 || m.Weights.Length < n * 4) continue;
                var perJoint = new SortedDictionary<int, (List<int> Idx, List<double> W)>();
                for (int v = 0; v < n; v++)
                    for (int c = 0; c < 4; c++)
                    {
                        int j = m.Joints[4 * v + c];
                        double w = m.Weights[4 * v + c];
                        if (w <= 0 || j < 0 || j >= jointIds.Length) continue;
                        if (!perJoint.TryGetValue(j, out var e)) perJoint[j] = e = (new List<int>(), new List<double>());
                        int at = e.Idx.Count - 1;
                        if (at >= 0 && e.Idx[at] == v) e.W[at] += w;   // the same joint twice on one vertex
                        else { e.Idx.Add(v); e.W.Add(w); }
                    }
                if (perJoint.Count == 0) continue;
                long skin = Id();
                objects.Add(N("Deformer", Ps(L_(skin), S(Cls(m.Name, "Deformer")), S("Skin")), Ns(
                    N("Version", Ps(I(101))), N("Link_DeformAcuracy", Ps(D(50))))));
                conns.Add(N("C", Ps(S("OO"), L_(skin), L_(geo))));
                foreach (var kv in perJoint)
                {
                    int j = kv.Key;
                    long cl = Id();
                    var link = bindW[j];
                    var xf = HmSkeleton.Inverse(link);
                    objects.Add(N("Deformer", Ps(L_(cl), S(Cls(scene.Joints[j].Name, "SubDeformer")), S("Cluster")), Ns(
                        N("Version", Ps(I(100))), N("UserData", Ps(S(""), S(""))),
                        N("Indexes", Ps(AI(kv.Value.Idx.ToArray()))), N("Weights", Ps(AD(kv.Value.W.ToArray()))),
                        N("Transform", Ps(AD(xf))), N("TransformLink", Ps(AD((double[])link.Clone()))))));
                    conns.Add(N("C", Ps(S("OO"), L_(cl), L_(skin))));
                    conns.Add(N("C", Ps(S("OO"), L_(jointIds[j]), L_(cl))));
                }
                PoseNode(model, ident);
                skinned++;
            }
            if (skinned == 0) return;
            for (int j = 0; j < jointIds.Length; j++) PoseNode(jointIds[j], bindW[j]);
            long pose = Id();
            var pc = Ns(N("Type", Ps(S("BindPose"))), N("Version", Ps(I(100))), N("NbPoseNodes", Ps(I(poseNodes.Count)))).ToList();
            pc.AddRange(poseNodes);
            objects.Add(N("Pose", Ps(L_(pose), S(Cls("BindPose", "Pose")), S("BindPose")), pc));
        }

        /// <summary>Value of an export channel at time t (cubic Hermite with per-second tangents, or linear).</summary>
        internal static double[] SampleChannel(ExportAnimChannel c, int comps, double t)
        {
            int n = c.Times.Length;
            double[] V(int k) => Enumerable.Range(0, comps).Select(q => (double)c.Values[k * comps + q]).ToArray();
            if (n == 1 || t <= c.Times[0]) return V(0);
            if (t >= c.Times[n - 1]) return V(n - 1);
            int lo = 0, hi = n - 1;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (c.Times[mid] <= t) lo = mid; else hi = mid; }
            double dt = c.Times[lo + 1] - c.Times[lo];
            if (!(dt > 0)) return V(lo);
            double u = (t - c.Times[lo]) / dt;
            var a = V(lo); var b = V(lo + 1);
            var r = new double[comps];
            if (c.Tangents == null)
            {
                for (int q = 0; q < comps; q++) r[q] = a[q] + (b[q] - a[q]) * u;
            }
            else
            {
                double u2 = u * u, u3 = u2 * u, h00 = 2 * u3 - 3 * u2 + 1, h10 = (u3 - 2 * u2 + u) * dt, h01 = -2 * u3 + 3 * u2, h11 = (u3 - u2) * dt;
                for (int q = 0; q < comps; q++) r[q] = h00 * a[q] + h10 * c.Tangents[lo * comps + q] + h01 * b[q] + h11 * c.Tangents[(lo + 1) * comps + q];
            }
            if (comps == 4)
            {
                double l = Math.Sqrt(r.Sum(x => x * x));
                if (l > 0) for (int q = 0; q < 4; q++) r[q] /= l;
            }
            return r;
        }

        private static long TimeSpanStop(ExportScene scene)
        {
            double end = 1;
            foreach (var an in scene.Animations)
                foreach (var c in an.Channels)
                    if (c.Times != null && c.Times.Length > 0) end = Math.Max(end, c.Times[c.Times.Length - 1]);
            return (long)Math.Round(end * KTimePerSecond);
        }

        public static byte[] WriteFbx(ExportScene scene)
        {
            long nextId = 1000000;
            long Id() => nextId++;
            var objects = new List<FNode>();
            var conns = new List<FNode>();
            var now = DateTime.Now;
            const string app = "Nautilus2027";
            var header = N("FBXHeaderExtension", null, Ns(
                N("FBXHeaderVersion", Ps(I(1003))), N("FBXVersion", Ps(I(FbxVersion))), N("EncryptionType", Ps(I(0))),
                N("CreationTimeStamp", null, Ns(N("Version", Ps(I(1000))), N("Year", Ps(I(now.Year))), N("Month", Ps(I(now.Month))), N("Day", Ps(I(now.Day))),
                    N("Hour", Ps(I(now.Hour))), N("Minute", Ps(I(now.Minute))), N("Second", Ps(I(now.Second))), N("Millisecond", Ps(I(0))))),
                N("Creator", Ps(S(app))),
                N("SceneInfo", Ps(S(Cls("GlobalInfo", "SceneInfo")), S("UserData")), Ns(
                    N("Type", Ps(S("UserData"))), N("Version", Ps(I(100))),
                    N("MetaData", null, Ns(N("Version", Ps(I(100))), N("Title", Ps(S(scene.Name))), N("Subject", Ps(S(""))), N("Author", Ps(S(""))), N("Keywords", Ps(S(""))), N("Revision", Ps(S(""))), N("Comment", Ps(S(""))))),
                    N("Properties70", null, Ns(
                        Prop("DocumentUrl", "KString", "Url", "", S(scene.Name + ".fbx")), Prop("SrcDocumentUrl", "KString", "Url", "", S(scene.Name + ".fbx")),
                        Prop("Original", "Compound", "", ""), Prop("Original|ApplicationVendor", "KString", "", "", S(app)), Prop("Original|ApplicationName", "KString", "", "", S(app)),
                        Prop("Original|ApplicationVersion", "KString", "", "", S("1.0")), Prop("Original|DateTime_GMT", "DateTime", "", "", S("01/01/1970 00:00:00.000")), Prop("Original|FileName", "KString", "", "", S(scene.Name + ".fbx")),
                        Prop("LastSaved", "Compound", "", ""), Prop("LastSaved|ApplicationVendor", "KString", "", "", S(app)), Prop("LastSaved|ApplicationName", "KString", "", "", S(app)),
                        Prop("LastSaved|ApplicationVersion", "KString", "", "", S("1.0")), Prop("LastSaved|DateTime_GMT", "DateTime", "", "", S("01/01/1970 00:00:00.000"))))))));
            var globalSettings = N("GlobalSettings", null, Ns(N("Version", Ps(I(1000))), N("Properties70", null, Ns(
                Prop("UpAxis", "int", "Integer", "", I(1)), Prop("UpAxisSign", "int", "Integer", "", I(1)), Prop("FrontAxis", "int", "Integer", "", I(2)), Prop("FrontAxisSign", "int", "Integer", "", I(1)),
                Prop("CoordAxis", "int", "Integer", "", I(0)), Prop("CoordAxisSign", "int", "Integer", "", I(1)), Prop("OriginalUpAxis", "int", "Integer", "", I(1)), Prop("OriginalUpAxisSign", "int", "Integer", "", I(1)),
                Prop("UnitScaleFactor", "double", "Number", "", D(100)), Prop("OriginalUnitScaleFactor", "double", "Number", "", D(100)),
                Prop("AmbientColor", "ColorRGB", "Color", "", D(0), D(0), D(0)), Prop("DefaultCamera", "KString", "", "", S("Producer Perspective")),
                Prop("TimeMode", "enum", "", "", I(6)), Prop("TimeSpanStart", "KTime", "Time", "", L_(0)), Prop("TimeSpanStop", "KTime", "Time", "", L_(TimeSpanStop(scene))), Prop("CustomFrameRate", "double", "Number", "", D(30))))));
            long docId = Id();
            var documents = N("Documents", null, Ns(N("Count", Ps(I(1))), N("Document", Ps(L_(docId), S(scene.Name), S("Scene")), Ns(
                N("Properties70", null, Ns(Prop("SourceObject", "object", "", ""), Prop("ActiveAnimStackName", "KString", "", "", S("")))), N("RootNode", Ps(L_(0)))))));

            // textures (+ embedded video clips)
            var texIds = scene.Textures.Select(t =>
            {
                long vid = Id(), tid = Id();
                string fname = "TEXTURES/" + t.Name + "." + t.Ext;
                objects.Add(N("Video", Ps(L_(vid), S(Cls(t.Name, "Video")), S("Clip")), Ns(
                    N("Type", Ps(S("Clip"))), N("Properties70", null, Ns(Prop("Path", "KString", "XRefUrl", "", S(fname)), Prop("RelPath", "KString", "XRefUrl", "", S(fname)))),
                    N("UseMipMap", Ps(I(0))), N("Filename", Ps(S(fname))), N("RelativeFilename", Ps(S(fname))), N("Content", Ps(R(t.Bytes))))));
                objects.Add(N("Texture", Ps(L_(tid), S(Cls(t.Name, "Texture")), S("")), Ns(
                    N("Type", Ps(S("TextureVideoClip"))), N("Version", Ps(I(202))), N("TextureName", Ps(S(Cls(t.Name, "Texture")))),
                    N("Properties70", null, Ns(Prop("UseMaterial", "bool", "", "", I(1)))),
                    N("Media", Ps(S(Cls(t.Name, "Video")))), N("FileName", Ps(S(fname))), N("RelativeFilename", Ps(S(fname))),
                    N("ModelUVTranslation", Ps(D(0), D(0))), N("ModelUVScaling", Ps(D(1), D(1))), N("Texture_Alpha_Source", Ps(S("None"))), N("Cropping", Ps(I(0), I(0), I(0), I(0))))));
                conns.Add(N("C", Ps(S("OO"), L_(vid), L_(tid))));
                return tid;
            }).ToList();
            // materials
            var matIds = scene.Materials.Select(m =>
            {
                long mid = Id();
                bool em = m.Kind == "emissive";
                double dcol = em ? 0 : 1, ecol = em ? 1 : 0;
                objects.Add(N("Material", Ps(L_(mid), S(Cls(m.Name, "Material")), S("")), Ns(
                    N("Version", Ps(I(102))), N("ShadingModel", Ps(S("phong"))), N("MultiLayer", Ps(I(0))),
                    N("Properties70", null, Ns(Prop("DiffuseColor", "Color", "", "A", D(dcol), D(dcol), D(dcol)), Prop("DiffuseFactor", "Number", "", "A", D(1)),
                        Prop("EmissiveColor", "Color", "", "A", D(ecol), D(ecol), D(ecol)), Prop("EmissiveFactor", "Number", "", "A", D(1)),
                        Prop("SpecularColor", "Color", "", "A", D(0.2), D(0.2), D(0.2)), Prop("Shininess", "Number", "", "A", D(32)), Prop("ShininessExponent", "Number", "", "A", D(32)),
                        Prop("TransparencyFactor", "Number", "", "A", D(0)), Prop("Opacity", "Number", "", "A", D(1))).Concat(
                            m.Home != null ? new[] { Prop("home_material", "KString", "", "U", S(JsonSerializer.Serialize(HmRoundTrip.WithoutLocalPaths(m.Home), HmRoundTrip.Json))) } : Array.Empty<FNode>())))));
                void Link(int? tex, string prop)
                {
                    if (tex.HasValue && tex.Value >= 0 && tex.Value < texIds.Count) conns.Add(N("C", Ps(S("OP"), L_(texIds[tex.Value]), L_(mid), S(prop))));
                }
                if (em) Link(m.Emissive ?? m.Colour, "EmissiveColor");
                else
                {
                    Link(m.Colour, "DiffuseColor");
                    if (m.Kind == "alphatest" || m.Kind == "glass") Link(m.Colour, "TransparentColor");
                }
                Link(m.Normal, "NormalMap");
                Link(m.Specular, "SpecularColor");
                return mid;
            }).ToList();
            // meshes
            var meshIds = new List<(ExportMesh Mesh, long Geo, long Model)>();
            foreach (var m in scene.Meshes)
            {
                long gid = Id(), modelId = Id();
                meshIds.Add((m, gid, modelId));
                int n = m.P.Length / 3;
                var pvi = new int[m.Tris.Length];
                var nrm = new double[m.Tris.Length * 3];
                for (int k = 0; k < m.Tris.Length; k++)
                {
                    int v = m.Tris[k];
                    pvi[k] = k % 3 == 2 ? ~v : v;
                    nrm[3 * k] = m.N[3 * v]; nrm[3 * k + 1] = m.N[3 * v + 1]; nrm[3 * k + 2] = m.N[3 * v + 2];
                }
                // FBX V runs bottom-up; computed in double so 1 - (1 - v) gives the float back exactly
                double[] FlipV(float[] src)
                {
                    var r = new double[n * 2];
                    for (int i = 0; i < n; i++) { r[2 * i] = src[2 * i]; r[2 * i + 1] = 1.0 - (double)src[2 * i + 1]; }
                    return r;
                }
                FNode LayerEl(string type, int idx, string name, string dataName, double[] data, string indexName = null)
                {
                    var ch = new List<FNode> { N("Version", Ps(I(101))), N("Name", Ps(S(name))), N("MappingInformationType", Ps(S("ByPolygonVertex"))),
                        N("ReferenceInformationType", Ps(S(indexName != null ? "IndexToDirect" : "Direct"))), N(dataName, Ps(AD(data))) };
                    if (indexName != null) ch.Add(N(indexName, Ps(AI((int[])m.Tris.Clone()))));
                    return N(type, Ps(I(idx)), ch);
                }
                FNode Ref(string type, int idx) => N("LayerElement", null, Ns(N("Type", Ps(S(type))), N("TypedIndex", Ps(I(idx)))));
                var geoChildren = new List<FNode>
                {
                    N("Properties70"), N("GeometryVersion", Ps(I(124))),
                    N("Vertices", Ps(AD(m.P.Select(x => (double)x).ToArray()))), N("PolygonVertexIndex", Ps(AI(pvi))),
                    N("LayerElementNormal", Ps(I(0)), Ns(N("Version", Ps(I(101))), N("Name", Ps(S(""))), N("MappingInformationType", Ps(S("ByPolygonVertex"))), N("ReferenceInformationType", Ps(S("Direct"))), N("Normals", Ps(AD(nrm))))),
                };
                var layer0 = new List<FNode> { N("Version", Ps(I(100))), Ref("LayerElementNormal", 0) };
                bool hasTan = m.Tangent != null && m.Tangent.Length >= n * 4;
                if (hasTan)
                {
                    // tangent xyz, and a binormal whose direction gives the handedness (w) back
                    var tan = new double[m.Tris.Length * 3];
                    var bin = new double[m.Tris.Length * 3];
                    for (int k = 0; k < m.Tris.Length; k++)
                    {
                        int v = m.Tris[k];
                        double tx = m.Tangent[4 * v], ty = m.Tangent[4 * v + 1], tz = m.Tangent[4 * v + 2], w = m.Tangent[4 * v + 3] < 0 ? -1 : 1;
                        double nx = m.N[3 * v], ny = m.N[3 * v + 1], nz = m.N[3 * v + 2];
                        tan[3 * k] = tx; tan[3 * k + 1] = ty; tan[3 * k + 2] = tz;
                        bin[3 * k] = (ny * tz - nz * ty) * w; bin[3 * k + 1] = (nz * tx - nx * tz) * w; bin[3 * k + 2] = (nx * ty - ny * tx) * w;
                    }
                    geoChildren.Add(N("LayerElementBinormal", Ps(I(0)), Ns(N("Version", Ps(I(101))), N("Name", Ps(S(""))), N("MappingInformationType", Ps(S("ByPolygonVertex"))), N("ReferenceInformationType", Ps(S("Direct"))), N("Binormals", Ps(AD(bin))))));
                    geoChildren.Add(N("LayerElementTangent", Ps(I(0)), Ns(N("Version", Ps(I(101))), N("Name", Ps(S(""))), N("MappingInformationType", Ps(S("ByPolygonVertex"))), N("ReferenceInformationType", Ps(S("Direct"))), N("Tangents", Ps(AD(tan))))));
                    layer0.Add(Ref("LayerElementBinormal", 0));
                    layer0.Add(Ref("LayerElementTangent", 0));
                }
                geoChildren.Add(LayerEl("LayerElementUV", 0, "UVMap", "UV", FlipV(m.UV), "UVIndex"));
                layer0.Add(Ref("LayerElementUV", 0));
                if (m.Colour != null && m.Colour.Length >= n * 4)
                {
                    var col = new double[n * 4];
                    for (int i = 0; i < n * 4; i++) col[i] = m.Colour[i] / 255.0;
                    geoChildren.Add(LayerEl("LayerElementColor", 0, "Col", "Colors", col, "ColorIndex"));
                    layer0.Add(Ref("LayerElementColor", 0));
                }
                geoChildren.Add(N("LayerElementMaterial", Ps(I(0)), Ns(N("Version", Ps(I(101))), N("Name", Ps(S(""))), N("MappingInformationType", Ps(S("AllSame"))), N("ReferenceInformationType", Ps(S("IndexToDirect"))), N("Materials", Ps(AI(new[] { 0 }))))));
                layer0.Add(Ref("LayerElementMaterial", 0));
                geoChildren.Add(N("Layer", Ps(I(0)), layer0));
                if (m.UV1 != null && m.UV1.Length >= n * 2)
                {
                    geoChildren.Add(LayerEl("LayerElementUV", 1, "Lightmap", "UV", FlipV(m.UV1), "UVIndex"));
                    geoChildren.Add(N("Layer", Ps(I(1)), Ns(N("Version", Ps(I(100))), Ref("LayerElementUV", 1))));
                }
                // Home round-trip data other tools ignore: the original strip and the MDL's extra vertex fields
                if (m.Home != null)
                {
                    if (m.Strip != null) geoChildren.Add(N("HomeStrip", Ps(AI(m.Strip))));
                    foreach (var kv in m.Custom)
                        geoChildren.Add(N("HomeAttribute", Ps(S(kv.Key), I(kv.Value.Comps), AD(kv.Value.Values.Select(x => (double)x).ToArray()))));
                }
                objects.Add(N("Geometry", Ps(L_(gid), S(Cls(m.Name, "Geometry")), S("Mesh")), geoChildren));
                var mprops = new List<FNode> { Prop("DefaultAttributeIndex", "int", "Integer", "", I(0)), Prop("InheritType", "enum", "", "", I(1)) };
                if (m.Home != null)
                {
                    mprops.Add(Prop("home_mesh", "KString", "", "U", S(JsonSerializer.Serialize(m.Home, HmRoundTrip.Json))));
                    if (scene.Home != null && meshIds.Count == 1)
                        mprops.Add(Prop("home_model", "KString", "", "U", S(JsonSerializer.Serialize(
                            new HomeMetaModel { Format = scene.Home.Format, Version = scene.Home.Version, Skeleton = scene.Home.Skeleton, Word8 = scene.Home.Word8, ModelBound = scene.Home.ModelBound }, HmRoundTrip.Json))));
                }
                objects.Add(N("Model", Ps(L_(modelId), S(Cls(m.Name, "Model")), S("Mesh")), Ns(
                    N("Version", Ps(I(232))), N("Properties70", null, mprops),
                    N("MultiLayer", Ps(I(0))), N("MultiTake", Ps(I(0))), N("Shading", Ps(C(true))), N("Culling", Ps(S("CullingOff"))))));
                conns.Add(N("C", Ps(S("OO"), L_(modelId), L_(0))));
                conns.Add(N("C", Ps(S("OO"), L_(gid), L_(modelId))));
                if (m.Material >= 0 && m.Material < matIds.Count) conns.Add(N("C", Ps(S("OO"), L_(matIds[m.Material]), L_(modelId))));
            }
            if (scene.Joints.Count > 0)
            {
                var jointIds = AddFbxSkeleton(scene, objects, conns, Id);
                AddFbxSkin(scene, meshIds, jointIds, objects, conns, Id);
            }
            int Count(string t) => objects.Count(o => o.Name == t);
            var defChildren = new List<FNode> { N("Version", Ps(I(100))), N("Count", Ps(I(1 + objects.Count))), N("ObjectType", Ps(S("GlobalSettings")), Ns(N("Count", Ps(I(1))))) };
            foreach (var t in new[] { "Model", "Geometry", "Material", "Texture", "Video", "NodeAttribute", "AnimationStack", "AnimationLayer", "AnimationCurveNode", "AnimationCurve", "Deformer", "Pose" })
                if (Count(t) > 0) defChildren.Add(N("ObjectType", Ps(S(t)), Ns(N("Count", Ps(I(Count(t)))))));
            var defs = N("Definitions", null, defChildren);
            return FbxBytes(new[]
            {
                header, N("FileId", Ps(R(FileId))), N("CreationTime", Ps(S(TimeId))), N("Creator", Ps(S(app))),
                globalSettings, documents, N("References"), defs, N("Objects", null, objects), N("Connections", null, conns), N("Takes", null, Ns(N("Current", Ps(S(""))))),
            });
        }
    }
}

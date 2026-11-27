#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mdl2Hkx;
using Nautilus2027.Formats.Home;

namespace Nautilus2027.Formats
{
    /// <summary>One drawable mesh for the preview window: world-space triangles, optional normals / UVs / texture.</summary>
    public sealed class PreviewMesh
    {
        public string Name;
        public float[] P;          // xyz per vertex
        public float[] N;          // xyz per vertex, or null (smooth normals are made by the viewer)
        public float[] UV;         // uv per vertex, v = 0 at the top, or null
        public int[] Tris;
        /// <summary>Colour texture as PNG / JPEG bytes, or as pixels; both null = untextured.</summary>
        public byte[] ImageBytes;
        public RgbaImage Image;
        /// <summary>Base colour 0xAARRGGBB used without a texture.</summary>
        public uint Colour = 0xFFB4B4B4;
        public bool Emissive;
    }

    /// <summary>A group of meshes the viewer can show / hide as one (the model, its collision, the original collision...).</summary>
    public sealed class PreviewLayer
    {
        public string Name;
        public List<PreviewMesh> Meshes = new List<PreviewMesh>();
        /// <summary>Colour 0xAARRGGBB for the whole layer (alpha &lt; 255 = see-through); null = each mesh's own colour / texture.</summary>
        public uint? Tint;
        public bool Visible = true;
    }

    public sealed class PreviewData
    {
        public string Title;
        public List<PreviewLayer> Layers = new List<PreviewLayer>();
        public List<string> Info = new List<string>();
        /// <summary>Spawn point (Home coordinates), if a scene gave one.</summary>
        public double[] Spawn;
        /// <summary>Something that moves (particles, a skeleton playing a clip), drawn on top of the layers; null = still.</summary>
        public PreviewAnimation Animation;
    }

    /// <summary>Where the preview camera is, for camera-facing sprites and ribbons (unit vectors, Home coordinates).</summary>
    public sealed class PreviewCamera
    {
        public double[] Eye = { 0, 0, 5 }, Forward = { 0, 0, -1 }, Right = { 1, 0, 0 }, Up = { 0, 1, 0 };
    }

    /// <summary>
    /// An animation for the Preview window: it calls <see cref="Frame"/> on the UI thread whenever the time or the camera
    /// changes and draws the meshes it returns, in order (so see-through ones can be returned back to front). A mesh's
    /// Colour alpha fades it; meshes with the same ImageBytes / Image share one texture.
    /// </summary>
    public sealed class PreviewAnimation
    {
        public string Name = "Animation";
        /// <summary>Length in seconds (the slider's range).</summary>
        public double Duration = 1;
        public bool Loop = true;
        public bool AutoPlay = true;
        /// <summary>Show the play bar (false for something that only faces the camera, like a skeleton in its bind pose).</summary>
        public bool ShowControls = true;
        public Func<double, PreviewCamera, List<PreviewMesh>> Frame;
        /// <summary>Box the animation stays in (min x, y, z, max x, y, z), for framing the view; null = the layers' box.</summary>
        public double[] Bounds;
        /// <summary>Lines for the status text under the view.</summary>
        public List<string> Info = new List<string>();
    }

    /// <summary>Builds what the Preview window draws: models with their colour textures, and collisions as shapes.</summary>
    public static class ModelPreview
    {
        private const int PreviewTextureSize = 1024;

        /// <summary>A model (MDL, GLB / glTF, FBX, OBJ) with its colour textures.</summary>
        public static PreviewData ForModel(string path)
        {
            var data = new PreviewData { Title = Path.GetFileName(path) };
            data.Layers.Add(ModelLayer(path, data.Info));
            Stats(data);
            return data;
        }

        /// <summary>
        /// A collision HKX as shapes; or, for a model, the model plus the collision the Collision converter would make from
        /// it at this complexity (and the scene's original collision, hidden, when one is found).
        /// </summary>
        public static PreviewData ForCollision(string path, int complexity)
        {
            var data = new PreviewData { Title = Path.GetFileName(path) };
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".hkx")
            {
                data.Layers.Add(HkxLayer("Collision", File.ReadAllBytes(path), 0xFFE08A3C, data.Info));
                Stats(data);
                return data;
            }

            var model = ModelLayer(path, data.Info);
            model.Name = "Model";
            data.Layers.Add(model);

            complexity = Math.Clamp(complexity, 1, 5);
            var notes = new List<string>();
            var meshes = CollisionTools.MeshesFrom(path, out string sceneSearch, notes);
            var s = ConversionSettings.Preset(complexity);
            var scene = CollisionTools.FindScene(sceneSearch);
            if (scene?.Spawn != null)
            {
                s.Spawn = scene.Spawn;
                var sp = scene.Spawn.Value;
                data.Spawn = new[] { sp.X, sp.Y, sp.Z };
            }
            var pts = meshes.SelectMany(m => m.Positions).ToList();
            if (pts.Count > 0)
            {
                double voxels = 1;
                for (int a = 0; a < 3; a++) voxels *= Math.Ceiling((pts.Max(p => p[a]) - pts.Min(p => p[a])) / s.Voxel + 6);
                if (voxels > 450e6)
                    data.Info.Add($"collision not previewed: complexity {complexity} needs about {voxels / 1e6:N0} M voxels - choose a lower complexity");
                else
                {
                    try
                    {
                        var R = Converter.Run(meshes, s);
                        if (R.Boxes.Count + R.EdgeWalls.Count == 0) data.Info.Add("the collision converter made no boxes (nothing reachable from the spawn?)");
                        else
                        {
                            var bytes = Converter.BuildChecked(R);
                            var layer = HkxLayer($"Collision ({CollisionTools.ComplexityNames[complexity - 1]})", bytes, 0x99E08A3C, null);
                            data.Layers.Add(layer);
                            data.Info.Add($"collision preview: {R.Boxes.Count} boxes + {R.EdgeWalls.Count} edge walls at {CollisionTools.ComplexityNames[complexity - 1]}");
                        }
                    }
                    catch (OutOfMemoryException) { data.Info.Add("collision not previewed: out of memory at this complexity"); }
                    catch (Exception ex) { data.Info.Add("collision not previewed: " + ex.Message); }
                }
            }
            if (scene?.CollisionPath != null && File.Exists(scene.CollisionPath))
            {
                try
                {
                    var orig = HkxLayer("Original collision (" + Path.GetFileName(scene.CollisionPath) + ")", File.ReadAllBytes(scene.CollisionPath), 0x993C8AE0, null);
                    orig.Visible = false;
                    data.Layers.Add(orig);
                }
                catch (Exception ex) { data.Info.Add("original collision could not be read: " + ex.Message); }
            }
            data.Info.AddRange(notes.Take(6));
            Stats(data);
            return data;
        }

        /// <summary>A scene built in memory (sky, effect shapes), grouped into layers by mesh name.</summary>
        public static PreviewData FromScene(ExportScene scene, string title)
        {
            var data = new PreviewData { Title = title };
            string LayerOf(string n)
            {
                n ??= "";
                if (n == "SkyDome" || n == "Sun") return "Sky";
                if (n.StartsWith("SpriteCloud", StringComparison.Ordinal)) return "Sprite clouds";
                if (n.StartsWith("ParticleCloud", StringComparison.Ordinal)) return "Particle clouds";
                if (n.EndsWith("_Texture", StringComparison.Ordinal)) return "Group textures";
                if (n.StartsWith("G", StringComparison.Ordinal) && n.Contains("_E")) return "Emitters";
                return "Model";
            }
            var layers = new Dictionary<string, PreviewLayer>();
            foreach (var m in scene.Meshes)
            {
                if (m.P == null || m.Tris == null || m.Tris.Length == 0) continue;
                string ln = LayerOf(m.Name);
                if (!layers.TryGetValue(ln, out var layer)) { layer = new PreviewLayer { Name = ln }; layers[ln] = layer; data.Layers.Add(layer); }
                var pm = new PreviewMesh { Name = m.Name, P = m.P, N = m.N, UV = m.UV, Tris = m.Tris };
                if (m.Material >= 0 && m.Material < scene.Materials.Count)
                {
                    var mt = scene.Materials[m.Material];
                    pm.Emissive = mt.Kind == "emissive";
                    int? ti = mt.Colour ?? mt.Emissive;
                    if (ti.HasValue && ti.Value >= 0 && ti.Value < scene.Textures.Count) pm.ImageBytes = scene.Textures[ti.Value].Bytes;
                    else if (mt.Kind == "default") pm.Colour = 0xFF5AA0E6;
                }
                layer.Meshes.Add(pm);
            }
            Stats(data);
            return data;
        }

        // ------------------------------------------------------------------ layers

        private static PreviewLayer HkxLayer(string name, byte[] bytes, uint tint, List<string> info)
        {
            var bodies = HkxReader.Read(bytes);
            var scene = CollisionTools.SceneFromHkx(name, bodies, out int tris);
            var layer = new PreviewLayer { Name = name, Tint = tint };
            foreach (var m in scene.Meshes)
                layer.Meshes.Add(new PreviewMesh { Name = m.Name, P = m.P, N = m.N, Tris = m.Tris, Colour = tint | 0xFF000000 });
            info?.Add($"{bodies.Count} collision bod(ies), {tris:N0} triangles");
            return layer;
        }

        private static PreviewLayer ModelLayer(string path, List<string> info)
        {
            var layer = new PreviewLayer { Name = "Model" };
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".mdl")
            {
                var notes = new List<string>();
                var scene = ModelConverter.MdlScene(path, false, PreviewTextureSize, out var model, notes);
                foreach (var m in scene.Meshes)
                {
                    var pm = new PreviewMesh { Name = m.Name, P = m.P, N = m.N, UV = m.UV, Tris = m.Tris };
                    if (m.Material >= 0 && m.Material < scene.Materials.Count)
                    {
                        var mt = scene.Materials[m.Material];
                        pm.Emissive = mt.Kind == "emissive";
                        int? ti = mt.Colour ?? mt.Emissive;
                        if (ti.HasValue && ti.Value >= 0 && ti.Value < scene.Textures.Count) pm.ImageBytes = scene.Textures[ti.Value].Bytes;
                    }
                    layer.Meshes.Add(pm);
                }
                if (model.Skeleton != 0) info.Add("skinned model, shown in its bind pose");
                info.AddRange(notes.Where(n => n.StartsWith("texture not found")).Take(5));
                return layer;
            }

            var doc = ModelConverter.LoadSource(path);
            bool fbx = doc.Format != null && doc.Format.StartsWith("FBX", StringComparison.Ordinal);
            foreach (var part in doc.Collect("full"))
            {
                int n = part.P.Count;
                var pm = new PreviewMesh { Name = part.Name, P = new float[n * 3], Tris = new int[part.Tris.Count * 3] };
                for (int i = 0; i < n; i++) { pm.P[3 * i] = (float)part.P[i].X; pm.P[3 * i + 1] = (float)part.P[i].Y; pm.P[3 * i + 2] = (float)part.P[i].Z; }
                int k = 0;
                foreach (var t in part.Tris)
                {
                    bool ok = t.Length >= 3 && t[0] >= 0 && t[1] >= 0 && t[2] >= 0 && t[0] < n && t[1] < n && t[2] < n;
                    for (int c = 0; c < 3; c++) pm.Tris[k++] = ok ? t[c] : 0;
                }
                if (part.N != null && part.N.Count == n)
                {
                    pm.N = new float[n * 3];
                    for (int i = 0; i < n; i++) { pm.N[3 * i] = (float)part.N[i].X; pm.N[3 * i + 1] = (float)part.N[i].Y; pm.N[3 * i + 2] = (float)part.N[i].Z; }
                }
                // the FBX reader already gives Home-data parts top-down V (see ModelConverter.RebuildMdl)
                bool flip = doc.DefaultFlipV && !(fbx && part.HomeJson != null);
                if (part.U.Count == n)
                {
                    pm.UV = new float[n * 2];
                    for (int i = 0; i < n; i++) { pm.UV[2 * i] = (float)part.U[i].U; pm.UV[2 * i + 1] = (float)(flip ? 1 - part.U[i].V : part.U[i].V); }
                }
                if (part.Material >= 0 && part.Material < doc.Materials.Count)
                {
                    var sm = doc.Materials[part.Material];
                    var bc = sm.BaseColor ?? new double[] { 1, 1, 1, 1 };
                    uint B(double x) => (uint)Math.Clamp((int)Math.Round(x * 255), 0, 255);
                    pm.Colour = (B(bc.Length > 3 ? bc[3] : 1) << 24) | (B(bc[0]) << 16) | (B(bc[1]) << 8) | B(bc[2]);
                    if ((pm.Colour & 0x00FFFFFF) == 0) pm.Colour |= 0x00B4B4B4;
                    pm.Emissive = sm.Textures.ContainsKey("emissiveMap") && !sm.Textures.ContainsKey("colourMap");
                    int ti = sm.Textures.TryGetValue("colourMap", out int a) ? a : sm.Textures.TryGetValue("emissiveMap", out int b) ? b : -1;
                    if (ti >= 0 && ti < doc.Images.Count)
                    {
                        var img = doc.Images[ti];
                        string mime = img.Bytes != null ? ImageCodec.WebMime(img.Bytes) : null;
                        if (mime != null) pm.ImageBytes = img.Bytes;
                        else
                        {
                            var rgba = img.Rgba;
                            if (rgba != null && (rgba.Width > PreviewTextureSize || rgba.Height > PreviewTextureSize)) rgba = Dds.Prepare(rgba, false, PreviewTextureSize);
                            pm.Image = rgba;
                        }
                    }
                }
                layer.Meshes.Add(pm);
            }
            info.AddRange(doc.Notes.Take(5));
            return layer;
        }

        private static void Stats(PreviewData data)
        {
            foreach (var l in data.Layers)
            {
                int tris = l.Meshes.Sum(m => m.Tris.Length / 3), verts = l.Meshes.Sum(m => m.P.Length / 3);
                var lo = new[] { float.MaxValue, float.MaxValue, float.MaxValue };
                var hi = new[] { float.MinValue, float.MinValue, float.MinValue };
                foreach (var m in l.Meshes)
                    for (int i = 0; i + 2 < m.P.Length; i += 3)
                        for (int c = 0; c < 3; c++) { lo[c] = Math.Min(lo[c], m.P[i + c]); hi[c] = Math.Max(hi[c], m.P[i + c]); }
                if (verts == 0) { data.Info.Insert(0, $"{l.Name}: empty"); continue; }
                float sx = hi[0] - lo[0], sy = hi[1] - lo[1], sz = hi[2] - lo[2];
                data.Info.Insert(0, $"{l.Name}: {l.Meshes.Count} mesh(es), {verts:N0} vertices, {tris:N0} triangles, size {sx:0.###} x {sy:0.###} x {sz:0.###} m");
            }
        }
    }
}

#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Mdl2Hkx;
using Nautilus2027.Formats.Home;

namespace Nautilus2027.Formats
{
    /// <summary>
    /// Collision (.HKX) for the MDL / HKX tab, built on the mdl2hkx port (BackendProcess/Mdl2Hkx):
    /// box collision from a model with complexity presets, HKX read-back and walk tests, HKX to OBJ / GLB for viewing.
    /// </summary>
    public static class CollisionTools
    {
        /// <summary>Complexity presets, index 0 = complexity 1. Preset 2 is the one verified in Home.</summary>
        public static readonly string[] ComplexityNames =
        {
            "1 - Lowest", "2 - Simple (default)", "3 - Balanced", "4 - Detailed", "5 - Exact (slow)",
        };

        public sealed class SceneInfo
        {
            public string ScenePath;
            public Vec3? Spawn;
            /// <summary>The scene's collision HKX as found on disk, or null.</summary>
            public string CollisionPath;
            /// <summary>True when the scene's environment model is the file being converted.</summary>
            public bool MatchesModel;
        }

        // ------------------------------------------------------------------ .SCENE

        /// <summary>
        /// Look for a .SCENE beside the model (or up to three folders above) and read its spawn point and collision asset.
        /// A scene whose environment model has the same file name as the model wins.
        /// </summary>
        public static SceneInfo FindScene(string modelPath)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(modelPath));
            string modelName = Path.GetFileName(modelPath);
            SceneInfo fallback = null;
            for (int level = 0; level < 4 && !string.IsNullOrEmpty(dir); level++, dir = Path.GetDirectoryName(dir))
            {
                IEnumerable<string> scenes;
                try { scenes = Directory.EnumerateFiles(dir, "*.scene", SearchOption.TopDirectoryOnly).ToList(); }
                catch (Exception) { continue; }
                foreach (var sp in scenes)
                {
                    var info = ReadScene(sp, modelName);
                    if (info == null) continue;
                    if (info.MatchesModel) return info;
                    if (fallback == null && info.Spawn.HasValue) fallback = info;
                }
            }
            return fallback;
        }

        private static SceneInfo ReadScene(string scenePath, string modelName)
        {
            XDocument doc;
            try { doc = XDocument.Load(scenePath); }
            catch (Exception) { return null; }
            XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
            var info = new SceneInfo { ScenePath = scenePath };
            foreach (var go in doc.Descendants().Where(e => e.Name.LocalName == "gameObject"))
            {
                if ((string)go.Attribute(xsi + "type") != "spawnPointType") continue;
                var tr = go.Elements().FirstOrDefault(e => e.Name.LocalName == "translate");
                var p = tr?.Value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (p != null && p.Length >= 3
                    && double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                    && double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
                    && double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double z))
                {
                    info.Spawn = new Vec3(x, y, z);
                    break;
                }
            }
            foreach (var asset in doc.Descendants().Where(e => e.Name.LocalName == "asset"))
            {
                string name = (string)asset.Attribute("name") ?? "", src = (string)asset.Attribute("source") ?? "";
                string rel = AssetRelativePath(src);
                if (rel == null) continue;
                if (string.Equals(Path.GetFileName(rel), modelName, StringComparison.OrdinalIgnoreCase)) info.MatchesModel = true;
                if (name.Equals("collision", StringComparison.OrdinalIgnoreCase) && info.CollisionPath == null)
                    info.CollisionPath = ModelConverter.FindHomeTexture(scenePath, rel);
            }
            return info;
        }

        /// <summary>"file:///resource_root/build/Environments/x/y.mdl" -> "Environments\x\y.mdl".</summary>
        private static string AssetRelativePath(string src)
        {
            if (string.IsNullOrWhiteSpace(src)) return null;
            string s = src.Replace('/', '\\');
            int i = s.IndexOf("resource_root\\", StringComparison.OrdinalIgnoreCase);
            if (i >= 0) s = s.Substring(i + "resource_root\\".Length);
            if (s.StartsWith("build\\", StringComparison.OrdinalIgnoreCase)) s = s.Substring("build\\".Length);
            return s.TrimStart('\\');
        }

        // ------------------------------------------------------------------ input meshes

        /// <summary>Meshes for the converter from an MDL, or from a GLB / FBX / OBJ through the model importers.</summary>
        public static List<CollisionMesh> MeshesFrom(string input, out string sceneSearchPath, List<string> notes)
        {
            sceneSearchPath = input;
            string ext = Path.GetExtension(input).ToLowerInvariant();
            if (ext == ".mdl")
                return CollisionMesh.FromMdl(MdlModel.Load(input));

            var doc = ModelConverter.LoadSource(input);
            notes.AddRange(doc.Notes.Where(n => !n.StartsWith("texture", StringComparison.OrdinalIgnoreCase)));
            // a GLB exported from an MDL remembers where that MDL was: look for its scene there
            string sidecar = ModelConverter.SidecarPath(input);
            if (File.Exists(sidecar))
            {
                var meta = HmRoundTrip.Parse<HomeMetaModel>(File.ReadAllText(sidecar));
                if (meta?.SourceMdl != null && File.Exists(meta.SourceMdl)) sceneSearchPath = meta.SourceMdl;
            }
            uint hDefault = HmCore.HashName("default"), hEmissive = HmCore.HashName("emissive");
            var outp = new List<CollisionMesh>();
            int index = 0;
            foreach (var part in doc.Collect("full"))
            {
                var sm = part.Material >= 0 && part.Material < doc.Materials.Count ? doc.Materials[part.Material] : null;
                var cm = new CollisionMesh
                {
                    Index = index++,
                    Positions = part.P.Select(v => new Vec3(v.X, v.Y, v.Z)).ToArray(),
                    Triangles = part.Tris.Select(t => (t[0], t[1], t[2])).ToList(),
                    MaterialType = hDefault,
                };
                if (sm != null)
                {
                    var home = HmRoundTrip.Parse<HomeMetaMaterial>(sm.HomeJson);
                    if (home != null) cm.MaterialType = home.Type;
                    else if (sm.Textures.ContainsKey("emissiveMap") && !sm.Textures.ContainsKey("colourMap")) cm.MaterialType = hEmissive;
                    cm.Names.Add(sm.Name);
                    foreach (var kv in sm.Textures)
                        if (kv.Value >= 0 && kv.Value < doc.Images.Count)
                            cm.Names.Add(doc.Images[kv.Value].FileName ?? doc.Images[kv.Value].Name);
                    if (sm.Textures.TryGetValue("colourMap", out int ci) && ci < doc.Images.Count)
                        cm.ColourTexture = doc.Images[ci].FileName ?? doc.Images[ci].Name;
                }
                cm.Names.Add(part.Name);
                outp.Add(cm);
            }
            return outp;
        }

        // ------------------------------------------------------------------ model -> HKX

        /// <summary>Box collision for a model. Writes OUTPUT\NAME.HKX and NAME_report.txt. Null = skipped (exists).</summary>
        public static ModelConverter.Result ToHkx(string input, string outputDir, OverwriteBehavior overwrite, int complexity)
        {
            complexity = Math.Clamp(complexity, 1, 5);
            var notes = new List<string>();
            var meshes = MeshesFrom(input, out string sceneSearch, notes);
            var s = ConversionSettings.Preset(complexity);
            var scene = FindScene(sceneSearch);
            if (scene?.Spawn != null) s.Spawn = scene.Spawn;

            // memory / time guard (docs/04 section 3)
            var pts = meshes.SelectMany(m => m.Positions).ToList();
            if (pts.Count == 0) throw new InvalidDataException("The model has no geometry.");
            double voxels = 1;
            for (int a = 0; a < 3; a++) voxels *= Math.Ceiling((pts.Max(p => p[a]) - pts.Min(p => p[a])) / s.Voxel + 6);
            if (voxels > 450e6)
                throw new NotSupportedException($"at complexity {complexity} this model needs about {voxels / 1e6:N0} M voxels, which is too much memory - choose a lower complexity");
            if (voxels > 150e6) notes.Add($"large job: about {voxels / 1e6:N0} M voxels at this complexity (needs several GB of memory)");

            string name = Path.GetFileNameWithoutExtension(input).ToUpperInvariant();
            Directory.CreateDirectory(outputDir);
            string outName = ModelConverter.ReserveName(name, n => Path.Combine(outputDir, n + ".HKX"), overwrite, out bool renamed);
            if (outName == null)
                return null;
            string outPath = Path.Combine(outputDir, outName + ".HKX");

            ConversionResult R;
            try { R = Converter.Run(meshes, s); }
            catch (OutOfMemoryException) { throw new InvalidOperationException($"ran out of memory at complexity {complexity}; choose a lower complexity"); }
            if (R.Boxes.Count + R.EdgeWalls.Count == 0)
                throw new InvalidDataException("no collision boxes were produced (nothing reachable from the spawn?)");
            var bytes = Converter.BuildChecked(R);
            File.WriteAllBytes(outPath, bytes);
            File.WriteAllText(Path.Combine(outputDir, outName + "_report.txt"), R.ReportText(), new UTF8Encoding(false));

            var r = new ModelConverter.Result
            {
                OutputPath = outPath,
                Renamed = renamed,
                Summary = $"{R.Boxes.Count} boxes + {R.EdgeWalls.Count} edge walls, {bytes.Length:N0} bytes, {ComplexityNames[complexity - 1]}, read-back OK",
            };
            r.Details.Add(scene?.Spawn != null
                ? $"spawn {scene.Spawn.Value} from {Path.GetFileName(scene.ScenePath)}"
                : "no .SCENE with a spawn point found near the model: started from the largest floor (put the MDL in its extracted scene folder to use the real spawn)");
            if (!double.IsNaN(R.MainFloor)) r.Details.Add($"main floor level {R.MainFloor:0.000} m");
            var skipped = R.Meshes.Where(m => m.Decision != "kept").ToList();
            if (skipped.Count > 0)
                r.Details.Add($"left out {skipped.Count} mesh(es): " + string.Join(", ", skipped.GroupBy(m => m.Decision).Select(g => $"{g.Count()} {g.Key}")));
            r.Details.AddRange(notes);
            r.Details.AddRange(R.Log.Select(l => "step: " + l));

            if (s.Spawn.HasValue)
            {
                try
                {
                    var wr = WalkTest.Run(HkxReader.Read(bytes), s.Spawn.Value);
                    r.Details.Add("walk test (new): " + wr + (wr.FallOffPoints > 0 ? "  <- there are places to walk off into a hole" : ""));
                    if (scene?.CollisionPath != null && File.Exists(scene.CollisionPath)
                        && !string.Equals(Path.GetFullPath(scene.CollisionPath), Path.GetFullPath(outPath), StringComparison.OrdinalIgnoreCase))
                    {
                        var wo = WalkTest.Run(HkxReader.Read(File.ReadAllBytes(scene.CollisionPath)), s.Spawn.Value);
                        r.Details.Add($"walk test (original {Path.GetFileName(scene.CollisionPath)}): {wo}");
                    }
                }
                catch (Exception ex)
                {
                    r.Details.Add("walk test could not run: " + ex.Message);
                }
            }
            r.Details.Add($"full report: {Path.Combine(outputDir, outName + "_report.txt")}");
            return r;
        }

        // ------------------------------------------------------------------ HKX -> viewable mesh

        /// <summary>One flat-shaded mesh of every body in a collision HKX (boxes, Sony's hulls and meshes).</summary>
        public static ExportScene SceneFromHkx(string name, List<HkxBody> bodies, out int triangles)
        {
            var P = new List<float>();
            var N = new List<float>();
            var T = new List<int>();
            foreach (var b in bodies)
                foreach (var (i0, i1, i2) in b.SurfaceTriangles())
                {
                    if (i0 < 0 || i1 < 0 || i2 < 0 || i0 >= b.Verts.Length || i1 >= b.Verts.Length || i2 >= b.Verts.Length) continue;
                    Vec3 a = b.Verts[i0], c = b.Verts[i1], d = b.Verts[i2];
                    var n = Vec3.Cross(c - a, d - a);
                    double l = n.Length;
                    if (l < 1e-12) continue;
                    n = n * (1 / l);
                    foreach (var v in new[] { a, c, d })
                    {
                        T.Add(P.Count / 3);
                        P.Add((float)v.X); P.Add((float)v.Y); P.Add((float)v.Z);
                        N.Add((float)n.X); N.Add((float)n.Y); N.Add((float)n.Z);
                    }
                }
            triangles = T.Count / 3;
            var mesh = new ExportMesh { Name = name, Material = -1, P = P.ToArray(), N = N.ToArray(), UV = new float[P.Count / 3 * 2], Tris = T.ToArray() };
            return new ExportScene { Name = name, Meshes = { mesh } };
        }

        // ------------------------------------------------------------------ inspect

        public static void DescribeHkx(StringBuilder sb, string path, byte[] bytes)
        {
            List<HkxBody> bodies;
            try { bodies = HkxReader.Read(bytes); }
            catch (Exception ex) { sb.AppendLine($"     Bodies: could not read ({ex.Message})"); return; }
            int convex = bodies.Count(b => b.Kind == "convex"), mesh = bodies.Count(b => b.Kind == "mesh");
            int boxes = bodies.Count(b => b.Kind == "convex" && b.Verts.Length == 8 && Enumerable.Range(0, 3).All(a => b.Verts.Select(v => Math.Round(v[a], 4)).Distinct().Count() == 2));
            sb.AppendLine($"     Bodies: {bodies.Count} ({convex} convex, of which {boxes} axis-aligned boxes; {mesh} triangle mesh{(mesh == 1 ? "" : "es")} with {bodies.Where(b => b.Kind == "mesh").Sum(b => b.Tris.Length):N0} triangles)");
            var all = bodies.SelectMany(b => b.Verts).ToList();
            if (all.Count > 0)
                sb.AppendLine($"     Bounds: x {all.Min(v => v.X):0.00} .. {all.Max(v => v.X):0.00}, y {all.Min(v => v.Y):0.00} .. {all.Max(v => v.Y):0.00}, z {all.Min(v => v.Z):0.00} .. {all.Max(v => v.Z):0.00}");
            var scene = FindScene(path);
            if (scene?.Spawn != null && bodies.Count > 0)
            {
                try { sb.AppendLine($"     Walk test from the {Path.GetFileName(scene.ScenePath)} spawn: {WalkTest.Run(bodies, scene.Spawn.Value)}"); }
                catch (Exception ex) { sb.AppendLine($"     Walk test could not run: {ex.Message}"); }
            }
        }
    }
}

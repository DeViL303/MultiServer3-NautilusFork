#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Nautilus2027.Formats.Home;

namespace Nautilus2027.Formats
{
    /// <summary>
    /// Model and collision conversion for the MDL / HKX tab, built on the C# port of the HOME_DEV_KIT
    /// model tools (Formats/Home): Home MDL read/write, GLB / FBX / OBJ import and export, DDS textures.
    /// </summary>
    public static class ModelConverter
    {
        /// <summary>Files accepted on the model side.</summary>
        public static readonly string[] ModelExtensions = { ".mdl", ".glb", ".fbx", ".obj" };

        /// <summary>Files accepted on the collision side: collision formats plus every model format.</summary>
        public static readonly string[] CollisionExtensions = { ".hkx", ".mdl", ".glb", ".fbx", ".obj" };

        /// <summary>Formats a model can be converted to.</summary>
        public static readonly string[] ModelTargets = { "MDL", "GLB", "FBX", "OBJ" };

        /// <summary>Formats a collision can be converted to.</summary>
        public static readonly string[] CollisionTargets = { "HKX", "OBJ", "GLB" };


        public sealed class Result
        {
            public string OutputPath;
            public string Summary;
            public bool Renamed;
            /// <summary>Extra report lines (notes, warnings, what was written).</summary>
            public List<string> Details = new List<string>();
        }

        private static string Ext(string path) => Path.GetExtension(path).ToLowerInvariant();

        // ------------------------------------------------------------------ loading

        /// <summary>Import a GLB / glTF / FBX / OBJ file. Textures and .mtl / .bin files are looked up next to it.</summary>
        public static SourceDoc LoadSource(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var files = new FileResolver(path);
            switch (Ext(path))
            {
                case ".glb":
                case ".gltf":
                    return GltfImport.Parse(bytes, files);
                case ".fbx":
                    return FbxImport.Parse(bytes, files);
                case ".obj":
                    return ObjImport.Parse(bytes, files);
                default:
                    throw new NotSupportedException($"{Ext(path)} files can't be imported.");
            }
        }

        // ------------------------------------------------------------------ model -> model

        /// <summary>
        /// Convert a model. <paramref name="maxTextureSize"/> limits the longest texture side (0 = keep the source size).
        /// Returns null when the output exists and the overwrite setting says skip.
        /// </summary>
        public static Result ConvertModel(string input, string target, string outputDir, OverwriteBehavior overwrite, int maxTextureSize = 0)
        {
            string ext = Ext(input);
            target = target.ToUpperInvariant();
            if (ext == "." + target.ToLowerInvariant())
                throw new NotSupportedException($"The file is already {target}.");
            if (!ModelExtensions.Contains(ext))
                throw new NotSupportedException($"{ext} files can't be converted.");

            if (ext == ".mdl")
                return MdlToModel(input, target, outputDir, overwrite, maxTextureSize);

            var doc = LoadSource(input);
            return target == "MDL"
                ? SourceToMdl(input, doc, outputDir, overwrite, maxTextureSize)
                : SourceToModel(input, doc, target, outputDir, overwrite, maxTextureSize);
        }

        private static string HomeModelName(string input)
        {
            string n = Regex.Replace(Path.GetFileNameWithoutExtension(input).Trim(), "[^A-Za-z0-9_\\-]+", "_").Trim('_');
            return (n.Length > 0 ? n : "MODEL").ToUpperInvariant();
        }

        private static HmConvertSettings SettingsFor(SourceDoc doc, string modelName, int maxTextureSize) => new HmConvertSettings
        {
            ModelName = modelName,
            FlipV = doc.DefaultFlipV,
            MaxSize = Math.Max(0, maxTextureSize),
        };

        private static ConversionPlan PlanFor(SourceDoc doc, HmConvertSettings settings)
        {
            var plan = HmConvert.Plan(doc, settings, i =>
            {
                var img = i >= 0 && i < doc.Images.Count ? doc.Images[i].Rgba : null;
                return img != null ? HmConvert.AnalyzeImage(img) : null;
            });
            if (plan.Parts.Count == 0)
                throw new InvalidDataException("The model has no triangles to convert.");
            return plan;
        }

        private static RgbaImage ImageAt(SourceDoc doc, int i) => i >= 0 && i < doc.Images.Count ? doc.Images[i].Rgba : null;

        private static void AddSourceNotes(Result r, SourceDoc doc, ConversionPlan plan)
        {
            if (!string.IsNullOrEmpty(plan.Note)) r.Details.Add(plan.Note);
            foreach (var n in doc.Notes) r.Details.Add(n);
            foreach (var img in doc.Images)
                if (img.Rgba == null && img.Bytes != null)
                    r.Details.Add($"image {img.Name} could not be read: {img.Error}");
            foreach (var m in plan.Materials)
                r.Details.Add($"material {m.Name}: {HmCore.Presets[m.Preset].Label} ({m.Reason})");
        }

        /// <summary>The .home.json written next to an export from an MDL.</summary>
        public static string SidecarPath(string exportPath) => Path.Combine(Path.GetDirectoryName(exportPath) ?? "", Path.GetFileNameWithoutExtension(exportPath) + ".home.json");

        private static HomeMetaModel LoadSidecar(string input)
        {
            string p = SidecarPath(input);
            if (!File.Exists(p)) return null;
            try { return HmRoundTrip.Parse<HomeMetaModel>(File.ReadAllText(p)); }
            catch (Exception) { return null; }
        }

        private sealed class ExactPart
        {
            public SourcePart Part;
            public HomeMetaMesh Mesh;
            public HomeMetaMaterial Material;
        }

        /// <summary>Parts of a GLB exported from an MDL that carry Home data (glTF extras, or the .home.json beside it).</summary>
        private static List<ExactPart> MatchHomeData(string input, SourceDoc doc, List<SourcePart> parts, out HomeMetaModel modelMeta, out HomeMetaModel sidecar)
        {
            sidecar = LoadSidecar(input);
            modelMeta = HmRoundTrip.Parse<HomeMetaModel>(doc.HomeJson) ?? sidecar;
            var sc = sidecar;
            var outL = new List<ExactPart>();
            foreach (var part in parts)
            {
                var mesh = HmRoundTrip.Parse<HomeMetaMesh>(part.HomeJson)
                           ?? HmRoundTrip.FindByName(sc?.Meshes, part.Name, x => x.Name, x => x.Index);
                HomeMetaMaterial mat = null;
                if (part.Material >= 0 && part.Material < doc.Materials.Count)
                {
                    var sm = doc.Materials[part.Material];
                    mat = HmRoundTrip.Parse<HomeMetaMaterial>(sm.HomeJson) ?? HmRoundTrip.FindByName(sc?.Materials, sm.Name, x => x.Name, x => x.Index);
                }
                if (mat == null && mesh != null && sc?.Materials != null)
                    mat = sc.Materials.FirstOrDefault(x => x.Index == mesh.Material);
                if (mesh == null || mat == null)
                {
                    outL.Add(new ExactPart { Part = part });
                    continue;
                }
                // the sidecar knows where the original DDS files are
                var scMat = sc?.Materials?.FirstOrDefault(x => x.Index == mat.Index);
                if (scMat != null)
                    foreach (var t in mat.Textures)
                        if (string.IsNullOrEmpty(t.SourceDds))
                            t.SourceDds = scMat.Textures.FirstOrDefault(x => string.Equals(x.Path, t.Path, StringComparison.OrdinalIgnoreCase))?.SourceDds;
                outL.Add(new ExactPart { Part = part, Mesh = mesh, Material = mat });
            }
            return outL;
        }

        /// <summary>GLB / FBX / OBJ -> Home MDL + DDS textures in OUTPUT\NAME\NAME.MDL and OUTPUT\NAME\TEXTURES\.</summary>
        private static Result SourceToMdl(string input, SourceDoc doc, string outputDir, OverwriteBehavior overwrite, int maxTextureSize)
        {
            string name = ReserveName(HomeModelName(input), n => Path.Combine(outputDir, n, n + ".MDL"), overwrite, out bool renamed);
            if (name == null)
                return null;

            if (doc.Format == "glTF" || doc.Format.StartsWith("FBX", StringComparison.Ordinal))
            {
                var allParts = doc.Collect("full").Where(p => p.Tris.Count > 0).ToList();
                var matched = MatchHomeData(input, doc, allParts, out var modelMeta, out var sidecar);
                if (matched.Any(m => m.Mesh != null))
                    return RebuildMdl(input, doc, matched, modelMeta, sidecar, name, renamed, outputDir, maxTextureSize);
            }

            var settings = SettingsFor(doc, name, maxTextureSize);
            var plan = PlanFor(doc, settings);
            var built = HmConvert.Build(plan, i => ImageAt(doc, i));
            if (built.Errors.Count > 0)
                throw new InvalidDataException("the built MDL failed its checks: " + string.Join("; ", built.Errors));

            string dir = Path.Combine(outputDir, name);
            string texDir = Path.Combine(dir, "TEXTURES");
            Directory.CreateDirectory(texDir);
            string mdlPath = Path.Combine(dir, name + ".MDL");
            File.WriteAllBytes(mdlPath, built.Mdl);
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in built.Textures)
            {
                string f = t.FileName.ToUpperInvariant();
                if (!written.Add(f)) continue;
                File.WriteAllBytes(Path.Combine(texDir, f), t.Bytes);
            }

            var r = new Result
            {
                OutputPath = mdlPath,
                Renamed = renamed,
                Summary = $"{built.Model.Meshes.Count} element(s), {built.Model.Materials.Count} material(s), {written.Count} texture(s)",
            };
            AddSourceNotes(r, doc, plan);
            r.Details.AddRange(built.Log);
            r.Details.Add($"textures are referenced as {HmConvert.ResolvePrefix(settings).ToUpperInvariant()}<NAME>.DDS - keep that folder layout when adding the model to a scene or object");
            return r;
        }

        /// <summary>
        /// MDL rebuilt from a GLB that was exported from an MDL: materials, texture slots and paths, element names, flags
        /// and vertex layouts come from the Home data; geometry, UVs, vertex colours and tangents from the GLB. Textures
        /// keep their original Home paths and are written under OUTPUT\NAME\ in the same folder layout as the game's.
        /// Parts without Home data (added in an editor) go through the normal conversion and are appended.
        /// </summary>
        private static Result RebuildMdl(string input, SourceDoc doc, List<ExactPart> parts, HomeMetaModel modelMeta, HomeMetaModel sidecar,
            string name, bool renamed, string outputDir, int maxTextureSize)
        {
            var log = new List<string>();
            var model = new HmModel
            {
                Version = (ushort)(modelMeta != null && modelMeta.Version >= 0x0101 && modelMeta.Version <= 0x0104 ? modelMeta.Version : 0x0103),
                Skeleton = modelMeta?.Skeleton ?? 0,
                Word8 = modelMeta?.Word8 ?? 0,
            };

            // material table: the original's (from the sidecar) plus any described only in the GLB, in original order
            var metas = new SortedDictionary<int, HomeMetaMaterial>();
            if (sidecar?.Materials != null)
                foreach (var m in sidecar.Materials) metas[m.Index] = m;
            foreach (var p in parts.Where(p => p.Material != null)) metas[p.Material.Index] = p.Material;
            var matPos = new Dictionary<int, int>();
            var glbMaterialFor = new Dictionary<int, SourceMaterial>();
            foreach (var p in parts.Where(p => p.Material != null))
                if (!glbMaterialFor.ContainsKey(p.Material.Index) && p.Part.Material >= 0 && p.Part.Material < doc.Materials.Count)
                    glbMaterialFor[p.Material.Index] = doc.Materials[p.Part.Material];
            foreach (var kv in metas)
            {
                matPos[kv.Key] = model.Materials.Count;
                model.Materials.Add(HmRoundTrip.ToMaterial(kv.Value));
            }

            // geometry
            var generic = new List<SourcePart>();
            int exact = 0;
            foreach (var p in parts)
            {
                HmMesh me = null;
                if (p.Mesh != null) me = HmRoundTrip.BuildExact(p.Part, p.Mesh, matPos[p.Material.Index], log);
                if (me != null) { model.Meshes.Add(me); exact++; }
                else
                {
                    // the FBX reader gives Home-data parts top-down V already; the generic path would flip them again
                    if (p.Mesh != null && doc.DefaultFlipV) p.Part.U = p.Part.U.Select(u => new V2(u.U, 1 - u.V)).ToList();
                    generic.Add(p.Part);
                }
            }
            if (model.Skeleton == 0 && doc.SkeletonKey.HasValue && model.Meshes.Any(HmSkinning.IsSkinned))
            {
                model.Skeleton = doc.SkeletonKey.Value;
                model.Word8 = (uint)doc.SkeletonJoints;
            }
            if (parts.Any(p => p.Mesh != null && p.Mesh.HasDeformers))
            {
                model.Skeleton = 0;
                model.Word8 = 0;
            }

            // parts added in an editor: the normal conversion, appended
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string dir = Path.Combine(outputDir, name);
            Directory.CreateDirectory(dir);
            if (generic.Count > 0)
            {
                var sub = new SourceDoc { Format = doc.Format, Images = doc.Images, Materials = doc.Materials, DefaultFlipV = doc.DefaultFlipV, Collect = _ => generic, SkeletonKey = doc.SkeletonKey, SkeletonJoints = doc.SkeletonJoints };
                var settings = SettingsFor(sub, name, maxTextureSize);
                settings.Version = model.Version;
                var plan = HmConvert.Plan(sub, settings, i =>
                {
                    var img = ImageAt(doc, i);
                    return img != null ? HmConvert.AnalyzeImage(img) : null;
                });
                if (plan.Parts.Count > 0)
                {
                    var built = HmConvert.Build(plan, i => ImageAt(doc, i));
                    int off = model.Materials.Count;
                    model.Materials.AddRange(built.Model.Materials);
                    foreach (var me in built.Model.Meshes) { me.Material += off; model.Meshes.Add(me); }
                    string texDir = Path.Combine(dir, "TEXTURES");
                    Directory.CreateDirectory(texDir);
                    foreach (var t in built.Textures)
                        if (written.Add(Path.Combine(texDir, t.FileName.ToUpperInvariant())))
                            File.WriteAllBytes(Path.Combine(texDir, t.FileName.ToUpperInvariant()), t.Bytes);
                    log.Add($"{generic.Count} part(s) without Home data were converted the normal way: " + string.Join(", ", generic.Select(g => g.Name).Distinct().Take(10)));
                    log.AddRange(built.Log.Where(l => !l.StartsWith("MDL verified")));
                }
            }
            if (model.Meshes.Count == 0)
                throw new InvalidDataException("The model has no triangles to convert.");

            if (modelMeta == null ? model.Version < 0x0104 : modelMeta.ModelBound != null)
            {
                var pts = new List<V3>();
                foreach (var me in model.Meshes)
                {
                    var pm = HmCore.MeshAttribute(me, HmCore.HPosition);
                    foreach (int k in new HashSet<int>(me.Indices.Select(x => (int)x))) pts.Add(new V3(pm[k][0], pm[k][1], pm[k][2]));
                }
                model.ModelBound = HmRoundTrip.KeepOrCompute(modelMeta?.ModelBound, pts);
            }

            string mdlPath = Path.Combine(dir, name + ".MDL");
            var mdl = HmCore.WriteMdl(model);
            File.WriteAllBytes(mdlPath, mdl);
            var errs = HmCore.Verify(mdl);
            foreach (var e in errs) log.Add("check: " + e + " (the original may do the same)");

            // textures: original paths, original DDS where unchanged
            int copied = 0, encoded = 0, notWritten = 0;
            var textureNotes = new List<string>();
            foreach (var kv in metas)
            {
                glbMaterialFor.TryGetValue(kv.Key, out var gm);
                foreach (var t in kv.Value.Textures)
                {
                    if (string.IsNullOrEmpty(t.Path)) continue;
                    string rel = t.Path.Replace('/', '\\').TrimStart('\\').Replace('\\', Path.DirectorySeparatorChar).ToUpperInvariant();
                    string dest = Path.Combine(dir, rel);
                    if (written.Contains(dest)) continue;
                    RgbaImage img = null;
                    var si = !string.IsNullOrEmpty(t.Image) ? doc.Images.FirstOrDefault(x => string.Equals(x.Name, t.Image, StringComparison.OrdinalIgnoreCase))
                                                              ?? doc.Images.FirstOrDefault(x => string.Equals(Path.GetFileNameWithoutExtension(x.Name ?? ""), t.Image, StringComparison.OrdinalIgnoreCase)) : null;
                    if (si == null && gm != null && t.SlotName != null && gm.Textures.TryGetValue(t.SlotName, out int ii) && ii < doc.Images.Count) si = doc.Images[ii];
                    if (si != null) img = si.Rgba;
                    byte[] bytes;
                    string note;
                    try { bytes = HmRoundTrip.TextureBytes(t, img, maxTextureSize, out note); }
                    catch (Exception ex) { bytes = null; note = ex.Message; }
                    if (bytes == null)
                    {
                        notWritten++;
                        textureNotes.Add($"{t.Path}: {note}");
                        written.Add(dest);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    File.WriteAllBytes(dest, bytes);
                    written.Add(dest);
                    if (note == null) copied++;
                    else
                    {
                        encoded++;
                        textureNotes.Add($"{t.Path}: {note}");
                    }
                }
            }

            var r = new Result
            {
                OutputPath = mdlPath,
                Renamed = renamed,
                Summary = $"rebuilt from Home data: {exact} of {model.Meshes.Count} element(s) exactly, {model.Materials.Count} material(s); textures: {copied} original, {encoded} re-encoded, {notWritten} left to the game's copy",
            };
            r.Details.Add("textures keep their original Home paths and are written under " + dir + " in the game's folder layout");
            if (sidecar == null)
                r.Details.Add("no .home.json next to the GLB, so unchanged textures were re-encoded rather than copied from the originals");
            r.Details.AddRange(log.Distinct());
            r.Details.AddRange(textureNotes.Take(40));
            if (textureNotes.Count > 40) r.Details.Add($"... and {textureNotes.Count - 40} more texture notes");
            return r;
        }

        /// <summary>GLB / FBX / OBJ -> GLB / FBX / OBJ, through the same Home conversion (what you see is what the MDL would be).</summary>
        private static Result SourceToModel(string input, SourceDoc doc, string target, string outputDir, OverwriteBehavior overwrite, int maxTextureSize)
        {
            string baseName = Path.GetFileNameWithoutExtension(input);
            var settings = SettingsFor(doc, HomeModelName(input), maxTextureSize);
            settings.Pow2 = false;   // exported textures keep their proportions
            var plan = PlanFor(doc, settings);
            var built = HmConvert.Build(plan, i => ImageAt(doc, i), encode: false);
            var byPath = new Dictionary<string, BuiltTexture>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in built.Textures) byPath[t.Path] = t;

            ExportTextureSource TexFor(string homePath)
            {
                if (!byPath.TryGetValue(homePath, out var bt)) return null;
                string nm = Regex.Replace(bt.FileName, "\\.dds$", "", RegexOptions.IgnoreCase);
                var src = bt.Source;
                if (src.Kind == "image" && src.Tint == null && !(bt.Role == "normalMap" && settings.FlipNormalGreen) && src.Image < doc.Images.Count)
                {
                    var si = doc.Images[src.Image];
                    string mime = ImageCodec.WebMime(si.Bytes);
                    var rgba = si.Rgba;
                    bool fits = maxTextureSize <= 0 || (rgba != null && rgba.Width <= maxTextureSize && rgba.Height <= maxTextureSize);
                    if (mime != null && fits)
                        return new ExportTextureSource { Name = nm, Bytes = si.Bytes, Mime = mime, Ext = mime == "image/jpeg" ? "jpg" : "png" };
                }
                return new ExportTextureSource { Name = nm, Rgba = bt.Preview };
            }

            var scene = HmExport.SceneFromModel(baseName, built.Model, TexFor, built.MeshNames, plan.Materials.Select(m => m.Name).ToList());
            var r = WriteScene(scene, target, outputDir, baseName, overwrite);
            if (r == null)
                return null;
            AddSourceNotes(r, doc, plan);
            r.Details.AddRange(built.Log.Where(l => !l.StartsWith("MDL verified")));
            return r;
        }

        /// <summary>Home MDL -> GLB / FBX / OBJ. Textures are found from the MDL's own paths (see FindHomeTexture).</summary>
        /// <summary>
        /// Export scene of an MDL with its textures (found from the MDL's own paths). With <paramref name="homeData"/> it carries
        /// everything needed to rebuild the MDL. Missing textures are listed in <paramref name="notes"/>.
        /// </summary>
        internal static ExportScene MdlScene(string input, bool homeData, int maxTextureSize, out HmModel model, List<string> notes)
        {
            model = HmCore.ReadMdl(File.ReadAllBytes(input), false);
            var missing = new List<string>();
            var resolver = new FileResolver(input);
            // inside a Home object, clothing materials take their textures from the object's bindings
            var overrides = HomeObjects.TextureOverrides(input, model, notes);
            ExportTextureSource TexFor(string homePath)
            {
                string found = homePath != null && overrides.TryGetValue(homePath, out var bound) && File.Exists(bound) ? bound : FindHomeTexture(input, homePath, resolver);
                if (found == null) { missing.Add(homePath); return null; }
                var bytes = File.ReadAllBytes(found);
                var info = Dds.ReadInfo(bytes);
                var src = new ExportTextureSource { Name = Path.GetFileNameWithoutExtension(found), SourcePath = found, Info = info };
                if (info != null && info.Cube)
                    return src;   // cube maps (reflections) have no glTF form; the MDL keeps pointing at the original
                var img = ImageCodec.Decode(bytes, found);
                if (maxTextureSize > 0) img = Dds.Prepare(img, false, maxTextureSize);
                src.Rgba = img;
                return src;
            }
            var scene = HmExport.SceneFromModel(Path.GetFileNameWithoutExtension(input), model, TexFor, homeData: homeData);
            if (scene.Meshes.Count == 0)
                throw new InvalidDataException("The MDL has no meshes that can be exported.");
            foreach (var p in missing.Distinct(StringComparer.OrdinalIgnoreCase))
                notes.Add("texture not found: " + p);
            if (missing.Count > 0)
                notes.Add("put the MDL inside its extracted scene/object folder (or its DDS files next to it or in a TEXTURES folder) so its textures can be found");
            return scene;
        }

        /// <summary>Home MDL -> GLB / FBX / OBJ. Skinned models get their skeleton (and a clip of the same name) when found beside them.</summary>
        private static Result MdlToModel(string input, string target, string outputDir, OverwriteBehavior overwrite, int maxTextureSize)
        {
            if (target == "MDL")
                throw new NotSupportedException("The file is already MDL.");
            string baseName = Path.GetFileNameWithoutExtension(input);
            var notes = new List<string>();
            bool homeData = target == "GLB" || target == "FBX";   // GLB and FBX carry everything needed to rebuild the MDL
            var scene = MdlScene(input, homeData, maxTextureSize, out var model, notes);

            bool skinned = model.Meshes.Any(HmSkinning.IsSkinned);
            if (skinned && target != "OBJ")
            {
                string skn = AnimationConverter.FindSkeletonByKey(input, model.Skeleton, out var sk);
                if (sk != null)
                {
                    AnimationConverter.AddSkeleton(scene, sk);
                    int n = AnimationConverter.AttachSkin(scene, model);
                    notes.Add($"skeleton {Path.GetFileName(skn)} ({sk.Joints.Count} joints) attached to {n} skinned mesh(es)");
                    if (HomeObjects.IsBuiltIn(skn)) notes.Add($"the skeleton is the {HomeObjects.BuiltInLabel(skn)} (clothing objects don't carry the avatar skeleton)");
                    var clips = new List<string>();
                    string ani = Path.ChangeExtension(skn, ".ani");
                    if (!File.Exists(ani)) ani = Path.ChangeExtension(input, ".ani");
                    if (File.Exists(ani)) clips.Add(ani);
                    // in a Home object: every clip in it made for this skeleton (a companion's idle / walk / run ...)
                    string objRoot = HomeObjects.ObjectRoot(input);
                    if (objRoot != null && target != "OBJ")
                    {
                        var joints = new HashSet<uint>(sk.Joints.Select(j => j.Hash));
                        IEnumerable<string> anis;
                        try { anis = Directory.EnumerateFiles(objRoot, "*.ani", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList(); }
                        catch (Exception) { anis = Enumerable.Empty<string>(); }
                        foreach (var f in anis)
                        {
                            if (clips.Count >= 64) { notes.Add("more than 64 clips in the object; the first 64 were included"); break; }
                            if (clips.Contains(f, StringComparer.OrdinalIgnoreCase)) continue;
                            try
                            {
                                var c = HmAnimation.Read(File.ReadAllBytes(f));
                                if (c.Items.Count > 0 && c.Items.Count(i => joints.Contains(i.Hash)) * 10 >= c.Items.Count * 9) clips.Add(f);
                            }
                            catch (Exception) { }
                        }
                    }
                    foreach (var f in clips)
                    {
                        try
                        {
                            AnimationConverter.AddClip(scene, HmAnimation.Read(File.ReadAllBytes(f)), Path.GetFileNameWithoutExtension(f), notes);
                        }
                        catch (Exception ex) { notes.Add($"{Path.GetFileName(f)} could not be read: {ex.Message}"); }
                    }
                    if (clips.Count > 0) notes.Add($"{clips.Count} animation(s) included: " + string.Join(", ", clips.Select(Path.GetFileNameWithoutExtension).Take(12)) + (clips.Count > 12 ? ", ..." : ""));
                }
                else notes.Add($"the model is skinned (skeleton key 0x{model.Skeleton:x8}, {model.Word8} joints) but no .SKN with that key was found nearby, so it has no bones in the export; its skin weights are still kept as Home data for converting back. Put the .SKN next to the MDL to get the bones");
            }
            else if (skinned) notes.Add("the model is skinned; OBJ has no skeletons, so it was exported in its bind pose");

            var r = WriteScene(scene, target, outputDir, baseName, overwrite);
            if (r == null)
                return null;
            if (scene.Home != null)
            {
                scene.Home.SourceMdl = Path.GetFullPath(input);
                string sidecar = SidecarPath(r.OutputPath);
                File.WriteAllText(sidecar, System.Text.Json.JsonSerializer.Serialize(scene.Home, HmRoundTrip.JsonIndented), new UTF8Encoding(false));
                string fmt = target == "FBX" ? "FBX" : "GLB";
                r.Details.Add($"Home data saved in the {fmt} and in {Path.GetFileName(sidecar)} - converting this {fmt} back to MDL rebuilds the original materials, texture slots, vertex layout and skin (keep the .home.json next to the {fmt} if you edit it in Blender)");
                if (target == "FBX") r.Details.Add("FBX: lightmap UVs, vertex colours, tangents and skin weights are in the file; the original strips and extra vertex fields are in extra nodes that only this tool reads (Blender drops them on re-export, and they are then recalculated)");
            }
            r.Details.AddRange(scene.Notes);
            r.Details.AddRange(notes);
            if (model.Meshes.Any(m => m.HasDeformers))
                r.Details.Add("some elements use blend shapes / cloth data, which isn't exported");
            return r;
        }

        /// <summary>
        /// Find a texture an MDL refers to (e.g. \ENVIRONMENTS\...\TEXTURES\NAME_C.DDS): the full path under any folder
        /// above the MDL, then the file name next to the MDL, in a TEXTURES folder beside or above it, or nearby.
        /// </summary>
        public static string FindHomeTexture(string mdlPath, string homePath, FileResolver resolver = null)
        {
            if (string.IsNullOrWhiteSpace(homePath)) return null;
            string rel = homePath.Replace('/', '\\').TrimStart('\\').Replace('\\', Path.DirectorySeparatorChar);
            string file = FileResolver.BaseName(homePath);
            string dir = Path.GetDirectoryName(Path.GetFullPath(mdlPath));
            try
            {
                for (string d = dir; !string.IsNullOrEmpty(d); d = Path.GetDirectoryName(d))
                {
                    string c = Path.Combine(d, rel);
                    if (File.Exists(c)) return c;
                }
                foreach (var c in new[]
                {
                    Path.Combine(dir, file),
                    Path.Combine(dir, "TEXTURES", file),
                    Path.Combine(Path.GetDirectoryName(dir) ?? dir, "TEXTURES", file),
                    Path.Combine(Path.GetDirectoryName(dir) ?? dir, file),
                })
                    if (File.Exists(c)) return c;
            }
            catch (Exception)
            {
                // invalid characters in the stored path: fall through to the name search
            }
            return (resolver ?? new FileResolver(mdlPath)).FindPath(file, true);
        }

        // ------------------------------------------------------------------ anything -> collision

        /// <summary>
        /// Convert a collision file, or turn a model into a collision mesh: one welded, triangle-only mesh in world space
        /// with no materials or UVs.
        /// </summary>
        public static Result ConvertToCollision(string input, string target, string outputDir, OverwriteBehavior overwrite, int complexity = 2)
        {
            string ext = Ext(input);
            target = target.ToUpperInvariant();
            if (ext == ".hkx")
            {
                if (target == "HKX")
                    throw new NotSupportedException("The file is already HKX.");
                // collision -> OBJ / GLB to look at it (boxes, Sony's convex hulls and triangle meshes)
                var bodies = Mdl2Hkx.HkxReader.Read(File.ReadAllBytes(input));
                if (bodies.Count == 0)
                    throw new InvalidDataException("no rigid bodies with a convex or mesh shape were found in the HKX.");
                string hkxName = Path.GetFileNameWithoutExtension(input);
                var hkxScene = CollisionTools.SceneFromHkx(hkxName, bodies, out int hkxTris);
                var hr = WriteScene(hkxScene, target, outputDir, hkxName, overwrite);
                if (hr == null)
                    return null;
                hr.Summary = $"{bodies.Count} bodies ({bodies.Count(b => b.Kind == "convex")} convex, {bodies.Count(b => b.Kind == "mesh")} mesh), {hkxTris:N0} triangles";
                return hr;
            }
            if (target == "HKX")
                return CollisionTools.ToHkx(input, outputDir, overwrite, complexity);
            if (target != "OBJ" && target != "GLB")
                throw new NotSupportedException($"{target} isn't a collision output format.");

            var positions = new List<V3>();
            var tris = new List<int[]>();
            var notes = new List<string>();
            if (ext == ".mdl")
            {
                var model = HmCore.ReadMdl(File.ReadAllBytes(input), false);
                foreach (var me in model.Meshes)
                {
                    var P = HmCore.MeshAttribute(me, HmCore.HPosition);
                    if (P == null) continue;
                    int off = positions.Count;
                    positions.AddRange(P.Select(p => new V3(p[0], p[1], p[2])));
                    foreach (var t in HmCore.DecodeTriangles(me.Indices, me.Prim))
                        if (t.All(x => x < P.Count)) tris.Add(new[] { t[0] + off, t[1] + off, t[2] + off });
                }
            }
            else if (ModelExtensions.Contains(ext))
            {
                var doc = LoadSource(input);
                notes.AddRange(doc.Notes.Where(n => !n.StartsWith("texture", StringComparison.OrdinalIgnoreCase)));
                foreach (var part in doc.Collect("full"))
                {
                    int off = positions.Count;
                    positions.AddRange(part.P);
                    foreach (var t in part.Tris)
                        if (t.All(x => x >= 0 && x < part.P.Count)) tris.Add(new[] { t[0] + off, t[1] + off, t[2] + off });
                }
            }
            else
            {
                throw new NotSupportedException($"{ext} files can't be converted.");
            }

            // weld identical positions and drop degenerate triangles
            var index = new Dictionary<(float, float, float), int>();
            var welded = new List<V3>();
            var outTris = new List<int>();
            int Weld(V3 v)
            {
                var key = ((float)v.X, (float)v.Y, (float)v.Z);
                if (!index.TryGetValue(key, out int i))
                {
                    i = welded.Count;
                    welded.Add(new V3(key.Item1, key.Item2, key.Item3));
                    index[key] = i;
                }
                return i;
            }
            foreach (var t in tris)
            {
                int a = Weld(positions[t[0]]), b = Weld(positions[t[1]]), c = Weld(positions[t[2]]);
                if (a == b || b == c || a == c) continue;
                outTris.Add(a); outTris.Add(b); outTris.Add(c);
            }
            if (outTris.Count == 0)
                throw new InvalidDataException("The model has no triangles to build a collision mesh from.");

            // area-weighted vertex normals (OBJ / GLB viewers expect some)
            var normals = new V3[welded.Count];
            for (int k = 0; k < outTris.Count; k += 3)
            {
                var fn = V3.Cross(welded[outTris[k + 1]] - welded[outTris[k]], welded[outTris[k + 2]] - welded[outTris[k]]);
                for (int j = 0; j < 3; j++) normals[outTris[k + j]] += fn;
            }
            string baseName = Path.GetFileNameWithoutExtension(input) + "_collision";
            var mesh = new ExportMesh
            {
                Name = baseName,
                Material = -1,
                P = welded.SelectMany(v => new[] { (float)v.X, (float)v.Y, (float)v.Z }).ToArray(),
                N = normals.Select(n => n.Normalized()).SelectMany(n => new[] { (float)n.X, (float)n.Y, (float)n.Z }).ToArray(),
                UV = new float[welded.Count * 2],
                Tris = outTris.ToArray(),
            };
            var scene = new ExportScene { Name = baseName, Meshes = { mesh } };
            var r = WriteScene(scene, target, outputDir, baseName, overwrite);
            if (r == null)
                return null;
            r.Summary = $"collision mesh: {welded.Count:N0} vertices, {outTris.Count / 3:N0} triangles";
            r.Details.AddRange(notes);
            return r;
        }

        // ------------------------------------------------------------------ writing

        /// <summary>Write a scene as GLB, FBX or OBJ. OBJ with materials goes in its own folder with .mtl and TEXTURES\.</summary>
        internal static Result WriteScene(ExportScene scene, string target, string outputDir, string baseName, OverwriteBehavior overwrite)
        {
            Directory.CreateDirectory(outputDir);
            bool objFolder = target == "OBJ" && (scene.Materials.Count > 0 || scene.Textures.Count > 0);
            string PathFor(string n) => target switch
            {
                "GLB" => Path.Combine(outputDir, n + ".glb"),
                "FBX" => Path.Combine(outputDir, n + ".fbx"),
                _ => objFolder ? Path.Combine(outputDir, n, n + ".obj") : Path.Combine(outputDir, n + ".obj"),
            };
            string name = ReserveName(baseName, PathFor, overwrite, out bool renamed);
            if (name == null)
                return null;
            scene.Name = name;
            string outPath = PathFor(name);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            switch (target)
            {
                case "GLB":
                    File.WriteAllBytes(outPath, HmExport.WriteGlb(scene));
                    break;
                case "FBX":
                    File.WriteAllBytes(outPath, HmExport.WriteFbx(scene));
                    break;
                case "OBJ":
                {
                    var (obj, mtl) = HmExport.WriteObj(scene, "TEXTURES");
                    var utf8 = new UTF8Encoding(false);
                    File.WriteAllText(outPath, obj, utf8);
                    if (objFolder)
                    {
                        string dir = Path.GetDirectoryName(outPath);
                        File.WriteAllText(Path.Combine(dir, name + ".mtl"), mtl, utf8);
                        if (scene.Textures.Count > 0)
                        {
                            string texDir = Path.Combine(dir, "TEXTURES");
                            Directory.CreateDirectory(texDir);
                            foreach (var t in scene.Textures)
                                File.WriteAllBytes(Path.Combine(texDir, t.Name + "." + t.Ext), t.Bytes);
                        }
                    }
                    break;
                }
                default:
                    throw new NotSupportedException($"{target} export isn't supported.");
            }
            int verts = scene.Meshes.Sum(m => m.P.Length / 3), triangles = scene.Meshes.Sum(m => m.Tris.Length / 3);
            return new Result
            {
                OutputPath = outPath,
                Renamed = renamed,
                Summary = $"{scene.Meshes.Count} mesh(es), {verts:N0} vertices, {triangles:N0} triangles, {scene.Materials.Count} material(s), {scene.Textures.Count} texture(s)",
            };
        }

        /// <summary>Output name for <paramref name="baseName"/> after applying the overwrite setting (null = skip).</summary>
        internal static string ReserveName(string baseName, Func<string, string> pathFor, OverwriteBehavior overwrite, out bool renamed)
        {
            renamed = false;
            if (!File.Exists(pathFor(baseName)))
                return baseName;
            switch (overwrite)
            {
                case OverwriteBehavior.Skip:
                    return null;
                case OverwriteBehavior.Rename:
                    for (int i = 1; ; i++)
                    {
                        string candidate = $"{baseName}_{i:D2}";
                        if (!File.Exists(pathFor(candidate)))
                        {
                            renamed = true;
                            return candidate;
                        }
                    }
                default:
                    return baseName;
            }
        }

        // ------------------------------------------------------------------ inspect

        /// <summary>Human-readable details for any supported file.</summary>
        public static string Describe(string path)
        {
            string ext = Ext(path);
            var sb = new StringBuilder();
            var info = new FileInfo(path);
            sb.AppendLine($"-- {info.Name} ({info.Length:N0} bytes)");

            if (ext == ".mdl")
            {
                var bytes = File.ReadAllBytes(path);
                HmModel model = null;
                string error = null;
                try { model = HmCore.ReadMdl(bytes, false); }
                catch (Exception ex) { error = ex.Message; }
                if (model != null)
                    DescribeMdl(sb, path, model);
                else
                {
                    var mdl = HomeModelFile.Read(bytes);
                    sb.AppendLine($"     Could not read the model: {error}");
                    string version = mdl.VersionByte.HasValue ? $"0x{mdl.VersionByte.Value:X2}" : "?";
                    sb.AppendLine(mdl.HasKnownSignature ? $"     Signature {mdl.Signature}, version byte {version}" : "     Signature not recognised - may not be a Home model");
                    sb.AppendLine($"     Header: {HomeModelFile.HexDump(mdl.Header.Take(16).ToArray())}");
                    AppendList(sb, "Referenced files", mdl.ReferencedPaths);
                }
            }
            else if (ext == ".hkx")
            {
                var hkx = HkxPackfile.Read(File.ReadAllBytes(path));
                sb.AppendLine($"     {(hkx.ContentsVersion.Length > 0 ? hkx.ContentsVersion : "Havok (unknown version)")}, packfile v{hkx.FileVersion}, {hkx.ByteOrder}, {hkx.PointerSize}-byte pointers");
                sb.AppendLine($"     Root object: {(string.IsNullOrEmpty(hkx.RootClassName) ? "?" : hkx.RootClassName)}");
                sb.AppendLine($"     Looks like: {hkx.DescribeContents()}");
                sb.AppendLine($"     Sections: {string.Join(", ", hkx.Sections.Select(s => $"{s.Tag} {s.EndOffset:N0} B"))}");
                if (hkx.ClassNames.Count > 0)
                    sb.AppendLine($"     Classes ({hkx.ClassNames.Count}): {string.Join(", ", hkx.ClassNames)}");
                CollisionTools.DescribeHkx(sb, path, File.ReadAllBytes(path));
            }
            else if (ModelExtensions.Contains(ext))
            {
                var doc = LoadSource(path);
                var parts = doc.Collect("full");
                int verts = parts.Sum(p => p.P.Count), tris = parts.Sum(p => p.Tris.Count);
                sb.AppendLine($"     {doc.Format}{(string.IsNullOrEmpty(doc.Generator) ? "" : $" from {doc.Generator}")}");
                sb.AppendLine($"     {parts.Count} part(s), {verts:N0} vertices, {tris:N0} triangles, {(parts.All(p => p.N != null) ? "has normals" : "normals will be generated")}");
                if (parts.Count > 0)
                {
                    var mn = new V3(parts.SelectMany(p => p.P).Min(v => v.X), parts.SelectMany(p => p.P).Min(v => v.Y), parts.SelectMany(p => p.P).Min(v => v.Z));
                    var mx = new V3(parts.SelectMany(p => p.P).Max(v => v.X), parts.SelectMany(p => p.P).Max(v => v.Y), parts.SelectMany(p => p.P).Max(v => v.Z));
                    var size = mx - mn;
                    sb.AppendLine($"     Size: {size.X:0.###} x {size.Y:0.###} x {size.Z:0.###} units (Home uses metres, Y up)");
                }
                sb.AppendLine($"     {doc.Materials.Count} material(s), {doc.Images.Count} texture image(s){(doc.JointNames.Count > 0 ? $", rigged ({doc.JointNames.Count} joints)" : "")}");
                foreach (var m in doc.Materials)
                    sb.AppendLine($"       {m.Name}: {m.AlphaMode.ToLowerInvariant()}{(m.DoubleSided ? ", double-sided" : "")}" +
                                  (m.Textures.Count > 0 ? ", " + string.Join(", ", m.Textures.Select(t => $"{t.Key} = {doc.Images[t.Value].FileName ?? doc.Images[t.Value].Name}")) : ", no textures"));
                int homeMats = doc.Materials.Count(m => m.HomeJson != null), homeMeshes = parts.Count(p => p.HomeJson != null);
                bool sidecar = File.Exists(SidecarPath(path));
                if (homeMats > 0 || homeMeshes > 0 || sidecar)
                    sb.AppendLine($"     Home round-trip data: {homeMeshes} mesh(es), {homeMats} material(s) in the file{(sidecar ? ", plus " + Path.GetFileName(SidecarPath(path)) : "")} - converting to MDL rebuilds the original");
                foreach (var n in doc.Notes)
                    sb.AppendLine($"     Note: {n}");
            }
            else
            {
                sb.AppendLine("     Unsupported file type.");
            }
            return sb.ToString();
        }

        private static void DescribeMdl(StringBuilder sb, string path, HmModel model)
        {
            int verts = model.Meshes.Sum(m => m.VertexCount);
            int tris = model.Meshes.Sum(m => HmCore.DecodeTriangles(m.Indices, m.Prim).Count);
            sb.AppendLine($"     Home model, version 0x{model.Version:X4}, {model.Meshes.Count} element(s), {verts:N0} vertices, {tris:N0} triangles");
            if (model.Skeleton != 0 || model.Meshes.Any(m => m.HasDeformers))
            {
                sb.AppendLine($"     Skinned: skeleton 0x{model.Skeleton:x8}, {model.Word8} joint(s)");
                string skn = AnimationConverter.FindSkeletonByKey(path, model.Skeleton, out _);
                sb.AppendLine(skn == null ? "     Skeleton file: not found beside the model, in its object or among the built-in rigs"
                    : $"     Skeleton file: {Path.GetFileName(skn)}{(HomeObjects.IsBuiltIn(skn) ? " (" + HomeObjects.BuiltInLabel(skn) + ")" : "")}");
            }
            string objRoot = HomeObjects.ObjectRoot(path);
            if (objRoot != null) sb.AppendLine($"     In Home object {Path.GetFileName(objRoot)}");
            var overrides = HomeObjects.TextureOverrides(path, model, null);
            if (model.ModelBound != null)
                sb.AppendLine($"     Bounding sphere radius {model.ModelBound[3]:0.###}");
            var resolver = new FileResolver(path);
            for (int i = 0; i < model.Materials.Count; i++)
            {
                var mt = model.Materials[i];
                int users = model.Meshes.Count(m => m.Material == i);
                sb.AppendLine($"     Material {i}: {HmCore.NameOf(mt.Type)} ({HmExport.KindOf(mt)}), {users} element(s)");
                foreach (var t in mt.Textures)
                {
                    if (overrides.TryGetValue(t.Filename ?? "", out var bound))
                    {
                        sb.AppendLine($"       {HmCore.NameOf(t.Slot)}: {t.Filename}  -> object uses {Path.GetFileName(bound)}");
                        continue;
                    }
                    bool found = FindHomeTexture(path, t.Filename, resolver) != null;
                    sb.AppendLine($"       {HmCore.NameOf(t.Slot)}: {t.Filename}{(found ? "" : "  (not found here)")}");
                }
            }
        }

        private static void AppendList(StringBuilder sb, string title, List<string> items)
        {
            if (items.Count == 0)
            {
                sb.AppendLine($"     {title}: none found");
                return;
            }
            sb.AppendLine($"     {title} ({items.Count}):");
            foreach (var item in items)
                sb.AppendLine($"       {item}");
        }
    }
}

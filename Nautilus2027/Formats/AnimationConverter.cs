#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Nautilus2027.Formats.Home;

namespace Nautilus2027.Formats
{
    /// <summary>
    /// ANI / SKN tab: Home .ANI clips and .SKN skeletons to GLB (lossless) and FBX (baked), and GLB back to
    /// ANI / SKN. Skinned MDLs that use the skeleton are included so the result can be looked at and edited in Blender.
    /// </summary>
    public static class AnimationConverter
    {
        public static readonly string[] AnimationExtensions = { ".ani", ".glb", ".gltf", ".fbx" };
        public static readonly string[] SkeletonExtensions = { ".skn", ".glb", ".gltf", ".fbx" };

        private static string Ext(string p) => Path.GetExtension(p).ToLowerInvariant();

        /// <summary>Skeleton + clips from a GLB / glTF or a binary FBX.</summary>
        internal static GltfAnimDoc ParseOpen(string path, byte[] data = null)
        {
            data ??= File.ReadAllBytes(path);
            return Ext(path) == ".fbx" ? FbxImport.ParseAnimation(data) : GltfAnimImport.Parse(data, new FileResolver(path));
        }

        // ------------------------------------------------------------------ finding related files

        private static IEnumerable<string> Nearby(string path, string pattern, int levels)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int l = 0; l <= levels && !string.IsNullOrEmpty(dir); l++, dir = Path.GetDirectoryName(dir))
            {
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(dir, pattern, l == 0 ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories).Take(400).ToList(); }
                catch (Exception) { continue; }
                foreach (var f in files) if (seen.Add(f)) yield return f;
            }
        }

        /// <summary>Files anywhere in the Home object the file is in (the folder with OBJECT.XML), if it is in one.</summary>
        private static IEnumerable<string> InObject(string path, string pattern)
        {
            string root = HomeObjects.ObjectRoot(path);
            if (root == null) return Enumerable.Empty<string>();
            try { return Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories).Take(2000).ToList(); }
            catch (Exception) { return Enumerable.Empty<string>(); }
        }

        /// <summary>Skeleton candidates: beside the file, nearby, in its object, then the built-in avatar rigs.</summary>
        private static IEnumerable<string> SkeletonCandidates(string path) =>
            Nearby(path, "*.skn", 2).Concat(InObject(path, "*.skn")).Concat(HomeObjects.BuiltInSkeletons());

        private static string SameStem(string path, string ext)
        {
            string c = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? "", Path.GetFileNameWithoutExtension(path) + ext);
            return File.Exists(c) ? c : null;
        }

        /// <summary>The skeleton a clip was made for: same name beside it, else the nearby SKN whose joints cover most of the clip.</summary>
        public static string FindSkeletonFor(string aniPath, HmAnimClip clip, out HmSkeletonFile sk)
        {
            sk = null;
            string same = SameStem(aniPath, ".skn");
            if (same != null)
            {
                try { sk = HmSkeleton.Read(File.ReadAllBytes(same)); return same; }
                catch (Exception) { sk = null; }
            }
            string best = null;
            int bestHits = 0;
            var want = new HashSet<uint>(clip.Items.Select(i => i.Hash));
            foreach (var f in SkeletonCandidates(aniPath).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var s = HmSkeleton.Read(File.ReadAllBytes(f));
                    int hits = s.Joints.Count(j => want.Contains(j.Hash));
                    if (hits > bestHits) { bestHits = hits; best = f; sk = s; }
                }
                catch (Exception) { }
            }
            if (best != null && bestHits * 2 < want.Count) { sk = null; return null; }   // a poor match is worse than none
            return best;
        }

        /// <summary>A nearby skinned MDL that uses this skeleton (same name first).</summary>
        public static string FindModelFor(string path, uint skeletonKey)
        {
            var cands = new List<string>();
            string same = SameStem(path, ".mdl");
            if (same != null) cands.Add(same);
            cands.AddRange(Nearby(path, "*.mdl", 0));
            // in an object: its models too, best LOD first (LOD1 / no LOD before LOD2, LOD3, _LO)
            cands.AddRange(InObject(path, "*.mdl").OrderBy(f =>
            {
                string n = Path.GetFileNameWithoutExtension(f).ToUpperInvariant();
                return n.EndsWith("_LOD1") ? 0 : System.Text.RegularExpressions.Regex.IsMatch(n, "_LOD\\d+$") || n.EndsWith("_LO") ? 2 : 1;
            }));
            foreach (var f in cands.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var head = new byte[16];
                    using (var fs = File.OpenRead(f)) if (fs.Read(head, 0, 16) < 16) continue;
                    if (head[0] == 0x48 && head[1] == 0x4D && HmCoreBE.U32(head, 4) == skeletonKey) return f;
                }
                catch (Exception) { }
            }
            return null;
        }

        /// <summary>Nearby skeleton with this key (same name first), for a skinned MDL.</summary>
        public static string FindSkeletonByKey(string path, uint key, out HmSkeletonFile sk)
        {
            sk = null;
            var cands = new List<string>();
            string same = SameStem(path, ".skn");
            if (same != null) cands.Add(same);
            cands.AddRange(SkeletonCandidates(path));
            foreach (var f in cands.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var s = HmSkeleton.Read(File.ReadAllBytes(f));
                    if (s.Key == key) { sk = s; return f; }
                }
                catch (Exception) { }
            }
            return null;
        }

        // ------------------------------------------------------------------ building scenes

        /// <summary>Joints of a skeleton (with Home data for the round trip) added to a scene.</summary>
        public static void AddSkeleton(ExportScene scene, HmSkeletonFile sk)
        {
            var bind = HmSkeleton.BindWorld(sk);
            for (int i = 0; i < sk.Joints.Count; i++)
            {
                var j = sk.Joints[i];
                scene.Joints.Add(new ExportJoint
                {
                    Name = j.Name,
                    Parent = j.Parent,
                    T = (double[])j.T.Clone(),
                    R = (double[])j.Q.Clone(),
                    S = (double[])j.S.Clone(),
                    InverseBind = HmSkeleton.Inverse(bind[i]),
                    Extras = new Dictionary<string, object> { ["hash"] = j.Hash, ["index"] = i, ["flags"] = (int)j.Flags, ["tw"] = j.TW, ["sw"] = j.SW },
                });
            }
            scene.SkeletonExtras = new Dictionary<string, object> { ["key"] = sk.Key, ["ownsMemory"] = (int)sk.OwnsMemory };
        }

        /// <summary>Skin weights of an MDL's elements onto the scene's meshes (made by SceneFromModel).</summary>
        public static int AttachSkin(ExportScene scene, HmModel model)
        {
            int skinned = 0;
            foreach (var m in scene.Meshes)
            {
                if (m.SourceIndex < 0 || m.SourceIndex >= model.Meshes.Count) continue;
                var s = HmSkinning.Decode(model.Meshes[m.SourceIndex]);
                if (s == null) continue;
                var (J, W) = s.Value;
                if (J.Any(j => j < 0 || j >= scene.Joints.Count)) continue;
                m.Joints = J;
                m.Weights = W;
                skinned++;
            }
            return skinned;
        }

        /// <summary>A clip as a glTF animation on the scene's joints. Joints the skeleton doesn't have are added as extra roots.</summary>
        public static void AddClip(ExportScene scene, HmAnimClip clip, string name, List<string> notes)
        {
            var byHash = new Dictionary<uint, int>();
            for (int i = 0; i < scene.Joints.Count; i++)
                if (scene.Joints[i].Extras != null && scene.Joints[i].Extras.TryGetValue("hash", out var h) && h is uint hu) byHash[hu] = i;
            var an = new ExportAnimation { Name = name, Extras = new Dictionary<string, object> { ["duration"] = clip.Duration, ["flags"] = clip.Flags } };
            int added = 0;
            foreach (var item in clip.Items)
            {
                if (!byHash.TryGetValue(item.Hash, out int ji))
                {
                    ji = scene.Joints.Count;
                    scene.Joints.Add(new ExportJoint { Name = item.Name, Extras = new Dictionary<string, object> { ["hash"] = item.Hash, ["index"] = ji } });
                    byHash[item.Hash] = ji;
                    added++;
                }
                foreach (var ch in item.Channels)
                {
                    string path = ch.Name == HmAnimation.HRotate ? "rotation" : ch.Name == HmAnimation.HTranslate ? "translation" : ch.Name == HmAnimation.HScale ? "scale" : null;
                    if (path == null) continue;
                    int comps = path == "rotation" ? 4 : 3;
                    var keys = ch.IsConstant ? new List<AniKey> { new AniKey { T = 0, V = ch.Constant } } : ch.Keys;
                    var c = new ExportAnimChannel
                    {
                        Joint = ji,
                        Path = path,
                        Times = keys.Select(k => (float)k.T).ToArray(),
                        Values = keys.SelectMany(k => k.V.Take(comps).Select(v => (float)v)).ToArray(),
                        Tangents = keys.SelectMany(k => k.G.Take(comps).Select(v => (float)v)).ToArray(),
                    };
                    an.Channels.Add(c);
                    // a constant channel is also the joint's pose for viewers that don't play the animation
                    if (ch.IsConstant && !scene.Joints[ji].Extras.ContainsKey("flags"))
                    {
                        if (path == "rotation") scene.Joints[ji].R = ch.Constant.Take(4).ToArray();
                        else if (path == "translation") scene.Joints[ji].T = ch.Constant.Take(3).ToArray();
                        else scene.Joints[ji].S = ch.Constant.Take(3).ToArray();
                    }
                }
            }
            if (added > 0)
                notes.Add(added == clip.Items.Count
                    ? $"no skeleton found for {name}: its {added} joints were exported without a hierarchy (put the .SKN next to the .ANI to get the real skeleton)"
                    : $"{added} joint(s) of {name} aren't in the skeleton; added as loose joints");
            scene.Animations.Add(an);
        }

        // ------------------------------------------------------------------ conversions

        private static ModelConverter.Result Write(ExportScene scene, string target, string outputDir, string baseName, OverwriteBehavior overwrite)
        {
            if (target == "FBX" && scene.Animations.Count > 0)
                scene.Notes.Add("FBX curves are baked at 30 frames per second; use GLB for an exact round trip back to ANI");
            var r = ModelConverter.WriteScene(scene, target, outputDir, baseName, overwrite);
            if (r == null) return null;
            r.Summary = $"{scene.Joints.Count} joint(s), {scene.Animations.Sum(a => a.Channels.Count)} animation channel(s), {scene.Meshes.Count(m => m.Joints != null)} skinned mesh(es)";
            r.Details.AddRange(scene.Notes.Distinct());
            return r;
        }

        /// <summary>Scene for a skeleton, with a skinned MDL that uses it when one is nearby (and its textures).</summary>
        internal static ExportScene SkeletonScene(string anchorPath, HmSkeletonFile sk, string name, bool withModel, int maxTextureSize, List<string> notes)
        {
            ExportScene scene = null;
            string mdl = withModel ? FindModelFor(anchorPath, sk.Key) : null;
            if (mdl != null)
            {
                try
                {
                    scene = ModelConverter.MdlScene(mdl, true, maxTextureSize, out var model, notes);
                    scene.Name = name;
                    AddSkeleton(scene, sk);
                    int n = AttachSkin(scene, model);
                    notes.Add($"included {Path.GetFileName(mdl)} ({n} skinned mesh(es)) so you can see the animation on the model");
                }
                catch (Exception ex)
                {
                    notes.Add($"{Path.GetFileName(mdl)} could not be included: {ex.Message}");
                    scene = null;
                }
            }
            if (scene == null)
            {
                scene = new ExportScene { Name = name };
                AddSkeleton(scene, sk);
            }
            return scene;
        }

        public static ModelConverter.Result ConvertAnimation(string input, string target, string outputDir, OverwriteBehavior overwrite, int maxTextureSize = 0)
        {
            string ext = Ext(input);
            target = target.ToUpperInvariant();
            string name = Path.GetFileNameWithoutExtension(input);
            var notes = new List<string>();

            if (ext == ".ani")
            {
                if (target == "ANI") throw new NotSupportedException("The file is already ANI.");
                var clip = HmAnimation.Read(File.ReadAllBytes(input));
                string sknPath = FindSkeletonFor(input, clip, out var sk);
                var scene = sk != null ? SkeletonScene(input, sk, name, true, maxTextureSize, notes) : new ExportScene { Name = name };
                if (sknPath != null) notes.Insert(0, $"skeleton: {Path.GetFileName(sknPath)} ({sk.Joints.Count} joints){(HomeObjects.IsBuiltIn(sknPath) ? ", " + HomeObjects.BuiltInLabel(sknPath) : "")}");
                AddClip(scene, clip, name, notes);
                scene.Notes.InsertRange(0, notes);
                var r = Write(scene, target, outputDir, name, overwrite);
                if (r != null) r.Summary = $"{clip.Duration:0.###} s, {clip.Items.Count} joint(s) animated; " + r.Summary;
                return r;
            }

            // GLB / FBX -> ANI (one per animation), or -> GLB / FBX through the Home conversion
            var doc = ParseOpen(input);
            if (doc.Clips.Count == 0) throw new InvalidDataException("the file has no animations");
            if (target == "ANI")
            {
                Directory.CreateDirectory(outputDir);
                var res = new ModelConverter.Result { Details = new List<string>(doc.Notes) };
                var outs = new List<string>();
                foreach (var (an, clip) in doc.Clips)
                {
                    string baseName = doc.Clips.Count == 1 ? name : name + "_" + HmConvert.SafeFileName(an);
                    string outName = ModelConverter.ReserveName(baseName.ToUpperInvariant(), n => Path.Combine(outputDir, n + ".ANI"), overwrite, out bool renamed);
                    if (outName == null) { res.Details.Add($"{an}: skipped (exists)"); continue; }
                    var bytes = HmAnimation.Write(clip);
                    var back = HmAnimation.Read(bytes);   // read-back check
                    if (back.Items.Count != clip.Items.Count) throw new InvalidDataException("read-back check failed");
                    string p = Path.Combine(outputDir, outName + ".ANI");
                    File.WriteAllBytes(p, bytes);
                    outs.Add(p);
                    res.Renamed |= renamed;
                    res.Details.Add($"{an} -> {Path.GetFileName(p)}: {clip.Duration:0.###} s, {clip.Items.Count} joint(s), {clip.Items.Sum(i => i.Channels.Count(c => !c.IsConstant))} animated channel(s), {bytes.Length:N0} bytes");
                }
                if (outs.Count == 0) return null;
                res.OutputPath = outs.Count == 1 ? outs[0] : outputDir;
                res.Summary = $"{outs.Count} clip(s) for a {doc.Skeleton.Joints.Count}-joint skeleton (key 0x{doc.Skeleton.Key:x8})";
                res.Details.Add("the clip animates joints by name: use it with the skeleton it was made for (convert the same GLB on the Skeleton side to get that .SKN)");
                return res;
            }
            var sc = new ExportScene { Name = name };
            AddSkeleton(sc, doc.Skeleton);
            foreach (var (an, clip) in doc.Clips) AddClip(sc, clip, an, notes);
            sc.Notes.AddRange(doc.Notes.Concat(notes));
            return Write(sc, target, outputDir, name, overwrite);
        }

        public static ModelConverter.Result ConvertSkeleton(string input, string target, string outputDir, OverwriteBehavior overwrite, int maxTextureSize = 0)
        {
            string ext = Ext(input);
            target = target.ToUpperInvariant();
            string name = Path.GetFileNameWithoutExtension(input);
            var notes = new List<string>();

            if (ext == ".skn")
            {
                if (target == "SKN") throw new NotSupportedException("The file is already SKN.");
                var sk = HmSkeleton.Read(File.ReadAllBytes(input));
                if (sk.HasExtras) notes.Add("the skeleton has LOD / procedural joint data, which isn't exported");
                var scene = SkeletonScene(input, sk, name, true, maxTextureSize, notes);
                scene.Notes.InsertRange(0, notes);
                return Write(scene, target, outputDir, name, overwrite);
            }

            var doc = ParseOpen(input);
            if (target == "SKN")
            {
                Directory.CreateDirectory(outputDir);
                string outName = ModelConverter.ReserveName(name.ToUpperInvariant(), n => Path.Combine(outputDir, n + ".SKN"), overwrite, out bool renamed);
                if (outName == null) return null;
                var bytes = HmSkeleton.Write(doc.Skeleton);
                var back = HmSkeleton.Read(bytes);
                if (back.Joints.Count != doc.Skeleton.Joints.Count) throw new InvalidDataException("read-back check failed");
                string p = Path.Combine(outputDir, outName + ".SKN");
                File.WriteAllBytes(p, bytes);
                var r = new ModelConverter.Result
                {
                    OutputPath = p,
                    Renamed = renamed,
                    Summary = $"{doc.Skeleton.Joints.Count} joint(s), key 0x{doc.Skeleton.Key:x8}, {bytes.Length:N0} bytes",
                    Details = new List<string>(doc.Notes),
                };
                if (!doc.HadSkin) r.Details.Add(ext == ".fbx" ? "the file has no skin, so the joints are its bones (or animated objects) as they are posed in the file"
                                                              : "the file has no skin, so the joints are its animated nodes and their parents");
                return r;
            }
            var sc = new ExportScene { Name = name };
            AddSkeleton(sc, doc.Skeleton);
            sc.Notes.AddRange(doc.Notes);
            return Write(sc, target, outputDir, name, overwrite);
        }

        // ------------------------------------------------------------------ inspect

        public static string Describe(string path)
        {
            var sb = new StringBuilder();
            var data = File.ReadAllBytes(path);
            sb.AppendLine($"-- {Path.GetFileName(path)} ({data.Length:N0} bytes)");
            string ext = Ext(path);
            if (ext == ".skn")
            {
                var sk = HmSkeleton.Read(data);
                int roots = sk.Joints.Count(j => j.Parent < 0), depth = 0;
                foreach (var j in sk.Joints) { int d = 0; for (int p = j.Parent; p >= 0; p = sk.Joints[p].Parent) d++; depth = Math.Max(depth, d); }
                sb.AppendLine($"     Home skeleton (SK08), {sk.Joints.Count} joint(s), {roots} root(s), {depth + 1} level(s), key 0x{sk.Key:x8}");
                sb.AppendLine("     Joints: " + string.Join(", ", sk.Joints.Take(24).Select(j => j.Name)) + (sk.Joints.Count > 24 ? $", ... ({sk.Joints.Count - 24} more)" : ""));
                string mdl = FindModelFor(path, sk.Key);
                if (mdl != null) sb.AppendLine($"     Used by {Path.GetFileName(mdl)} (same key)");
                var clips = Nearby(path, "*.ani", 0).Where(f =>
                {
                    try { var c = HmAnimation.Read(File.ReadAllBytes(f)); return c.Items.Count > 0 && c.Items.Count(i => sk.IndexOf(i.Hash) >= 0) * 2 >= c.Items.Count; }
                    catch (Exception) { return false; }
                }).Select(Path.GetFileName).ToList();
                if (clips.Count > 0) sb.AppendLine("     Clips beside it for this skeleton: " + string.Join(", ", clips));
                if (sk.HasExtras) sb.AppendLine("     Has LOD / procedural joint data");
            }
            else if (ext == ".ani")
            {
                var c = HmAnimation.Read(data);
                int animated = c.Items.Sum(i => i.Channels.Count(ch => !ch.IsConstant)), keys = c.Items.Sum(i => i.Channels.Sum(ch => ch.Keys.Count));
                string[] tn = { "constant", "constant (indexed)", "hermite", "half hermite", "packed hermite" };
                sb.AppendLine($"     Home animation clip (AC11), {c.Duration:0.###} s, {c.Items.Count} joint(s), {c.ChannelCount} channel(s): " +
                              string.Join(", ", Enumerable.Range(0, 5).Where(i => c.TypeCounts[i] > 0).Select(i => $"{c.TypeCounts[i]} {tn[i]}")));
                sb.AppendLine($"     {animated} animated channel(s), {keys:N0} key(s), {c.Timeblocks} timeblock(s){(c.HasLocomotion ? ", has locomotion data" : "")}");
                string skn = FindSkeletonFor(path, c, out var sk);
                sb.AppendLine(skn != null ? $"     Skeleton: {Path.GetFileName(skn)}{(HomeObjects.IsBuiltIn(skn) ? " (" + HomeObjects.BuiltInLabel(skn) + ")" : "")} ({c.Items.Count(i => sk.IndexOf(i.Hash) >= 0)} of {c.Items.Count} joints match)"
                                          : "     Skeleton: none found nearby, in the object or among the built-in avatar rigs");
                sb.AppendLine("     Joints: " + string.Join(", ", c.Items.Take(16).Select(i => i.Name + "(" + string.Join("/", i.Channels.Select(ch => HmAnimation.ChannelLabel(ch.Name)[0] + (ch.IsConstant ? "=" : "~"))) + ")")) +
                              (c.Items.Count > 16 ? $", ... ({c.Items.Count - 16} more)" : "") + "   (t/r/s, = constant, ~ animated)");
            }
            else if (ext == ".glb" || ext == ".gltf" || ext == ".fbx")
            {
                var doc = ParseOpen(path, data);
                sb.AppendLine($"     Skeleton: {doc.Skeleton.Joints.Count} joint(s){(doc.HadSkin ? "" : " (no skin: animated nodes)")}, key 0x{doc.Skeleton.Key:x8}");
                foreach (var (n, c) in doc.Clips)
                    sb.AppendLine($"     Animation {n}: {c.Duration:0.###} s, {c.Items.Count} joint(s), {c.Items.Sum(i => i.Channels.Count(ch => !ch.IsConstant))} animated channel(s)");
                if (doc.Clips.Count == 0) sb.AppendLine("     No animations");
                foreach (var n in doc.Notes) sb.AppendLine("     Note: " + n);
            }
            return sb.ToString();
        }
    }
}

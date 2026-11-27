#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Nautilus2027.Formats.Home;

namespace Nautilus2027.Formats
{
    /// <summary>One convertible file found inside a Home object.</summary>
    public sealed class ObjectAsset
    {
        public string Path;
        /// <summary>MDL, SKN, ANI or HKX.</summary>
        public string Kind;
        public string Label;
        public string Details;
        /// <summary>Ticked in the picker to start with.</summary>
        public bool Selected = true;
        /// <summary>A file shipped with the app (dependencies\HomeRigs), not part of the object.</summary>
        public bool BuiltIn;
    }

    /// <summary>A Home object (a folder with OBJECT.XML): what it is and what in it can be converted.</summary>
    public sealed class HomeObjectInfo
    {
        public string Root, Id, Title, Kind, Gender;
        public List<ObjectAsset> Assets = new List<ObjectAsset>();
        public List<string> Notes = new List<string>();

        public string Heading => $"{Title}  -  {Kind}{(Gender != null ? ", " + Gender : "")}  ({Id})";
    }

    /// <summary>
    /// Home objects as a whole: object zips (OBJECTS\GUID\OBJECT.XML + RESOURCES.XML + files) are unpacked under
    /// Output\Objects, their models / skeletons / clips / collisions listed, skinned models matched to their skeleton by
    /// the key in the MDL header (the SKN in the object, else the male / female avatar rigs shipped in dependencies\HomeRigs),
    /// and clothing texture bindings (OBJECT.XML materials -> RESOURCES.XML textures) used in place of the MDL's own paths.
    /// </summary>
    public static class HomeObjects
    {
        public static string RigsFolder => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies", "HomeRigs");
        public static string ExtractRoot => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", "Objects");

        // ------------------------------------------------------------------ built-in avatar rigs

        private sealed class Rig
        {
            public string Path, Label;
            public uint Key;
            public int Joints;
        }

        private static List<Rig> _rigs;
        private static readonly object RigLock = new object();

        private static List<Rig> Rigs()
        {
            lock (RigLock)
            {
                if (_rigs != null) return _rigs;
                var outL = new List<Rig>();
                try
                {
                    if (Directory.Exists(RigsFolder))
                        foreach (var f in Directory.EnumerateFiles(RigsFolder, "*.skn"))
                        {
                            try
                            {
                                var s = HmSkeleton.Read(File.ReadAllBytes(f));
                                string n = System.IO.Path.GetFileNameWithoutExtension(f).ToUpperInvariant();
                                string who = n.StartsWith("FEMALE") ? "female" : n.StartsWith("MALE") ? "male" : "avatar";
                                string part = n.Contains("FACE") ? "head / face" : "body";
                                outL.Add(new Rig { Path = f, Key = s.Key, Joints = s.Joints.Count, Label = $"built-in {who} {part} rig" });
                            }
                            catch (Exception) { }
                        }
                }
                catch (Exception) { }
                _rigs = outL;
                return outL;
            }
        }

        /// <summary>The skeletons shipped with the app (avatar body and face rigs).</summary>
        public static IEnumerable<string> BuiltInSkeletons() => Rigs().Select(r => r.Path);

        /// <summary>The built-in rig with this skeleton key, or null.</summary>
        public static string BuiltInSkeletonFor(uint key) => key == 0 ? null : Rigs().FirstOrDefault(r => r.Key == key)?.Path;

        /// <summary>"built-in male body rig" for a file in the rigs folder, else null.</summary>
        public static string BuiltInLabel(string path) =>
            Rigs().FirstOrDefault(r => string.Equals(System.IO.Path.GetFullPath(r.Path), System.IO.Path.GetFullPath(path ?? ""), StringComparison.OrdinalIgnoreCase))?.Label;

        public static bool IsBuiltIn(string path) => BuiltInLabel(path) != null;

        // ------------------------------------------------------------------ finding objects

        /// <summary>The object folder (the one with OBJECT.XML) a file is in, or null.</summary>
        public static string ObjectRoot(string path)
        {
            try
            {
                string dir = File.Exists(path) ? System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) : System.IO.Path.GetFullPath(path);
                for (int l = 0; l < 12 && !string.IsNullOrEmpty(dir); l++, dir = System.IO.Path.GetDirectoryName(dir))
                    if (FileIn(dir, "OBJECT.XML") != null) return dir;
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>Case-insensitive file lookup in one folder.</summary>
        private static string FileIn(string dir, string name)
        {
            try
            {
                string direct = System.IO.Path.Combine(dir, name);
                if (File.Exists(direct)) return direct;
                return Directory.EnumerateFiles(dir).FirstOrDefault(f => string.Equals(System.IO.Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception) { return null; }
        }

        /// <summary>Object folders (with OBJECT.XML) at or below a folder.</summary>
        public static List<string> FindObjectRoots(string folder, int maxDepth = 6)
        {
            var outL = new List<string>();
            void Walk(string d, int depth)
            {
                if (outL.Count >= 200) return;
                if (FileIn(d, "OBJECT.XML") != null) { outL.Add(d); return; }   // an object's own sub-folders aren't objects
                if (depth >= maxDepth) return;
                IEnumerable<string> subs;
                try { subs = Directory.EnumerateDirectories(d).ToList(); }
                catch (Exception) { return; }
                foreach (var s in subs) Walk(s, depth + 1);
            }
            if (Directory.Exists(folder)) Walk(System.IO.Path.GetFullPath(folder), 0);
            return outL;
        }

        /// <summary>
        /// Unpack a zip under Output\Objects\NAME (skipped when the same zip was already unpacked there). Entries that
        /// would land outside that folder are ignored.
        /// </summary>
        public static string ExtractZip(string zipPath, List<string> notes)
        {
            const long MaxTotal = 8L * 1024 * 1024 * 1024, MaxEntry = 2L * 1024 * 1024 * 1024;
            var info = new FileInfo(zipPath);
            string name = Regex.Replace(System.IO.Path.GetFileNameWithoutExtension(zipPath), "[^A-Za-z0-9_.\\-]+", "_").Trim('.', ' ');
            if (name.Length == 0) name = "object";
            if (name.Length > 80) name = name.Substring(0, 80);
            string dest = System.IO.Path.GetFullPath(System.IO.Path.Combine(ExtractRoot, name));
            string marker = System.IO.Path.Combine(dest, ".nautilus_unpacked");
            string stamp = info.Length + "|" + info.LastWriteTimeUtc.Ticks;
            try { if (File.Exists(marker) && File.ReadAllText(marker) == stamp) return dest; }
            catch (Exception) { }

            Directory.CreateDirectory(dest);
            string prefix = dest.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
            int files = 0, skipped = 0, failed = 0;
            long total = 0;
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            ZipArchive zip;
            try { zip = ZipFile.OpenRead(zipPath); }
            catch (InvalidDataException) { throw new InvalidDataException($"{System.IO.Path.GetFileName(zipPath)} isn't a readable zip (damaged, or not a zip)"); }
            using (zip)
            {
                foreach (var e in zip.Entries)
                {
                    if (string.IsNullOrEmpty(e.Name)) continue;   // folder entry
                    if (e.Length > MaxEntry || total + e.Length > MaxTotal) { skipped++; continue; }
                    // each part of the name made safe for Windows (':' '*' '?' ... and trailing dots / spaces)
                    var parts = e.FullName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => new string(p.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).TrimEnd('.', ' '))
                        .Where(p => p.Length > 0 && p != "." && p != "..").ToArray();
                    if (parts.Length == 0) { skipped++; continue; }
                    string target;
                    try { target = System.IO.Path.GetFullPath(System.IO.Path.Combine(new[] { dest }.Concat(parts).ToArray())); }
                    catch (Exception) { skipped++; continue; }
                    if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { skipped++; continue; }
                    try
                    {
                        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
                        e.ExtractToFile(target, true);
                        files++;
                        total += e.Length;
                    }
                    catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is NotSupportedException)
                    {
                        failed++;
                        if (failed <= 3) notes?.Add($"{System.IO.Path.GetFileName(zipPath)}: {e.FullName} could not be unpacked ({ex.Message})");
                    }
                }
            }
            // only remember a clean unpack (a damaged one is tried again next time)
            if (failed == 0) { try { File.WriteAllText(marker, stamp); } catch (Exception) { } }
            notes?.Add($"{System.IO.Path.GetFileName(zipPath)}: unpacked {files} file(s) to {dest}" +
                       (skipped > 0 ? $" ({skipped} entries skipped: unsafe names or too large)" : "") +
                       (failed > 0 ? $" ({failed} entries damaged)" : ""));
            if (files == 0 && failed > 0) throw new InvalidDataException($"{System.IO.Path.GetFileName(zipPath)}: nothing could be unpacked (the zip is damaged)");
            return dest;
        }

        // ------------------------------------------------------------------ OBJECT.XML / RESOURCES.XML

        /// <summary>A Home XML file; damaged files are repaired where possible (see <see cref="SafeXml"/>), never thrown.</summary>
        private static XDocument LoadXml(string path, List<string> notes = null) => SafeXml.Load(path, notes);

        private static IEnumerable<XElement> Named(XContainer x, string local) => x?.Descendants().Where(e => e.Name.LocalName == local) ?? Enumerable.Empty<XElement>();
        private static XElement Child(XElement x, string local) => x?.Elements().FirstOrDefault(e => e.Name.LocalName == local);
        private static string ChildText(XElement x, string local) => Child(x, local)?.Value?.Trim() ?? "";

        /// <summary>A file named by RESOURCES.XML / OBJECT.XML (relative to the object, any case), or null.</summary>
        public static string ResolveInObject(string root, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || root == null) return null;
            string rel = relative.Replace('\\', '/').Trim().TrimStart('/');
            // never outside the object (".." or a rooted / drive path in a damaged XML)
            if (rel.Split('/').Any(p => p == ".." || p.Contains(':')) || rel.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0) return null;
            string direct;
            try { direct = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, rel.Replace('/', System.IO.Path.DirectorySeparatorChar))); }
            catch (Exception) { return null; }
            if (!direct.StartsWith(System.IO.Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)) return null;
            if (File.Exists(direct)) return direct;
            // walk the parts case-insensitively (the zips are upper case, the XML mixed case)
            string cur = root;
            var parts = rel.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                bool last = i == parts.Length - 1;
                string next = null;
                try
                {
                    next = (last ? Directory.EnumerateFiles(cur) : Directory.EnumerateDirectories(cur))
                        .FirstOrDefault(p => string.Equals(System.IO.Path.GetFileName(p), parts[i], StringComparison.OrdinalIgnoreCase));
                }
                catch (Exception) { }
                if (next == null) return null;
                cur = next;
            }
            return cur;
        }

        /// <summary>RESOURCES.XML: resource name -> file in the object (only files that are there).</summary>
        public static Dictionary<string, string> Resources(string root, List<string> notes = null)
        {
            var outD = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string file = FileIn(root, "RESOURCES.XML");
            if (file == null) { notes?.Add("the object has no RESOURCES.XML"); return outD; }
            var doc = LoadXml(file, notes);
            if (doc == null) return outD;
            foreach (var e in doc.Descendants())
            {
                string n = (string)e.Attribute("name"), f = (string)e.Attribute("file");
                if (string.IsNullOrEmpty(n) || string.IsNullOrEmpty(f) || outD.ContainsKey(n)) continue;
                string p = ResolveInObject(root, f);
                if (p != null) outD[n] = p;
            }
            return outD;
        }

        private static string LocalisedTitle(string root, string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            var doc = LoadXml(FileIn(root, "LOCALISATION.XML"));   // a damaged one just means no title
            foreach (var ph in Named(doc, "PHRASE"))
            {
                var langs = ph.Elements().Where(e => e.Name.LocalName == "LANG").ToList();
                if (!langs.Any(l => (string)l.Attribute("ID") == "REF" && l.Value.Trim() == key)) continue;
                var en = langs.FirstOrDefault(l => ((string)l.Attribute("ID") ?? "").StartsWith("en", StringComparison.OrdinalIgnoreCase));
                string t = en?.Value?.Trim();
                if (!string.IsNullOrEmpty(t)) return Regex.Replace(t, "\\s*\\[#[^\\]]*\\]", "").Trim();
            }
            return null;
        }

        /// <summary>The clothing model entry of OBJECT.XML whose LODs include this MDL file, or null.</summary>
        private static XElement ClothingModel(XDocument obj, string mdlFile)
        {
            string fn = System.IO.Path.GetFileName(mdlFile);
            foreach (var model in Named(Named(obj, "clothing").FirstOrDefault(), "model"))
            {
                var lods = Child(model, "lods");
                if (lods != null && lods.Elements().Any(l => string.Equals(System.IO.Path.GetFileName(l.Value.Trim().Replace('\\', '/')), fn, StringComparison.OrdinalIgnoreCase)))
                    return model;
            }
            return null;
        }

        /// <summary>
        /// Clothing texture bindings for an MDL inside an object: the MDL's own texture path -> the file the object
        /// actually uses (OBJECT.XML material N, map slot -> RESOURCES.XML texture). Materials pair up by order.
        /// </summary>
        public static Dictionary<string, string> TextureOverrides(string mdlPath, HmModel model, List<string> notes)
        {
            var outD = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string root = ObjectRoot(mdlPath);
            if (root == null || model == null) return outD;
            var obj = LoadXml(FileIn(root, "OBJECT.XML"), notes);
            var cm = ClothingModel(obj, mdlPath);
            if (cm == null) return outD;
            var res = Resources(root, notes);
            var mats = Named(Child(cm, "materials"), "material").ToList();
            if (mats.Count != model.Materials.Count)
                notes?.Add($"OBJECT.XML lists {mats.Count} material(s) for this model and the MDL has {model.Materials.Count}; textures were matched by order");
            int bound = 0, external = 0;
            for (int i = 0; i < mats.Count && i < model.Materials.Count; i++)
                foreach (var map in Named(mats[i], "map"))
                {
                    string slot = ChildText(map, "name"), source = ChildText(map, "source");
                    if (slot.Length == 0 || source.Length == 0) continue;
                    var tex = model.Materials[i].Textures.FirstOrDefault(t => string.Equals(HmRoundTrip.SlotName(t.Slot), slot, StringComparison.OrdinalIgnoreCase));
                    if (tex == null || string.IsNullOrEmpty(tex.Filename)) continue;
                    if (res.TryGetValue(source, out var file))
                    {
                        if (!outD.ContainsKey(tex.Filename)) { outD[tex.Filename] = file; bound++; }
                    }
                    else external++;
                }
            if (bound > 0) notes?.Add($"{bound} texture(s) taken from the object's own bindings (OBJECT.XML + RESOURCES.XML)");
            if (external > 0) notes?.Add($"{external} texture(s) come from shared avatar resources (e.g. the skin colour), which aren't in the object; the MDL's own paths were used");
            return outD;
        }

        // ------------------------------------------------------------------ scanning an object

        private static readonly Regex LodRx = new Regex("_LOD(\\d+)$", RegexOptions.IgnoreCase);

        private static (uint Key, int Joints, ushort Version) MdlHead(string path)
        {
            var h = new byte[12];
            using (var fs = File.OpenRead(path)) if (fs.Read(h, 0, 12) < 12) return (0, 0, 0);
            if (h[0] != 0x48 || h[1] != 0x4D) return (0, 0, 0);
            return (HmCoreBE.U32(h, 4), (int)HmCoreBE.U32(h, 8), HmCoreBE.U16(h, 2));
        }

        /// <summary>What an object is and the MDL / SKN / ANI / HKX files in it, with each model's skeleton worked out.</summary>
        public static HomeObjectInfo Scan(string root)
        {
            var info = new HomeObjectInfo { Root = root, Id = System.IO.Path.GetFileName(root.TrimEnd(System.IO.Path.DirectorySeparatorChar)) };
            string objFile = FileIn(root, "OBJECT.XML");
            var obj = objFile != null ? LoadXml(objFile, info.Notes) : null;
            if (objFile == null && FileIn(root, "RESOURCES.XML") != null) info.Notes.Add("the object has no OBJECT.XML (only RESOURCES.XML); its files are listed as found");
            string headerName = ChildText(Named(obj, "header").FirstOrDefault(), "name");
            var clothing = Named(obj, "clothing").FirstOrDefault();
            var rig = Named(obj, "rig").FirstOrDefault();
            string sex = null;
            if (clothing != null)
            {
                string type = ChildText(clothing, "type");
                info.Kind = "clothing" + (type.Length > 0 ? " (" + type + ")" : "");
                info.Title = ChildText(clothing, "name");
            }
            else if (rig != null)
            {
                info.Kind = "avatar rig";
                info.Title = ChildText(rig, "display_name");
                foreach (var f in Named(rig, "field"))
                    if (string.Equals(ChildText(f, "name"), "sex", StringComparison.OrdinalIgnoreCase)) sex = ChildText(f, "value");
            }
            else if (Named(obj, "mini_game").Any()) info.Kind = "mini-game";
            else if (Named(obj, "portable").Any(e => e.HasElements)) info.Kind = "companion / portable";
            else if (Named(obj, "active").Any(e => e.HasElements)) info.Kind = "furniture / active item";
            else
            {
                bool scene = false;
                try { scene = obj == null && Directory.EnumerateFiles(root, "*.scene", SearchOption.AllDirectories).Any(); }
                catch (Exception) { }
                info.Kind = obj != null ? "object" : scene ? "scene" : "folder";
            }
            string loc = LocalisedTitle(root, headerName);
            if (!string.IsNullOrEmpty(loc) && string.IsNullOrEmpty(info.Title)) info.Title = loc;
            else if (!string.IsNullOrEmpty(loc) && loc.Length < 60) info.Title += " - " + loc;
            if (string.IsNullOrEmpty(info.Title)) info.Title = info.Id;

            List<string> FilesOf(string pattern)
            {
                try { return Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList(); }
                catch (Exception) { return new List<string>(); }
            }
            string Rel(string f) => f.Length > root.Length ? f.Substring(root.Length).TrimStart(System.IO.Path.DirectorySeparatorChar) : System.IO.Path.GetFileName(f);

            // skeletons in the object, by key
            var sknByKey = new Dictionary<uint, string>();
            var sknAssets = new List<ObjectAsset>();
            foreach (var f in FilesOf("*.skn"))
            {
                try
                {
                    var s = HmSkeleton.Read(File.ReadAllBytes(f));
                    if (!sknByKey.ContainsKey(s.Key)) sknByKey[s.Key] = f;
                    sknAssets.Add(new ObjectAsset { Path = f, Kind = "SKN", Label = System.IO.Path.GetFileName(f), Details = $"skeleton, {s.Joints.Count} joints, key 0x{s.Key:x8}  -  {Rel(f)}" });
                }
                catch (Exception ex) { info.Notes.Add($"{Rel(f)}: {ex.Message}"); }
            }

            // models
            var usedBuiltIn = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int missingSkeleton = 0;
            var mdls = FilesOf("*.mdl");
            bool hasLods = mdls.Any(f => LodRx.IsMatch(System.IO.Path.GetFileNameWithoutExtension(f)));
            foreach (var f in mdls)
            {
                string stem = System.IO.Path.GetFileNameWithoutExtension(f);
                var lod = LodRx.Match(stem);
                var a = new ObjectAsset { Path = f, Kind = "MDL", Label = System.IO.Path.GetFileName(f) };
                var parts = new List<string>();
                if (lod.Success) parts.Add("LOD" + lod.Groups[1].Value);
                try
                {
                    var (key, joints, ver) = MdlHead(f);
                    if (ver == 0) parts.Add("not a Home MDL");
                    else if (key == 0) parts.Add("static");
                    else
                    {
                        parts.Add($"skinned, {joints} joints");
                        if (sknByKey.TryGetValue(key, out var skn)) parts.Add("skeleton " + System.IO.Path.GetFileName(skn));
                        else
                        {
                            string bi = BuiltInSkeletonFor(key);
                            if (bi != null)
                            {
                                parts.Add($"skeleton {System.IO.Path.GetFileName(bi)} ({BuiltInLabel(bi)})");
                                usedBuiltIn[bi] = usedBuiltIn.TryGetValue(bi, out int c) ? c + 1 : 1;
                            }
                            else { parts.Add($"skeleton key 0x{key:x8} not found"); missingSkeleton++; }
                        }
                    }
                }
                catch (Exception ex) { parts.Add("unreadable: " + ex.Message); }
                parts.Add(Rel(f));
                a.Details = string.Join("  -  ", parts);
                // start with the best LOD of each model ticked
                a.Selected = !hasLods || !lod.Success || lod.Groups[1].Value == "1";
                info.Assets.Add(a);
            }
            info.Assets.AddRange(sknAssets);
            // clothing uses the avatar's skeleton, which isn't in the object: offer the built-in rig it needs
            foreach (var kv in usedBuiltIn)
            {
                info.Assets.Add(new ObjectAsset
                {
                    Path = kv.Key, Kind = "SKN", BuiltIn = true,
                    Label = System.IO.Path.GetFileName(kv.Key),
                    Details = $"{BuiltInLabel(kv.Key)}, used by {kv.Value} model(s) in this object (the avatar skeleton isn't in clothing objects)",
                });
            }
            if (missingSkeleton > 0) info.Notes.Add($"{missingSkeleton} skinned model(s) use a skeleton that isn't in the object or built in; they will export without bones");

            // gender: the rig's sex field, else the avatar rig the models use, else the names
            if (!string.IsNullOrEmpty(sex)) info.Gender = sex.ToLowerInvariant();
            else if (usedBuiltIn.Count > 0)
            {
                string n = System.IO.Path.GetFileName(usedBuiltIn.Keys.First()).ToUpperInvariant();
                info.Gender = n.StartsWith("FEMALE") ? "female" : n.StartsWith("MALE") ? "male" : null;
            }
            else if (Regex.IsMatch(info.Title ?? "", "^Female", RegexOptions.IgnoreCase)) info.Gender = "female";
            else if (Regex.IsMatch(info.Title ?? "", "^Male", RegexOptions.IgnoreCase)) info.Gender = "male";

            // clips
            foreach (var f in FilesOf("*.ani"))
                info.Assets.Add(new ObjectAsset { Path = f, Kind = "ANI", Label = System.IO.Path.GetFileName(f), Details = $"animation clip, {new FileInfo(f).Length:N0} bytes  -  {Rel(f)}" });
            // collisions
            foreach (var f in FilesOf("*.hkx"))
                info.Assets.Add(new ObjectAsset
                {
                    Path = f, Kind = "HKX", Label = System.IO.Path.GetFileName(f),
                    Details = (System.IO.Path.GetFileName(f).ToUpperInvariant().Contains("SAFEVOLUME") ? "safe volume" : "collision") + "  -  " + Rel(f),
                });
            // skies and particle effects
            foreach (var f in FilesOf("*.atmos"))
                info.Assets.Add(new ObjectAsset
                {
                    Path = f, Kind = "ATMOS", Label = System.IO.Path.GetFileName(f),
                    Details = "sky / atmosphere" + (HmAtmos.CdataPath(f) != null ? " with cloud data (CDATA)" : "") + "  -  " + Rel(f),
                });
            foreach (var f in FilesOf("*.efx"))
                info.Assets.Add(new ObjectAsset { Path = f, Kind = "EFX", Label = System.IO.Path.GetFileName(f), Details = $"particle effect, {new FileInfo(f).Length:N0} bytes  -  {Rel(f)}" });
            foreach (var f in FilesOf("*.agf"))
            {
                string what = "TexGen layer";
                try { var a = Home.HmAgf.Read(File.ReadAllBytes(f)); what = $"TexGen layer {a.Width} x {a.Height}" + (a.Warnings.Count > 0 ? " (damaged?)" : ""); }
                catch (Exception ex) { what = "TexGen layer (unreadable: " + ex.Message + ")"; }
                info.Assets.Add(new ObjectAsset { Path = f, Kind = "AGF", Label = System.IO.Path.GetFileName(f), Details = what + "  -  " + Rel(f) });
            }
            foreach (var f in FilesOf("*.rig"))
            {
                string what = "face rig";
                try { var r = Home.HmRig.Read(File.ReadAllBytes(f)); what = $"face rig, {r.Controls.Count} controls"; }
                catch (Exception ex) { what = "face rig (unreadable: " + ex.Message + ")"; }
                info.Assets.Add(new ObjectAsset { Path = f, Kind = "RIG", Label = System.IO.Path.GetFileName(f), Details = what + "  -  " + Rel(f) });
            }
            foreach (var f in FilesOf("*.probe"))
                info.Assets.Add(new ObjectAsset { Path = f, Kind = "PROBE", Label = System.IO.Path.GetFileName(f), Details = $"light probe (scene lights), {new FileInfo(f).Length:N0} bytes  -  {Rel(f)}" });
            foreach (var f in FilesOf("*.scene"))
            {
                string what = "scene";
                try
                {
                    var sd = HomeScene.Load(f);
                    what = $"scene: {sd.Effects.Count} placed effect(s)" + (sd.Atmos != null ? ", sky" : "") + (sd.Probe != null ? ", light probe" : "");
                    if (sd.Notes.Count > 0) what += $" ({sd.Notes.Count} note(s))";
                }
                catch (Exception ex) { what = "scene (unreadable: " + ex.Message + ")"; }
                info.Assets.Add(new ObjectAsset { Path = f, Kind = "SCENE", Label = System.IO.Path.GetFileName(f), Details = what + "  -  " + Rel(f) });
            }
            return info;
        }

        /// <summary>One line per object for a tool's list.</summary>
        public static string Summary(HomeObjectInfo o)
        {
            var sb = new StringBuilder();
            int m = o.Assets.Count(a => a.Kind == "MDL"), s = o.Assets.Count(a => a.Kind == "SKN" && !a.BuiltIn), an = o.Assets.Count(a => a.Kind == "ANI"), h = o.Assets.Count(a => a.Kind == "HKX");
            int at = o.Assets.Count(a => a.Kind == "ATMOS"), fx = o.Assets.Count(a => a.Kind == "EFX"), sc = o.Assets.Count(a => a.Kind == "SCENE"), pr = o.Assets.Count(a => a.Kind == "PROBE");
            sb.Append($"-- Object {o.Heading}: {m} model(s), {s} skeleton(s), {an} clip(s), {h} collision(s)" + (at + fx > 0 ? $", {at} sky file(s), {fx} effect(s)" : "") +
                      (sc + pr > 0 ? $", {sc} scene(s), {pr} light probe(s)" : ""));
            var bi = o.Assets.Where(a => a.BuiltIn).Select(a => a.Label).ToList();
            if (bi.Count > 0) sb.Append("; uses built-in " + string.Join(", ", bi));
            sb.AppendLine();
            foreach (var n in o.Notes) sb.AppendLine("     " + n);
            return sb.ToString();
        }
    }
}

#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nautilus2027.Formats.Home;

namespace Nautilus2027.Formats
{
    /// <summary>
    /// AGF / RIG tab: avatar TexGen layers (.AGF, indexed images) to / from PNG and other images, and head face rigs
    /// (.RIG, "FD05") to / from JSON.
    /// </summary>
    public static class AgfRigConverter
    {
        public static readonly string[] AgfExtensions = { ".agf", ".png", ".tga", ".bmp", ".jpg", ".jpeg", ".tif", ".tiff" };
        public static readonly string[] AgfTargets = { "AGF", "PNG" };
        public static readonly string[] RigExtensions = { ".rig", ".json" };
        public static readonly string[] RigTargets = { "RIG", "JSON" };

        private static string Ext(string p) => Path.GetExtension(p).ToLowerInvariant();

        private static readonly JsonSerializerOptions Pretty = new JsonSerializerOptions { WriteIndented = true };

        // ------------------------------------------------------------------ AGF

        /// <summary>The original .AGF for an image: same name beside it (or the name less "_edit" / " (2)" style suffixes).</summary>
        private static string AgfBeside(string image)
        {
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(image)) ?? "";
                string stem = Path.GetFileNameWithoutExtension(image);
                return Directory.EnumerateFiles(dir).FirstOrDefault(f => Ext(f) == ".agf" &&
                    string.Equals(Path.GetFileNameWithoutExtension(f), stem, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception) { return null; }
        }

        private static AgfImage LoadAgf(string input, List<string> notes)
        {
            var bytes = File.ReadAllBytes(input);
            if (Ext(input) == ".agf")
            {
                var a = HmAgf.Read(bytes);
                notes?.AddRange(a.Warnings);
                return a;
            }
            string orig = AgfBeside(input);
            byte[] origBytes = null;
            if (orig != null)
                try { origBytes = File.ReadAllBytes(orig); notes?.Add($"palette from {Path.GetFileName(orig)}"); }
                catch (Exception) { }
            return HmAgf.FromImage(bytes, Path.GetFileName(input), origBytes, notes);
        }

        public static ModelConverter.Result ConvertAgf(string input, string target, string outputDir, OverwriteBehavior overwrite)
        {
            target = target.ToUpperInvariant();
            string ext = Ext(input);
            if (ext == "." + target.ToLowerInvariant()) throw new NotSupportedException($"The file is already {target}.");
            var notes = new List<string>();
            var img = LoadAgf(input, notes);
            string name = Path.GetFileNameWithoutExtension(input).ToUpperInvariant();
            Directory.CreateDirectory(outputDir);
            var r = new ModelConverter.Result();
            switch (target)
            {
                case "AGF":
                {
                    string outName = ModelConverter.ReserveName(name, n => Path.Combine(outputDir, n + ".AGF"), overwrite, out bool renamed);
                    if (outName == null) return null;
                    byte[] data = HmAgf.Write(img);
                    var back = HmAgf.Read(data);
                    if (!back.Pixels.AsSpan().SequenceEqual(img.Pixels)) throw new InvalidDataException("read-back check failed (pixels changed)");
                    string p = Path.Combine(outputDir, outName + ".AGF");
                    File.WriteAllBytes(p, data);
                    r.OutputPath = p; r.Renamed = renamed;
                    r.Summary = $"{img.Width} x {img.Height}, {data.Length:N0} bytes";
                    if (img.Original != null && data.AsSpan().SequenceEqual(img.Original)) r.Details.Add("identical to the original AGF");
                    else if (img.Original != null) r.Details.Add("changed bands re-packed; unchanged bands kept from the original");
                    break;
                }
                case "PNG":
                {
                    string outName = ModelConverter.ReserveName(name, n => Path.Combine(outputDir, n + ".png"), overwrite, out bool renamed);
                    if (outName == null) return null;
                    string p = Path.Combine(outputDir, outName + ".png");
                    File.WriteAllBytes(p, HmAgf.ToPng(img));
                    r.OutputPath = p; r.Renamed = renamed;
                    r.Summary = $"{img.Width} x {img.Height}, indexed (256-colour palette)";
                    r.Details.Add("an indexed PNG with the AGF's palette (the original AGF is kept inside it): edit it in indexed / palette mode to keep the colours exact, then convert back with AGF picked");
                    break;
                }
                default:
                    throw new NotSupportedException(target + " isn't an AGF output");
            }
            r.Details.AddRange(notes.Distinct());
            return r;
        }

        public static string DescribeAgf(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"-- {Path.GetFileName(path)} ({new FileInfo(path).Length:N0} bytes)");
            var notes = new List<string>();
            var img = LoadAgf(path, notes);
            sb.Append(HmAgf.Describe(img));
            foreach (var n in notes.Where(n => !img.Warnings.Contains(n))) sb.AppendLine("     Note: " + n);
            string root = HomeObjects.ObjectRoot(path);
            if (root != null)
            {
                var res = Directory.EnumerateFiles(root).FirstOrDefault(f => string.Equals(Path.GetFileName(f), "RESOURCES.XML", StringComparison.OrdinalIgnoreCase));
                if (res != null)
                {
                    var x = SafeXml.Load(res, null);
                    var hit = x?.Descendants().FirstOrDefault(e => SafeXml.Attr(e, "file") is string f && f.Replace('\\', '/').EndsWith(Path.GetFileName(path), StringComparison.OrdinalIgnoreCase));
                    if (hit != null) sb.AppendLine($"     In RESOURCES.XML as \"{SafeXml.Attr(hit, "name")}\"" + (SafeXml.Attr(hit, "deferred_loading") == "true" ? " (loaded when needed)" : ""));
                }
            }
            return sb.ToString();
        }

        public static PreviewData PreviewAgf(string path)
        {
            var notes = new List<string>();
            var img = LoadAgf(path, notes);
            var rgba = HmAgf.ToRgba(img);
            float aspect = img.Height / (float)img.Width, s = 2f;
            float hw = s / 2, hh = s * aspect / 2;
            var data = new PreviewData { Title = Path.GetFileName(path) };
            var layer = new PreviewLayer { Name = "Image" };
            layer.Meshes.Add(new PreviewMesh
            {
                Name = "AGF",
                P = new[] { -hw, hh * 2, 0f, hw, hh * 2, 0f, hw, 0f, 0f, -hw, 0f, 0f },
                N = new[] { 0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f },
                UV = new[] { 0f, 0f, 1f, 0f, 1f, 1f, 0f, 1f },
                Tris = new[] { 0, 2, 1, 0, 3, 2 },
                Image = rgba,
                Emissive = true,
            });
            data.Layers.Add(layer);
            data.Info.Add($"{img.Width} x {img.Height} TexGen layer, shown with its palette");
            data.Info.AddRange(notes.Take(4));
            return data;
        }

        // ------------------------------------------------------------------ RIG

        /// <summary>The HEADCONTROLRIG.XML that points at this rig (in the object's DATA folder), and the controls it lists.</summary>
        private static (string Xml, List<string> Controls) ControlRigXml(string rigPath)
        {
            try
            {
                string root = HomeObjects.ObjectRoot(rigPath) ?? Path.GetDirectoryName(Path.GetFullPath(rigPath));
                string want = Path.GetFileName(rigPath);
                foreach (var f in Directory.EnumerateFiles(root, "*.xml", SearchOption.AllDirectories).Take(400))
                {
                    if (!Path.GetFileName(f).ToUpperInvariant().Contains("CONTROLRIG")) continue;
                    var x = SafeXml.Load(f, null);
                    if (x?.Root == null) continue;
                    string p = SafeXml.ChildText(x.Root, "PATH") ?? "";
                    if (!p.Replace('\\', '/').EndsWith(want, StringComparison.OrdinalIgnoreCase)) continue;
                    var list = x.Root.Elements().Where(e => string.Equals(e.Name.LocalName, "CONTROL", StringComparison.OrdinalIgnoreCase))
                                .Select(e => e.Value.Trim()).Where(v => v.Length > 0).ToList();
                    return (Path.GetFileName(f), list);
                }
            }
            catch (Exception) { }
            return (null, null);
        }

        private static RigFile LoadRig(string input, List<string> notes, out string sourceName)
        {
            if (Ext(input) == ".rig")
            {
                var r = HmRig.Read(File.ReadAllBytes(input));
                notes?.AddRange(r.Warnings);
                sourceName = Path.GetFileName(input);
                return r;
            }
            JsonNode j;
            try { j = JsonNode.Parse(File.ReadAllText(input), documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }); }
            catch (JsonException ex) { throw new InvalidDataException("the JSON can't be read: " + ex.Message); }
            sourceName = (j as JsonObject)?["source"] is JsonValue sv && sv.TryGetValue(out string s) ? s : Path.GetFileNameWithoutExtension(input) + ".RIG";
            return HmRig.FromJson(j, notes);
        }

        public static ModelConverter.Result ConvertRig(string input, string target, string outputDir, OverwriteBehavior overwrite)
        {
            target = target.ToUpperInvariant();
            string ext = Ext(input);
            if (ext == "." + target.ToLowerInvariant()) throw new NotSupportedException($"The file is already {target}.");
            var notes = new List<string>();
            var rig = LoadRig(input, notes, out string sourceName);
            string name = Path.GetFileNameWithoutExtension(sourceName).ToUpperInvariant();
            Directory.CreateDirectory(outputDir);
            var r = new ModelConverter.Result();
            switch (target)
            {
                case "RIG":
                {
                    string outName = ModelConverter.ReserveName(name, n => Path.Combine(outputDir, n + ".RIG"), overwrite, out bool renamed);
                    if (outName == null) return null;
                    byte[] data = HmRig.Write(rig);
                    var back = HmRig.Read(data);
                    if (back.Controls.Count != rig.Controls.Count) throw new InvalidDataException("read-back check failed");
                    string p = Path.Combine(outputDir, outName + ".RIG");
                    File.WriteAllBytes(p, data);
                    r.OutputPath = p; r.Renamed = renamed;
                    r.Summary = $"{rig.Controls.Count} control(s), {data.Length:N0} bytes";
                    break;
                }
                case "JSON":
                {
                    string outName = ModelConverter.ReserveName(name, n => Path.Combine(outputDir, n + ".rig.json"), overwrite, out bool renamed);
                    if (outName == null) return null;
                    var (xml, exposed) = ext == ".rig" ? ControlRigXml(input) : (null, null);
                    string p = Path.Combine(outputDir, outName + ".rig.json");
                    File.WriteAllText(p, HmRig.ToJson(rig, sourceName, exposed).ToJsonString(Pretty), new UTF8Encoding(false));
                    r.OutputPath = p; r.Renamed = renamed;
                    r.Summary = $"{rig.Controls.Count} control(s), {rig.Controls.Count(c => c.Name == null)} unnamed";
                    if (xml != null) r.Details.Add($"\"slider\" marks the {exposed.Count} controls {xml} lists");
                    r.Details.Add("edit and convert the .json back with RIG picked");
                    break;
                }
                default:
                    throw new NotSupportedException(target + " isn't a RIG output");
            }
            r.Details.AddRange(notes.Distinct());
            return r;
        }

        public static string DescribeRig(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"-- {Path.GetFileName(path)} ({new FileInfo(path).Length:N0} bytes)");
            var notes = new List<string>();
            var rig = LoadRig(path, notes, out _);
            var (xml, exposed) = Ext(path) == ".rig" ? ControlRigXml(path) : (null, null);
            sb.Append(HmRig.Describe(rig, exposed, xml));
            foreach (var n in notes.Where(n => !rig.Warnings.Contains(n))) sb.AppendLine("     Note: " + n);
            return sb.ToString();
        }
    }
}

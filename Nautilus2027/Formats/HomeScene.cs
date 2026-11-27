#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Nautilus2027.Formats
{
    /// <summary>A particle effect placed in a scene (an ATGParticleEffectType game object).</summary>
    public sealed class SceneEffect
    {
        public int Index;
        public string Name;
        /// <summary>The effect's path as the scene gives it (relative to the resource root), and the file found for it.</summary>
        public string Efx, EfxPath;
        /// <summary>Position, rotation in degrees (Maya xyz order), scale.</summary>
        public double[] T = { 0, 0, 0 }, R = { 0, 0, 0 }, S = { 1, 1, 1 };
        public bool HasRotate, HasScale;
        /// <summary>Where the game object is in the scene text.</summary>
        public int Start, Length;
    }

    public sealed class HomeSceneDoc
    {
        public string Path, Text;
        /// <summary>The older "gap:" scene editor format (gap:transform / degrees) rather than the newer one (radians).</summary>
        public bool Gap;
        public bool Bom;
        public Dictionary<string, string> Assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<SceneEffect> Effects = new List<SceneEffect>();
        public string Atmos, AtmosPath, Probe, ProbePath;
        public List<string> Notes = new List<string>();
    }

    /// <summary>
    /// Home .SCENE files (scene editor XML). Read with patterns rather than an XML parser so a damaged scene still gives
    /// what can be found in it; written back by changing only the numbers of the moved effects, keeping everything else
    /// byte for byte.
    /// </summary>
    public static class HomeScene
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const RegexOptions RO = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        private static readonly Regex AssetRx = new Regex(@"<(?:gap:)?asset\b([^<>]*?)/?>", RO);
        private static readonly Regex AttrRx = new Regex(@"([\w:\-]+)\s*=\s*(?:""([^""]*)""|'([^']*)')", RO);
        private static readonly Regex GoTagRx = new Regex(@"<(/?)(?:gap:)?gameObject\b([^<>]*?)(/?)>", RO);

        private static Dictionary<string, string> Attrs(string s)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in AttrRx.Matches(s ?? ""))
            {
                string k = m.Groups[1].Value;
                int c = k.IndexOf(':');
                if (c >= 0 && !k.StartsWith("xmlns", StringComparison.OrdinalIgnoreCase)) k = k.Substring(c + 1);   // xsi:type -> type
                d[k] = Decode(m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value);
            }
            return d;
        }

        private static string Decode(string s) =>
            s.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&apos;", "'").Replace("&amp;", "&");

        /// <summary>"file:///resource_root/build/a/b.efx" -> "a/b.efx" (null if the path climbs out with "..").</summary>
        public static string RelativeSource(string src)
        {
            if (string.IsNullOrWhiteSpace(src)) return null;
            string s = src.Trim().Replace('\\', '/');
            if (s.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) s = s.Substring(5);
            s = s.TrimStart('/');
            if (s.StartsWith("resource_root/", StringComparison.OrdinalIgnoreCase)) s = s.Substring(14);
            if (s.StartsWith("build/", StringComparison.OrdinalIgnoreCase)) s = s.Substring(6);
            var parts = s.Split('/').Where(p => p.Length > 0 && p != ".").ToList();
            if (parts.Count == 0 || parts.Any(p => p == ".." || p.Contains(':') || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)) return null;
            return string.Join("/", parts);
        }

        /// <summary>The file a scene path points to: tried under the scene's folder and each folder above it.</summary>
        public static string Resolve(string scenePath, string source)
        {
            string rel = RelativeSource(source);
            if (rel == null) return null;
            string local = rel.Replace('/', Path.DirectorySeparatorChar);
            try
            {
                int up = 0;
                for (string d = Path.GetDirectoryName(Path.GetFullPath(scenePath)); !string.IsNullOrEmpty(d) && up < 10; d = Path.GetDirectoryName(d), up++)
                {
                    string c = Path.Combine(d, local);
                    if (File.Exists(c)) return c;
                    c = Path.Combine(d, "build", local);
                    if (File.Exists(c)) return c;
                }
                // last try: the bare file name beside the scene
                string beside = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(scenePath)) ?? "", Path.GetFileName(local));
                if (File.Exists(beside)) return beside;
            }
            catch (Exception) { }
            return null;
        }

        private static double[] Nums(string s, int n, double[] def)
        {
            if (s == null) return def;
            var parts = s.Split(new[] { ' ', '\t', '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < n) return def;
            var r = new double[n];
            for (int i = 0; i < n; i++)
            {
                if (double.TryParse(parts[i], NumberStyles.Float, Inv, out double d) && double.IsFinite(d)) r[i] = d;
                else if (def != null) r[i] = def[i];
                else return null;
            }
            return r;
        }

        private static string Child(string body, string tag)
        {
            var m = Regex.Match(body, @"<(?:gap:)?" + tag + @"\s*>([^<]*)</(?:gap:)?" + tag + @"\s*>", RO);
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string RefIn(string body, string tag)
        {
            var m = Regex.Match(body, @"<(?:gap:)?" + tag + @"\b[^<>]*?\bref\s*=\s*[""']#?([^""']*)[""']", RO);
            return m.Success ? Decode(m.Groups[1].Value) : null;
        }

        public static HomeSceneDoc Load(string path)
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length > SafeXml.MaxBytes) throw new InvalidDataException($"{Path.GetFileName(path)} is too large for a scene ({bytes.Length:N0} bytes)");
            var doc = Parse(SafeXml.ReadText(bytes), path);
            doc.Bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            return doc;
        }

        public static HomeSceneDoc Parse(string text, string path)
        {
            var doc = new HomeSceneDoc { Path = path, Text = text ?? "" };
            text = doc.Text;
            if (text.IndexOf("gameObject", StringComparison.OrdinalIgnoreCase) < 0 && text.IndexOf("asset", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidDataException("not a Home scene (no assets or game objects in it)");
            doc.Gap = Regex.IsMatch(text, @"<gap:(game|gameObject|asset)\b", RO);
            if (!Regex.IsMatch(text, @"</(?:gap:)?game\s*>\s*$", RO)) doc.Notes.Add("the scene doesn't end with </game> (cut off or damaged): read what was there");

            foreach (Match m in AssetRx.Matches(text))
            {
                var a = Attrs(m.Groups[1].Value);
                if (a.TryGetValue("name", out string n) && n.Length > 0 && a.TryGetValue("source", out string s))
                {
                    if (doc.Assets.ContainsKey(n)) doc.Notes.Add($"asset \"{n}\" is listed twice (the first is used)");
                    else doc.Assets[n] = s;
                }
            }

            // game objects, with their extent (they can hold other game objects inside folders, but not usually inside each other)
            var tags = GoTagRx.Matches(text).Cast<Match>().ToList();
            int fx = 0;
            for (int i = 0; i < tags.Count; i++)
            {
                var open = tags[i];
                if (open.Groups[1].Value == "/") continue;
                var a = Attrs(open.Groups[2].Value);
                a.TryGetValue("type", out string type);
                type ??= "";
                bool selfClose = open.Groups[3].Value == "/";
                int end = open.Index + open.Length;
                string body = "";
                if (!selfClose)
                {
                    int depth = 1, j = i + 1;
                    for (; j < tags.Count; j++)
                    {
                        if (tags[j].Groups[1].Value == "/") depth--;
                        else if (tags[j].Groups[3].Value != "/") depth++;
                        if (depth == 0) break;
                    }
                    if (j < tags.Count) { end = tags[j].Index + tags[j].Length; body = text.Substring(open.Index + open.Length, tags[j].Index - open.Index - open.Length); }
                    else
                    {
                        // never closed (damaged): up to the next game object
                        int next = i + 1 < tags.Count ? tags[i + 1].Index : text.Length;
                        body = text.Substring(open.Index + open.Length, next - open.Index - open.Length);
                        end = next;
                        doc.Notes.Add($"game object \"{(a.TryGetValue("name", out string nn) ? nn : "?")}\" is not closed");
                    }
                }
                a.TryGetValue("name", out string name);
                if (type.EndsWith("ATGParticleEffectType", StringComparison.OrdinalIgnoreCase))
                {
                    var e = new SceneEffect { Index = fx++, Name = name ?? "Effect" + fx, Start = open.Index, Length = end - open.Index };
                    string refName = RefIn(body, "efx_filename");
                    if (a.TryGetValue("efx_filename", out string attr) && attr.Length > 0) e.Efx = attr;
                    else if (refName != null && doc.Assets.TryGetValue(refName, out string src)) e.Efx = src;
                    else if (refName != null) doc.Notes.Add($"effect \"{e.Name}\" uses asset \"{refName}\", which isn't in the asset list");
                    else doc.Notes.Add($"effect \"{e.Name}\" names no .efx file");
                    if (e.Efx != null)
                    {
                        e.EfxPath = Resolve(path, e.Efx);
                        e.Efx = RelativeSource(e.Efx) ?? e.Efx;
                    }
                    string tr = Child(body, "translate"), ro = Child(body, "rotate"), sc = Child(body, "scale"), xf = Child(body, "transform");
                    e.HasRotate = ro != null; e.HasScale = sc != null;
                    e.T = Nums(tr, 3, new double[] { 0, 0, 0 });
                    e.S = Nums(sc, 3, new double[] { 1, 1, 1 });
                    var r = Nums(ro, 3, new double[] { 0, 0, 0 });
                    e.R = doc.Gap ? r : r.Select(x => x * 180 / Math.PI).ToArray();   // the newer format stores radians
                    if (tr == null && xf != null)
                    {
                        var m16 = Nums(xf, 16, null);
                        if (m16 != null) e.T = new[] { m16[12], m16[13], m16[14] };
                    }
                    doc.Effects.Add(e);
                    // skip what's inside it
                }
                else if (type.EndsWith("atmosFileType", StringComparison.OrdinalIgnoreCase) && doc.Atmos == null)
                {
                    string refName = RefIn(body, "file");
                    if (refName != null && doc.Assets.TryGetValue(refName, out string src)) { doc.Atmos = RelativeSource(src) ?? src; doc.AtmosPath = Resolve(path, src); }
                }
                else if (type.EndsWith("probeFileType", StringComparison.OrdinalIgnoreCase) && doc.Probe == null)
                {
                    string refName = RefIn(body, "file");
                    if (refName != null && doc.Assets.TryGetValue(refName, out string src)) { doc.Probe = RelativeSource(src) ?? src; doc.ProbePath = Resolve(path, src); }
                }
            }
            // skies listed only as assets (no Atmosphere File object)
            if (doc.Atmos == null)
            {
                var a = doc.Assets.Values.FirstOrDefault(v => v.EndsWith(".atmos", StringComparison.OrdinalIgnoreCase));
                if (a != null) { doc.Atmos = RelativeSource(a) ?? a; doc.AtmosPath = Resolve(path, a); doc.Notes.Add("the sky is listed as an asset but no Atmosphere File object uses it"); }
            }
            if (doc.Probe == null)
            {
                var a = doc.Assets.Values.FirstOrDefault(v => v.EndsWith(".probe", StringComparison.OrdinalIgnoreCase));
                if (a != null) { doc.Probe = RelativeSource(a) ?? a; doc.ProbePath = Resolve(path, a); }
            }
            foreach (var g in doc.Effects.Where(e => e.Efx != null && e.EfxPath == null).GroupBy(e => e.Efx, StringComparer.OrdinalIgnoreCase))
                doc.Notes.Add($"effect file not found: {g.Key} (used {g.Count()} time(s))");
            if (doc.Atmos != null && doc.AtmosPath == null) doc.Notes.Add("sky file not found: " + doc.Atmos);
            if (doc.Probe != null && doc.ProbePath == null) doc.Notes.Add("light probe file not found: " + doc.Probe);
            return doc;
        }

        // ------------------------------------------------------------------ transforms

        /// <summary>Euler degrees (Maya xyz order: x first) -> quaternion xyzw.</summary>
        public static double[] EulerToQuat(double[] deg)
        {
            double hx = deg[0] * Math.PI / 360, hy = deg[1] * Math.PI / 360, hz = deg[2] * Math.PI / 360;
            double cx = Math.Cos(hx), sx = Math.Sin(hx), cy = Math.Cos(hy), sy = Math.Sin(hy), cz = Math.Cos(hz), sz = Math.Sin(hz);
            // q = qz * qy * qx
            return new[]
            {
                sx * cy * cz - cx * sy * sz,
                cx * sy * cz + sx * cy * sz,
                cx * cy * sz - sx * sy * cz,
                cx * cy * cz + sx * sy * sz,
            };
        }

        /// <summary>Quaternion xyzw -> Euler degrees (Maya xyz order).</summary>
        public static double[] QuatToEuler(double[] q)
        {
            double x = q[0], y = q[1], z = q[2], w = q[3];
            // rotation matrix (column vectors) R = Rz Ry Rx
            double r20 = 2 * (x * z - w * y), r21 = 2 * (y * z + w * x), r22 = 1 - 2 * (x * x + y * y);
            double r10 = 2 * (x * y + w * z), r00 = 1 - 2 * (y * y + z * z);
            double ry = Math.Asin(Math.Clamp(-r20, -1, 1));
            double rx, rz;
            if (Math.Abs(r20) < 0.999999) { rx = Math.Atan2(r21, r22); rz = Math.Atan2(r10, r00); }
            else
            {
                // gimbal lock (y at +-90): put the turn in x
                double r01 = 2 * (x * y - w * z), r11 = 1 - 2 * (x * x + z * z);
                rx = Math.Atan2(r20 < 0 ? r01 : -r01, r11); rz = 0;
            }
            return new[] { rx * 180 / Math.PI, ry * 180 / Math.PI, rz * 180 / Math.PI };
        }

        /// <summary>The object's matrix, column-major (translation in m[12..14]).</summary>
        public static double[] Matrix(SceneEffect e) => Home.HmSkeleton.FromQts(EulerToQuat(e.R), e.T, e.S);

        // ------------------------------------------------------------------ writing

        private static string F(double v)
        {
            if (!double.IsFinite(v) || Math.Abs(v) < 1e-9) v = 0;   // no -1.3e-17 from rotation round-off
            float f = (float)v;
            if (f == 0) return "0";
            return f.ToString("R", Inv);
        }

        private static string F3(double[] v) => $"{F(v[0])} {F(v[1])} {F(v[2])}";

        private static string SetChild(string body, string tag, string value, bool gap, bool addIfMissing, params string[] after)
        {
            var rx = new Regex(@"(<(?:gap:)?" + tag + @"\s*>)([^<]*)(</(?:gap:)?" + tag + @"\s*>)", RO);
            if (rx.IsMatch(body)) return rx.Replace(body, m => m.Groups[1].Value + value + m.Groups[3].Value, 1);
            if (!addIfMissing) return body;
            // insert as the first child, indented like the next line
            var ind = Regex.Match(body, @"^\r?\n([ \t]*)<");
            string nl = body.Contains("\r\n") ? "\r\n" : "\n";
            string pad = ind.Success ? ind.Groups[1].Value : "  ";
            string p = gap ? "gap:" : "";
            // after the last of the given siblings, when there is one (translate, rotate, scale in that order)
            int at = -1;
            foreach (var sib in after)
            {
                var sm = Regex.Match(body, @"</(?:gap:)?" + sib + @"\s*>", RO);
                if (sm.Success) at = Math.Max(at, sm.Index + sm.Length);
            }
            if (at >= 0)
            {
                var lineInd = Regex.Match(body.Substring(0, at), @"\n([ \t]*)<[^\n]*$");
                string pad2 = lineInd.Success ? lineInd.Groups[1].Value : pad;
                return body.Substring(0, at) + $"{nl}{pad2}<{p}{tag}>{value}</{p}{tag}>" + body.Substring(at);
            }
            if (body.Length == 0) return $"{nl}{pad}<{p}{tag}>{value}</{p}{tag}>{nl}";
            return $"{nl}{pad}<{p}{tag}>{value}</{p}{tag}>" + body;
        }

        /// <summary>The scene text with the effects' new placements (matched by order and name).</summary>
        public static string Write(HomeSceneDoc doc, IList<SceneEffect> edited, List<string> notes)
        {
            var fresh = Parse(doc.Text, doc.Path);
            var text = doc.Text;
            var plan = new List<(SceneEffect Old, SceneEffect New)>();
            foreach (var e in edited)
            {
                var match = fresh.Effects.FirstOrDefault(x => x.Index == e.Index && x.Name == e.Name)
                            ?? fresh.Effects.FirstOrDefault(x => x.Name == e.Name && !plan.Any(p => p.Old == x));
                if (match == null) { notes?.Add($"effect \"{e.Name}\" isn't in the scene any more: skipped"); continue; }
                plan.Add((match, e));
            }
            int changed = 0;
            foreach (var (old, e) in plan.OrderByDescending(p => p.Old.Start))
            {
                bool moved = Enumerable.Range(0, 3).Any(i => Math.Abs(old.T[i] - e.T[i]) > 1e-5);
                bool turned = Enumerable.Range(0, 3).Any(i => Math.Abs(AngleDiff(old.R[i], e.R[i])) > 1e-4);
                bool scaled = Enumerable.Range(0, 3).Any(i => Math.Abs(old.S[i] - e.S[i]) > 1e-6);
                if (!moved && !turned && !scaled) continue;
                string whole = text.Substring(old.Start, old.Length);
                int gt = whole.IndexOf('>');
                if (gt < 0 || whole[gt - 1] == '/')
                {
                    // <gameObject ... /> : open it up
                    if (gt < 0) continue;
                    string close = doc.Gap ? "</gap:gameObject>" : "</gameObject>";
                    whole = whole.Substring(0, gt - 1).TrimEnd() + ">" + close;
                    gt = whole.IndexOf('>');
                }
                var endTag = Regex.Match(whole, @"</(?:gap:)?gameObject\s*>$", RO);
                int closeAt = endTag.Success && endTag.Index > gt ? endTag.Index : whole.Length;   // a damaged object may have no end tag
                string head = whole.Substring(0, gt + 1), body = whole.Substring(gt + 1, closeAt - gt - 1), tail = whole.Substring(closeAt);
                if (doc.Gap)
                {
                    var m = Matrix(e);
                    // gap:transform is written row by row with the translation in the last row
                    body = SetChild(body, "transform", string.Join(" ", m.Select(F)), true, false);
                    body = SetChild(body, "translate", F3(e.T), true, true);
                    body = SetChild(body, "rotate", F3(e.R), true, turned, "transform", "translate");
                    body = SetChild(body, "scale", F3(e.S), true, scaled, "transform", "translate", "rotate");
                    var bb = Nums(Child(body, "boundingBox"), 6, null);
                    if (bb != null)
                    {
                        var nb = new double[6];
                        for (int i = 0; i < 3; i++)
                        {
                            double ratio = Math.Abs(old.S[i]) > 1e-9 ? Math.Abs(e.S[i] / old.S[i]) : 1;
                            double c = (bb[i] + bb[i + 3]) / 2 - old.T[i] , h = (bb[i + 3] - bb[i]) / 2;
                            double nc = e.T[i] + c * ratio;
                            nb[i] = nc - h * ratio; nb[i + 3] = nc + h * ratio;
                        }
                        body = SetChild(body, "boundingBox", string.Join(" ", nb.Select(F)), true, false);
                    }
                }
                else
                {
                    body = SetChild(body, "translate", F3(e.T), false, true);
                    if (turned || old.HasRotate) body = SetChild(body, "rotate", F3(e.R.Select(x => x * Math.PI / 180).ToArray()), false, true, "translate");
                    if (scaled || old.HasScale) body = SetChild(body, "scale", F3(e.S), false, true, "translate", "rotate");
                }
                if (body.Length > 0 && !body.EndsWith("\n") && !body.TrimEnd(' ', '\t').EndsWith("\n"))
                {
                    var ind = Regex.Match(text.Substring(0, old.Start), @"([ \t]*)$");
                    body += (text.Contains("\r\n") ? "\r\n" : "\n") + (ind.Success ? ind.Groups[1].Value : "");
                }
                text = text.Substring(0, old.Start) + head + body + tail + text.Substring(old.Start + old.Length);
                notes?.Add($"{e.Name}: " + string.Join(", ", new[] { moved ? "moved" : null, turned ? "turned" : null, scaled ? "scaled" : null }.Where(x => x != null)));
                changed++;
            }
            if (changed == 0) notes?.Add("no effect was moved, turned or scaled: the scene is written unchanged");
            return text;
        }

        private static double AngleDiff(double a, double b)
        {
            double d = (a - b) % 360;
            if (d > 180) d -= 360;
            if (d < -180) d += 360;
            return d;
        }

        public static byte[] Bytes(HomeSceneDoc doc, string text)
        {
            var body = new UTF8Encoding(false).GetBytes(text);
            if (!doc.Bom) return body;
            var b = new byte[body.Length + 3];
            b[0] = 0xEF; b[1] = 0xBB; b[2] = 0xBF;
            Buffer.BlockCopy(body, 0, b, 3, body.Length);
            return b;
        }

        public static string Describe(HomeSceneDoc doc)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"     Home scene ({(doc.Gap ? "older gap: format" : "scene editor format")}), {doc.Assets.Count} asset(s), {doc.Effects.Count} particle effect(s) placed");
            if (doc.Atmos != null) sb.AppendLine($"     Sky: {doc.Atmos}{(doc.AtmosPath == null ? " (not found)" : "")}");
            if (doc.Probe != null) sb.AppendLine($"     Light probe: {doc.Probe}{(doc.ProbePath == null ? " (not found)" : "")}");
            foreach (var e in doc.Effects.Take(60))
                sb.AppendLine($"       FX {e.Index}: {e.Name} = {Path.GetFileName(e.Efx ?? "?")} at ({F3(e.T)})" +
                              (e.R.Any(x => Math.Abs(x) > 1e-6) ? $", turned ({string.Join(" ", e.R.Select(x => x.ToString("0.##", Inv)))})°" : "") +
                              (e.S.Any(x => Math.Abs(x - 1) > 1e-6) ? $", scale ({F3(e.S)})" : "") +
                              (e.Efx != null && e.EfxPath == null ? "  [file not found]" : ""));
            if (doc.Effects.Count > 60) sb.AppendLine($"       ... and {doc.Effects.Count - 60} more");
            foreach (var n in doc.Notes) sb.AppendLine("     Note: " + n);
            return sb.ToString();
        }
    }
}

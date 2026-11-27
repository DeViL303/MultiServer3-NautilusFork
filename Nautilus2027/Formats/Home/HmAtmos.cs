#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Nautilus2027.Formats;

// Home sky / atmosphere: .ATMOS (XML written by the Maya "Home Sky" tool) and its .CDATA (particle cloud sprites).
//
// ATMOS: <HomeAtmosphere type="SceneDefinition" version="7"> with SkySceneSettings, BasicSky or GradientSky, ExpHeightFog,
// SceneFog, SpriteCloud (textured TriMesh of Vert / Tri), ParticleCloudShadingParams, ParticleCloudDome, ParticleCloud
// (one per cloud: vertexDataAddress / numSprites into the CDATA) and CloudVertData (size of the CDATA).
// The exporter writes one attribute per line, tab indented, with a few attributes spaced "name  = " - the spacing is kept.
//
// CDATA (Hub::ParticleCloudVertexData, 16 bytes a sprite): s16 x, y, z = position / geomBoundSphereRadius * 32767
// (relative to the cloud's geomBoundSpherePos), then 10 bytes of baked shading kept as they are. Each cloud's block is
// padded to 128 bytes; the file ends with one extra byte after the last cloud (kept).
//
// Reading and writing back an unedited file gives the same bytes (checked on the apartment, Home Square and VeeMee skies).
namespace Nautilus2027.Formats.Home
{
    public sealed class AtmosAttr
    {
        public string Name, Value;
        /// <summary>Text between the name and the opening quote (" = " normally).</summary>
        public string Sep = " = ";
    }

    public sealed class AtmosElement
    {
        public string Name;
        public List<AtmosAttr> Attrs = new List<AtmosAttr>();
        public List<AtmosElement> Children = new List<AtmosElement>();
        /// <summary>Written on one line (Vert / Tri).</summary>
        public bool SingleLine;

        public string Get(string name) => Attrs.FirstOrDefault(a => a.Name == name)?.Value;

        public void Set(string name, string value)
        {
            var a = Attrs.FirstOrDefault(x => x.Name == name);
            if (a == null) Attrs.Add(new AtmosAttr { Name = name, Value = value, Sep = SingleLine ? "=" : " = " });
            else a.Value = value;
        }

        public double Num(string name, double def = 0) =>
            double.TryParse(Get(name), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : def;

        public double[] Vec(string name)
        {
            var s = Get(name);
            if (s == null) return null;
            return s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0).ToArray();
        }
    }

    public sealed class CloudSprite
    {
        /// <summary>Position relative to the cloud's geomBoundSpherePos.</summary>
        public double X, Y, Z;
        /// <summary>The other 10 bytes of the record (baked shading), kept as they are.</summary>
        public byte[] Data = new byte[10];
    }

    public sealed class AtmosDoc
    {
        public bool Bom;
        public string NewLine = "\r\n";
        public string Prolog = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n";
        public AtmosElement Root;
        /// <summary>Sprites of each ParticleCloud (same order as the elements); empty when there is no CDATA.</summary>
        public List<List<CloudSprite>> Clouds = new List<List<CloudSprite>>();
        public byte[] CdataTail = { 1 };
        public bool HasCdata;

        public IEnumerable<AtmosElement> Elements(string name) => Root.Children.Where(e => e.Name == name);
        public AtmosElement First(string name) => Root.Children.FirstOrDefault(e => e.Name == name);
    }

    public static class HmAtmos
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public static string F6(double v) => v.ToString("F6", Inv);

        // ------------------------------------------------------------------ ATMOS text

        private static readonly Regex TagOpen = new Regex(@"<(/?)([A-Za-z_][\w:.\-]*)|<!--|<\?|<!", RegexOptions.Compiled);
        private static readonly Regex AttrRx = new Regex(@"\G(\s+)([A-Za-z_][\w:.\-]*)(\s*=\s*)(?:""([^""]*)""|'([^']*)'|([^\s/>""']+))", RegexOptions.Compiled);
        private static readonly Regex TagEnd = new Regex(@"\G\s*(/?)>", RegexOptions.Compiled);

        public static AtmosDoc Parse(string text) => Parse(text, null);

        /// <summary>
        /// Read an ATMOS text. Damaged files are read as far as possible (comments skipped, single-quoted / unquoted values
        /// taken, junk inside a tag skipped, unmatched closing tags ignored, a cut-off file closed); what was fixed goes into
        /// <paramref name="notes"/>. A clean file reads and writes back to the same text.
        /// </summary>
        public static AtmosDoc Parse(string text, List<string> notes)
        {
            var doc = new AtmosDoc();
            text ??= "";
            if (text.Length > 0 && text[0] == '\uFEFF') { doc.Bom = true; text = text.Substring(1); }
            if (text.Trim().Length == 0) throw new InvalidDataException("the file is empty");
            doc.NewLine = text.Contains("\r\n") ? "\r\n" : "\n";
            int start = text.IndexOf("<HomeAtmosphere", StringComparison.Ordinal);
            if (start < 0) start = text.IndexOf("<homeatmosphere", StringComparison.OrdinalIgnoreCase);
            var fixes = new List<string>();
            if (start < 0)
            {
                // no root: take the first element that looks like a sky element and put a root around them
                var any = Regex.Match(text, @"<(SkySceneSettings|BasicSky|GradientSky|TexturedSky|ExpHeightFog|SceneFog|SpriteCloud|ParticleCloud\w*|CloudVertData)\b");
                if (!any.Success) throw new InvalidDataException("not a Home atmosphere file (no <HomeAtmosphere> or sky elements)");
                doc.Root = new AtmosElement { Name = "HomeAtmosphere" };
                doc.Root.Attrs.Add(new AtmosAttr { Name = "type", Value = "SceneDefinition" });
                doc.Root.Attrs.Add(new AtmosAttr { Name = "version", Value = "7" });
                doc.Prolog = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + doc.NewLine;
                fixes.Add("no <HomeAtmosphere> root: one was added around the sky elements");
                start = any.Index;
            }
            else doc.Prolog = text.Substring(0, start);
            var stack = new Stack<AtmosElement>();
            bool synthetic = doc.Root != null;
            if (synthetic) stack.Push(doc.Root);
            int pos = start, junk = 0, strayClose = 0, quoteFix = 0;
            while (pos < text.Length)
            {
                var m = TagOpen.Match(text, pos);
                if (!m.Success) break;
                if (m.Value == "<!--")
                {
                    int e = text.IndexOf("-->", m.Index + 4, StringComparison.Ordinal);
                    pos = e < 0 ? text.Length : e + 3;
                    if (!fixes.Contains("comments dropped")) fixes.Add("comments dropped");
                    continue;
                }
                if (m.Value == "<?" || m.Value == "<!")
                {
                    int e = text.IndexOf('>', m.Index + 2);
                    pos = e < 0 ? text.Length : e + 1;
                    continue;
                }
                if (m.Groups[1].Value == "/")
                {
                    string closeName = m.Groups[2].Value;
                    int gt = text.IndexOf('>', m.Index);
                    pos = gt < 0 ? text.Length : gt + 1;
                    if (stack.Any(e => e.Name == closeName))
                        while (stack.Count > 0 && stack.Pop().Name != closeName) { }
                    else strayClose++;
                    if (stack.Count == 0) break;
                    continue;
                }
                var el = new AtmosElement { Name = m.Groups[2].Value };
                int p = m.Index + m.Length;
                bool selfClosing = false, closed = false;
                while (p < text.Length)
                {
                    var e = TagEnd.Match(text, p);
                    if (e.Success) { selfClosing = e.Groups[1].Value == "/"; p = e.Index + e.Length; closed = true; break; }
                    var a = AttrRx.Match(text, p);
                    if (a.Success)
                    {
                        string val = a.Groups[4].Success ? a.Groups[4].Value : a.Groups[5].Success ? a.Groups[5].Value : a.Groups[6].Value;
                        if (!a.Groups[4].Success) quoteFix++;
                        if (el.Attrs.Any(x => x.Name == a.Groups[2].Value)) junk++;
                        else el.Attrs.Add(new AtmosAttr { Name = a.Groups[2].Value, Sep = a.Groups[3].Value, Value = val });
                        p = a.Index + a.Length;
                        continue;
                    }
                    if (text[p] == '<') break;   // the tag never ended: a new one starts here
                    p++;                          // junk character inside the tag
                    junk++;
                }
                if (!closed) fixes.Add($"<{el.Name}> isn't closed with '>'");
                el.SingleLine = text.IndexOf('\n', m.Index, Math.Max(0, p - m.Index)) < 0;
                if (stack.Count > 0) stack.Peek().Children.Add(el);
                else if (doc.Root == null) doc.Root = el;
                else { doc.Root.Children.Add(el); fixes.Add($"<{el.Name}> was after the end of the root; kept inside it"); }
                if (!selfClosing && closed) stack.Push(el);
                pos = p;
            }
            if (doc.Root == null) throw new InvalidDataException("not a Home atmosphere file");
            if (stack.Count > (synthetic ? 1 : 0))
                fixes.Add($"the file ends early ({stack.Count} element(s) left open); they were closed");
            if (junk > 0) fixes.Add($"{junk} unreadable / repeated character(s) or attribute(s) inside tags skipped");
            if (strayClose > 0) fixes.Add($"{strayClose} unmatched closing tag(s) ignored");
            if (quoteFix > 0) fixes.Add($"{quoteFix} value(s) without double quotes read as they are (written back with double quotes)");
            foreach (var f in fixes) notes?.Add("ATMOS repaired: " + f);
            return doc;
        }

        public static string Write(AtmosDoc doc)
        {
            string nl = doc.NewLine ?? "\r\n";
            var sb = new StringBuilder();
            if (doc.Bom) sb.Append('﻿');
            sb.Append(doc.Prolog ?? "");
            void W(AtmosElement el, int depth)
            {
                string ind = new string('\t', depth);
                if (el.SingleLine)
                {
                    sb.Append(ind).Append('<').Append(el.Name);
                    foreach (var a in el.Attrs) sb.Append(' ').Append(a.Name).Append(a.Sep ?? "=").Append('"').Append(a.Value).Append('"');
                    if (el.Children.Count == 0) { sb.Append(" />").Append(nl); return; }
                    sb.Append('>').Append(nl);
                }
                else
                {
                    sb.Append(ind).Append('<').Append(el.Name).Append(nl);
                    foreach (var a in el.Attrs) sb.Append(ind).Append('\t').Append(a.Name).Append(a.Sep ?? " = ").Append('"').Append(a.Value).Append('"').Append(nl);
                    if (el.Children.Count == 0) { sb.Append(ind).Append("/>").Append(nl); return; }
                    sb.Append(ind).Append("\t>").Append(nl);
                }
                foreach (var c in el.Children) W(c, depth + 1);
                sb.Append(ind).Append("</").Append(el.Name).Append('>').Append(nl);
            }
            W(doc.Root, 0);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ CDATA

        public static List<AtmosElement> ParticleClouds(AtmosDoc doc) => doc.Elements("ParticleCloud").ToList();

        /// <summary>Sprites of each ParticleCloud from the CDATA bytes.</summary>
        public static void ReadCdata(AtmosDoc doc, byte[] data, List<string> notes)
        {
            doc.Clouds.Clear();
            doc.HasCdata = true;
            int end = 0;
            foreach (var c in ParticleClouds(doc))
            {
                double addrD = c.Num("vertexDataAddress", -1), nD = c.Num("numSprites", 0);
                double R = c.Num("geomBoundSphereRadius", 1);
                var list = new List<CloudSprite>();
                if (!(R > 0) || double.IsInfinity(R)) { notes?.Add($"a particle cloud has geomBoundSphereRadius {c.Get("geomBoundSphereRadius") ?? "(missing)"}; 1 was used"); R = 1; }
                if (addrD < 0 || addrD > data.Length || nD < 0 || double.IsNaN(nD))
                {
                    notes?.Add($"a particle cloud's vertexDataAddress / numSprites ({c.Get("vertexDataAddress") ?? "missing"} / {c.Get("numSprites") ?? "missing"}) don't fit the CDATA; it has no sprites");
                    doc.Clouds.Add(list);
                    continue;
                }
                int addr = (int)addrD, n = (int)Math.Min(nD, (data.Length - addr) / 16 + 1);
                for (int i = 0; i < n; i++)
                {
                    int o = addr + 16 * i;
                    if (o < 0 || o + 16 > data.Length) { notes?.Add($"cloud at {addr}: sprite {i} is past the end of the CDATA"); break; }
                    short x = (short)((data[o] << 8) | data[o + 1]), y = (short)((data[o + 2] << 8) | data[o + 3]), z = (short)((data[o + 4] << 8) | data[o + 5]);
                    var s = new CloudSprite { X = Math.Round(x / 32767.0 * R, 6), Y = Math.Round(y / 32767.0 * R, 6), Z = Math.Round(z / 32767.0 * R, 6) };
                    Buffer.BlockCopy(data, o + 6, s.Data, 0, 10);
                    list.Add(s);
                }
                doc.Clouds.Add(list);
                end = Math.Max(end, addr + 16 * list.Count);
            }
            doc.CdataTail = end < data.Length ? data.Skip(end).ToArray() : Array.Empty<byte>();
        }

        /// <summary>
        /// CDATA from the sprites, updating each ParticleCloud's vertexDataAddress / numSprites (and the bound radius when a
        /// sprite was moved outside it) and CloudVertData's size.
        /// </summary>
        public static byte[] WriteCdata(AtmosDoc doc)
        {
            var clouds = ParticleClouds(doc);
            var ms = new MemoryStream();
            for (int k = 0; k < clouds.Count && k < doc.Clouds.Count; k++)
            {
                var c = clouds[k];
                var sprites = doc.Clouds[k];
                if (k > 0) { int pad = (int)((128 - ms.Length % 128) % 128); ms.Write(new byte[pad], 0, pad); }
                double R = c.Num("geomBoundSphereRadius", 1);
                foreach (var s in sprites)
                {
                    if (!double.IsFinite(s.X)) s.X = 0;
                    if (!double.IsFinite(s.Y)) s.Y = 0;
                    if (!double.IsFinite(s.Z)) s.Z = 0;
                }
                double far = sprites.Count == 0 ? 0 : sprites.Max(s => Math.Max(Math.Abs(s.X), Math.Max(Math.Abs(s.Y), Math.Abs(s.Z))));
                if (!(R > 0) || double.IsInfinity(R)) R = 0;
                if (far > R * (1 + 1e-6) + 1e-5 || R == 0)   // a hair over from text round-off is clamped, not a new radius
                {
                    R = Math.Max(far * 1.0001, 1);
                    c.Set("geomBoundSphereRadius", F6(R));
                    R = c.Num("geomBoundSphereRadius", R);
                }
                c.Set("vertexDataAddress", ms.Length.ToString(Inv));
                c.Set("numSprites", sprites.Count.ToString(Inv));
                foreach (var s in sprites)
                {
                    foreach (var v in new[] { s.X, s.Y, s.Z })
                    {
                        int q = (int)Math.Round(v / R * 32767, MidpointRounding.AwayFromZero);
                        q = Math.Clamp(q, -32768, 32767);
                        ms.WriteByte((byte)(q >> 8)); ms.WriteByte((byte)q);
                    }
                    var d = s.Data ?? new byte[10];
                    ms.Write(d, 0, Math.Min(10, d.Length));
                    if (d.Length < 10) ms.Write(new byte[10 - d.Length], 0, 10 - d.Length);
                }
            }
            if (doc.CdataTail != null) ms.Write(doc.CdataTail, 0, doc.CdataTail.Length);
            var bytes = ms.ToArray();
            var cvd = doc.First("CloudVertData");
            if (cvd != null) cvd.Set("size", bytes.Length.ToString(Inv));
            return bytes;
        }

        // ------------------------------------------------------------------ files

        public static string CdataPath(string atmosPath)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(atmosPath)) ?? "", stem = Path.GetFileNameWithoutExtension(atmosPath);
            foreach (var ext in new[] { ".CDATA", ".cdata", ".Cdata" })
            {
                string p = Path.Combine(dir, stem + ext);
                if (File.Exists(p)) return p;
            }
            try
            {
                return Directory.EnumerateFiles(dir).FirstOrDefault(f =>
                    string.Equals(Path.GetFileNameWithoutExtension(f), stem, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(Path.GetExtension(f), ".cdata", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception) { return null; }
        }

        /// <summary>An .ATMOS and the .CDATA beside it.</summary>
        public static AtmosDoc Load(string atmosPath, List<string> notes)
        {
            var bytes = File.ReadAllBytes(atmosPath);
            if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
                notes?.Add("the .ATMOS is UTF-16; it will be written back as UTF-8 like Sony's files");
            bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            var doc = Parse(SafeXml.ReadText(bytes), notes);
            doc.Bom |= bom;
            string cd = CdataPath(atmosPath);
            if (cd != null)
            {
                var data = File.ReadAllBytes(cd);
                if (data.Length == 0) notes?.Add("the .CDATA beside it is empty; the clouds have no sprites");
                ReadCdata(doc, data, notes);
            }
            else if (ParticleClouds(doc).Count > 0)
                notes?.Add($"{Path.GetFileNameWithoutExtension(atmosPath)}.CDATA isn't beside the .ATMOS, so the particle clouds have no sprites (keep the two files together)");
            return doc;
        }

        public static byte[] AtmosBytes(AtmosDoc doc) => new UTF8Encoding(false).GetBytes(Write(doc));

        // ------------------------------------------------------------------ JSON

        /// <summary>
        /// The document as JSON: root attributes, then every element in order with its attributes (as the strings the file
        /// holds), sprite cloud meshes as vertex / triangle lists and particle clouds with their sprites.
        /// </summary>
        public static JsonObject ToJson(AtmosDoc doc)
        {
            var o = new JsonObject
            {
                ["format"] = "Home ATMOS",
                ["formatVersion"] = 1,
                ["note"] = "Edit values here and convert back to ATMOS. Sprite positions are relative to their cloud's geomBoundSpherePos; " +
                           "\"shading\" is baked lighting from the Home Sky tool. numSprites / vertexDataAddress / numVerts / numTris / CloudVertData size are worked out when writing.",
                ["root"] = ElementJson(doc.Root, false),
            };
            var els = new JsonArray();
            int cloud = 0;
            foreach (var e in doc.Root.Children)
            {
                var j = ElementJson(e, true);
                if (e.Name == "ParticleCloud" && doc.HasCdata && cloud < doc.Clouds.Count)
                {
                    var sp = new JsonArray();
                    foreach (var s in doc.Clouds[cloud])
                        sp.Add(new JsonArray(Num(s.X), Num(s.Y), Num(s.Z), Convert.ToHexString(s.Data).ToLowerInvariant()));
                    j["sprites"] = sp;
                    cloud++;
                }
                els.Add(j);
            }
            o["elements"] = els;
            o["file"] = new JsonObject
            {
                ["bom"] = doc.Bom,
                ["newline"] = doc.NewLine,
                ["prolog"] = doc.Prolog,
                ["hasCdata"] = doc.HasCdata,
                ["cdataTail"] = Convert.ToHexString(doc.CdataTail ?? Array.Empty<byte>()).ToLowerInvariant(),
            };
            return o;
        }

        private static JsonNode Num(double v) => JsonValue.Create(v);

        private static string JStr(JsonNode n, string def) => n is JsonValue v && v.TryGetValue(out string s) ? s : def;
        private static bool JBool(JsonNode n, bool def) => n is JsonValue v && v.TryGetValue(out bool b) ? b : def;

        /// <summary>A number from the JSON, tolerating numbers typed as strings; anything unreadable is 0.</summary>
        private static double JNum(JsonNode n)
        {
            if (n is JsonValue v)
            {
                if (v.TryGetValue(out double d)) return double.IsFinite(d) ? d : 0;
                if (v.TryGetValue(out string s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && double.IsFinite(d)) return d;
            }
            return 0;
        }

        private static bool IsMeshChild(AtmosElement c) =>
            c.SingleLine && c.Children.Count == 0 &&
            ((c.Name == "Vert" && c.Attrs.Count == 2 && c.Attrs[0].Name == "pos" && c.Attrs[1].Name == "uv") ||
             (c.Name == "Tri" && c.Attrs.Count == 1 && c.Attrs[0].Name == "indicies")) &&
            c.Attrs.All(a => a.Sep == "=");

        private static JsonObject ElementJson(AtmosElement e, bool withChildren)
        {
            var j = new JsonObject { ["type"] = e.Name };
            var attrs = new JsonObject();
            var spacing = new JsonObject();
            foreach (var a in e.Attrs)
            {
                attrs[a.Name] = a.Value;
                string def = e.SingleLine ? "=" : " = ";
                if (a.Sep != def) spacing[a.Name] = a.Sep;
            }
            j["attributes"] = attrs;
            if (spacing.Count > 0) j["spacing"] = spacing;
            if (e.SingleLine) j["singleLine"] = true;
            if (!withChildren || e.Children.Count == 0) return j;
            if (e.Name == "TriMesh" && e.Children.All(IsMeshChild))
            {
                var verts = new JsonArray();
                var tris = new JsonArray();
                foreach (var c in e.Children)
                {
                    if (c.Name == "Vert")
                    {
                        var v = c.Vec("pos").Concat(c.Vec("uv")).ToArray();
                        var arr = new JsonArray();
                        foreach (var x in v) arr.Add(JsonValue.Create(x));
                        verts.Add(arr);
                    }
                    else
                    {
                        var arr = new JsonArray();
                        foreach (var x in c.Vec("indicies")) arr.Add(JsonValue.Create((int)x));
                        tris.Add(arr);
                    }
                }
                j["verts"] = verts;
                j["tris"] = tris;
                return j;
            }
            var ch = new JsonArray();
            foreach (var c in e.Children) ch.Add(ElementJson(c, true));
            j["children"] = ch;
            return j;
        }

        public static AtmosDoc FromJson(JsonNode root)
        {
            if (!(root is JsonObject) || !(root["format"] is JsonValue fv && fv.TryGetValue(out string fmt) && fmt == "Home ATMOS")) throw new InvalidDataException("not an ATMOS JSON made by this tool (\"format\": \"Home ATMOS\")");
            var doc = new AtmosDoc();
            var file = root["file"] as JsonObject;
            if (file != null)
            {
                doc.Bom = JBool(file["bom"], false);
                doc.NewLine = JStr(file["newline"], "\r\n");
                doc.Prolog = JStr(file["prolog"], doc.Prolog);
                doc.HasCdata = JBool(file["hasCdata"], false);
                string tail = file["cdataTail"] is JsonValue tv && tv.TryGetValue(out string ts) ? ts : null;
                try { doc.CdataTail = string.IsNullOrEmpty(tail) ? new byte[] { 1 } : Convert.FromHexString(tail); }
                catch (FormatException) { doc.CdataTail = new byte[] { 1 }; }
            }
            if (root["root"] is JsonObject ro) doc.Root = ElementFromJson(ro, false);
            else
            {
                doc.Root = new AtmosElement { Name = "HomeAtmosphere" };
                doc.Root.Attrs.Add(new AtmosAttr { Name = "type", Value = "SceneDefinition" });
                doc.Root.Attrs.Add(new AtmosAttr { Name = "version", Value = "7" });
            }
            foreach (var j in (root["elements"] as JsonArray) ?? new JsonArray())
            {
                if (!(j is JsonObject)) continue;   // a stray value in the list
                var e = ElementFromJson((JsonObject)j, true);
                doc.Root.Children.Add(e);
                if (e.Name == "ParticleCloud" && j.At("sprites") is JsonArray sp)
                {
                    doc.HasCdata = true;
                    var list = new List<CloudSprite>();
                    foreach (var s in sp)
                    {
                        if (!(s is JsonArray a) || a.Count < 3) continue;
                        var cs = new CloudSprite { X = JNum(a[0]), Y = JNum(a[1]), Z = JNum(a[2]) };
                        if (a.Count > 3 && a[3] is JsonValue hv && hv.TryGetValue(out string h) && h.Length == 20)
                        {
                            try { cs.Data = Convert.FromHexString(h); }
                            catch (FormatException) { }   // bad hex: neutral shading
                        }
                        list.Add(cs);
                    }
                    doc.Clouds.Add(list);
                }
                else if (e.Name == "ParticleCloud" && doc.HasCdata) doc.Clouds.Add(new List<CloudSprite>());
            }
            return doc;
        }

        private static AtmosElement ElementFromJson(JsonObject j, bool withChildren)
        {
            if (j == null) throw new InvalidDataException("an element in the JSON is missing");
            var e = new AtmosElement { Name = JStr(j["type"], "Unknown"), SingleLine = JBool(j["singleLine"], false) };
            if (!Regex.IsMatch(e.Name, @"^[A-Za-z_][\w.\-]*$")) throw new InvalidDataException($"\"{e.Name}\" isn't a valid element type");
            var spacing = j["spacing"] as JsonObject;
            foreach (var kv in (j["attributes"] as JsonObject) ?? new JsonObject())
            {
                string sep = JStr(spacing.At(kv.Key), e.SingleLine ? "=" : " = ");
                string val = kv.Value is JsonValue v && v.TryGetValue(out string s) ? s : kv.Value?.ToJsonString() ?? "";
                if (!Regex.IsMatch(kv.Key, @"^[A-Za-z_][\w.\-]*$")) throw new InvalidDataException($"<{e.Name}>: \"{kv.Key}\" isn't a valid attribute name");
                val = (val ?? "").Replace("\"", "'").Replace("<", " ").Replace(">", " ");   // a quote or bracket would break the file
                if (!Regex.IsMatch(sep, @"^\s*=\s*$")) sep = e.SingleLine ? "=" : " = ";
                e.Attrs.Add(new AtmosAttr { Name = kv.Key, Value = val, Sep = sep });
            }
            if (!withChildren) return e;
            if (j["verts"] is JsonArray verts)
            {
                foreach (var v in verts)
                {
                    var a = (v as JsonArray)?.Select(JNum).ToArray() ?? new double[5];
                    double At(int i) => i < a.Length ? a[i] : 0;
                    var vert = new AtmosElement { Name = "Vert", SingleLine = true };
                    vert.Attrs.Add(new AtmosAttr { Name = "pos", Sep = "=", Value = $"{F6(At(0))} {F6(At(1))} {F6(At(2))}" });
                    vert.Attrs.Add(new AtmosAttr { Name = "uv", Sep = "=", Value = $"{F6(At(3))} {F6(At(4))}" });
                    e.Children.Add(vert);
                }
                int tris = 0;
                foreach (var t in (j["tris"] as JsonArray) ?? new JsonArray())
                {
                    var a = (t as JsonArray)?.Select(x => (int)JNum(x)).ToArray() ?? new int[3];
                    var tri = new AtmosElement { Name = "Tri", SingleLine = true };
                    tri.Attrs.Add(new AtmosAttr { Name = "indicies", Sep = "=", Value = string.Join(" ", a.Select(x => x.ToString(Inv))) });
                    e.Children.Add(tri);
                    tris++;
                }
                e.Set("numVerts", verts.Count.ToString(Inv));
                e.Set("numTris", tris.ToString(Inv));
            }
            else if (j["children"] is JsonArray ch)
                foreach (var c in ch) e.Children.Add(ElementFromJson(c as JsonObject, true));
            return e;
        }

        // ------------------------------------------------------------------ describe

        public static string Describe(AtmosDoc doc)
        {
            var sb = new StringBuilder();
            string sky = doc.First("BasicSky") != null ? "basic sky (zenith / horizon colours)" : doc.First("GradientSky") != null ? "gradient sky (" + doc.First("GradientSky").Get("gradientTexture") + ")" : doc.First("TexturedSky") != null ? "textured sky" : "no sky element";
            sb.AppendLine($"     Home atmosphere v{doc.Root.Get("version")}: {sky}");
            var sun = doc.First("BasicSky") ?? doc.First("GradientSky");
            if (sun != null) sb.AppendLine($"     Sun: direction {sun.Get("sunDir")}, colour {sun.Get("sunColour")}, intensity {sun.Get("sunIntensity")}{(sun.Get("sunEnable") == "0" ? " (disc off)" : "")}");
            var ss = doc.First("SkySceneSettings");
            if (ss != null) sb.AppendLine($"     Uses the game's sun direction: {(ss.Get("useGameSunDir") == "1" ? "yes" : "no (fixed)")}");
            var hf = doc.First("ExpHeightFog");
            if (hf != null) sb.AppendLine($"     Height fog: density {hf.Get("fogDensity")}, height {hf.Get("fogHeight")}, colour {hf.Get("fogColour")}");
            var sf = doc.First("SceneFog");
            if (sf != null) sb.AppendLine($"     Scene fog: {sf.Get("fogStart")} .. {sf.Get("fogEnd")} m, alpha {sf.Get("fogAlpha")}, colour {sf.Get("fogColour")}");
            var spr = doc.Elements("SpriteCloud").ToList();
            if (spr.Count > 0) sb.AppendLine($"     {spr.Count} sprite cloud(s) (wispy cloud meshes): " + string.Join(", ", spr.Select(s => Path.GetFileName((s.Get("texture") ?? "").Replace('\\', '/'))).Distinct()));
            var pcs = ParticleClouds(doc);
            if (pcs.Count > 0)
            {
                var dome = doc.First("ParticleCloudDome");
                sb.AppendLine($"     {pcs.Count} particle cloud(s), {pcs.Sum(c => (int)c.Num("numSprites"))} sprites" +
                              (dome != null ? $"; dome height {dome.Get("domeHeight")}, radius {dome.Get("domeInnerRadius")} .. {dome.Get("domeOuterRadius")}, wind {dome.Get("windVelocity")} @ {dome.Get("windDir")} deg" : ""));
                sb.AppendLine(doc.HasCdata ? $"     Cloud sprite data (CDATA): {doc.Clouds.Sum(c => c.Count)} sprites read" : "     Cloud sprite data (CDATA): not found beside the file");
            }
            var known = new HashSet<string> { "SkySceneSettings", "BasicSky", "GradientSky", "TexturedSky", "ExpHeightFog", "SceneFog", "SpriteCloud", "ParticleCloudShadingParams", "ParticleCloudDome", "ParticleCloud", "CloudVertData" };
            var other = doc.Root.Children.Where(e => !known.Contains(e.Name)).Select(e => e.Name).Distinct().ToList();
            if (other.Count > 0) sb.AppendLine("     Other elements (kept as they are): " + string.Join(", ", other));
            return sb.ToString();
        }
    }
}

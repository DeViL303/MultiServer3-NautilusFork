#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Nautilus2027.Formats.Home;

namespace Nautilus2027.Formats
{
    // CDATA tool (ATMOS / CDATA tab): the cloud sprite data that goes with a sky. A .CDATA can't be read without its
    // .ATMOS (the sprite positions are scaled by each cloud's bounding sphere), so the sky is always looked up beside it.
    // Sprites go out as world positions (cloud centre + offset) in JSON, CSV or PLY (point cloud), with the 10 baked
    // shading bytes of each sprite kept; on the way back sprites can be moved, added or deleted, and the CDATA and the
    // ATMOS (sprite counts, addresses, sizes) are rebuilt.
    public static partial class AtmosEfxConverter
    {
        public static readonly string[] CdataExtensions = { ".cdata", ".atmos", ".json", ".csv", ".ply", ".obj" };
        public static readonly string[] CdataTargets = { "CDATA", "JSON", "CSV", "PLY" };

        private sealed class CdataPoint
        {
            public double X, Y, Z;
            public int Cloud = -1;
            public byte[] Shading;
        }

        // ------------------------------------------------------------------ finding the sky

        /// <summary>The .ATMOS that goes with a CDATA / CSV / PLY / OBJ: same name (less ".cdata"), beside it.</summary>
        private static string AtmosBeside(string path)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
            string stem = Path.GetFileNameWithoutExtension(path);
            if (stem.EndsWith(".cdata", StringComparison.OrdinalIgnoreCase)) stem = Path.GetFileNameWithoutExtension(stem);
            try
            {
                var hit = Directory.EnumerateFiles(dir).FirstOrDefault(f =>
                    string.Equals(Path.GetExtension(f), ".atmos", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(Path.GetFileNameWithoutExtension(f), stem, StringComparison.OrdinalIgnoreCase));
                if (hit != null) return hit;
                // a single sky in the folder will do
                var all = Directory.EnumerateFiles(dir, "*.*").Where(f => string.Equals(Path.GetExtension(f), ".atmos", StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
                return all.Count == 1 ? all[0] : null;
            }
            catch (Exception) { return null; }
        }

        private static string NeedAtmos(string path)
        {
            return AtmosBeside(path) ?? throw new InvalidDataException(
                $"no .ATMOS beside {Path.GetFileName(path)}: the cloud sprite data can only be read with its sky (put {StemOf(path)}.ATMOS in the same folder)");
        }

        private static string StemOf(string path)
        {
            string stem = Path.GetFileNameWithoutExtension(path);
            return stem.EndsWith(".cdata", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(stem) : stem;
        }

        // ------------------------------------------------------------------ JSON

        private static string Hex(byte[] b) => b == null ? "" : Convert.ToHexString(b);

        private static byte[] UnHex(string s, int n)
        {
            if (string.IsNullOrEmpty(s) || s.Length != 2 * n) return null;
            try { return Convert.FromHexString(s); }
            catch (FormatException) { return null; }
        }

        private static double[] CloudCentre(AtmosElement c)
        {
            var v = c.Vec("geomBoundSpherePos");
            return v != null && v.Length >= 3 && v.Take(3).All(double.IsFinite) ? new[] { v[0], v[1], v[2] } : new double[3];
        }

        public static JsonObject CdataJson(AtmosDoc doc, string sourceName, string atmosText)
        {
            var clouds = new JsonArray();
            var pcs = HmAtmos.ParticleClouds(doc);
            for (int k = 0; k < pcs.Count; k++)
            {
                var c = pcs[k];
                var centre = CloudCentre(c);
                var sprites = new JsonArray();
                if (k < doc.Clouds.Count)
                    foreach (var s in doc.Clouds[k])
                        sprites.Add(new JsonArray(
                            JsonValue.Create(Math.Round(centre[0] + s.X, 6)), JsonValue.Create(Math.Round(centre[1] + s.Y, 6)), JsonValue.Create(Math.Round(centre[2] + s.Z, 6)),
                            JsonValue.Create(Hex(s.Data))));
                clouds.Add(new JsonObject
                {
                    ["index"] = k,
                    ["centre"] = new JsonArray(centre.Select(x => (JsonNode)JsonValue.Create(Math.Round(x, 6))).ToArray()),
                    ["radius"] = Math.Round(c.Num("geomBoundSphereRadius", 1), 6),
                    ["sprites"] = sprites,
                });
            }
            return new JsonObject
            {
                ["format"] = "Home CDATA",
                ["source"] = sourceName,
                ["note"] = "each sprite is [x, y, z, shading]: a world position and its 10 baked shading bytes as hex. Move, add or delete sprites (a new one without shading copies its nearest neighbour's), then convert back with CDATA picked: the .CDATA and its .ATMOS are both written",
                ["clouds"] = clouds,
                ["tail"] = Hex(doc.CdataTail),
                ["atmos"] = atmosText,
            };
        }

        private static double Num(JsonNode n, double def)
        {
            if (n is JsonValue v)
            {
                if (v.TryGetValue(out double d) && double.IsFinite(d)) return d;
                if (v.TryGetValue(out string s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && double.IsFinite(d)) return d;
            }
            return def;
        }

        private static List<CdataPoint> PointsFromJson(JsonObject j, List<string> notes)
        {
            var pts = new List<CdataPoint>();
            int bad = 0;
            foreach (var cn in (j["clouds"] as JsonArray) ?? new JsonArray())
            {
                if (!(cn is JsonObject cj)) continue;
                int k = JI(cj["index"], -1);
                foreach (var sn in (cj["sprites"] as JsonArray) ?? new JsonArray())
                {
                    if (!(sn is JsonArray a) || a.Count < 3) { bad++; continue; }
                    double x = Num(a[0], double.NaN), y = Num(a[1], double.NaN), z = Num(a[2], double.NaN);
                    if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z)) { bad++; continue; }
                    pts.Add(new CdataPoint { X = x, Y = y, Z = z, Cloud = k, Shading = a.Count > 3 ? UnHex(JS(a[3]), 10) : null });
                }
            }
            if (bad > 0) notes.Add($"{bad} sprite(s) in the JSON weren't [x, y, z, shading] numbers and were left out");
            return pts;
        }

        // ------------------------------------------------------------------ CSV

        private static string CsvText(AtmosDoc doc)
        {
            var sb = new StringBuilder();
            sb.Append("cloud,x,y,z,shading\r\n");
            var pcs = HmAtmos.ParticleClouds(doc);
            for (int k = 0; k < pcs.Count && k < doc.Clouds.Count; k++)
            {
                var centre = CloudCentre(pcs[k]);
                foreach (var s in doc.Clouds[k])
                    sb.Append(k.ToString(Inv)).Append(',').Append(HmAtmos.F6(centre[0] + s.X)).Append(',').Append(HmAtmos.F6(centre[1] + s.Y)).Append(',')
                      .Append(HmAtmos.F6(centre[2] + s.Z)).Append(',').Append(Hex(s.Data)).Append("\r\n");
            }
            return sb.ToString();
        }

        private static List<CdataPoint> PointsFromCsv(string path, List<string> notes)
        {
            var pts = new List<CdataPoint>();
            var lines = SafeXml.ReadText(File.ReadAllBytes(path)).Split('\n');
            int iCloud = 0, iX = 1, iY = 2, iZ = 3, iSh = 4, bad = 0;
            char sep = ',';
            bool header = false;
            foreach (var raw in lines)
            {
                string line = raw.Trim().Trim('﻿');
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (!header)
                {
                    header = true;
                    // Excel in some countries writes ';' (and decimal commas)
                    sep = line.Count(ch => ch == ';') > line.Count(ch => ch == ',') ? ';' : line.Contains('\t') && !line.Contains(',') ? '\t' : ',';
                    var cols = line.Split(sep).Select(c => c.Trim().Trim('"').ToLowerInvariant()).ToList();
                    if (cols.Contains("x"))
                    {
                        iCloud = cols.IndexOf("cloud"); iX = cols.IndexOf("x"); iY = cols.IndexOf("y"); iZ = cols.IndexOf("z"); iSh = cols.IndexOf("shading");
                        if (iY < 0 || iZ < 0) throw new InvalidDataException("the CSV header needs x, y and z columns");
                        continue;
                    }
                }
                var f = line.Split(sep).Select(c => c.Trim().Trim('"')).ToArray();
                double P(int i)
                {
                    if (i < 0 || i >= f.Length) return double.NaN;
                    string t = sep == ';' ? f[i].Replace(',', '.') : f[i];
                    return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && double.IsFinite(d) ? d : double.NaN;
                }
                double x = P(iX), y = P(iY), z = P(iZ), c = P(iCloud);
                if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z)) { bad++; continue; }
                pts.Add(new CdataPoint
                {
                    X = x, Y = y, Z = z,
                    Cloud = double.IsNaN(c) ? -1 : (int)Math.Round(c),
                    Shading = iSh >= 0 && iSh < f.Length ? UnHex(f[iSh], 10) : null,
                });
            }
            if (bad > 0) notes.Add($"{bad} CSV row(s) had no x / y / z numbers and were left out");
            return pts;
        }

        // ------------------------------------------------------------------ PLY / OBJ

        private static string PlyText(AtmosDoc doc, string source)
        {
            var pcs = HmAtmos.ParticleClouds(doc);
            int n = 0;
            for (int k = 0; k < pcs.Count && k < doc.Clouds.Count; k++) n += doc.Clouds[k].Count;
            var sb = new StringBuilder();
            sb.Append("ply\nformat ascii 1.0\n");
            sb.Append("comment Home cloud sprites from ").Append(source.Replace('\n', ' ')).Append(" (Y is up)\n");
            sb.Append($"element vertex {n}\nproperty float x\nproperty float y\nproperty float z\nproperty int cloud\n");
            for (int i = 0; i < 10; i++) sb.Append($"property uchar s{i}\n");
            sb.Append("end_header\n");
            for (int k = 0; k < pcs.Count && k < doc.Clouds.Count; k++)
            {
                var centre = CloudCentre(pcs[k]);
                foreach (var s in doc.Clouds[k])
                {
                    sb.Append(HmAtmos.F6(centre[0] + s.X)).Append(' ').Append(HmAtmos.F6(centre[1] + s.Y)).Append(' ').Append(HmAtmos.F6(centre[2] + s.Z)).Append(' ').Append(k.ToString(Inv));
                    foreach (var b in s.Data ?? new byte[10]) sb.Append(' ').Append(b.ToString(Inv));
                    sb.Append('\n');
                }
            }
            return sb.ToString();
        }

        private static int PlySize(string t) => t switch
        {
            "char" or "uchar" or "int8" or "uint8" => 1,
            "short" or "ushort" or "int16" or "uint16" => 2,
            "int" or "uint" or "float" or "int32" or "uint32" or "float32" => 4,
            "double" or "float64" => 8,
            _ => throw new InvalidDataException($"PLY property type \"{t}\" isn't known"),
        };

        private static double PlyValue(byte[] d, int o, string t, bool big)
        {
            ReadOnlySpan<byte> s = d.AsSpan(o, PlySize(t));
            Span<byte> b = stackalloc byte[8];
            s.CopyTo(b);
            if (big && s.Length > 1) b.Slice(0, s.Length).Reverse();
            return t switch
            {
                "char" or "int8" => (sbyte)b[0],
                "uchar" or "uint8" => b[0],
                "short" or "int16" => BitConverter.ToInt16(b),
                "ushort" or "uint16" => BitConverter.ToUInt16(b),
                "int" or "int32" => BitConverter.ToInt32(b),
                "uint" or "uint32" => BitConverter.ToUInt32(b),
                "float" or "float32" => BitConverter.ToSingle(b),
                _ => BitConverter.ToDouble(b),
            };
        }

        private static List<CdataPoint> PointsFromPly(string path, List<string> notes)
        {
            var d = File.ReadAllBytes(path);
            int headerEnd = -1;
            for (int i = 0; i + 10 <= d.Length && i < 65536; i++)
                if (d[i] == (byte)'e' && Encoding.ASCII.GetString(d, i, 10) == "end_header")
                {
                    int j = i + 10;
                    while (j < d.Length && d[j] != (byte)'\n') j++;
                    headerEnd = Math.Min(j + 1, d.Length);
                    break;
                }
            if (d.Length < 3 || Encoding.ASCII.GetString(d, 0, 3) != "ply" || headerEnd < 0) throw new InvalidDataException("not a PLY file (no \"ply\" ... \"end_header\")");
            var header = Encoding.ASCII.GetString(d, 0, headerEnd).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            string format = header.FirstOrDefault(l => l.StartsWith("format "))?.Split(' ').ElementAtOrDefault(1) ?? "ascii";
            // elements and their properties
            var elements = new List<(string Name, long Count, List<(string Name, string Type, string ListCount)> Props)>();
            foreach (var l in header)
            {
                var w = l.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (w[0] == "element" && w.Length >= 3) elements.Add((w[1], long.TryParse(w[2], out long c) ? c : 0, new List<(string, string, string)>()));
                else if (w[0] == "property" && elements.Count > 0)
                {
                    if (w.Length >= 5 && w[1] == "list") elements[^1].Props.Add((w[4], w[3], w[2]));
                    else if (w.Length >= 3) elements[^1].Props.Add((w[2], w[1], null));
                }
            }
            int vi = elements.FindIndex(e => e.Name == "vertex");
            if (vi < 0) throw new InvalidDataException("the PLY has no vertex element");
            var vert = elements[vi];
            if (vert.Count > 10_000_000) throw new InvalidDataException("the PLY has too many points");
            int ix = vert.Props.FindIndex(p => p.Name == "x"), iy = vert.Props.FindIndex(p => p.Name == "y"), iz = vert.Props.FindIndex(p => p.Name == "z");
            if (ix < 0 || iy < 0 || iz < 0) throw new InvalidDataException("the PLY's points have no x / y / z");
            int ic = vert.Props.FindIndex(p => p.Name == "cloud");
            var ish = Enumerable.Range(0, 10).Select(i => vert.Props.FindIndex(p => p.Name == "s" + i)).ToArray();
            bool hasShading = ish.All(i => i >= 0);
            var pts = new List<CdataPoint>();

            CdataPoint Make(double[] v)
            {
                var p = new CdataPoint { X = v[ix], Y = v[iy], Z = v[iz], Cloud = ic >= 0 ? (int)Math.Round(v[ic]) : -1 };
                if (hasShading) p.Shading = ish.Select(i => (byte)Math.Clamp(Math.Round(v[i]), 0, 255)).ToArray();
                return p;
            }

            if (format == "ascii")
            {
                var lines = Encoding.ASCII.GetString(d, headerEnd, d.Length - headerEnd).Split('\n');
                int at = 0;
                // skip the lines of elements before the vertices (one line per item in ASCII)
                for (int e = 0; e < vi; e++) at += (int)Math.Min(elements[e].Count, int.MaxValue);
                int bad = 0;
                for (long n = 0; n < vert.Count && at < lines.Length; n++, at++)
                {
                    var w = lines[at].Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (w.Length < vert.Props.Count || vert.Props.Any(p => p.ListCount != null)) { bad++; continue; }
                    var v = new double[vert.Props.Count];
                    bool ok = true;
                    for (int k = 0; k < v.Length; k++)
                        if (!double.TryParse(w[k], NumberStyles.Float, CultureInfo.InvariantCulture, out v[k]) || !double.IsFinite(v[k])) ok = false;
                    if (ok) pts.Add(Make(v)); else bad++;
                }
                if (bad > 0) notes.Add($"{bad} PLY point(s) couldn't be read and were left out");
            }
            else if (format == "binary_little_endian" || format == "binary_big_endian")
            {
                bool big = format == "binary_big_endian";
                if (elements.Take(vi).Any(e => e.Props.Any(p => p.ListCount != null)) || vert.Props.Any(p => p.ListCount != null))
                    throw new InvalidDataException("this binary PLY has lists before its points: save it as ASCII PLY, or with the points only");
                long o = headerEnd;
                for (int e = 0; e < vi; e++) o += elements[e].Count * elements[e].Props.Sum(p => PlySize(p.Type));
                int stride = vert.Props.Sum(p => PlySize(p.Type));
                for (long n = 0; n < vert.Count; n++, o += stride)
                {
                    if (o + stride > d.Length) { notes.Add($"the PLY ends after {n} of its {vert.Count} points"); break; }
                    var v = new double[vert.Props.Count];
                    int at = (int)o;
                    for (int k = 0; k < v.Length; k++) { v[k] = PlyValue(d, at, vert.Props[k].Type, big); at += PlySize(vert.Props[k].Type); }
                    if (v.Take(3).All(double.IsFinite)) pts.Add(Make(v));
                }
            }
            else throw new InvalidDataException($"PLY format \"{format}\" isn't known");
            if (!hasShading) notes.Add("the PLY has no shading values (s0..s9): each sprite takes its nearest original sprite's");
            if (ic < 0) notes.Add("the PLY has no cloud numbers: each sprite goes to the nearest cloud");
            return pts;
        }

        private static List<CdataPoint> PointsFromObj(string path, List<string> notes)
        {
            var pts = new List<CdataPoint>();
            int faces = 0;
            foreach (var raw in SafeXml.ReadText(File.ReadAllBytes(path)).Split('\n'))
            {
                var w = raw.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (w.Length == 0) continue;
                if (w[0] == "f") faces++;
                if (w[0] != "v" || w.Length < 4) continue;
                if (double.TryParse(w[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) &&
                    double.TryParse(w[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double y) &&
                    double.TryParse(w[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double z) &&
                    double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(z))
                    pts.Add(new CdataPoint { X = x, Y = y, Z = z });
            }
            if (faces > 0) notes.Add($"the OBJ has {faces} face(s): every vertex is taken as a sprite (export the points alone for an exact count)");
            notes.Add("an OBJ has no cloud numbers or shading: each sprite goes to the nearest cloud and takes its nearest original sprite's shading");
            return pts;
        }

        // ------------------------------------------------------------------ points -> sprites

        /// <summary>Replaces the document's sprites with the points (world positions), keeping shading where it is known.</summary>
        private static void ApplyPoints(AtmosDoc doc, List<CdataPoint> pts, List<string> notes)
        {
            var pcs = HmAtmos.ParticleClouds(doc);
            if (pcs.Count == 0) throw new InvalidDataException("the sky has no particle clouds to put sprites in");
            var centres = pcs.Select(CloudCentre).ToList();
            var radii = pcs.Select(c => { double r = c.Num("geomBoundSphereRadius", 1); return r > 0 && double.IsFinite(r) ? r : 1; }).ToList();
            var originals = new List<List<CloudSprite>>();
            for (int k = 0; k < pcs.Count; k++) originals.Add(k < doc.Clouds.Count ? doc.Clouds[k] : new List<CloudSprite>());
            var fresh = pcs.Select(_ => new List<CloudSprite>()).ToList();
            int guessedCloud = 0, guessedShade = 0, badCloud = 0;
            foreach (var p in pts)
            {
                int k = p.Cloud;
                if (k >= pcs.Count) { badCloud++; k = -1; }
                if (k < 0)
                {
                    // the cloud whose sphere it is most inside
                    double best = double.MaxValue;
                    for (int c = 0; c < pcs.Count; c++)
                    {
                        double dx = p.X - centres[c][0], dy = p.Y - centres[c][1], dz = p.Z - centres[c][2];
                        double rel = Math.Sqrt(dx * dx + dy * dy + dz * dz) / radii[c];
                        if (rel < best) { best = rel; k = c; }
                    }
                    if (p.Cloud < 0) guessedCloud++;
                }
                var s = new CloudSprite { X = p.X - centres[k][0], Y = p.Y - centres[k][1], Z = p.Z - centres[k][2] };
                if (p.Shading != null && p.Shading.Length == 10) s.Data = p.Shading;
                else
                {
                    guessedShade++;
                    var pool = originals[k].Count > 0 ? originals[k] : originals.SelectMany(x => x).ToList();
                    CloudSprite near = null;
                    double bd = double.MaxValue;
                    foreach (var o in pool)
                    {
                        double dx = o.X - s.X, dy = o.Y - s.Y, dz = o.Z - s.Z, dd = dx * dx + dy * dy + dz * dz;
                        if (pool != originals[k]) { var oc = centres[originals.FindIndex(l => l.Contains(o))]; dx = oc[0] + o.X - p.X; dy = oc[1] + o.Y - p.Y; dz = oc[2] + o.Z - p.Z; dd = dx * dx + dy * dy + dz * dz; }
                        if (dd < bd) { bd = dd; near = o; }
                    }
                    s.Data = near != null ? (byte[])near.Data.Clone() : new byte[10];
                }
                fresh[k].Add(s);
            }
            doc.Clouds = fresh;
            doc.HasCdata = true;
            int before = originals.Sum(l => l.Count), after = fresh.Sum(l => l.Count);
            if (before != after) notes.Add($"{before} sprite(s) before, {after} now");
            for (int k = 0; k < pcs.Count; k++)
                if (fresh[k].Count != originals[k].Count) notes.Add($"cloud {k}: {originals[k].Count} -> {fresh[k].Count} sprite(s)");
            if (badCloud > 0) notes.Add($"{badCloud} sprite(s) named a cloud the sky doesn't have: put in the nearest cloud");
            if (guessedCloud > 0 && guessedCloud < pts.Count) notes.Add($"{guessedCloud} sprite(s) had no cloud number: put in the nearest cloud");
            if (guessedShade > 0 && guessedShade < pts.Count) notes.Add($"{guessedShade} sprite(s) had no shading: copied from their nearest neighbour");
            var empty = Enumerable.Range(0, pcs.Count).Where(k => fresh[k].Count == 0 && originals[k].Count > 0).ToList();
            if (empty.Count > 0) notes.Add($"cloud(s) {string.Join(", ", empty)} now have no sprites (they will not show)");
        }

        // ------------------------------------------------------------------ loading

        /// <summary>The sky (with its sprites, edited from the input when it's a JSON / CSV / PLY / OBJ) and its original .ATMOS bytes.</summary>
        private static (AtmosDoc Doc, byte[] AtmosOriginal, string AtmosName) LoadCdataDoc(string input, List<string> notes)
        {
            switch (Ext(input))
            {
                case ".atmos":
                {
                    var doc = HmAtmos.Load(input, notes);
                    if (!doc.HasCdata) throw new InvalidDataException($"{Path.GetFileNameWithoutExtension(input)}.CDATA isn't beside the .ATMOS");
                    return (doc, File.ReadAllBytes(input), Path.GetFileName(input));
                }
                case ".cdata":
                {
                    string atmos = NeedAtmos(input);
                    var bytes = File.ReadAllBytes(atmos);
                    var doc = HmAtmos.Parse(SafeXml.ReadText(bytes), notes);
                    doc.Bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                    HmAtmos.ReadCdata(doc, File.ReadAllBytes(input), notes);
                    if (!string.Equals(StemOf(atmos), StemOf(input), StringComparison.OrdinalIgnoreCase)) notes.Add($"read with {Path.GetFileName(atmos)} (the only sky in the folder)");
                    return (doc, bytes, Path.GetFileName(atmos));
                }
                case ".json":
                {
                    var j = ReadJson(input) as JsonObject;
                    if (j == null || JS(j["format"]) != "Home CDATA") throw new InvalidDataException("not a CDATA JSON made by this tool (\"format\": \"Home CDATA\")");
                    string text = JS(j["atmos"]);
                    if (string.IsNullOrEmpty(text)) throw new InvalidDataException("the JSON has no \"atmos\" text (the sky the sprites belong to)");
                    var doc = HmAtmos.Parse(text, notes);
                    string tail = JS(j["tail"]);
                    doc.CdataTail = tail == "" ? Array.Empty<byte>() : UnHex(tail, (tail ?? "").Length / 2) ?? new byte[] { 1 };
                    doc.Clouds = HmAtmos.ParticleClouds(doc).Select(_ => new List<CloudSprite>()).ToList();
                    var pts = PointsFromJson(j, notes);
                    ApplyPoints(doc, pts, notes);
                    string src = JS(j["source"]) ?? "SKY.ATMOS";
                    return (doc, new UTF8Encoding(false).GetBytes(text), Path.ChangeExtension(Path.GetFileName(src), ".ATMOS"));
                }
                default:
                {
                    string atmos = NeedAtmos(input);
                    var doc = HmAtmos.Load(atmos, notes);
                    var pts = Ext(input) switch
                    {
                        ".csv" => PointsFromCsv(input, notes),
                        ".ply" => PointsFromPly(input, notes),
                        _ => PointsFromObj(input, notes),
                    };
                    if (pts.Count == 0) throw new InvalidDataException($"no points could be read from {Path.GetFileName(input)}");
                    ApplyPoints(doc, pts, notes);
                    return (doc, File.ReadAllBytes(atmos), Path.GetFileName(atmos));
                }
            }
        }

        // ------------------------------------------------------------------ convert / inspect / preview

        public static ModelConverter.Result ConvertCdata(string input, string target, string outputDir, OverwriteBehavior overwrite)
        {
            target = target.ToUpperInvariant();
            string ext = Ext(input);
            if (("." + target.ToLowerInvariant()) == ext) throw new NotSupportedException("The file is already " + target + ".");
            var notes = new List<string>();
            var (doc, atmosBytes, atmosName) = LoadCdataDoc(input, notes);
            string name = StemOf(atmosName).ToUpperInvariant();
            Directory.CreateDirectory(outputDir);
            var r = new ModelConverter.Result();
            int sprites = doc.Clouds.Sum(c => c.Count), clouds = HmAtmos.ParticleClouds(doc).Count;

            // a copy of the sky beside a CSV / PLY, so the file can be converted back
            void SkyBeside(string outName)
            {
                string sky = Path.Combine(outputDir, outName + ".ATMOS");
                File.WriteAllBytes(sky, atmosBytes);
                r.Details.Add($"{outName}.ATMOS is beside it: keep the two together to convert back with CDATA picked");
            }

            switch (target)
            {
                case "CDATA":
                {
                    string outName = ModelConverter.ReserveName(name, n => Path.Combine(outputDir, n + ".CDATA"), overwrite, out bool renamed);
                    if (outName == null) return null;
                    byte[] cdata = HmAtmos.WriteCdata(doc);   // updates the sky's counts / addresses / size
                    byte[] sky = HmAtmos.AtmosBytes(doc);
                    var back = HmAtmos.Parse(new UTF8Encoding(false).GetString(sky));
                    HmAtmos.ReadCdata(back, cdata, null);
                    if (back.Clouds.Sum(c => c.Count) != sprites) throw new InvalidDataException("read-back check failed (sprite count changed)");
                    string p = Path.Combine(outputDir, outName + ".CDATA");
                    File.WriteAllBytes(p, cdata);
                    File.WriteAllBytes(Path.Combine(outputDir, outName + ".ATMOS"), sky);
                    r.OutputPath = p; r.Renamed = renamed;
                    r.Summary = $"{sprites} sprite(s) in {clouds} cloud(s), {cdata.Length:N0} bytes";
                    r.Details.Add(sky.AsSpan().SequenceEqual(atmosBytes)
                        ? $"{outName}.ATMOS written beside it (unchanged)"
                        : $"{outName}.ATMOS written beside it with the new sprite counts / addresses: use the two together");
                    break;
                }
                case "JSON":
                {
                    string outName = ModelConverter.ReserveName(name, n => Path.Combine(outputDir, n + ".cdata.json"), overwrite, out bool renamed);
                    if (outName == null) return null;
                    string p = Path.Combine(outputDir, outName + ".cdata.json");
                    File.WriteAllText(p, JsonText(CdataJson(doc, atmosName, SafeXml.ReadText(atmosBytes))), new UTF8Encoding(false));
                    r.OutputPath = p; r.Renamed = renamed;
                    r.Summary = $"{sprites} sprite(s) in {clouds} cloud(s)";
                    break;
                }
                case "CSV":
                {
                    string outName = ModelConverter.ReserveName(name, n => Path.Combine(outputDir, n + ".csv"), overwrite, out bool renamed);
                    if (outName == null) return null;
                    string p = Path.Combine(outputDir, outName + ".csv");
                    File.WriteAllText(p, CsvText(doc), new UTF8Encoding(false));
                    SkyBeside(outName);
                    r.OutputPath = p; r.Renamed = renamed;
                    r.Summary = $"{sprites} sprite(s) in {clouds} cloud(s)";
                    r.Details.Add("one row per sprite: cloud, world x / y / z, shading bytes (hex); add or delete rows to add or remove sprites");
                    break;
                }
                case "PLY":
                {
                    string outName = ModelConverter.ReserveName(name, n => Path.Combine(outputDir, n + ".ply"), overwrite, out bool renamed);
                    if (outName == null) return null;
                    string p = Path.Combine(outputDir, outName + ".ply");
                    File.WriteAllText(p, PlyText(doc, atmosName), new UTF8Encoding(false));
                    SkyBeside(outName);
                    r.OutputPath = p; r.Renamed = renamed;
                    r.Summary = $"{sprites} point(s) in {clouds} cloud(s)";
                    r.Details.Add("a point cloud (Y up) for Blender / MeshLab / CloudCompare; a PLY without the cloud and s0..s9 values still converts back (nearest cloud / shading)");
                    break;
                }
                default:
                    throw new NotSupportedException(target + " isn't a CDATA output");
            }
            r.Details.AddRange(notes.Distinct());
            return r;
        }

        public static string DescribeCdata(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"-- {Path.GetFileName(path)} ({new FileInfo(path).Length:N0} bytes)");
            var notes = new List<string>();
            if (Ext(path) == ".cdata" && AtmosBeside(path) == null)
            {
                long len = new FileInfo(path).Length;
                sb.AppendLine($"     Cloud sprite data, about {len / 16:N0} sprite record(s) of 16 bytes; no .ATMOS beside it, so the clouds can't be told apart or placed");
                return sb.ToString();
            }
            var (doc, _, atmosName) = LoadCdataDoc(path, notes);
            var pcs = HmAtmos.ParticleClouds(doc);
            int total = doc.Clouds.Sum(c => c.Count);
            sb.AppendLine($"     Cloud sprite data for {atmosName}: {pcs.Count} particle cloud(s), {total:N0} sprite(s)");
            string size = doc.First("CloudVertData")?.Get("size");
            if (Ext(path) == ".cdata" || Ext(path) == ".atmos")
            {
                long real = Ext(path) == ".cdata" ? new FileInfo(path).Length : (HmAtmos.CdataPath(path) is string cp ? new FileInfo(cp).Length : -1);
                if (size != null && real >= 0 && size != real.ToString(Inv)) sb.AppendLine($"     Warning: the sky says the CDATA is {size} bytes, the file is {real:N0}");
            }
            // overlapping blocks
            var spans = pcs.Select((c, k) => (k, a: c.Num("vertexDataAddress", -1), n: k < doc.Clouds.Count ? doc.Clouds[k].Count : 0)).Where(x => x.a >= 0).OrderBy(x => x.a).ToList();
            for (int i = 1; i < spans.Count; i++)
                if (spans[i].a < spans[i - 1].a + 16 * spans[i - 1].n) sb.AppendLine($"     Warning: clouds {spans[i - 1].k} and {spans[i].k} share sprite data");
            for (int k = 0; k < pcs.Count; k++)
            {
                var c = pcs[k];
                var list = k < doc.Clouds.Count ? doc.Clouds[k] : new List<CloudSprite>();
                double R = c.Num("geomBoundSphereRadius", 1);
                var centre = CloudCentre(c);
                int edge = list.Count(s => Math.Max(Math.Abs(s.X), Math.Max(Math.Abs(s.Y), Math.Abs(s.Z))) >= R * 0.9999);
                sb.AppendLine($"       cloud {k}: {list.Count} sprite(s) around ({string.Join(" ", centre.Select(x => x.ToString("0.#", Inv)))}), radius {R.ToString("0.##", Inv)}" +
                              (list.Count > 0 ? $", spread {list.Max(s => Math.Sqrt(s.X * s.X + s.Y * s.Y + s.Z * s.Z)).ToString("0.#", Inv)}" : "") +
                              (edge > 0 ? $", {edge} at the edge of the radius" : ""));
            }
            foreach (var n in notes) sb.AppendLine("     Note: " + n);
            return sb.ToString();
        }

        public static PreviewData PreviewCdata(string path)
        {
            var notes = new List<string>();
            var (doc, _, atmosName) = LoadCdataDoc(path, notes);
            string anchor = Ext(path) == ".atmos" ? path : (AtmosBeside(path) ?? path);
            var scene = AtmosScene(doc, anchor, StemOf(atmosName), notes);
            scene.Meshes.RemoveAll(m => !(m.Name ?? "").StartsWith("ParticleCloud_"));
            if (scene.Meshes.Count == 0) throw new InvalidDataException("the sky has no particle clouds");
            var d = ModelPreview.FromScene(scene, Path.GetFileName(path));
            d.Info.Add($"{doc.Clouds.Sum(c => c.Count):N0} sprite(s) in {HmAtmos.ParticleClouds(doc).Count} cloud(s) of {atmosName}");
            d.Info.AddRange(notes.Take(6));
            return d;
        }
    }
}

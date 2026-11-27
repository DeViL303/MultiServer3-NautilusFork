#nullable disable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nautilus2027.Formats.Home;

namespace Nautilus2027.Formats
{
    /// <summary>
    /// ATMOS / CDATA and EFX / PROBE tabs.
    /// ATMOS (+ CDATA) to JSON (every setting, exact back) and to GLB / FBX / OBJ (sky dome, sun, wispy cloud meshes, every
    /// cloud sprite as a textured card), and back: from JSON, or from a GLB / FBX / OBJ with its .atmos.json (or the copy
    /// inside the GLB), taking moved / reshaped clouds and a moved sun from the model.
    /// EFX to JSON (every node setting, texture, count; exact back) and to GLB (the emitter shapes placed as the effect places
    /// them, with the group's texture), and back, taking moved / turned / scaled emitters from the GLB.
    /// </summary>
    public static partial class AtmosEfxConverter
    {
        public static readonly string[] AtmosExtensions = { ".atmos", ".scene", ".json", ".glb", ".gltf", ".fbx", ".obj" };
        public static readonly string[] ProbeExtensions = { ".probe", ".scene", ".json", ".glb", ".gltf", ".fbx", ".obj" };
        public static readonly string[] ProbeTargets = { "PROBE", "JSON", "GLB", "FBX" };
        public static readonly string[] EfxExtensions = { ".efx", ".scene", ".json", ".glb", ".gltf" };
        public static readonly string[] AtmosTargets = { "ATMOS", "JSON", "GLB", "FBX" };
        public static readonly string[] EfxTargets = { "EFX", "JSON", "GLB" };

        private const string AtmosKey = "nautilus_atmos", EfxKey = "nautilus_efx", ProbeKey = "nautilus_probe", SceneKey = "nautilus_scenefx";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static string Ext(string p) => Path.GetExtension(p).ToLowerInvariant();

        // ------------------------------------------------------------------ JSON text

        /// <summary>Indented JSON with short number / string lists kept on one line (sprites, vertices, vectors).</summary>
        public static string JsonText(JsonNode node)
        {
            var sb = new StringBuilder();
            void W(JsonNode n, int ind)
            {
                string pad = new string(' ', ind * 2), pad2 = new string(' ', ind * 2 + 2);
                switch (n)
                {
                    case null: sb.Append("null"); break;
                    case JsonObject o:
                        if (o.Count == 0) { sb.Append("{}"); break; }
                        sb.Append("{\n");
                        int i = 0;
                        foreach (var kv in o)
                        {
                            sb.Append(pad2).Append(JsonSerializer.Serialize(kv.Key)).Append(": ");
                            W(kv.Value, ind + 1);
                            sb.Append(++i < o.Count ? ",\n" : "\n");
                        }
                        sb.Append(pad).Append('}');
                        break;
                    case JsonArray a:
                        if (a.Count == 0) { sb.Append("[]"); break; }
                        if (a.All(x => x == null || x is JsonValue))
                        {
                            sb.Append('[').Append(string.Join(", ", a.Select(x => x == null ? "null" : x.ToJsonString()))).Append(']');
                            break;
                        }
                        sb.Append("[\n");
                        for (int k = 0; k < a.Count; k++)
                        {
                            sb.Append(pad2);
                            W(a[k], ind + 1);
                            sb.Append(k + 1 < a.Count ? ",\n" : "\n");
                        }
                        sb.Append(pad).Append(']');
                        break;
                    default: sb.Append(n.ToJsonString()); break;
                }
            }
            W(node, 0);
            sb.Append('\n');
            return sb.ToString();
        }

        private static JsonNode ReadJson(string path)
        {
            try { return JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }); }
            catch (JsonException ex) { throw new InvalidDataException($"{Path.GetFileName(path)} isn't valid JSON: {ex.Message}"); }
        }

        private static string JS(JsonNode n) => n is JsonValue v && v.TryGetValue(out string t) ? t : null;

        private static int JI(JsonNode n, int def)
        {
            if (n is JsonValue v)
            {
                if (v.TryGetValue(out double d) && double.IsFinite(d) && Math.Abs(d) < int.MaxValue) return (int)d;
                if (v.TryGetValue(out string t) && int.TryParse(t, out int i)) return i;
            }
            return def;
        }

        /// <summary>"Home ATMOS" / "Home EFX" for a JSON made by this tab, else null.</summary>
        public static string JsonFormat(string path)
        {
            try { return JS(ReadJson(path).At("format")); }
            catch (Exception) { return null; }
        }

        /// <summary>The document saved in a GLB's scene extras, or in NAME.atmos.json / NAME.efx.json beside the model.</summary>
        private static JsonNode StoredDoc(string modelPath, string key, string sidecarExt, List<string> notes)
        {
            if (Ext(modelPath) == ".glb" || Ext(modelPath) == ".gltf")
            {
                try
                {
                    var bytes = File.ReadAllBytes(modelPath);
                    JsonNode g;
                    if (bytes.Length > 20 && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4)) == 0x46546C67)
                    {
                        int len = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12, 4));
                        g = JsonNode.Parse(Encoding.UTF8.GetString(bytes, 20, len));
                    }
                    else g = JsonNode.Parse(Encoding.UTF8.GetString(bytes).TrimStart('﻿'));
                    foreach (var sc in (g.At("scenes") as JsonArray) ?? new JsonArray())
                        if (sc.At("extras").At(key) is JsonNode s && JS(s) is string text && text.Length > 0)
                            return JsonNode.Parse(text);
                }
                catch (Exception) { }
            }
            string side = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(modelPath)) ?? "", Path.GetFileNameWithoutExtension(modelPath) + sidecarExt);
            if (File.Exists(side))
            {
                notes?.Add("settings taken from " + Path.GetFileName(side));
                return ReadJson(side);
            }
            return null;
        }

        // ------------------------------------------------------------------ textures / meshes

        private static int Texture(ExportScene scene, Dictionary<string, int> cache, string anchor, string homePath, List<string> notes, int maxSize = 1024)
        {
            if (string.IsNullOrWhiteSpace(homePath)) return -1;
            if (cache.TryGetValue(homePath, out int have)) return have;
            int idx = -1;
            string file = ModelConverter.FindHomeTexture(anchor, homePath);
            if (file == null) notes.Add("texture not found: " + homePath);
            else
            {
                try
                {
                    var img = ImageCodec.Decode(File.ReadAllBytes(file), file);
                    if (img != null)
                    {
                        if (img.Width > maxSize || img.Height > maxSize) img = Dds.Prepare(img, false, maxSize);
                        idx = AddImage(scene, Path.GetFileNameWithoutExtension(file), img);
                    }
                }
                catch (Exception ex) { notes.Add($"texture {homePath} could not be read: {ex.Message}"); }
            }
            cache[homePath] = idx;
            return idx;
        }

        private static int AddImage(ExportScene scene, string name, RgbaImage img)
        {
            string n = name;
            for (int k = 2; scene.Textures.Any(t => string.Equals(t.Name, n, StringComparison.OrdinalIgnoreCase)); k++) n = name + "_" + k;
            scene.Textures.Add(new ExportTexture { Name = n, Ext = "png", Mime = "image/png", Bytes = ImageCodec.EncodePng(img), RgbaCrc = HmCore.Crc32(img.Data) });
            return scene.Textures.Count - 1;
        }

        private static int Material(ExportScene scene, string name, int tex, string kind)
        {
            var m = scene.Materials.FindIndex(x => x.Name == name);
            if (m >= 0) return m;
            scene.Materials.Add(new ExportMaterial { Name = name, Kind = kind, Colour = tex >= 0 ? tex : (int?)null, Emissive = kind == "emissive" && tex >= 0 ? tex : (int?)null });
            return scene.Materials.Count - 1;
        }

        private sealed class MeshBuilder
        {
            public List<float> P = new List<float>(), N = new List<float>(), UV = new List<float>();
            public List<int> T = new List<int>();
            public int Count => P.Count / 3;
            public int Add(double[] p, double[] n, double u, double v)
            {
                P.Add((float)p[0]); P.Add((float)p[1]); P.Add((float)p[2]);
                N.Add((float)n[0]); N.Add((float)n[1]); N.Add((float)n[2]);
                UV.Add((float)u); UV.Add((float)v);
                return Count - 1;
            }
            public void Tri(int a, int b, int c) { T.Add(a); T.Add(b); T.Add(c); }
            public ExportMesh Mesh(string name, int material) =>
                new ExportMesh { Name = name, Material = material, P = P.ToArray(), N = N.ToArray(), UV = UV.ToArray(), Tris = T.ToArray() };
        }

        private static double[] Sub(double[] a, double[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
        private static double[] Add(double[] a, double[] b) => new[] { a[0] + b[0], a[1] + b[1], a[2] + b[2] };
        private static double[] Mul(double[] a, double s) => new[] { a[0] * s, a[1] * s, a[2] * s };
        private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        private static double[] Cross(double[] a, double[] b) => new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
        private static double Len(double[] a) => Math.Sqrt(Dot(a, a));
        private static double[] Norm(double[] a) { double l = Len(a); return l > 1e-12 ? Mul(a, 1 / l) : new double[] { 0, 0, 1 }; }
        private static double[] V3(double[] a) => a != null && a.Length >= 3 ? new[] { a[0], a[1], a[2] } : new double[] { 0, 0, 0 };

        /// <summary>A card facing the point <paramref name="eye"/>, as 4 vertices in order (for reading back its centre).</summary>
        private static void Card(MeshBuilder mb, double[] c, double size, double[] eye)
        {
            var n = Norm(Sub(eye, c));
            var up = Math.Abs(n[1]) > 0.95 ? new double[] { 0, 0, 1 } : new double[] { 0, 1, 0 };
            var r = Norm(Cross(up, n));
            var u = Cross(n, r);
            double h = size / 2;
            int a = mb.Add(Add(c, Add(Mul(r, -h), Mul(u, h))), n, 0, 0);
            int b = mb.Add(Add(c, Add(Mul(r, h), Mul(u, h))), n, 1, 0);
            int d = mb.Add(Add(c, Add(Mul(r, h), Mul(u, -h))), n, 1, 1);
            int e = mb.Add(Add(c, Add(Mul(r, -h), Mul(u, -h))), n, 0, 1);
            mb.Tri(a, d, b); mb.Tri(a, e, d);
        }

        // ------------------------------------------------------------------ ATMOS -> scene

        private static double[] Col(AtmosElement e, string name, double[] def) { var v = e?.Vec(name); return v != null && v.Length >= 3 ? v : def; }

        private static byte B(double x) => (byte)Math.Clamp((int)Math.Round(Math.Pow(Math.Clamp(x, 0, 1), 1 / 2.2) * 255), 0, 255);

        /// <summary>The sky as a model: dome, sun, sprite clouds, particle cloud sprites (each cloud one mesh, a card per sprite).</summary>
        public static ExportScene AtmosScene(AtmosDoc doc, string anchor, string name, List<string> notes)
        {
            var scene = new ExportScene { Name = name };
            var cache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var dome = doc.First("ParticleCloudDome");
            double Rs = Math.Max(3000, (dome?.Num("domeOuterRadius", 0) ?? 0) * 1.4);
            var basic = doc.First("BasicSky");
            var grad = doc.First("GradientSky");
            var sunEl = basic ?? grad ?? doc.First("TexturedSky");

            // dome: v runs from the zenith (0) to 10 degrees below the horizon (1)
            int skyTex = -1;
            double gradU = 0.5;
            if (grad != null)
            {
                skyTex = Texture(scene, cache, anchor, grad.Get("gradientTexture"), notes);
                gradU = grad.Num("xcoord", 0.5);
            }
            if (skyTex < 0)
            {
                var zen = Col(basic, "zenithColour", new[] { 0.36, 0.53, 0.78 });
                var hor = Col(basic, "horizonColour", new[] { 0.9, 0.95, 0.97 });
                double falloff = Math.Max(0.1, basic?.Num("horizonFalloff", 3) ?? 3), off = basic?.Num("horizonOffset", 0) ?? 0;
                var img = new RgbaImage(4, 64);
                for (int y = 0; y < 64; y++)
                {
                    double elev = (90 - y / 63.0 * 100) * Math.PI / 180;
                    double t = Math.Pow(Math.Clamp(Math.Sin(elev) + off, 0, 1), 1 / falloff);
                    for (int x = 0; x < 4; x++)
                    {
                        int o = (y * 4 + x) * 4;
                        for (int c = 0; c < 3; c++) img.Data[o + c] = B(hor[c] + (zen[c] - hor[c]) * t);
                        img.Data[o + 3] = 255;
                    }
                }
                skyTex = AddImage(scene, "sky_gradient", img);
            }
            var sky = new MeshBuilder();
            const int lon = 48, lat = 14;
            for (int i = 0; i <= lat; i++)
            {
                double elevDeg = 90 - i * 100.0 / lat, el = elevDeg * Math.PI / 180;
                for (int j = 0; j <= lon; j++)
                {
                    double az = j * 2 * Math.PI / lon;
                    var dir = new[] { Math.Cos(el) * Math.Sin(az), Math.Sin(el), Math.Cos(el) * Math.Cos(az) };
                    sky.Add(Mul(dir, Rs), Mul(dir, -1), grad != null ? gradU : j / (double)lon, i / (double)lat);
                }
            }
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    int a = i * (lon + 1) + j, b = a + 1, c = a + lon + 1, d = c + 1;
                    sky.Tri(a, b, c); sky.Tri(b, d, c);   // facing inwards
                }
            scene.Meshes.Add(sky.Mesh("SkyDome", Material(scene, "sky", skyTex, "emissive")));

            // sun disc
            if (sunEl != null)
            {
                var sd = Norm(V3(sunEl.Vec("sunDir")));
                var sc = Col(sunEl, "sunColour", new double[] { 1, 1, 1 });
                var simg = new RgbaImage(2, 2);
                for (int k = 0; k < 4; k++) { for (int c = 0; c < 3; c++) simg.Data[4 * k + c] = B(sc[c]); simg.Data[4 * k + 3] = 255; }
                int st = AddImage(scene, "sun", simg);
                var sun = new MeshBuilder();
                double size = Rs * Math.Clamp(sunEl.Num("sunDiskSize", 0.12), 0.02, 0.5) * 0.25;
                Card(sun, Mul(sd, Rs * 0.95), size, new double[] { 0, 0, 0 });
                scene.Meshes.Add(sun.Mesh("Sun", Material(scene, "sun", st, "emissive")));
            }

            // sprite clouds (high wispy cloud meshes)
            int si = 0;
            foreach (var spc in doc.Elements("SpriteCloud"))
            {
                var tm = spc.Children.FirstOrDefault(c => c.Name == "TriMesh");
                if (tm == null) { si++; continue; }
                int tex = Texture(scene, cache, anchor, spc.Get("texture"), notes);
                var mb = new MeshBuilder();
                var verts = tm.Children.Where(c => c.Name == "Vert").ToList();
                foreach (var v in verts)
                {
                    var p = V3(v.Vec("pos"));
                    var uv = v.Vec("uv") ?? new double[] { 0, 0 };
                    mb.Add(p, Norm(Mul(p, -1)), uv.Length > 0 ? uv[0] : 0, uv.Length > 1 ? uv[1] : 0);
                }
                foreach (var t in tm.Children.Where(c => c.Name == "Tri"))
                {
                    var ix = t.Vec("indicies");
                    if (ix != null && ix.Length >= 3 && ix.All(x => x >= 0 && x < verts.Count)) mb.Tri((int)ix[0], (int)ix[1], (int)ix[2]);
                }
                if (mb.T.Count > 0) scene.Meshes.Add(mb.Mesh($"SpriteCloud_{si:00}", Material(scene, "sprite_cloud_" + Path.GetFileNameWithoutExtension((spc.Get("texture") ?? "").Replace('\\', '/')), tex, "glass")));
                si++;
            }

            // particle clouds: a card per sprite, facing the middle of the sky
            var shading = doc.First("ParticleCloudShadingParams");
            int ptex = Texture(scene, cache, anchor, shading?.Get("spriteTexture"), notes);
            int pmat = Material(scene, "cloud_particle", ptex, "glass");
            var clouds = HmAtmos.ParticleClouds(doc);
            for (int k = 0; k < clouds.Count; k++)
            {
                var c = clouds[k];
                var centre = V3(c.Vec("geomBoundSpherePos"));
                double size = c.Num("spriteSize", 100);
                var mb = new MeshBuilder();
                if (doc.HasCdata && k < doc.Clouds.Count)
                    foreach (var s in doc.Clouds[k]) Card(mb, Add(centre, new[] { s.X, s.Y, s.Z }), size, new double[] { 0, 0, 0 });
                else Card(mb, centre, size, new double[] { 0, 0, 0 });
                scene.Meshes.Add(mb.Mesh($"ParticleCloud_{k:00}", pmat));
            }
            if (clouds.Count > 0) notes.Add($"{clouds.Count} particle cloud(s) as card meshes (one card per sprite) - move clouds or single cards in Blender; delete cards to thin a cloud");
            return scene;
        }

        // ------------------------------------------------------------------ model -> ATMOS (edits)

        private static double[] Centre(List<V3> P, int k) =>
            new[] { (P[4 * k].X + P[4 * k + 1].X + P[4 * k + 2].X + P[4 * k + 3].X) / 4, (P[4 * k].Y + P[4 * k + 1].Y + P[4 * k + 2].Y + P[4 * k + 3].Y) / 4, (P[4 * k].Z + P[4 * k + 1].Z + P[4 * k + 2].Z + P[4 * k + 3].Z) / 4 };

        private static string F6V(double[] v) => string.Join(" ", v.Select(HmAtmos.F6));

        /// <summary>Clouds / sun / sprite cloud meshes from a GLB / FBX / OBJ made by this tab, onto the stored document.</summary>
        private static void ApplyModelEdits(AtmosDoc doc, string modelPath, List<string> notes)
        {
            var src = ModelConverter.LoadSource(modelPath);
            var parts = src.Collect("full");
            List<SourcePart> Named(string n) => parts.Where(p => string.Equals(HmRoundTrip.BaseName(p.Name ?? ""), n, StringComparison.OrdinalIgnoreCase)).ToList();
            bool flipV = src.DefaultFlipV;
            int moved = 0, reshaped = 0;

            // particle clouds
            var clouds = HmAtmos.ParticleClouds(doc);
            for (int k = 0; k < clouds.Count && doc.HasCdata && k < doc.Clouds.Count; k++)
            {
                var ps = Named($"ParticleCloud_{k:00}");
                if (ps.Count == 0) { notes.Add($"ParticleCloud_{k:00} isn't in the model; that cloud was left as it was"); continue; }
                var P = ps.SelectMany(p => p.P).ToList();
                int n = P.Count / 4;
                var c = clouds[k];
                var pos = V3(c.Vec("geomBoundSpherePos"));
                var old = doc.Clouds[k];
                if (n == 0) { notes.Add($"ParticleCloud_{k:00} has no cards left; the cloud keeps one sprite"); n = 0; }
                var centres = Enumerable.Range(0, n).Select(i => Centre(P, i)).ToList();
                double[] Mean(IEnumerable<double[]> xs) { var l = xs.ToList(); return l.Count == 0 ? new double[3] : new[] { l.Average(x => x[0]), l.Average(x => x[1]), l.Average(x => x[2]) }; }
                var oldMean = Add(pos, Mean(old.Select(s => new[] { s.X, s.Y, s.Z })));
                var shift = n == old.Count ? Mean(centres.Zip(old, (a, s) => Sub(a, Add(pos, new[] { s.X, s.Y, s.Z })))) : Sub(Mean(centres), oldMean);
                double tol = Math.Max(0.01, c.Num("geomBoundSphereRadius", 1) / 32767.0);
                if (Len(shift) > tol)
                {
                    pos = Add(pos, shift);
                    c.Set("geomBoundSpherePos", F6V(pos));
                    moved++;
                }
                if (n == 0) continue;
                var list = new List<CloudSprite>();
                for (int i = 0; i < n; i++)
                {
                    var l = Sub(centres[i], pos);
                    var keep = old.Count > 0 ? old[i < old.Count ? i : i % old.Count] : null;
                    var s = new CloudSprite { X = Math.Round(l[0], 6), Y = Math.Round(l[1], 6), Z = Math.Round(l[2], 6), Data = keep != null ? (byte[])keep.Data.Clone() : new byte[10] };
                    // unmoved sprites keep their exact stored position
                    if (keep != null && i < old.Count && Math.Abs(s.X - keep.X) < tol && Math.Abs(s.Y - keep.Y) < tol && Math.Abs(s.Z - keep.Z) < tol) { s.X = keep.X; s.Y = keep.Y; s.Z = keep.Z; }
                    list.Add(s);
                }
                if (n != old.Count) notes.Add($"ParticleCloud_{k:00}: {old.Count} -> {n} sprites (new sprites copy the shading of existing ones)");
                doc.Clouds[k] = list;
            }

            // sprite clouds
            int si = 0;
            foreach (var spc in doc.Elements("SpriteCloud").ToList())
            {
                var tm = spc.Children.FirstOrDefault(c => c.Name == "TriMesh");
                var ps = Named($"SpriteCloud_{si:00}");
                si++;
                if (tm == null || ps.Count == 0) continue;
                var p0 = ps[0];
                var verts = tm.Children.Where(c => c.Name == "Vert").ToList();
                double UvV(double v) => flipV ? 1 - v : v;
                if (p0.P.Count == verts.Count)
                {
                    bool any = false;
                    for (int i = 0; i < verts.Count; i++)
                    {
                        var op = V3(verts[i].Vec("pos"));
                        var np = new[] { p0.P[i].X, p0.P[i].Y, p0.P[i].Z };
                        if (Len(Sub(op, np)) > 1e-2) { verts[i].Set("pos", F6V(np)); any = true; }
                        if (i < p0.U.Count)
                        {
                            var ou = verts[i].Vec("uv") ?? new double[2];
                            double nu = p0.U[i].U, nv = UvV(p0.U[i].V);
                            if (Math.Abs(ou[0] - nu) > 1e-4 || Math.Abs(ou[1] - nv) > 1e-4) { verts[i].Set("uv", $"{HmAtmos.F6(nu)} {HmAtmos.F6(nv)}"); any = true; }
                        }
                    }
                    if (any) reshaped++;
                }
                else
                {
                    // re-modelled: take the mesh as it is now
                    tm.Children.Clear();
                    for (int i = 0; i < p0.P.Count; i++)
                    {
                        var v = new AtmosElement { Name = "Vert", SingleLine = true };
                        v.Attrs.Add(new AtmosAttr { Name = "pos", Sep = "=", Value = F6V(new[] { p0.P[i].X, p0.P[i].Y, p0.P[i].Z }) });
                        var uv = i < p0.U.Count ? p0.U[i] : new V2(0, 0);
                        v.Attrs.Add(new AtmosAttr { Name = "uv", Sep = "=", Value = $"{HmAtmos.F6(uv.U)} {HmAtmos.F6(UvV(uv.V))}" });
                        tm.Children.Add(v);
                    }
                    foreach (var t in p0.Tris)
                    {
                        var tri = new AtmosElement { Name = "Tri", SingleLine = true };
                        tri.Attrs.Add(new AtmosAttr { Name = "indicies", Sep = "=", Value = $"{t[0]} {t[1]} {t[2]}" });
                        tm.Children.Add(tri);
                    }
                    tm.Set("numVerts", p0.P.Count.ToString(Inv));
                    tm.Set("numTris", p0.Tris.Count.ToString(Inv));
                    reshaped++;
                }
            }

            // sun
            var sunEl = doc.First("BasicSky") ?? doc.First("GradientSky") ?? doc.First("TexturedSky");
            var sunPart = Named("Sun").FirstOrDefault();
            if (sunEl != null && sunPart != null && sunPart.P.Count >= 4)
            {
                var nd = Norm(Centre(sunPart.P, 0));
                var od = Norm(V3(sunEl.Vec("sunDir")));
                if (Dot(nd, od) < Math.Cos(0.25 * Math.PI / 180))
                {
                    var shade = doc.First("ParticleCloudShadingParams");
                    bool sameAsShading = shade != null && shade.Get("sunDir") == sunEl.Get("sunDir");
                    sunEl.Set("sunDir", F6V(nd));
                    if (sameAsShading) shade.Set("sunDir", F6V(nd));
                    notes.Add("sun direction changed to " + F6V(nd) + (doc.First("SkySceneSettings")?.Get("useGameSunDir") == "1" ? " (note: useGameSunDir is 1, so Home uses the game's own sun direction)" : ""));
                }
            }
            if (moved > 0) notes.Add($"{moved} particle cloud(s) moved");
            if (reshaped > 0) notes.Add($"{reshaped} sprite cloud mesh(es) changed");
        }

        // ------------------------------------------------------------------ ATMOS conversions

        /// <summary>The ATMOS document from any input of the ATMOS tool (with the CDATA, edits from a model applied).</summary>
        public static AtmosDoc LoadAtmos(string input, List<string> notes)
        {
            switch (Ext(input))
            {
                case ".atmos":
                    return HmAtmos.Load(input, notes);
                case ".json":
                    return HmAtmos.FromJson(ReadJson(input));
                default:
                {
                    var stored = StoredDoc(input, AtmosKey, ".atmos.json", notes)
                                 ?? throw new InvalidDataException("this model has no sky settings: convert the .ATMOS to GLB / FBX with this tab first, and keep the .atmos.json made with it beside the model");
                    var doc = HmAtmos.FromJson(stored);
                    ApplyModelEdits(doc, input, notes);
                    return doc;
                }
            }
        }

        public static ModelConverter.Result ConvertAtmos(string input, string target, string outputDir, OverwriteBehavior overwrite)
        {
            target = target.ToUpperInvariant();
            string ext = Ext(input), name = Path.GetFileNameWithoutExtension(input);
            if (ext == ".scene") return ConvertSceneSky(input, target, outputDir, overwrite);
            if (IsProbeInput(input)) throw new NotSupportedException("this is a light probe: use the PROBE Converter (EFX / PROBE tab)");
            if (ext == ".json" && name.EndsWith(".atmos", StringComparison.OrdinalIgnoreCase)) name = Path.GetFileNameWithoutExtension(name);
            if (ext == ".json" && JsonFormat(input) != "Home ATMOS") throw new NotSupportedException("this JSON isn't an ATMOS JSON made by this tab");
            if ((ext == ".atmos" && target == "ATMOS") || (ext == ".json" && target == "JSON")) throw new NotSupportedException("The file is already " + target + ".");
            var notes = new List<string>();
            var doc = LoadAtmos(input, notes);
            Directory.CreateDirectory(outputDir);
            var r = new ModelConverter.Result();
            switch (target)
            {
                case "ATMOS":
                {
                    string outName = ModelConverter.ReserveName(name.ToUpperInvariant(), n => Path.Combine(outputDir, n + ".ATMOS"), overwrite, out bool renamed);
                    if (outName == null) return null;
                    byte[] cdata = doc.HasCdata ? HmAtmos.WriteCdata(doc) : null;   // updates addresses / counts first
                    var bytes = HmAtmos.AtmosBytes(doc);
                    string p = Path.Combine(outputDir, outName + ".ATMOS");
                    File.WriteAllBytes(p, bytes);
                    if (cdata != null) File.WriteAllBytes(Path.Combine(outputDir, outName + ".CDATA"), cdata);
                    // read-back check
                    var back = HmAtmos.Parse(new UTF8Encoding(false).GetString(bytes));
                    if (cdata != null) HmAtmos.ReadCdata(back, cdata, null);
                    if (back.Root.Children.Count != doc.Root.Children.Count) throw new InvalidDataException("read-back check failed");
                    r.OutputPath = p;
                    r.Renamed = renamed;
                    r.Summary = $"{bytes.Length:N0} bytes" + (cdata != null ? $" + CDATA {cdata.Length:N0} bytes ({doc.Clouds.Sum(c => c.Count)} cloud sprites)" : "");
                    r.Details.Add("keep the .ATMOS and .CDATA together with the same name; the scene refers to the .ATMOS");
                    break;
                }
                case "JSON":
                {
                    string outName = ModelConverter.ReserveName(name.ToUpperInvariant(), n => Path.Combine(outputDir, n + ".atmos.json"), overwrite, out bool renamed);
                    if (outName == null) return null;
                    string p = Path.Combine(outputDir, outName + ".atmos.json");
                    File.WriteAllText(p, JsonText(HmAtmos.ToJson(doc)), new UTF8Encoding(false));
                    r.OutputPath = p;
                    r.Renamed = renamed;
                    r.Summary = $"{doc.Root.Children.Count} element(s), {doc.Clouds.Sum(c => c.Count)} cloud sprites";
                    r.Details.Add("edit the values and convert the .json back to ATMOS (the .CDATA is written with it)");
                    break;
                }
                case "GLB":
                case "FBX":
                case "OBJ":
                {
                    var scene = AtmosScene(doc, input, name, notes);
                    string json = JsonText(HmAtmos.ToJson(doc));
                    scene.SceneExtras = new Dictionary<string, object> { [AtmosKey] = json };
                    var wr = ModelConverter.WriteScene(scene, target, outputDir, name, overwrite);
                    if (wr == null) return null;
                    string side = Path.Combine(Path.GetDirectoryName(wr.OutputPath) ?? outputDir, Path.GetFileNameWithoutExtension(wr.OutputPath) + ".atmos.json");
                    File.WriteAllText(side, json, new UTF8Encoding(false));
                    r = wr;
                    r.Summary = $"sky dome, {doc.Elements("SpriteCloud").Count()} sprite cloud(s), {doc.Clouds.Count} particle cloud(s) with {doc.Clouds.Sum(c => c.Count)} sprites";
                    r.Details.Add($"all the settings are saved in {Path.GetFileName(side)}{(target == "GLB" ? " (and inside the GLB)" : "")} - keep it beside the model to convert back to ATMOS");
                    r.Details.Add("sizes are in Home units (metres): the sky is several km across, so zoom out in Blender (and raise the camera's clip end)");
                    break;
                }
                default:
                    throw new NotSupportedException(target + " isn't an ATMOS output");
            }
            r.Details.AddRange(notes.Distinct());
            return r;
        }

        // ------------------------------------------------------------------ EFX -> scene

        private static readonly double[] Origin = { 0, 0, 0 };

        /// <summary>Unit shapes for the emitters (pivot first: origin, +X, +Y, +Z, so the placement can be read back).</summary>
        private static void Shape(MeshBuilder mb, string fn, double[] q, double[] t, double[] s)
        {
            var m = HmSkeleton.FromQts(new[] { q[0], q[1], q[2], q[3] }, t, s);
            double[] X(double x, double y, double z) => new[] { m[0] * x + m[4] * y + m[8] * z + m[12], m[1] * x + m[5] * y + m[9] * z + m[13], m[2] * x + m[6] * y + m[10] * z + m[14] };
            double[] Nrm(double x, double y, double z) => Norm(new[] { m[0] * x + m[4] * y + m[8] * z, m[1] * x + m[5] * y + m[9] * z, m[2] * x + m[6] * y + m[10] * z });
            // pivot (a small tripod)
            int p0 = mb.Add(X(0, 0, 0), Nrm(0, 1, 0), 0, 0), px = mb.Add(X(1, 0, 0), Nrm(0, 1, 0), 1, 0), py = mb.Add(X(0, 1, 0), Nrm(0, 1, 0), 0, 1), pz = mb.Add(X(0, 0, 1), Nrm(0, 1, 0), 1, 1);
            mb.Tri(p0, px, py); mb.Tri(p0, py, pz); mb.Tri(p0, pz, px);
            const int seg = 16;
            if (fn == "CubeVolumeEmitter")
            {
                var c = new List<int>();
                for (int i = 0; i < 8; i++) { double x = (i & 1) == 0 ? -0.5 : 0.5, y = (i & 2) == 0 ? -0.5 : 0.5, z = (i & 4) == 0 ? -0.5 : 0.5; c.Add(mb.Add(X(x, y, z), Nrm(x, y, z), x + 0.5, y + 0.5)); }
                int[][] f = { new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } };
                foreach (var q4 in f) { mb.Tri(c[q4[0]], c[q4[1]], c[q4[2]]); mb.Tri(c[q4[0]], c[q4[2]], c[q4[3]]); }
            }
            else if (fn == "ConeEmitter" || fn == "CylinderVolumeEmitter")
            {
                bool cone = fn == "ConeEmitter";
                int tip = mb.Add(X(0, cone ? 0 : 0.5, 0), Nrm(0, 1, 0), 0.5, 0);
                var ring = new List<int>();
                for (int i = 0; i <= seg; i++)
                {
                    double a = i * 2 * Math.PI / seg, x = Math.Cos(a) * 0.5, z = Math.Sin(a) * 0.5;
                    ring.Add(mb.Add(X(x, cone ? 1 : -0.5, z), Nrm(x, 0, z), i / (double)seg, 1));
                }
                if (cone) for (int i = 0; i < seg; i++) mb.Tri(tip, ring[i + 1], ring[i]);
                else
                {
                    var top = new List<int>();
                    for (int i = 0; i <= seg; i++) { double a = i * 2 * Math.PI / seg, x = Math.Cos(a) * 0.5, z = Math.Sin(a) * 0.5; top.Add(mb.Add(X(x, 0.5, z), Nrm(x, 0, z), i / (double)seg, 0)); }
                    for (int i = 0; i < seg; i++) { mb.Tri(top[i], ring[i + 1], ring[i]); mb.Tri(top[i], top[i + 1], ring[i + 1]); mb.Tri(tip, top[i + 1], top[i]); }
                }
            }
            else
            {
                // sphere (volume / surface)
                const int la = 8;
                int start = mb.Count;
                for (int i = 0; i <= la; i++)
                    for (int j = 0; j <= seg; j++)
                    {
                        double th = i * Math.PI / la, ph = j * 2 * Math.PI / seg;
                        double x = Math.Sin(th) * Math.Cos(ph), y = Math.Cos(th), z = Math.Sin(th) * Math.Sin(ph);
                        mb.Add(X(x, y, z), Nrm(x, y, z), j / (double)seg, i / (double)la);
                    }
                for (int i = 0; i < la; i++)
                    for (int j = 0; j < seg; j++)
                    {
                        int a = start + i * (seg + 1) + j, b = a + 1, c = a + seg + 1, d = c + 1;
                        mb.Tri(a, c, b); mb.Tri(b, c, d);
                    }
            }
        }

        private static float[] NodeFloats(JsonNode settings, string key, int n, float[] def)
        {
            if (!(settings.At(key) is JsonArray a) || a.Count < n) return def;
            var r = new float[n];
            for (int i = 0; i < n; i++) r[i] = a[i] is JsonValue v && v.TryGetValue(out double d) && double.IsFinite(d) && Math.Abs(d) < 3e38 ? (float)d : def[i];
            return r;
        }

        /// <summary>The effect as a model: each placed emitter as its shape, and a card with each group's texture.</summary>
        public static ExportScene EfxScene(JsonObject efx, string anchor, string name, List<string> notes)
        {
            var scene = new ExportScene { Name = name };
            var cache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int shapes = 0;
            foreach (var gNode in (efx["groups"] as JsonArray) ?? new JsonArray())
            {
                var g = gNode as JsonObject;
                int gi = JI(g.At("index"), 0);
                string tex = ((g.At("renderStages") as JsonArray)?.FirstOrDefault().At("textures") as JsonArray)?.FirstOrDefault(t => t != null) is JsonNode tn ? JS(tn) : null;
                int ti = tex != null ? Texture(scene, cache, anchor, tex, notes, 512) : -1;
                int mat = Material(scene, ti >= 0 ? "group" + gi + "_" + Path.GetFileNameWithoutExtension(tex.Replace('\\', '/')) : "emitter", ti, ti >= 0 ? "glass" : "default");
                var nodes = (g.At("emission").At("nodes") as JsonArray) ?? new JsonArray();
                double[] first = null;
                for (int k = 0; k < nodes.Count; k++)
                {
                    string fn = JS(nodes[k].At("function"));
                    var st = nodes[k].At("settings");
                    if (!HmEfx.IsShapeEmitter(fn) || st.At("origin") == null) continue;
                    var o = NodeFloats(st, "origin", 3, new float[] { 0, 0, 0 });
                    var q = NodeFloats(st, "orientation", 4, new float[] { 0, 0, 0, 1 });
                    var s = NodeFloats(st, "scale", 3, new float[] { 1, 1, 1 });
                    var mb = new MeshBuilder();
                    Shape(mb, fn, q.Select(x => (double)x).ToArray(), o.Select(x => (double)x).ToArray(), s.Select(x => (double)x).ToArray());
                    scene.Meshes.Add(mb.Mesh($"G{gi}_E{k}_{fn}", Material(scene, "emitter_shape", -1, "default")));
                    first ??= o.Select(x => (double)x).ToArray();
                    shapes++;
                }
                if (ti >= 0)
                {
                    var mb = new MeshBuilder();
                    Card(mb, first ?? Origin, 0.5, Add(first ?? Origin, new double[] { 0, 0, 5 }));
                    scene.Meshes.Add(mb.Mesh($"G{gi}_Texture", mat));
                }
            }
            notes.Add(shapes > 0 ? $"{shapes} emitter shape(s): move / turn / scale them in Blender and convert back to EFX (the G#_Texture cards only show each group's texture)"
                                 : "this effect has no placed emitter shapes (it emits from its origin)");
            notes.Add("shapes show position, turn and size; cone angles, speeds, colours and the rest are in the JSON settings");
            return scene;
        }

        private static void ApplyEfxModelEdits(JsonObject efx, string modelPath, List<string> notes)
        {
            var src = ModelConverter.LoadSource(modelPath);
            var parts = src.Collect("full");
            int changed = 0;
            foreach (var gNode in (efx["groups"] as JsonArray) ?? new JsonArray())
            {
                var g = gNode as JsonObject;
                int gi = JI(g.At("index"), 0);
                var nodes = (g.At("emission").At("nodes") as JsonArray) ?? new JsonArray();
                for (int k = 0; k < nodes.Count; k++)
                {
                    string fn = JS(nodes[k].At("function"));
                    if (!(nodes[k].At("settings") is JsonObject st) || !HmEfx.IsShapeEmitter(fn) || st["origin"] == null) continue;
                    string want = $"G{gi}_E{k}_{fn}";
                    var part = parts.FirstOrDefault(p => string.Equals(HmRoundTrip.BaseName(p.Name ?? ""), want, StringComparison.OrdinalIgnoreCase));
                    if (part == null || part.P.Count < 4) continue;
                    double[] P(int i) => new[] { part.P[i].X, part.P[i].Y, part.P[i].Z };
                    var o = P(0);
                    var ax = Sub(P(1), o); var ay = Sub(P(2), o); var az = Sub(P(3), o);
                    var m = new[] { ax[0], ax[1], ax[2], 0, ay[0], ay[1], ay[2], 0, az[0], az[1], az[2], 0, o[0], o[1], o[2], 1 };
                    var (t, q, s) = HmSkeleton.Decompose(m);
                    var oo = NodeFloats(st, "origin", 3, new float[3]);
                    var oq = NodeFloats(st, "orientation", 4, new float[] { 0, 0, 0, 1 });
                    var os = NodeFloats(st, "scale", 3, new float[] { 1, 1, 1 });
                    // same rotation either sign
                    double qd = Math.Abs(q[0] * oq[0] + q[1] * oq[1] + q[2] * oq[2] + q[3] * oq[3]);
                    if (q[0] * oq[0] + q[1] * oq[1] + q[2] * oq[2] + q[3] * oq[3] < 0) q = q.Select(x => -x).ToArray();
                    bool moved = Enumerable.Range(0, 3).Any(i => Math.Abs(t[i] - oo[i]) > 1e-3);
                    bool turned = qd < 0.99999;
                    bool scaled = Enumerable.Range(0, 3).Any(i => Math.Abs(s[i] - os[i]) > 1e-3 * Math.Max(1, Math.Abs(os[i])));
                    JsonArray Arr(IEnumerable<double> xs) => new JsonArray(xs.Select(x => (JsonNode)JsonValue.Create(Math.Round(x, 6))).ToArray());
                    if (moved) st["origin"] = Arr(t);
                    if (turned) st["orientation"] = Arr(q);
                    if (scaled) st["scale"] = Arr(s);
                    if (moved || turned || scaled) { changed++; notes.Add($"group {gi} {fn}: " + string.Join(", ", new[] { moved ? "moved" : null, turned ? "turned" : null, scaled ? "scaled" : null }.Where(x => x != null))); }
                }
            }
            if (changed == 0) notes.Add("no emitter was moved, turned or scaled in the model");
        }

        // ------------------------------------------------------------------ EFX conversions

        public static JsonObject LoadEfxJson(string input, List<string> notes)
        {
            switch (Ext(input))
            {
                case ".efx":
                {
                    var f = HmEfx.Read(File.ReadAllBytes(input));
                    notes?.AddRange(f.Warnings);
                    return HmEfx.ToJson(f, Path.GetFileName(input));
                }
                case ".json":
                    return ReadJson(input) as JsonObject ?? throw new InvalidDataException("the JSON is empty");
                default:
                {
                    var stored = StoredDoc(input, EfxKey, ".efx.json", notes) as JsonObject
                                 ?? throw new InvalidDataException("this model has no effect settings: convert the .EFX to GLB with this tab first, and keep the .efx.json made with it beside the model");
                    ApplyEfxModelEdits(stored, input, notes);
                    return stored;
                }
            }
        }

        public static ModelConverter.Result ConvertEfx(string input, string target, string outputDir, OverwriteBehavior overwrite)
        {
            target = target.ToUpperInvariant();
            string ext = Ext(input), name = Path.GetFileNameWithoutExtension(input);
            if (IsSceneFxInput(input)) return ConvertSceneFx(input, target, outputDir, overwrite);
            if (ext == ".json" && name.EndsWith(".efx", StringComparison.OrdinalIgnoreCase)) name = Path.GetFileNameWithoutExtension(name);
            if (ext == ".json" && JsonFormat(input) != "Home EFX") throw new NotSupportedException("this JSON isn't an EFX JSON made by this tab");
            if ((ext == ".efx" && target == "EFX") || (ext == ".json" && target == "JSON")) throw new NotSupportedException("The file is already " + target + ".");
            var notes = new List<string>();
            var json = LoadEfxJson(input, notes);
            Directory.CreateDirectory(outputDir);
            var r = new ModelConverter.Result();
            switch (target)
            {
                case "EFX":
                {
                    var f = HmEfx.FromJson(json, notes);
                    var bytes = HmEfx.Write(f);
                    var back = HmEfx.Read(bytes);   // read-back check
                    if (back.Groups.Count != f.Groups.Count) throw new InvalidDataException("read-back check failed");
                    string outName = ModelConverter.ReserveName(name.ToUpperInvariant(), n => Path.Combine(outputDir, n + ".EFX"), overwrite, out bool renamed);
                    if (outName == null) return null;
                    string p = Path.Combine(outputDir, outName + ".EFX");
                    File.WriteAllBytes(p, bytes);
                    r.OutputPath = p;
                    r.Renamed = renamed;
                    r.Summary = $"{f.Groups.Count} group(s), {bytes.Length:N0} bytes";
                    string orig = JS(json["source"]);
                    if (orig != null) r.Details.Add("made from " + orig);
                    break;
                }
                case "JSON":
                {
                    string outName = ModelConverter.ReserveName(name.ToUpperInvariant(), n => Path.Combine(outputDir, n + ".efx.json"), overwrite, out bool renamed);
                    if (outName == null) return null;
                    string p = Path.Combine(outputDir, outName + ".efx.json");
                    File.WriteAllText(p, JsonText(json), new UTF8Encoding(false));
                    r.OutputPath = p;
                    r.Renamed = renamed;
                    r.Summary = $"{((json["groups"] as JsonArray)?.Count ?? 0)} group(s)";
                    r.Details.Add("edit settings / textures / counts and convert the .json back to EFX");
                    break;
                }
                case "GLB":
                {
                    var scene = EfxScene(json, input, name, notes);
                    string text = JsonText(json);
                    scene.SceneExtras = new Dictionary<string, object> { [EfxKey] = text };
                    var wr = ModelConverter.WriteScene(scene, "GLB", outputDir, name, overwrite);
                    if (wr == null) return null;
                    string side = Path.Combine(Path.GetDirectoryName(wr.OutputPath) ?? outputDir, Path.GetFileNameWithoutExtension(wr.OutputPath) + ".efx.json");
                    File.WriteAllText(side, text, new UTF8Encoding(false));
                    r = wr;
                    r.Summary = $"{scene.Meshes.Count} mesh(es)";
                    r.Details.Add($"the effect's settings are saved in {Path.GetFileName(side)} and inside the GLB - keep the JSON beside the GLB to convert back");
                    break;
                }
                default:
                    throw new NotSupportedException(target + " isn't an EFX output");
            }
            r.Details.AddRange(notes.Distinct());
            return r;
        }

        // ------------------------------------------------------------------ which files each tool takes from a dropped object / folder

        private static bool ModelWith(string path, string key, string sidecar)
        {
            try { return StoredDoc(path, key, sidecar, null) != null; }
            catch (Exception) { return false; }
        }

        private static HomeSceneDoc SceneOrNull(string path)
        {
            try { return HomeScene.Load(path); }
            catch (Exception) { return null; }
        }

        /// <summary>For files found inside objects / folders: does the ATMOS tool have anything to do with it?</summary>
        public static bool AcceptsAtmos(string path) => Ext(path) switch
        {
            ".atmos" => true,
            ".scene" => SceneOrNull(path)?.AtmosPath != null,
            ".json" => JsonFormat(path) == "Home ATMOS",
            ".glb" or ".gltf" or ".fbx" or ".obj" => ModelWith(path, AtmosKey, ".atmos.json"),
            _ => false,
        };

        public static bool AcceptsCdata(string path) => Ext(path) switch
        {
            ".cdata" => true,
            ".atmos" => HmAtmos.CdataPath(path) != null,
            ".json" => JsonFormat(path) == "Home CDATA",
            _ => false,   // CSV / PLY / OBJ only when dropped on their own
        };

        public static bool AcceptsEfx(string path) => Ext(path) switch
        {
            ".efx" => true,
            ".scene" => SceneOrNull(path)?.Effects.Count > 0,
            ".json" => JsonFormat(path) is "Home EFX" or "Home SCENE FX",
            ".glb" or ".gltf" => ModelWith(path, EfxKey, ".efx.json") || ModelWith(path, SceneKey, ".scenefx.json"),
            _ => false,
        };

        public static bool AcceptsProbe(string path) => Ext(path) switch
        {
            ".probe" => true,
            ".scene" => SceneOrNull(path)?.ProbePath != null,
            ".json" => JsonFormat(path) == "Home PROBE",
            ".glb" or ".gltf" or ".fbx" or ".obj" => ModelWith(path, ProbeKey, ".probe.json"),
            _ => false,
        };

        // ------------------------------------------------------------------ light probes

        private static bool IsProbeInput(string input)
        {
            switch (Ext(input))
            {
                case ".probe": return true;
                case ".json": return JsonFormat(input) == "Home PROBE";
                case ".glb": case ".gltf": case ".fbx": case ".obj":
                    try { return StoredDoc(input, ProbeKey, ".probe.json", null) != null; }
                    catch (Exception) { return false; }
                default: return false;
            }
        }

        private static string Safe(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "") sb.Append(char.IsLetterOrDigit(c) && c < 128 ? c : '_');
            var r = sb.ToString().Trim('_');
            return r.Length == 0 ? "x" : r.Length > 40 ? r.Substring(0, 40) : r;
        }

        /// <summary>Quaternion turning +Y onto <paramref name="d"/>.</summary>
        private static double[] FromUp(double[] d)
        {
            var n = Norm(d);
            double c = n[1];
            if (c > 0.999999) return new double[] { 0, 0, 0, 1 };
            if (c < -0.999999) return new double[] { 1, 0, 0, 0 };
            var axis = Norm(Cross(new double[] { 0, 1, 0 }, n));
            double a = Math.Acos(Math.Clamp(c, -1, 1)) / 2;
            return new[] { axis[0] * Math.Sin(a), axis[1] * Math.Sin(a), axis[2] * Math.Sin(a), Math.Cos(a) };
        }

        private static string LightMesh(int i, string name) => $"L{i}_{Safe(name)}";

        /// <summary>The probe's lights as markers: a ball for each light, a cone pointing the sun's way.</summary>
        public static ExportScene ProbeScene(ProbeFile f, string name)
        {
            var scene = new ExportScene { Name = name };
            foreach (var l in f.Lights)
            {
                var mb = new MeshBuilder();
                var pos = l.Position.Take(3).Select(x => double.IsFinite(x) ? (double)x : 0).ToArray();
                bool sun = l.Type == 0;
                var dir = l.Direction.Take(3).Select(x => double.IsFinite(x) ? (double)x : 0).ToArray();
                Shape(mb, sun ? "ConeEmitter" : "SphereVolumeEmitter", sun ? FromUp(dir) : new double[] { 0, 0, 0, 1 }, pos, sun ? new double[] { 2, 2, 2 } : new double[] { 0.25, 0.25, 0.25 });
                scene.Meshes.Add(mb.Mesh(LightMesh(l.Index, l.Name), Material(scene, "light_" + HmProbe.TypeName(l.Type).Replace(' ', '_'), -1, "emissive")));
            }
            return scene;
        }

        private static void ApplyProbeModelEdits(JsonObject json, string modelPath, List<string> notes)
        {
            var parts = ModelConverter.LoadSource(modelPath).Collect("full");
            int changed = 0;
            foreach (var node in (json["lights"] as JsonArray) ?? new JsonArray())
            {
                if (!(node is JsonObject lj)) continue;
                int i = JI(lj["index"], -1);
                string want = LightMesh(i, JS(lj["name"]));
                var part = parts.FirstOrDefault(p => string.Equals(HmRoundTrip.BaseName(p.Name ?? ""), want, StringComparison.OrdinalIgnoreCase));
                if (part == null || part.P.Count < 4) continue;
                var p0 = new[] { part.P[0].X, part.P[0].Y, part.P[0].Z };
                var py = new[] { part.P[2].X, part.P[2].Y, part.P[2].Z };
                var old = NodeFloats(lj, "position", 3, new float[3]);
                JsonArray Arr(IEnumerable<double> xs) => new JsonArray(xs.Select(x => (JsonNode)JsonValue.Create(Math.Round(x, 6))).ToArray());
                var what = new List<string>();
                if (Enumerable.Range(0, 3).Any(k => Math.Abs(p0[k] - old[k]) > 1e-3)) { lj["position"] = Arr(p0); what.Add("moved"); }
                if (JS(lj["type"]) == "sun")
                {
                    var d = Norm(Sub(py, p0));
                    var od = Norm(NodeFloats(lj, "direction", 3, new float[] { 0, 1, 0 }).Select(x => (double)x).ToArray());
                    if (Dot(d, od) < 0.99999) { lj["direction"] = Arr(d); what.Add("turned"); }
                }
                if (what.Count > 0) { changed++; notes.Add($"light {i} {JS(lj["name"])}: {string.Join(", ", what)}"); }
            }
            if (changed == 0) notes.Add("no light was moved or turned in the model");
        }

        private static JsonObject LoadProbeJson(string input, List<string> notes)
        {
            switch (Ext(input))
            {
                case ".probe":
                {
                    var f = HmProbe.Read(File.ReadAllBytes(input));
                    notes?.AddRange(f.Warnings);
                    return HmProbe.ToJson(f, Path.GetFileName(input));
                }
                case ".json":
                    return ReadJson(input) as JsonObject ?? throw new InvalidDataException("the JSON is empty");
                default:
                {
                    var stored = StoredDoc(input, ProbeKey, ".probe.json", notes) as JsonObject
                                 ?? throw new InvalidDataException("this model has no light probe settings: convert the .PROBE to GLB / FBX with this tab first, and keep the .probe.json made with it beside the model");
                    ApplyProbeModelEdits(stored, input, notes);
                    return stored;
                }
            }
        }

        private static ProbeFile LoadProbe(string input, List<string> notes) =>
            Ext(input) == ".probe" ? HmProbe.Read(File.ReadAllBytes(input)) : HmProbe.FromJson(LoadProbeJson(input, notes), notes);

        /// <summary>The .PROBE a scene uses (for the PROBE tool), else the input itself.</summary>
        private static string ProbeOf(string input, List<string> notes)
        {
            if (Ext(input) != ".scene") return input;
            var sd = HomeScene.Load(input);
            if (sd.ProbePath == null)
                throw new InvalidDataException(sd.Probe != null ? "the scene's light probe wasn't found next to it (" + sd.Probe + ")" : "this scene has no light probe");
            notes?.Add($"light probe of scene {Path.GetFileName(input)}: {sd.Probe}");
            return sd.ProbePath;
        }

        public static string DescribeProbe(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"-- {Path.GetFileName(path)} ({new FileInfo(path).Length:N0} bytes)");
            var notes = new List<string>();
            string probe = ProbeOf(path, notes);
            if (!IsProbeInput(probe)) throw new InvalidDataException("not a light probe (.PROBE) or a probe JSON / model made by this tool");
            var pf = LoadProbe(probe, notes);
            sb.Append(HmProbe.Describe(pf));
            foreach (var n in notes) sb.AppendLine("     Note: " + n);
            return sb.ToString();
        }

        public static PreviewData PreviewProbe(string path)
        {
            var notes = new List<string>();
            string probe = ProbeOf(path, notes);
            if (!IsProbeInput(probe)) throw new InvalidDataException("not a light probe (.PROBE) or a probe JSON / model made by this tool");
            var pf = LoadProbe(probe, notes);
            if (pf.Lights.Count == 0) throw new InvalidDataException("this probe has no lights to show");
            var ps = ProbeScene(pf, Path.GetFileNameWithoutExtension(probe));
            var pd = ModelPreview.FromScene(ps, Path.GetFileName(probe));
            pd.Info.AddRange(HmProbe.Describe(pf).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(12));
            pd.Info.AddRange(notes.Take(4));
            return pd;
        }

        public static ModelConverter.Result ConvertProbe(string input, string target, string outputDir, OverwriteBehavior overwrite)
        {
            target = target.ToUpperInvariant();
            if (target == "ATMOS") target = "PROBE";
            var sceneNotes = new List<string>();
            bool fromScene = Ext(input) == ".scene";
            input = ProbeOf(input, sceneNotes);
            if (!IsProbeInput(input)) throw new InvalidDataException("not a light probe (.PROBE) or a probe JSON / model made by this tool");
            string ext = Ext(input), name = Path.GetFileNameWithoutExtension(input);
            if (name.EndsWith(".probe", StringComparison.OrdinalIgnoreCase)) name = Path.GetFileNameWithoutExtension(name);
            if (!fromScene && ((ext == ".probe" && target == "PROBE") || (ext == ".json" && target == "JSON")))
                throw new NotSupportedException(ext == ".probe" ? "The file is already a PROBE (pick JSON, GLB or FBX)." : "The file is already JSON.");
            var notes = sceneNotes;
            var json = LoadProbeJson(input, notes);
            var f = HmProbe.FromJson(json, notes);
            Directory.CreateDirectory(outputDir);
            var r = new ModelConverter.Result();
            switch (target)
            {
                case "PROBE":
                {
                    string outName = ModelConverter.ReserveName(name.ToUpperInvariant(), n => Path.Combine(outputDir, n + ".PROBE"), overwrite, out bool renamed);
                    if (outName == null) return null;
                    string p = Path.Combine(outputDir, outName + ".PROBE");
                    File.WriteAllBytes(p, f.Data);
                    r.OutputPath = p; r.Renamed = renamed;
                    r.Summary = $"{f.Lights.Count} light(s), {f.Data.Length:N0} bytes";
                    r.Details.Add("the baked lighting grid is unchanged: bake the probe again in the Home tools for baked light to follow moved lights");
                    break;
                }
                case "JSON":
                {
                    string outName = ModelConverter.ReserveName(name.ToUpperInvariant(), n => Path.Combine(outputDir, n + ".probe.json"), overwrite, out bool renamed);
                    if (outName == null) return null;
                    string p = Path.Combine(outputDir, outName + ".probe.json");
                    File.WriteAllText(p, JsonText(HmProbe.ToJson(f, JS(json["source"]) ?? Path.GetFileName(input))), new UTF8Encoding(false));
                    r.OutputPath = p; r.Renamed = renamed;
                    r.Summary = $"{f.Lights.Count} light(s)";
                    r.Details.Add("edit the lights and convert the .json back with PROBE picked to get the .PROBE");
                    break;
                }
                case "GLB":
                case "FBX":
                case "OBJ":
                {
                    if (f.Lights.Count == 0) throw new InvalidDataException("this probe has no lights to show (JSON still works)");
                    var scene = ProbeScene(f, name);
                    string text = JsonText(HmProbe.ToJson(f, JS(json["source"]) ?? Path.GetFileName(input)));
                    scene.SceneExtras = new Dictionary<string, object> { [ProbeKey] = text };
                    var wr = ModelConverter.WriteScene(scene, target, outputDir, name, overwrite);
                    if (wr == null) return null;
                    string side = Path.Combine(Path.GetDirectoryName(wr.OutputPath) ?? outputDir, Path.GetFileNameWithoutExtension(wr.OutputPath) + ".probe.json");
                    File.WriteAllText(side, text, new UTF8Encoding(false));
                    r = wr;
                    r.Summary = $"{f.Lights.Count} light marker(s)";
                    r.Details.Add($"move the light balls (and turn the sun cone) in Blender, keep {Path.GetFileName(side)} beside the model, and convert back with PROBE picked");
                    break;
                }
                default:
                    throw new NotSupportedException(target + " isn't a PROBE output");
            }
            r.Details.AddRange(notes.Distinct());
            return r;
        }

        /// <summary>A scene on the ATMOS tool: its sky (and light probe) converted, or copied out for ATMOS.</summary>
        private static ModelConverter.Result ConvertSceneSky(string input, string target, string outputDir, OverwriteBehavior overwrite)
        {
            var sd = HomeScene.Load(input);
            target = target.ToUpperInvariant();
            if (sd.AtmosPath == null)
                throw new InvalidDataException(sd.Atmos != null ? "the scene's sky wasn't found next to it (" + sd.Atmos + ")" :
                    "this scene has no sky (.ATMOS)" + (sd.Probe != null ? "; its light probe goes on the PROBE Converter (EFX / PROBE tab)" : ""));
            ModelConverter.Result r = null;
            if (target == "ATMOS")
            {
                // copy the scene's sky files out as they are
                Directory.CreateDirectory(outputDir);
                foreach (var src in new[] { sd.AtmosPath, HmAtmos.CdataPath(sd.AtmosPath) }.Where(x => x != null))
                {
                    string ext = Path.GetExtension(src).ToUpperInvariant();
                    string outName = ModelConverter.ReserveName(Path.GetFileNameWithoutExtension(src).ToUpperInvariant(), n => Path.Combine(outputDir, n + ext), overwrite, out bool renamed);
                    if (outName == null) continue;
                    string p = Path.Combine(outputDir, outName + ext);
                    File.Copy(src, p, true);
                    if (r == null) r = new ModelConverter.Result { OutputPath = p, Renamed = renamed };
                    else r.Details.Add("also copied " + p);
                }
                if (r == null) return null;
                r.Summary = "the scene's sky files, copied out";
            }
            else
            {
                r = ConvertAtmos(sd.AtmosPath, target, outputDir, overwrite);
                if (r == null) return null;
            }
            r.Details.Insert(0, $"sky of scene {Path.GetFileName(input)}: {sd.Atmos}");
            r.Details.AddRange(sd.Notes.Where(n => !n.StartsWith("effect")));
            return r;
        }

        // ------------------------------------------------------------------ scenes (placed effects)

        private const double MarkerSize = 0.5;

        /// <summary>Output\Atmos\GLB -> Output\Probe\GLB.</summary>
        private static string Sibling(string outputDir, string kind)
        {
            string fmt = Path.GetFileName(outputDir.TrimEnd(Path.DirectorySeparatorChar));
            string top = Path.GetDirectoryName(Path.GetDirectoryName(outputDir.TrimEnd(Path.DirectorySeparatorChar)) ?? "");
            return string.IsNullOrEmpty(top) ? Path.Combine(outputDir, kind) : Path.Combine(top, kind, fmt);
        }

        private static bool IsSceneFxInput(string input)
        {
            switch (Ext(input))
            {
                case ".scene": return true;
                case ".json": return JsonFormat(input) == "Home SCENE FX";
                case ".glb": case ".gltf":
                    try { return StoredDoc(input, SceneKey, ".scenefx.json", null) != null; }
                    catch (Exception) { return false; }
                default: return false;
            }
        }

        private static JsonArray Arr3(double[] v) => new JsonArray(v.Take(3).Select(x => (JsonNode)JsonValue.Create(double.IsFinite(x) ? Math.Round(x, 6) : 0)).ToArray());

        public static JsonObject SceneFxJson(HomeSceneDoc sd)
        {
            var fx = new JsonArray();
            foreach (var e in sd.Effects)
                fx.Add(new JsonObject
                {
                    ["index"] = e.Index,
                    ["name"] = e.Name,
                    ["efx"] = e.Efx,
                    ["found"] = e.EfxPath != null,
                    ["translate"] = Arr3(e.T),
                    ["rotate"] = Arr3(e.R),
                    ["scale"] = Arr3(e.S),
                });
            return new JsonObject
            {
                ["format"] = "Home SCENE FX",
                ["source"] = Path.GetFileName(sd.Path),
                ["style"] = sd.Gap ? "gap" : "scene",
                ["note"] = "move / turn (degrees) / scale the placed effects and convert back with EFX picked to get the updated .SCENE; the rest of the scene is kept as it is",
                ["sky"] = sd.Atmos,
                ["probe"] = sd.Probe,
                ["effects"] = fx,
                ["bom"] = sd.Bom,
                ["scene"] = sd.Text,
            };
        }

        /// <summary>A 3-vector from the JSON; a value the JSON shows unchanged (to 6 places) keeps its exact original.</summary>
        private static double[] JVec(JsonNode n, double[] def)
        {
            var r = (double[])def.Clone();
            if (!(n is JsonArray a)) return r;
            for (int i = 0; i < 3 && i < a.Count; i++)
            {
                double d;
                if (!(a[i] is JsonValue v) ||
                    !((v.TryGetValue(out d) && double.IsFinite(d)) || (v.TryGetValue(out string t) && double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && double.IsFinite(d))))
                    continue;
                if (Math.Abs(d - Math.Round(def[i], 6)) > 1e-9) r[i] = d;
            }
            return r;
        }

        private static (HomeSceneDoc, List<SceneEffect>) SceneFxFromJson(JsonObject j, string path, List<string> notes)
        {
            string text = JS(j["scene"]);
            if (string.IsNullOrEmpty(text)) throw new InvalidDataException("the JSON has no \"scene\" text (the original scene, needed to write it back)");
            var sd = HomeScene.Parse(text, path);
            sd.Bom = j["bom"] is JsonValue bv && bv.TryGetValue(out bool bom) && bom;
            var edited = new List<SceneEffect>();
            foreach (var node in (j["effects"] as JsonArray) ?? new JsonArray())
            {
                if (!(node is JsonObject ej)) continue;
                int i = JI(ej["index"], -1);
                string name = JS(ej["name"]);
                var old = sd.Effects.FirstOrDefault(x => x.Index == i && (name == null || x.Name == name)) ?? sd.Effects.FirstOrDefault(x => x.Name == name);
                if (old == null) { notes?.Add($"effect {i} \"{name}\" isn't in the scene text: skipped"); continue; }
                edited.Add(new SceneEffect
                {
                    Index = old.Index, Name = old.Name, Efx = old.Efx,
                    T = JVec(ej["translate"], old.T), R = JVec(ej["rotate"], old.R), S = JVec(ej["scale"], old.S),
                });
            }
            return (sd, edited);
        }

        private static void ApplySceneFxModelEdits(JsonObject j, string modelPath, List<string> notes)
        {
            var parts = ModelConverter.LoadSource(modelPath).Collect("full");
            foreach (var node in (j["effects"] as JsonArray) ?? new JsonArray())
            {
                if (!(node is JsonObject ej)) continue;
                string want = $"FX{JI(ej["index"], -1)}_{Safe(JS(ej["name"]))}";
                var part = parts.FirstOrDefault(p => string.Equals(HmRoundTrip.BaseName(p.Name ?? ""), want, StringComparison.OrdinalIgnoreCase));
                if (part == null || part.P.Count < 4) continue;
                double[] P(int k) => new[] { part.P[k].X, part.P[k].Y, part.P[k].Z };
                var o = P(0);
                var ax = Sub(P(1), o); var ay = Sub(P(2), o); var az = Sub(P(3), o);
                var (t, q, s) = HmSkeleton.Decompose(new[] { ax[0], ax[1], ax[2], 0, ay[0], ay[1], ay[2], 0, az[0], az[1], az[2], 0, o[0], o[1], o[2], 1 });
                s = s.Select(x => x / MarkerSize).ToArray();
                var oldT = NodeFloats(ej, "translate", 3, new float[3]);
                var oldR = NodeFloats(ej, "rotate", 3, new float[3]).Select(x => (double)x).ToArray();
                var oldS = NodeFloats(ej, "scale", 3, new float[] { 1, 1, 1 });
                var oq = HomeScene.EulerToQuat(oldR);
                double dq = Math.Abs(q[0] * oq[0] + q[1] * oq[1] + q[2] * oq[2] + q[3] * oq[3]);
                var what = new List<string>();
                if (Enumerable.Range(0, 3).Any(k => Math.Abs(t[k] - oldT[k]) > 1e-3)) { ej["translate"] = Arr3(t); what.Add("moved"); }
                if (dq < 0.99999) { ej["rotate"] = Arr3(HomeScene.QuatToEuler(q)); what.Add("turned"); }
                if (Enumerable.Range(0, 3).Any(k => Math.Abs(s[k] - oldS[k]) > 1e-3 * Math.Max(1, Math.Abs(oldS[k])))) { ej["scale"] = Arr3(s); what.Add("scaled"); }
                if (what.Count > 0) notes.Add($"{JS(ej["name"])}: {string.Join(", ", what)} in the model");
            }
        }

        private static JsonObject LoadSceneFxJson(string input, List<string> notes)
        {
            switch (Ext(input))
            {
                case ".scene":
                {
                    var sd = HomeScene.Load(input);
                    notes?.AddRange(sd.Notes);
                    return SceneFxJson(sd);
                }
                case ".json":
                    return ReadJson(input) as JsonObject ?? throw new InvalidDataException("the JSON is empty");
                default:
                {
                    var stored = StoredDoc(input, SceneKey, ".scenefx.json", notes) as JsonObject
                                 ?? throw new InvalidDataException("this model has no scene layout: convert the .SCENE to GLB with this tab first, and keep the .scenefx.json made with it beside the model");
                    ApplySceneFxModelEdits(stored, input, notes);
                    return stored;
                }
            }
        }

        /// <summary>The scene's placed effects: a marker box for each (move / turn / scale it), with the effect's own shapes under it.</summary>
        public static ExportScene SceneFxScene(JsonObject j, string anchor, string name, List<string> notes)
        {
            string path = Ext(anchor) == ".scene" ? anchor : null;
            var sd = path != null ? HomeScene.Load(path) : null;
            var scene = new ExportScene { Name = name };
            var cache = new Dictionary<string, (ExportScene Sub, int Mat0, int Tex0)>(StringComparer.OrdinalIgnoreCase);
            int marker = Material(scene, "fx_marker", -1, "default"), shapes = 0;
            foreach (var node in (j["effects"] as JsonArray) ?? new JsonArray())
            {
                if (!(node is JsonObject ej)) continue;
                int i = JI(ej["index"], 0);
                var e = new SceneEffect
                {
                    T = NodeFloats(ej, "translate", 3, new float[3]).Select(x => (double)x).ToArray(),
                    R = NodeFloats(ej, "rotate", 3, new float[3]).Select(x => (double)x).ToArray(),
                    S = NodeFloats(ej, "scale", 3, new float[] { 1, 1, 1 }).Select(x => (double)x).ToArray(),
                };
                var mb = new MeshBuilder();
                Shape(mb, "CubeVolumeEmitter", HomeScene.EulerToQuat(e.R), e.T, e.S.Select(x => x * MarkerSize).ToArray());
                scene.Meshes.Add(mb.Mesh($"FX{i}_{Safe(JS(ej["name"]))}", marker));

                // the effect's shapes, placed by the marker
                string efx = JS(ej["efx"]);
                string file = sd?.Effects.FirstOrDefault(x => x.Index == i)?.EfxPath ?? (efx != null ? HomeScene.Resolve(anchor, efx) : null);
                if (file == null || shapes > 2000) continue;
                if (!cache.TryGetValue(file, out var c))
                {
                    try
                    {
                        var sub = EfxScene(HmEfx.ToJson(HmEfx.Read(File.ReadAllBytes(file)), Path.GetFileName(file)), file, Path.GetFileNameWithoutExtension(file), new List<string>());
                        int tex0 = scene.Textures.Count, mat0 = scene.Materials.Count;
                        scene.Textures.AddRange(sub.Textures);
                        foreach (var m in sub.Materials)
                            scene.Materials.Add(new ExportMaterial
                            {
                                Name = Path.GetFileNameWithoutExtension(file) + "_" + m.Name, Kind = m.Kind, AlphaRef = m.AlphaRef,
                                Colour = m.Colour + tex0, Emissive = m.Emissive + tex0,
                            });
                        c = (sub, mat0, tex0);
                    }
                    catch (Exception ex) { notes.Add($"{Path.GetFileName(file)} could not be shown: {ex.Message}"); c = (null, 0, 0); }
                    cache[file] = c;
                }
                if (c.Sub == null) continue;
                var mx = HomeScene.Matrix(e);
                foreach (var m in c.Sub.Meshes)
                {
                    if (m.P == null || m.Tris == null) continue;
                    var p = new float[m.P.Length];
                    var nn = new float[m.P.Length];
                    var mn = m.N != null && m.N.Length == m.P.Length ? m.N : new float[m.P.Length];
                    for (int k = 0; k + 2 < m.P.Length; k += 3)
                    {
                        double x = m.P[k], y = m.P[k + 1], z = m.P[k + 2];
                        p[k] = (float)(mx[0] * x + mx[4] * y + mx[8] * z + mx[12]);
                        p[k + 1] = (float)(mx[1] * x + mx[5] * y + mx[9] * z + mx[13]);
                        p[k + 2] = (float)(mx[2] * x + mx[6] * y + mx[10] * z + mx[14]);
                        var n3 = Norm(new[] { mx[0] * mn[k] + mx[4] * mn[k + 1] + mx[8] * mn[k + 2], mx[1] * mn[k] + mx[5] * mn[k + 1] + mx[9] * mn[k + 2], mx[2] * mn[k] + mx[6] * mn[k + 1] + mx[10] * mn[k + 2] });
                        nn[k] = (float)n3[0]; nn[k + 1] = (float)n3[1]; nn[k + 2] = (float)n3[2];
                    }
                    scene.Meshes.Add(new ExportMesh { Name = $"FX{i}__{m.Name}", Material = m.Material >= 0 ? m.Material + c.Mat0 : -1, P = p, N = nn, UV = m.UV, Tris = m.Tris });
                    shapes++;
                }
            }
            if (shapes > 2000) notes.Add("very many effects: only the first ones show their shapes");
            notes.Add("move / turn / scale the FX#_ boxes; the FX#__ shapes under them only show what each effect looks like");
            return scene;
        }

        public static ModelConverter.Result ConvertSceneFx(string input, string target, string outputDir, OverwriteBehavior overwrite)
        {
            target = target.ToUpperInvariant();
            string ext = Ext(input), name = Path.GetFileNameWithoutExtension(input);
            if (name.EndsWith(".scenefx", StringComparison.OrdinalIgnoreCase)) name = Path.GetFileNameWithoutExtension(name);
            if (ext == ".json" && target == "JSON") throw new NotSupportedException("The file is already JSON.");
            var notes = new List<string>();
            var json = LoadSceneFxJson(input, notes);
            outputDir = Path.Combine(Path.GetDirectoryName(outputDir.TrimEnd(Path.DirectorySeparatorChar)) ?? outputDir, "Scenes", Path.GetFileName(outputDir.TrimEnd(Path.DirectorySeparatorChar)));   // EFX\\Scenes\\<format>
            Directory.CreateDirectory(outputDir);
            var r = new ModelConverter.Result();
            int count = (json["effects"] as JsonArray)?.Count ?? 0;
            switch (target)
            {
                case "EFX":
                {
                    if (ext == ".scene")
                    {
                        // copy out every effect the scene uses
                        var sd = HomeScene.Load(input);
                        var files = sd.Effects.Where(e => e.EfxPath != null).Select(e => e.EfxPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        if (files.Count == 0) throw new InvalidDataException(sd.Effects.Count == 0 ? "this scene places no particle effects" : "none of the scene's .efx files were found next to it");
                        foreach (var src in files)
                        {
                            string outName = ModelConverter.ReserveName(Path.GetFileNameWithoutExtension(src).ToUpperInvariant(), n => Path.Combine(outputDir, n + ".EFX"), overwrite, out bool renamed);
                            if (outName == null) continue;
                            string p = Path.Combine(outputDir, outName + ".EFX");
                            File.Copy(src, p, true);
                            if (r.OutputPath == null) { r.OutputPath = p; r.Renamed = renamed; }
                            else r.Details.Add("also " + p);
                        }
                        if (r.OutputPath == null) return null;
                        r.Summary = $"{files.Count} effect file(s) used by {sd.Effects.Count} placement(s)";
                        break;
                    }
                    var (doc, edited) = SceneFxFromJson(json, input, notes);
                    string text = HomeScene.Write(doc, edited, notes);
                    var back = HomeScene.Parse(text, input);   // read-back check
                    if (back.Effects.Count != doc.Effects.Count) throw new InvalidDataException("read-back check failed (effect count changed)");
                    string sceneName = Path.GetFileNameWithoutExtension(JS(json["source"]) ?? name + ".SCENE");
                    string on = ModelConverter.ReserveName(sceneName.ToUpperInvariant(), n => Path.Combine(outputDir, n + ".SCENE"), overwrite, out bool ren);
                    if (on == null) return null;
                    string sp = Path.Combine(outputDir, on + ".SCENE");
                    File.WriteAllBytes(sp, HomeScene.Bytes(doc, text));
                    r.OutputPath = sp; r.Renamed = ren;
                    r.Summary = $"{count} placed effect(s)";
                    r.Details.Add("put it back where the scene came from (same name), with the scene's other files");
                    break;
                }
                case "JSON":
                {
                    string outName = ModelConverter.ReserveName(name.ToUpperInvariant(), n => Path.Combine(outputDir, n + ".scenefx.json"), overwrite, out bool renamed);
                    if (outName == null) return null;
                    string p = Path.Combine(outputDir, outName + ".scenefx.json");
                    File.WriteAllText(p, JsonText(json), new UTF8Encoding(false));
                    r.OutputPath = p; r.Renamed = renamed;
                    r.Summary = $"{count} placed effect(s)";
                    r.Details.Add("edit translate / rotate (degrees) / scale and convert the .json back with EFX picked to get the .SCENE");
                    break;
                }
                case "GLB":
                {
                    if (count == 0) throw new InvalidDataException("this scene places no particle effects, so there is nothing to lay out (JSON still works)");
                    var scene = SceneFxScene(json, input, name, notes);
                    string text = JsonText(json);
                    scene.SceneExtras = new Dictionary<string, object> { [SceneKey] = text };
                    var wr = ModelConverter.WriteScene(scene, "GLB", outputDir, name, overwrite);
                    if (wr == null) return null;
                    string side = Path.Combine(Path.GetDirectoryName(wr.OutputPath) ?? outputDir, Path.GetFileNameWithoutExtension(wr.OutputPath) + ".scenefx.json");
                    File.WriteAllText(side, text, new UTF8Encoding(false));
                    r = wr;
                    r.Summary = $"{count} placed effect(s), {scene.Meshes.Count} mesh(es)";
                    r.Details.Add($"the scene is saved in {Path.GetFileName(side)} and inside the GLB: convert the GLB back with EFX picked to get the updated .SCENE");
                    break;
                }
                default:
                    throw new NotSupportedException(target + " isn't a scene output");
            }
            r.Details.AddRange(notes.Distinct());
            return r;
        }

        // ------------------------------------------------------------------ inspect / preview

        public static string DescribeAtmos(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"-- {Path.GetFileName(path)} ({new FileInfo(path).Length:N0} bytes)");
            var notes = new List<string>();
            if (Ext(path) == ".scene")
            {
                var sd = HomeScene.Load(path);
                sb.Append(HomeScene.Describe(sd));
                if (sd.AtmosPath != null) sb.Append(DescribeAtmos(sd.AtmosPath));
                return sb.ToString();
            }
            if (IsProbeInput(path)) return DescribeProbe(path);
            var doc = LoadAtmos(path, notes);
            sb.Append(HmAtmos.Describe(doc));
            foreach (var n in notes) sb.AppendLine("     Note: " + n);
            return sb.ToString();
        }

        public static string DescribeEfx(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"-- {Path.GetFileName(path)} ({new FileInfo(path).Length:N0} bytes)");
            var notes = new List<string>();
            if (IsSceneFxInput(path))
            {
                var sj = LoadSceneFxJson(path, notes);
                var (sd, edited) = SceneFxFromJson(sj, path, notes);
                foreach (var e in edited) { var o = sd.Effects.FirstOrDefault(x => x.Index == e.Index); if (o != null) { o.T = e.T; o.R = e.R; o.S = e.S; } }
                sb.Append(HomeScene.Describe(sd));
                foreach (var n in notes) sb.AppendLine("     Note: " + n);
                return sb.ToString();
            }
            var json = LoadEfxJson(path, notes);
            var f = HmEfx.FromJson(json, notes);
            sb.Append(HmEfx.Describe(f));
            foreach (var n in notes) sb.AppendLine("     Note: " + n);
            return sb.ToString();
        }

        public static PreviewData PreviewAtmos(string path)
        {
            var notes = new List<string>();
            if (Ext(path) == ".scene")
            {
                var sd = HomeScene.Load(path);
                if (sd.AtmosPath != null) return PreviewAtmos(sd.AtmosPath);
                throw new InvalidDataException(sd.Atmos != null ? "the scene's sky wasn't found next to it (" + sd.Atmos + ")" : "this scene has no sky (.ATMOS)");
            }
            if (IsProbeInput(path)) return PreviewProbe(path);
            var doc = LoadAtmos(path, notes);
            var scene = AtmosScene(doc, path, Path.GetFileNameWithoutExtension(path), notes);
            var d = ModelPreview.FromScene(scene, Path.GetFileName(path));
            d.Info.AddRange(HmAtmos.Describe(doc).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
            d.Info.AddRange(notes.Where(n => n.StartsWith("texture")).Take(4));
            return d;
        }

        public static PreviewData PreviewEfx(string path)
        {
            var notes = new List<string>();
            if (IsSceneFxInput(path))
            {
                var sj = LoadSceneFxJson(path, notes);
                var ss = SceneFxScene(sj, path, Path.GetFileNameWithoutExtension(path), notes);
                var sdp = ModelPreview.FromScene(ss, Path.GetFileName(path));
                sdp.Info.Add($"{((sj["effects"] as JsonArray)?.Count ?? 0)} placed effect(s); boxes mark where each one sits");
                sdp.Info.AddRange(notes.Take(6));
                return sdp;
            }
            var json = LoadEfxJson(path, notes);
            var scene = EfxScene(json, path, Path.GetFileNameWithoutExtension(path), notes);
            var d = ModelPreview.FromScene(scene, Path.GetFileName(path));
            try
            {
                var simInfo = new List<string>();
                d.Animation = EfxAnimation(json, scene, simInfo);
                if (d.Animation != null)
                {
                    d.Animation.Info.AddRange(simInfo);
                    // while it plays: the emitter shapes see-through, the texture cards hidden (they're on the layer bar)
                    foreach (var l in d.Layers)
                    {
                        if (l.Name == "Emitters") l.Tint = 0x405AA0E6;
                        else if (l.Name == "Group textures") l.Visible = false;
                    }
                }
            }
            catch (Exception ex)
            {
                d.Info.Add("the particle playback could not be set up: " + ex.Message);
            }
            d.Info.AddRange(HmEfx.Describe(HmEfx.FromJson(json, null)).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
            return d;
        }
    }
}

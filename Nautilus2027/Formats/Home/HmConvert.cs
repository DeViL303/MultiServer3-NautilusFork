#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

// Conversion pipeline: imported model (SourceDoc) + decoded images -> MDL bytes + DDS textures.
// C# port of the HOME_DEV_KIT conversion module (ws/mdlb/src/13.js).
namespace Nautilus2027.Formats.Home
{
    public sealed class HmConvertSettings
    {
        public string ModelName = "MODEL";
        public ushort Version = 0x0103;
        /// <summary>Home path the MDL uses for its textures; {name} becomes the model name.</summary>
        public string TexPrefix = "\\{name}\\TEXTURES\\";
        /// <summary>auto | full | noscale</summary>
        public string Transform = "auto";
        /// <summary>y | z</summary>
        public string UpAxis = "y";
        public double Scale = 1;
        public bool FlipV;
        public bool FlipWinding;
        /// <summary>auto | DXT1 | DXT5 | ARGB</summary>
        public string DdsFormat = "auto";
        public bool Mips = true;
        public bool Pow2 = true;
        /// <summary>Largest texture side in pixels; 0 = keep the source size.</summary>
        public int MaxSize;
        public bool FlipNormalGreen;
        /// <summary>generate (flat normal / spec maps) | placeholder (generic shared textures)</summary>
        public string MissingMaps = "generate";
        public double SmoothAngle = 60;
        public bool DoubleSided = true;
    }

    public sealed class ImageStats
    {
        public double LowFrac, PartialFrac, DarkWhereClear, Corr;
    }

    public sealed class PlannedTexture
    {
        public string Key;
        public int Id;
        /// <summary>image | solid</summary>
        public string Kind;
        public int Image = -1;
        public string Role;
        public byte[] Tint;      // RGBA multiplier baked into the image, or null
        public byte[] Colour;    // RGBA for solid textures
        public string FileName;
    }

    public sealed class PlannedMaterial
    {
        public int Source;
        public string Name, CleanName, Preset, Reason;
        public double? AlphaRef;
        public Dictionary<string, int> SrcTextures;
        public double[] BaseColor;
        public bool DoubleSided;
        public Dictionary<string, PlannedTexture> Slots = new Dictionary<string, PlannedTexture>();
    }

    public sealed class ConversionPlan
    {
        public HmConvertSettings Settings;
        public string Mode, Note = "";
        public List<SourcePart> Parts;
        public List<PlannedMaterial> Materials;
        public List<PlannedTexture> Textures = new List<PlannedTexture>();
        public V3 BoundsMin, BoundsMax, BoundsSize;
        /// <summary>Skeleton of the skinned parts (0 = none): written to the MDL header.</summary>
        public uint SkeletonKey;
        public int SkeletonJoints;
    }

    public sealed class BuiltTexture
    {
        public string FileName, Path, Format, Role;
        public byte[] Bytes;
        public int Width, Height, Mips;
        public RgbaImage Preview;
        public PlannedTexture Source;
    }

    public sealed class BuildResult
    {
        public byte[] Mdl;
        public HmModel Model;
        public List<BuiltTexture> Textures = new List<BuiltTexture>();
        public List<string> Log = new List<string>();
        public List<string> Errors = new List<string>();
        public List<string> MeshNames = new List<string>();
    }

    public static class HmConvert
    {
        private static readonly Dictionary<string, string> Suffix = new Dictionary<string, string>
        {
            ["colourMap"] = "_c", ["emissiveMap"] = "_c", ["normalMap"] = "_n", ["specularMap"] = "_s", ["envMap"] = "_env",
        };

        /// <summary>glTF joint name -> Home joint hash ('j_xxxxxxxx' is how unnamed Home joints are exported; other names hash lower-case; any 'namespace:' / '|path|' prefix is dropped).</summary>
        public static uint JointHash(string nm)
        {
            string s = (nm ?? "").Split(':', '|').Last().Trim().ToLowerInvariant();
            var m = Regex.Match(s, "^j_([0-9a-f]{8})$");
            return m.Success ? uint.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) : HmCore.HashName(s);
        }

        public static string CleanName(string s)
        {
            string n = string.IsNullOrEmpty(s) ? "mat" : s;
            n = Regex.Replace(n, "^\\d+_", "");
            n = Regex.Replace(n, "^xml_\\d+_", "", RegexOptions.IgnoreCase);
            n = Regex.Replace(n, "SG$", "");
            n = Regex.Replace(n, "_mesh\\d+$", "", RegexOptions.IgnoreCase);
            n = Regex.Replace(n, "[^A-Za-z0-9_]+", "_");
            n = Regex.Replace(n, "_+", "_");
            n = Regex.Replace(n, "^_|_$", "");
            return n.Length > 0 ? n : "mat";
        }

        public static string SafeFileName(string s) => Regex.Replace(s ?? "", "[^A-Za-z0-9_.\\-]+", "_");

        /// <summary>Classify a colour texture by how its alpha is used.</summary>
        public static ImageStats AnalyzeImage(RgbaImage img)
        {
            var d = img.Data;
            int n = img.Width * img.Height;
            double low = 0, partial = 0, clearN = 0, clearBright = 0, sa = 0, sl = 0, saa = 0, sll = 0, sal = 0;
            int step = Math.Max(1, n / 65536);
            int cnt = 0;
            for (int i = 0; i < n; i += step)
            {
                int o = 4 * i;
                int a = d[o + 3], l = Math.Max(d[o], Math.Max(d[o + 1], d[o + 2]));
                cnt++;
                if (a < 250) low++;
                if (a > 10 && a < 245) partial++;
                if (a < 16) { clearN++; clearBright += l; }
                sa += a; sl += l; saa += (double)a * a; sll += (double)l * l; sal += (double)a * l;
            }
            if (cnt == 0) return new ImageStats { DarkWhereClear = 255 };
            double va = saa / cnt - Math.Pow(sa / cnt, 2), vl = sll / cnt - Math.Pow(sl / cnt, 2);
            double corr = va > 1e-6 && vl > 1e-6 ? (sal / cnt - (sa / cnt) * (sl / cnt)) / Math.Sqrt(va * vl) : 0;
            return new ImageStats { LowFrac = low / cnt, PartialFrac = partial / cnt, DarkWhereClear = clearN > 0 ? clearBright / clearN : 255, Corr = corr };
        }

        private static (string Preset, string Reason) SuggestPreset(SourceMaterial gm, ImageStats stats)
        {
            bool hasColour = gm.Textures.ContainsKey("colourMap"), hasEmissive = gm.Textures.ContainsKey("emissiveMap");
            if (gm.AlphaMode == "OPAQUE" && !(hasEmissive && !hasColour)) return ("default", "material is opaque");
            if (!hasColour && hasEmissive) return ("emissive", "only an emissive texture");
            if (gm.AlphaMode == "BLEND" && gm.BaseColor != null && gm.BaseColor.Length > 3 && gm.BaseColor[3] < 0.99 && (stats == null || stats.LowFrac < 0.02))
                return ("glass", "see-through material (alpha " + gm.BaseColor[3].ToString("0.00", CultureInfo.InvariantCulture) + ")");
            if (stats == null) return (gm.AlphaMode == "MASK" ? "alphatest" : "default", "no colour texture alpha");
            if (stats.LowFrac < 0.02) return ("default", "texture alpha is (almost) fully opaque");
            if (stats.DarkWhereClear < 40 && stats.Corr > 0.5) return ("emissive", "black where transparent, alpha follows brightness (glow overlay)");
            return ("alphatest", "texture has transparent areas (cut-out)");
        }

        private static void Bounds(List<SourcePart> parts, out V3 mn, out V3 mx, out V3 size)
        {
            mn = new V3(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
            mx = new V3(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);
            foreach (var p in parts)
                foreach (var v in p.P)
                    for (int k = 0; k < 3; k++)
                    {
                        if (v[k] < mn[k]) mn[k] = v[k];
                        if (v[k] > mx[k]) mx[k] = v[k];
                    }
            size = mx - mn;
        }

        private static double MaxOf(V3 v) => Math.Max(v.X, Math.Max(v.Y, v.Z));

        /// <summary>Decide transform mode, material presets and texture names. <paramref name="imageStats"/>(image index) may return null.</summary>
        public static ConversionPlan Plan(SourceDoc doc, HmConvertSettings s, Func<int, ImageStats> imageStats)
        {
            s ??= new HmConvertSettings();
            string mode = s.Transform, note = "";
            if (mode == "auto")
            {
                bool Plausible(List<SourcePart> ps)
                {
                    Bounds(ps, out _, out _, out var size);
                    double m = MaxOf(size);
                    return m > 0.05 && m < 200;
                }
                var full = doc.Collect("full");
                if (Plausible(full)) mode = "full";
                else
                {
                    var ns = doc.Collect("noscale");
                    if (Plausible(ns))
                    {
                        mode = "noscale";
                        Bounds(full, out _, out _, out var fs);
                        note = "node scale ignored: applying it made the model " + MaxOf(fs).ToString("0.####", CultureInfo.InvariantCulture) + " units across";
                    }
                    else mode = "full";
                }
            }
            var parts = doc.Collect(mode).Where(p => p.Tris.Count > 0).ToList();
            var usedMats = parts.Select(p => p.Material).Distinct().ToList();
            var materials = usedMats.Select(mi =>
            {
                var gm = mi >= 0 && mi < doc.Materials.Count ? doc.Materials[mi] : new SourceMaterial { Index = -1, Name = "default", AlphaMode = "OPAQUE", AlphaCutoff = 0.5 };
                ImageStats stats = gm.Textures.TryGetValue("colourMap", out int ci) && imageStats != null ? imageStats(ci) : null;
                var (preset, reason) = SuggestPreset(gm, stats);
                return new PlannedMaterial
                {
                    Source = mi,
                    Name = gm.Name,
                    CleanName = CleanName(gm.Name),
                    Preset = preset,
                    Reason = reason,
                    AlphaRef = gm.AlphaMode == "MASK" ? gm.AlphaCutoff : (double?)null,
                    SrcTextures = gm.Textures,
                    BaseColor = gm.BaseColor ?? new double[] { 1, 1, 1, 1 },
                    DoubleSided = gm.DoubleSided,
                };
            }).ToList();
            var pl = new ConversionPlan { Settings = s, Mode = mode, Note = note, Parts = parts, Materials = materials, SkeletonKey = doc.SkeletonKey ?? 0, SkeletonJoints = doc.SkeletonJoints };
            AssignTextures(pl);
            Bounds(parts, out pl.BoundsMin, out pl.BoundsMax, out pl.BoundsSize);
            return pl;
        }

        private static byte[] ToRgba8(double[] c) => c.Take(4).Select(v => (byte)HmCore.JsRound(Math.Max(0, Math.Min(1, v)) * 255)).ToArray();

        /// <summary>(Re)compute the output texture list from the materials' presets. Keeps names already given.</summary>
        public static void AssignTextures(ConversionPlan pl)
        {
            var old = pl.Textures.Where(t => t.Key != null).GroupBy(t => t.Key).ToDictionary(g => g.Key, g => g.First().FileName);
            var tex = new List<PlannedTexture>();
            var byKey = new Dictionary<string, PlannedTexture>();
            var names = new HashSet<string>();
            var s = pl.Settings;
            string Uniq(string b)
            {
                string n = b;
                int k = 2;
                while (names.Contains(n.ToLowerInvariant()))
                    n = Regex.Replace(b, "(\\.dds)$", "_" + (k++) + "$1", RegexOptions.IgnoreCase);
                names.Add(n.ToLowerInvariant());
                return n;
            }
            PlannedTexture Add(PlannedTexture def)
            {
                if (byKey.TryGetValue(def.Key, out var existing)) return existing;
                def.Id = tex.Count;
                def.FileName = Uniq(old.TryGetValue(def.Key, out var kept) && kept != null ? kept : def.FileName);
                tex.Add(def);
                byKey[def.Key] = def;
                return def;
            }
            foreach (var m in pl.Materials)
            {
                var p = HmCore.Presets[m.Preset];
                m.Slots = new Dictionary<string, PlannedTexture>();
                foreach (var slot in p.Slots)
                {
                    int img = -1;
                    if (m.SrcTextures.TryGetValue(slot, out int si)) img = si;
                    if (slot == "emissiveMap" && img < 0 && m.SrcTextures.TryGetValue("colourMap", out int ci)) img = ci;
                    if (slot == "colourMap" && img < 0 && m.SrcTextures.TryGetValue("emissiveMap", out int ei) && m.Preset != "default") img = ei;
                    byte[] tint = null;
                    if (slot == "colourMap" && m.BaseColor != null)
                    {
                        bool tinted = false;
                        for (int i = 0; i < m.BaseColor.Length && i < 4; i++)
                        {
                            double v = m.BaseColor[i];
                            if (Math.Abs(v - 1) > 0.02 && (i < 3 || v < 1)) tinted = true;
                        }
                        if (tinted) tint = ToRgba8(m.BaseColor);
                    }
                    if (img >= 0)
                    {
                        // the material's colour factor is baked into its own texture (Home has no tint)
                        string key = "img" + img + ":" + slot + (tint != null ? ":tint" + string.Join("_", tint) : "");
                        m.Slots[slot] = Add(new PlannedTexture { Key = key, Kind = "image", Image = img, Role = slot, Tint = tint, FileName = SafeFileName(m.CleanName + Suffix[slot] + ".dds") });
                    }
                    else if (slot == "colourMap" || slot == "emissiveMap")
                    {
                        var c = ToRgba8(m.BaseColor);
                        m.Slots[slot] = Add(new PlannedTexture { Key = "solid" + string.Join("_", c), Kind = "solid", Colour = c, Role = slot, FileName = SafeFileName(m.CleanName + "_c.dds") });
                    }
                    else if (s.MissingMaps == "generate" && slot == "normalMap")
                    {
                        m.Slots[slot] = Add(new PlannedTexture { Key = "flat_n", Kind = "solid", Colour = new byte[] { 128, 128, 255, 255 }, Role = slot, FileName = SafeFileName(s.ModelName + "_flat_n.dds") });
                    }
                    else if (s.MissingMaps == "generate" && slot == "specularMap")
                    {
                        m.Slots[slot] = Add(new PlannedTexture { Key = "flat_s", Kind = "solid", Colour = new byte[] { 40, 40, 40, 255 }, Role = slot, FileName = SafeFileName(s.ModelName + "_flat_s.dds") });
                    }
                    else m.Slots[slot] = null;   // generic placeholder path
                }
            }
            pl.Textures = tex;
        }

        public static string ResolvePrefix(HmConvertSettings s)
        {
            string pre = (s.TexPrefix ?? "").Replace("{name}", string.IsNullOrEmpty(s.ModelName) ? "MODEL" : s.ModelName).Replace('/', '\\');
            if (pre.Length > 0 && !pre.EndsWith("\\")) pre += "\\";
            return pre;
        }

        /// <summary>Home path of a texture (Home paths are upper case).</summary>
        public static string TexPath(HmConvertSettings s, PlannedTexture t) => (ResolvePrefix(s) + t.FileName).ToUpperInvariant();

        /// <summary>
        /// Build the MDL and its textures. <paramref name="image"/>(index) returns decoded source images (null if unreadable).
        /// With <paramref name="encode"/> false, textures are only resized (for exporting to other formats).
        /// </summary>
        public static BuildResult Build(ConversionPlan pl, Func<int, RgbaImage> image, bool encode = true)
        {
            var s = pl.Settings;
            var res = new BuildResult();
            var log = res.Log;
            var model = new HmModel { Version = s.Version };
            var matIndex = new Dictionary<int, int>();
            foreach (var m in pl.Materials)
            {
                var tx = new Dictionary<string, string>();
                foreach (var kv in m.Slots)
                    if (kv.Value != null) tx[kv.Key] = TexPath(s, kv.Value);
                double? alphaRef = m.Preset == "alphatest" ? (m.AlphaRef ?? 0.5) : (double?)null;
                var mt = HmCore.MakeMaterial(m.Preset, tx, m.Name, alphaRef, s.Version >= 0x0104 ? 0u : 0xFF000000u);
                matIndex[m.Source] = model.Materials.Count;
                model.Materials.Add(mt);
            }

            // geometry
            double cosLim = Math.Cos(s.SmoothAngle * Math.PI / 180);
            var doubled = new List<string>();
            bool anySkin = false;
            int skinnedParts = 0;
            var skinDropped = new List<string>();
            bool upZ = s.UpAxis == "z";
            foreach (var part in pl.Parts)
            {
                var P = part.P.Select(v => (upZ ? new V3(v.X, v.Z, -v.Y) : v) * s.Scale).ToList();
                var N = part.N?.Select(v => upZ ? new V3(v.X, v.Z, -v.Y) : v).ToList();
                var U = part.U.Select(u => s.FlipV ? new V2(u.U, 1 - u.V) : u).ToList();
                while (U.Count < P.Count) U.Add(new V2(0, 0));
                if (N != null && N.Count < P.Count) N = null;
                var tris = part.Tris.Where(t => t[0] >= 0 && t[1] >= 0 && t[2] >= 0 && t[0] < P.Count && t[1] < P.Count && t[2] < P.Count).ToList();
                if (s.FlipWinding) tris = tris.Select(t => new[] { t[0], t[2], t[1] }).ToList();
                // skin weights stay on the vertices only while the vertex list is unchanged and the mesh isn't rescaled
                HmSkin skin = null;
                if (part.Skin != null && pl.SkeletonKey != 0 && part.Skin.J.Count == P.Count && part.Skin.W.Count == P.Count)
                {
                    if (N == null) skinDropped.Add(part.Name + " (no normals in the file)");
                    else if (upZ || Math.Abs(s.Scale - 1) > 1e-9) skinDropped.Add(part.Name + " (scaled / re-axed, so it would no longer fit the skeleton)");
                    else
                    {
                        skin = new HmSkin();
                        for (int v = 0; v < P.Count; v++)
                        {
                            var w = part.Skin.W[v].Take(4).Concat(Enumerable.Repeat(0.0, 4)).Take(4).Select(x => Math.Max(0, x)).ToArray();
                            var j = part.Skin.J[v].Take(4).Concat(Enumerable.Repeat(0, 4)).Take(4).Select(x => Math.Max(0, Math.Min(255, x))).ToArray();
                            double sum = w.Sum();
                            if (sum > 0) for (int c = 0; c < 4; c++) w[c] /= sum;
                            else { w = new double[] { 1, 0, 0, 0 }; }
                            skin.J.Add(j);
                            skin.W.Add(w);
                        }
                    }
                }
                if (N == null)
                {
                    SmoothNormals(P, tris, cosLim, U, out var P2, out var U2, out var N2, out var T2);
                    P = P2; U = U2; N = N2; tris = T2;
                }
                // winding that disagrees with the normals (common on double-sided cards): follow the normals
                int bad = 0, clear = 0;
                foreach (var t in tris)
                {
                    V3 A = P[t[0]], B = P[t[1]], C = P[t[2]];
                    V3 fn = V3.Cross(B - A, C - A), sn = N[t[0]] + N[t[1]] + N[t[2]];
                    double lf = fn.Length, ls = sn.Length;
                    if (lf < 1e-12 || ls < 1e-6) continue;
                    double cs = V3.Dot(fn, sn) / (lf * ls);
                    if (Math.Abs(cs) < 0.3) continue;   // bent normals (grass cards etc.) say nothing about winding
                    clear++;
                    if (cs < 0) bad++;
                }
                if (clear > 0 && bad > clear / 2.0)
                {
                    tris = tris.Select(t => new[] { t[0], t[2], t[1] }).ToList();
                    log.Add(part.Name + ": triangle winding was opposite to the normals; flipped");
                }
                // double-sided material: add the back faces (Home draws one side only)
                var pm = pl.Materials.FirstOrDefault(m => m.Source == part.Material);
                if (pm != null && pm.DoubleSided && s.DoubleSided && part.Skin == null)
                {
                    int n0 = P.Count;
                    bool keepNormals = clear < tris.Count / 2.0;   // bent normals (grass): back faces keep them so both sides light alike
                    P = P.Concat(P).ToList();
                    U = U.Concat(U).ToList();
                    N = N.Concat(keepNormals ? N : N.Select(v => -v)).ToList();
                    tris = tris.Concat(tris.Select(t => new[] { t[0] + n0, t[2] + n0, t[1] + n0 })).ToList();
                    if (!doubled.Contains(pm.Name)) doubled.Add(pm.Name);
                }
                if (part.Skin != null && skin == null) anySkin = true;
                if (tris.Count == 0) continue;
                if (skin != null) skinnedParts++;
                var meshes = HmCore.BuildMeshes(P, U, N, tris, new HmBuildOptions { Material = matIndex[part.Material], Name = part.Name, Version = s.Version, Skin = skin });
                model.Meshes.AddRange(meshes);
                for (int k = 0; k < meshes.Count; k++) res.MeshNames.Add(meshes.Count > 1 ? part.Name + "_" + (k + 1) : part.Name);
            }
            if (model.Meshes.Count == 0) throw new InvalidOperationException("the model has no triangles to convert");
            if (doubled.Count > 0) log.Add("double-sided material(s) given back faces: " + string.Join(", ", doubled));
            if (skinnedParts > 0)
            {
                model.Skeleton = pl.SkeletonKey;
                model.Word8 = (uint)pl.SkeletonJoints;
                log.Add($"{skinnedParts} part(s) written with skin weights for skeleton key 0x{pl.SkeletonKey:x8} ({pl.SkeletonJoints} joints) - make the matching .SKN from the same file on the ANI / SKN tab");
                if (pl.SkeletonJoints > 256) log.Add("the skeleton has more than 256 joints; Home joint indices are one byte, so joints past 255 can't be used");
            }
            foreach (var d in skinDropped) log.Add("rigged part converted as static: " + d);
            if (anySkin && skinnedParts == 0 && skinDropped.Count == 0) log.Add("the model is rigged; it was converted as a static model in its bind pose");
            if (s.Version < 0x0104)
            {
                var pts = new List<V3>();
                foreach (var me in model.Meshes)
                {
                    var pm = HmCore.MeshAttribute(me, HmCore.HPosition);
                    foreach (int k in new HashSet<int>(me.Indices.Select(x => (int)x))) pts.Add(new V3(pm[k][0], pm[k][1], pm[k][2]));
                }
                model.ModelBound = HmCore.BoundingSphere(pts);
            }
            var mdl = HmCore.WriteMdl(model);
            var errs = HmCore.Verify(mdl);
            if (errs.Count > 0) log.Add("verify: " + string.Join("; ", errs));
            else log.Add($"MDL verified: {model.Meshes.Count} elements, {model.Materials.Count} materials, {mdl.Length:N0} bytes");

            // textures
            foreach (var t in pl.Textures)
            {
                RgbaImage img;
                if (t.Kind == "solid")
                {
                    img = RgbaImage.Solid(4, 4, t.Colour);
                }
                else
                {
                    img = image(t.Image);
                    if (img == null)
                    {
                        log.Add($"texture {t.FileName}: the source image could not be read; a grey texture was used instead");
                        img = RgbaImage.Solid(4, 4, new byte[] { 128, 128, 128, 255 });
                    }
                    if (t.Tint != null)
                    {
                        var d = (byte[])img.Data.Clone();
                        var c = t.Tint;
                        for (int i = 0; i < d.Length; i += 4)
                        {
                            d[i] = (byte)(d[i] * c[0] / 255);
                            d[i + 1] = (byte)(d[i + 1] * c[1] / 255);
                            d[i + 2] = (byte)(d[i + 2] * c[2] / 255);
                            d[i + 3] = (byte)(d[i + 3] * c[3] / 255);
                        }
                        img = new RgbaImage(img.Width, img.Height, d);
                    }
                    if (t.Role == "normalMap" && s.FlipNormalGreen)
                    {
                        var d = (byte[])img.Data.Clone();
                        for (int i = 1; i < d.Length; i += 4) d[i] = (byte)(255 - d[i]);
                        img = new RgbaImage(img.Width, img.Height, d);
                    }
                }
                string fmt = t.Role == "normalMap" && s.DdsFormat == "auto" ? "DXT1" : s.DdsFormat;
                if (!encode)
                {
                    var im = Dds.Prepare(img, s.Pow2, s.MaxSize);
                    res.Textures.Add(new BuiltTexture { FileName = t.FileName, Path = TexPath(s, t), Width = im.Width, Height = im.Height, Preview = im, Role = t.Role, Source = t });
                    continue;
                }
                var e = Dds.Encode(img, fmt, s.Mips, s.Pow2, s.MaxSize);
                res.Textures.Add(new BuiltTexture { FileName = t.FileName, Path = TexPath(s, t), Bytes = e.Bytes, Format = e.Format, Width = e.Width, Height = e.Height, Mips = e.Mips, Preview = e.Image, Role = t.Role, Source = t });
                log.Add($"texture {t.FileName}: {e.Format} {e.Width}x{e.Height}, {e.Mips} mips");
            }
            res.Mdl = mdl;
            res.Model = model;
            res.Errors = errs;
            return res;
        }

        /// <summary>Normals for sources without them (angle-limited smoothing). Splits vertices per corner.</summary>
        private static void SmoothNormals(List<V3> P, List<int[]> tris, double cosLim, List<V2> U,
            out List<V3> P2, out List<V2> U2, out List<V3> N2, out List<int[]> T2)
        {
            (long, long, long) Key(V3 v) => ((long)HmCore.JsRound(v.X * 1e5), (long)HmCore.JsRound(v.Y * 1e5), (long)HmCore.JsRound(v.Z * 1e5));
            var fn = tris.Select(t => V3.Cross(P[t[1]] - P[t[0]], P[t[2]] - P[t[0]])).ToList();
            var fnUnit = fn.Select(f => f.Normalized()).ToList();
            var byPos = new Dictionary<(long, long, long), List<int>>();
            for (int f = 0; f < tris.Count; f++)
                foreach (int v in tris[f])
                {
                    var k = Key(P[v]);
                    if (!byPos.TryGetValue(k, out var l)) byPos[k] = l = new List<int>();
                    l.Add(f);
                }
            var p2 = new List<V3>();
            var u2 = new List<V2>();
            var n2 = new List<V3>();
            var t2 = new List<int[]>();
            var lut = new Dictionary<(int, long, long, long), int>();
            for (int f = 0; f < tris.Count; f++)
            {
                var me = fnUnit[f];
                var nt = new int[3];
                for (int c = 0; c < 3; c++)
                {
                    int v = tris[f][c];
                    var acc = new V3(0, 0, 0);
                    foreach (int g in byPos[Key(P[v])])
                        if (V3.Dot(fnUnit[g], me) >= cosLim) acc += fn[g];
                    var n = acc.Normalized(me);
                    var k = (v, (long)HmCore.JsRound(n.X * 1e4), (long)HmCore.JsRound(n.Y * 1e4), (long)HmCore.JsRound(n.Z * 1e4));
                    if (!lut.TryGetValue(k, out int idx))
                    {
                        idx = p2.Count;
                        lut[k] = idx;
                        p2.Add(P[v]);
                        u2.Add(U[v]);
                        n2.Add(n);
                    }
                    nt[c] = idx;
                }
                t2.Add(nt);
            }
            P2 = p2; U2 = u2; N2 = n2; T2 = t2;
        }
    }
}

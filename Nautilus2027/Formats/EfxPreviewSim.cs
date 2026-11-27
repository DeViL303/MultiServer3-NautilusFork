#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Nautilus2027.Formats.Home;

// A rough particle playback of an EFX for the Preview window. The real effect is a node graph run on the SPUs; only some
// of its settings are understood (emitter placement, particle counts, Shaper curves, gravity, plane collision), so this
// guesses the rest: particles leave the emitter shape (along a cone's axis, out of a sphere, up out of a box), live for the
// time in the group's first update constants, slow down, fall with the GravityForce, fade with the Shaper curve that rises
// and falls, and take their size from another Shaper curve. Each particle's state is worked out from the time alone, so
// pausing and scrubbing work.
namespace Nautilus2027.Formats
{
    public static partial class AtmosEfxConverter
    {
        private const int SimMaxPerGroup = 300, SimMaxTotal = 1500;

        private sealed class SimGroup
        {
            public int Index;
            public string Shape;
            public double[] M;                 // emitter placement (column-major)
            public double[] Q = { 0, 0, 0, 1 };
            public double LifeMin = 1, LifeMax = 2;
            public double Rate = 10;
            public double Speed = 0.5, Drag = 0.3, Turb;
            public double[] Gravity = { 0, 0, 0 };
            public double[] PlaneO, PlaneN;
            public float[] Fade, Size;
            public double BaseSize = 0.3;
            public bool Spin;
            public byte[] TexBytes;
            public RgbaImage TexImage;
        }

        private static readonly RgbaImage SoftDot = MakeSoftDot(32);

        private static RgbaImage MakeSoftDot(int n)
        {
            var img = new RgbaImage(n, n, new byte[n * n * 4]);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    double dx = (x + 0.5) / n * 2 - 1, dy = (y + 0.5) / n * 2 - 1, r = Math.Sqrt(dx * dx + dy * dy);
                    double a = Math.Clamp(1 - r, 0, 1);
                    int o = 4 * (y * n + x);
                    img.Data[o] = 255; img.Data[o + 1] = 244; img.Data[o + 2] = 224; img.Data[o + 3] = (byte)(a * a * 255);
                }
            return img;
        }

        private static float[] SimFloats(JsonNode settings, string key)
        {
            if (!(settings.At(key) is JsonArray a)) return null;
            var r = new float[a.Count];
            for (int i = 0; i < a.Count; i++)
                r[i] = a[i] is JsonValue v && v.TryGetValue(out double d) && double.IsFinite(d) && Math.Abs(d) < 1e30 ? (float)d : 0;
            return r;
        }

        private static bool RisesAndFalls(float[] v)
        {
            if (v == null || v.Length < 3) return false;
            float max = v.Max();
            return max > 1e-4 && v[0] <= max * 0.25 && v[v.Length - 1] <= max * 0.25;
        }

        /// <summary>Particles of the effect's groups as an animation for the preview (null when there is nothing to play).</summary>
        private static PreviewAnimation EfxAnimation(JsonObject efx, ExportScene scene, List<string> info)
        {
            var groups = new List<SimGroup>();
            foreach (var gNode in (efx["groups"] as JsonArray) ?? new JsonArray())
            {
                if (!(gNode is JsonObject g)) continue;
                var stages = g.At("renderStages") as JsonArray;
                if (stages == null || stages.Count == 0) continue;   // controller groups draw nothing
                var sg = new SimGroup { Index = JI(g.At("index"), groups.Count) };
                int count = Math.Clamp(JI(g.At("particleCount"), 50), 1, 100000);
                int maxEm = Math.Max(1, JI(g.At("maxEmission"), 1));

                var emit = (g.At("emission").At("nodes") as JsonArray) ?? new JsonArray();
                var upd = (g.At("update").At("nodes") as JsonArray) ?? new JsonArray();
                var placement = new double[] { 0, 0, 0 };
                var scale = new double[] { 1, 1, 1 };
                foreach (var nd in emit)
                {
                    string fn = JS(nd.At("function"));
                    var st = nd.At("settings");
                    if (sg.Shape == null && HmEfx.IsShapeEmitter(fn) && st.At("origin") != null)
                    {
                        sg.Shape = fn;
                        placement = NodeFloats(st, "origin", 3, new float[3]).Select(x => (double)x).ToArray();
                        sg.Q = NodeFloats(st, "orientation", 4, new float[] { 0, 0, 0, 1 }).Select(x => (double)x).ToArray();
                        scale = NodeFloats(st, "scale", 3, new float[] { 1, 1, 1 }).Select(x => (double)x).ToArray();
                    }
                    else if (fn == "ConstantEmitter" && SimFloats(st, "floats") is float[] cf && cf.Length >= 3 && cf[2] > 0 && cf[2] <= 50 && sg.Speed == 0.5)
                        sg.Speed = cf[2];
                    else if (fn == "RandomAxialRotation") sg.Spin = true;
                }
                double ql = Math.Sqrt(sg.Q.Sum(x => x * x));
                sg.Q = ql > 1e-9 ? sg.Q.Select(x => x / ql).ToArray() : new double[] { 0, 0, 0, 1 };
                sg.M = HmSkeleton.FromQts(sg.Q, placement, scale);

                bool lifeSet = false;
                double groupLife = g.At("lifeTime") is JsonValue lv && lv.TryGetValue(out double gl) && gl > 0.01 && gl < 600 ? gl : 0;
                if (groupLife > 0) { sg.LifeMin = groupLife * 0.8; sg.LifeMax = groupLife; lifeSet = true; }
                var shapers = new List<float[]>();
                bool damping = false;
                foreach (var nd in upd)
                {
                    string fn = JS(nd.At("function"));
                    var st = nd.At("settings");
                    switch (fn)
                    {
                        case "ConstantEmitter":
                            if (!lifeSet && SimFloats(st, "floats") is float[] lf && lf.Length >= 1 && lf[0] > 0.05 && lf[0] <= 60)
                            {
                                double a = lf[0], b = lf.Length >= 2 && lf[1] > 0.05 && lf[1] <= 60 ? lf[1] : lf[0] * 0.7;
                                sg.LifeMin = Math.Min(a, b); sg.LifeMax = Math.Max(a, b);
                                lifeSet = true;
                            }
                            break;
                        case "EmissionTrigger":
                            if (!lifeSet && st.At("maxLifeTime") is JsonValue mv && mv.TryGetValue(out double ml) && ml > 0.05 && ml <= 60)
                            { sg.LifeMin = ml * 0.7; sg.LifeMax = ml; lifeSet = true; }
                            break;
                        case "GravityForce":
                            if (SimFloats(st, "floats") is float[] gf && gf.Length >= 3 && gf.All(x => Math.Abs(x) < 100)) sg.Gravity = new double[] { gf[0], gf[1], gf[2] };
                            break;
                        case "Damping": damping = true; break;
                        case "Turbulence": case "Noise": sg.Turb = 1; break;
                        case "Shaper":
                            if (SimFloats(st, "values") is float[] sv && sv.Length >= 2) shapers.Add(sv);
                            break;
                        case "PlaneCollision":
                            var pn = SimFloats(st, "normal");
                            var po = SimFloats(st, "origin");
                            if (pn != null && po != null && pn.Length >= 3 && po.Length >= 3)
                            {
                                double nl = Math.Sqrt(pn[0] * pn[0] + pn[1] * pn[1] + pn[2] * pn[2]);
                                if (nl > 1e-6) { sg.PlaneN = new[] { pn[0] / nl, pn[1] / nl, pn[2] / nl }; sg.PlaneO = new double[] { po[0], po[1], po[2] }; }
                            }
                            break;
                    }
                }
                sg.Drag = damping ? 1.5 : 0.3;
                sg.Fade = shapers.FirstOrDefault(RisesAndFalls);
                if (sg.Fade != null) { float mx = sg.Fade.Max(); sg.Fade = sg.Fade.Select(x => Math.Clamp(x / mx, 0f, 1f)).ToArray(); }
                sg.Size = shapers.FirstOrDefault(s => !RisesAndFalls(s) && s.All(x => x > 0.003f && x <= 20f));
                double extent = Math.Max(0.05, scale.Select(x => Math.Abs(x)).Max());
                sg.BaseSize = Math.Clamp(extent * 0.35, 0.08, 1.5);
                sg.Turb = sg.Turb > 0 ? Math.Max(0.1, sg.BaseSize * 0.6) : 0;

                double meanLife = (sg.LifeMin + sg.LifeMax) / 2;
                sg.Rate = Math.Min(count / meanLife, maxEm * 30.0);
                sg.Rate = Math.Min(sg.Rate, SimMaxPerGroup / sg.LifeMax);

                // the group's texture: the one its texture card uses, else a soft dot
                var card = scene.Meshes.FirstOrDefault(m => m.Name == $"G{sg.Index}_Texture");
                if (card != null && card.Material >= 0 && card.Material < scene.Materials.Count)
                {
                    int? ti = scene.Materials[card.Material].Colour ?? scene.Materials[card.Material].Emissive;
                    if (ti.HasValue && ti.Value >= 0 && ti.Value < scene.Textures.Count) sg.TexBytes = scene.Textures[ti.Value].Bytes;
                }
                if (sg.TexBytes == null) sg.TexImage = SoftDot;
                groups.Add(sg);
            }
            if (groups.Count == 0) return null;

            // keep the whole effect under the total cap
            double live = groups.Sum(g => g.Rate * g.LifeMax);
            if (live > SimMaxTotal) foreach (var g in groups) g.Rate *= SimMaxTotal / live;

            // box: the emitters and where a sample of particles goes
            var lo = new[] { double.MaxValue, double.MaxValue, double.MaxValue };
            var hi = new[] { double.MinValue, double.MinValue, double.MinValue };
            foreach (var g in groups)
                for (long n = 0; n < 60; n++)
                {
                    double life = Life(g, n);
                    for (int k = 0; k <= 4; k++)
                    {
                        var p = Particle(g, n, life * k / 4.0);
                        for (int c = 0; c < 3; c++) { lo[c] = Math.Min(lo[c], p[c]); hi[c] = Math.Max(hi[c], p[c]); }
                    }
                }
            double pad = groups.Max(g => g.BaseSize);
            for (int c = 0; c < 3; c++) { lo[c] -= pad; hi[c] += pad; }

            double maxLife = groups.Max(g => g.LifeMax);
            var anim = new PreviewAnimation
            {
                Name = "Particles",
                Duration = Math.Clamp(maxLife * 3, 4, 30),
                Loop = true,
                AutoPlay = true,
                Bounds = new[] { lo[0], lo[1], lo[2], hi[0], hi[1], hi[2] },
            };
            anim.Frame = (t, cam) => SimFrame(groups, t, cam);
            anim.Info.Add($"particles: {groups.Count} group(s), about {groups.Sum(g => g.Rate * (g.LifeMin + g.LifeMax) / 2):0} alive - a rough playback: speeds, spread and colours are guessed (the real effect runs a node graph)");
            info.AddRange(groups.Select(g => $"group {g.Index}: {g.Shape ?? "point"}, life {g.LifeMin:0.##}-{g.LifeMax:0.##} s, {g.Rate:0.#} / s{(g.Gravity.Any(x => x != 0) ? $", gravity {g.Gravity[1]:0.##}" : "")}{(g.Fade != null ? ", fades" : "")}{(g.TexBytes == null ? ", no texture found" : "")}").Take(6));
            return anim;
        }

        // ------------------------------------------------------------------ one particle

        private static double Rnd(int group, long n, int k)
        {
            unchecked
            {
                ulong z = (ulong)n * 0x9E3779B97F4A7C15UL + (ulong)(group * 7919 + k * 104729) * 0xBF58476D1CE4E5B9UL + 0x94D049BB133111EBUL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                z ^= z >> 31;
                return (z >> 11) * (1.0 / (1UL << 53));
            }
        }

        private static double Life(SimGroup g, long n) => g.LifeMin + (g.LifeMax - g.LifeMin) * Rnd(g.Index, n, 0);

        private static double[] RandomDir(SimGroup g, long n, int k)
        {
            double z = Rnd(g.Index, n, k) * 2 - 1, a = Rnd(g.Index, n, k + 1) * 2 * Math.PI, r = Math.Sqrt(Math.Max(0, 1 - z * z));
            return new[] { r * Math.Cos(a), z, r * Math.Sin(a) };
        }

        private static double[] Rotate(double[] q, double[] v)
        {
            double x = q[0], y = q[1], z = q[2], w = q[3];
            double tx = 2 * (y * v[2] - z * v[1]), ty = 2 * (z * v[0] - x * v[2]), tz = 2 * (x * v[1] - y * v[0]);
            return new[] { v[0] + w * tx + (y * tz - z * ty), v[1] + w * ty + (z * tx - x * tz), v[2] + w * tz + (x * ty - y * tx) };
        }

        /// <summary>Where particle n is at the given age.</summary>
        private static double[] Particle(SimGroup g, long n, double age)
        {
            double r1 = Rnd(g.Index, n, 1), r2 = Rnd(g.Index, n, 2), r3 = Rnd(g.Index, n, 3);
            double[] local, dir;
            switch (g.Shape)
            {
                case "CubeVolumeEmitter":
                    local = new[] { r1 - 0.5, r2 - 0.5, r3 - 0.5 };
                    dir = null;
                    break;
                case "CylinderVolumeEmitter":
                {
                    double rr = Math.Sqrt(r1) * 0.5, a = r2 * 2 * Math.PI;
                    local = new[] { rr * Math.Cos(a), r3 - 0.5, rr * Math.Sin(a) };
                    dir = null;
                    break;
                }
                case "SphereVolumeEmitter":
                {
                    var d = RandomDir(g, n, 4);
                    double rr = Math.Cbrt(r1);
                    local = new[] { d[0] * rr, d[1] * rr, d[2] * rr };
                    dir = d;
                    break;
                }
                case "SphereSurfaceEmitter":
                {
                    var d = RandomDir(g, n, 4);
                    local = d;
                    dir = d;
                    break;
                }
                case "ConeEmitter":
                {
                    // inside the drawn cone: tip at the origin, opening along +Y (half angle atan 0.5)
                    double ca = 1 - r1 * (1 - Math.Cos(Math.Atan(0.5))), sa = Math.Sqrt(Math.Max(0, 1 - ca * ca)), a = r2 * 2 * Math.PI;
                    local = new double[] { 0, 0, 0 };
                    dir = new[] { sa * Math.Cos(a), ca, sa * Math.Sin(a) };
                    break;
                }
                default:
                    local = new double[] { 0, 0, 0 };
                    dir = null;
                    break;
            }
            if (dir == null)
            {
                // up, with a little spread
                double sp = 0.25, a = r3 * 2 * Math.PI, s = Rnd(g.Index, n, 6) * sp;
                dir = new[] { s * Math.Cos(a), 1, s * Math.Sin(a) };
                double l = Math.Sqrt(dir[0] * dir[0] + 1 + dir[2] * dir[2]);
                dir = dir.Select(x => x / l).ToArray();
            }
            var m = g.M;
            var p0 = new[]
            {
                m[0] * local[0] + m[4] * local[1] + m[8] * local[2] + m[12],
                m[1] * local[0] + m[5] * local[1] + m[9] * local[2] + m[13],
                m[2] * local[0] + m[6] * local[1] + m[10] * local[2] + m[14],
            };
            var wd = g.Shape != null ? Rotate(g.Q, dir) : dir;
            if ((g.Shape == "SphereSurfaceEmitter" || g.Shape == "SphereVolumeEmitter") && g.Gravity.All(x => x == 0))
            {
                // with nothing pulling them down, sphere-emitted particles (smoke, fire, sparkles) mostly rise
                wd = new[] { wd[0] * 0.6, wd[1] * 0.6 + 1, wd[2] * 0.6 };
                double wl = Math.Sqrt(wd[0] * wd[0] + wd[1] * wd[1] + wd[2] * wd[2]);
                wd = new[] { wd[0] / wl, wd[1] / wl, wd[2] / wl };
            }
            double speed = g.Speed * (0.7 + 0.6 * Rnd(g.Index, n, 7));
            // v' = gravity - drag * v, solved for the position
            double k = g.Drag, e = Math.Exp(-k * age), f1 = (1 - e) / k, f2 = (age - f1) / k;
            var p = new double[3];
            for (int c = 0; c < 3; c++) p[c] = p0[c] + wd[c] * speed * f1 + g.Gravity[c] * f2;
            if (g.Turb > 0)
            {
                double amp = g.Turb * Math.Min(1, age);
                for (int c = 0; c < 3; c++)
                    p[c] += amp * Math.Sin(age * (1.3 + c * 0.4) + Rnd(g.Index, n, 10 + c) * 6.283) * 0.5;
            }
            if (g.PlaneN != null)
            {
                double d = (p[0] - g.PlaneO[0]) * g.PlaneN[0] + (p[1] - g.PlaneO[1]) * g.PlaneN[1] + (p[2] - g.PlaneO[2]) * g.PlaneN[2];
                if (d < 0) for (int c = 0; c < 3; c++) p[c] -= d * g.PlaneN[c];
            }
            return p;
        }

        private static double Curve(float[] v, double u)
        {
            if (v == null || v.Length == 0) return 1;
            double x = Math.Clamp(u, 0, 1) * (v.Length - 1);
            int i = Math.Min((int)x, v.Length - 2);
            if (i < 0) return v[0];
            double f = x - i;
            return v[i] + (v[i + 1] - v[i]) * f;
        }

        // ------------------------------------------------------------------ a frame

        private static List<PreviewMesh> SimFrame(List<SimGroup> groups, double t, PreviewCamera cam)
        {
            var parts = new List<(double Depth, SimGroup G, double[] P, double Half, double Angle, uint Alpha)>();
            foreach (var g in groups)
            {
                // particles born in (t - longest life, t]; earlier births (before 0) count too, so the effect is already running
                long first = (long)Math.Floor((t - g.LifeMax) * g.Rate), last = (long)Math.Floor(t * g.Rate);
                for (long n = first; n <= last; n++)
                {
                    double born = n / g.Rate, age = t - born, life = Life(g, n);
                    if (age < 0 || age >= life) continue;
                    double u = age / life;
                    double fade = g.Fade != null ? Curve(g.Fade, u) : Math.Min(1, Math.Min(u * 8, (1 - u) * 4));
                    uint alpha = (uint)Math.Clamp(Math.Round(fade * 16) * 16, 0, 255);
                    if (alpha < 8) continue;
                    double size = (g.Size != null ? Curve(g.Size, u) : g.BaseSize) * (0.8 + 0.4 * Rnd(g.Index, n, 8));
                    size *= 0.5 + 0.5 * fade;
                    var p = Particle(g, n, age);
                    double depth = (p[0] - cam.Eye[0]) * cam.Forward[0] + (p[1] - cam.Eye[1]) * cam.Forward[1] + (p[2] - cam.Eye[2]) * cam.Forward[2];
                    if (depth <= 0) continue;   // behind the camera
                    double angle = g.Spin ? Rnd(g.Index, n, 9) * 2 * Math.PI + age * (Rnd(g.Index, n, 11) - 0.5) : 0;
                    parts.Add((depth, g, p, size / 2, angle, alpha));
                }
            }
            // far first, so see-through sprites blend over what's behind them
            parts.Sort((a, b) => b.Depth.CompareTo(a.Depth));

            var outL = new List<PreviewMesh>();
            var nf = new[] { (float)-cam.Forward[0], (float)-cam.Forward[1], (float)-cam.Forward[2] };
            List<float> P = null, N = null, UV = null;
            List<int> T = null;
            SimGroup curG = null;
            uint curA = 0;
            void Flush()
            {
                if (P == null || P.Count == 0) return;
                outL.Add(new PreviewMesh
                {
                    Name = "G" + curG.Index + "_Particles",
                    P = P.ToArray(), N = N.ToArray(), UV = UV.ToArray(), Tris = T.ToArray(),
                    ImageBytes = curG.TexBytes, Image = curG.TexImage,
                    Colour = (curA << 24) | 0xFFFFFF,
                });
            }
            foreach (var (_, pg, pp, h, pang, palpha) in parts)
            {
                if (pg != curG || palpha != curA)
                {
                    Flush();
                    curG = pg; curA = palpha;
                    P = new List<float>(); N = new List<float>(); UV = new List<float>(); T = new List<int>();
                }
                double ca = Math.Cos(pang) * h, sa = Math.Sin(pang) * h;
                var rx = new[] { cam.Right[0] * ca + cam.Up[0] * sa, cam.Right[1] * ca + cam.Up[1] * sa, cam.Right[2] * ca + cam.Up[2] * sa };
                var uy = new[] { cam.Up[0] * ca - cam.Right[0] * sa, cam.Up[1] * ca - cam.Right[1] * sa, cam.Up[2] * ca - cam.Right[2] * sa };
                int b0 = P.Count / 3;
                for (int corner = 0; corner < 4; corner++)
                {
                    double sx = corner == 0 || corner == 3 ? -1 : 1, sy = corner < 2 ? 1 : -1;
                    for (int c = 0; c < 3; c++) P.Add((float)(pp[c] + rx[c] * sx + uy[c] * sy));
                    N.AddRange(nf);
                    UV.Add(sx < 0 ? 0f : 1f); UV.Add(sy > 0 ? 0f : 1f);
                }
                T.AddRange(new[] { b0, b0 + 1, b0 + 2, b0, b0 + 2, b0 + 3 });
            }
            Flush();
            return outL;
        }
    }
}

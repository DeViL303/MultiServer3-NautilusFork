#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nautilus2027.Formats.Home;

namespace Nautilus2027.Formats
{
    /// <summary>
    /// Preview of skeletons and animation clips: the bones drawn as tapered ribbons with a marker on each joint, playing the
    /// clip, and the skinned model found beside the file (see-through, so the bones show) moving with it.
    /// </summary>
    public static class AnimationPreview
    {
        private const int TextureSize = 1024;

        private const uint BoneColour = 0xFF3CC8FF, JointColour = 0xFFFFD040, StillJointColour = 0xFF9AA0A8, RootColour = 0xFFFF5050;
        private const uint SkinAlpha = 0x99;

        /// <summary>An ANI clip (or the first animation of a GLB / FBX) playing on its skeleton.</summary>
        public static PreviewData ForAnimation(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            var notes = new List<string>();
            ExportScene scene;
            string label;
            if (Path.GetExtension(path).Equals(".ani", StringComparison.OrdinalIgnoreCase))
            {
                var clip = HmAnimation.Read(File.ReadAllBytes(path));
                string sknPath = AnimationConverter.FindSkeletonFor(path, clip, out var sk);
                scene = sk != null ? AnimationConverter.SkeletonScene(path, sk, name, true, TextureSize, notes) : new ExportScene { Name = name };
                if (sknPath != null) notes.Insert(0, $"skeleton: {Path.GetFileName(sknPath)} ({sk.Joints.Count} joints){(HomeObjects.IsBuiltIn(sknPath) ? ", " + HomeObjects.BuiltInLabel(sknPath) : "")}");
                AnimationConverter.AddClip(scene, clip, name, notes);
                label = $"{name}: {clip.Duration:0.###} s, {clip.Items.Count} joint(s) animated";
            }
            else
            {
                var doc = AnimationConverter.ParseOpen(path);
                if (doc.Clips.Count == 0) throw new InvalidDataException("the file has no animations");
                scene = new ExportScene { Name = name };
                AnimationConverter.AddSkeleton(scene, doc.Skeleton);
                var (an, clip) = doc.Clips[0];
                AnimationConverter.AddClip(scene, clip, an, notes);
                label = $"{an}: {clip.Duration:0.###} s, {clip.Items.Count} joint(s) animated";
                if (doc.Clips.Count > 1) notes.Insert(0, $"showing the first of {doc.Clips.Count} animations ({string.Join(", ", doc.Clips.Skip(1).Take(5).Select(c => c.Name))}{(doc.Clips.Count > 6 ? ", ..." : "")})");
                notes.AddRange(doc.Notes.Take(3));
            }
            var data = Build(scene, scene.Animations.LastOrDefault(), Path.GetFileName(path), notes);
            data.Animation.Info.Insert(0, label);
            return data;
        }

        /// <summary>A skeleton in its bind pose (with the skinned model found beside it).</summary>
        public static PreviewData ForSkeleton(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            var notes = new List<string>();
            ExportScene scene;
            if (Path.GetExtension(path).Equals(".skn", StringComparison.OrdinalIgnoreCase))
            {
                var sk = HmSkeleton.Read(File.ReadAllBytes(path));
                scene = AnimationConverter.SkeletonScene(path, sk, name, true, TextureSize, notes);
                notes.Insert(0, $"skeleton key 0x{sk.Key:x8}");
            }
            else
            {
                var doc = AnimationConverter.ParseOpen(path);
                scene = new ExportScene { Name = name };
                AnimationConverter.AddSkeleton(scene, doc.Skeleton);
                notes.AddRange(doc.Notes.Take(3));
            }
            var data = Build(scene, null, Path.GetFileName(path), notes);
            data.Animation.Info.Insert(0, $"{scene.Joints.Count} joint(s), bind pose");
            return data;
        }

        // ------------------------------------------------------------------ building

        private sealed class Rig
        {
            public int Count;
            public int[] Parent;
            public double[][] T, R, S;
            public double[][] InverseBind;
            public bool[] Animated;
            public List<(ExportAnimChannel C, int Comps)> Channels = new List<(ExportAnimChannel, int)>();
        }

        private static PreviewData Build(ExportScene scene, ExportAnimation anim, string title, List<string> notes)
        {
            var rig = new Rig { Count = scene.Joints.Count };
            int n = rig.Count;
            rig.Parent = new int[n];
            rig.T = new double[n][]; rig.R = new double[n][]; rig.S = new double[n][];
            rig.InverseBind = new double[n][];
            rig.Animated = new bool[n];
            for (int j = 0; j < n; j++)
            {
                var jt = scene.Joints[j];
                rig.Parent[j] = jt.Parent >= 0 && jt.Parent < j ? jt.Parent : -1;   // parents come first
                rig.T[j] = Pad(jt.T, 3, 0);
                rig.R[j] = Pad(jt.R, 4, 0, 1);
                rig.S[j] = Pad(jt.S, 3, 1);
                rig.InverseBind[j] = jt.InverseBind != null && jt.InverseBind.Length == 16 ? jt.InverseBind : null;
            }
            double duration = 0;
            if (anim != null)
            {
                foreach (var c in anim.Channels)
                {
                    if (c.Joint < 0 || c.Joint >= n || c.Times == null || c.Times.Length == 0 || c.Values == null) continue;
                    int comps = c.Path == "rotation" ? 4 : c.Path == "translation" || c.Path == "scale" ? 3 : 0;
                    if (comps == 0 || c.Values.Length < c.Times.Length * comps) continue;
                    if (c.Tangents != null && c.Tangents.Length < c.Times.Length * comps) c.Tangents = null;
                    rig.Channels.Add((c, comps));
                    if (c.Times.Length > 1) rig.Animated[c.Joint] = true;
                    duration = Math.Max(duration, c.Times[c.Times.Length - 1]);
                }
                if (anim.Extras != null && anim.Extras.TryGetValue("duration", out var dv) && dv is float df && df > 0) duration = Math.Max(duration, df);
            }

            // the model: skinned meshes move with the clip (drawn by the animation), the rest stays as a layer
            var data = new PreviewData { Title = title };
            var still = new PreviewLayer { Name = "Model" };
            var skinned = new List<(PreviewMesh Mesh, ExportMesh Src)>();
            foreach (var m in scene.Meshes)
            {
                if (m.P == null || m.Tris == null || m.Tris.Length == 0) continue;
                var pm = new PreviewMesh { Name = m.Name, P = m.P, N = m.N, UV = m.UV, Tris = m.Tris };
                if (m.Material >= 0 && m.Material < scene.Materials.Count)
                {
                    var mt = scene.Materials[m.Material];
                    int? ti = mt.Colour ?? mt.Emissive;
                    if (ti.HasValue && ti.Value >= 0 && ti.Value < scene.Textures.Count) pm.ImageBytes = scene.Textures[ti.Value].Bytes;
                }
                int nv = m.P.Length / 3;
                bool skin = m.Joints != null && m.Weights != null && m.Joints.Length >= nv * 4 && m.Weights.Length >= nv * 4;
                if (skin) skinned.Add((pm, m));
                else still.Meshes.Add(pm);
            }
            if (still.Meshes.Count > 0) data.Layers.Add(still);

            // box: the bind pose and a few frames of the clip
            var lo = new[] { double.MaxValue, double.MaxValue, double.MaxValue };
            var hi = new[] { double.MinValue, double.MinValue, double.MinValue };
            void Grow(double x, double y, double z)
            {
                lo[0] = Math.Min(lo[0], x); lo[1] = Math.Min(lo[1], y); lo[2] = Math.Min(lo[2], z);
                hi[0] = Math.Max(hi[0], x); hi[1] = Math.Max(hi[1], y); hi[2] = Math.Max(hi[2], z);
            }
            for (int k = 0; k <= 8; k++)
            {
                var w = Pose(rig, duration * k / 8.0, rig.Channels.Count > 0);
                foreach (var m in w) Grow(m[12], m[13], m[14]);
            }
            foreach (var (sm, _) in skinned)
                for (int i = 0; i + 2 < sm.P.Length; i += 3) Grow(sm.P[i], sm.P[i + 1], sm.P[i + 2]);
            if (n == 0 && skinned.Count == 0) throw new InvalidDataException("there are no joints to show");
            if (lo[0] > hi[0]) { lo = new double[] { -0.5, -0.5, -0.5 }; hi = new double[] { 0.5, 0.5, 0.5 }; }
            double size = Math.Max(0.05, Math.Sqrt(Sq(hi[0] - lo[0]) + Sq(hi[1] - lo[1]) + Sq(hi[2] - lo[2])));

            bool moving = rig.Channels.Count > 0;
            var animation = new PreviewAnimation
            {
                Name = anim?.Name ?? "Skeleton",
                Duration = moving && duration > 0 ? duration : 1,
                Loop = true,
                AutoPlay = moving,
                ShowControls = moving,
                Bounds = new[] { lo[0], lo[1], lo[2], hi[0], hi[1], hi[2] },
            };
            animation.Frame = (t, cam) => Frame(rig, skinned, t, cam, size, moving);
            int animatedJoints = rig.Animated.Count(a => a);
            if (n > 0) animation.Info.Add($"{n} joint(s){(moving ? $", {animatedJoints} moving (yellow), {n - animatedJoints} still (grey)" : "")}; the root is red");
            if (skinned.Count > 0) animation.Info.Add($"{skinned.Count} skinned mesh(es) {(moving ? "move with the clip" : "shown")} see-through, so the bones show");
            animation.Info.AddRange(notes.Distinct().Take(5));
            data.Animation = animation;
            return data;
        }

        private static double Sq(double x) => x * x;

        private static double[] Pad(double[] v, int n, double fill, double last = double.NaN)
        {
            var r = new double[n];
            for (int i = 0; i < n; i++) r[i] = v != null && i < v.Length && double.IsFinite(v[i]) ? v[i] : (i == n - 1 && !double.IsNaN(last) ? last : fill);
            return r;
        }

        /// <summary>World matrices of the joints at time t.</summary>
        private static double[][] Pose(Rig rig, double t, bool animate)
        {
            int n = rig.Count;
            var T = new double[n][]; var R = new double[n][]; var S = new double[n][];
            for (int j = 0; j < n; j++) { T[j] = rig.T[j]; R[j] = rig.R[j]; S[j] = rig.S[j]; }
            if (animate)
                foreach (var (c, comps) in rig.Channels)
                {
                    var v = HmExport.SampleChannel(c, comps, t);
                    if (v.Any(x => !double.IsFinite(x))) continue;
                    if (c.Path == "rotation") R[c.Joint] = v;
                    else if (c.Path == "translation") T[c.Joint] = v;
                    else S[c.Joint] = v;
                }
            var w = new double[n][];
            for (int j = 0; j < n; j++)
            {
                var l = HmSkeleton.FromQts(R[j], T[j], S[j]);
                int p = rig.Parent[j];
                w[j] = p >= 0 ? Mat4.Mul(w[p], l) : l;
            }
            return w;
        }

        private static List<PreviewMesh> Frame(Rig rig, List<(PreviewMesh Mesh, ExportMesh Src)> skinned, double t, PreviewCamera cam, double size, bool animate)
        {
            var world = Pose(rig, t, animate);
            int n = rig.Count;
            var outL = new List<PreviewMesh>();
            var nf = new[] { (float)-cam.Forward[0], (float)-cam.Forward[1], (float)-cam.Forward[2] };

            // bones: a ribbon from each parent to its child, wide at the parent end, turned to face the camera
            var bp = new List<float>(); var bn = new List<float>(); var bt = new List<int>();
            void V(List<float> p, List<float> nn, double x, double y, double z) { p.Add((float)x); p.Add((float)y); p.Add((float)z); nn.AddRange(nf); }
            double minW = size * 0.0025, maxW = size * 0.018;
            for (int j = 0; j < n; j++)
            {
                int p = rig.Parent[j];
                if (p < 0) continue;
                double ax = world[p][12], ay = world[p][13], az = world[p][14], bx = world[j][12], by = world[j][13], bz = world[j][14];
                double dx = bx - ax, dy = by - ay, dz = bz - az, len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (len < 1e-6) continue;
                // side = bone x (towards the camera)
                double vx = (ax + bx) / 2 - cam.Eye[0], vy = (ay + by) / 2 - cam.Eye[1], vz = (az + bz) / 2 - cam.Eye[2];
                double sx = dy * vz - dz * vy, sy = dz * vx - dx * vz, sz = dx * vy - dy * vx, sl = Math.Sqrt(sx * sx + sy * sy + sz * sz);
                if (sl < 1e-12) { sx = cam.Right[0]; sy = cam.Right[1]; sz = cam.Right[2]; sl = 1; }
                double wa = Math.Clamp(len * 0.12, minW, maxW), wb = wa * 0.25;
                sx /= sl; sy /= sl; sz /= sl;
                int b0 = bp.Count / 3;
                V(bp, bn, ax + sx * wa, ay + sy * wa, az + sz * wa);
                V(bp, bn, ax - sx * wa, ay - sy * wa, az - sz * wa);
                V(bp, bn, bx - sx * wb, by - sy * wb, bz - sz * wb);
                V(bp, bn, bx + sx * wb, by + sy * wb, bz + sz * wb);
                bt.AddRange(new[] { b0, b0 + 1, b0 + 2, b0, b0 + 2, b0 + 3 });
            }
            if (bt.Count > 0) outL.Add(new PreviewMesh { Name = "Bones", P = bp.ToArray(), N = bn.ToArray(), Tris = bt.ToArray(), Colour = BoneColour, Emissive = true });

            // joints: small camera-facing diamonds (root red, moving yellow, still grey)
            var groups = new Dictionary<uint, (List<float> P, List<float> N, List<int> T)>();
            double r = Math.Max(size * 0.006, minW * 1.5);
            for (int j = 0; j < n; j++)
            {
                uint col = rig.Parent[j] < 0 ? RootColour : rig.Animated[j] ? JointColour : StillJointColour;
                if (!groups.TryGetValue(col, out var g)) groups[col] = g = (new List<float>(), new List<float>(), new List<int>());
                double x = world[j][12], y = world[j][13], z = world[j][14];
                double rr = rig.Parent[j] < 0 ? r * 1.6 : r;
                int k0 = g.P.Count / 3;
                V(g.P, g.N, x + cam.Up[0] * rr, y + cam.Up[1] * rr, z + cam.Up[2] * rr);
                V(g.P, g.N, x + cam.Right[0] * rr, y + cam.Right[1] * rr, z + cam.Right[2] * rr);
                V(g.P, g.N, x - cam.Up[0] * rr, y - cam.Up[1] * rr, z - cam.Up[2] * rr);
                V(g.P, g.N, x - cam.Right[0] * rr, y - cam.Right[1] * rr, z - cam.Right[2] * rr);
                g.T.AddRange(new[] { k0, k0 + 1, k0 + 2, k0, k0 + 2, k0 + 3 });
            }
            foreach (var kv in groups)
                outL.Add(new PreviewMesh { Name = "Joints", P = kv.Value.P.ToArray(), N = kv.Value.N.ToArray(), Tris = kv.Value.T.ToArray(), Colour = kv.Key, Emissive = true });

            // skinned meshes last (see-through over the bones): linear blend skinning with the joints' skin matrices
            if (skinned.Count > 0)
            {
                var skinM = new double[n][];
                for (int j = 0; j < n; j++) skinM[j] = rig.InverseBind[j] != null ? Mat4.Mul(world[j], rig.InverseBind[j]) : null;
                foreach (var (pm, src) in skinned)
                {
                    int nv = pm.P.Length / 3;
                    var P = new float[nv * 3];
                    var N = pm.N != null && pm.N.Length >= nv * 3 ? new float[nv * 3] : null;
                    for (int v = 0; v < nv; v++)
                    {
                        double x = pm.P[3 * v], y = pm.P[3 * v + 1], z = pm.P[3 * v + 2];
                        double ox = 0, oy = 0, oz = 0, nx = 0, ny = 0, nz = 0, wsum = 0;
                        for (int c = 0; c < 4; c++)
                        {
                            int j = src.Joints[4 * v + c];
                            double w = src.Weights[4 * v + c];
                            if (!(w > 0) || j < 0 || j >= n || skinM[j] == null) continue;
                            var m = skinM[j];
                            ox += w * (m[0] * x + m[4] * y + m[8] * z + m[12]);
                            oy += w * (m[1] * x + m[5] * y + m[9] * z + m[13]);
                            oz += w * (m[2] * x + m[6] * y + m[10] * z + m[14]);
                            if (N != null)
                            {
                                double ax = pm.N[3 * v], ay = pm.N[3 * v + 1], az = pm.N[3 * v + 2];
                                nx += w * (m[0] * ax + m[4] * ay + m[8] * az);
                                ny += w * (m[1] * ax + m[5] * ay + m[9] * az);
                                nz += w * (m[2] * ax + m[6] * ay + m[10] * az);
                            }
                            wsum += w;
                        }
                        if (wsum <= 1e-9) { ox = x; oy = y; oz = z; if (N != null) { nx = pm.N[3 * v]; ny = pm.N[3 * v + 1]; nz = pm.N[3 * v + 2]; } wsum = 1; }
                        P[3 * v] = (float)(ox / wsum); P[3 * v + 1] = (float)(oy / wsum); P[3 * v + 2] = (float)(oz / wsum);
                        if (N != null)
                        {
                            double l = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                            if (l < 1e-12) l = 1;
                            N[3 * v] = (float)(nx / l); N[3 * v + 1] = (float)(ny / l); N[3 * v + 2] = (float)(nz / l);
                        }
                    }
                    outL.Add(new PreviewMesh
                    {
                        Name = pm.Name, P = P, N = N, UV = pm.UV, Tris = pm.Tris, ImageBytes = pm.ImageBytes,
                        Colour = (SkinAlpha << 24) | (pm.Colour & 0x00FFFFFF),
                    });
                }
            }
            return outL;
        }
    }
}

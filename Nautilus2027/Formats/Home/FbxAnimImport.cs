#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

// Binary FBX -> Home skeleton (SKN) and animation clips (ANI).
// Joints are the LimbNode / Limb / Root models plus every model a skin cluster links to (without either, the animated
// non-mesh models and their parents). A joint's parent is its nearest joint ancestor; transforms of non-joint models in
// between (a Blender "Armature" object, say) are folded into the joint below them.
// Rest pose: cluster TransformLink, else the BindPose matrix, else the model's own properties.
// Each AnimationStack becomes one clip, sampled at its key times (gaps longer than a frame are filled at the file's
// frame rate); Hermite tangents come from the slopes between samples. Everything is converted to Home space (Y up, metres).
// The home_* user properties this tool writes (joint hash / index / flags, skeleton key, clip duration / flags) are used when present.
namespace Nautilus2027.Formats.Home
{
    public static partial class FbxImport
    {
        private sealed class FCurve
        {
            public double[] T;      // seconds
            public double[] V;
            public bool[] Step;
            public double Default;

            public double Eval(double t)
            {
                int n = T?.Length ?? 0;
                if (n == 0) return Default;
                if (t <= T[0]) return V[0];
                if (t >= T[n - 1]) return V[n - 1];
                int lo = 0, hi = n - 1;
                while (hi - lo > 1) { int mid = (lo + hi) / 2; if (T[mid] <= t) lo = mid; else hi = mid; }
                if (Step[lo]) return V[lo];
                double dt = T[lo + 1] - T[lo];
                return dt > 0 ? V[lo] + (V[lo + 1] - V[lo]) * (t - T[lo]) / dt : V[lo];
            }
        }

        private const double KTicks = 46186158000.0;

        private static double FrameRate(Dictionary<string, List<object>> gs)
        {
            int mode = gs.TryGetValue("TimeMode", out var l) && l.Count > 0 ? (int)Dbl(l[0]) : 0;
            double custom = gs.TryGetValue("CustomFrameRate", out var c) && c.Count > 0 ? Dbl(c[0]) : 0;
            switch (mode)
            {
                case 1: return 120;
                case 2: return 100;
                case 3: return 60;
                case 4: return 50;
                case 5: return 48;
                case 6: case 7: return 30;
                case 8: case 9: return 29.97;
                case 10: return 25;
                case 11: return 24;
                case 12: return 1000;
                case 13: return 23.976;
                case 14: return custom > 0 ? custom : 30;
                case 15: return 96;
                case 16: return 72;
                case 17: return 59.94;
                case 18: return 119.88;
                default: return 30;
            }
        }

        /// <summary>A home_* user property as a number (hex strings such as "0x1234abcd" accepted).</summary>
        private static double? UserNum(Dictionary<string, List<object>> pr, string name)
        {
            if (!pr.TryGetValue(name, out var l) || l.Count == 0) return null;
            if (l[0] is string s)
            {
                s = s.Trim();
                if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                    ulong.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong hx)) return hx;
                return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : (double?)null;
            }
            double d = Dbl(l[0], double.NaN);
            return double.IsNaN(d) ? (double?)null : d;
        }

        public static GltfAnimDoc ParseAnimation(byte[] bytes) => ParseAnimation(bytes, out _);

        /// <summary>As <see cref="ParseAnimation(byte[])"/>, also giving the FBX object id of each joint (in skeleton order).</summary>
        internal static GltfAnimDoc ParseAnimation(byte[] bytes, out List<string> jointIds)
        {
            jointIds = null;
            var (_, topList) = ReadNodes(bytes);
            var top = new Dictionary<string, FbxNode>();
            foreach (var n in topList) if (!top.ContainsKey(n.Name)) top[n.Name] = n;
            top.TryGetValue("Objects", out var objects);
            top.TryGetValue("Connections", out var conns);
            if (objects == null || conns == null) throw new InvalidDataException("FBX has no Objects/Connections (FBX before 6.0?)");

            // axis system + units -> Y-up metres (same as the mesh importer)
            top.TryGetValue("GlobalSettings", out var globalSettings);
            var gs = Props70(globalSettings);
            double G(string k, double d) => gs.TryGetValue(k, out var l) && l.Count > 0 ? Dbl(l[0], d) : d;
            double[] AxisVec(double a, double s)
            {
                var v = new double[3];
                int ai = (int)a;
                if (ai >= 0 && ai < 3) v[ai] = s;
                return v;
            }
            var up = AxisVec(G("UpAxis", 1), G("UpAxisSign", 1));
            var front = AxisVec(G("FrontAxis", 2), G("FrontAxisSign", 1));
            var right = AxisVec(G("CoordAxis", 0), G("CoordAxisSign", 1));
            double unit = G("UnitScaleFactor", 1) / 100;
            var AX = new[] { right[0], up[0], front[0], 0, right[1], up[1], front[1], 0, right[2], up[2], front[2], 0, 0, 0, 0, 1 };
            for (int i = 0; i < 12; i++) if (i % 4 < 3) AX[i] *= unit;
            var AXi = HmSkeleton.Inverse(AX);
            double[] ToHome(double[] m) => Mat4.Mul(AX, Mat4.Mul(m, AXi));
            double fps = FrameRate(gs);

            var byId = new Dictionary<string, FbxNode>();
            foreach (var o in objects.Children)
                if (o.Props.Count > 0) byId[Key(o.Props[0])] = o;
            var parentsOf = new Dictionary<string, List<Link>>();
            var childrenOf = new Dictionary<string, List<Link>>();
            foreach (var c in conns.Children)
            {
                if (c.Props.Count < 3) continue;
                string type = Text(c.Props[0]), k = Key(c.Props[1]), pk = Key(c.Props[2]), prop = c.Props.Count > 3 ? Text(c.Props[3]) : null;
                if (!childrenOf.TryGetValue(pk, out var cl)) childrenOf[pk] = cl = new List<Link>();
                cl.Add(new Link { Id = k, Type = type, Prop = prop });
                if (!parentsOf.TryGetValue(k, out var pl)) parentsOf[k] = pl = new List<Link>();
                pl.Add(new Link { Id = pk, Type = type, Prop = prop });
            }
            List<(FbxNode Node, string Prop)> Kids(string id, string name)
            {
                var outL = new List<(FbxNode, string)>();
                if (childrenOf.TryGetValue(id, out var l))
                    foreach (var c in l)
                        if (byId.TryGetValue(c.Id, out var node) && (name == null || node.Name == name))
                            outL.Add((node, c.Prop));
                return outL;
            }
            string IdOf(FbxNode n) => Key(n.Props[0]);
            string TypeOf(FbxNode n) => n.Props.Count > 2 ? Text(n.Props[2]) : "";
            FbxNode ParentModel(FbxNode m)
            {
                if (parentsOf.TryGetValue(IdOf(m), out var pl))
                    foreach (var p in pl)
                        if (p.Type == "OO" && byId.TryGetValue(p.Id, out var pn) && pn.Name == "Model") return pn;
                return null;
            }

            var models = objects.Children.Where(o => o.Name == "Model" && o.Props.Count > 0).ToList();
            bool IsMesh(FbxNode m) => TypeOf(m) == "Mesh" || Kids(IdOf(m), "Geometry").Count > 0 || Child(m, "Vertices") != null;

            // ---- skin clusters (bone -> bind world matrix)
            var clusterLink = new Dictionary<string, double[]>();
            foreach (var d in objects.Children.Where(o => o.Name == "Deformer" && o.Props.Count > 2 && Text(o.Props[2]) == "Cluster"))
            {
                var bone = Kids(IdOf(d), "Model").Select(k => k.Node).FirstOrDefault();
                if (bone == null) continue;
                var tl = ChildVal(d, "TransformLink", null) as double[];
                if (tl != null && tl.Length == 16 && !clusterLink.ContainsKey(IdOf(bone))) clusterLink[IdOf(bone)] = tl;
                else if (!clusterLink.ContainsKey(IdOf(bone))) clusterLink[IdOf(bone)] = null;
            }
            var bindPose = new Dictionary<string, double[]>();
            foreach (var pose in objects.Children.Where(o => o.Name == "Pose" && o.Props.Count > 2 && Text(o.Props[2]) == "BindPose"))
                foreach (var pn in pose.Children.Where(c => c.Name == "PoseNode"))
                {
                    var nid = ChildVal(pn, "Node", null);
                    var mat = ChildVal(pn, "Matrix", null) as double[];
                    if (nid != null && mat != null && mat.Length == 16 && !bindPose.ContainsKey(Key(nid))) bindPose[Key(nid)] = mat;
                }

            // ---- animation curves: stack -> layers -> curve nodes -> model property
            var stacks = objects.Children.Where(o => o.Name == "AnimationStack" && o.Props.Count > 0).ToList();
            FCurve ReadCurve(FbxNode cv, double def)
            {
                var kt = ChildVal(cv, "KeyTime", null) as double[] ?? Array.Empty<double>();
                var kv = ChildVal(cv, "KeyValueFloat", null) as double[] ?? ChildVal(cv, "KeyValueDouble", null) as double[] ?? Array.Empty<double>();
                int n = Math.Min(kt.Length, kv.Length);
                var flags = ChildVal(cv, "KeyAttrFlags", null) as double[] ?? Array.Empty<double>();
                var refs = ChildVal(cv, "KeyAttrRefCount", null) as double[] ?? Array.Empty<double>();
                var step = new bool[n];
                int k = 0;
                for (int a = 0; a < flags.Length && k < n; a++)
                {
                    int cnt = a < refs.Length ? (int)refs[a] : n - k;
                    bool st = ((int)flags[a] & 0x2) != 0;
                    for (int r = 0; r < cnt && k < n; r++) step[k++] = st;
                }
                return new FCurve
                {
                    T = kt.Take(n).Select(x => x / KTicks).ToArray(),
                    V = kv.Take(n).ToArray(),
                    Step = step,
                    Default = Dbl(ChildVal(cv, "Default", def), def),
                };
            }
            // per stack: model id -> property -> 3 curves
            var stackAnims = new List<(FbxNode Stack, Dictionary<string, Dictionary<string, FCurve[]>> Props)>();
            foreach (var st in stacks)
            {
                var props = new Dictionary<string, Dictionary<string, FCurve[]>>();
                foreach (var (layer, _) in Kids(IdOf(st), "AnimationLayer"))
                    foreach (var (cn, _) in Kids(IdOf(layer), "AnimationCurveNode"))
                    {
                        if (!parentsOf.TryGetValue(IdOf(cn), out var targets)) continue;
                        var tgt = targets.FirstOrDefault(t => t.Type == "OP" && byId.TryGetValue(t.Id, out var tn) && tn.Name == "Model" &&
                                                              (t.Prop == "Lcl Translation" || t.Prop == "Lcl Rotation" || t.Prop == "Lcl Scaling"));
                        if (tgt == null) continue;
                        var cpr = Props70(cn);
                        double Def(string a) => cpr.TryGetValue(a, out var l) && l.Count > 0 ? Dbl(l[0]) : 0;
                        var curves = new FCurve[3];
                        string[] axes = { "d|X", "d|Y", "d|Z" };
                        for (int a = 0; a < 3; a++) curves[a] = new FCurve { Default = Def(axes[a]) };
                        bool any = false;
                        foreach (var (cv, p) in Kids(IdOf(cn), "AnimationCurve"))
                        {
                            int a = Array.IndexOf(axes, p);
                            if (a < 0) continue;
                            curves[a] = ReadCurve(cv, curves[a].Default);
                            any = true;
                        }
                        if (!any && !cpr.ContainsKey("d|X")) continue;
                        if (!props.TryGetValue(tgt.Id, out var mp)) props[tgt.Id] = mp = new Dictionary<string, FCurve[]>();
                        mp[tgt.Prop] = curves;
                    }
                if (props.Count > 0) stackAnims.Add((st, props));
            }

            // ---- joint set
            var joints = new HashSet<FbxNode>();
            foreach (var m in models)
            {
                string t = TypeOf(m);
                if (t == "LimbNode" || t == "Limb" || t == "Root" || clusterLink.ContainsKey(IdOf(m))) joints.Add(m);
            }
            var doc = new GltfAnimDoc { HadSkin = clusterLink.Count > 0 };
            if (joints.Count == 0)
            {
                foreach (var (_, props) in stackAnims)
                    foreach (var id in props.Keys)
                        if (byId.TryGetValue(id, out var m) && !IsMesh(m)) joints.Add(m);
                foreach (var m in joints.ToList())
                    for (var p = ParentModel(m); p != null && !IsMesh(p) && joints.Count < 10000; p = ParentModel(p)) joints.Add(p);
            }
            if (joints.Count == 0) throw new InvalidDataException("the FBX has no bones (LimbNodes), no skin and no animated objects, so there is no skeleton in it");

            // order: stored Home index when every joint has one, else depth-first in file order (parents first)
            int? HomeIndex(FbxNode m) { var v = UserNum(Props70(m), "home_index"); return v.HasValue ? (int)v.Value : (int?)null; }
            List<FbxNode> order;
            if (joints.All(m => HomeIndex(m).HasValue) && joints.Select(m => HomeIndex(m).Value).Distinct().Count() == joints.Count)
                order = joints.OrderBy(m => HomeIndex(m).Value).ToList();
            else
            {
                order = new List<FbxNode>();
                var visited = new HashSet<FbxNode>();
                void Visit(FbxNode m, int depth)
                {
                    if (!visited.Add(m) || depth > 512) return;
                    if (joints.Contains(m)) order.Add(m);
                    foreach (var (c, _) in Kids(IdOf(m), "Model")) Visit(c, depth + 1);
                }
                foreach (var m in models) if (ParentModel(m) == null) Visit(m, 0);
                foreach (var m in models) if (joints.Contains(m) && !order.Contains(m)) order.Add(m);
            }
            var orderIndex = new Dictionary<FbxNode, int>();
            void Reindex() { orderIndex.Clear(); for (int k = 0; k < order.Count; k++) orderIndex[order[k]] = k; }
            Reindex();
            int JointParent(FbxNode m)
            {
                int guard = 0;
                for (var p = ParentModel(m); p != null && guard++ < 512; p = ParentModel(p))
                    if (orderIndex.TryGetValue(p, out int k)) return k;
                return -1;
            }
            bool moved = true;
            for (int guard = 0; moved && guard < order.Count * 2; guard++)
            {
                moved = false;
                for (int k = 0; k < order.Count; k++)
                {
                    int p = JointParent(order[k]);
                    if (p > k) { var n = order[p]; order.RemoveAt(p); order.Insert(k, n); Reindex(); moved = true; break; }
                }
            }
            var parentIdx = order.Select(JointParent).ToArray();
            jointIds = order.Select(IdOf).ToList();

            // ---- transforms
            double[] Local(FbxNode model, Dictionary<string, FCurve[]> anim, double t)
            {
                var pr = Props70(model);
                double[] V(string k, double[] d)
                {
                    if (anim != null && anim.TryGetValue(k, out var cv)) return new[] { cv[0].Eval(t), cv[1].Eval(t), cv[2].Eval(t) };
                    return pr.TryGetValue(k, out var l) && l.Count >= 3 ? new[] { Dbl(l[0]), Dbl(l[1]), Dbl(l[2]) } : d;
                }
                int ord = pr.TryGetValue("RotationOrder", out var ro) && ro.Count > 0 ? (int)Dbl(ro[0]) : 0;
                var zero = new double[] { 0, 0, 0 };
                var one = new double[] { 1, 1, 1 };
                var rotPivot = V("RotationPivot", zero);
                var scalePivot = V("ScalingPivot", zero);
                return Mat4.Mul(T(V("Lcl Translation", zero)), Mat4.Mul(T(V("RotationOffset", zero)), Mat4.Mul(T(rotPivot),
                    Mat4.Mul(Rxyz(V("PreRotation", zero), 0), Mat4.Mul(Rxyz(V("Lcl Rotation", zero), ord), Mat4.Mul(InvRot(V("PostRotation", zero)),
                        Mat4.Mul(T(Neg(rotPivot)), Mat4.Mul(T(V("ScalingOffset", zero)), Mat4.Mul(T(scalePivot), Mat4.Mul(Sc(V("Lcl Scaling", one)), T(Neg(scalePivot))))))))))));
            }
            Func<FbxNode, double[]> WorldFn(Dictionary<string, Dictionary<string, FCurve[]>> anims, double t)
            {
                var cache = new Dictionary<FbxNode, double[]>();
                double[] W(FbxNode m, int depth)
                {
                    if (cache.TryGetValue(m, out var c)) return c;
                    Dictionary<string, FCurve[]> a = null;
                    if (anims != null && !anims.TryGetValue(IdOf(m), out a)) a = null;
                    var L = Local(m, a, t);
                    var p = depth < 512 ? ParentModel(m) : null;
                    var r = p != null ? Mat4.Mul(W(p, depth + 1), L) : L;
                    cache[m] = r;
                    return r;
                }
                return m => W(m, 0);
            }
            List<double[]> JointLocals(Func<FbxNode, double[]> world)
            {
                var outL = new List<double[]>();
                for (int k = 0; k < order.Count; k++)
                {
                    var w = world(order[k]);
                    var L = parentIdx[k] >= 0 ? Mat4.Mul(HmSkeleton.Inverse(world(order[parentIdx[k]])), w) : w;
                    outL.Add(ToHome(L));
                }
                return outL;
            }

            // ---- skeleton (rest pose)
            var restWorld = WorldFn(null, 0);
            int fromCluster = 0, fromPose = 0;
            var restCache = new Dictionary<FbxNode, double[]>();
            double[] Rest(FbxNode m)
            {
                if (restCache.TryGetValue(m, out var c)) return c;
                double[] r;
                if (clusterLink.TryGetValue(IdOf(m), out var tl) && tl != null) { r = tl; fromCluster++; }
                else if (bindPose.TryGetValue(IdOf(m), out var bp)) { r = bp; fromPose++; }
                else r = restWorld(m);
                restCache[m] = r;
                return r;
            }
            var restLocals = JointLocals(Rest);
            var sk = new HmSkeletonFile();
            for (int k = 0; k < order.Count; k++)
            {
                var m = order[k];
                var pr = Props70(m);
                string name = ObjName(m.Props.Count > 1 ? m.Props[1] : "");
                if (name.Length == 0) name = "joint" + k;
                double? hv = UserNum(pr, "home_hash");
                var j = new SkJoint
                {
                    Index = k,
                    Hash = hv.HasValue ? (uint)hv.Value : HmSkeleton.LabelHash(HmRoundTrip.BaseName(name)),
                    Parent = (short)parentIdx[k],
                    Flags = (short)(UserNum(pr, "home_flags") ?? 0),
                    TW = (float)(UserNum(pr, "home_tw") ?? 0),
                    SW = (float)(UserNum(pr, "home_sw") ?? 0),
                };
                var (t, q, s) = HmSkeleton.Decompose(restLocals[k]);
                j.T = t; j.Q = q; j.S = s;
                sk.Joints.Add(j);
                doc.JointNodes.Add(k);
            }
            var seen = new HashSet<uint>();
            foreach (var j in sk.Joints)
            {
                if (seen.Add(j.Hash)) continue;
                uint h = j.Hash;
                while (!seen.Add(h)) h = HmCore.HashName(HmSkeleton.JointLabel(h) + "_dup");
                doc.Notes.Add($"two joints are called {HmSkeleton.JointLabel(j.Hash)}; the second was renamed {HmSkeleton.JointLabel(h)}");
                j.Hash = h;
            }
            double? key = null;
            short owns = 0;
            for (int k = 0; k < order.Count && !key.HasValue; k++)
            {
                var pr = Props70(order[k]);
                key = UserNum(pr, "home_key");
                if (key.HasValue) owns = (short)(UserNum(pr, "home_ownsMemory") ?? 0);
            }
            sk.Key = key.HasValue ? (uint)key.Value : HmSkeleton.NewKey(sk);
            sk.OwnsMemory = owns;
            doc.Skeleton = sk;
            if (!key.HasValue) doc.Notes.Add($"new skeleton key 0x{sk.Key:x8} (an MDL made for this skeleton must use the same key)");
            if (fromCluster + fromPose < order.Count && doc.HadSkin)
                doc.Notes.Add($"rest pose: {fromCluster} joint(s) from the skin, {fromPose} from the bind pose, {order.Count - fromCluster - fromPose} from the joints' own transforms");

            // ---- clips
            foreach (var (st, props) in stackAnims)
            {
                var spr = Props70(st);
                string clipName = ObjName(st.Props.Count > 1 ? st.Props[1] : "");
                if (clipName.StartsWith("AnimStack::")) clipName = clipName.Substring(11);
                if (clipName.Length == 0) clipName = "animation_" + doc.Clips.Count;
                var keyTimes = new SortedSet<double>();
                foreach (var mp in props.Values)
                    foreach (var cvs in mp.Values)
                        foreach (var cv in cvs)
                            if (cv.T != null) foreach (var t in cv.T) keyTimes.Add(Math.Round(t * 1e6) / 1e6);
                if (keyTimes.Count == 0) continue;
                double start = keyTimes.Min, stop = keyTimes.Max;
                double? ls = UserNum(spr, "LocalStart"), le = UserNum(spr, "LocalStop");
                if (ls.HasValue && le.HasValue && le.Value > ls.Value) { start = ls.Value / KTicks; stop = le.Value / KTicks; }
                var times = keyTimes.Where(t => t >= start - 1e-6 && t <= stop + 1e-6).ToList();
                if (times.Count == 0 || times[0] > start + 1e-6) times.Insert(0, start);
                if (times[times.Count - 1] < stop - 1e-6) times.Add(stop);
                // fill gaps longer than a frame so sparse keys still follow the curve
                var filled = new List<double> { times[0] };
                for (int k = 1; k < times.Count; k++)
                {
                    double gap = times[k] - times[k - 1];
                    if (gap > 1.5 / fps)
                    {
                        int steps = (int)Math.Ceiling(gap * fps);
                        for (int s = 1; s < steps; s++) filled.Add(times[k - 1] + gap * s / steps);
                    }
                    filled.Add(times[k]);
                }
                times = filled;
                if (times.Count > 20000) { doc.Notes.Add($"{clipName}: more than 20000 samples; skipped"); continue; }

                var frames = times.Select(t => JointLocals(WorldFn(props, t)).Select(HmSkeleton.Decompose).ToList()).ToList();
                var clip = new HmAnimClip { Flags = (int)(UserNum(spr, "home_flags") ?? 0) };
                double dur = stop - start;
                double? hd = UserNum(spr, "home_duration");
                clip.Duration = (float)(hd.HasValue && hd.Value >= dur - 1e-4 ? hd.Value : dur);
                for (int k = 0; k < order.Count; k++)
                {
                    props.TryGetValue(IdOf(order[k]), out var own);
                    var item = new AniItem { Hash = sk.Joints[k].Hash };
                    foreach (var path in new[] { "translation", "rotation", "scale" })
                    {
                        string fprop = path == "translation" ? "Lcl Translation" : path == "rotation" ? "Lcl Rotation" : "Lcl Scaling";
                        var keys = new List<AniKey>();
                        for (int f = 0; f < frames.Count; f++)
                        {
                            var (tt, qq, ss) = frames[f][k];
                            var v = new double[4];
                            var src = path == "translation" ? tt : path == "rotation" ? qq : ss;
                            for (int c = 0; c < src.Length && c < 4; c++) v[c] = src[c];
                            keys.Add(new AniKey { T = times[f] - start, V = v });
                        }
                        if (path == "rotation")
                            for (int f = 1; f < keys.Count; f++)
                            {
                                double dot = 0;
                                for (int c = 0; c < 4; c++) dot += keys[f].V[c] * keys[f - 1].V[c];
                                if (dot < 0) for (int c = 0; c < 4; c++) keys[f].V[c] = -keys[f].V[c];
                            }
                        bool constant = keys.All(kk => Enumerable.Range(0, 4).All(c => Math.Abs(kk.V[c] - keys[0].V[c]) < 1e-6));
                        if (constant && (own == null || !own.ContainsKey(fprop))) continue;
                        uint nameHash = path == "rotation" ? HmAnimation.HRotate : path == "translation" ? HmAnimation.HTranslate : HmAnimation.HScale;
                        var chn = new AniChannel { Name = nameHash };
                        if (constant) chn.Constant = keys[0].V;
                        else
                        {
                            for (int f = 0; f < keys.Count; f++)
                            {
                                int a = Math.Max(0, f - 1), b = Math.Min(keys.Count - 1, f + 1);
                                double dt = keys[b].T - keys[a].T;
                                var g = new double[4];
                                if (dt > 0) for (int c = 0; c < 4; c++) g[c] = (keys[b].V[c] - keys[a].V[c]) / dt;
                                keys[f].G = g;
                            }
                            chn.Keys = keys;
                        }
                        item.Channels.Add(chn);
                    }
                    if (item.Channels.Count > 0) clip.Items.Add(item);
                }
                if (clip.Items.Count == 0) continue;
                doc.Clips.Add((clipName, clip));
            }
            if (stackAnims.Count > 0)
                doc.Notes.Add($"animation sampled at the FBX key times ({fps:0.##} fps file); for an exact round trip of Home clips use GLB");
            return doc;
        }

        // ------------------------------------------------------------------ mesh-side helpers (used by Parse)

        private static string UserStr(Dictionary<string, List<object>> pr, string name) =>
            pr.TryGetValue(name, out var l) && l.Count > 0 && l[0] is string s && s.Length > 0 ? s : null;

        /// <summary>The layer element <paramref name="name"/> with the given layer index (e.g. the second UV set is index 1).</summary>
        private static Layer LayerAt(FbxNode geo, string name, int index, string dataName, string idxName)
        {
            var l = geo?.Children.FirstOrDefault(c => c.Name == name && c.Props.Count > 0 && (int)Dbl(c.Props[0]) == index);
            if (l == null) return null;
            return new Layer
            {
                Map = Text(ChildVal(l, "MappingInformationType", "ByPolygonVertex")),
                Ref = Text(ChildVal(l, "ReferenceInformationType", "Direct")),
                Data = ChildVal(l, dataName, null) as double[] ?? Array.Empty<double>(),
                Index = idxName != null ? ChildVal(l, idxName, null) as double[] : null,
            };
        }

        private static double[] PickAt(Layer L, int comps, int cornerIdx, int ctrl, int poly)
        {
            if (L == null || L.Data.Length == 0) return null;
            bool byV = L.Map.IndexOf("Vert", StringComparison.OrdinalIgnoreCase) >= 0 && L.Map.IndexOf("Polygon", StringComparison.OrdinalIgnoreCase) < 0;
            bool byPoly = L.Map == "ByPolygon", same = L.Map == "AllSame";
            int i = same ? 0 : byPoly ? poly : byV ? ctrl : cornerIdx;
            if (L.Ref.Contains("Index") && L.Index != null)
            {
                if (i < 0 || i >= L.Index.Length) return null;
                i = (int)L.Index[i];
            }
            if (i < 0 || (i + 1) * comps > L.Data.Length) return null;
            var r = new double[comps];
            Array.Copy(L.Data, i * comps, r, 0, comps);
            return r;
        }

        /// <summary>
        /// Skin weights per geometry id: for each control point up to 4 (joint, weight), weights summing to 1. Joint numbers
        /// are in the order of the skeleton <see cref="ParseAnimation(byte[])"/> makes from the same file (so the .SKN made
        /// from it on the ANI / SKN tab fits).
        /// </summary>
        private static Dictionary<string, (int[] J, double[] W)[]> ReadSkins(byte[] bytes, FbxNode objects, FbxNode conns, out uint key, out int jointCount, List<string> notes)
        {
            key = 0;
            jointCount = 0;
            var outD = new Dictionary<string, (int[] J, double[] W)[]>();
            var skinNodes = objects.Children.Where(o => o.Name == "Deformer" && o.Props.Count > 2 && Text(o.Props[2]) == "Skin").ToList();
            if (skinNodes.Count == 0) return outD;
            GltfAnimDoc sk;
            List<string> jointIds;
            try { sk = ParseAnimation(bytes, out jointIds); }
            catch (Exception ex) { notes.Add("the FBX is rigged but its skeleton could not be read (" + ex.Message + "); converted as a static model"); return outD; }
            key = sk.Skeleton.Key;
            jointCount = sk.Skeleton.Joints.Count;
            var jointIndex = new Dictionary<string, int>();
            for (int k = 0; k < jointIds.Count; k++) jointIndex[jointIds[k]] = k;

            var byId = new Dictionary<string, FbxNode>();
            foreach (var o in objects.Children) if (o.Props.Count > 0) byId[Key(o.Props[0])] = o;
            var kids = new Dictionary<string, List<string>>();
            var parents = new Dictionary<string, List<string>>();
            foreach (var c in conns.Children)
            {
                if (c.Props.Count < 3 || Text(c.Props[0]) != "OO") continue;
                string k = Key(c.Props[1]), pk = Key(c.Props[2]);
                if (!kids.TryGetValue(pk, out var kl)) kids[pk] = kl = new List<string>();
                kl.Add(k);
                if (!parents.TryGetValue(k, out var pl)) parents[k] = pl = new List<string>();
                pl.Add(pk);
            }
            IEnumerable<FbxNode> Linked(Dictionary<string, List<string>> map, string id, string name) =>
                map.TryGetValue(id, out var l) ? l.Where(byId.ContainsKey).Select(x => byId[x]).Where(n => n.Name == name) : Enumerable.Empty<FbxNode>();

            int unknown = 0;
            foreach (var skin in skinNodes)
            {
                string sid = Key(skin.Props[0]);
                foreach (var geo in Linked(parents, sid, "Geometry"))
                {
                    int nCtrl = (ChildVal(geo, "Vertices", null) as double[] ?? Array.Empty<double>()).Length / 3;
                    var per = new List<(int J, double W)>[nCtrl];
                    foreach (var cl in Linked(kids, sid, "Deformer").Where(d => d.Props.Count > 2 && Text(d.Props[2]) == "Cluster"))
                    {
                        var bone = Linked(kids, Key(cl.Props[0]), "Model").FirstOrDefault();
                        if (bone == null || !jointIndex.TryGetValue(Key(bone.Props[0]), out int j)) { unknown++; continue; }
                        var idx = ChildVal(cl, "Indexes", null) as double[] ?? Array.Empty<double>();
                        var w = ChildVal(cl, "Weights", null) as double[] ?? Array.Empty<double>();
                        for (int k = 0; k < idx.Length && k < w.Length; k++)
                        {
                            int v = (int)idx[k];
                            if (v < 0 || v >= nCtrl || !(w[k] > 0)) continue;
                            (per[v] ??= new List<(int, double)>()).Add((j, w[k]));
                        }
                    }
                    var res = new (int[] J, double[] W)[nCtrl];
                    int over = 0, big = 0;
                    for (int v = 0; v < nCtrl; v++)
                    {
                        var l = per[v];
                        var J = new int[4];
                        var W = new double[4];
                        if (l == null || l.Count == 0) { W[0] = 1; res[v] = (J, W); continue; }
                        // the same joint from two clusters counts once
                        var merged = l.GroupBy(x => x.J).Select(g => (J: g.Key, W: g.Sum(x => x.W))).OrderByDescending(x => x.W).ToList();
                        if (merged.Count > 4) over++;
                        double sum = merged.Take(4).Sum(x => x.W);
                        for (int c = 0; c < 4 && c < merged.Count; c++)
                        {
                            J[c] = merged[c].J;
                            W[c] = sum > 0 ? merged[c].W / sum : 0;
                            if (J[c] > 255) big++;
                        }
                        res[v] = (J, W);
                    }
                    outD[Key(geo.Props[0])] = res;
                    if (over > 0) notes.Add($"{ObjName(geo.Props.Count > 1 ? geo.Props[1] : "")}: {over} vertex(es) had more than 4 joints; the 4 strongest were kept");
                    if (big > 0) notes.Add($"{ObjName(geo.Props.Count > 1 ? geo.Props[1] : "")}: {big} weight(s) on joints past 255, which Home can't index");
                }
            }
            if (unknown > 0) notes.Add($"{unknown} skin cluster(s) point at objects that aren't skeleton joints; ignored");
            return outD;
        }

        /// <summary>
        /// A mesh exported from an MDL by this tool (it has home_mesh data): one vertex per control point in the original
        /// order, with its lightmap UVs, colours, tangents, skin, original strip and extra fields, so the MDL can be rebuilt
        /// exactly. Null if the geometry doesn't have that shape (then the normal import is used).
        /// </summary>
        private static SourcePart ExactPart(FbxNode geo, double[] M, double[] NT, bool flip, string name, int material, (int[] J, double[] W)[] skin)
        {
            var V = ChildVal(geo, "Vertices", null) as double[] ?? Array.Empty<double>();
            var PVI = ChildVal(geo, "PolygonVertexIndex", null) as double[] ?? Array.Empty<double>();
            int n = V.Length / 3;
            if (n == 0 || PVI.Length == 0) return null;
            var nL = LayerAt(geo, "LayerElementNormal", 0, "Normals", "NormalsIndex");
            var tL = LayerAt(geo, "LayerElementTangent", 0, "Tangents", "TangentsIndex");
            var bL = LayerAt(geo, "LayerElementBinormal", 0, "Binormals", "BinormalsIndex");
            var uL = LayerAt(geo, "LayerElementUV", 0, "UV", "UVIndex");
            var u1L = LayerAt(geo, "LayerElementUV", 1, "UV", "UVIndex");
            var cL = LayerAt(geo, "LayerElementColor", 0, "Colors", "ColorIndex");
            var nrm = new double[n][]; var tan = new double[n][]; var bin = new double[n][]; var uv = new double[n][]; var uv1 = new double[n][]; var col = new double[n][];
            var part = new SourcePart { Name = name, Material = material };
            var poly = new List<(int K, int C)>();
            int polyIdx = 0;
            for (int k = 0; k < PVI.Length; k++)
            {
                int c = (int)PVI[k];
                bool last = c < 0;
                if (last) c = ~c;
                if (c >= n) return null;
                poly.Add((k, c));
                // per control point: the first corner that uses it
                if (nrm[c] == null) nrm[c] = PickAt(nL, 3, k, c, polyIdx);
                if (tan[c] == null) tan[c] = PickAt(tL, 3, k, c, polyIdx);
                if (bin[c] == null) bin[c] = PickAt(bL, 3, k, c, polyIdx);
                if (uv[c] == null) uv[c] = PickAt(uL, 2, k, c, polyIdx);
                if (uv1[c] == null) uv1[c] = PickAt(u1L, 2, k, c, polyIdx);
                if (col[c] == null) col[c] = PickAt(cL, 4, k, c, polyIdx);
                if (!last) continue;
                for (int t = 1; t + 1 < poly.Count; t++)
                    part.Tris.Add(flip ? new[] { poly[0].C, poly[t + 1].C, poly[t].C } : new[] { poly[0].C, poly[t].C, poly[t + 1].C });
                poly.Clear();
                polyIdx++;
            }
            if (part.Tris.Count == 0) return null;
            V3 Dir(double[] m, double x, double y, double z) => new V3(m[0] * x + m[4] * y + m[8] * z, m[1] * x + m[5] * y + m[9] * z, m[2] * x + m[6] * y + m[10] * z);
            for (int i = 0; i < n; i++)
            {
                part.P.Add(Mat4.Point(M, V[3 * i], V[3 * i + 1], V[3 * i + 2]));
                // FBX V runs bottom-up; Home's top-down (as glTF)
                part.U.Add(uv[i] != null ? new V2(uv[i][0], 1.0 - uv[i][1]) : new V2(0, 0));
            }
            if (nL != null && nrm.All(x => x != null)) part.N = nrm.Select(x => Mat4.Normal(NT, x[0], x[1], x[2])).ToList();
            if (u1L != null && uv1.All(x => x != null)) part.U1 = uv1.Select(x => new V2(x[0], 1.0 - x[1])).ToList();
            if (cL != null && col.All(x => x != null)) part.Colour = col.ToList();
            if (tL != null && part.N != null && tan.All(x => x != null))
            {
                part.Tangent = new List<double[]>(n);
                for (int i = 0; i < n; i++)
                {
                    var t = Dir(M, tan[i][0], tan[i][1], tan[i][2]);
                    double w = 1;
                    if (bin[i] != null)
                    {
                        var b = Dir(M, bin[i][0], bin[i][1], bin[i][2]);
                        w = V3.Dot(V3.Cross(part.N[i], t), b) < 0 ? -1 : 1;
                        if (flip) w = -w;
                    }
                    part.Tangent.Add(new[] { t.X, t.Y, t.Z, w });
                }
            }
            if (skin != null && skin.Length == n)
                part.Skin = new SourceSkin { J = skin.Select(x => (int[])x.J.Clone()).ToList(), W = skin.Select(x => (double[])x.W.Clone()).ToList() };
            foreach (var a in geo.Children.Where(c => c.Name == "HomeAttribute" && c.Props.Count >= 3))
            {
                string an = Text(a.Props[0]);
                int comps = (int)Dbl(a.Props[1]);
                var vals = a.Props[2] as double[];
                if (an.Length == 0 || comps <= 0 || vals == null || vals.Length != n * comps) continue;
                var list = new List<double[]>(n);
                for (int i = 0; i < n; i++) { var e = new double[comps]; Array.Copy(vals, i * comps, e, 0, comps); list.Add(e); }
                part.Custom[an] = list;
            }
            if (!flip && ChildVal(geo, "HomeStrip", null) is double[] strip && strip.Length >= 3)
                part.StripIndices = strip.Select(x => (int)x).ToArray();
            return part;
        }
    }
}

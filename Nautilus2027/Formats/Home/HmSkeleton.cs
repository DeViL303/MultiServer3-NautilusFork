#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// Home skeletons (.SKN, GpSkeletonDef 'SK08'). Layout from the HDK eboot's debug info (GpSkeletonDef, GpJointLinkage,
// GpJoint, GpJointName) and the HOME_DEV_KIT (ws/mdlb/src/08.js):
//   +0 'SK08'  +4 u32 skeleton key (the MDL header stores the same key at +4 and the joint count at +8)
//   +8 s16 ownsMemory  +10 s16 joint count
//   +12 / +16 / +20 self-relative offsets: linkage[8 B] (s16 flags, parent, next sibling, first child),
//                   bind pose[48 B] (local rotation quat xyzw, translation xyzw, scale xyzw), names[8 B] (hash, index; sorted by hash)
//   +24 skeletal LOD, +28 / +32 procedural joints (offsets, 0 in every shipped file seen), +36 3 x u32 reserved.
// All values are big-endian. Joint flag 1 = kParentScaleCompensate (the parent's scale doesn't scale this joint's rotation).
namespace Nautilus2027.Formats.Home
{
    public sealed class SkJoint
    {
        public int Index;
        public uint Hash;
        public short Flags, Parent = -1, NextSibling = -1, FirstChild = -1;
        public double[] Q = { 0, 0, 0, 1 };
        public double[] T = { 0, 0, 0 };
        public double[] S = { 1, 1, 1 };
        /// <summary>The w of the stored translation / scale vectors (kept for an exact rewrite).</summary>
        public float TW, SW;
        public string Name => HmSkeleton.JointLabel(Hash);
    }

    public sealed class HmSkeletonFile
    {
        public uint Key;
        public short OwnsMemory;
        public List<SkJoint> Joints = new List<SkJoint>();
        /// <summary>True when the file has LOD or procedural-joint data (not rewritten by this code).</summary>
        public bool HasExtras;

        public int IndexOf(uint hash)
        {
            for (int i = 0; i < Joints.Count; i++) if (Joints[i].Hash == hash) return i;
            return -1;
        }
    }

    public static class HmSkeleton
    {
        // the avatar skeleton's known joint names (anything else is written as j_xxxxxxxx)
        private static readonly Dictionary<uint, string> Known = BuildKnown();

        private static Dictionary<uint, string> BuildKnown()
        {
            var names = new List<string> { "pelvisroot", "pelvis", "hips", "spine", "spine1", "spine2", "spine3", "neck", "neck1", "head", "null", "tongue_end", "root" };
            foreach (var s in new[] { "left", "right" })
            {
                foreach (var p in new[] { "upleg", "leg", "foot", "toes", "shoulder", "arm", "forearm", "hand" }) names.Add(s + p);
                foreach (var f in new[] { "thumb", "index", "middle", "ring", "pinky" })
                    for (int i = 1; i <= 3; i++) names.Add(s + f + i);
            }
            var d = new Dictionary<uint, string>();
            foreach (var n in names) d[HmCore.HashName(n)] = n;
            return d;
        }

        public static string JointLabel(uint h) => Known.TryGetValue(h, out var n) ? n : "j_" + h.ToString("x8");

        /// <summary>Joint name from a file -> hash: "j_xxxxxxxx" is taken as the hash; any "namespace:" or "a|b|" prefix is dropped; others are hashed lower-case.</summary>
        public static uint LabelHash(string name) => HmConvert.JointHash(name);

        public static bool LooksLikeSkn(byte[] b) => b != null && b.Length >= 48 && b[0] == 0x53 && b[1] == 0x4B && b[2] == 0x30 && b[3] == 0x38;

        private static int Rel(byte[] d, int o)
        {
            int v = (int)HmCoreBE.U32(d, o);
            return v == 0 ? 0 : o + v;
        }

        public static HmSkeletonFile Read(byte[] d)
        {
            if (!LooksLikeSkn(d)) throw new InvalidDataException("not a Home skeleton (SK08)");
            try
            {
                var sk = new HmSkeletonFile { Key = HmCoreBE.U32(d, 4), OwnsMemory = HmCoreBE.S16(d, 8) };
                int n = HmCoreBE.S16(d, 10);
                if (n < 0 || n > 4096) throw new InvalidDataException("joint count is not plausible");
                int L = Rel(d, 12), B = Rel(d, 16), N = Rel(d, 20);
                sk.HasExtras = HmCoreBE.U32(d, 24) != 0 || HmCoreBE.U32(d, 28) != 0;
                for (int i = 0; i < n; i++)
                {
                    float F(int k) => HmCoreBE.F32(d, B + 48 * i + 4 * k);
                    sk.Joints.Add(new SkJoint
                    {
                        Index = i,
                        Flags = HmCoreBE.S16(d, L + 8 * i),
                        Parent = HmCoreBE.S16(d, L + 8 * i + 2),
                        NextSibling = HmCoreBE.S16(d, L + 8 * i + 4),
                        FirstChild = HmCoreBE.S16(d, L + 8 * i + 6),
                        Q = new double[] { F(0), F(1), F(2), F(3) },
                        T = new double[] { F(4), F(5), F(6) },
                        TW = F(7),
                        S = new double[] { F(8), F(9), F(10) },
                        SW = F(11),
                    });
                }
                for (int i = 0; i < n; i++)
                {
                    uint h = HmCoreBE.U32(d, N + 8 * i);
                    int j = (int)HmCoreBE.U32(d, N + 8 * i + 4);
                    if (j >= 0 && j < n) sk.Joints[j].Hash = h;
                }
                return sk;
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new InvalidDataException("the skeleton is truncated or corrupt");
            }
        }

        /// <summary>Write a skeleton. Joints must be ordered parents first; sibling / child links are rebuilt from the parents.</summary>
        public static byte[] Write(HmSkeletonFile sk)
        {
            int n = sk.Joints.Count;
            for (int i = 0; i < n; i++)
            {
                var j = sk.Joints[i];
                if (j.Parent >= i) throw new InvalidDataException($"joint {i} ({j.Name}) comes before its parent");
                if (j.Parent < -1) throw new InvalidDataException($"joint {i} has a bad parent index");
            }
            if (sk.Joints.Select(j => j.Hash).Distinct().Count() != n) throw new InvalidDataException("two joints have the same name");
            RebuildLinks(sk);
            int L = 48, B = Align(L + 8 * n, 16), N = B + 48 * n, size = N + 8 * n;   // shipped files end right after the names
            var d = new byte[size];
            d[0] = 0x53; d[1] = 0x4B; d[2] = 0x30; d[3] = 0x38;
            HmCore.PutU32(d, 4, sk.Key);
            HmCore.PutS16(d, 8, sk.OwnsMemory);
            HmCore.PutS16(d, 10, (short)n);
            HmCore.PutS32(d, 12, L - 12);
            HmCore.PutS32(d, 16, B - 16);
            HmCore.PutS32(d, 20, N - 20);
            for (int i = 0; i < n; i++)
            {
                var j = sk.Joints[i];
                HmCore.PutS16(d, L + 8 * i, j.Flags);
                HmCore.PutS16(d, L + 8 * i + 2, j.Parent);
                HmCore.PutS16(d, L + 8 * i + 4, j.NextSibling);
                HmCore.PutS16(d, L + 8 * i + 6, j.FirstChild);
                var v = new[] { j.Q[0], j.Q[1], j.Q[2], j.Q[3], j.T[0], j.T[1], j.T[2], j.TW, j.S[0], j.S[1], j.S[2], j.SW };
                for (int k = 0; k < 12; k++) HmCore.PutF32(d, B + 48 * i + 4 * k, (float)v[k]);
            }
            var names = sk.Joints.Select((j, i) => (j.Hash, i)).OrderBy(x => x.Hash).ToList();
            for (int k = 0; k < n; k++)
            {
                HmCore.PutU32(d, N + 8 * k, names[k].Hash);
                HmCore.PutS32(d, N + 8 * k + 4, names[k].i);
            }
            return d;
        }

        private static int Align(int v, int a) => (v + a - 1) / a * a;

        /// <summary>First child / next sibling from the parent indices, children in index order (as in shipped files).</summary>
        public static void RebuildLinks(HmSkeletonFile sk)
        {
            foreach (var j in sk.Joints) { j.FirstChild = -1; j.NextSibling = -1; }
            var lastChild = new Dictionary<int, int>();
            for (int i = 0; i < sk.Joints.Count; i++)
            {
                int p = sk.Joints[i].Parent;
                if (p < 0)
                {
                    // roots chain as siblings of each other
                    int prevRoot = -1;
                    for (int k = i - 1; k >= 0; k--) if (sk.Joints[k].Parent < 0) { prevRoot = k; break; }
                    if (prevRoot >= 0) sk.Joints[prevRoot].NextSibling = (short)i;
                    continue;
                }
                if (lastChild.TryGetValue(p, out int prev)) sk.Joints[prev].NextSibling = (short)i;
                else sk.Joints[p].FirstChild = (short)i;
                lastChild[p] = i;
            }
        }

        /// <summary>Skeleton key for a new skeleton (any value works; the MDL that uses the skeleton must store the same key).</summary>
        public static uint NewKey(HmSkeletonFile sk)
        {
            var b = new byte[sk.Joints.Count * 4];
            for (int i = 0; i < sk.Joints.Count; i++) HmCore.PutU32(b, 4 * i, sk.Joints[i].Hash);
            uint k = ~HmCore.Crc32(b);
            return k == 0 ? 1u : k;
        }

        // ------------------------------------------------------------------ matrices (column-major 4x4)

        public static double[] FromQts(double[] q, double[] t, double[] s)
        {
            double x = q[0], y = q[1], z = q[2], w = q[3];
            double s0 = s?[0] ?? 1, s1 = s?[1] ?? 1, s2 = s?[2] ?? 1;
            return new[]
            {
                (1 - 2 * (y * y + z * z)) * s0, 2 * (x * y + z * w) * s0, 2 * (x * z - y * w) * s0, 0,
                2 * (x * y - z * w) * s1, (1 - 2 * (x * x + z * z)) * s1, 2 * (y * z + x * w) * s1, 0,
                2 * (x * z + y * w) * s2, 2 * (y * z - x * w) * s2, (1 - 2 * (x * x + y * y)) * s2, 0,
                t[0], t[1], t[2], 1,
            };
        }

        /// <summary>Local matrix as GpSkeleton::GetLocalMatrix builds it (parent scale compensation for flag 1).</summary>
        public static double[] LocalMatrix(HmSkeletonFile sk, int k, double[] q, double[] t, double[] s, double[] parentScale)
        {
            var m = FromQts(q, t, s);
            var j = sk.Joints[k];
            if (parentScale != null && (j.Flags & 1) != 0 && j.Parent >= 0)
                for (int c = 0; c < 3; c++)
                    for (int r = 0; r < 3; r++) m[4 * c + r] /= parentScale[r] == 0 ? 1 : parentScale[r];
            return m;
        }

        /// <summary>Bind-pose world matrices.</summary>
        public static List<double[]> BindWorld(HmSkeletonFile sk)
        {
            var outL = new List<double[]>();
            foreach (var j in sk.Joints)
            {
                var m = LocalMatrix(sk, j.Index, j.Q, j.T, j.S, j.Parent >= 0 ? sk.Joints[j.Parent].S : null);
                if (j.Parent >= 0 && j.Parent < outL.Count) m = Mat4.Mul(outL[j.Parent], m);
                outL.Add(m);
            }
            return outL;
        }

        /// <summary>Affine inverse.</summary>
        public static double[] Inverse(double[] m)
        {
            double a = m[0], b = m[1], c = m[2], d = m[4], e = m[5], f = m[6], g = m[8], h = m[9], i = m[10];
            double A = e * i - f * h, B = -(d * i - f * g), C = d * h - e * g, det = a * A + b * B + c * C;
            if (Math.Abs(det) < 1e-30) det = 1e-30;
            var r = new double[16];
            r[0] = A / det; r[1] = -(b * i - c * h) / det; r[2] = (b * f - c * e) / det;
            r[4] = B / det; r[5] = (a * i - c * g) / det; r[6] = -(a * f - c * d) / det;
            r[8] = C / det; r[9] = -(a * h - b * g) / det; r[10] = (a * e - b * d) / det;
            r[12] = -(r[0] * m[12] + r[4] * m[13] + r[8] * m[14]);
            r[13] = -(r[1] * m[12] + r[5] * m[13] + r[9] * m[14]);
            r[14] = -(r[2] * m[12] + r[6] * m[13] + r[10] * m[14]);
            r[15] = 1;
            return r;
        }

        /// <summary>Matrix -> translation, unit quaternion (xyzw), scale.</summary>
        public static (double[] T, double[] Q, double[] S) Decompose(double[] m)
        {
            double sx = Hyp(m[0], m[1], m[2]), sy = Hyp(m[4], m[5], m[6]), sz = Hyp(m[8], m[9], m[10]);
            if (sx == 0) sx = 1; if (sy == 0) sy = 1; if (sz == 0) sz = 1;
            if (Mat4.Det3(m) < 0) sx = -sx;
            double a = m[0] / sx, b = m[1] / sx, c = m[2] / sx, d = m[4] / sy, e = m[5] / sy, f = m[6] / sy, g = m[8] / sz, h = m[9] / sz, i = m[10] / sz;
            double[] q;
            double tr = a + e + i;
            if (tr > 0) { double s = Math.Sqrt(tr + 1) * 2; q = new[] { (f - h) / s, (g - c) / s, (b - d) / s, s / 4 }; }
            else if (a > e && a > i) { double s = Math.Sqrt(1 + a - e - i) * 2; q = new[] { s / 4, (d + b) / s, (g + c) / s, (f - h) / s }; }
            else if (e > i) { double s = Math.Sqrt(1 + e - a - i) * 2; q = new[] { (d + b) / s, s / 4, (h + f) / s, (g - c) / s }; }
            else { double s = Math.Sqrt(1 + i - a - e) * 2; q = new[] { (g + c) / s, (h + f) / s, s / 4, (b - d) / s }; }
            double l = Math.Sqrt(q.Sum(x => x * x));
            if (l == 0) l = 1;
            return (new[] { m[12], m[13], m[14] }, q.Select(x => x / l).ToArray(), new[] { sx, sy, sz });
        }

        private static double Hyp(double a, double b, double c) => Math.Sqrt(a * a + b * b + c * c);
    }

    /// <summary>Big-endian reads for the Home formats.</summary>
    internal static class HmCoreBE
    {
        public static uint U32(byte[] d, int o) => System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(o, 4));
        public static int S32(byte[] d, int o) => System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(o, 4));
        public static ushort U16(byte[] d, int o) => System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(o, 2));
        public static short S16(byte[] d, int o) => System.Buffers.Binary.BinaryPrimitives.ReadInt16BigEndian(d.AsSpan(o, 2));
        public static float F32(byte[] d, int o) => BitConverter.Int32BitsToSingle(S32(d, o));
    }
}

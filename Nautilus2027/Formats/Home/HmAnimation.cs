#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// Home animation clips (.ANI, FpAnimClipDef 'AC11'). Layout from the HDK eboot's debug info (FpAnimClipDef, FpAnimItem,
// FpAnimChannelInfo, FpAnimTimeblockIndex, FpAnimTimeblock, FpAnimTimeblockChannelHeader) and the HOME_DEV_KIT reader
// (ws/mdlb/src/11.js) / writer (ws/fanwork/ac11.py, verified in game):
//   +0 'AC11' +4 flags +8 duration +12 item count +14 timeblock count +16 channel count
//   +20 constants (vec4) +24 constant index array +28 timeblock index (timeMin, start, end) +32 locomotion info
//   +36 end of SPU DMA +40 channel-name hashes +44 channel infos +48 items (joint hash, channel count, channel infos)
//   +52 hermite keys (value, tangent vec4) +56 half hermite +60 packed bitstream +64 packing specs +68 spec index
//   +72 key transforms +76 transform index +80 channel count per type +90 first channel per type
//   (types: 0 constant, 1 constant-indexed, 2 hermite, 3 half hermite, 4 packed hermite).
// A timeblock: u16 time type (0 float / 1 byte / 2 short), s16 animated channel count, f32 time offset, f32 time step,
// then 8 B per animated channel (s32 first-key bit index, s16 key count - 1, s16 first time index), then the times.
// Keys are joined with cubic Hermite curves; tangents are per second. Rotations are quaternions, normalised after evaluation.
namespace Nautilus2027.Formats.Home
{
    public sealed class AniKey
    {
        public double T;
        public double[] V = new double[4];
        public double[] G = new double[4];
    }

    public sealed class AniChannel
    {
        public uint Name;            // hash of rotate / translate / scale (or a user channel)
        /// <summary>Value of a constant channel; null for an animated one.</summary>
        public double[] Constant;
        public List<AniKey> Keys = new List<AniKey>();
        public bool IsConstant => Constant != null;

        /// <summary>Value at time t (cubic Hermite between keys, held before the first / after the last key).</summary>
        public double[] Evaluate(double t) => Evaluate(t, out _);

        public double[] Evaluate(double t, out double[] derivative)
        {
            derivative = new double[4];
            if (Constant != null) return (double[])Constant.Clone();
            int n = Keys.Count;
            if (n == 0) return new double[] { 0, 0, 0, 1 };
            if (n == 1 || t <= Keys[0].T) return (double[])(t <= Keys[0].T && n > 1 ? Keys[0].V : Keys[n - 1].V).Clone();
            if (t >= Keys[n - 1].T) return (double[])Keys[n - 1].V.Clone();
            int lo = 0, hi = n - 1;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (Keys[mid].T <= t) lo = mid; else hi = mid; }
            var a = Keys[lo]; var b = Keys[lo + 1];
            double dt = b.T - a.T;
            if (!(dt > 0)) return (double[])a.V.Clone();
            double u = (t - a.T) / dt, u2 = u * u, u3 = u2 * u;
            double h00 = 2 * u3 - 3 * u2 + 1, h10 = (u3 - 2 * u2 + u) * dt, h01 = -2 * u3 + 3 * u2, h11 = (u3 - u2) * dt;
            double d00 = (6 * u2 - 6 * u) / dt, d10 = 3 * u2 - 4 * u + 1, d01 = (-6 * u2 + 6 * u) / dt, d11 = 3 * u2 - 2 * u;
            var r = new double[4];
            for (int i = 0; i < 4; i++)
            {
                r[i] = h00 * a.V[i] + h10 * a.G[i] + h01 * b.V[i] + h11 * b.G[i];
                derivative[i] = d00 * a.V[i] + d10 * a.G[i] + d01 * b.V[i] + d11 * b.G[i];
            }
            return r;
        }
    }

    public sealed class AniItem
    {
        public uint Hash;
        /// <summary>Channels of this item in file order.</summary>
        public List<AniChannel> Channels = new List<AniChannel>();
        public string Name => HmSkeleton.JointLabel(Hash);

        public AniChannel Get(uint name) => Channels.FirstOrDefault(c => c.Name == name);
    }

    public sealed class HmAnimClip
    {
        public float Duration;
        public int Flags;
        public List<AniItem> Items = new List<AniItem>();
        /// <summary>Facts about the source file, for Inspect.</summary>
        public int Timeblocks, ChannelCount;
        public int[] TypeCounts = new int[5];
        public bool HasLocomotion;

        public AniItem Item(uint hash) => Items.FirstOrDefault(i => i.Hash == hash);
    }

    public static class HmAnimation
    {
        public static readonly uint HRotate = HmCore.HashName("rotate");
        public static readonly uint HTranslate = HmCore.HashName("translate");
        public static readonly uint HScale = HmCore.HashName("scale");

        public static string ChannelLabel(uint h) => h == HRotate ? "rotate" : h == HTranslate ? "translate" : h == HScale ? "scale" : "0x" + h.ToString("x8");

        public static bool LooksLikeAni(byte[] b) => b != null && b.Length > 112 && b[0] == 0x41 && b[1] == 0x43 && b[2] == 0x31 && b[3] == 0x31;

        private static readonly int[] Bias = { 0x80, 0x7f, 0x7e, 0x7c, 0x78, 0x70, 0x60, 0x40, 0x0, -0x80, -0x180 };

        // ------------------------------------------------------------------ reader

        public static HmAnimClip Read(byte[] d)
        {
            if (!LooksLikeAni(d)) throw new InvalidDataException("not a Home animation (AC11)");
            try
            {
                return ReadUnchecked(d);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new InvalidDataException("the animation is truncated or corrupt");
            }
            catch (IndexOutOfRangeException)
            {
                throw new InvalidDataException("the animation is truncated or corrupt");
            }
        }

        private static HmAnimClip ReadUnchecked(byte[] d)
        {
            ushort U16(int o) => HmCoreBE.U16(d, o);
            short S16(int o) => HmCoreBE.S16(d, o);
            uint U32(int o) => HmCoreBE.U32(d, o);
            float F(int o) => HmCoreBE.F32(d, o);
            int Rel(int o) { int v = HmCoreBE.S32(d, o); return v == 0 ? 0 : o + v; }
            double[] Vec(int o) => new double[] { F(o), F(o + 4), F(o + 8), F(o + 12) };

            var clip = new HmAnimClip { Flags = HmCoreBE.S32(d, 4), Duration = F(8) };
            int nItems = S16(12), nTb = S16(14), nCh = S16(16);
            if (nItems < 0 || nTb < 0 || nCh < 0) throw new InvalidDataException("counts are not plausible");
            int cnst = Rel(20), cIdx = Rel(24), tbIdx = Rel(28), names = Rel(40), items = Rel(48), herm = Rel(52), half = Rel(56), bits = Rel(60),
                specs = Rel(64), specIdx = Rel(68), xf = Rel(72), xfIdx = Rel(76);
            clip.HasLocomotion = Rel(32) != 0;
            var tcount = new int[5];
            var tstart = new int[5];
            for (int i = 0; i < 5; i++) { tcount[i] = U16(80 + 2 * i); tstart[i] = U16(90 + 2 * i); }
            clip.TypeCounts = tcount;
            clip.Timeblocks = nTb;
            clip.ChannelCount = nCh;
            int CType(int c) { for (int t = 4; t >= 0; t--) if (tcount[t] > 0 && c >= tstart[t]) return t; return 0; }

            // MSB-first bit reader for packed keys
            long Bits(int byteAddr, long pos, int n)
            {
                long v = 0, p = (long)byteAddr * 8 + pos;
                for (int i = 0; i < n; i++, p++) v = v * 2 + ((d[p >> 3] >> (7 - (int)(p & 7))) & 1);
                return v;
            }
            double[][] DecodePacked(int spec, long pos)
            {
                var outV = new double[2][];
                for (int part = 0; part < 2; part++)
                {
                    int sb = spec + (part == 1 ? 7 : 2);
                    var comps = new double[4];
                    for (int c = 0; c < 4; c++)
                    {
                        int h = (int)Bits(sb, 10 * c, 10), sg = h >> 9, ne = (h >> 5) & 15, nm = h & 31, tot = sg + ne + nm;
                        long v = tot > 0 ? Bits(bits, pos, tot) : 0;
                        pos += tot;
                        if (ne > 0)
                        {
                            long mantMask = (1L << nm) - 1, mant = v & mantMask;
                            mant = nm <= 23 ? mant << (23 - nm) : mant >> (nm - 23);
                            int e = (int)((((v >> nm) & ((1L << ne) - 1)) + Bias[ne]) & 0xff);
                            int sign = sg > 0 ? (int)((v >> (ne + nm)) & 1) : 0;
                            comps[c] = BitConverter.Int32BitsToSingle((sign << 31) | (e << 23) | (int)mant);
                        }
                        else if (nm == 0) comps[c] = 0;
                        else
                        {
                            long mask = (1L << nm) - 1;
                            if (sg > 0 && v >= (1L << nm)) v -= 1L << (nm + 1);
                            comps[c] = (double)v / mask;
                        }
                    }
                    outV[part] = comps;
                }
                return outV;
            }
            double[] Xform(int xi, double[] v, bool tangent)
            {
                if (xi == 0) return v;
                int o = xf + 48 * xi;
                var r = tangent ? new double[4] : Vec(o + 32);
                for (int i = 0; i < 4; i++)
                    for (int k = 0; k < 4; k++) r[k] += S16(o + 2 * (4 * i + k)) / 32767.0 * v[i];
                return r;
            }
            double[] Rebuild(int mode, double[] v)
            {
                int ci = mode == 24 ? 0 : mode == 20 ? 1 : mode == 18 ? 2 : mode == 17 ? 3 : -1;
                if (ci < 0) return v;
                double s = 0;
                for (int k = 0; k < 4; k++) if (k != ci) s += v[k] * v[k];
                var r = (double[])v.Clone();
                r[ci] = (v[ci] >= 0 ? 1 : -1) * Math.Sqrt(Math.Max(0, 1 - s));
                return r;
            }

            var channels = new AniChannel[nCh];
            for (int c = 0; c < nCh; c++)
            {
                int ty = CType(c);
                if (ty == 0) channels[c] = new AniChannel { Constant = Vec(cnst + 16 * c) };
                else if (ty == 1) channels[c] = new AniChannel { Constant = Vec(cnst + 16 * U16(cIdx + 2 * (c - tstart[1]))) };
                else channels[c] = new AniChannel();
            }
            int animatedStart = tcount[0] + tcount[1];
            for (int b = 0; b < nTb; b++)
            {
                int tb = Rel(tbIdx + 12 * b + 4), hdr0 = (tb + 15) & ~3, nAnim = S16(tb + 2), type = U16(tb);
                float toff = F(tb + 4), tstep = F(tb + 8);
                int times0 = (hdr0 + nAnim * 8 + 15) & ~15;
                for (int c = animatedStart; c < nCh && c - animatedStart < nAnim; c++)
                {
                    int hdr = hdr0 + (c - animatedStart) * 8, ty = CType(c);
                    int n = S16(hdr + 4) + 1, first = S16(hdr + 6), bitIdx = HmCoreBE.S32(d, hdr);
                    var list = channels[c].Keys;
                    for (int i = 0; i < n; i++)
                    {
                        double t = type == 0 ? F(times0 + 4 * (first + i)) : toff + (type == 1 ? d[times0 + first + i] : U16(times0 + 2 * (first + i))) * tstep;
                        var key = new AniKey { T = t };
                        if (ty == 2)
                        {
                            int o = herm + (bitIdx >> 3) + 32 * i;
                            key.V = Vec(o);
                            key.G = Vec(o + 16);
                        }
                        else if (ty == 3)
                        {
                            int o = half + (bitIdx >> 3) + 16 * i;
                            for (int q = 0; q < 4; q++) { key.V[q] = Half(U16(o + 2 * q)); key.G[q] = Half(U16(o + 8 + 2 * q)); }
                        }
                        else
                        {
                            int spec = specs + 12 * U16(specIdx + 2 * (c - tstart[4])), xi = U16(xfIdx + 2 * (c - tstart[4]));
                            int nb = U16(spec) & 0x1ff, mode = (U16(spec) >> 9) & 31;
                            var vg = DecodePacked(spec, (long)bitIdx + (long)i * nb);
                            key.V = Rebuild(mode, Xform(xi, vg[0], false));
                            key.G = Xform(xi, vg[1], true);
                        }
                        // the closing key of a block repeats the first key of the next one
                        if (list.Count > 0 && t <= list[list.Count - 1].T + 1e-7) continue;
                        list.Add(key);
                    }
                }
            }

            for (int i = 0; i < nItems; i++)
            {
                int o = items + 12 * i, cnt = HmCoreBE.S32(d, o + 4), ci = Rel(o + 8);
                var item = new AniItem { Hash = U32(o) };
                for (int k = 0; k < cnt; k++)
                {
                    int p = ci + 3 * k;
                    uint nh = U32(names + 4 * d[p]);
                    int g = (d[p + 1] << 8) | d[p + 2];
                    if (g < 0 || g >= nCh) continue;
                    var src = channels[g];
                    // channels can be shared between items in principle: give each item its own copy
                    item.Channels.Add(new AniChannel { Name = nh, Constant = src.Constant, Keys = src.Keys });
                }
                clip.Items.Add(item);
            }
            return clip;
        }

        private static double Half(ushort h)
        {
            int s = h >> 15, e = (h >> 10) & 31, m = h & 1023;
            double v = e == 0 ? m * Math.Pow(2, -24) : e == 31 ? (m != 0 ? double.NaN : double.PositiveInfinity) : (1 + m / 1024.0) * Math.Pow(2, e - 15);
            return s != 0 ? -v : v;
        }

        // ------------------------------------------------------------------ writer

        /// <summary>
        /// Write a clip: constant channels first (type 0), animated channels as full Hermite keys (type 2) with float times,
        /// items sorted by hash - the layout of the kit's in-game-verified writer. Long clips are split into several timeblocks.
        /// </summary>
        public static byte[] Write(HmAnimClip clip)
        {
            foreach (var it in clip.Items) it.Channels.RemoveAll(c => !c.IsConstant && c.Keys.Count == 0);
            var items = clip.Items.Where(i => i.Channels.Count > 0).OrderBy(i => i.Hash).ToList();
            if (items.Select(i => i.Hash).Distinct().Count() != items.Count) throw new InvalidDataException("two items animate the same joint");
            var consts = new List<(int Item, AniChannel Ch)>();
            var herms = new List<(int Item, AniChannel Ch)>();
            for (int i = 0; i < items.Count; i++)
                foreach (var ch in items[i].Channels)
                {
                    if (ch.IsConstant || ch.Keys.Count <= 1) consts.Add((i, ch));
                    else herms.Add((i, ch));
                }
            int nC = consts.Count + herms.Count;
            if (nC > 32767) throw new InvalidDataException("too many channels");
            var chanOf = new Dictionary<AniChannel, int>();
            for (int k = 0; k < consts.Count; k++) chanOf[consts[k].Ch] = k;
            for (int k = 0; k < herms.Count; k++) chanOf[herms[k].Ch] = consts.Count + k;

            var nameOrder = new List<uint> { HTranslate, HRotate, HScale };
            foreach (var it in items) foreach (var ch in it.Channels) if (!nameOrder.Contains(ch.Name)) nameOrder.Add(ch.Name);
            if (nameOrder.Count > 255) throw new InvalidDataException("too many channel names");

            var blocks = SplitBlocks(herms.Select(h => h.Ch).ToList(), clip.Duration);

            var buf = new List<byte>(4096);
            void Pad(int a) { while (buf.Count % a != 0) buf.Add(0); }
            void AddU32(uint v) { buf.Add((byte)(v >> 24)); buf.Add((byte)(v >> 16)); buf.Add((byte)(v >> 8)); buf.Add((byte)v); }
            void AddF32(double v) => AddU32((uint)BitConverter.SingleToInt32Bits((float)v));
            void SetU32(int o, uint v) { buf[o] = (byte)(v >> 24); buf[o + 1] = (byte)(v >> 16); buf[o + 2] = (byte)(v >> 8); buf[o + 3] = (byte)v; }
            void SetU16(int o, int v) { buf[o] = (byte)(v >> 8); buf[o + 1] = (byte)v; }
            void SetRel(int o, int target) => SetU32(o, target != 0 ? (uint)(target - o) : 0);

            buf.AddRange(new byte[0x70]);
            buf[0] = 0x41; buf[1] = 0x43; buf[2] = 0x31; buf[3] = 0x31;
            SetU32(4, (uint)clip.Flags);
            SetU32(8, (uint)BitConverter.SingleToInt32Bits(clip.Duration));
            SetU16(12, items.Count);
            SetU16(14, blocks.Count);
            SetU16(16, nC);
            int[] tcount = { consts.Count, 0, herms.Count, 0, 0 };
            int[] tstart = { 0, consts.Count, consts.Count, nC, nC };
            for (int i = 0; i < 5; i++) { SetU16(0x50 + 2 * i, tcount[i]); SetU16(0x5A + 2 * i, tstart[i]); }

            int cnst = buf.Count;
            foreach (var (_, ch) in consts) { var v = ch.IsConstant ? ch.Constant : ch.Keys[0].V; for (int k = 0; k < 4; k++) AddF32(v[k]); }
            int tbi = buf.Count;
            buf.AddRange(new byte[12 * blocks.Count]);
            Pad(16);
            int itab = buf.Count;
            buf.AddRange(new byte[12 * items.Count]);
            int names = buf.Count;
            foreach (var h in nameOrder) AddU32(h);
            int ci0 = buf.Count;
            for (int i = 0; i < items.Count; i++)
            {
                SetU32(itab + 12 * i, items[i].Hash);
                SetU32(itab + 12 * i + 4, (uint)items[i].Channels.Count);
                SetRel(itab + 12 * i + 8, buf.Count);
                foreach (var ch in items[i].Channels)
                {
                    int g = chanOf[ch];
                    buf.Add((byte)nameOrder.IndexOf(ch.Name));
                    buf.Add((byte)(g >> 8));
                    buf.Add((byte)g);
                }
            }
            Pad(16);
            int herm = buf.Count;
            // keys: block by block, channel by channel
            var bitIdx = new int[blocks.Count, herms.Count];
            for (int b = 0; b < blocks.Count; b++)
                for (int c = 0; c < herms.Count; c++)
                {
                    bitIdx[b, c] = (buf.Count - herm) * 8;
                    foreach (var k in blocks[b].Keys[c]) { for (int q = 0; q < 4; q++) AddF32(k.V[q]); for (int q = 0; q < 4; q++) AddF32(k.G[q]); }
                }
            int bitsArr = buf.Count;
            for (int b = 0; b < blocks.Count; b++)
            {
                Pad(128);
                int tb = buf.Count;
                buf.AddRange(new byte[12]);
                SetU16(tb, 0);
                SetU16(tb + 2, herms.Count);
                SetU32(tb + 4, (uint)BitConverter.SingleToInt32Bits(0f));
                SetU32(tb + 8, (uint)BitConverter.SingleToInt32Bits(0.0010681152f));
                int hdr0 = (tb + 15) & ~3;
                while (buf.Count < hdr0) buf.Add(0);
                int first = 0;
                for (int c = 0; c < herms.Count; c++)
                {
                    int n = blocks[b].Keys[c].Count;
                    AddU32((uint)bitIdx[b, c]);
                    buf.Add((byte)((n - 1) >> 8)); buf.Add((byte)(n - 1));
                    buf.Add((byte)(first >> 8)); buf.Add((byte)first);
                    first += n;
                }
                int times0 = (buf.Count + 15) & ~15;
                while (buf.Count < times0) buf.Add(0);
                for (int c = 0; c < herms.Count; c++) foreach (var k in blocks[b].Keys[c]) AddF32(k.T);
                Pad(16);
                int end = buf.Count;
                SetU32(tbi + 12 * b, (uint)BitConverter.SingleToInt32Bits((float)blocks[b].T0));
                SetRel(tbi + 12 * b + 4, tb);
                SetRel(tbi + 12 * b + 8, end);
            }
            // header pointers: the same targets as the kit's writer (and the shipped clips it was matched to)
            foreach (var (o, t) in new[] { (0x14, cnst), (0x18, tbi), (0x1C, tbi), (0x20, 0), (0x24, itab), (0x28, names), (0x2C, ci0), (0x30, itab), (0x34, herm),
                                          (0x38, 0), (0x3C, bitsArr), (0x40, itab), (0x44, itab), (0x48, itab), (0x4C, itab) })
                SetRel(o, t);
            return buf.ToArray();
        }

        private sealed class Block
        {
            public double T0, T1;
            public List<List<AniKey>> Keys = new List<List<AniKey>>();
        }

        /// <summary>One timeblock, or several when the key / time counts would overflow a block's 16-bit indices.</summary>
        private static List<Block> SplitBlocks(List<AniChannel> herms, double duration)
        {
            long total = herms.Sum(h => (long)h.Keys.Count);
            int maxPerChannel = herms.Count == 0 ? 0 : herms.Max(h => h.Keys.Count);
            int nb = (int)Math.Max(1, Math.Max(Math.Ceiling(total / 30000.0), Math.Ceiling(maxPerChannel / 30000.0)));
            if (nb == 1)
                return new List<Block> { new Block { T0 = 0, T1 = duration, Keys = herms.Select(h => h.Keys).ToList() } };
            double end = Math.Max(duration, herms.Max(h => h.Keys[h.Keys.Count - 1].T));
            var blocks = new List<Block>();
            for (int b = 0; b < nb; b++)
            {
                double t0 = end * b / nb, t1 = end * (b + 1) / nb;
                var blk = new Block { T0 = t0, T1 = t1 };
                foreach (var h in herms)
                {
                    var list = new List<AniKey>();
                    AniKey At(double t) { var v = h.Evaluate(t, out var g); return new AniKey { T = t, V = v, G = g }; }
                    if (h.Keys[0].T > t0 + 1e-9 || !h.Keys.Any(k => Math.Abs(k.T - t0) < 1e-9)) list.Add(At(t0));
                    list.AddRange(h.Keys.Where(k => k.T >= t0 - 1e-9 && k.T < t1 - 1e-9));
                    var closing = h.Keys.FirstOrDefault(k => Math.Abs(k.T - t1) < 1e-9) ?? At(t1);
                    list.Add(closing);
                    blk.Keys.Add(list);
                }
                blocks.Add(blk);
            }
            return blocks;
        }
    }
}

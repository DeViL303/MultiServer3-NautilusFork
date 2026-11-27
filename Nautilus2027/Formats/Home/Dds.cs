#nullable disable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

// DDS writer (DXT1 / DXT5 with full mip chain, or uncompressed A8R8G8B8) and a reader for DXT1/3/5 and
// uncompressed DDS. C# port of the HOME_DEV_KIT DDS module (ws/mdlb/src/02.js).
namespace Nautilus2027.Formats.Home
{
    /// <summary>8-bit RGBA image, rows top to bottom, not premultiplied.</summary>
    public sealed class RgbaImage
    {
        public int Width, Height;
        public byte[] Data;

        public RgbaImage(int width, int height, byte[] data = null)
        {
            Width = width;
            Height = height;
            Data = data ?? new byte[width * height * 4];
        }

        public RgbaImage Clone() => new RgbaImage(Width, Height, (byte[])Data.Clone());

        public static RgbaImage Solid(int w, int h, byte[] rgba)
        {
            var img = new RgbaImage(w, h);
            for (int i = 0; i < w * h; i++) Buffer.BlockCopy(rgba, 0, img.Data, 4 * i, 4);
            return img;
        }
    }

    /// <summary>Header facts of an existing DDS (to re-encode a texture the way it was).</summary>
    public sealed class DdsInfo
    {
        public string Format;   // DXT1 | DXT3 | DXT5 | ARGB | RGB<bits> | other FourCC
        public int Width, Height, Mips;
        public bool Cube;
    }

    public sealed class DdsResult
    {
        public byte[] Bytes;
        public string Format;
        public int Width, Height, Mips;
        public RgbaImage Image;
    }

    public static class Dds
    {
        public static int NextPow2(int n)
        {
            int p = 1;
            while (p < n) p <<= 1;
            return p;
        }

        /// <summary>Area resample (box filter when shrinking).</summary>
        public static RgbaImage Resize(RgbaImage img, int w, int h)
        {
            if (img.Width == w && img.Height == h) return img;
            var outD = new byte[w * h * 4];
            double sx = (double)img.Width / w, sy = (double)img.Height / h;
            var src = img.Data;
            var acc = new double[4];
            for (int y = 0; y < h; y++)
            {
                double y0 = y * sy, y1 = Math.Max(y0 + 1e-6, (y + 1) * sy);
                for (int x = 0; x < w; x++)
                {
                    double x0 = x * sx, x1 = Math.Max(x0 + 1e-6, (x + 1) * sx);
                    acc[0] = acc[1] = acc[2] = acc[3] = 0;
                    double wt = 0;
                    for (int yy = (int)Math.Floor(y0); yy < (int)Math.Ceiling(y1); yy++)
                    {
                        double wy = Math.Min(yy + 1, y1) - Math.Max(yy, y0);
                        if (wy <= 0) continue;
                        int ry = Math.Min(img.Height - 1, yy);
                        for (int xx = (int)Math.Floor(x0); xx < (int)Math.Ceiling(x1); xx++)
                        {
                            double wx = Math.Min(xx + 1, x1) - Math.Max(xx, x0);
                            if (wx <= 0) continue;
                            int o = (ry * img.Width + Math.Min(img.Width - 1, xx)) * 4;
                            double ww = wx * wy;
                            acc[0] += src[o] * ww; acc[1] += src[o + 1] * ww; acc[2] += src[o + 2] * ww; acc[3] += src[o + 3] * ww;
                            wt += ww;
                        }
                    }
                    int oo = (y * w + x) * 4;
                    for (int c = 0; c < 4; c++) outD[oo + c] = (byte)Math.Max(0, Math.Min(255, HmCore.JsRound(acc[c] / wt)));
                }
            }
            return new RgbaImage(w, h, outD);
        }

        private static RgbaImage Half(RgbaImage img) => Resize(img, Math.Max(1, img.Width >> 1), Math.Max(1, img.Height >> 1));

        private static int To565(int r, int g, int b) => ((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3);

        private static double[] From565(int c) => new[] { ((c >> 11) & 31) * 255.0 / 31, ((c >> 5) & 63) * 255.0 / 63, (c & 31) * 255.0 / 31 };

        private static int[][] Block(RgbaImage img, int bx, int by)
        {
            var px = new int[16][];
            for (int j = 0; j < 4; j++)
                for (int i = 0; i < 4; i++)
                {
                    int x = Math.Min(img.Width - 1, bx + i), y = Math.Min(img.Height - 1, by + j), o = (y * img.Width + x) * 4;
                    px[j * 4 + i] = new int[] { img.Data[o], img.Data[o + 1], img.Data[o + 2], img.Data[o + 3] };
                }
            return px;
        }

        private static void ColourBlock(int[][] px, byte[] outB, int off)
        {
            // principal axis endpoints (power iteration on covariance), inset slightly
            var mean = new double[3];
            foreach (var p in px) for (int c = 0; c < 3; c++) mean[c] += p[c] / 16.0;
            var cov = new double[6];
            foreach (var p in px)
            {
                double r = p[0] - mean[0], g = p[1] - mean[1], b = p[2] - mean[2];
                cov[0] += r * r; cov[1] += r * g; cov[2] += r * b; cov[3] += g * g; cov[4] += g * b; cov[5] += b * b;
            }
            var ax = new double[] { 1, 1, 1 };
            for (int it = 0; it < 8; it++)
            {
                double n0 = cov[0] * ax[0] + cov[1] * ax[1] + cov[2] * ax[2];
                double n1 = cov[1] * ax[0] + cov[3] * ax[1] + cov[4] * ax[2];
                double n2 = cov[2] * ax[0] + cov[4] * ax[1] + cov[5] * ax[2];
                double l = Math.Sqrt(n0 * n0 + n1 * n1 + n2 * n2);
                if (l < 1e-9) break;
                ax[0] = n0 / l; ax[1] = n1 / l; ax[2] = n2 / l;
            }
            double mn = double.PositiveInfinity, mx = double.NegativeInfinity;
            int[] pmn = px[0], pmx = px[0];
            foreach (var p in px)
            {
                double t = (p[0] - mean[0]) * ax[0] + (p[1] - mean[1]) * ax[1] + (p[2] - mean[2]) * ax[2];
                if (t < mn) { mn = t; pmn = p; }
                if (t > mx) { mx = t; pmx = p; }
            }
            int[] Inset(int[] a, int[] b)
            {
                var r = new int[3];
                for (int c = 0; c < 3; c++) r[c] = (int)Math.Max(0, Math.Min(255, HmCore.JsRound(a[c] + (b[c] - a[c]) / 16.0)));
                return r;
            }
            int[] hi = Inset(pmx, pmn), lo = Inset(pmn, pmx);
            int c0 = To565(hi[0], hi[1], hi[2]), c1 = To565(lo[0], lo[1], lo[2]);
            if (c0 < c1) { int t = c0; c0 = c1; c1 = t; }
            uint idx = 0;
            if (c0 != c1)
            {
                double[] e0 = From565(c0), e1 = From565(c1);
                var pal = new double[4][];
                pal[0] = e0;
                pal[1] = e1;
                pal[2] = new double[3];
                pal[3] = new double[3];
                for (int c = 0; c < 3; c++) { pal[2][c] = (2 * e0[c] + e1[c]) / 3; pal[3][c] = (e0[c] + 2 * e1[c]) / 3; }
                for (int k = 15; k >= 0; k--)
                {
                    var p = px[k];
                    int best = 0;
                    double bd = double.PositiveInfinity;
                    for (int q = 0; q < 4; q++)
                    {
                        double dr = p[0] - pal[q][0], dg = p[1] - pal[q][1], db = p[2] - pal[q][2];
                        double d = dr * dr + dg * dg + db * db;
                        if (d < bd) { bd = d; best = q; }
                    }
                    idx = (idx << 2) | (uint)best;
                }
            }
            outB[off] = (byte)(c0 & 255); outB[off + 1] = (byte)(c0 >> 8); outB[off + 2] = (byte)(c1 & 255); outB[off + 3] = (byte)(c1 >> 8);
            outB[off + 4] = (byte)(idx & 255); outB[off + 5] = (byte)((idx >> 8) & 255); outB[off + 6] = (byte)((idx >> 16) & 255); outB[off + 7] = (byte)((idx >> 24) & 255);
        }

        private static void AlphaBlock(int[][] px, byte[] outB, int off)
        {
            int a0 = 0, a1 = 255;
            foreach (var p in px) { a0 = Math.Max(a0, p[3]); a1 = Math.Min(a1, p[3]); }
            outB[off] = (byte)a0;
            outB[off + 1] = (byte)a1;
            ulong bits = 0;
            if (a0 != a1)
            {
                var pal = new double[8];
                pal[0] = a0; pal[1] = a1;
                for (int k = 1; k <= 6; k++) pal[k + 1] = ((7 - k) * a0 + k * a1) / 7.0;
                for (int k = 15; k >= 0; k--)
                {
                    int best = 0;
                    double bd = double.PositiveInfinity;
                    for (int q = 0; q < 8; q++)
                    {
                        double d = Math.Abs(px[k][3] - pal[q]);
                        if (d < bd) { bd = d; best = q; }
                    }
                    bits = (bits << 3) | (uint)best;
                }
            }
            for (int k = 0; k < 6; k++) outB[off + 2 + k] = (byte)((bits >> (8 * k)) & 255);
        }

        private static byte[] Compress(RgbaImage img, string fmt)
        {
            int bw = Math.Max(1, (img.Width + 3) / 4), bh = Math.Max(1, (img.Height + 3) / 4);
            int bsz = fmt == "DXT1" ? 8 : 16;
            var outB = new byte[bw * bh * bsz];
            int off = 0;
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    var px = Block(img, bx * 4, by * 4);
                    if (fmt == "DXT5") { AlphaBlock(px, outB, off); off += 8; }
                    ColourBlock(px, outB, off);
                    off += 8;
                }
            return outB;
        }

        public static bool HasAlpha(RgbaImage img)
        {
            for (int i = 3; i < img.Data.Length; i += 4)
                if (img.Data[i] < 250) return true;
            return false;
        }

        /// <summary>Power-of-two resize (optional) then halve until within <paramref name="maxSize"/> (0 = no limit).</summary>
        public static RgbaImage Prepare(RgbaImage img, bool pow2, int maxSize)
        {
            if (pow2) img = Resize(img, NextPow2(img.Width), NextPow2(img.Height));
            if (maxSize > 0)
            {
                int w = img.Width, h = img.Height;
                while (w > maxSize || h > maxSize) { w = Math.Max(1, w >> 1); h = Math.Max(1, h >> 1); }
                img = Resize(img, w, h);
            }
            return img;
        }

        public static DdsInfo ReadInfo(byte[] b)
        {
            if (!LooksLikeDds(b)) return null;
            uint R(int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o, 4));
            uint pf = R(80);
            string fmt;
            if ((pf & 4) != 0) fmt = Encoding.ASCII.GetString(b, 84, 4).TrimEnd('\0');
            else fmt = R(88) == 32 && R(104) != 0 ? "ARGB" : "RGB" + R(88);
            return new DdsInfo { Format = fmt, Height = (int)R(12), Width = (int)R(16), Mips = Math.Max(1, (int)R(28)), Cube = (R(112) & 0x200) != 0 };
        }

        /// <summary>Number of levels in a full mip chain down to 1x1.</summary>
        public static int FullMipCount(int w, int h)
        {
            int n = 1;
            while (w > 1 || h > 1) { w = Math.Max(1, w >> 1); h = Math.Max(1, h >> 1); n++; }
            return n;
        }

        /// <summary>format: "auto" | "DXT1" | "DXT5" | "ARGB". <paramref name="mipLimit"/> caps the number of levels (0 = full chain).</summary>
        public static DdsResult Encode(RgbaImage img, string format = "auto", bool mips = true, bool pow2 = true, int maxSize = 0, int mipLimit = 0)
        {
            string fmt = string.IsNullOrEmpty(format) ? "auto" : format;
            if (fmt == "auto") fmt = HasAlpha(img) ? "DXT5" : "DXT1";
            img = Prepare(img, pow2, maxSize);
            var levels = new List<RgbaImage> { img };
            if (mips)
                while ((levels[levels.Count - 1].Width > 1 || levels[levels.Count - 1].Height > 1) && (mipLimit <= 0 || levels.Count < mipLimit))
                    levels.Add(Half(levels[levels.Count - 1]));
            var datas = levels.Select(l =>
            {
                if (fmt == "ARGB")
                {
                    var o = new byte[l.Width * l.Height * 4];
                    for (int i = 0; i < l.Width * l.Height; i++)
                    {
                        o[4 * i] = l.Data[4 * i + 2]; o[4 * i + 1] = l.Data[4 * i + 1]; o[4 * i + 2] = l.Data[4 * i]; o[4 * i + 3] = l.Data[4 * i + 3];
                    }
                    return o;
                }
                return Compress(l, fmt);
            }).ToList();

            var hdr = new byte[128];
            void W32(int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(hdr.AsSpan(o, 4), v);
            hdr[0] = 0x44; hdr[1] = 0x44; hdr[2] = 0x53; hdr[3] = 0x20;
            W32(4, 124);
            uint flags = 0x1 | 0x2 | 0x4 | 0x1000 | (levels.Count > 1 ? 0x20000u : 0);
            flags |= fmt == "ARGB" ? 0x8u : 0x80000u;
            W32(8, flags);
            W32(12, (uint)img.Height);
            W32(16, (uint)img.Width);
            W32(20, fmt == "ARGB" ? (uint)(img.Width * 4) : (uint)datas[0].Length);
            W32(28, (uint)levels.Count);
            W32(76, 32);
            if (fmt == "ARGB")
            {
                W32(80, 0x41); W32(88, 32);
                W32(92, 0x00FF0000); W32(96, 0x0000FF00); W32(100, 0x000000FF); W32(104, 0xFF000000);
            }
            else
            {
                W32(80, 0x4);
                var four = Encoding.ASCII.GetBytes(fmt == "DXT1" ? "DXT1" : "DXT5");
                Buffer.BlockCopy(four, 0, hdr, 84, 4);
            }
            W32(108, 0x1000 | (levels.Count > 1 ? 0x400008u : 0));
            int total = 128 + datas.Sum(d => d.Length);
            var bytes = new byte[total];
            Buffer.BlockCopy(hdr, 0, bytes, 0, 128);
            int off = 128;
            foreach (var d in datas) { Buffer.BlockCopy(d, 0, bytes, off, d.Length); off += d.Length; }
            return new DdsResult { Bytes = bytes, Format = fmt, Width = img.Width, Height = img.Height, Mips = levels.Count, Image = img };
        }

        public static bool LooksLikeDds(byte[] b) => b != null && b.Length >= 128 && b[0] == 0x44 && b[1] == 0x44 && b[2] == 0x53 && b[3] == 0x20;

        /// <summary>Decode the top level of a DXT1/DXT3/DXT5 or uncompressed DDS to RGBA.</summary>
        public static RgbaImage Decode(byte[] bytes)
        {
            if (!LooksLikeDds(bytes)) throw new InvalidDataException("not a DDS file");
            uint R32(int o) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(o, 4));
            int h = (int)R32(12), w = (int)R32(16);
            uint pfFlags = R32(80);
            string four = Encoding.ASCII.GetString(bytes, 84, 4);
            if (w <= 0 || h <= 0 || w > 16384 || h > 16384) throw new InvalidDataException("DDS size is not plausible");
            var outD = new byte[w * h * 4];
            int o = 128;
            if ((pfFlags & 4) == 0)
            {
                int bpp = (int)(R32(88) / 8);
                uint rm = R32(92), gm = R32(96), bm = R32(100), am = R32(104);
                if (bpp < 1 || bpp > 4) throw new InvalidDataException("unsupported uncompressed DDS layout");
                if (o + w * h * bpp > bytes.Length) throw new InvalidDataException("DDS is truncated");
                byte Ch(uint v, uint m)
                {
                    if (m == 0) return 255;
                    int s = 0;
                    while (((m >> s) & 1) == 0) s++;
                    uint mx = m >> s;
                    return (byte)HmCore.JsRound(((v & m) >> s) * 255.0 / mx);
                }
                for (int i = 0; i < w * h; i++)
                {
                    uint v = 0;
                    for (int k = 0; k < bpp; k++) v |= (uint)bytes[o + i * bpp + k] << (8 * k);
                    outD[4 * i] = Ch(v, rm);
                    outD[4 * i + 1] = Ch(v, gm);
                    outD[4 * i + 2] = Ch(v, bm);
                    outD[4 * i + 3] = am != 0 ? Ch(v, am) : (byte)255;
                }
                return new RgbaImage(w, h, outD);
            }
            if (four != "DXT1" && four != "DXT3" && four != "DXT5") throw new InvalidDataException("unsupported DDS format " + four.TrimEnd('\0'));
            int bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4);
            int need = bw * bh * (four == "DXT1" ? 8 : 16);
            if (o + need > bytes.Length) throw new InvalidDataException("DDS is truncated");
            var alpha = new double[16];
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    for (int k = 0; k < 16; k++) alpha[k] = 255;
                    if (four == "DXT5")
                    {
                        int a0 = bytes[o], a1 = bytes[o + 1];
                        var pal = new List<double> { a0, a1 };
                        if (a0 > a1) for (int k = 1; k <= 6; k++) pal.Add(((7 - k) * a0 + k * a1) / 7.0);
                        else { for (int k = 1; k <= 4; k++) pal.Add(((5 - k) * a0 + k * a1) / 5.0); pal.Add(0); pal.Add(255); }
                        ulong bits = 0;
                        for (int k = 5; k >= 0; k--) bits = (bits << 8) | bytes[o + 2 + k];
                        for (int k = 0; k < 16; k++) alpha[k] = pal[(int)((bits >> (3 * k)) & 7)];
                        o += 8;
                    }
                    else if (four == "DXT3")
                    {
                        for (int k = 0; k < 16; k++) alpha[k] = ((bytes[o + (k >> 1)] >> ((k & 1) * 4)) & 15) * 17;
                        o += 8;
                    }
                    int c0 = bytes[o] | (bytes[o + 1] << 8), c1 = bytes[o + 2] | (bytes[o + 3] << 8);
                    double[] e0 = From565(c0), e1 = From565(c1);
                    var cp = new double[4][];
                    cp[0] = e0;
                    cp[1] = e1;
                    cp[2] = new double[3];
                    cp[3] = new double[3];
                    bool four4 = c0 > c1 || four != "DXT1";
                    for (int c = 0; c < 3; c++)
                    {
                        if (four4) { cp[2][c] = (2 * e0[c] + e1[c]) / 3; cp[3][c] = (e0[c] + 2 * e1[c]) / 3; }
                        else { cp[2][c] = (e0[c] + e1[c]) / 2; cp[3][c] = 0; }
                    }
                    uint idx = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(o + 4, 4));
                    for (int k = 0; k < 16; k++)
                    {
                        int x = bx * 4 + (k & 3), y = by * 4 + (k >> 2);
                        if (x >= w || y >= h) continue;
                        int q = (int)((idx >> (2 * k)) & 3), p = 4 * (y * w + x);
                        outD[p] = (byte)cp[q][0];
                        outD[p + 1] = (byte)cp[q][1];
                        outD[p + 2] = (byte)cp[q][2];
                        outD[p + 3] = four == "DXT1" && c0 <= c1 && q == 3 ? (byte)0 : (byte)alpha[k];
                    }
                    o += 8;
                }
            return new RgbaImage(w, h, outD);
        }
    }
}

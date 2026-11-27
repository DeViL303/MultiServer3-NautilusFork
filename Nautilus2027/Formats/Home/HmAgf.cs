#nullable disable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Nautilus2027.Formats.Home
{
    /// <summary>
    /// A Home .AGF texture-generation layer (Hub::AgfTexture, used by the avatar TexGen for things like eyebrows):
    /// an 8-bit image with a 256-colour ARGB palette, packed in bands of 4 rows, each band run-length coded and padded to 16 bytes.
    /// </summary>
    public sealed class AgfImage
    {
        public int Width, Height;
        /// <summary>256 colours, 0xAARRGGBB.</summary>
        public uint[] Palette = new uint[256];
        /// <summary>One palette index per pixel, rows top to bottom.</summary>
        public byte[] Pixels;
        /// <summary>The file this came from (its coded bands are kept where the pixels didn't change).</summary>
        public byte[] Original;
        public List<string> Warnings = new List<string>();
    }

    public static class HmAgf
    {
        public const int BandRows = 4;

        public static bool Looks(byte[] d) => d != null && d.Length >= 16 && d[0] == (byte)'A' && d[1] == (byte)'G' && d[2] == (byte)'F' && d[3] == 0;

        private static uint U32(byte[] d, int o) => BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(o, 4));

        public static AgfImage Read(byte[] d)
        {
            if (!Looks(d)) throw new InvalidDataException("not an AGF texture (no \"AGF\" header)");
            uint w = U32(d, 4), h = U32(d, 8), n = U32(d, 12);
            if (w == 0 || h == 0 || w > 8192 || h > 8192) throw new InvalidDataException($"the AGF says it is {w} x {h}, which can't be right");
            if (n == 0 || n > 256) throw new InvalidDataException($"the AGF says it has {n} palette colours (1 to 256 expected)");
            var img = new AgfImage { Width = (int)w, Height = (int)h, Pixels = new byte[w * h], Original = d };
            int o = 16;
            if (o + 4 * n > d.Length) throw new InvalidDataException("the AGF is cut off inside its palette");
            for (int i = 0; i < n; i++) img.Palette[i] = U32(d, o + 4 * i);
            if (n < 256) img.Warnings.Add($"only {n} palette colours (the rest are black)");
            o += 4 * (int)n;
            int bands = ((int)h + BandRows - 1) / BandRows;
            if (o + 2 * bands > d.Length) throw new InvalidDataException("the AGF is cut off inside its band table");
            var sizes = new int[bands];
            for (int b = 0; b < bands; b++) sizes[b] = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(o + 2 * b, 2));
            o += 2 * bands;
            Array.Fill(img.Pixels, (byte)255);
            int bad = 0;
            for (int b = 0; b < bands; b++)
            {
                int need = Math.Min(BandRows, (int)h - b * BandRows) * (int)w, at = b * BandRows * (int)w;
                int end = Math.Min(d.Length, o + sizes[b]);
                if (o + sizes[b] > d.Length && bad++ == 0) img.Warnings.Add($"the file is cut off in band {b} (rows {b * BandRows} on show as background)");
                int got = DecodeBand(d, o, end, img.Pixels, at, need);
                if (got != need && o + sizes[b] <= d.Length && bad++ == 0) img.Warnings.Add($"band {b} holds {got} of its {need} pixels (damaged?)");
                o += sizes[b];
            }
            if (o < d.Length) img.Warnings.Add($"{d.Length - o} byte(s) after the last band");
            return img;
        }

        /// <summary>Runs: a byte with the top bit set repeats the next byte (low 7 bits) times; else that many bytes follow as they are. 0 ends the band.</summary>
        private static int DecodeBand(byte[] d, int i, int end, byte[] px, int at, int need)
        {
            int got = 0;
            while (got < need && i < end)
            {
                int c = d[i++];
                if (c == 0) break;
                if ((c & 0x80) != 0)
                {
                    if (i >= end) break;
                    byte v = d[i++];
                    int k = Math.Min(c & 0x7f, need - got);
                    px.AsSpan(at + got, k).Fill(v);
                    got += k;
                }
                else
                {
                    int k = Math.Min(Math.Min(c, end - i), need - got);
                    Buffer.BlockCopy(d, i, px, at + got, k);
                    got += k; i += c;
                }
            }
            return got;
        }

        private static byte[] EncodeBand(byte[] p, int at, int n)
        {
            var o = new List<byte>(n / 2 + 16);
            var lit = new List<byte>(128);
            void Flush()
            {
                for (int s = 0; s < lit.Count; s += 127)
                {
                    int k = Math.Min(127, lit.Count - s);
                    o.Add((byte)k);
                    for (int j = 0; j < k; j++) o.Add(lit[s + j]);
                }
                lit.Clear();
            }
            int i = 0;
            while (i < n)
            {
                int r = 1;
                // like the game's files, the band's last pixel is always written as a single literal
                while (i + r < n - 1 && p[at + i + r] == p[at + i] && r < 127) r++;
                if (r >= 2) { Flush(); o.Add((byte)(0x80 | r)); o.Add(p[at + i]); i += r; }
                else { lit.Add(p[at + i]); i++; }
            }
            Flush();
            while (o.Count % 16 != 0) o.Add(0);
            return o.ToArray();
        }

        /// <summary>The AGF file; bands whose pixels are unchanged from the original keep its exact bytes.</summary>
        public static byte[] Write(AgfImage img)
        {
            int w = img.Width, h = img.Height, bands = (h + BandRows - 1) / BandRows;
            AgfImage orig = null;
            List<byte[]> origBands = null;
            if (img.Original != null)
                try
                {
                    orig = Read(img.Original);
                    if (orig.Width == w && orig.Height == h && orig.Warnings.Count == 0) origBands = Bands(img.Original);
                }
                catch (Exception) { }
            var parts = new List<byte[]>();
            for (int b = 0; b < bands; b++)
            {
                int at = b * BandRows * w, n = Math.Min(BandRows, h - b * BandRows) * w;
                if (origBands != null && orig.Pixels.AsSpan(at, n).SequenceEqual(img.Pixels.AsSpan(at, n))) parts.Add(origBands[b]);
                else parts.Add(EncodeBand(img.Pixels, at, n));
            }
            if (parts.Any(p => p.Length > 65535)) throw new InvalidDataException("the image is too wide for an AGF band");
            var ms = new MemoryStream();
            var head = new byte[16];
            Encoding.ASCII.GetBytes("AGF").CopyTo(head, 0);
            BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(4), (uint)w);
            BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(8), (uint)h);
            BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(12), 256);
            ms.Write(head);
            var pal = new byte[1024];
            for (int i = 0; i < 256; i++) BinaryPrimitives.WriteUInt32BigEndian(pal.AsSpan(4 * i), img.Palette[i]);
            ms.Write(pal);
            var tab = new byte[2 * bands];
            for (int b = 0; b < bands; b++) BinaryPrimitives.WriteUInt16BigEndian(tab.AsSpan(2 * b), (ushort)parts[b].Length);
            ms.Write(tab);
            foreach (var p in parts) ms.Write(p);
            return ms.ToArray();
        }

        private static List<byte[]> Bands(byte[] d)
        {
            int h = (int)U32(d, 8), n = (int)U32(d, 12), o = 16 + 4 * n, bands = (h + BandRows - 1) / BandRows;
            var r = new List<byte[]>();
            int at = o + 2 * bands;
            for (int b = 0; b < bands; b++)
            {
                int s = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(o + 2 * b, 2));
                r.Add(d.AsSpan(at, s).ToArray());
                at += s;
            }
            return r;
        }

        public static RgbaImage ToRgba(AgfImage img)
        {
            var px = new byte[img.Width * img.Height * 4];
            for (int i = 0; i < img.Pixels.Length; i++)
            {
                uint c = img.Palette[img.Pixels[i]];
                px[4 * i] = (byte)(c >> 16); px[4 * i + 1] = (byte)(c >> 8); px[4 * i + 2] = (byte)c; px[4 * i + 3] = (byte)(c >> 24);
            }
            return new RgbaImage(img.Width, img.Height, px);
        }

        // ------------------------------------------------------------------ PNG (indexed, so the palette and indices go across exactly)

        private const string KeepChunk = "agFd";   // ancillary, private, safe to copy: the original AGF

        public static byte[] ToPng(AgfImage img)
        {
            int w = img.Width, h = img.Height;
            var raw = new byte[(w + 1) * h];
            for (int y = 0; y < h; y++) Buffer.BlockCopy(img.Pixels, y * w, raw, y * (w + 1) + 1, w);
            byte[] idat;
            using (var ms = new MemoryStream())
            {
                using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(raw, 0, raw.Length);
                idat = ms.ToArray();
            }
            var plte = new byte[768];
            var trns = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                uint c = img.Palette[i];
                plte[3 * i] = (byte)(c >> 16); plte[3 * i + 1] = (byte)(c >> 8); plte[3 * i + 2] = (byte)c; trns[i] = (byte)(c >> 24);
            }
            using var o = new MemoryStream();
            o.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            var ihdr = new byte[13];
            BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0), (uint)w);
            BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)h);
            ihdr[8] = 8; ihdr[9] = 3;
            Chunk(o, "IHDR", ihdr);
            Chunk(o, "PLTE", plte);
            if (trns.Any(a => a != 255)) Chunk(o, "tRNS", trns);
            if (img.Original != null) Chunk(o, KeepChunk, img.Original);
            Chunk(o, "IDAT", idat);
            Chunk(o, "IEND", Array.Empty<byte>());
            return o.ToArray();
        }

        private static void Chunk(Stream s, string type, byte[] body)
        {
            var c = new byte[12 + body.Length];
            BinaryPrimitives.WriteUInt32BigEndian(c.AsSpan(0), (uint)body.Length);
            Encoding.ASCII.GetBytes(type).CopyTo(c, 4);
            Buffer.BlockCopy(body, 0, c, 8, body.Length);
            BinaryPrimitives.WriteUInt32BigEndian(c.AsSpan(8 + body.Length), HmCore.Crc32(c, 4, 4 + body.Length));
            s.Write(c, 0, c.Length);
        }

        /// <summary>
        /// An image back to AGF. An indexed PNG keeps its indices and palette as they are; any other image (or format) is
        /// matched to the palette of the original AGF when one is known (kept inside PNGs made here, or <paramref name="paletteFrom"/>),
        /// else to a grey ramp.
        /// </summary>
        public static AgfImage FromImage(byte[] bytes, string nameHint, byte[] paletteFrom, List<string> notes)
        {
            byte[] kept = null;
            PngIndexed ix = null;
            if (bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50)
            {
                kept = PngChunk(bytes, KeepChunk);
                try { ix = ReadIndexedPng(bytes); }
                catch (Exception ex) { notes?.Add("PNG palette not read (" + ex.Message + "): matched by colour"); }
            }
            AgfImage basis = null;
            foreach (var src in new[] { kept, paletteFrom })
            {
                if (src == null || basis != null) continue;
                try { basis = Read(src); } catch (Exception) { }
            }
            var img = new AgfImage { Original = basis?.Original };
            if (ix != null)
            {
                img.Width = ix.Width; img.Height = ix.Height; img.Pixels = ix.Indices;
                for (int i = 0; i < 256; i++) img.Palette[i] = i < ix.Palette.Length ? ix.Palette[i] : 0xFF000000;
                if (basis != null && !basis.Palette.SequenceEqual(img.Palette)) notes?.Add("the PNG's palette differs from the original AGF's: the PNG's is used");
            }
            else
            {
                var rgba = ImageCodec.Decode(bytes, nameHint) ?? throw new InvalidDataException("the image could not be read");
                img.Width = rgba.Width; img.Height = rgba.Height;
                if (basis != null) img.Palette = (uint[])basis.Palette.Clone();
                else
                {
                    for (int i = 0; i < 256; i++) img.Palette[i] = 0xFF000000u | (uint)(i * 0x010101);
                    notes?.Add("no original AGF palette known: a grey ramp is used (put the original .AGF beside the image to keep its colours)");
                }
                img.Pixels = Match(rgba, img.Palette, notes);
            }
            if (img.Width > 8192 || img.Height > 8192) throw new InvalidDataException("the image is too large for an AGF");
            if (img.Height % BandRows != 0) notes?.Add($"the height ({img.Height}) isn't a multiple of {BandRows}: the game's files always are");
            if (basis != null && (basis.Width != img.Width || basis.Height != img.Height))
                notes?.Add($"size changed from {basis.Width} x {basis.Height} to {img.Width} x {img.Height}: keep the LOD sizes the game expects (512 / 128 / 32)");
            return img;
        }

        /// <summary>Each pixel to the nearest palette colour (exact matches first; on a tie the highest index, as the game's background is 255).</summary>
        private static byte[] Match(RgbaImage rgba, uint[] pal, List<string> notes)
        {
            var exact = new Dictionary<uint, byte>();
            for (int i = 255; i >= 0; i--) exact.TryAdd(pal[i], (byte)i);
            var cache = new Dictionary<uint, byte>();
            var px = new byte[rgba.Width * rgba.Height];
            int near = 0;
            for (int i = 0; i < px.Length; i++)
            {
                byte r = rgba.Data[4 * i], g = rgba.Data[4 * i + 1], b = rgba.Data[4 * i + 2], a = rgba.Data[4 * i + 3];
                uint c = ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
                if (exact.TryGetValue(c, out byte e)) { px[i] = e; continue; }
                if (!cache.TryGetValue(c, out byte best))
                {
                    long bd = long.MaxValue;
                    for (int k = 255; k >= 0; k--)
                    {
                        uint p = pal[k];
                        long dr = r - (int)((p >> 16) & 255), dg = g - (int)((p >> 8) & 255), db = b - (int)(p & 255), da = a - (int)(p >> 24);
                        long dd = dr * dr + dg * dg + db * db + da * da;
                        if (dd < bd) { bd = dd; best = (byte)k; }
                    }
                    cache[c] = best;
                }
                px[i] = best;
                near++;
            }
            if (near > 0) notes?.Add($"{near:N0} pixel(s) weren't palette colours and took the nearest one");
            return px;
        }

        private sealed class PngIndexed
        {
            public int Width, Height;
            public byte[] Indices;
            public uint[] Palette;
        }

        private static byte[] PngChunk(byte[] d, string type)
        {
            int o = 8;
            while (o + 12 <= d.Length)
            {
                int len = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(o, 4)), int.MaxValue);
                if (len < 0 || o + 12 + (long)len > d.Length) break;
                if (Encoding.ASCII.GetString(d, o + 4, 4) == type) return d.AsSpan(o + 8, len).ToArray();
                o += 12 + len;
            }
            return null;
        }

        /// <summary>An 8-bit (or 1/2/4-bit) indexed, non-interlaced PNG as indices + palette; null for any other PNG.</summary>
        private static PngIndexed ReadIndexedPng(byte[] d)
        {
            var ihdr = PngChunk(d, "IHDR");
            if (ihdr == null || ihdr.Length < 13 || ihdr[9] != 3 || ihdr[12] != 0) return null;
            int w = (int)BinaryPrimitives.ReadUInt32BigEndian(ihdr.AsSpan(0)), h = (int)BinaryPrimitives.ReadUInt32BigEndian(ihdr.AsSpan(4)), bits = ihdr[8];
            if (w <= 0 || h <= 0 || w > 8192 || h > 8192 || (bits != 1 && bits != 2 && bits != 4 && bits != 8)) return null;
            var plte = PngChunk(d, "PLTE") ?? throw new InvalidDataException("no PLTE");
            var trns = PngChunk(d, "tRNS") ?? Array.Empty<byte>();
            var pal = new uint[plte.Length / 3];
            for (int i = 0; i < pal.Length; i++)
                pal[i] = ((uint)(i < trns.Length ? trns[i] : 255) << 24) | ((uint)plte[3 * i] << 16) | ((uint)plte[3 * i + 1] << 8) | plte[3 * i + 2];
            // all IDAT chunks, inflated
            var z = new MemoryStream();
            int o = 8;
            while (o + 12 <= d.Length)
            {
                int len = (int)BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(o, 4));
                if (len < 0 || o + 12 + (long)len > d.Length) break;
                if (Encoding.ASCII.GetString(d, o + 4, 4) == "IDAT") z.Write(d, o + 8, len);
                o += 12 + len;
            }
            z.Position = 0;
            int stride = (w * bits + 7) / 8;
            var raw = new byte[(stride + 1) * h];
            using (var zs = new ZLibStream(z, CompressionMode.Decompress))
            {
                int got = 0, k;
                while (got < raw.Length && (k = zs.Read(raw, got, raw.Length - got)) > 0) got += k;
                if (got < raw.Length) throw new InvalidDataException("the PNG's pixel data is cut off");
            }
            var prev = new byte[stride];
            var cur = new byte[stride];
            var idx = new byte[w * h];
            for (int y = 0; y < h; y++)
            {
                int f = raw[y * (stride + 1)];
                Buffer.BlockCopy(raw, y * (stride + 1) + 1, cur, 0, stride);
                for (int x = 0; x < stride; x++)
                {
                    int a = x >= 1 ? cur[x - 1] : 0, b = prev[x], c = x >= 1 ? prev[x - 1] : 0;
                    int add = f switch
                    {
                        0 => 0,
                        1 => a,
                        2 => b,
                        3 => (a + b) / 2,
                        4 => Paeth(a, b, c),
                        _ => throw new InvalidDataException("bad PNG filter"),
                    };
                    cur[x] = (byte)(cur[x] + add);
                }
                for (int x = 0; x < w; x++)
                {
                    int v = bits == 8 ? cur[x] : (cur[x * bits / 8] >> (8 - bits - (x * bits % 8))) & ((1 << bits) - 1);
                    idx[y * w + x] = (byte)v;
                }
                (prev, cur) = (cur, prev);
            }
            if (idx.Any(v => v >= pal.Length)) throw new InvalidDataException("an index is past the end of the palette");
            return new PngIndexed { Width = w, Height = h, Indices = idx, Palette = pal };
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }

        // ------------------------------------------------------------------ inspect

        public static string Describe(AgfImage img)
        {
            var sb = new StringBuilder();
            var counts = new int[256];
            foreach (var p in img.Pixels) counts[p]++;
            int used = counts.Count(c => c > 0);
            int bg = Array.IndexOf(counts, counts.Max());
            int bands = (img.Height + BandRows - 1) / BandRows;
            sb.AppendLine($"     Home TexGen layer (AGF), {img.Width} x {img.Height}, {used} of 256 palette colours used, {bands} band(s) of {BandRows} rows");
            uint bgc = img.Palette[bg];
            sb.AppendLine($"     Background: index {bg} (#{bgc:X8} ARGB), {100.0 * counts[bg] / Math.Max(1, img.Pixels.Length):0.#}% of the pixels");
            bool grey = img.Palette.All(c => ((c >> 16) & 255) == ((c >> 8) & 255) && ((c >> 8) & 255) == (c & 255));
            sb.AppendLine($"     Palette: {(grey ? "greys" : "colours")}, alpha {(img.Palette.All(c => c >> 24 == 255) ? "solid" : "varies")}, from #{img.Palette[0]:X8} to #{img.Palette[255]:X8}");
            // where the drawn part sits
            int x0 = img.Width, y0 = img.Height, x1 = -1, y1 = -1;
            for (int y = 0; y < img.Height; y++)
                for (int x = 0; x < img.Width; x++)
                    if (img.Pixels[y * img.Width + x] != bg) { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); }
            if (x1 >= 0) sb.AppendLine($"     Drawn area: x {x0}..{x1}, y {y0}..{y1}");
            else sb.AppendLine("     Drawn area: none (all background)");
            foreach (var w in img.Warnings) sb.AppendLine("     Warning: " + w);
            return sb.ToString();
        }
    }
}

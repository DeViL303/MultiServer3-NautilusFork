#nullable disable
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nautilus2027.Formats.Home
{
    /// <summary>
    /// Image decoding to exact (non-premultiplied) RGBA and PNG encoding. PNG / JPEG / BMP / GIF / TIFF go through
    /// Windows Imaging (WPF); TGA and DDS are decoded here.
    /// </summary>
    public static class ImageCodec
    {
        /// <summary>Decode any supported image. <paramref name="nameHint"/> (file name) helps pick TGA, which has no signature.</summary>
        public static RgbaImage Decode(byte[] bytes, string nameHint = null)
        {
            if (bytes == null || bytes.Length < 4) throw new InvalidDataException("empty image");
            string ext = Path.GetExtension(nameHint ?? "").ToLowerInvariant();
            if (Dds.LooksLikeDds(bytes)) return Dds.Decode(bytes);
            if (ext == ".tga") return DecodeTga(bytes);
            try
            {
                return DecodeWic(bytes);
            }
            catch (Exception) when (ext != ".tga" && LooksLikeTga(bytes))
            {
                return DecodeTga(bytes);
            }
        }

        private static bool LooksLikeTga(byte[] u8) =>
            u8.Length > 18 && u8[1] == 0 && (u8[2] == 2 || u8[2] == 3 || u8[2] == 10 || u8[2] == 11) && (u8[16] == 8 || u8[16] == 16 || u8[16] == 24 || u8[16] == 32);

        private static RgbaImage DecodeWic(byte[] bytes)
        {
            using (var ms = new MemoryStream(bytes, false))
            {
                var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                BitmapSource frame = decoder.Frames[0];
                if (frame.Format != PixelFormats.Bgra32)
                    frame = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                int w = frame.PixelWidth, h = frame.PixelHeight, stride = w * 4;
                var px = new byte[stride * h];
                frame.CopyPixels(px, stride, 0);
                for (int i = 0; i < px.Length; i += 4)
                {
                    byte b = px[i];
                    px[i] = px[i + 2];
                    px[i + 2] = b;
                }
                return new RgbaImage(w, h, px);
            }
        }

        public static RgbaImage DecodeTga(byte[] u8)
        {
            int idl = u8[0], cmt = u8[1], type = u8[2], w = u8[12] | (u8[13] << 8), h = u8[14] | (u8[15] << 8), bpp = u8[16], desc = u8[17];
            if (cmt != 0) throw new InvalidDataException("colour-mapped TGA not supported");
            if (type != 2 && type != 3 && type != 10 && type != 11) throw new InvalidDataException("unsupported TGA type " + type);
            int bp = bpp >> 3;
            bool rle = type >= 9, grey = type == 3 || type == 11;
            int p = 18 + idl;
            var pix = new byte[w * h * 4];
            void Put(int i, int q)
            {
                if (grey)
                {
                    pix[4 * i] = pix[4 * i + 1] = pix[4 * i + 2] = u8[q];
                    pix[4 * i + 3] = bp == 2 ? u8[q + 1] : (byte)255;
                }
                else if (bp == 2)
                {
                    int v = u8[q] | (u8[q + 1] << 8);
                    pix[4 * i] = (byte)(((v >> 10) & 31) * 255 / 31);
                    pix[4 * i + 1] = (byte)(((v >> 5) & 31) * 255 / 31);
                    pix[4 * i + 2] = (byte)((v & 31) * 255 / 31);
                    pix[4 * i + 3] = 255;
                }
                else
                {
                    pix[4 * i] = u8[q + 2];
                    pix[4 * i + 1] = u8[q + 1];
                    pix[4 * i + 2] = u8[q];
                    pix[4 * i + 3] = bp == 4 ? u8[q + 3] : (byte)255;
                }
            }
            int n = 0;
            if (!rle)
            {
                for (; n < w * h; n++, p += bp) Put(n, p);
            }
            else
            {
                while (n < w * h)
                {
                    int c = u8[p++], count = (c & 127) + 1;
                    if ((c & 128) != 0)
                    {
                        for (int k = 0; k < count && n < w * h; k++) Put(n++, p);
                        p += bp;
                    }
                    else
                    {
                        for (int k = 0; k < count && n < w * h; k++, p += bp) Put(n++, p);
                    }
                }
            }
            if ((desc & 0x20) == 0)
            {
                // bottom-up -> flip to top-down
                int row = w * 4;
                var tmp = new byte[row];
                for (int y = 0; y < h >> 1; y++)
                {
                    int a = y * row, b = (h - 1 - y) * row;
                    Buffer.BlockCopy(pix, a, tmp, 0, row);
                    Buffer.BlockCopy(pix, b, pix, a, row);
                    Buffer.BlockCopy(tmp, 0, pix, b, row);
                }
            }
            return new RgbaImage(w, h, pix);
        }

        /// <summary>PNG (RGBA8, no filtering, zlib) with exact alpha.</summary>
        public static byte[] EncodePng(RgbaImage img)
        {
            int w = img.Width, h = img.Height;
            var raw = new byte[(w * 4 + 1) * h];
            for (int y = 0; y < h; y++)
            {
                raw[y * (w * 4 + 1)] = 0;
                Buffer.BlockCopy(img.Data, y * w * 4, raw, y * (w * 4 + 1) + 1, w * 4);
            }
            byte[] idat;
            using (var ms = new MemoryStream())
            {
                using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true))
                    z.Write(raw, 0, raw.Length);
                idat = ms.ToArray();
            }
            using (var outS = new MemoryStream())
            {
                outS.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
                var ihdr = new byte[13];
                HmCore.PutU32(ihdr, 0, (uint)w);
                HmCore.PutU32(ihdr, 4, (uint)h);
                ihdr[8] = 8;
                ihdr[9] = 6;
                WriteChunk(outS, "IHDR", ihdr);
                WriteChunk(outS, "IDAT", idat);
                WriteChunk(outS, "IEND", Array.Empty<byte>());
                return outS.ToArray();
            }
        }

        private static void WriteChunk(Stream s, string type, byte[] body)
        {
            var c = new byte[12 + body.Length];
            HmCore.PutU32(c, 0, (uint)body.Length);
            Buffer.BlockCopy(Encoding.ASCII.GetBytes(type), 0, c, 4, 4);
            Buffer.BlockCopy(body, 0, c, 8, body.Length);
            HmCore.PutU32(c, 8 + body.Length, HmCore.Crc32(c, 4, 4 + body.Length));
            s.Write(c, 0, c.Length);
        }

        /// <summary>True for PNG or JPEG bytes (can be embedded in GLB/FBX/OBJ exports as they are).</summary>
        public static string WebMime(byte[] b)
        {
            if (b == null || b.Length < 4) return null;
            if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "image/png";
            if (b[0] == 0xFF && b[1] == 0xD8) return "image/jpeg";
            return null;
        }
    }
}

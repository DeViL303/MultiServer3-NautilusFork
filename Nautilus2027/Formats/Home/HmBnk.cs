#nullable disable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Nautilus2027.Formats.Home
{
    public enum BnkCodec { PsAdpcm, Pcm16BE, Mpeg }

    /// <summary>One sound wave in a bank (a SCREAM "grain" of type 0x100).</summary>
    public sealed class BnkWave
    {
        public int Index;
        /// <summary>Where the wave's 0x18-byte header is in the file (absolute).</summary>
        public int HeaderAt;
        public string Name;
        public byte CenterNote, CenterFine;
        public ushort Flags;
        public uint Offset, Size;
        public BnkCodec Codec => (Flags & 0x80) != 0 ? BnkCodec.Pcm16BE : (Flags & 0x1000) != 0 ? BnkCodec.Mpeg : BnkCodec.PsAdpcm;
        public bool Loop => (Flags & 0x40) != 0;
        public int SampleRate;
    }

    public sealed class BnkFile
    {
        public byte[] Data;
        public uint Version, SblkVersion;
        public int SblkAt, SblkSize, DataAt, DataSize;
        public int Sounds, Grains, Streams;
        public List<BnkWave> Waves = new List<BnkWave>();
        public List<string> SoundNames = new List<string>();
        public List<string> Warnings = new List<string>();
    }

    /// <summary>
    /// Sony SCREAM sound banks (.BNK, "SBlk" stored as "klBS"; version 3 in Home) read and written natively: the waves come
    /// out as WAV (PS-ADPCM and big-endian PCM16 decoded) and go back in (encoded the same way), the bank's tables kept as
    /// they are with only each wave's offset / size / pitch / loop changed.
    /// </summary>
    public static class HmBnk
    {
        private static uint U32(byte[] d, int o) => BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(o, 4));
        private static ushort U16(byte[] d, int o) => BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(o, 2));
        private static void PutU32(byte[] d, int o, uint v) => BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(o, 4), v);
        private static void PutU16(byte[] d, int o, ushort v) => BinaryPrimitives.WriteUInt16BigEndian(d.AsSpan(o, 2), v);

        public static bool Looks(byte[] d) => d != null && d.Length >= 0x40 && U32(d, 4) >= 2 && U32(d, 4) < 16 && U32(d, 8) + 4 <= d.Length &&
                                              SblkMagic(d, (int)U32(d, 8));

        private static bool SblkMagic(byte[] d, int o) =>
            o >= 0 && o + 4 <= d.Length && (Encoding.ASCII.GetString(d, o, 4) == "klBS" || Encoding.ASCII.GetString(d, o, 4) == "SBlk");

        public static BnkFile Read(byte[] d)
        {
            if (d == null || d.Length < 0x40) throw new InvalidDataException("too small for a sound bank");
            var f = new BnkFile { Data = d, Version = U32(d, 0) };
            uint sections = U32(d, 4);
            if (sections < 2 || sections > 16) throw new InvalidDataException("not a SCREAM sound bank (bad section count)");
            f.SblkAt = (int)U32(d, 8); f.SblkSize = (int)U32(d, 12);
            f.DataAt = (int)U32(d, 16); f.DataSize = (int)U32(d, 20);
            if (!SblkMagic(d, f.SblkAt)) throw new InvalidDataException("no \"SBlk\" bank header (not a SCREAM sound bank)");
            if ((long)f.SblkAt + f.SblkSize > d.Length) throw new InvalidDataException("the bank header runs past the end of the file");
            if ((long)f.DataAt + f.DataSize > d.Length) { f.Warnings.Add("the wave data runs past the end of the file (cut off?)"); f.DataSize = Math.Max(0, d.Length - f.DataAt); }
            int sb = f.SblkAt;
            f.SblkVersion = U32(d, sb + 4);
            if (f.SblkVersion != 3) f.Warnings.Add($"SBlk version {f.SblkVersion} (this tool was built from version 3 banks): read as version 3");
            f.Sounds = U16(d, sb + 0x16); f.Grains = U16(d, sb + 0x18); f.Streams = U16(d, sb + 0x1a);
            int t1 = sb + (int)U32(d, sb + 0x1c), t2 = sb + (int)U32(d, sb + 0x20), t3 = sb + (int)U32(d, sb + 0x34), t4 = sb + (int)U32(d, sb + 0x38);
            int end = sb + f.SblkSize;
            bool In(int at, int len) => at >= sb && at + len <= end;
            if (!In(t1, 12 * f.Sounds) || !In(t2, 8 * f.Grains) || !In(t3, 0)) throw new InvalidDataException("the bank's tables don't fit in its header");

            // sound names: 0x14-byte chunks (16-byte name, u16 sound index) after the name hash table
            var names = new Dictionary<int, string>();
            if (In(t4, 0x0c))
                for (int o = t4 + 0x0c; o + 0x14 <= end; o += 0x14)
                {
                    int n = 0;
                    while (n < 16 && d[o + n] >= 32 && d[o + n] < 127) n++;
                    if (n == 0 || (n < 16 && d[o + n] != 0)) continue;
                    int id = U16(d, o + 0x10);
                    if (id < f.Sounds && !names.ContainsKey(id)) names[id] = Encoding.ASCII.GetString(d, o, n);
                }
            for (int i = 0; i < f.Sounds; i++) f.SoundNames.Add(names.TryGetValue(i, out var s) ? s : null);

            // which sound each grain belongs to
            var grainSound = new int[f.Grains];
            Array.Fill(grainSound, -1);
            for (int i = 0; i < f.Sounds; i++)
            {
                int count = d[t1 + 12 * i + 4];
                int first = (int)(U32(d, t1 + 12 * i + 8) / 8);
                for (int g = first; g < first + count && g < f.Grains; g++) if (g >= 0) grainSound[g] = i;
            }
            var seen = new Dictionary<int, BnkWave>();
            var perSound = new Dictionary<int, int>();
            for (int g = 0; g < f.Grains; g++)
            {
                uint v = U32(d, t2 + 8 * g);
                if (v >> 16 != 0x0100) continue;
                int at = t3 + (int)(v & 0xffff);
                if (!In(at, 0x18)) { f.Warnings.Add($"grain {g} points past the bank header"); continue; }
                if (seen.ContainsKey(at)) continue;
                var w = new BnkWave
                {
                    Index = f.Waves.Count, HeaderAt = at,
                    CenterNote = d[at + 2], CenterFine = d[at + 3],
                    Flags = U16(d, at + 0x0e), Offset = U32(d, at + 0x10), Size = U32(d, at + 0x14),
                };
                w.SampleRate = SampleRate(w.CenterNote, w.CenterFine);
                int snd = grainSound[g];
                string baseName = snd >= 0 ? f.SoundNames[snd] ?? $"sound{snd}" : "wave";
                perSound.TryGetValue(snd, out int k);
                perSound[snd] = k + 1;
                w.Name = k == 0 ? baseName : $"{baseName}_{k + 1}";
                if ((long)w.Offset + w.Size > f.DataSize) f.Warnings.Add($"wave {w.Index} ({w.Name}) runs past the wave data");
                seen[at] = w;
                f.Waves.Add(w);
            }
            return f;
        }

        // ------------------------------------------------------------------ pitch

        private static readonly int[] CommonRates = { 8000, 11025, 16000, 22050, 24000, 32000, 44100, 48000, 96000 };

        /// <summary>The rate the wave plays at for middle C (note 60), as the game's SPU2 pitch maths gives it.</summary>
        public static int SampleRate(byte centerNote, byte centerFine)
        {
            bool ps2 = (centerNote & 0x80) != 0;
            int center = ps2 ? 0x100 - centerNote : centerNote;
            double semis = 60 - center + centerFine / 128.0;
            double pitch = Math.Min(0x4000, 4096 * Math.Pow(2, semis / 12));
            if (!ps2) pitch = pitch * 44100 / 48000;
            double rate = 48000 * pitch / 4096;
            foreach (var r in CommonRates) if (Math.Abs(rate - r) <= r * 0.004) return r;
            return (int)Math.Round(rate);
        }

        /// <summary>Center note / fine (PS2 form) that make middle C play at <paramref name="rate"/>.</summary>
        public static (byte Note, byte Fine) PitchFor(int rate)
        {
            rate = Math.Clamp(rate, 1000, 192000);
            double centerExact = 60 - 12 * Math.Log2(rate / 48000.0);   // = center - fine/128
            int center = (int)Math.Ceiling(centerExact);
            int fine = (int)Math.Round((center - centerExact) * 128);
            if (fine >= 128) { center++; fine -= 128; }
            center = Math.Clamp(center, 1, 127);
            return ((byte)(0x100 - center), (byte)Math.Clamp(fine, 0, 127));
        }

        // ------------------------------------------------------------------ PS-ADPCM

        private static readonly int[,] Coef = { { 0, 0 }, { 60, 0 }, { 115, -52 }, { 98, -55 }, { 122, -60 } };

        private static short Clamp16(int v) => (short)Math.Clamp(v, short.MinValue, short.MaxValue);

        /// <summary>Decodes PS-ADPCM frames (the leading all-zero frame and the end marker frame are left out).</summary>
        public static short[] DecodeAdpcm(byte[] src, int at, int size, out int loopStart, out int loopEnd)
        {
            loopStart = loopEnd = -1;
            var outp = new List<short>(size / 16 * 28);
            int h1 = 0, h2 = 0;
            int frames = size / 16;
            for (int fr = 0; fr < frames; fr++)
            {
                int o = at + fr * 16;
                if (o + 16 > src.Length) break;
                byte head = src[o], flag = src[o + 1];
                if (fr == 0 && IsZero(src, o)) continue;   // the silent lead-in frame every wave starts with
                if (flag == 0x07 || (head == 0xff && flag == 0xff)) break;   // end marker
                int shift = head & 0x0f, filt = (head >> 4) & 0x0f;
                if (shift > 12) shift = 9;
                if (filt > 4) filt = 0;
                if ((flag & 0x04) != 0 && loopStart < 0) loopStart = outp.Count;
                for (int i = 0; i < 28; i++)
                {
                    int nib = (src[o + 2 + i / 2] >> ((i & 1) * 4)) & 0x0f;
                    int s = (short)(nib << 12) >> shift;
                    int v = Clamp16(s + ((h1 * Coef[filt, 0] + h2 * Coef[filt, 1] + 32) >> 6));
                    outp.Add((short)v);
                    h2 = h1; h1 = v;
                }
                if ((flag & 0x01) != 0)
                {
                    if ((flag & 0x02) != 0) loopEnd = outp.Count;
                    break;
                }
            }
            return outp.ToArray();
        }

        private static bool IsZero(byte[] d, int o)
        {
            for (int i = 0; i < 16; i++) if (d[o + i] != 0) return false;
            return true;
        }

        /// <summary>
        /// Encodes PCM as the game's PS-ADPCM waves: a silent lead-in frame, the data (loop start / body / end flags when
        /// looping), then the end marker frame the game's banks use.
        /// </summary>
        public static byte[] EncodeAdpcm(short[] pcm, bool loop, int loopStartSample)
        {
            int blocks = Math.Max(1, (pcm.Length + 27) / 28);
            var outp = new byte[(blocks + 2) * 16];
            int h1 = 0, h2 = 0;
            int loopFrame = loop ? Math.Clamp(loopStartSample < 0 ? 0 : loopStartSample / 28, 0, blocks - 1) : -1;
            var x = new int[28];
            var bestNib = new byte[28];
            var nib = new byte[28];
            for (int b = 0; b < blocks; b++)
            {
                for (int i = 0; i < 28; i++) { int k = b * 28 + i; x[i] = k < pcm.Length ? pcm[k] : 0; }
                long bestErr = long.MaxValue;
                int bestF = 0, bestS = 0, bh1 = 0, bh2 = 0;
                for (int f = 0; f < 5; f++)
                    for (int s = 0; s <= 12; s++)
                    {
                        int t1 = h1, t2 = h2;
                        long err = 0;
                        for (int i = 0; i < 28 && err < bestErr; i++)
                        {
                            int pred = (t1 * Coef[f, 0] + t2 * Coef[f, 1] + 32) >> 6;
                            double r = (x[i] - pred) * (double)(1 << s) / 4096.0;
                            int n = (int)Math.Clamp(Math.Round(r), -8, 7);
                            int v = Clamp16(((short)(n << 12) >> s) + pred);
                            long e = x[i] - v;
                            err += e * e;
                            nib[i] = (byte)(n & 0x0f);
                            t2 = t1; t1 = v;
                        }
                        if (err < bestErr) { bestErr = err; bestF = f; bestS = s; bh1 = t1; bh2 = t2; Array.Copy(nib, bestNib, 28); }
                    }
                h1 = bh1; h2 = bh2;
                int o = (b + 1) * 16;
                outp[o] = (byte)((bestF << 4) | bestS);
                byte flag = 0;
                if (loop)
                {
                    flag = 0x02;
                    if (b == loopFrame) flag = 0x06;
                    if (b == blocks - 1) flag = 0x03;
                }
                else if (b == blocks - 1) flag = 0x01;
                outp[o + 1] = flag;
                for (int i = 0; i < 28; i += 2) outp[o + 2 + i / 2] = (byte)(bestNib[i] | (bestNib[i + 1] << 4));
            }
            int m = (blocks + 1) * 16;
            if (loop)
            {
                PutU32(outp, m, 0xffffffff);
                PutU32(outp, m + 4, 0x1c);
                PutU32(outp, m + 8, (uint)((blocks + 1) * 28));
            }
            else outp[m + 1] = 0x07;
            return outp;
        }

        // ------------------------------------------------------------------ decode / encode one wave

        /// <summary>The wave as 16-bit mono samples (MPEG waves give null).</summary>
        public static short[] Decode(BnkFile f, BnkWave w, out int loopStart, out int loopEnd)
        {
            loopStart = loopEnd = -1;
            int at = f.DataAt + (int)w.Offset;
            int size = (int)Math.Min(w.Size, (long)f.Data.Length - at);
            if (size <= 0) return Array.Empty<short>();
            switch (w.Codec)
            {
                case BnkCodec.Pcm16BE:
                {
                    var s = new short[size / 2];
                    for (int i = 0; i < s.Length; i++) s[i] = BinaryPrimitives.ReadInt16BigEndian(f.Data.AsSpan(at + 2 * i, 2));
                    if (w.Loop) { loopStart = 0; loopEnd = s.Length; }
                    return s;
                }
                case BnkCodec.PsAdpcm:
                {
                    var s = DecodeAdpcm(f.Data, at, size, out loopStart, out loopEnd);
                    if (!w.Loop) loopStart = loopEnd = -1;
                    else { if (loopStart < 0) loopStart = 0; if (loopEnd < 0) loopEnd = s.Length; }
                    return s;
                }
                default:
                    return null;
            }
        }

        public static byte[] Encode(BnkCodec codec, short[] pcm, bool loop, int loopStart)
        {
            switch (codec)
            {
                case BnkCodec.Pcm16BE:
                {
                    int n = (pcm.Length * 2 + 15) & ~15;
                    var b = new byte[n];
                    for (int i = 0; i < pcm.Length; i++) BinaryPrimitives.WriteInt16BigEndian(b.AsSpan(2 * i, 2), pcm[i]);
                    return b;
                }
                case BnkCodec.PsAdpcm:
                    return EncodeAdpcm(pcm, loop, loopStart);
                default:
                    throw new NotSupportedException("MPEG waves can't be re-encoded here");
            }
        }

        public static uint PcmHash(short[] pcm)
        {
            var b = new byte[pcm.Length * 2];
            Buffer.BlockCopy(pcm, 0, b, 0, b.Length);
            return HmCore.Crc32(b, 0, b.Length);
        }

        // ------------------------------------------------------------------ rebuild

        public sealed class WaveEdit
        {
            public BnkWave Wave;
            /// <summary>New encoded bytes, or null to keep the original.</summary>
            public byte[] Bytes;
            public int? SampleRate;
            public bool? Loop;
        }

        /// <summary>The bank with the given waves replaced; untouched waves and every table are kept byte for byte.</summary>
        public static byte[] Rebuild(BnkFile f, IList<WaveEdit> edits)
        {
            var d = f.Data;
            var oldData = d.AsSpan(f.DataAt, f.DataSize).ToArray();
            var byWave = new Dictionary<int, WaveEdit>();
            foreach (var ed in edits) if (ed?.Wave != null) byWave[ed.Wave.HeaderAt] = ed;
            byte[] NewBytes(BnkWave wv) => byWave.TryGetValue(wv.HeaderAt, out var we) ? we.Bytes : null;

            // the old data in order: each distinct (offset, size) region once; unchanged regions stay in place relative to
            // the bytes around them, changed ones are swapped for their new bytes
            var regions = f.Waves.GroupBy(w => (w.Offset, w.Size)).OrderBy(g => g.Key.Offset).ThenByDescending(g => g.Key.Size).ToList();
            var ms = new MemoryStream();
            var placed = new List<(long OldStart, long OldEnd, long NewStart, bool Replaced)>();
            var newPlace = new Dictionary<int, (uint Off, uint Size)>();
            var appended = new List<(BnkWave W, byte[] B)>();
            long cursor = 0;
            foreach (var reg in regions)
            {
                long ro = Math.Min(reg.Key.Offset, (long)oldData.Length), rs = Math.Max(0, Math.Min(reg.Key.Size, oldData.Length - ro));
                var changed = reg.Where(w => NewBytes(w) != null).ToList();
                byte[] content = null;
                // the region takes the new bytes only if every wave sharing it changed to the same thing;
                // otherwise it keeps the old bytes for the others and the changed ones go at the end
                if (changed.Count == reg.Count() && changed.All(w => NewBytes(w).AsSpan().SequenceEqual(NewBytes(changed[0]))))
                    content = NewBytes(changed[0]);
                else
                    foreach (var w in changed) appended.Add((w, NewBytes(w)));
                if (ro < cursor)
                {
                    // inside / overlapping a region already written: unchanged waves follow it if it wasn't replaced
                    var host = placed.LastOrDefault(p => ro >= p.OldStart && ro + rs <= p.OldEnd);
                    foreach (var w in reg)
                    {
                        if (appended.Any(a => a.W == w)) continue;
                        if (content == null && host.OldEnd > 0 && !host.Replaced) newPlace[w.HeaderAt] = ((uint)(host.NewStart + ro - host.OldStart), (uint)rs);
                        else appended.Add((w, content ?? oldData.AsSpan((int)ro, (int)rs).ToArray()));
                    }
                    continue;
                }
                if (ro > cursor) ms.Write(oldData, (int)cursor, (int)(ro - cursor));
                long at = ms.Position;
                if (content == null) ms.Write(oldData, (int)ro, (int)rs);
                else ms.Write(content, 0, content.Length);
                long size = content == null ? rs : content.Length;
                foreach (var w in reg)
                    if (!appended.Any(a => a.W == w)) newPlace[w.HeaderAt] = ((uint)at, (uint)size);
                placed.Add((ro, ro + rs, at, content != null));
                cursor = ro + rs;
            }
            if (cursor < oldData.Length) ms.Write(oldData, (int)cursor, (int)(oldData.Length - cursor));
            foreach (var (w, b) in appended)
            {
                while (ms.Position % 16 != 0) ms.WriteByte(0);
                newPlace[w.HeaderAt] = ((uint)ms.Position, (uint)b.Length);
                ms.Write(b, 0, b.Length);
            }
            var data = ms.ToArray();

            var outp = new byte[f.DataAt + data.Length];
            Buffer.BlockCopy(d, 0, outp, 0, f.DataAt);
            Buffer.BlockCopy(data, 0, outp, f.DataAt, data.Length);
            PutU32(outp, 20, (uint)data.Length);
            int sb = f.SblkAt;
            if (U32(d, sb + 0x28) == (uint)f.DataSize) PutU32(outp, sb + 0x28, (uint)data.Length);
            if (U32(d, sb + 0x2c) == (uint)f.DataSize) PutU32(outp, sb + 0x2c, (uint)data.Length);
            foreach (var w in f.Waves)
            {
                if (newPlace.TryGetValue(w.HeaderAt, out var p)) { PutU32(outp, w.HeaderAt + 0x10, p.Off); PutU32(outp, w.HeaderAt + 0x14, p.Size); }
                if (byWave.TryGetValue(w.HeaderAt, out var e))
                {
                    if (e.SampleRate is int r && r != w.SampleRate)
                    {
                        var (n, fi) = PitchFor(r);
                        outp[w.HeaderAt + 2] = n; outp[w.HeaderAt + 3] = fi;
                    }
                    if (e.Loop is bool lp && w.Codec == BnkCodec.PsAdpcm)
                        PutU16(outp, w.HeaderAt + 0x0e, (ushort)(lp ? w.Flags | 0x40 : w.Flags & ~0x40));
                }
            }
            return outp;
        }

        // ------------------------------------------------------------------ WAV

        public static byte[] Wav(short[] pcm, int rate, int loopStart, int loopEnd)
        {
            bool loop = loopStart >= 0 && loopEnd > loopStart;
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            int dataBytes = pcm.Length * 2;
            int smpl = loop ? 8 + 36 + 24 : 0;
            bw.Write(Encoding.ASCII.GetBytes("RIFF")); bw.Write(36 + 8 + dataBytes + smpl); bw.Write(Encoding.ASCII.GetBytes("WAVE"));
            bw.Write(Encoding.ASCII.GetBytes("fmt ")); bw.Write(16); bw.Write((short)1); bw.Write((short)1); bw.Write(rate); bw.Write(rate * 2); bw.Write((short)2); bw.Write((short)16);
            bw.Write(Encoding.ASCII.GetBytes("data")); bw.Write(dataBytes);
            var b = new byte[dataBytes];
            Buffer.BlockCopy(pcm, 0, b, 0, dataBytes);
            bw.Write(b);
            if (loop)
            {
                bw.Write(Encoding.ASCII.GetBytes("smpl")); bw.Write(36 + 24);
                bw.Write(0); bw.Write(0); bw.Write((int)(1000000000L / rate)); bw.Write(60); bw.Write(0); bw.Write(0); bw.Write(0);
                bw.Write(1); bw.Write(0);
                bw.Write(0); bw.Write(0); bw.Write(loopStart); bw.Write(loopEnd - 1); bw.Write(0); bw.Write(0);
            }
            bw.Flush();
            return ms.ToArray();
        }

        /// <summary>A WAV as 16-bit mono (PCM 8/16/24/32-bit or float, any channel count, mixed down), with its rate and smpl loop.</summary>
        public static short[] ReadWav(byte[] d, out int rate, out int loopStart, out int loopEnd)
        {
            rate = 0; loopStart = loopEnd = -1;
            if (d.Length < 12 || Encoding.ASCII.GetString(d, 0, 4) != "RIFF" || Encoding.ASCII.GetString(d, 8, 4) != "WAVE")
                throw new InvalidDataException("not a WAV file");
            int fmt = 0, ch = 0, bits = 0, dataAt = -1, dataLen = 0;
            int o = 12;
            while (o + 8 <= d.Length)
            {
                string id = Encoding.ASCII.GetString(d, o, 4);
                int len = BitConverter.ToInt32(d, o + 4);
                if (len < 0) break;
                int body = o + 8;
                if (id == "fmt " && body + 16 <= d.Length)
                {
                    fmt = BitConverter.ToUInt16(d, body); ch = BitConverter.ToUInt16(d, body + 2); rate = BitConverter.ToInt32(d, body + 4); bits = BitConverter.ToUInt16(d, body + 14);
                    if (fmt == 0xFFFE && len >= 26 && body + 26 <= d.Length) fmt = BitConverter.ToUInt16(d, body + 24);
                }
                else if (id == "data") { dataAt = body; dataLen = (int)Math.Min(len, (long)d.Length - body); }
                else if (id == "smpl" && len >= 36 + 24 && body + 60 <= d.Length && BitConverter.ToInt32(d, body + 28) > 0)
                {
                    loopStart = BitConverter.ToInt32(d, body + 36 + 8);
                    loopEnd = BitConverter.ToInt32(d, body + 36 + 12) + 1;
                }
                o = body + len + (len & 1);
            }
            if (dataAt < 0 || ch <= 0 || rate <= 0) throw new InvalidDataException("the WAV has no format or data");
            if (!(fmt == 1 && (bits == 8 || bits == 16 || bits == 24 || bits == 32)) && !(fmt == 3 && bits == 32))
                throw new InvalidDataException($"WAV format {fmt} / {bits}-bit isn't supported (use PCM or 32-bit float)");
            int bps = bits / 8, frames = dataLen / (bps * ch);
            var pcm = new short[frames];
            for (int i = 0; i < frames; i++)
            {
                double sum = 0;
                for (int c = 0; c < ch; c++)
                {
                    int at = dataAt + (i * ch + c) * bps;
                    double v = bits switch
                    {
                        8 => (d[at] - 128) / 128.0,
                        16 => BitConverter.ToInt16(d, at) / 32768.0,
                        24 => ((d[at] | (d[at + 1] << 8) | ((sbyte)d[at + 2] << 16))) / 8388608.0,
                        _ => fmt == 3 ? BitConverter.ToSingle(d, at) : BitConverter.ToInt32(d, at) / 2147483648.0,
                    };
                    sum += v;
                }
                pcm[i] = (short)Math.Clamp(Math.Round(sum / ch * 32768), short.MinValue, short.MaxValue);
            }
            if (bits == 16 && ch == 1)   // exact copy, no rounding
                Buffer.BlockCopy(d, dataAt, pcm, 0, frames * 2);
            return pcm;
        }

        public static string Describe(BnkFile f)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"     SCREAM sound bank (bank v{f.Version}, SBlk v{f.SblkVersion}): {f.Sounds} sound(s), {f.Grains} grain(s), {f.Waves.Count} wave(s), {f.DataSize:N0} bytes of wave data");
            foreach (var w in f.Warnings) sb.AppendLine("     Warning: " + w);
            foreach (var w in f.Waves.Take(80))
            {
                double secs = w.Codec == BnkCodec.PsAdpcm ? w.Size / 16.0 * 28 / Math.Max(1, w.SampleRate) : w.Codec == BnkCodec.Pcm16BE ? w.Size / 2.0 / Math.Max(1, w.SampleRate) : 0;
                sb.AppendLine($"       {w.Index:00} {w.Name}: {w.Codec}, {w.SampleRate} Hz, {(secs > 0 ? secs.ToString("0.00") + " s" : w.Size.ToString("N0") + " bytes")}{(w.Loop ? ", loops" : "")}");
            }
            if (f.Waves.Count > 80) sb.AppendLine($"       ... and {f.Waves.Count - 80} more");
            return sb.ToString();
        }
    }
}

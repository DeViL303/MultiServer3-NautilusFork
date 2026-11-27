#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nautilus2027.Formats.Home;

namespace Nautilus2027.Formats
{
    /// <summary>
    /// Native BNK (SCREAM sound bank) unpacking to WAV and repacking from WAV: no vgmstream needed.
    /// Unpacking writes a folder with one WAV per wave, a copy of the original bank and a NAME.bnk.json list; repacking
    /// reads that list back, re-encodes only the WAVs that changed, and keeps everything else byte for byte.
    /// </summary>
    public static class BnkConverter
    {
        public const string OriginalName = "_ORIGINAL.BNK";
        private static readonly JsonSerializerOptions Pretty = new JsonSerializerOptions { WriteIndented = true };

        private static string Safe(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "") sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return sb.Length == 0 ? "wave" : sb.ToString();
        }

        public static string Describe(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"-- {Path.GetFileName(path)} ({new FileInfo(path).Length:N0} bytes)");
            if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                var (bank, waves, _) = LoadManifest(path);
                sb.AppendLine($"     Unpacked bank {bank.Waves.Count} wave(s); {waves.Count} listed in the JSON");
                return sb.ToString();
            }
            sb.Append(HmBnk.Describe(HmBnk.Read(File.ReadAllBytes(path))));
            return sb.ToString();
        }

        /// <summary>Unpacks a bank into outputRoot\NAME\ and returns the lines to report.</summary>
        public static List<string> Unpack(string bnkPath, string outputRoot)
        {
            var log = new List<string>();
            var bytes = File.ReadAllBytes(bnkPath);
            var f = HmBnk.Read(bytes);
            string name = Path.GetFileNameWithoutExtension(bnkPath).ToUpperInvariant();
            string dir = Path.Combine(outputRoot, name);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, OriginalName), bytes);
            var list = new JsonArray();
            int ok = 0;
            foreach (var w in f.Waves)
            {
                string baseName = $"{w.Index:00}_{Safe(w.Name)}";
                var entry = new JsonObject
                {
                    ["index"] = w.Index,
                    ["name"] = w.Name,
                    ["codec"] = w.Codec.ToString(),
                    ["rate"] = w.SampleRate,
                    ["loop"] = w.Loop,
                };
                try
                {
                    var pcm = HmBnk.Decode(f, w, out int ls, out int le);
                    if (pcm == null)
                    {
                        // MPEG: stored as it is (after its 0x80-byte header)
                        int at = f.DataAt + (int)w.Offset + 0x80, len = (int)w.Size - 0x80;
                        if (len > 0 && at + len <= bytes.Length) File.WriteAllBytes(Path.Combine(dir, baseName + ".mp3"), bytes.AsSpan(at, len).ToArray());
                        entry["file"] = baseName + ".mp3";
                        entry["note"] = "MPEG wave: extracted as stored, kept as it was on repack";
                        log.Add($"     {baseName}.mp3 (MPEG, as stored)");
                    }
                    else
                    {
                        File.WriteAllBytes(Path.Combine(dir, baseName + ".wav"), HmBnk.Wav(pcm, w.SampleRate, ls, le));
                        entry["file"] = baseName + ".wav";
                        entry["hash"] = HmBnk.PcmHash(pcm).ToString("x8");
                        if (w.Loop) { entry["loopStart"] = Math.Max(0, ls); entry["loopEnd"] = le < 0 ? pcm.Length : le; }
                        entry["seconds"] = Math.Round(pcm.Length / (double)Math.Max(1, w.SampleRate), 3);
                    }
                    ok++;
                }
                catch (Exception ex)
                {
                    log.Add($"     {baseName}: could not be decoded ({ex.Message})");
                    entry["error"] = ex.Message;
                }
                list.Add(entry);
            }
            var manifest = new JsonObject
            {
                ["format"] = "Home BNK",
                ["source"] = Path.GetFileName(bnkPath),
                ["original"] = OriginalName,
                ["note"] = "edit or replace the WAVs (any rate / channels / bit depth: they are mixed to mono 16-bit), set \"loop\" if needed, " +
                           "then repack this .bnk.json. Unchanged WAVs keep their original data exactly; a WAV at a different rate changes the wave's pitch to match",
                ["waves"] = list,
            };
            string manifestPath = Path.Combine(dir, name + ".bnk.json");
            File.WriteAllText(manifestPath, manifest.ToJsonString(Pretty), new UTF8Encoding(false));
            log.Insert(0, $"-- Unpacked: {Path.GetFileName(bnkPath)} -> {dir} ({ok} of {f.Waves.Count} wave(s))");
            foreach (var w in f.Warnings) log.Add("     Warning: " + w);
            return log;
        }

        private static (BnkFile Bank, List<JsonObject> Waves, string Dir) LoadManifest(string manifestPath)
        {
            JsonNode root;
            try { root = JsonNode.Parse(File.ReadAllText(manifestPath), documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }); }
            catch (JsonException ex) { throw new InvalidDataException("the .bnk.json can't be read: " + ex.Message); }
            if (!(root is JsonObject j) || !(j["format"] is JsonValue fv && fv.TryGetValue(out string fmt) && fmt == "Home BNK"))
                throw new InvalidDataException("not a BNK list made by this tool (\"format\": \"Home BNK\")");
            string dir = Path.GetDirectoryName(Path.GetFullPath(manifestPath));
            string orig = j["original"] is JsonValue ov && ov.TryGetValue(out string o) ? o : OriginalName;
            if (orig.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || orig.Contains("..")) orig = OriginalName;
            string origPath = Path.Combine(dir, orig);
            if (!File.Exists(origPath)) throw new InvalidDataException($"{orig} (the original bank) isn't beside the .bnk.json");
            var bank = HmBnk.Read(File.ReadAllBytes(origPath));
            var waves = ((j["waves"] as JsonArray) ?? new JsonArray()).OfType<JsonObject>().ToList();
            return (bank, waves, dir);
        }

        private static int? Int(JsonNode n) => n is JsonValue v && v.TryGetValue(out double d) && double.IsFinite(d) ? (int)d : (int?)null;
        private static bool? Bool(JsonNode n) => n is JsonValue v && v.TryGetValue(out bool b) ? b : (bool?)null;
        private static string Str(JsonNode n) => n is JsonValue v && v.TryGetValue(out string s) ? s : null;

        /// <summary>Repacks an unpacked bank (its .bnk.json) to outputDir\NAME.BNK; returns the lines to report.</summary>
        public static List<string> Pack(string manifestPath, string outputDir, OverwriteBehavior overwrite)
        {
            var log = new List<string>();
            var (bank, waves, dir) = LoadManifest(manifestPath);
            var edits = new List<HmBnk.WaveEdit>();
            int changed = 0;
            foreach (var wj in waves)
            {
                int? idx = Int(wj["index"]);
                if (idx == null || idx < 0 || idx >= bank.Waves.Count) { log.Add($"     a wave in the JSON has no valid index: skipped"); continue; }
                var w = bank.Waves[idx.Value];
                string file = Str(wj["file"]);
                if (w.Codec == BnkCodec.Mpeg) continue;
                if (file == null || file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { log.Add($"     wave {idx}: no file named: kept"); continue; }
                string wavPath = Path.Combine(dir, file);
                if (!File.Exists(wavPath)) { log.Add($"     {file} is missing: wave kept as it was"); continue; }
                short[] pcm;
                int rate, ls, le;
                try { pcm = HmBnk.ReadWav(File.ReadAllBytes(wavPath), out rate, out ls, out le); }
                catch (Exception ex) { log.Add($"     {file}: {ex.Message}: wave kept as it was"); continue; }
                if (pcm.Length == 0) { log.Add($"     {file} is empty: wave kept as it was"); continue; }
                bool loop = Bool(wj["loop"]) ?? w.Loop;
                int loopStart = ls >= 0 ? ls : Int(wj["loopStart"]) ?? 0;
                string hash = HmBnk.PcmHash(pcm).ToString("x8");
                bool samePcm = hash == Str(wj["hash"]);
                bool sameLoop = loop == w.Loop;
                bool sameRate = rate == w.SampleRate;
                if (samePcm && sameLoop && sameRate) continue;
                var e = new HmBnk.WaveEdit { Wave = w };
                if (!samePcm || !sameLoop)
                {
                    e.Bytes = HmBnk.Encode(w.Codec, pcm, loop, loopStart);
                    e.Loop = loop;
                }
                if (!sameRate) e.SampleRate = rate;
                edits.Add(e);
                changed++;
                log.Add($"     {file}: " + string.Join(", ", new[]
                {
                    !samePcm ? $"re-encoded ({w.Codec}, {pcm.Length / (double)rate:0.00} s)" : null,
                    !sameRate ? $"rate {w.SampleRate} -> {rate} Hz (pitch changed to match)" : null,
                    !sameLoop ? (loop ? "now loops" : "no longer loops") : null,
                }.Where(x => x != null)));
            }
            byte[] outBytes = changed == 0 ? bank.Data : HmBnk.Rebuild(bank, edits);
            var back = HmBnk.Read(outBytes);
            if (back.Waves.Count != bank.Waves.Count) throw new InvalidDataException("read-back check failed (wave count changed)");
            string name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(manifestPath)).ToUpperInvariant();
            Directory.CreateDirectory(outputDir);
            string outName = ModelConverter.ReserveName(name, n => Path.Combine(outputDir, n + ".BNK"), overwrite, out bool renamed);
            if (outName == null) { log.Insert(0, $"-- SKIPPED: {name}.BNK already exists - See settings to change"); return log; }
            string outPath = Path.Combine(outputDir, outName + ".BNK");
            File.WriteAllBytes(outPath, outBytes);
            log.Insert(0, $"-- Success: {Path.GetFileName(manifestPath)} -> {outPath}{(renamed ? " (Renamed)" : "")} ({changed} wave(s) changed, {outBytes.Length:N0} bytes)" +
                          (changed == 0 ? " - identical to the original" : ""));
            return log;
        }
    }
}

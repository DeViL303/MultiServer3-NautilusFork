#nullable disable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace Nautilus2027.Formats.Home
{
    public sealed class RigControl
    {
        public uint Hash;
        public string Name;
        /// <summary>The control's four stored values (its rest settings; mostly 0).</summary>
        public float[] Values = new float[4];
    }

    /// <summary>A Home .RIG face definition ("FD05", the head control rig): the face controls (sliders) and their settings.</summary>
    public sealed class RigFile
    {
        public byte[] Header = new byte[16];
        /// <summary>The block before the controls (128 bytes in the game's files, all zero); kept as it is.</summary>
        public byte[] Block0 = new byte[128];
        public List<RigControl> Controls = new List<RigControl>();
        /// <summary>Everything after the control names (an "AU01" action-unit block), with the four pointers into it.</summary>
        public byte[] Tail = Array.Empty<byte>();
        public long[] TailPointers = new long[4];
        public List<string> Warnings = new List<string>();
    }

    public static class HmRig
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Control names from the game (kHash_atg_fa_ctrl_*); names are hashed with HashName.</summary>
        public static readonly string[] KnownNames =
        {
            "asian", "black", "blink", "bot", "bot_lip", "brow_height_width", "brow_set_angle", "cheeks_depth_scale", "cheeks_height_width",
            "cheekup", "chin_asymmetry", "chin_doublechin_angle", "chin_height_width", "cranium_asymmetry", "cranium_scale_width",
            "ears_advanced_scale", "ears_asymmetry", "ears_scale_angle", "eyes", "eyes_asymmetry", "eyes_droop_shape",
            "eyes_height_distanceapart", "eyes_scale_angle", "face_asymmetry", "face_forehead_width", "face_scale_depth", "frown",
            "general_growth", "head_height_width", "head_slope_depth", "japanese", "jaw_inout", "jaw_swing", "jowl_height_width",
            "latino", "lips_asymmetry", "lips_fullness_depth", "lips_height_width", "lipswing", "middleeast", "nativeamerican",
            "nativeaustralian", "neck_length_thickness", "neck_weight_fat", "nose", "nose_broken", "nose_height_width",
            "nose_length_angle", "open", "oriental", "pout", "purse", "scared", "sneer", "tongue", "tongue_out", "top", "up", "white",
        };

        private static Dictionary<uint, string> _names;

        public static string NameOf(uint hash)
        {
            if (_names == null)
            {
                var d = new Dictionary<uint, string>();
                foreach (var n in KnownNames) d[HmCore.HashName("atg_fa_ctrl_" + n)] = "atg_fa_ctrl_" + n;
                _names = d;
            }
            return _names.TryGetValue(hash, out var s) ? s : null;
        }

        private static uint U32(byte[] d, int o) => BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(o, 4));

        public static bool Looks(byte[] d) => d != null && d.Length >= 0x30 && d[0] == (byte)'F' && d[1] == (byte)'D' && d[2] == (byte)'0';

        private static long Ptr(byte[] d, int field)
        {
            uint v = U32(d, field);
            return v == 0 ? -1 : field + (long)v;
        }

        public static RigFile Read(byte[] d)
        {
            if (!Looks(d)) throw new InvalidDataException("not a Home face rig (no \"FD0\" header)");
            var r = new RigFile();
            Buffer.BlockCopy(d, 0, r.Header, 0, 16);
            if (d[3] != (byte)'5') r.Warnings.Add($"face rig version FD0{(char)d[3]} (this tool was built from FD05 files)");
            int n = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(6, 2));
            var p = Enumerable.Range(0, 7).Select(i => Ptr(d, 0x10 + 4 * i)).ToArray();
            bool Fits(long at, long len) => at >= 0x30 && at + len <= d.Length;
            if (!Fits(p[0], 0) || !Fits(p[1], 16L * n) || !Fits(p[2], 4L * n) || p[1] < p[0] || p[2] < p[1] + 16L * n)
                throw new InvalidDataException("the face rig's tables don't fit in the file (damaged or a different layout)");
            r.Block0 = d.AsSpan((int)p[0], (int)(p[1] - p[0])).ToArray();
            for (int i = 0; i < n; i++)
            {
                var c = new RigControl { Hash = U32(d, (int)p[2] + 4 * i) };
                c.Name = NameOf(c.Hash);
                for (int k = 0; k < 4; k++) c.Values[k] = BitConverter.Int32BitsToSingle((int)U32(d, (int)p[1] + 16 * i + 4 * k));
                r.Controls.Add(c);
            }
            for (int i = 1; i < n; i++)
                if (r.Controls[i].Hash <= r.Controls[i - 1].Hash) { r.Warnings.Add("the control names aren't in hash order (the game looks them up by order)"); break; }
            long tail = p[3] >= 0 ? p[3] : d.Length;
            if (tail < p[2] + 4L * n || tail > d.Length) { r.Warnings.Add("the action-unit block pointer is out of place"); tail = Math.Min(d.Length, (p[2] + 4L * n + 15) & ~15L); }
            r.Tail = d.AsSpan((int)tail).ToArray();
            for (int i = 0; i < 4; i++) r.TailPointers[i] = p[3 + i] < 0 ? -1 : p[3 + i] - tail;
            if (r.Tail.Length >= 4 && Encoding.ASCII.GetString(r.Tail, 0, 4) != "AU01") r.Warnings.Add("no \"AU01\" block after the controls");
            return r;
        }

        public static byte[] Write(RigFile r)
        {
            var list = r.Controls.OrderBy(c => c.Hash).ToList();
            int n = list.Count;
            if (n > 65535) throw new InvalidDataException("too many controls");
            if (list.Select(c => c.Hash).Distinct().Count() != n) throw new InvalidDataException("two controls have the same name / hash");
            long pA = 0x30, pB = pA + r.Block0.Length, pH = pB + 16L * n, pT = (pH + 4L * n + 15) & ~15L;
            var d = new byte[pT + r.Tail.Length];
            Buffer.BlockCopy(r.Header, 0, d, 0, 16);
            BinaryPrimitives.WriteUInt16BigEndian(d.AsSpan(6), (ushort)n);
            var targets = new long[] { pA, pB, pH }.Concat(r.TailPointers.Select(t => t < 0 ? -1 : pT + t)).ToArray();
            for (int i = 0; i < 7; i++)
                BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(0x10 + 4 * i), targets[i] < 0 ? 0u : (uint)(targets[i] - (0x10 + 4 * i)));
            Buffer.BlockCopy(r.Block0, 0, d, (int)pA, r.Block0.Length);
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < 4; k++) BinaryPrimitives.WriteInt32BigEndian(d.AsSpan((int)pB + 16 * i + 4 * k), BitConverter.SingleToInt32Bits(list[i].Values[k]));
                BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan((int)pH + 4 * i), list[i].Hash);
            }
            Buffer.BlockCopy(r.Tail, 0, d, (int)pT, r.Tail.Length);
            return d;
        }

        public static string HashText(uint h) => "0x" + h.ToString("x8");

        public static JsonObject ToJson(RigFile r, string sourceName, IList<string> exposed)
        {
            var arr = new JsonArray();
            foreach (var c in r.Controls)
            {
                var o = new JsonObject { ["name"] = c.Name ?? HashText(c.Hash) };
                if (c.Name != null) o["hash"] = HashText(c.Hash);
                o["values"] = new JsonArray(c.Values.Select(v => (JsonNode)JsonValue.Create(double.IsFinite(v) ? double.Parse(v.ToString("R", Inv), Inv) : 0)).ToArray());
                if (exposed != null) o["slider"] = exposed.Contains(c.Name ?? "", StringComparer.OrdinalIgnoreCase);
                arr.Add(o);
            }
            var j = new JsonObject
            {
                ["format"] = "Home RIG",
                ["source"] = sourceName,
                ["note"] = "the head's face controls. Change a control's values, rename it, or add / remove controls (a new control needs its name, " +
                           "atg_fa_ctrl_..., and must also be a blend shape / control of the head model to do anything). \"slider\" marks the controls the HEADCONTROLRIG.XML lists",
                ["version"] = Encoding.ASCII.GetString(r.Header, 0, 4),
                ["controls"] = arr,
                ["block0"] = Convert.ToHexString(r.Block0),
                ["tail"] = Convert.ToHexString(r.Tail),
                ["tailPointers"] = new JsonArray(r.TailPointers.Select(t => (JsonNode)JsonValue.Create(t)).ToArray()),
                ["header"] = Convert.ToHexString(r.Header),
            };
            return j;
        }

        private static string JStr(JsonNode n) => n is JsonValue v && v.TryGetValue(out string s) ? s : null;

        private static byte[] Hex(JsonNode n, string what, byte[] def)
        {
            string s = JStr(n);
            if (s == null) return def;
            try { return Convert.FromHexString(s); }
            catch (FormatException) { throw new InvalidDataException($"\"{what}\" isn't valid hex (leave it as the tool wrote it)"); }
        }

        public static uint ParseName(string s, out string name)
        {
            name = null;
            s = (s ?? "").Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && uint.TryParse(s.Substring(2), NumberStyles.HexNumber, Inv, out uint h)) return h;
            if (s.StartsWith("#") && uint.TryParse(s.Substring(1), NumberStyles.HexNumber, Inv, out h)) return h;
            if (s.Length == 0) throw new InvalidDataException("a control has no name");
            name = s.ToLowerInvariant();   // the game's names are all lower case
            return HmCore.HashName(name);
        }

        public static RigFile FromJson(JsonNode root, List<string> notes)
        {
            if (!(root is JsonObject j) || JStr(j["format"]) != "Home RIG") throw new InvalidDataException("not a RIG JSON made by this tool (\"format\": \"Home RIG\")");
            var r = new RigFile
            {
                Header = Hex(j["header"], "header", Encoding.ASCII.GetBytes("FD05").Concat(new byte[] { 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }).ToArray()),
                Block0 = Hex(j["block0"], "block0", new byte[128]),
                Tail = Hex(j["tail"], "tail", Encoding.ASCII.GetBytes("AU01").Concat(new byte[12]).ToArray()),
            };
            if (r.Header.Length != 16) throw new InvalidDataException("\"header\" must be 16 bytes");
            if (j["tailPointers"] is JsonArray tp && tp.Count == 4)
                for (int i = 0; i < 4; i++) r.TailPointers[i] = tp[i] is JsonValue v && v.TryGetValue(out long t) && t >= -1 && t <= r.Tail.Length ? t : 0;
            else r.TailPointers = new long[] { 0, 0, r.Tail.Length, r.Tail.Length };
            foreach (var cn in (j["controls"] as JsonArray) ?? new JsonArray())
            {
                if (!(cn is JsonObject co)) continue;
                uint hash = ParseName(JStr(co["name"]), out string name);
                var c = new RigControl { Hash = hash, Name = name ?? NameOf(hash) };
                if (co["values"] is JsonArray va)
                    for (int k = 0; k < 4 && k < va.Count; k++)
                        if (va[k] is JsonValue vv && vv.TryGetValue(out double dv) && double.IsFinite(dv)) c.Values[k] = (float)dv;
                if (name != null && NameOf(hash) == null && !name.StartsWith("atg_fa_ctrl_", StringComparison.OrdinalIgnoreCase))
                    notes?.Add($"control \"{name}\" isn't one of the game's names (atg_fa_ctrl_...)");
                if (r.Controls.Any(x => x.Hash == hash)) { notes?.Add($"control {c.Name ?? HashText(hash)} is listed twice: the first is kept"); continue; }
                r.Controls.Add(c);
            }
            if (r.Controls.Count == 0) throw new InvalidDataException("the JSON lists no controls");
            r.Controls = r.Controls.OrderBy(c => c.Hash).ToList();
            return r;
        }

        public static string Describe(RigFile r, IList<string> exposed, string xmlName)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"     Home face rig ({Encoding.ASCII.GetString(r.Header, 0, 4)}), {r.Controls.Count} control(s), {r.Controls.Count(c => c.Name == null)} with unknown names");
            if (exposed != null) sb.AppendLine($"     {xmlName} lists {exposed.Count} of them as sliders");
            foreach (var w in r.Warnings) sb.AppendLine("     Warning: " + w);
            foreach (var c in r.Controls.OrderBy(c => c.Name ?? "~" + HashText(c.Hash), StringComparer.OrdinalIgnoreCase))
            {
                string vals = c.Values.Any(v => v != 0) ? "  values " + string.Join(" ", c.Values.Select(v => v.ToString("0.###", Inv))) : "";
                string tag = exposed != null && exposed.Contains(c.Name ?? "", StringComparer.OrdinalIgnoreCase) ? "  [slider]" : "";
                sb.AppendLine($"       {c.Name ?? HashText(c.Hash)}{tag}{vals}");
            }
            if (exposed != null)
                foreach (var x in exposed.Where(x => !r.Controls.Any(c => string.Equals(c.Name, x, StringComparison.OrdinalIgnoreCase))))
                    sb.AppendLine($"     Warning: {xmlName} lists {x}, which the rig doesn't have");
            return sb.ToString();
        }
    }
}

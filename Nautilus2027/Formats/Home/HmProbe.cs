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
    public sealed class ProbeLight
    {
        public int Index, Offset;
        public string Name;
        public uint Type;
        /// <summary>The record's float blocks: shape settings, position, direction, extent, colour (x intensity); 4 floats each.</summary>
        public float[] Shape = new float[4], Position = new float[4], Direction = new float[4], Extent = new float[4], Colour = new float[4];
    }

    public sealed class ProbeFile
    {
        public byte[] Data;
        public int Version;
        public float[] GridOrigin = new float[3], GridSpacing = new float[3];
        public int LightsAt;
        public List<ProbeLight> Lights = new List<ProbeLight>();
        public List<string> Warnings = new List<string>();
    }

    /// <summary>
    /// Home light probe (.PROBE): the scene's lights (as baked from Maya) followed by the baked lighting grid. The lights can be
    /// read and edited (name, colour, position, direction...); the grid is kept as it is, so a change shows in what the
    /// game lights live but not in the baked light until the probe is baked again.
    /// </summary>
    public static class HmProbe
    {
        private const int Record = 0x70, NameLen = 0x1c;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public static readonly string[] TypeNames = { "sun", "point", "spot", "area" };

        public static string TypeName(uint t) => t < TypeNames.Length ? TypeNames[t] : "type " + t;

        private static uint U32(byte[] d, int o) => BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(o, 4));
        private static float F32(byte[] d, int o) => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(o, 4)));
        private static void PutF32(byte[] d, int o, float v) => BinaryPrimitives.WriteInt32BigEndian(d.AsSpan(o, 4), BitConverter.SingleToInt32Bits(v));
        private static void PutU32(byte[] d, int o, uint v) => BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(o, 4), v);

        public static bool Looks(byte[] d) => d != null && d.Length >= 0x40 && d[0] == (byte)'P' && d[1] == (byte)'R';

        public static ProbeFile Read(byte[] d)
        {
            if (!Looks(d)) throw new InvalidDataException("not a Home light probe (no \"PR\" header)");
            var f = new ProbeFile { Data = d, Version = d[2] };
            uint n = U32(d, 4);
            f.LightsAt = d[2] == 2 ? 0x4030 : 0x40;
            if (d[2] != 1 && d[2] != 2 && d[2] != 0xff) f.Warnings.Add($"unknown probe version {d[2]:x2}{d[3]:x2}: lights read as version 1");
            for (int i = 0; i < 3; i++) { f.GridOrigin[i] = F32(d, 0x20 + 4 * i); f.GridSpacing[i] = F32(d, 0x30 + 4 * i); }
            long fit = (d.Length - f.LightsAt) / Record;
            if (n > 4096 || n > fit)
            {
                f.Warnings.Add($"the header says {n} light(s) but only {Math.Max(0, Math.Min(fit, 4096))} fit: the file may be cut off or damaged");
                n = (uint)Math.Max(0, Math.Min(fit, 4096));
            }
            for (int i = 0; i < n; i++)
            {
                int o = f.LightsAt + i * Record;
                int e = o;
                while (e < o + NameLen && d[e] != 0) e++;
                var l = new ProbeLight { Index = i, Offset = o, Name = Encoding.Latin1.GetString(d, o, e - o), Type = U32(d, o + 0x1c) };
                for (int k = 0; k < 4; k++)
                {
                    l.Shape[k] = F32(d, o + 0x20 + 4 * k); l.Position[k] = F32(d, o + 0x30 + 4 * k); l.Direction[k] = F32(d, o + 0x40 + 4 * k);
                    l.Extent[k] = F32(d, o + 0x50 + 4 * k); l.Colour[k] = F32(d, o + 0x60 + 4 * k);
                }
                f.Lights.Add(l);
            }
            return f;
        }

        public static string Describe(ProbeFile f)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"     Home light probe (version {f.Data[2]:x2}{f.Data[3]:x2}), {f.Lights.Count} light(s), grid from ({V(f.GridOrigin)}) every ({V(f.GridSpacing)}), {f.Data.Length:N0} bytes");
            foreach (var w in f.Warnings) sb.AppendLine("     Warning: " + w);
            foreach (var g in f.Lights.GroupBy(l => TypeName(l.Type))) sb.AppendLine($"       {g.Count()} {g.Key} light(s)");
            foreach (var l in f.Lights.Take(40))
                sb.AppendLine($"       {l.Index}: {l.Name} ({TypeName(l.Type)}) at ({V(l.Position)})" +
                              (l.Type == 0 ? $" toward ({V(l.Direction)})" : "") + $", colour ({V(l.Colour)})");
            if (f.Lights.Count > 40) sb.AppendLine($"       ... and {f.Lights.Count - 40} more");
            return sb.ToString();
        }

        private static string V(float[] v) => string.Join(" ", v.Take(3).Select(x => x.ToString("0.###", Inv)));

        private static JsonArray Arr(float[] v, int n) => new JsonArray(v.Take(n).Select(x => (JsonNode)JsonValue.Create(double.IsFinite(x) ? Math.Round((double)x, 6) : 0)).ToArray());

        public static JsonObject ToJson(ProbeFile f, string sourceName)
        {
            var lights = new JsonArray();
            foreach (var l in f.Lights)
                lights.Add(new JsonObject
                {
                    ["index"] = l.Index,
                    ["name"] = l.Name,
                    ["type"] = TypeName(l.Type),
                    ["shape"] = Arr(l.Shape, 4),
                    ["position"] = Arr(l.Position, 3),
                    ["direction"] = Arr(l.Direction, 3),
                    ["extent"] = Arr(l.Extent, 4),
                    ["colour"] = Arr(l.Colour, 4),
                });
            return new JsonObject
            {
                ["format"] = "Home PROBE",
                ["source"] = sourceName,
                ["note"] = "edit the lights (name up to 27 letters, type, position, direction, colour...); the baked lighting grid is kept as it is, so bake the probe again in the Home tools for the baked light to follow",
                ["grid"] = new JsonObject { ["origin"] = Arr(f.GridOrigin, 3), ["spacing"] = Arr(f.GridSpacing, 3) },
                ["lights"] = lights,
                ["file"] = new JsonObject { ["data"] = Convert.ToBase64String(f.Data) },
            };
        }

        private static string JStr(JsonNode n) => n is JsonValue v && v.TryGetValue(out string s) ? s : null;

        private static void Floats(JsonNode n, float[] into, int count)
        {
            if (!(n is JsonArray a)) return;
            for (int i = 0; i < Math.Min(count, a.Count); i++)
                if (a[i] is JsonValue v)
                {
                    if (!(v.TryGetValue(out double d) && double.IsFinite(d)) &&
                        !(v.TryGetValue(out string s) && double.TryParse(s, NumberStyles.Float, Inv, out d) && double.IsFinite(d))) continue;
                    // the JSON shows values to 6 places: an unchanged one keeps its exact bits
                    double shown = double.IsFinite(into[i]) ? Math.Round((double)into[i], 6) : 0;
                    if (d != shown) into[i] = (float)d;
                }
        }

        /// <summary>The probe with the JSON's light edits written into a copy of the original bytes.</summary>
        public static ProbeFile FromJson(JsonNode root, List<string> notes)
        {
            if (!(root is JsonObject) || JStr(root["format"]) != "Home PROBE") throw new InvalidDataException("not a PROBE JSON made by this tool (\"format\": \"Home PROBE\")");
            byte[] data;
            try { data = Convert.FromBase64String(JStr(root["file"].At("data")) ?? ""); }
            catch (FormatException) { throw new InvalidDataException("the JSON's \"file\" / \"data\" isn't valid base64 (it holds the original probe; don't edit it)"); }
            var f = Read(data);
            var b = (byte[])data.Clone();
            var lights = (root["lights"] as JsonArray) ?? new JsonArray();
            if (lights.Count != f.Lights.Count) notes?.Add($"the JSON has {lights.Count} light(s), the probe {f.Lights.Count}: lights can be changed but not added or removed (extra ones are ignored)");
            foreach (var node in lights)
            {
                if (!(node is JsonObject lj)) continue;
                int i = lj["index"] is JsonValue iv && iv.TryGetValue(out double di) ? (int)di : lights.IndexOf(node);
                if (i < 0 || i >= f.Lights.Count) continue;
                var l = f.Lights[i];
                string name = JStr(lj["name"]);
                if (name != null && name != l.Name)
                {
                    var nb = Encoding.Latin1.GetBytes(new string(name.Where(c => c >= 32 && c < 127).Take(NameLen - 1).ToArray()));
                    Array.Clear(b, l.Offset, NameLen);
                    Buffer.BlockCopy(nb, 0, b, l.Offset, nb.Length);
                    if (name.Length > NameLen - 1) notes?.Add($"light {i}: name cut to {NameLen - 1} letters");
                }
                string type = JStr(lj["type"]);
                if (type != null)
                {
                    int t = Array.FindIndex(TypeNames, x => string.Equals(x, type, StringComparison.OrdinalIgnoreCase));
                    if (t < 0 && type.StartsWith("type ") && uint.TryParse(type.Substring(5), out uint ut)) t = (int)ut;
                    if (t >= 0) PutU32(b, l.Offset + 0x1c, (uint)t);
                    else notes?.Add($"light {i}: unknown type \"{type}\" (kept {TypeName(l.Type)})");
                }
                var sh = (float[])l.Shape.Clone(); Floats(lj["shape"], sh, 4);
                var po = (float[])l.Position.Clone(); Floats(lj["position"], po, 3);
                var di3 = (float[])l.Direction.Clone(); Floats(lj["direction"], di3, 3);
                var ex = (float[])l.Extent.Clone(); Floats(lj["extent"], ex, 4);
                var co = (float[])l.Colour.Clone(); Floats(lj["colour"], co, 4);
                for (int k = 0; k < 4; k++)
                {
                    PutF32(b, l.Offset + 0x20 + 4 * k, sh[k]); PutF32(b, l.Offset + 0x30 + 4 * k, po[k]); PutF32(b, l.Offset + 0x40 + 4 * k, di3[k]);
                    PutF32(b, l.Offset + 0x50 + 4 * k, ex[k]); PutF32(b, l.Offset + 0x60 + 4 * k, co[k]);
                }
            }
            return Read(b);
        }
    }
}

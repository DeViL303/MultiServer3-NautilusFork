#nullable disable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

// Home particle effects (.EFX, exported by the ATG Particle Effects Tool), from the eboot's debug info (GpLoadedEffect,
// GpParticleEffect, GpParticleNode, Gp*Data) and checked on 16 effects from scenes and objects.
//
// File: u32 version (11), u32 n, n u32 relocations (offsets of pointers in the data), zero padding to 16, then the data
// (big-endian, pointers are offsets into it):
//   HeaderData (28 bytes: flags, u16 numGroups, user data / game data / user event counts and pointers)
//   u32 count + function hashes (~crc32 of "Id" + function name, e.g. IdConeEmitter) - nodes and render stages index this
//   u32 count + offsets (command patch locations, kept as they are)
//   GroupData[numGroups] (160 bytes each, 16-aligned): emission and update GpParticleEffect (node lists + command buffers),
//   render stages (textures, attributes), particle count, max emission, life time, bounding boxes
//   ... command buffers, nodes, strings.
// A node's settings are uniformSize bytes at its effect's execute buffer + uniformOffset (emitters: origin / orientation /
// scale; Shaper: scale, offset, curve values; EmissionTrigger; PlaneCollision; ConstantEmitter: constants).
//
// The JSON keeps the original file (base64) and lists every setting; converting back patches the changed values into it, so
// an unedited JSON gives the same bytes. The node graph itself (adding / removing nodes) can only be changed in the ATG tool.
namespace Nautilus2027.Formats.Home
{
    public sealed class EfxNode
    {
        public int Offset;          // of the GpParticleNode record
        public uint Hash;
        public string Function;
        public int DataOffset, DataSize;
    }

    public sealed class EfxEffect
    {
        public int Offset;
        public byte[] Info = new byte[16];
        public int Exec;
        public List<EfxNode> Nodes = new List<EfxNode>();
    }

    public sealed class EfxRenderStage
    {
        public int Offset;
        public string Function;
        public int[] TextureOffsets = new int[2];   // pointer locations
        public string[] Textures = new string[2];
        public List<(int Offset, uint Resource)> Attribs = new List<(int, uint)>();
        public int SettingsOffset, SettingsSize;
    }

    public sealed class EfxGroup
    {
        public int Offset;
        public EfxEffect Emission, Update;
        public List<EfxRenderStage> Stages = new List<EfxRenderStage>();
        public List<int> BoundingBoxes = new List<int>();
    }

    public sealed class EfxFile
    {
        public uint Version;
        public List<uint> Relocs = new List<uint>();
        public byte[] Blob;
        // decoded
        public uint Flags;
        public List<uint> Functions = new List<uint>();
        public List<EfxGroup> Groups = new List<EfxGroup>();
        public int UserDataIndices, GameDataPatches, UserEvents;
        public List<string> Warnings = new List<string>();
    }

    public static class HmEfx
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static readonly string[] FunctionNames =
        {
            "Add", "AnimatedTexture", "Attractor", "BlendedQuadRenderStage", "BlendedSpriteRenderStage", "ComputeBounds", "ConeEmitter",
            "ConstantEmitter", "CubeVolumeEmitter", "CylinderVolumeCheck", "CylinderVolumeEmitter", "Damping", "Effect", "EmissionTrigger",
            "EulerIntegrateOrientation", "EulerIntegratePosition", "GenerateSortedIndices", "GravityForce", "InverseDistance", "Length", "Mul",
            "Noise", "PackFloatN", "PackHalfN", "PackUnsignedCharN", "PlaneCollision", "Prune", "QuadRenderStage", "RandomAxialRotation",
            "RandomEmitter", "Select", "Shaper", "SphereSurfaceEmitter", "SphereVolumeCheck", "SphereVolumeEmitter", "SpriteRenderStage",
            "Subtract", "Turbulence", "Twister", "UnpackFloatN", "UnpackHalfN", "UnpackUnsignedCharN", "UpdateAge", "VelocityQuadRenderStage",
            "VelocityTrailRenderStage", "VolumeDistance", "VolumeInclusion", "Vortex", "Wind", "WriteEvents",
        };

        private static Dictionary<uint, string> _names;
        public static string FunctionName(uint hash)
        {
            if (_names == null)
            {
                var d = new Dictionary<uint, string>();
                foreach (var n in FunctionNames) d[HmCore.HashName("Id" + n)] = n;
                _names = d;
            }
            return _names.TryGetValue(hash, out var s) ? s : "0x" + hash.ToString("x8");
        }

        /// <summary>Emitters whose 40-byte settings are a placement (origin, orientation, scale).</summary>
        public static bool IsShapeEmitter(string f) =>
            f == "ConeEmitter" || f == "CubeVolumeEmitter" || f == "CylinderVolumeEmitter" || f == "SphereVolumeEmitter" || f == "SphereSurfaceEmitter";

        private static uint U32(byte[] b, int o) => BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(o, 4));
        private static ushort U16(byte[] b, int o) => BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(o, 2));
        private static float F32(byte[] b, int o) => BitConverter.Int32BitsToSingle((int)U32(b, o));
        private static void PutU32(byte[] b, int o, uint v) => BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(o, 4), v);
        private static void PutU16(byte[] b, int o, ushort v) => BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(o, 2), v);
        private static void PutF32(byte[] b, int o, float v) => PutU32(b, o, (uint)BitConverter.SingleToInt32Bits(v));

        // ------------------------------------------------------------------ binary

        public static EfxFile Read(byte[] d)
        {
            if (d.Length < 16) throw new InvalidDataException("too small for an EFX file");
            var f = new EfxFile { Version = U32(d, 0) };
            uint n = U32(d, 4);
            if (n > 100000 || 8 + 4L * n > d.Length) throw new InvalidDataException("not an EFX file (bad relocation count)");
            for (int i = 0; i < n; i++) f.Relocs.Add(U32(d, 8 + 4 * i));
            int baseOff = (int)((8 + 4 * n + 15) & ~15u);
            if (baseOff > d.Length) throw new InvalidDataException("EFX data is missing");
            f.Blob = d.AsSpan(baseOff).ToArray();
            if (f.Version != 11) f.Warnings.Add($"EFX version {f.Version} (this tool was built from version 11 files): check the result in game");
            int badRel = f.Relocs.Count(r => r + 4L > f.Blob.Length || (r & 3) != 0);
            if (badRel > 0) f.Warnings.Add($"{badRel} pointer(s) in the relocation table point outside the effect data (the file may be damaged)");
            for (int i = 0; i < baseOff - 8 - 4 * (int)n; i++)
                if (d[8 + 4 * n + i] != 0) { f.Warnings.Add("non-zero padding after the relocation table (kept as is in the data)"); break; }
            Decode(f);
            return f;
        }

        public static byte[] Write(EfxFile f)
        {
            int n = f.Relocs.Count, baseOff = (8 + 4 * n + 15) & ~15;
            var outB = new byte[baseOff + f.Blob.Length];
            PutU32(outB, 0, f.Version);
            PutU32(outB, 4, (uint)n);
            for (int i = 0; i < n; i++) PutU32(outB, 8 + 4 * i, f.Relocs[i]);
            Buffer.BlockCopy(f.Blob, 0, outB, baseOff, f.Blob.Length);
            return outB;
        }

        private static string CString(byte[] b, int o)
        {
            if (o <= 0 || o >= b.Length) return null;
            int e = o;
            while (e < b.Length && b[e] != 0) e++;
            return Encoding.Latin1.GetString(b, o, e - o);
        }

        private static void Decode(EfxFile f)
        {
            var b = f.Blob;
            void Need(long end, string what) { if (end > b.Length || end < 0) throw new InvalidDataException("EFX " + what + " runs past the end of the file"); }
            Need(32, "header");
            f.Flags = U32(b, 0);
            int numGroups = U16(b, 4);
            f.UserDataIndices = U16(b, 6); f.GameDataPatches = U16(b, 8); f.UserEvents = U16(b, 10);
            int hc = (int)U32(b, 28);
            Need(32 + 4L * hc + 4, "function table");
            for (int i = 0; i < hc; i++) f.Functions.Add(U32(b, 32 + 4 * i));
            int oc = (int)U32(b, 32 + 4 * hc);
            Need(36 + 4L * hc + 4L * oc, "offset table");
            int G = (36 + 4 * hc + 4 * oc + 15) & ~15;
            Need(G + 160L * numGroups, "group table");
            string Fn(int idx) => idx >= 0 && idx < f.Functions.Count ? FunctionName(f.Functions[idx]) : "#" + idx;
            for (int g = 0; g < numGroups; g++)
            {
                int o = G + 160 * g;
                var grp = new EfxGroup { Offset = o };
                EfxEffect Eff(int e)
                {
                    var ef = new EfxEffect { Offset = e, Exec = (int)U32(b, e + 20) };
                    Buffer.BlockCopy(b, e, ef.Info, 0, 16);
                    int nodes = (int)U32(b, e + 28), nc = U16(b, e + 12);
                    if (nodes > 0)
                    {
                        Need(nodes + 32L * nc, "node list");
                        for (int k = 0; k < nc; k++)
                        {
                            int no = nodes + 32 * k;
                            var node = new EfxNode
                            {
                                Offset = no,
                                Hash = U32(b, no),
                                Function = Fn((int)U32(b, no + 4)),
                                DataOffset = ef.Exec + (int)U32(b, no + 8),
                                DataSize = (int)U32(b, no + 12),
                            };
                            if (node.DataSize > 0 && (node.DataOffset + node.DataSize > b.Length || ef.Exec <= 0)) node.DataSize = 0;
                            ef.Nodes.Add(node);
                        }
                    }
                    return ef;
                }
                grp.Emission = Eff(o);
                grp.Update = Eff(o + 48);
                int rs = (int)U32(b, o + 96), nrs = b[o + 120];
                if (rs > 0)
                {
                    Need(rs + 36L * nrs, "render stages");
                    for (int k = 0; k < nrs; k++)
                    {
                        int s = rs + 36 * k;
                        var st = new EfxRenderStage { Offset = s, Function = Fn(b[s + 34]), SettingsOffset = (int)U32(b, s), SettingsSize = (int)U32(b, s + 4) };
                        for (int t = 0; t < 2; t++)
                        {
                            st.TextureOffsets[t] = s + 8 + 4 * t;
                            st.Textures[t] = U32(b, s + 8 + 4 * t) != 0 ? CString(b, (int)U32(b, s + 8 + 4 * t)) : null;
                        }
                        int at = (int)U32(b, s + 24), na = b[s + 32];
                        if (at > 0)
                        {
                            Need(at + 20L * na, "render attributes");
                            for (int j = 0; j < na; j++) st.Attribs.Add((at + 20 * j, U32(b, at + 20 * j)));
                        }
                        grp.Stages.Add(st);
                    }
                }
                int bb = (int)U32(b, o + 144), nbb = b[o + 114];
                if (bb > 0)
                {
                    Need(bb + 32L * nbb, "bounding boxes");
                    for (int k = 0; k < nbb; k++) grp.BoundingBoxes.Add(bb + 32 * k);
                }
                f.Groups.Add(grp);
            }
        }

        // ------------------------------------------------------------------ JSON

        private static JsonNode Fl(float v) =>
            float.IsFinite(v) ? JsonValue.Create(double.Parse(v.ToString("R", Inv), Inv)) : JsonValue.Create(v.ToString("R", Inv));

        private static JsonArray Floats(byte[] b, int o, int count)
        {
            var a = new JsonArray();
            for (int i = 0; i < count; i++) a.Add(Fl(F32(b, o + 4 * i)));
            return a;
        }

        private static string JStr(JsonNode n) => n is JsonValue v && v.TryGetValue(out string s) ? s : null;

        private static double JNum(JsonNode n, double def)
        {
            if (n is JsonValue v)
            {
                if (v.TryGetValue(out double d) && double.IsFinite(d)) return d;
                if (v.TryGetValue(out string s) && double.TryParse(s, NumberStyles.Float, Inv, out d) && double.IsFinite(d)) return d;
            }
            return def;
        }

        private static float ToF(JsonNode n)
        {
            if (n is JsonValue v)
            {
                if (v.TryGetValue(out double d)) return (float)d;
                if (v.TryGetValue(out string s) && float.TryParse(s, NumberStyles.Float, Inv, out float f)) return f;
            }
            throw new InvalidDataException("a number in the JSON can't be read: " + n?.ToJsonString());
        }

        private static string Hex(byte[] b, int o, int n) => Convert.ToHexString(b, o, n).ToLowerInvariant();

        /// <summary>The settings of one node as JSON (named fields where the struct is known).</summary>
        private static JsonObject NodeValues(byte[] b, EfxNode n)
        {
            int o = n.DataOffset, s = n.DataSize;
            var v = new JsonObject();
            if (s == 0) return v;
            if (s == 40 && IsShapeEmitter(n.Function))
            {
                v["origin"] = Floats(b, o, 3);
                v["orientation"] = Floats(b, o + 12, 4);
                v["scale"] = Floats(b, o + 28, 3);
            }
            else if (n.Function == "Shaper" && s >= 12)
            {
                int count = U16(b, o + 10);
                v["scale"] = Fl(F32(b, o));
                v["offset"] = Fl(F32(b, o + 4));
                v["flags"] = U16(b, o + 8);
                v["values"] = Floats(b, o + 12, Math.Min(count, (s - 12) / 4));
            }
            else if (n.Function == "EmissionTrigger" && s == 8)
            {
                v["maxLifeTime"] = Fl(F32(b, o));
                v["emitMode"] = b[o + 4];
                v["perFrameMaxEmission"] = b[o + 5];
            }
            else if (n.Function == "PlaneCollision" && s == 64)
            {
                v["basisA"] = Floats(b, o, 3);
                v["normal"] = Floats(b, o + 12, 3);
                v["basisB"] = Floats(b, o + 24, 3);
                v["origin"] = Floats(b, o + 36, 3);
                v["elasticity"] = Floats(b, o + 48, 3);
                v["threshold"] = Fl(F32(b, o + 60));
            }
            else if (n.Function == "WriteEvents" || n.Function == "Effect" || n.Function == "Prune" || s % 4 != 0)
                v["raw"] = Hex(b, o, s);
            else
                v["floats"] = Floats(b, o, s / 4);
            return v;
        }

        public static JsonObject ToJson(EfxFile f, string sourceName)
        {
            var b = f.Blob;
            var groups = new JsonArray();
            for (int gi = 0; gi < f.Groups.Count; gi++)
            {
                var g = f.Groups[gi];
                int o = g.Offset;
                JsonObject Eff(EfxEffect e, string kind)
                {
                    var nodes = new JsonArray();
                    foreach (var n in e.Nodes)
                    {
                        var j = new JsonObject { ["function"] = n.Function, ["id"] = "0x" + n.Hash.ToString("x8") };
                        var vals = NodeValues(b, n);
                        if (vals.Count > 0) j["settings"] = vals;
                        nodes.Add(j);
                    }
                    return new JsonObject { ["type"] = kind, ["nodes"] = nodes };
                }
                var gj = new JsonObject
                {
                    ["index"] = gi,
                    ["particleCount"] = U32(b, o + 104),
                    ["maxEmission"] = U16(b, o + 122),
                    ["lifeTime"] = Fl(F32(b, o + 124)),
                    ["groupFlags"] = U16(b, o + 112),
                    ["renderStride"] = U16(b, o + 108),
                    ["emission"] = Eff(g.Emission, "emission"),
                    ["update"] = Eff(g.Update, "update"),
                };
                var stages = new JsonArray();
                foreach (var st in g.Stages)
                {
                    var sj = new JsonObject { ["function"] = st.Function };
                    var tex = new JsonArray();
                    foreach (var t in st.Textures) tex.Add(t == null ? null : JsonValue.Create(t));
                    sj["textures"] = tex;
                    var attrs = new JsonArray();
                    foreach (var (ao, res) in st.Attribs)
                        attrs.Add(new JsonObject { ["resource"] = res, ["value"] = Floats(b, ao + 4, 4) });
                    sj["attributes"] = attrs;
                    if (st.SettingsOffset > 0 && st.SettingsSize > 0 && st.SettingsOffset + st.SettingsSize <= b.Length)
                        sj["renderSettings"] = Hex(b, st.SettingsOffset, st.SettingsSize);
                    stages.Add(sj);
                }
                gj["renderStages"] = stages;
                var bbs = new JsonArray();
                foreach (var bo in g.BoundingBoxes)
                    bbs.Add(new JsonObject { ["min"] = Floats(b, bo, 3), ["max"] = Floats(b, bo + 12, 3) });
                if (bbs.Count > 0) gj["boundingBoxes"] = bbs;
                groups.Add(gj);
            }
            return new JsonObject
            {
                ["format"] = "Home EFX",
                ["formatVersion"] = 1,
                ["source"] = sourceName,
                ["note"] = "Edit settings, textures, particle counts and bounding boxes here and convert back to EFX. Nodes can't be added or removed " +
                           "(that needs the ATG Particle Effects Tool). Group 0 with no render stage is usually the trigger that starts the others.",
                ["flags"] = "0x" + f.Flags.ToString("x8"),
                ["groups"] = groups,
                ["file"] = new JsonObject
                {
                    ["version"] = f.Version,
                    ["relocations"] = new JsonArray(f.Relocs.Select(r => (JsonNode)JsonValue.Create(r)).ToArray()),
                    ["data"] = Convert.ToBase64String(f.Blob),
                },
            };
        }

        /// <summary>The original file from the JSON with every setting in it written back (changed or not).</summary>
        public static EfxFile FromJson(JsonNode root, List<string> notes)
        {
            if (!(root is JsonObject) || !(root["format"] is JsonValue fv && fv.TryGetValue(out string fmt) && fmt == "Home EFX"))
                throw new InvalidDataException("not an EFX JSON made by this tool (\"format\": \"Home EFX\")");
            var file = root["file"] as JsonObject ?? throw new InvalidDataException("the JSON has no \"file\" section (the original effect data)");
            byte[] blob;
            try { blob = Convert.FromBase64String(file["data"] is JsonValue dv && dv.TryGetValue(out string ds) ? ds : ""); }
            catch (FormatException) { throw new InvalidDataException("the JSON's \"file\" / \"data\" isn't valid base64 (it holds the original effect; don't edit it)"); }
            if (blob.Length == 0) throw new InvalidDataException("the JSON's \"file\" / \"data\" is empty");
            var f = new EfxFile
            {
                Version = (uint)JNum(file["version"], 11),
                Relocs = ((file["relocations"] as JsonArray) ?? new JsonArray()).Select(x => (uint)JNum(x, 0)).ToList(),
                Blob = blob,
            };
            if (f.Relocs.Any(r => r + 4 > blob.Length)) throw new InvalidDataException("the JSON's relocation list doesn't fit the effect data (\"file\" was edited?)");
            Decode(f);
            byte[] b = f.Blob;
            var groups = root["groups"] as JsonArray ?? new JsonArray();
            if (groups.Count != f.Groups.Count) throw new InvalidDataException($"the JSON lists {groups.Count} groups and the effect has {f.Groups.Count}; groups can't be added or removed");
            var appended = new List<byte>();
            int changedTextures = 0;
            for (int gi = 0; gi < f.Groups.Count; gi++)
            {
                var g = f.Groups[gi];
                var gj = groups[gi] as JsonObject;
                if (gj == null) continue;
                int o = g.Offset;
                if (gj["particleCount"] != null) PutU32(b, o + 104, (uint)Math.Clamp(JNum(gj["particleCount"], U32(b, o + 104)), 0, 1_000_000));
                if (gj["maxEmission"] != null) PutU16(b, o + 122, (ushort)Math.Clamp(JNum(gj["maxEmission"], U16(b, o + 122)), 0, 65535));
                if (gj["lifeTime"] != null) PutF32(b, o + 124, ToF(gj["lifeTime"]));
                void Eff(EfxEffect e, JsonObject ej, string kind)
                {
                    var nodes = ej.At("nodes") as JsonArray;
                    if (nodes == null) return;
                    if (nodes.Count != e.Nodes.Count) throw new InvalidDataException($"group {gi} {kind}: {nodes.Count} nodes in the JSON, {e.Nodes.Count} in the effect; nodes can't be added or removed");
                    for (int k = 0; k < e.Nodes.Count; k++)
                    {
                        var n = e.Nodes[k];
                        var nj = nodes[k] as JsonObject;
                        if (nj == null) continue;
                        if (JStr(nj["function"]) != n.Function) throw new InvalidDataException($"group {gi} {kind} node {k}: is {n.Function} in the effect, {JStr(nj["function"])} in the JSON; the node graph can't be changed here");
                        if (nj["settings"] is JsonObject sv && n.DataSize > 0) WriteNode(b, n, sv, $"group {gi} {kind} node {k} ({n.Function})");
                    }
                }
                Eff(g.Emission, gj["emission"] as JsonObject, "emission");
                Eff(g.Update, gj["update"] as JsonObject, "update");
                var stages = gj["renderStages"] as JsonArray;
                for (int s = 0; stages != null && s < stages.Count && s < g.Stages.Count; s++)
                {
                    var st = g.Stages[s];
                    var sj = stages[s] as JsonObject;
                    if (sj == null) continue;
                    var tex = sj["textures"] as JsonArray;
                    for (int t = 0; tex != null && t < 2 && t < tex.Count; t++)
                    {
                        string want = JStr(tex[t]);
                        if (want != null && (want.Length > 255 || want.Any(ch => ch < 32 || ch > 126))) throw new InvalidDataException($"group {gi}: texture name \"{want}\" must be plain text up to 255 characters");
                        if (want == st.Textures[t] || string.IsNullOrEmpty(want)) continue;
                        if (st.Textures[t] == null) { notes?.Add($"group {gi}: texture slot {t + 1} is empty in the effect and can't be filled here"); continue; }
                        // new name: appended to the data, the pointer (already relocated) aimed at it
                        int at = f.Blob.Length + appended.Count;
                        appended.AddRange(Encoding.Latin1.GetBytes(want));
                        appended.Add(0);
                        PutU32(b, st.TextureOffsets[t], (uint)at);
                        changedTextures++;
                    }
                    var attrs = sj["attributes"] as JsonArray;
                    for (int a = 0; attrs != null && a < attrs.Count && a < st.Attribs.Count; a++)
                        if (attrs[a].At("value") is JsonArray va)
                            for (int q = 0; q < 4 && q < va.Count; q++) PutF32(b, st.Attribs[a].Offset + 4 + 4 * q, ToF(va[q]));
                }
                var bbs = gj["boundingBoxes"] as JsonArray;
                for (int k = 0; bbs != null && k < bbs.Count && k < g.BoundingBoxes.Count; k++)
                {
                    var mn = bbs[k].At("min") as JsonArray; var mx = bbs[k].At("max") as JsonArray;
                    for (int q = 0; q < 3; q++)
                    {
                        if (mn != null && q < mn.Count) PutF32(b, g.BoundingBoxes[k] + 4 * q, ToF(mn[q]));
                        if (mx != null && q < mx.Count) PutF32(b, g.BoundingBoxes[k] + 12 + 4 * q, ToF(mx[q]));
                    }
                }
            }
            if (appended.Count > 0)
            {
                f.Blob = b.Concat(appended).ToArray();
                notes?.Add($"{changedTextures} texture name(s) changed");
            }
            return f;
        }

        private static void WriteNode(byte[] b, EfxNode n, JsonObject v, string where)
        {
            int o = n.DataOffset, s = n.DataSize;
            void Vec(string key, int at, int count)
            {
                if (!(v[key] is JsonArray a)) return;
                if (a.Count != count) throw new InvalidDataException($"{where}: \"{key}\" needs {count} numbers");
                for (int i = 0; i < count; i++) PutF32(b, at + 4 * i, ToF(a[i]));
            }
            if (v["raw"] is JsonNode raw)
            {
                byte[] bytes;
                try { bytes = Convert.FromHexString(JStr(raw) ?? ""); }
                catch (FormatException) { throw new InvalidDataException($"{where}: \"raw\" isn't valid hex"); }
                if (bytes.Length != s) throw new InvalidDataException($"{where}: \"raw\" must stay {s} bytes");
                Buffer.BlockCopy(bytes, 0, b, o, s);
                return;
            }
            if (s == 40 && IsShapeEmitter(n.Function))
            {
                Vec("origin", o, 3); Vec("orientation", o + 12, 4); Vec("scale", o + 28, 3);
            }
            else if (n.Function == "Shaper" && s >= 12)
            {
                if (v["scale"] != null) PutF32(b, o, ToF(v["scale"]));
                if (v["offset"] != null) PutF32(b, o + 4, ToF(v["offset"]));
                if (v["flags"] != null) PutU16(b, o + 8, (ushort)Math.Clamp(JNum(v["flags"], 0), 0, 65535));
                Vec("values", o + 12, Math.Min(U16(b, o + 10), (s - 12) / 4));
            }
            else if (n.Function == "EmissionTrigger" && s == 8)
            {
                if (v["maxLifeTime"] != null) PutF32(b, o, ToF(v["maxLifeTime"]));
                if (v["emitMode"] != null) b[o + 4] = (byte)Math.Clamp(JNum(v["emitMode"], b[o + 4]), 0, 255);
                if (v["perFrameMaxEmission"] != null) b[o + 5] = (byte)Math.Clamp(JNum(v["perFrameMaxEmission"], b[o + 5]), 0, 255);
            }
            else if (n.Function == "PlaneCollision" && s == 64)
            {
                Vec("basisA", o, 3); Vec("normal", o + 12, 3); Vec("basisB", o + 24, 3); Vec("origin", o + 36, 3); Vec("elasticity", o + 48, 3);
                if (v["threshold"] != null) PutF32(b, o + 60, ToF(v["threshold"]));
            }
            else if (v["floats"] is JsonArray)
                Vec("floats", o, s / 4);
        }

        // ------------------------------------------------------------------ describe

        public static string Describe(EfxFile f)
        {
            var sb = new StringBuilder();
            var b = f.Blob;
            sb.AppendLine($"     Home particle effect (EFX v{f.Version}), {f.Groups.Count} group(s), {f.Functions.Count} node type(s) used, {f.Relocs.Count} pointers");
            foreach (var w in f.Warnings) sb.AppendLine("     Warning: " + w);
            for (int gi = 0; gi < f.Groups.Count; gi++)
            {
                var g = f.Groups[gi];
                int o = g.Offset;
                string tex = string.Join(", ", g.Stages.SelectMany(s => s.Textures).Where(t => t != null).Select(t => Path.GetFileName(t.Replace('\\', '/'))));
                sb.AppendLine($"     Group {gi}: {U32(b, o + 104)} particles, max emission {U16(b, o + 122)}" +
                              (g.Stages.Count > 0 ? $", {string.Join("/", g.Stages.Select(s => s.Function))} {tex}" : ", no rendering (trigger / events)"));
                sb.AppendLine($"        emit: {string.Join(" > ", g.Emission.Nodes.Select(n => n.Function))}");
                sb.AppendLine($"        update: {string.Join(" > ", g.Update.Nodes.Select(n => n.Function))}");
            }
            return sb.ToString();
        }
    }
}

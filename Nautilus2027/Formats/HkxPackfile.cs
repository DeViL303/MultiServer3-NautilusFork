#nullable disable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Nautilus2027.Formats
{
    /// <summary>
    /// Reads the header of a Havok binary packfile (.hkx): version, byte order, sections, the root
    /// object's class and the list of classes used. Works for big-endian (PS3) and little-endian files.
    /// </summary>
    public sealed class HkxPackfile
    {
        public const uint Magic0 = 0x57E0E057;
        public const uint Magic1 = 0x10C0C010;

        public sealed class Section
        {
            public string Tag = "";
            public int AbsoluteDataStart;
            public int LocalFixupsOffset;
            public int GlobalFixupsOffset;
            public int VirtualFixupsOffset;
            public int ExportsOffset;
            public int ImportsOffset;
            public int EndOffset;
        }

        public int UserTag;
        public int FileVersion;
        public byte PointerSize;
        public bool LittleEndian;
        public bool ReusePaddingOptimization;
        public bool EmptyBaseClassOptimization;
        public int ContentsSectionIndex;
        public int ContentsSectionOffset;
        public int ContentsClassNameSectionIndex;
        public int ContentsClassNameSectionOffset;
        public string ContentsVersion = "";
        public int Flags;
        public List<Section> Sections = new List<Section>();
        /// <summary>Class of the root object (e.g. hkRootLevelContainer), if it could be read.</summary>
        public string RootClassName;
        /// <summary>Every class listed in the __classnames__ section, in file order.</summary>
        public List<string> ClassNames = new List<string>();

        public string ByteOrder => LittleEndian ? "little-endian" : "big-endian";

        /// <summary>Quick check of the 8-byte packfile signature.</summary>
        public static bool IsPackfile(byte[] data) =>
            data.Length >= 8
            && BinaryPrimitives.ReadUInt32BigEndian(data) == Magic0
            && BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4)) == Magic1;

        /// <summary>Recognise the newer Havok tagfile format (not parsed here).</summary>
        public static bool IsTagfile(byte[] data)
        {
            if (data.Length < 8) return false;
            uint a = BinaryPrimitives.ReadUInt32LittleEndian(data), b = BinaryPrimitives.ReadUInt32BigEndian(data);
            return a == 0xCAB00D1E || b == 0xCAB00D1E || a == 0xD011FACE || b == 0xD011FACE
                || (data[4] == (byte)'T' && data[5] == (byte)'A' && data[6] == (byte)'G' && data[7] == (byte)'0');
        }

        public static HkxPackfile Read(byte[] data)
        {
            if (!IsPackfile(data))
                throw new InvalidDataException(IsTagfile(data)
                    ? "This is a Havok tagfile (newer format), which isn't supported yet."
                    : "Not a Havok packfile (missing 57 E0 E0 57 10 C0 C0 10 signature).");
            if (data.Length < 0x40)
                throw new InvalidDataException("File is too small for a Havok packfile header.");

            var hkx = new HkxPackfile();
            // The signature is the same in both byte orders; the real order is in the layout rules.
            hkx.PointerSize = data[0x10];
            hkx.LittleEndian = data[0x11] != 0;
            hkx.ReusePaddingOptimization = data[0x12] != 0;
            hkx.EmptyBaseClassOptimization = data[0x13] != 0;

            int I32(int offset)
            {
                if (offset < 0 || offset + 4 > data.Length)
                    throw new InvalidDataException($"Unexpected end of file at offset 0x{offset:X}.");
                var span = data.AsSpan(offset, 4);
                return hkx.LittleEndian ? BinaryPrimitives.ReadInt32LittleEndian(span) : BinaryPrimitives.ReadInt32BigEndian(span);
            }

            short I16(int offset)
            {
                var span = data.AsSpan(offset, 2);
                return hkx.LittleEndian ? BinaryPrimitives.ReadInt16LittleEndian(span) : BinaryPrimitives.ReadInt16BigEndian(span);
            }

            hkx.UserTag = I32(0x08);
            hkx.FileVersion = I32(0x0C);
            int numSections = I32(0x14);
            hkx.ContentsSectionIndex = I32(0x18);
            hkx.ContentsSectionOffset = I32(0x1C);
            hkx.ContentsClassNameSectionIndex = I32(0x20);
            hkx.ContentsClassNameSectionOffset = I32(0x24);
            hkx.ContentsVersion = ReadCString(data, 0x28, 16);
            hkx.Flags = I32(0x38);

            if (numSections < 0 || numSections > 64)
                throw new InvalidDataException($"Unreasonable section count {numSections}.");

            // Version 11+ (Havok 2010+) adds predicate padding after the header and 16 bytes per section header.
            int sectionStart = 0x40;
            int sectionHeaderSize = 0x30;
            if (hkx.FileVersion >= 11)
            {
                short padding = I16(0x3E);
                if (padding > 0) sectionStart += padding;
                sectionHeaderSize = 0x40;
            }

            for (int i = 0; i < numSections; i++)
            {
                int o = sectionStart + i * sectionHeaderSize;
                if (o + 0x30 > data.Length)
                    throw new InvalidDataException("Section table runs past the end of the file.");
                hkx.Sections.Add(new Section
                {
                    Tag = ReadCString(data, o, 20),
                    AbsoluteDataStart = I32(o + 0x14),
                    LocalFixupsOffset = I32(o + 0x18),
                    GlobalFixupsOffset = I32(o + 0x1C),
                    VirtualFixupsOffset = I32(o + 0x20),
                    ExportsOffset = I32(o + 0x24),
                    ImportsOffset = I32(o + 0x28),
                    EndOffset = I32(o + 0x2C),
                });
            }

            // Root object's class name: a string inside the class-name section.
            if (hkx.ContentsClassNameSectionIndex >= 0 && hkx.ContentsClassNameSectionIndex < hkx.Sections.Count)
            {
                int at = hkx.Sections[hkx.ContentsClassNameSectionIndex].AbsoluteDataStart + hkx.ContentsClassNameSectionOffset;
                if (at >= 0 && at < data.Length)
                    hkx.RootClassName = ReadCString(data, at, 256);
            }

            // Class list: entries of (u32 signature, 0x09, name\0) until the section's data ends.
            var classSection = hkx.Sections.Find(s => s.Tag == "__classnames__");
            if (classSection != null)
            {
                int pos = classSection.AbsoluteDataStart;
                int end = Math.Min(data.Length, classSection.AbsoluteDataStart + classSection.LocalFixupsOffset);
                while (pos + 5 < end)
                {
                    uint signature = (uint)I32(pos);
                    if (signature == 0xFFFFFFFF || data[pos + 4] != 0x09)
                        break;
                    string name = ReadCString(data, pos + 5, end - (pos + 5));
                    if (name.Length == 0)
                        break;
                    hkx.ClassNames.Add(name);
                    pos += 5 + name.Length + 1;
                }
            }

            return hkx;
        }

        /// <summary>A rough guess at what the file holds, from the classes it uses.</summary>
        public string DescribeContents()
        {
            var kinds = new List<string>();
            bool Has(string prefix) => ClassNames.Exists(c => c.StartsWith(prefix, StringComparison.Ordinal));
            if (Has("hkpRigidBody") || Has("hkpPhysicsData") || Has("hkpShape") || ClassNames.Exists(c => c.EndsWith("Shape", StringComparison.Ordinal)))
                kinds.Add("physics/collision");
            if (Has("hkaSkeleton")) kinds.Add("skeleton");
            if (Has("hkaAnimation") || ClassNames.Exists(c => c.Contains("Animation"))) kinds.Add("animation");
            if (Has("hkaMeshBinding") || Has("hkxMesh")) kinds.Add("mesh/skin binding");
            if (Has("hkaiNavMesh") || Has("hkaiNav")) kinds.Add("navigation mesh");
            return kinds.Count > 0 ? string.Join(", ", kinds) : "unknown";
        }

        private static string ReadCString(byte[] data, int offset, int maxLength)
        {
            if (offset < 0 || offset >= data.Length) return "";
            int end = offset;
            int limit = Math.Min(data.Length, offset + maxLength);
            while (end < limit && data[end] != 0) end++;
            return Encoding.ASCII.GetString(data, offset, end - offset);
        }
    }
}

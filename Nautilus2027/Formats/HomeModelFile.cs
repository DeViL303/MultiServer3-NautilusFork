#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nautilus2027.Formats
{
    /// <summary>
    /// Basic information about a PlayStation Home model (.mdl). The full layout isn't documented yet, so
    /// this reads the signature ("HM" or "MR0"), the version byte at offset 3, and the file paths
    /// embedded in the model (textures, materials and similar).
    /// </summary>
    public sealed class HomeModelFile
    {
        public int Size;
        public byte[] Header = Array.Empty<byte>();
        public string Signature = "";
        public byte? VersionByte;
        public List<string> ReferencedPaths = new List<string>();

        public bool HasKnownSignature => Signature == "HM" || Signature == "MR0";

        private static readonly string[] PathExtensions =
        {
            ".dds", ".png", ".tga", ".jpg", ".bmp", ".mdl", ".hkx", ".ani", ".skn", ".efx", ".effect",
            ".mat", ".material", ".shader", ".xml", ".lua", ".luac", ".probe", ".atmos",
        };

        public static HomeModelFile Read(byte[] data)
        {
            var mdl = new HomeModelFile
            {
                Size = data.Length,
                Header = data.Take(32).ToArray(),
            };

            if (data.Length >= 2 && data[0] == (byte)'H' && data[1] == (byte)'M')
                mdl.Signature = "HM";
            else if (data.Length >= 3 && data[0] == (byte)'M' && data[1] == (byte)'R' && data[2] == (byte)'0')
                mdl.Signature = "MR0";

            if (data.Length > 4)
                mdl.VersionByte = data[3];

            mdl.ReferencedPaths = FindPaths(data);
            return mdl;
        }

        public static string HexDump(byte[] bytes) => string.Join(" ", bytes.Select(b => b.ToString("X2")));

        /// <summary>Printable ASCII runs that look like file paths (end in a known extension).</summary>
        private static List<string> FindPaths(byte[] data)
        {
            var found = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sb = new StringBuilder();

            void Flush()
            {
                if (sb.Length >= 5)
                {
                    string s = sb.ToString();
                    string lower = s.ToLowerInvariant();
                    if (PathExtensions.Any(ext => lower.EndsWith(ext, StringComparison.Ordinal)) && seen.Add(s))
                        found.Add(s);
                }
                sb.Clear();
            }

            foreach (byte b in data)
            {
                if (b >= 0x20 && b < 0x7F)
                    sb.Append((char)b);
                else
                    Flush();
            }
            Flush();
            return found;
        }
    }
}

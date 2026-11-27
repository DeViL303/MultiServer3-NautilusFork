#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// Common shape every importer (glTF/GLB, OBJ, FBX) produces, as in the HOME_DEV_KIT's importers:
// a flat list of triangle parts in world space, materials that point at images, and the images themselves.
namespace Nautilus2027.Formats.Home
{
    public sealed class SourceImage
    {
        public string Name;
        public string Mime;
        public byte[] Bytes;
        public string FileName;
        private RgbaImage _rgba;
        private bool _tried;

        /// <summary>Why <see cref="Rgba"/> is null, if it is.</summary>
        public string Error;

        public SourceImage() { }

        public SourceImage(RgbaImage decoded, string name)
        {
            _rgba = decoded;
            _tried = true;
            Name = name;
        }

        /// <summary>Decoded pixels (decoded on first use); null if the image could not be read.</summary>
        public RgbaImage Rgba
        {
            get
            {
                if (_tried) return _rgba;
                _tried = true;
                try { _rgba = ImageCodec.Decode(Bytes, FileName ?? Name); }
                catch (Exception ex) { Error = ex.Message; _rgba = null; }
                return _rgba;
            }
        }
    }

    public sealed class SourceMaterial
    {
        public int Index;
        public string Name = "material";
        public string AlphaMode = "OPAQUE";     // OPAQUE | MASK | BLEND
        public double AlphaCutoff = 0.5;
        public bool DoubleSided;
        /// <summary>Slot (colourMap, normalMap, specularMap, emissiveMap) -> index into <see cref="SourceDoc.Images"/>.</summary>
        public Dictionary<string, int> Textures = new Dictionary<string, int>();
        public double[] BaseColor = { 1, 1, 1, 1 };
        /// <summary>Home material description carried in glTF extras ("home"), as JSON; null if none.</summary>
        public string HomeJson;
    }

    public sealed class SourceSkin
    {
        public List<string> Names = new List<string>();
        public List<int[]> J = new List<int[]>();
        public List<double[]> W = new List<double[]>();
    }

    public sealed class SourcePart
    {
        public string Name;
        public int Material = -1;
        public List<V3> P = new List<V3>();
        /// <summary>Null when the source has no normals (they are generated).</summary>
        public List<V3> N;
        public List<V2> U = new List<V2>();
        public List<int[]> Tris = new List<int[]>();
        public SourceSkin Skin;
        /// <summary>Second UV set (glTF TEXCOORD_1), or null.</summary>
        public List<V2> U1;
        /// <summary>Vertex colours RGBA 0..1 (glTF COLOR_0), or null.</summary>
        public List<double[]> Colour;
        /// <summary>Tangents xyz + handedness w (glTF TANGENT), or null.</summary>
        public List<double[]> Tangent;
        /// <summary>Custom per-vertex attributes (glTF attributes starting with "_"), by attribute name.</summary>
        public Dictionary<string, List<double[]>> Custom = new Dictionary<string, List<double[]>>();
        /// <summary>The original strip indices when the file stored a triangle strip (glTF mode 5), or null.</summary>
        public int[] StripIndices;
        /// <summary>Home mesh description carried in glTF extras ("home"), as JSON; null if none.</summary>
        public string HomeJson;
    }

    public sealed class SourceDoc
    {
        public string Format;
        public string Generator = "";
        public List<SourceImage> Images = new List<SourceImage>();
        public List<SourceMaterial> Materials = new List<SourceMaterial>();
        /// <summary>Parts for a transform mode: "full" (node transforms) or "noscale" (node scale ignored).</summary>
        public Func<string, List<SourcePart>> Collect;
        public bool DefaultFlipV;
        public List<string> Notes = new List<string>();
        public List<string> JointNames = new List<string>();
        /// <summary>Home model description carried in the glTF asset extras ("home"), as JSON; null if none.</summary>
        public string HomeJson;
        /// <summary>
        /// The skeleton the skinned parts use, as the ANI / SKN tab would write it (key + joint count); null when
        /// the file has no skin. Skin joint indices in <see cref="SourceSkin.J"/> are in that skeleton's joint order.
        /// </summary>
        public uint? SkeletonKey;
        public int SkeletonJoints;
        /// <summary>glTF only: node index of each skeleton joint, in skeleton order.</summary>
        public List<int> SkeletonNodes;
    }

    /// <summary>
    /// Finds files a model refers to (textures, .mtl, .bin) near the model: by the path as written, relative to the
    /// model, or by file name anywhere in the model's folder (a few levels deep) or its parent folder.
    /// </summary>
    public sealed class FileResolver
    {
        private static readonly string[] ImageExts = { "png", "jpg", "jpeg", "tga", "dds", "bmp", "webp", "gif" };
        private readonly string _dir;
        private Dictionary<string, string> _byName;

        public FileResolver(string modelPath)
        {
            _dir = Path.GetDirectoryName(Path.GetFullPath(modelPath)) ?? "";
        }

        public static string BaseName(string p) => (p ?? "").Replace('\\', '/').Split('/').Last().Trim();

        public static string Stem(string p)
        {
            string b = BaseName(p);
            int dot = b.LastIndexOf('.');
            return (dot > 0 ? b.Substring(0, dot) : b).ToLowerInvariant();
        }

        private void BuildIndex()
        {
            _byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int budget = 20000;
            void AddDir(string dir, int depth)
            {
                // breadth first so files closest to the model win
                var queue = new Queue<(string, int)>();
                queue.Enqueue((dir, 0));
                while (queue.Count > 0 && budget > 0)
                {
                    var (d, lvl) = queue.Dequeue();
                    string[] files, dirs;
                    try
                    {
                        files = Directory.GetFiles(d);
                        dirs = lvl < depth ? Directory.GetDirectories(d) : Array.Empty<string>();
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    foreach (var f in files)
                    {
                        if (--budget <= 0) break;
                        string n = Path.GetFileName(f);
                        if (!_byName.ContainsKey(n)) _byName[n] = f;
                    }
                    foreach (var sub in dirs) queue.Enqueue((sub, lvl + 1));
                }
            }
            if (Directory.Exists(_dir)) AddDir(_dir, 3);
            string parent = Path.GetDirectoryName(_dir);
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent)) AddDir(parent, 1);
        }

        private string ByName(string name)
        {
            if (_byName == null) BuildIndex();
            return _byName.TryGetValue(name, out var p) ? p : null;
        }

        /// <summary>Path of a referenced file, or null.</summary>
        public string FindPath(string reference, bool tryOtherImageExtensions = true)
        {
            if (string.IsNullOrWhiteSpace(reference)) return null;
            string r = reference.Trim().Trim('"');
            try
            {
                if (Path.IsPathRooted(r) && File.Exists(r)) return r;
                string rel = Path.Combine(_dir, r.Replace('/', Path.DirectorySeparatorChar).TrimStart('\\', '/'));
                if (File.Exists(rel)) return rel;
            }
            catch (Exception)
            {
                // invalid path characters: fall back to the name lookup
            }
            string b = BaseName(r);
            if (b.Length == 0) return null;
            string found = ByName(b);
            if (found != null || !tryOtherImageExtensions) return found;
            string s = Stem(r);
            foreach (var ext in ImageExts)
            {
                found = ByName(s + "." + ext);
                if (found != null) return found;
            }
            return null;
        }

        public byte[] Find(string reference, out string fileName, bool tryOtherImageExtensions = true)
        {
            string p = FindPath(reference, tryOtherImageExtensions);
            fileName = p != null ? Path.GetFileName(p) : null;
            if (p == null) return null;
            try { return File.ReadAllBytes(p); }
            catch (Exception) { fileName = null; return null; }
        }
    }

    /// <summary>Image list shared by a document's materials; each referenced file is loaded once.</summary>
    public sealed class ImageTable
    {
        private readonly FileResolver _files;
        private readonly Dictionary<string, int?> _index = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        public readonly List<SourceImage> Images = new List<SourceImage>();
        public readonly List<string> Notes = new List<string>();

        public ImageTable(FileResolver files) { _files = files; }

        public int? Get(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference)) return null;
            string key = FileResolver.BaseName(reference);
            if (_index.TryGetValue(key, out var cached)) return cached;
            int? i = null;
            string foundName = null;
            byte[] bytes = _files != null ? _files.Find(reference, out foundName, true) : null;
            if (bytes == null)
            {
                Notes.Add($"texture not found: {key} (put it next to the model or in a folder beside it)");
            }
            else
            {
                Images.Add(new SourceImage { Name = FileResolver.Stem(foundName), FileName = foundName, Bytes = bytes });
                i = Images.Count - 1;
            }
            _index[key] = i;
            return i;
        }

        public int? Embedded(string name, byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            Images.Add(new SourceImage { Name = FileResolver.Stem(name), FileName = FileResolver.BaseName(name), Bytes = bytes });
            return Images.Count - 1;
        }
    }

    /// <summary>Column-major 4x4 matrix helpers (glTF / FBX conventions).</summary>
    internal static class Mat4
    {
        public static double[] Identity() => new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };

        public static double[] Mul(double[] a, double[] b)
        {
            var r = new double[16];
            for (int c = 0; c < 4; c++)
                for (int rr = 0; rr < 4; rr++)
                {
                    double s = 0;
                    for (int k = 0; k < 4; k++) s += a[rr + 4 * k] * b[k + 4 * c];
                    r[rr + 4 * c] = s;
                }
            return r;
        }

        public static double Det3(double[] m) =>
            m[0] * (m[5] * m[10] - m[9] * m[6]) - m[4] * (m[1] * m[10] - m[9] * m[2]) + m[8] * (m[1] * m[6] - m[5] * m[2]);

        /// <summary>Inverse-transpose of the upper 3x3, row-major [r*3+c], for transforming normals.</summary>
        public static double[] Inv3T(double[] m)
        {
            double a00 = m[0], a01 = m[4], a02 = m[8], a10 = m[1], a11 = m[5], a12 = m[9], a20 = m[2], a21 = m[6], a22 = m[10];
            double det = a00 * (a11 * a22 - a12 * a21) - a01 * (a10 * a22 - a12 * a20) + a02 * (a10 * a21 - a11 * a20);
            if (Math.Abs(det) < 1e-30) return new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
            double i = 1 / det;
            // inverse (row-major)
            double i00 = (a11 * a22 - a12 * a21) * i, i01 = (a02 * a21 - a01 * a22) * i, i02 = (a01 * a12 - a02 * a11) * i;
            double i10 = (a12 * a20 - a10 * a22) * i, i11 = (a00 * a22 - a02 * a20) * i, i12 = (a02 * a10 - a00 * a12) * i;
            double i20 = (a10 * a21 - a11 * a20) * i, i21 = (a01 * a20 - a00 * a21) * i, i22 = (a00 * a11 - a01 * a10) * i;
            // transpose
            return new[] { i00, i10, i20, i01, i11, i21, i02, i12, i22 };
        }

        public static V3 Point(double[] m, double x, double y, double z) =>
            new V3(m[0] * x + m[4] * y + m[8] * z + m[12], m[1] * x + m[5] * y + m[9] * z + m[13], m[2] * x + m[6] * y + m[10] * z + m[14]);

        public static V3 Normal(double[] nt, double x, double y, double z) =>
            new V3(nt[0] * x + nt[1] * y + nt[2] * z, nt[3] * x + nt[4] * y + nt[5] * z, nt[6] * x + nt[7] * y + nt[8] * z);
    }
}

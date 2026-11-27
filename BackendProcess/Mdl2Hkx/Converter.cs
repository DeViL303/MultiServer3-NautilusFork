// The whole pipeline (docs/03_CONVERSION_PIPELINE.md) in order. Port of python_reference/mdl2hkx.py v0.5.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Mdl2Hkx
{
    public sealed class MeshReport
    {
        public int Index, Triangles; public string MaterialType = "", ColourTexture = "-", Decision = "kept";
        public override string ToString() => $"{Index,4} {Triangles,6}  {MaterialType,-12} {ColourTexture,-34} {Decision}";
    }

    public sealed class ConversionResult
    {
        public List<Box> Boxes = new();          // main collision boxes (world, Y up)
        public List<Box> EdgeWalls = new();      // thin walls at open drops
        public List<MeshReport> Meshes = new();
        public List<string> Log = new();
        public int TrianglesTotal, TrianglesUsed, VoxelsSurface, VoxelsKept, VoxelsRemovedTrim, VoxelsRemovedReach;
        public double MainFloor = double.NaN; public int Nx, Ny, Nz;
        public long FileSize; public TimeSpan Elapsed;
        public IEnumerable<Box> AllBoxes => Boxes.Concat(EdgeWalls);
        public string ReportText()
        {
            var sb = new StringBuilder();
            foreach (var l in Log) sb.AppendLine(l);
            sb.AppendLine(); sb.AppendLine("mesh  tris  type          colour texture                      -> decision");
            foreach (var m in Meshes) sb.AppendLine(m.ToString());
            return sb.ToString();
        }
    }

    /// <summary>One mesh of input for the converter: world-space triangles plus what the material filter looks at.</summary>
    public sealed class CollisionMesh
    {
        public int Index;
        public Vec3[] Positions = Array.Empty<Vec3>();
        public List<(int a, int b, int c)> Triangles = new();
        /// <summary>Home material type hash ("emissive", "default", glass 0x7F05531A, ...).</summary>
        public uint MaterialType;
        /// <summary>Material name and texture file names, for the foliage / skip patterns.</summary>
        public List<string> Names = new();
        public string ColourTexture = "-";
        public string? Problem;

        public static List<CollisionMesh> FromMdl(MdlModel mdl)
        {
            uint hColour = HomeHash.Name("colourMap");
            var outp = new List<CollisionMesh>();
            foreach (var me in mdl.Meshes)
            {
                var mt = me.MaterialIndex >= 0 && me.MaterialIndex < mdl.Materials.Count ? mdl.Materials[me.MaterialIndex] : new MdlMaterial();
                var cm = new CollisionMesh { Index = me.Index, MaterialType = mt.TypeHash, ColourTexture = mt.Textures.FirstOrDefault(t => t.SlotHash == hColour)?.ShortName ?? "-" };
                cm.Names.Add(mt.InstanceName);
                cm.Names.AddRange(mt.Textures.Select(t => t.ShortName));
                try { cm.Positions = me.Positions(); cm.Triangles = me.Triangles(); }
                catch (Exception ex) { cm.Problem = ex.Message; }
                outp.Add(cm);
            }
            return outp;
        }
    }

    public static class Converter
    {
        /// <param name="progress">optional callback (step name) for a UI</param>
        public static ConversionResult Run(MdlModel mdl, ConversionSettings s, Action<string>? progress = null) =>
            Run(CollisionMesh.FromMdl(mdl), s, progress);

        /// <param name="progress">optional callback (step name) for a UI</param>
        public static ConversionResult Run(IList<CollisionMesh> meshes, ConversionSettings s, Action<string>? progress = null)
        {
            var sw = Stopwatch.StartNew(); var R = new ConversionResult(); void Log(string m) { R.Log.Add(m); progress?.Invoke(m); }
            double V = s.Voxel;

            // ---- 1-2 triangles + material filter
            var fol = s.UseFoliageFilter && s.FoliageRegex.Length > 0 ? new Regex(s.FoliageRegex, RegexOptions.IgnoreCase) : null;
            var skip = s.SkipRegex.Length > 0 ? new Regex(s.SkipRegex, RegexOptions.IgnoreCase) : null;
            uint hEmissive = HomeHash.Name("emissive");
            var tris = new List<Tri>();
            foreach (var me in meshes)
            {
                var names = me.Names.Where(n => !string.IsNullOrEmpty(n)).ToList();
                string? why = null;
                if (me.Problem != null) why = "unreadable: " + me.Problem;
                else if (s.SkipEmissive && me.MaterialType == hEmissive) why = "emissive";
                else if (s.SkipGlass && me.MaterialType == MdlMaterial.GlassType) why = "glass";
                else if (s.SkipMeshes.Contains(me.Index)) why = "skip-mesh";
                else if (fol != null && names.Any(n => fol.IsMatch(n))) why = "foliage";
                else if (skip != null && names.Any(n => skip.IsMatch(n))) why = "skip pattern";
                var P = me.Positions; var T = me.Triangles;
                R.TrianglesTotal += T.Count;
                R.Meshes.Add(new MeshReport { Index = me.Index, Triangles = T.Count, MaterialType = HomeHash.NameOf(me.MaterialType),
                    ColourTexture = me.ColourTexture, Decision = why ?? "kept" });
                if (why != null) continue;
                foreach (var (a, b, c) in T)
                {
                    if (a < 0 || b < 0 || c < 0 || a >= P.Length || b >= P.Length || c >= P.Length) continue;
                    var t = new Tri { A = P[a], B = P[b], C = P[c], Mesh = me.Index };
                    if (s.MinY.HasValue && Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y)) < s.MinY.Value) continue;
                    tris.Add(t);
                }
            }
            R.TrianglesUsed = tris.Count; Log($"triangles {R.TrianglesTotal}, used {R.TrianglesUsed}");

            // ---- 3 voxelise
            var vs = Voxelizer.Build(tris, V); var occ = vs.Occ; R.Nx = occ.Nx; R.Ny = occ.Ny; R.Nz = occ.Nz;
            R.VoxelsSurface = (int)occ.Count(); Log($"grid {occ.Nx} x {occ.Ny} x {occ.Nz}, surface voxels {R.VoxelsSurface}");

            // ---- 4 sticky-out trim
            int r = Math.Max(1, (int)Math.Round(s.TrimRadius / V)), w = 2 * r + 1; double panel = w * w;
            var cnt = Morphology.CubeCount(occ, r); var seed = occ.Empty();
            for (int q = 0; q < occ.D.Length; q++) if (occ.D[q] != 0 && cnt[q] >= s.TrimKeep * panel - 1e-3) seed.D[q] = 1;
            cnt = null!;
            R.VoxelsRemovedTrim = (int)(occ.Count() - seed.Count());
            var kept = seed; for (int t = 0; t < r; t++) kept = Morphology.Dilate6(kept, occ);      // grow back inside the original
            int small = Morphology.RemoveSmallPieces(kept, s.MinPieceSize / V);
            Log($"trimmed: {R.VoxelsRemovedTrim} voxels as sticky-out, {small} small loose pieces");

            // ---- 5 optional fusing
            if (s.Close > 0 && !s.BigBlocks) kept = Morphology.CloseOr(kept, Math.Max(1, (int)Math.Round(s.Close / V / 2)));
            for (int t = 0; t < s.Thicken; t++) kept = Morphology.Dilate6(kept);

            // ---- 6 reachability
            int hc = (int)Math.Round(ConversionSettings.AvatarHeight / V);
            int Steps(double m) => Math.Max(1, (int)Math.Floor(m / V + 1e-6));
            Grid3? band = null;
            if (s.Reach > 0)
            {
                var floor = Reachability.Floors(kept, hc); var start = Reachability.StartCell(floor, vs, s.Spawn);
                var seen = Reachability.Flood(floor, start, Steps(s.FloodStep));
                band = Reachability.Band(seen, (int)Math.Round(s.Reach / V), (int)Math.Round(s.Margin / V));
                int removed = 0; for (int q = 0; q < kept.D.Length; q++) if (kept.D[q] != 0 && band.D[q] == 0) { kept.D[q] = 0; removed++; }
                R.VoxelsRemovedReach = removed;
                Log($"reachable floor cells {seen.Count()} (from {(s.Spawn.HasValue ? "spawn" : "largest floor")}), out of reach removed {removed} voxels");
            }
            var floor2 = Reachability.Floors(kept, hc);
            var walk = Reachability.Flood(floor2, Reachability.StartCell(floor2, vs, s.Spawn), Steps(s.WalkStep));

            // ---- 7 edge walls
            if (s.EdgeWallHeight > 0) { R.EdgeWalls = Reachability.EdgeWalls(kept, walk, vs, s, hc); Log($"walkable floor cells {walk.Count()}; open edges walled: {R.EdgeWalls.Count} strips"); }

            // ---- 8 boxes
            List<VBox> vb;
            int headVox = Math.Max(hc, (int)Math.Round(s.Headroom / V));
            if (s.BigBlocks)
            {
                var air = kept.Empty();
                for (int i = 0; i < walk.Nx; i++) for (int j = 0; j < walk.Ny; j++) for (int k = 0; k < walk.Nz; k++)
                    if (walk[i, j, k]) for (int d = 1; d <= headVox; d++) air[i, Math.Min(j + d, walk.Ny - 1), k] = true;
                for (int q = 0; q < air.D.Length; q++) if (kept.D[q] != 0) air.D[q] = 0;              // protected = air ∖ solid
                var good = kept.Empty();
                for (int q = 0; q < good.D.Length; q++)
                    good.D[q] = (byte)(kept.D[q] != 0 || (air.D[q] == 0 && (band == null || band.D[q] != 0)) ? 1 : 0);
                vb = BlockBoxer.Build(kept, air, good, s.Fill);
                if (s.MinBox > 0) vb = vb.Where(b => Math.Max(b.X1 - b.X0, Math.Max(b.Y1 - b.Y0, b.Z1 - b.Z0)) * V >= s.MinBox).ToList();
            }
            else vb = GreedyBoxer.Build(kept);
            var boxes = vb.Select(b => vs.WorldBox(b)).ToList();
            Log($"{(s.BigBlocks ? "big blocks" : "exact greedy")}: {boxes.Count} boxes");

            // ---- 9 post
            if (s.Snap)
            {
                int ns = PostProcess.SnapTops(boxes, vb, vs, s.SnapTolerance); Log($"snapped {ns} box tops to the real model surface");
                PostProcess.SnapFaces(boxes, vb, vs);
            }
            if (s.Prune) { int p = PostProcess.Prune(boxes, vb, walk, headVox, Math.Max(1, (int)Math.Ceiling(s.PruneDistance / V))); Log($"pruned {p} unreachable boxes ({boxes.Count} left)"); }
            if (s.FloorTolerance > 0)
            {
                var (main, set, lips) = PostProcess.FlatFloor(boxes, vb, walk, vs, s.FloorTolerance); R.MainFloor = main;
                Log($"main floor level {main:0.000} m: {set} box tops set to it, {lips} thin lips removed");
            }
            if (s.Seams > 0) { int f = PostProcess.BurySeams(boxes, s.Seams, V); Log($"floor seams overlapped: {f} box sides pushed into neighbours"); }

            R.Boxes = boxes; R.VoxelsKept = (int)kept.Count(); R.Elapsed = sw.Elapsed;
            Log($"boxes {boxes.Count} + edge walls {R.EdgeWalls.Count}, predicted HKX size {HkxWriter.EstimateSize(boxes.Count + R.EdgeWalls.Count)} bytes, {sw.Elapsed.TotalSeconds:0.0} s");
            return R;
        }

        /// <summary>Write the result as an HKX, read it back and check every body (docs/05 1.1). Returns the file bytes.</summary>
        public static byte[] BuildChecked(ConversionResult R, byte[]? template = null)
        {
            var want = R.AllBoxes.ToList();
            var bytes = HkxWriter.Build(want, template ?? HkxTemplate.Bytes);
            var back = HkxReader.Read(bytes);
            if (back.Count != want.Count) throw new InvalidDataException($"read-back: {back.Count} bodies, expected {want.Count}");
            for (int q = 0; q < want.Count; q++)
            {
                var bb = back[q].Bounds;
                for (int a = 0; a < 3; a++)
                    if (Math.Abs(bb.Min[a] - want[q].Min[a]) > 1e-3 || Math.Abs(bb.Max[a] - want[q].Max[a]) > 1e-3)
                        throw new InvalidDataException($"read-back mismatch on body {q}: {bb} vs {want[q]}");
            }
            R.FileSize = bytes.Length;
            return bytes;
        }

        /// <summary>Convenience: MDL file -> HKX file (+ OUT_report.txt). Returns the result.</summary>
        public static ConversionResult ConvertFile(string mdlPath, string hkxPath, string templatePath, ConversionSettings s, Action<string>? progress = null)
        {
            var R = Run(MdlModel.Load(mdlPath), s, progress);
            R.FileSize = HkxWriter.Write(hkxPath, R.AllBoxes.ToList(), File.ReadAllBytes(templatePath));
            // read-back check (docs/05 1.1)
            var back = HkxReader.Read(hkxPath); var want = R.AllBoxes.ToList();
            if (back.Count != want.Count) throw new InvalidDataException($"read-back: {back.Count} bodies, expected {want.Count}");
            for (int q = 0; q < want.Count; q++)
            {
                var bb = back[q].Bounds;
                for (int a = 0; a < 3; a++)
                    if (Math.Abs(bb.Min[a] - want[q].Min[a]) > 1e-3 || Math.Abs(bb.Max[a] - want[q].Max[a]) > 1e-3)
                        throw new InvalidDataException($"read-back mismatch on body {q}: {bb} vs {want[q]}");
            }
            R.Log.Add($"wrote {hkxPath}: {want.Count} bodies, {R.FileSize} bytes (read-back OK)");
            File.WriteAllText(Path.ChangeExtension(hkxPath, null) + "_report.txt", R.ReportText());
            return R;
        }
    }
}

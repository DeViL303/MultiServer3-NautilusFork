// Steps 6-7: standable floor cells, flood fill from the spawn, the keep band, and edge walls at open drops.
using System;
using System.Collections.Generic;

namespace Mdl2Hkx
{
    public static class Reachability
    {
        /// <summary>Solid voxels with hc free voxels directly above them (inside the grid) = places an avatar could stand.</summary>
        public static Grid3 Floors(Grid3 g, int hc)
        {
            var f = g.Empty(); var run = new int[g.Ny + 1];
            for (int i = 0; i < g.Nx; i++)
                for (int k = 0; k < g.Nz; k++)
                {
                    run[g.Ny] = 0;                                      // run[j] = consecutive free voxels starting at j going up
                    for (int j = g.Ny - 1; j >= 0; j--) run[j] = g[i, j, k] ? 0 : run[j + 1] + 1;
                    for (int j = 0; j < g.Ny; j++)
                        if (g[i, j, k] && j + hc < g.Ny && run[j + 1] >= hc) f[i, j, k] = true;
                }
            return f;
        }

        /// <summary>Start cell: nearest floor cell to the spawn (window +-6 columns, any height), else first cell of the largest floor component.</summary>
        public static (int i, int j, int k) StartCell(Grid3 floor, VoxelSpace vs, Vec3? spawn)
        {
            if (spawn.HasValue)
            {
                var (i, j, k) = vs.Cell(spawn.Value); int best = int.MaxValue; (int, int, int) bc = (-1, -1, -1);
                for (int a = Math.Max(i - 6, 0); a < Math.Min(i + 7, floor.Nx); a++)
                    for (int b = 0; b < floor.Ny; b++)
                        for (int c = Math.Max(k - 6, 0); c < Math.Min(k + 7, floor.Nz); c++)
                        {
                            if (!floor[a, b, c]) continue;
                            int dist = Math.Abs(b - j) + Math.Abs(a - i) + Math.Abs(c - k);
                            if (dist < best) { best = dist; bc = (a, b, c); }
                        }
                if (best == int.MaxValue) throw new InvalidOperationException("no standable floor near the spawn point");
                return bc;
            }
            // largest 26-connected component of floor cells
            var seen = new bool[floor.D.Length]; var q = new List<int>(); int bestN = 0, bestFirst = -1;
            for (int s = 0; s < floor.D.Length; s++)
            {
                if (floor.D[s] == 0 || seen[s]) continue;
                q.Clear(); q.Add(s); seen[s] = true; int first = s;
                for (int h = 0; h < q.Count; h++)
                {
                    int id = q[h], i = id / (floor.Ny * floor.Nz), j = id / floor.Nz % floor.Ny, k = id % floor.Nz; first = Math.Min(first, id);
                    for (int a = -1; a <= 1; a++) for (int b = -1; b <= 1; b++) for (int c = -1; c <= 1; c++)
                    { int ii = i + a, jj = j + b, kk = k + c; if (!floor.In(ii, jj, kk)) continue; int n = floor.Idx(ii, jj, kk); if (floor.D[n] != 0 && !seen[n]) { seen[n] = true; q.Add(n); } }
                }
                if (q.Count > bestN) { bestN = q.Count; bestFirst = first; }
            }
            if (bestFirst < 0) throw new InvalidOperationException("model has no standable floor");
            return (bestFirst / (floor.Ny * floor.Nz), bestFirst / floor.Nz % floor.Ny, bestFirst % floor.Nz);
        }

        /// <summary>BFS over floor cells: 4 horizontal neighbours, up/down at most stepVox voxels.</summary>
        public static Grid3 Flood(Grid3 floor, (int i, int j, int k) start, int stepVox)
        {
            stepVox = Math.Max(1, stepVox);
            var seen = floor.Empty(); var q = new Queue<(int, int, int)>(); q.Enqueue(start); seen[start.i, start.j, start.k] = true;
            var dirs = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
            while (q.Count > 0)
            {
                var (i, j, k) = q.Dequeue();
                foreach (var (di, dk) in dirs)
                {
                    int a = i + di, c = k + dk; if (a < 0 || c < 0 || a >= floor.Nx || c >= floor.Nz) continue;
                    for (int dj = -stepVox; dj <= stepVox; dj++)
                    {
                        int b = j + dj; if (b < 0 || b >= floor.Ny) continue;
                        if (floor[a, b, c] && !seen[a, b, c]) { seen[a, b, c] = true; q.Enqueue((a, b, c)); }
                    }
                }
            }
            return seen;
        }

        /// <summary>Keep band: columns from 1 voxel below to reachVox above every reachable cell, dilated horizontally by marginVox.</summary>
        public static Grid3 Band(Grid3 seen, int reachVox, int marginVox)
        {
            var band = seen.Empty();
            for (int i = 0; i < seen.Nx; i++) for (int j = 0; j < seen.Ny; j++) for (int k = 0; k < seen.Nz; k++)
            {
                if (!seen[i, j, k]) continue;
                for (int d = -1; d <= reachVox; d++) { int b = j + d; if (b >= 0 && b < seen.Ny) band[i, b, k] = true; }
            }
            return Morphology.DilateDiskXZ(band, Math.Max(1, marginVox));
        }

        /// <summary>Thin low walls where a flat walkable floor ends at an open drop (step 7).</summary>
        public static List<Box> EdgeWalls(Grid3 kept, Grid3 walk, VoxelSpace vs, ConversionSettings s, int hc)
        {
            int D = (int)Math.Ceiling(s.EdgeDrop / vs.V); double V = vs.V; var lo = vs.Lo;
            bool SolidBetween(int a, int c, int j0, int j1) { j0 = Math.Max(j0, 0); j1 = Math.Min(j1, kept.Ny); for (int b = j0; b < j1; b++) if (kept[a, b, c]) return true; return false; }
            var segs = new Dictionary<(int di, int dk, int plane, int j), List<int>>();
            var dirs = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
            for (int i = 0; i < walk.Nx; i++) for (int j = 0; j < walk.Ny; j++) for (int k = 0; k < walk.Nz; k++)
            {
                if (!walk[i, j, k]) continue;
                foreach (var (di, dk) in dirs)
                {
                    int a = i + di, c = k + dk; if (a < 0 || c < 0 || a >= kept.Nx || c >= kept.Nz) continue;
                    if (SolidBetween(a, c, j - D, j + 1 + hc)) continue;           // floor, step, ledge or wall beside it
                    int b = i - di, e = k - dk;                                       // flat: floor continues level on the inner side
                    if (b < 0 || e < 0 || b >= walk.Nx || e >= walk.Nz) continue;
                    bool flat = false; for (int jj = Math.Max(j - 1, 0); jj < Math.Min(j + 2, walk.Ny); jj++) if (walk[b, jj, e]) flat = true;
                    if (!flat) continue;
                    var key = (di, dk, di != 0 ? i : k, j);
                    if (!segs.TryGetValue(key, out var l)) segs[key] = l = new List<int>();
                    l.Add(di != 0 ? k : i);
                }
            }
            var outp = new List<Box>(); double t = s.EdgeThickness, Hw = s.EdgeWallHeight;
            foreach (var kv in segs)
            {
                var (di, dk, pl, j) = kv.Key; var cells = kv.Value; cells.Sort();
                int s0 = 0;
                for (int q = 1; q <= cells.Count; q++)
                {
                    if (q < cells.Count && cells[q] == cells[q - 1] + 1) continue;
                    int r0 = cells[s0], r1 = cells[q - 1] + 1; double y0 = lo.Y + (j + 1) * V;
                    if (di != 0)
                    {
                        double xe = lo.X + (pl + (di > 0 ? 1 : 0)) * V; double x0 = di > 0 ? xe - t : xe, x1 = di > 0 ? xe : xe + t;
                        outp.Add(new Box(new Vec3(x0, y0, lo.Z + r0 * V), new Vec3(x1, y0 + Hw, lo.Z + r1 * V)));
                    }
                    else
                    {
                        double ze = lo.Z + (pl + (dk > 0 ? 1 : 0)) * V; double z0 = dk > 0 ? ze - t : ze, z1 = dk > 0 ? ze : ze + t;
                        outp.Add(new Box(new Vec3(lo.X + r0 * V, y0, z0), new Vec3(lo.X + r1 * V, y0 + Hw, z1)));
                    }
                    s0 = q;
                }
            }
            return outp;
        }
    }
}

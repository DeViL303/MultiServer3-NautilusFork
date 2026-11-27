// Step 8: turning solid voxels into boxes.
//   GreedyBoxer - exact, lossless (runs along x -> rectangles along y -> boxes along z).
//   BlockBoxer  - lossy big blocks: grow boxes through "good" space (solid or don't-care) while never touching
//                 protected air, then merge pairs and drop redundant boxes. Tiled to bound memory.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mdl2Hkx
{
    public static class GreedyBoxer
    {
        public static List<VBox> Build(Grid3 g)
        {
            // 1. runs along x for every (z, y)
            var runs = new List<(int z, int y, int x0, int x1)>();
            for (int k = 0; k < g.Nz; k++)
                for (int j = 0; j < g.Ny; j++)
                {
                    int i = 0;
                    while (i < g.Nx)
                    {
                        if (!g[i, j, k]) { i++; continue; }
                        int s = i; while (i < g.Nx && g[i, j, k]) i++;
                        runs.Add((k, j, s, i));
                    }
                }
            // 2. merge runs with the same (z, x0, x1) on consecutive y -> rectangles (z, x0, x1, y0, y1)
            var rects = new List<(int z, int x0, int x1, int y0, int y1)>();
            foreach (var grp in runs.GroupBy(r => (r.z, r.x0, r.x1)))
            {
                var ys = grp.Select(r => r.y).OrderBy(y => y).ToList(); int st = 0;
                for (int q = 1; q <= ys.Count; q++)
                    if (q == ys.Count || ys[q] != ys[q - 1] + 1) { rects.Add((grp.Key.z, grp.Key.x0, grp.Key.x1, ys[st], ys[q - 1] + 1)); st = q; }
            }
            // 3. merge rectangles with the same (x0, x1, y0, y1) on consecutive z -> boxes
            var boxes = new List<VBox>();
            foreach (var grp in rects.GroupBy(r => (r.x0, r.x1, r.y0, r.y1)))
            {
                var zs = grp.Select(r => r.z).OrderBy(z => z).ToList(); int st = 0;
                for (int q = 1; q <= zs.Count; q++)
                    if (q == zs.Count || zs[q] != zs[q - 1] + 1)
                    { boxes.Add(new VBox(grp.Key.x0, grp.Key.y0, zs[st], grp.Key.x1, grp.Key.y1, zs[q - 1] + 1)); st = q; }
            }
            return boxes;
        }
    }

    public static class BlockBoxer
    {
        const int Tile = 160;

        /// <param name="solid">voxels that must be covered</param>
        /// <param name="protect">voxels no box may contain (air above walkable floors up to the headroom)</param>
        /// <param name="good">solid + don't-care voxels; a box must stay >= fill good while growing</param>
        public static List<VBox> Build(Grid3 solid, Grid3 protect, Grid3 good, double fill)
        {
            // crop to the solid's bounding box + 2
            int[] mn = { int.MaxValue, int.MaxValue, int.MaxValue }, mx = { -1, -1, -1 };
            for (int i = 0; i < solid.Nx; i++) for (int j = 0; j < solid.Ny; j++) for (int k = 0; k < solid.Nz; k++)
                if (solid[i, j, k]) { mn[0] = Math.Min(mn[0], i); mn[1] = Math.Min(mn[1], j); mn[2] = Math.Min(mn[2], k); mx[0] = Math.Max(mx[0], i); mx[1] = Math.Max(mx[1], j); mx[2] = Math.Max(mx[2], k); }
            var all = new List<VBox>(); if (mx[0] < 0) return all;
            int[] o = new int[3], e = new int[3];
            for (int a = 0; a < 3; a++) { o[a] = Math.Max(0, mn[a] - 2); e[a] = Math.Min(solid.Dim(a), mx[a] + 3); }
            for (int x0 = o[0]; x0 < e[0]; x0 += Tile)
                for (int z0 = o[2]; z0 < e[2]; z0 += Tile)
                {
                    int x1 = Math.Min(x0 + Tile, e[0]), z1 = Math.Min(z0 + Tile, e[2]);
                    var tb = TileBoxes(solid, protect, good, fill, x0, o[1], z0, x1, e[1], z1);
                    foreach (var b in tb) all.Add(b.Offset(x0, o[1], z0));
                }
            JoinAcrossTiles(all);
            return all;
        }

        static List<VBox> TileBoxes(Grid3 solid, Grid3 protect, Grid3 good, double fill, int ox, int oy, int oz, int ex, int ey, int ez)
        {
            int nx = ex - ox, ny = ey - oy, nz = ez - oz;
            var S = new Sat3(good, ox, oy, oz, ex, ey, ez); var P = new Sat3(protect, ox, oy, oz, ex, ey, ez);
            bool any = false; var todo = new bool[nx * ny * nz];
            int L(int i, int j, int k) => (i * ny + j) * nz + k;
            for (int i = 0; i < nx; i++) for (int j = 0; j < ny; j++) for (int k = 0; k < nz; k++)
                if (solid[ox + i, oy + j, oz + k]) { todo[L(i, j, k)] = true; any = true; }
            var B = new List<VBox>(); if (!any) return B;
            int[] dim = { nx, ny, nz };
            bool Ok(VBox slab) { long vol = slab.Volume; return P.Count(slab) == 0 && S.Count(slab) >= fill * vol; }
            // grow: seeds in x-major order; faces tried -x,+x,-y,+y,-z,+z until none moves
            for (int i = 0; i < nx; i++) for (int j = 0; j < ny; j++) for (int k = 0; k < nz; k++)
            {
                if (!todo[L(i, j, k)]) continue;
                var b = new VBox(i, j, k, i + 1, j + 1, k + 1); bool grew = true;
                while (grew)
                {
                    grew = false;
                    for (int ax = 0; ax < 3; ax++)
                        for (int side = 0; side < 2; side++)
                        {
                            var c = b; VBox slab;
                            if (side == 1) { if (c.Hi(ax) >= dim[ax]) continue; c.SetHi(ax, c.Hi(ax) + 1); slab = c; slab.SetLo(ax, c.Hi(ax) - 1); }
                            else { if (c.Lo(ax) <= 0) continue; c.SetLo(ax, c.Lo(ax) - 1); slab = c; slab.SetHi(ax, c.Lo(ax) + 1); }
                            if (Ok(slab)) { b = c; grew = true; }
                        }
                }
                for (int a = b.X0; a < b.X1; a++) for (int q = b.Y0; q < b.Y1; q++) for (int r = b.Z0; r < b.Z1; r++) todo[L(a, q, r)] = false;
                B.Add(b);
            }
            // merge pass: largest first, merge with the partner giving the smallest valid union; repeat until stable
            bool merged = true;
            while (merged && B.Count > 1)
            {
                merged = false; var alive = Enumerable.Repeat(true, B.Count).ToArray();
                var order = Enumerable.Range(0, B.Count).OrderByDescending(q => B[q].Volume).ToList();
                foreach (int i in order)
                {
                    if (!alive[i]) continue; int best = -1; long bestVol = long.MaxValue; VBox bestU = default;
                    for (int c = 0; c < B.Count; c++)
                    {
                        if (c == i || !alive[c]) continue;
                        var U = new VBox(Math.Min(B[i].X0, B[c].X0), Math.Min(B[i].Y0, B[c].Y0), Math.Min(B[i].Z0, B[c].Z0), Math.Max(B[i].X1, B[c].X1), Math.Max(B[i].Y1, B[c].Y1), Math.Max(B[i].Z1, B[c].Z1));
                        long uv = U.Volume;
                        if (uv < bestVol && P.Count(U) == 0 && S.Count(U) >= fill * uv) { best = c; bestVol = uv; bestU = U; }
                    }
                    if (best >= 0) { B[i] = bestU; alive[best] = false; merged = true; }
                }
                B = B.Where((_, q) => alive[q]).ToList();
            }
            // redundancy pass: smallest first, drop a box if all its solid voxels are covered by other boxes too
            var cov = new ushort[nx * ny * nz];
            foreach (var b in B) for (int a = b.X0; a < b.X1; a++) for (int q = b.Y0; q < b.Y1; q++) for (int r = b.Z0; r < b.Z1; r++) cov[L(a, q, r)]++;
            var keep = Enumerable.Repeat(true, B.Count).ToArray();
            foreach (int n in Enumerable.Range(0, B.Count).OrderBy(q => B[q].Volume).ToList())
            {
                var b = B[n]; bool anySolid = false, allTwice = true;
                for (int a = b.X0; a < b.X1 && allTwice; a++) for (int q = b.Y0; q < b.Y1 && allTwice; q++) for (int r = b.Z0; r < b.Z1; r++)
                    if (solid[ox + a, oy + q, oz + r]) { anySolid = true; if (cov[L(a, q, r)] < 2) { allTwice = false; break; } }
                if (!anySolid || allTwice)
                { keep[n] = false; for (int a = b.X0; a < b.X1; a++) for (int q = b.Y0; q < b.Y1; q++) for (int r = b.Z0; r < b.Z1; r++) cov[L(a, q, r)]--; }
            }
            return B.Where((_, q) => keep[q]).ToList();
        }

        /// <summary>Join boxes with identical cross-sections that touch exactly (tile borders); x then z, repeat until stable.</summary>
        static void JoinAcrossTiles(List<VBox> B)
        {
            foreach (int ax in new[] { 0, 2 })
            {
                bool changed = true;
                while (changed)
                {
                    changed = false;
                    var key = new Dictionary<(int, int, int, int, int), List<int>>();
                    (int, int, int, int, int) Key(VBox b, int start)
                    {
                        // cross-section = the two other axes' (lo, hi) + the face coordinate along ax
                        int o1 = ax == 0 ? 1 : 0, o2 = ax == 2 ? 1 : 2;
                        return (b.Lo(o1), b.Lo(o2), b.Hi(o1), b.Hi(o2), start);
                    }
                    for (int n = 0; n < B.Count; n++) { var k = Key(B[n], B[n].Lo(ax)); if (!key.TryGetValue(k, out var l)) key[k] = l = new List<int>(); l.Add(n); }
                    var dead = new HashSet<int>();
                    for (int n = 0; n < B.Count; n++)
                    {
                        if (dead.Contains(n)) continue;
                        if (!key.TryGetValue(Key(B[n], B[n].Hi(ax)), out var l)) continue;
                        foreach (int m in l) if (m != n && !dead.Contains(m)) { var b = B[n]; b.SetHi(ax, B[m].Hi(ax)); B[n] = b; dead.Add(m); changed = true; break; }
                    }
                    if (dead.Count > 0) { var keep = B.Where((_, q) => !dead.Contains(q)).ToList(); B.Clear(); B.AddRange(keep); }
                }
            }
        }
    }
}

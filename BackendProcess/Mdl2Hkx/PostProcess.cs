// Step 9: post-processing of the boxes (docs/03 section 9).
//   9a SnapTops      - top faces onto the real horizontal surfaces, equalise near-equal tops
//   9b SnapFaces     - exposed side faces (and tops) onto the real surfaces facing that axis; never bottoms
//   9c Prune         - drop boxes the avatar can never touch
//   9d FlatFloor     - one exact floor level; remove thin lips lying on a fully covered floor
//   9e BurySeams     - push same-top boxes that only touch into each other
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mdl2Hkx
{
    public static class PostProcess
    {
        // ---------------------------------------------------------------- 9a
        public static int SnapTops(List<Box> boxes, List<VBox> vb, VoxelSpace vs, double snapTol)
        {
            var occ = vs.Occ; int ns = 0; var snapped = new bool[boxes.Count];
            for (int n = 0; n < vb.Count; n++)
            {
                var b = vb[n]; int j = b.Y1 - 1; float best = float.NegativeInfinity; bool hit = false;
                for (int i = b.X0; i < b.X1; i++) for (int k = b.Z0; k < b.Z1; k++)
                    if (vs.Surf.TryGet(1, occ.Idx(i, j, k), out var e)) { hit = true; best = Math.Max(best, e.Max); }
                if (hit && boxes[n].Min.Y + 1e-3 < best && best <= boxes[n].Max.Y)
                { var bx = boxes[n]; bx.Max.Y = best; boxes[n] = bx; ns++; snapped[n] = true; }
            }
            var st = Enumerable.Range(0, boxes.Count).Where(q => snapped[q]).Select(q => boxes[q].Max.Y).ToList();
            for (int n = 0; n < boxes.Count; n++)
            {
                if (snapped[n]) continue;                                   // pure fill: follow a real surface in the same voxel layer
                double t0 = boxes[n].Max.Y, c = double.NegativeInfinity;
                foreach (var t in st) if (t > t0 - vs.V - 1e-4 && t <= t0 + 1e-4) c = Math.Max(c, t);
                if (c > boxes[n].Min.Y + 1e-3) { var bx = boxes[n]; bx.Max.Y = c; boxes[n] = bx; }
            }
            // groups of tops spanning at most snapTol (no chaining) take the group's highest value
            var order = Enumerable.Range(0, boxes.Count).OrderBy(q => boxes[q].Max.Y).ToList(); int g0 = 0;
            var grp = boxes.Select(b => b.Max.Y).ToArray();
            for (int q = 1; q <= order.Count; q++)
                if (q == order.Count || boxes[order[q]].Max.Y - boxes[order[g0]].Max.Y > snapTol)
                { double top = boxes[order[q - 1]].Max.Y; for (int r = g0; r < q; r++) grp[order[r]] = top; g0 = q; }
            for (int n = 0; n < boxes.Count; n++) { var bx = boxes[n]; bx.Max.Y = grp[n]; boxes[n] = bx; }
            return ns;
        }

        // ---------------------------------------------------------------- 9b
        public static void SnapFaces(List<Box> boxes, List<VBox> vb, VoxelSpace vs)
        {
            var occ = vs.Occ; var cov = occ.Empty();                       // voxels covered by any box
            foreach (var b in vb) for (int i = b.X0; i < b.X1; i++) for (int j = b.Y0; j < b.Y1; j++) for (int k = b.Z0; k < b.Z1; k++) cov.D[cov.Idx(i, j, k)] = 1;
            for (int n = 0; n < vb.Count; n++)
            {
                var b = vb[n]; var a = boxes[n].Min; var c = boxes[n].Max;
                foreach (int ax in new[] { 0, 2, 1 })
                    for (int side = 0; side < 2; side++)
                    {
                        if (ax == 1 && side == 0) continue;                  // never move bottoms
                        int layer = side == 1 ? b.Hi(ax) - 1 : b.Lo(ax), outl = side == 1 ? b.Hi(ax) : b.Lo(ax) - 1;
                        if (outl < 0 || outl >= occ.Dim(ax)) continue;
                        // exposed? fraction of the outside layer covered by boxes
                        var so = b; so.SetLo(ax, outl); so.SetHi(ax, outl + 1);
                        if (Fraction(cov, so) > 0.6) continue;
                        // first layer from the face inwards (max 4) holding surfaces that face this axis
                        bool hit = false; float vmin = float.PositiveInfinity, vmax = float.NegativeInfinity;
                        int thick = b.Hi(ax) - b.Lo(ax);
                        for (int step = 0; step < Math.Min(4, thick) && !hit; step++)
                        {
                            int lay = side == 1 ? layer - step : layer + step; var sl = b; sl.SetLo(ax, lay); sl.SetHi(ax, lay + 1);
                            for (int i = sl.X0; i < sl.X1; i++) for (int j = sl.Y0; j < sl.Y1; j++) for (int k = sl.Z0; k < sl.Z1; k++)
                                if (vs.Surf.TryGet(ax, occ.Idx(i, j, k), out var e)) { hit = true; vmin = Math.Min(vmin, e.Min); vmax = Math.Max(vmax, e.Max); }
                        }
                        if (!hit) continue;
                        if (side == 1) { if (a[ax] + 1e-3 < vmax && vmax < c[ax]) c[ax] = vmax; }
                        else { if (a[ax] < vmin && vmin < c[ax] - 1e-3) a[ax] = vmin; }
                    }
                boxes[n] = new Box(a, c);
            }
        }

        static double Fraction(Grid3 g, VBox b)
        {
            long tot = 0, set = 0;
            for (int i = b.X0; i < b.X1; i++) for (int j = b.Y0; j < b.Y1; j++) for (int k = b.Z0; k < b.Z1; k++)
            { tot++; if (g.In(i, j, k) && g.D[g.Idx(i, j, k)] != 0) set++; }
            return tot == 0 ? 0 : (double)set / tot;
        }

        // ---------------------------------------------------------------- 9c
        public static int Prune(List<Box> boxes, List<VBox> vb, Grid3 walk, int headVox, int pruneVox)
        {
            var air = walk.Empty();
            for (int i = 0; i < walk.Nx; i++) for (int j = 0; j < walk.Ny; j++) for (int k = 0; k < walk.Nz; k++)
                if (walk[i, j, k]) for (int d = 0; d <= headVox; d++) air[i, Math.Min(j + d, walk.Ny - 1), k] = true;
            var S = new Sat3(air); int before = boxes.Count;
            var keep = vb.Select(b => S.Count(b.X0 - pruneVox, b.Y0 - pruneVox, b.Z0 - pruneVox, b.X1 + pruneVox, b.Y1 + pruneVox, b.Z1 + pruneVox) > 0).ToArray();
            Filter(boxes, vb, keep);
            return before - boxes.Count;
        }

        static void Filter(List<Box> boxes, List<VBox> vb, bool[] keep)
        {
            var nb = new List<Box>(); var nv = new List<VBox>();
            for (int q = 0; q < boxes.Count; q++) if (keep[q]) { nb.Add(boxes[q]); nv.Add(vb[q]); }
            boxes.Clear(); boxes.AddRange(nb); vb.Clear(); vb.AddRange(nv);
        }

        // ---------------------------------------------------------------- 9d
        public static (double main, int set, int lips) FlatFloor(List<Box> boxes, List<VBox> vb, Grid3 walk, VoxelSpace vs, double tol)
        {
            var occ = vs.Occ; var hist = new Dictionary<double, int>();
            for (int i = 0; i < walk.Nx; i++) for (int j = 0; j < walk.Ny; j++) for (int k = 0; k < walk.Nz; k++)
                if (walk[i, j, k] && vs.Surf.TryGet(1, occ.Idx(i, j, k), out var e))
                { double h = Math.Round(e.Max, 2); hist[h] = hist.TryGetValue(h, out var c) ? c + 1 : 1; }
            if (hist.Count == 0) return (double.NaN, 0, 0);
            double main = hist.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
            int set = 0;
            for (int n = 0; n < boxes.Count; n++)
                if (Math.Abs(boxes[n].Max.Y - main) <= tol && boxes[n].Min.Y < main - 1e-3) { var b = boxes[n]; b.Max.Y = main; boxes[n] = b; set++; }
            // lips lying on the floor, removed only where floor-level boxes cover the whole footprint
            var FL = boxes.Where(b => Math.Abs(b.Max.Y - main) < 1e-4).ToList();
            bool FloorUnder(Box b)
            {
                double st = vs.V / 2;
                for (double x = b.Min.X + st / 2; x < b.Max.X; x += st)
                    for (double z = b.Min.Z + st / 2; z < b.Max.Z; z += st)
                    {
                        bool ok = false;
                        foreach (var f in FL) if (x >= f.Min.X - .01 && x <= f.Max.X + .01 && z >= f.Min.Z - .01 && z <= f.Max.Z + .01) { ok = true; break; }
                        if (!ok) return false;
                    }
                return true;
            }
            var keep = new bool[boxes.Count]; int lips = 0;
            for (int n = 0; n < boxes.Count; n++)
            {
                var b = boxes[n]; bool lip = b.Min.Y >= main - 1e-3 && b.Max.Y > main + 1e-4 && b.Max.Y <= main + tol && FloorUnder(b);
                keep[n] = !lip; if (lip) lips++;
            }
            Filter(boxes, vb, keep);
            return (main, set, lips);
        }

        // ---------------------------------------------------------------- 9e
        public static int BurySeams(List<Box> boxes, double E, double V)
        {
            const double eps = 0.01; int n = boxes.Count, fixedN = 0;
            var L = boxes.Select(b => b.Min).ToArray(); var H = boxes.Select(b => b.Max).ToArray();
            bool Covered(double x0, double x1, double z0, double z1, double top, int skip)
            {
                var m = Enumerable.Range(0, n).Where(q => q != skip && Math.Abs(H[q].Y - top) < 1e-4).ToList();
                if (m.Count == 0) return false;
                double st = V / 2; bool anyPt = false;
                for (double x = x0 + st / 2; x < x1; x += st)
                    for (double z = z0 + st / 2; z < z1; z += st)
                    {
                        anyPt = true; bool ins = false;
                        foreach (var q in m) if (x >= L[q].X - eps && x <= H[q].X + eps && z >= L[q].Z - eps && z <= H[q].Z + eps) { ins = true; break; }
                        if (!ins) return false;
                    }
                return anyPt;
            }
            for (int i = 0; i < n; i++)
            {
                double top = H[i].Y;
                var same = Enumerable.Range(0, n).Where(q => q != i && Math.Abs(H[q].Y - top) < 1e-4).ToList();
                if (same.Count == 0) continue;
                foreach (var (ax, ox) in new[] { (0, 2), (2, 0) })
                    foreach (int sgn in new[] { 1, -1 })
                    {
                        double edge = sgn > 0 ? H[i][ax] : L[i][ax];
                        bool nb = same.Any(q => Math.Abs((sgn > 0 ? L[q][ax] : H[q][ax]) - edge) < eps && L[q][ox] < H[i][ox] - eps && H[q][ox] > L[i][ox] + eps);
                        if (!nb) continue;
                        foreach (double d in new[] { E, E / 2, E / 4, V })
                        {
                            double a0 = sgn > 0 ? edge : edge - d, a1 = sgn > 0 ? edge + d : edge;
                            bool cov = ax == 0 ? Covered(a0, a1, L[i].Z, H[i].Z, top, i) : Covered(L[i].X, H[i].X, a0, a1, top, i);
                            if (cov) { if (sgn > 0) H[i][ax] = a1; else L[i][ax] = a0; fixedN++; break; }
                        }
                    }
            }
            for (int q = 0; q < n; q++) boxes[q] = new Box(L[q], H[q]);
            return fixedN;
        }
    }
}

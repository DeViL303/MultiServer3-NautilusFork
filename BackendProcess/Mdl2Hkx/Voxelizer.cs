// Step 3: voxelise triangle surfaces by dense point sampling, and build the per-axis surface index
// (where the real surfaces facing each axis sit inside each voxel) used later to snap boxes onto the model.
using System;
using System.Collections.Generic;

namespace Mdl2Hkx
{
    /// <summary>Per axis (0=x, 1=y, 2=z): voxel index -> (min, max) coordinate along that axis of samples from triangles
    /// whose normal has |n_axis| > 0.3. Axis 1 max = top of floors / horizontal surfaces.</summary>
    public sealed class SurfaceIndex
    {
        public readonly Dictionary<int, (float Min, float Max)>[] Axis = { new(), new(), new() };
        public void Add(int axis, int voxel, double v)
        {
            var d = Axis[axis]; float f = (float)v;
            d[voxel] = d.TryGetValue(voxel, out var e) ? (Math.Min(e.Min, f), Math.Max(e.Max, f)) : (f, f);
        }
        public bool TryGet(int axis, int voxel, out (float Min, float Max) e) => Axis[axis].TryGetValue(voxel, out e);
    }

    public sealed class VoxelSpace
    {
        public Vec3 Lo; public double V; public Grid3 Occ = null!; public SurfaceIndex Surf = new();
        public Vec3 World(int i, int j, int k) => new Vec3(Lo.X + i * V, Lo.Y + j * V, Lo.Z + k * V);
        public Box WorldBox(VBox b) => new Box(World(b.X0, b.Y0, b.Z0), World(b.X1, b.Y1, b.Z1));
        public (int i, int j, int k) Cell(Vec3 p) => ((int)Math.Floor((p.X - Lo.X) / V), (int)Math.Floor((p.Y - Lo.Y) / V), (int)Math.Floor((p.Z - Lo.Z) / V));
    }

    public static class Voxelizer
    {
        public static VoxelSpace Build(IList<Tri> tris, double V)
        {
            if (tris.Count == 0) throw new ArgumentException("no triangles to voxelise");
            var lo = new Vec3(1e30, 1e30, 1e30); var hi = new Vec3(-1e30, -1e30, -1e30);
            foreach (var t in tris) foreach (var p in new[] { t.A, t.B, t.C }) for (int a = 0; a < 3; a++) { lo[a] = Math.Min(lo[a], p[a]); hi[a] = Math.Max(hi[a], p[a]); }
            lo = lo - new Vec3(3 * V, 3 * V, 3 * V); hi = hi + new Vec3(3 * V, 3 * V, 3 * V);
            int nx = (int)Math.Ceiling((hi.X - lo.X) / V), ny = (int)Math.Ceiling((hi.Y - lo.Y) / V), nz = (int)Math.Ceiling((hi.Z - lo.Z) / V);
            var vs = new VoxelSpace { Lo = lo, V = V, Occ = new Grid3(nx, ny, nz) };
            var occ = vs.Occ;
            foreach (var t in tris)
            {
                Vec3 e1 = t.B - t.A, e2 = t.C - t.A, e3 = t.C - t.B;
                double L = Math.Max(e1.Length, Math.Max(e2.Length, e3.Length));
                int k = (int)Math.Ceiling(L / (V * 0.45)) + 1;                    // sample spacing < 0.45 voxel
                var nrm = Vec3.Cross(e1, e2); double nl = nrm.Length + 1e-12;
                bool fx = Math.Abs(nrm.X) / nl > 0.3, fy = Math.Abs(nrm.Y) / nl > 0.3, fz = Math.Abs(nrm.Z) / nl > 0.3;
                for (int iu = 0; iu <= k; iu++)
                    for (int iw = 0; iu + iw <= k; iw++)
                    {
                        double u = (double)iu / k, w = (double)iw / k;
                        var P = t.A + e1 * u + e2 * w;
                        int i = (int)Math.Floor((P.X - lo.X) / V), j = (int)Math.Floor((P.Y - lo.Y) / V), kk = (int)Math.Floor((P.Z - lo.Z) / V);
                        if (!occ.In(i, j, kk)) continue;
                        int id = occ.Idx(i, j, kk); occ.D[id] = 1;
                        if (fx) vs.Surf.Add(0, id, P.X);
                        if (fy) vs.Surf.Add(1, id, P.Y);
                        if (fz) vs.Surf.Add(2, id, P.Z);
                    }
            }
            return vs;
        }
    }

    public static class Morphology
    {
        /// <summary>Number of set voxels in the (2r+1)^3 cube around every voxel (zero outside the grid). Separable prefix sums.</summary>
        public static int[] CubeCount(Grid3 g, int r)
        {
            int nx = g.Nx, ny = g.Ny, nz = g.Nz; var a = new int[g.D.Length]; var b = new int[g.D.Length];
            for (int i = 0; i < a.Length; i++) a[i] = g.D[i] != 0 ? 1 : 0;
            // along z
            Window(a, b, nx * ny, nz, 1, nz, r);
            // along y: lines (i, k) with stride nz
            for (int i = 0; i < nx; i++) WindowStrided(b, a, i * ny * nz, nz, ny, nz, r);
            // along x: lines (j, k) with stride ny*nz
            Array.Clear(b);
            WindowStrided(a, b, 0, ny * nz, nx, ny * nz, r);
            return b;
        }
        // lines are contiguous blocks of length len (count lines); window sum radius r
        static void Window(int[] src, int[] dst, int lines, int len, int stride, int lineStep, int r)
        {
            var pre = new int[len + 1];
            for (int l = 0; l < lines; l++)
            {
                int o = l * lineStep; pre[0] = 0;
                for (int t = 0; t < len; t++) pre[t + 1] = pre[t] + src[o + t * stride];
                for (int t = 0; t < len; t++) dst[o + t * stride] = pre[Math.Min(len, t + r + 1)] - pre[Math.Max(0, t - r)];
            }
        }
        // lines start at base + q (q = 0..lineCount-1), elements spaced by stride, len elements
        static void WindowStrided(int[] src, int[] dst, int bas, int lineCount, int len, int stride, int r)
        {
            var pre = new int[len + 1];
            for (int q = 0; q < lineCount; q++)
            {
                int o = bas + q; pre[0] = 0;
                for (int t = 0; t < len; t++) pre[t + 1] = pre[t] + src[o + t * stride];
                for (int t = 0; t < len; t++) dst[o + t * stride] = pre[Math.Min(len, t + r + 1)] - pre[Math.Max(0, t - r)];
            }
        }

        static readonly (int, int, int)[] N6 = { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };

        /// <summary>One 6-neighbour dilation; if mask != null the result is AND-ed with it.</summary>
        public static Grid3 Dilate6(Grid3 g, Grid3? mask = null)
        {
            var o = g.Clone();
            for (int i = 0; i < g.Nx; i++) for (int j = 0; j < g.Ny; j++) for (int k = 0; k < g.Nz; k++)
            {
                if (!g[i, j, k]) continue;
                foreach (var (di, dj, dk) in N6) { int a = i + di, b = j + dj, c = k + dk; if (g.In(a, b, c)) o.D[o.Idx(a, b, c)] = 1; }
            }
            if (mask != null) for (int q = 0; q < o.D.Length; q++) o.D[q] &= mask.D[q];
            return o;
        }

        /// <summary>One 6-neighbour erosion with outside = empty (scipy border_value=0).</summary>
        public static Grid3 Erode6(Grid3 g)
        {
            var o = g.Empty();
            for (int i = 0; i < g.Nx; i++) for (int j = 0; j < g.Ny; j++) for (int k = 0; k < g.Nz; k++)
            {
                if (!g[i, j, k]) continue; bool keep = true;
                foreach (var (di, dj, dk) in N6) { int a = i + di, b = j + dj, c = k + dk; if (!g.In(a, b, c) || !g[a, b, c]) { keep = false; break; } }
                if (keep) o[i, j, k] = true;
            }
            return o;
        }

        /// <summary>Closing (dilate k, erode k) OR-ed with the original: fuses both faces of thin walls.</summary>
        public static Grid3 CloseOr(Grid3 g, int k)
        {
            var c = g; for (int t = 0; t < k; t++) c = Dilate6(c); for (int t = 0; t < k; t++) c = Erode6(c);
            for (int q = 0; q < c.D.Length; q++) c.D[q] |= g.D[q];
            return c;
        }

        /// <summary>Horizontal disk dilation (radius m voxels in x/z), used for the reach band margin.</summary>
        public static Grid3 DilateDiskXZ(Grid3 g, int m)
        {
            var o = g.Empty(); var off = new List<(int, int)>();
            for (int a = -m; a <= m; a++) for (int c = -m; c <= m; c++) if (a * a + c * c <= m * m) off.Add((a, c));
            for (int i = 0; i < g.Nx; i++) for (int j = 0; j < g.Ny; j++) for (int k = 0; k < g.Nz; k++)
            {
                if (!g[i, j, k]) continue;
                foreach (var (a, c) in off) { int x = i + a, z = k + c; if (x >= 0 && z >= 0 && x < g.Nx && z < g.Nz) o.D[o.Idx(x, j, z)] = 1; }
            }
            return o;
        }

        /// <summary>Remove 26-connected pieces whose bounding box's largest side is below minExtent voxels. Returns count removed.</summary>
        public static int RemoveSmallPieces(Grid3 g, double minExtentVox)
        {
            var seen = new bool[g.D.Length]; var q = new List<int>(); int removed = 0;
            for (int s = 0; s < g.D.Length; s++)
            {
                if (g.D[s] == 0 || seen[s]) continue;
                q.Clear(); q.Add(s); seen[s] = true;
                int x0 = int.MaxValue, y0 = int.MaxValue, z0 = int.MaxValue, x1 = -1, y1 = -1, z1 = -1;
                for (int h = 0; h < q.Count; h++)
                {
                    int id = q[h], i = id / (g.Ny * g.Nz), j = id / g.Nz % g.Ny, k = id % g.Nz;
                    x0 = Math.Min(x0, i); x1 = Math.Max(x1, i); y0 = Math.Min(y0, j); y1 = Math.Max(y1, j); z0 = Math.Min(z0, k); z1 = Math.Max(z1, k);
                    for (int a = -1; a <= 1; a++) for (int b = -1; b <= 1; b++) for (int c = -1; c <= 1; c++)
                    {
                        int ii = i + a, jj = j + b, kk = k + c; if (!g.In(ii, jj, kk)) continue;
                        int n = g.Idx(ii, jj, kk); if (g.D[n] != 0 && !seen[n]) { seen[n] = true; q.Add(n); }
                    }
                }
                int ext = Math.Max(x1 - x0, Math.Max(y1 - y0, z1 - z0)) + 1;
                if (ext < minExtentVox) { foreach (var id in q) g.D[id] = 0; removed++; }
            }
            return removed;
        }
    }
}

// Walk test on a collision HKX alone (docs/05 section 1.2). Port of python_reference/hkxwalk.py.
// Avatar: radius 0.25 m, height 2.3 m (use >= 2.3: 1.7 m misses ceilings that really trap the avatar), step 0.10 m.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mdl2Hkx
{
    public sealed class WalkResult
    {
        public double AreaM2; public int HeightLevels, FallOffPoints, LeftTestArea;
        public bool[,] ReachTopDown = new bool[0, 0];     // [x, z] for drawing a map (+x right, +z DOWN the page)
        public bool[,] SolidTopDown = new bool[0, 0];
        public Vec3 Lo; public double V;
        public override string ToString() => $"reachable floor area {AreaM2:0.0} m2, height levels {HeightLevels}, fall-off points {FallOffPoints}" +
                                             (LeftTestArea > 0 ? $" (walked out of the test area {LeftTestArea} times: raise margin)" : "");
    }

    public static class WalkTest
    {
        public static WalkResult Run(IList<HkxBody> bodies, Vec3 spawn, double V = 0.05, double radius = 0.25, double height = 2.3,
                                     double step = 0.10, double fall = 1.0, double margin = 35.0)
        {
            // grid bounds: bodies' bounds clipped to spawn +- margin (and +-15 m vertically)
            var lo = new Vec3(1e30, 1e30, 1e30); var hi = new Vec3(-1e30, -1e30, -1e30);
            foreach (var b in bodies) foreach (var v in b.Verts) for (int a = 0; a < 3; a++) { lo[a] = Math.Min(lo[a], v[a]); hi[a] = Math.Max(hi[a], v[a]); }
            for (int a = 0; a < 3; a++) { double m = a == 1 ? 15 : margin; lo[a] = Math.Max(lo[a] - 1, spawn[a] - m); hi[a] = Math.Min(hi[a] + 1, spawn[a] + m); }
            int nx = (int)Math.Ceiling((hi.X - lo.X) / V), ny = (int)Math.Ceiling((hi.Y - lo.Y) / V), nz = (int)Math.Ceiling((hi.Z - lo.Z) / V);
            var occ = new Grid3(nx, ny, nz);
            (int, int, int) Cell(Vec3 p) => ((int)Math.Floor((p.X - lo.X) / V), (int)Math.Floor((p.Y - lo.Y) / V), (int)Math.Floor((p.Z - lo.Z) / V));
            foreach (var b in bodies)
            {
                if (b.Kind == "mesh")
                {
                    foreach (var (i0, i1, i2) in b.Tris)
                    {
                        Vec3 p0 = b.Verts[i0], e1 = b.Verts[i1] - p0, e2 = b.Verts[i2] - p0;
                        int k = (int)Math.Ceiling(Math.Max(e1.Length, Math.Max(e2.Length, (e2 - e1).Length)) / (V * .45)) + 1;
                        for (int iu = 0; iu <= k; iu++) for (int iw = 0; iu + iw <= k; iw++)
                        { var (x, y, z) = Cell(p0 + e1 * ((double)iu / k) + e2 * ((double)iw / k)); if (occ.In(x, y, z)) occ[x, y, z] = true; }
                    }
                    continue;
                }
                // axis-aligned boxes (and anything thinner than a voxel): fill the voxel range [floor(min), ceil(max));
                // rotated hulls: test voxel centres against the hull's planes
                var bb = b.Bounds;
                int x0 = Math.Max(0, (int)Math.Floor((bb.Min.X - lo.X) / V)), x1 = Math.Min(nx, (int)Math.Ceiling((bb.Max.X - lo.X) / V));
                int y0 = Math.Max(0, (int)Math.Floor((bb.Min.Y - lo.Y) / V)), y1 = Math.Min(ny, (int)Math.Ceiling((bb.Max.Y - lo.Y) / V));
                int z0 = Math.Max(0, (int)Math.Floor((bb.Min.Z - lo.Z) / V)), z1 = Math.Min(nz, (int)Math.Ceiling((bb.Max.Z - lo.Z) / V));
                bool axisBox = b.Verts.Length == 8 && Enumerable.Range(0, 3).All(a => b.Verts.Select(v => Math.Round(v[a], 4)).Distinct().Count() == 2);
                bool thin = bb.Max.X - bb.Min.X < V || bb.Max.Y - bb.Min.Y < V || bb.Max.Z - bb.Min.Z < V;
                for (int i = x0; i < x1; i++)
                    for (int j = y0; j < y1; j++)
                        for (int k = z0; k < z1; k++)
                            if (axisBox || thin || b.Contains(new Vec3(lo.X + (i + .5) * V, lo.Y + (j + .5) * V, lo.Z + (k + .5) * V))) occ[i, j, k] = true;
            }
            // body clearance: collision grown by the radius horizontally
            int Rr = (int)Math.Round(radius / V); var dil = Morphology.DilateDiskXZ(occ, Rr);
            int H = (int)Math.Round(height / V), ST = Math.Max(1, (int)Math.Round(step / V)), FALL = (int)(fall / V);
            bool Free(int i, int j0, int j1, int k) { for (int j = Math.Max(j0, 0); j < Math.Min(j1, ny); j++) if (dil[i, j, k]) return false; return true; }
            bool Walkable(int i, int j, int k) => j >= 1 && j < ny && occ[i, j - 1, k] && !occ[i, j, k] && Free(i, j + ST + 1, j + H, k);
            // start: nearest walkable cell to the spawn
            var (si, sj, sk) = Cell(spawn); (int, int, int) start = (-1, -1, -1); int best = int.MaxValue;
            for (int i = Math.Max(si - 8, 0); i < Math.Min(si + 9, nx); i++) for (int j = 0; j < ny; j++) for (int k = Math.Max(sk - 8, 0); k < Math.Min(sk + 9, nz); k++)
                if (Walkable(i, j, k)) { int dd = 2 * Math.Abs(j - sj) + Math.Abs(i - si) + Math.Abs(k - sk); if (dd < best) { best = dd; start = (i, j, k); } }
            var res = new WalkResult { Lo = lo, V = V };
            if (best == int.MaxValue) return res;
            var seen = new HashSet<(int, int, int)> { start }; var q = new Queue<(int, int, int)>(); q.Enqueue(start); var falls = new HashSet<(int, int)>();
            var dirs = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
            while (q.Count > 0)
            {
                var (i, j, k) = q.Dequeue();
                foreach (var (di, dk) in dirs)
                {
                    int a = i + di, c = k + dk; if (a < 0 || c < 0 || a >= nx || c >= nz) { res.LeftTestArea++; continue; }
                    bool moved = false;
                    for (int dj = ST; dj >= -FALL; dj--)                       // step up a little, or step / drop down
                    {
                        int b = j + dj; if (b < 0 || b >= ny || !Walkable(a, b, c)) continue;
                        moved = true; if (seen.Add((a, b, c))) q.Enqueue((a, b, c)); break;
                    }
                    if (!moved)
                    {
                        bool landing = false; for (int b = Math.Max(j - FALL, 0); b < j; b++) if (occ[a, b, c]) { landing = true; break; }
                        if (!landing && Free(a, j, j + H, c)) falls.Add((a, c));
                    }
                }
            }
            res.ReachTopDown = new bool[nx, nz]; res.SolidTopDown = new bool[nx, nz];
            foreach (var (i, _, k) in seen) res.ReachTopDown[i, k] = true;
            for (int i = 0; i < nx; i++) for (int k = 0; k < nz; k++) for (int j = 0; j < ny; j++) if (occ[i, j, k]) { res.SolidTopDown[i, k] = true; break; }
            int cells = 0; foreach (var x in res.ReachTopDown) if (x) cells++;
            res.AreaM2 = cells * V * V; res.HeightLevels = seen.Select(s => s.Item2).Distinct().Count(); res.FallOffPoints = falls.Count;
            return res;
        }
    }
}

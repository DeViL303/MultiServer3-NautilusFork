# 06 — The C# reference port

`csharp/` holds a straight port of the Python reference (v0.5) as a class library plus a command-line front end.

> **Status: written without a C# compiler available, so it has not been compiled or run yet.** It was reviewed line by
> line against the Python that produced the in-game-verified file. Expect a few small compile fixes. Before relying on
> it, run the checks in §5. The Python reference in `python_reference/` is the ground truth.

## 1. Projects

```
csharp/
  Mdl2Hkx.Core/          class library (net8.0, no dependencies)
    Common.cs            big-endian IO, HomeHash (~crc32), Vec3, Box, VBox, Grid3, Sat3 (summed-volume table)
    MdlReader.cs         MdlModel / MdlMesh / MdlMaterial: positions, triangles (list/strip), materials, textures
    Hkx.cs               HkxWriter (template-based), HkxReader (convex + MOPP meshes, rotations, planes)
    Settings.cs          ConversionSettings (every option) + Preset(1..5)
    Voxelizer.cs         step 3 (+ SurfaceIndex) and Morphology (box filter, dilate/erode, disk, small pieces)
    Reachability.cs      steps 6-7: floors, start cell, flood fill, band, edge walls
    Boxers.cs            step 8: GreedyBoxer (exact), BlockBoxer (big blocks, tiled)
    PostProcess.cs       step 9: SnapTops, SnapFaces, Prune, FlatFloor (+lips), BurySeams
    Converter.cs         the whole pipeline, report, ConvertFile() with read-back check
    WalkTest.cs          walk test on any HKX (incl. Sony's rotated hulls and meshes)
  Mdl2Hkx.Cli/           mdl2hkx.exe — same options as the Python script + --complexity
```

Build:

```
cd csharp
dotnet new sln -n Mdl2Hkx
dotnet sln add Mdl2Hkx.Core/Mdl2Hkx.Core.csproj Mdl2Hkx.Cli/Mdl2Hkx.Cli.csproj
dotnet build -c Release
dotnet run --project Mdl2Hkx.Cli -c Release -- BASIC_APARTMENT.MDL OUT.HKX --template ../template/HKX_TEMPLATE.HKX --complexity 2 --spawn=-0.0033,3.0816,-10.0595 --compare BASIC_APARTMENT.HKX
```

## 2. Using the library from an app

```csharp
var mdl = MdlModel.Load(mdlPath);
var s = ConversionSettings.Preset(complexitySlider);        // 1..5
s.Spawn = new Vec3(-0.0033, 3.0816, -10.0595);              // read from the .SCENE (see §4)
s.SkipMeshes = userUncheckedMeshes;                         // from the report table
var result = Converter.Run(mdl, s, msg => statusBar.Text = msg);   // run on a worker thread
// show result.Boxes / result.EdgeWalls in a 3-D view, result.Meshes in a table, result.Log in a console
HkxWriter.Write(outPath, result.AllBoxes.ToList(), File.ReadAllBytes(templatePath));
var walk = WalkTest.Run(HkxReader.Read(outPath), s.Spawn.Value);  // area, fall-offs, top-down map
```

`Converter.ConvertFile(...)` does run + write + read-back verification + report file in one call.

Live estimates for the settings UI before running:
* voxel count = `Π ceil(size_a / V + 6)` → warn above ~150 M;
* file size = `HkxWriter.EstimateSize(boxCount)` after a run.

## 3. Python → C# mapping

| Python (`mdl2hkx.py`) | C# |
|---|---|
| `mdl_dump.js` + HMCore | `MdlModel.Read`, `MdlMesh.Positions/Triangles` |
| material filter loop | `Converter.Run` §1-2 |
| voxelise + `AX_f/AX_mx/AX_mn` | `Voxelizer.Build` → `VoxelSpace.Occ`, `SurfaceIndex` |
| `uniform_filter` count, seed, grow, `label` | `Morphology.CubeCount`, `Dilate6(mask)`, `RemoveSmallPieces` |
| `binary_closing` | `Morphology.CloseOr` |
| `floors`, `start_cell`, `flood`, band | `Reachability.Floors/StartCell/Flood/Band` |
| edge walls | `Reachability.EdgeWalls` |
| `greedy` | `GreedyBoxer.Build` |
| `blocks` / `_blocks` | `BlockBoxer.Build` / `TileBoxes` |
| `surf_top` + grouping | `PostProcess.SnapTops` |
| `face_snap` (coverage grid `covg`) | `PostProcess.SnapFaces` |
| prune | `PostProcess.Prune` |
| floor-tol + lips | `PostProcess.FlatFloor` |
| seams | `PostProcess.BurySeams` |
| `hkxwrite.write_hkx` | `HkxWriter.Build/Write` |
| `readhkx2.read_hkx` | `HkxReader.Read` |
| `hkxwalk.py` | `WalkTest.Run` |
| `seamcheck.py`, `facecheck.py` | not ported (easy: see docs/05) |
| `render_overlay.py` | not ported — use your own 3-D view (Helix Toolkit, MonoGame, OpenTK…) |

Known differences:
* The writer emits planes as +0.0 where qhull gives −0.0 → files are equal in value, not byte-for-byte.
* Tie-breaks in the blocks merge pass use a stable sort (numpy's is not), so box lists can differ slightly in order and,
  rarely, shape. Walk area / fall-offs / seams should match.
* `Grid3` uses one byte per voxel and the morphology functions work on the whole grid (no slabs). Fine for rooms;
  for big outdoor scenes add the slab / tile processing the Python uses (`slabs()` helper) or switch to bit-packed grids.

## 4. Getting the spawn point from the `.SCENE`

The scene is XML. Find `<gap:gameObject xsi:type="spawnPointType">` and read its `<gap:translate>x y z</gap:translate>`.
Basic Apartment: `-0.00333935 3.08163738 -10.0595016`. Y is up. The collision asset path is the `<gap:asset name="collision">`
source; the environment model is `<gap:asset name="environment">`.

## 5. First-run checklist for the port

1. `HkxReader.Read(original BASIC_APARTMENT.HKX)` → 46 convex + 2 mesh bodies; bounds x −11.03…7.44, y −0.26…9.27,
   z −13.82…15.56.
2. `HkxWriter.Build` on 1 box, then read back → same box. Compare with the Python writer's file for the same box:
   identical except possible ±0.0 plane components.
3. `MdlModel.Load(BASIC_APARTMENT.MDL)` → 70 meshes, 18,601 triangles in total; bounds x −7.8…11.39,
   y 1.47…10.29, z −14.69…12.63.
4. `Converter.Run` with `Preset(2)` and the apartment spawn → about 77 boxes, main floor 3.000 m; walk test ≈ 207 m²,
   0 fall-offs (compare with `examples/`).
5. Then presets 1, 3, 4 → numbers close to docs/04 §1.

## 6. Performance notes

* Voxeliser and `CubeCount` are embarrassingly parallel per triangle / per line: `Parallel.For` over triangles needs a
  lock-free `occ` write (bytes, idempotent) and a concurrent or per-thread `SurfaceIndex` merged at the end.
* `Dilate6` / `DilateDiskXZ` allocate a new grid each call; reuse buffers if memory matters.
* The blocks merge pass is O(n²) per round per tile; n is small (tens to a few hundred per tile).
* `PostProcess.BurySeams` and `FlatFloor` are O(n²) in boxes; fine for blocks mode, a second or two for exact mode
  with thousands of boxes (bucket boxes by top height if it matters).

## 7. Extension points the app will probably want

* **Stairs** (docs/05 §4): detect riser runs between walk levels and insert 3 sub-steps per riser.
* **Curved walls**: emit rotated boxes / hulls (`HkxWriter` would need general hulls: vertices + planes from a convex
  hull; the format already supports it — see 01 §6.5–6.7).
* **Per-mesh overrides**: force "solid block" for a mesh's bounding box (furniture), or "never collide".
* **Manual boxes**: let the user add / delete / move boxes in the 3-D view before writing.

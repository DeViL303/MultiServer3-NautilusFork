# mdl2hkx — handover package

Automatic collision (`.HKX`) for PlayStation Home spaces, generated from the space's model (`.MDL`).
Box-based, reachability-aware, with complexity presets. Built and tested on the Basic Apartment; the in-game
verified result is `examples/BASIC_APARTMENT_preset2_simple.HKX` (77 boxes, 72 KB).

This package is meant to let you build the converter into a C# app.

## What's inside

| Folder / file | What it is |
|---|---|
| `docs/01_HKX_FORMAT.md` | The Home HKX (Havok 5 PS3 packfile) format, byte by byte: header, sections, fixups, rigid body, convex shape, how to read Sony's files (rotations, MOPP meshes) |
| `docs/02_MDL_FORMAT.md` | The parts of the `.MDL` format the converter needs: meshes, vertex fields, strips, materials, textures, name hashes |
| `docs/03_CONVERSION_PIPELINE.md` | The algorithm, step by step, with the reason for every rule |
| `docs/04_SETTINGS_AND_PRESETS.md` | Every setting, the 5 complexity presets with measured box counts, sizes, times, fit; memory guidance; UI ideas |
| `docs/05_VALIDATION_AND_LESSONS.md` | Walk test, seam check, wall-gap check; every bug we hit in game and the rule it produced; avatar facts |
| `docs/06_CSHARP_PORT.md` | How the C# code is organised, how to use it from an app, Python→C# map, first-run checklist |
| `csharp/` | C# class library `Mdl2Hkx.Core` + command line `Mdl2Hkx.Cli` (net8.0, no dependencies) — **not compiled yet**, see docs/06 |
| `python_reference/` | The working Python implementation (ground truth): `mdl2hkx.py` + helpers + checks + overlay renderer |
| `template/HKX_TEMPLATE.HKX` | One-body HKX exported by the HDK, used as the layout template by both writers; plus an annotated hex dump |
| `examples/` | Basic Apartment preset 2 output (HKX, report, overlay, walk comparison) and the logs of all 5 presets |

## The idea in one paragraph

Voxelise the model's surfaces, drop what should not collide (glows, foliage, sticky-out bits), flood-fill the floors an
avatar can walk to from the spawn point and keep only collision near them, then cover what is left with axis-aligned
boxes — either exactly, or as a few big blocks that may swallow unreachable space but never the air you can stand in.
Finally snap every box face back onto the real model surface, make the floor one exact height, bury seams between
floor boxes, and write one Havok rigid body per box.

## Quick start (Python reference)

```
# needs python3 + numpy + scipy (+ pillow for pictures), node, and the kit's mdlb/src/00.js (HMCore)
export MDLB_CORE=/path/to/kit/mdlb/src/00.js
python3 python_reference/mdl2hkx.py BASIC_APARTMENT.MDL OUT.HKX --spawn=-0.0033,3.0816,-10.0595 \
        --voxel .08 --blocks --fill .70 --compare BASIC_APARTMENT.HKX --no-render
python3 python_reference/hkxwalk.py OUT.HKX --spawn=-0.0033,3.0816,-10.0595 --height 2.3
```

The overlay pictures (`--no-render` removed) need headless Chrome and the small WebGL viewer in `python_reference/viewer/`.

## Quick start (C#)

```
cd csharp && dotnet new sln -n Mdl2Hkx && dotnet sln add */*.csproj && dotnet build -c Release
dotnet run --project Mdl2Hkx.Cli -c Release -- BASIC_APARTMENT.MDL OUT.HKX --template ../template/HKX_TEMPLATE.HKX \
        --complexity 2 --spawn=-0.0033,3.0816,-10.0595 --compare BASIC_APARTMENT.HKX
```

## Complexity presets (Basic Apartment)

| # | Name | Boxes | File | Walk area | Status |
|---|---|---|---|---|---|
| 1 | Lowest | 68 | 65 KB | 208.6 m² | offline checks pass |
| 2 | **Simple (default)** | 77 | 73 KB | 207.5 m² | **verified in Home** |
| 3 | Balanced | 110 | 103 KB | 207.1 m² | offline checks pass |
| 4 | Detailed (exact 5 cm) | 1,027 | 936 KB | 209.0 m² | offline checks pass |
| 5 | Exact (3 cm) | 3,016 | 2.7 MB | 207.7 m² | 16 short floor seams remain |
| — | Original Sony file | 46 + 2 meshes | 49 KB | 207.6 m² | — |

## Known gaps

Stairs (needs sub-steps per riser), curved walls (axis-aligned boxes stand off curves a little), merging many boxes into
one body to shrink files, bridge boxes for leftover seams in exact mode. See docs/05 §4 and docs/06 §7.

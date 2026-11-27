# 04 — Settings, complexity presets and what they cost

## 1. Measured presets (Basic Apartment, 18,601 triangles, spawn −0.0033, 3.0816, −10.0595)

All runs with the Python reference v0.5. "Walk area" and "fall-offs" from `hkxwalk.py` with a 2.3 m tall avatar, 0.25 m
radius, 10 cm step. "Wall gap" is the median distance from box side faces to the nearest model surface (lower = closer
fit; it includes outer faces, so treat it as a rough fit indicator).

| # | Preset | Mode | Voxel | Fill | Boxes | File | Time* | Walk area | Fall-offs | Exposed seams | Wall gap |
|---|---|---|---|---|---|---|---|---|---|---|---|
| — | **Original Sony HKX** | hand made | — | — | 46 hulls + 2 meshes | 49 KB | — | 207.6 m² | 0 | — | 6.1 cm |
| 1 | Lowest | blocks | 0.10 | 0.55 | 68 | 65 KB | 3 s | 208.6 m² | 0 | 0 | 6.5 cm |
| 2 | **Simple** (tested in game) | blocks | 0.08 | 0.70 | 77 | 73 KB | 6 s | 207.5 m² | 0 | 0 | 7.4 cm |
| 3 | Balanced | blocks | 0.06 | 0.80 | 110 | 103 KB | 12 s | 207.1 m² | 0 | 0 | 6.5 cm |
| 4 | Detailed | exact | 0.05 | — | 1,027 | 936 KB | 21 s | 209.0 m² | 0 | 0 | 0.9 cm |
| 5 | Exact | exact | 0.03 | — | 3,016 | 2.7 MB | 138 s | 207.7 m² | 0 | 16 | 0.8 cm |

\* single-threaded numpy on a cloud VM; a C# port with `Parallel.For` on the box filter and voxeliser should be faster.

**Verified in Home:** preset 2 (the v4 file: 77 boxes) — walk anywhere, onto the balcony, close to walls, no sticking.
Presets 1, 3, 4, 5 pass every offline check but have not been loaded in game yet.

Recommendation for the app's default: **preset 2**. Offer 1 for "smallest file", 3 for a slightly tighter fit, and
4/5 as "advanced" (big files; exact fit; good for small objects or when blocks look too coarse).

### Suggested complexity slider

| Slider | Preset |
|---|---|
| 1 | Lowest — fewest, biggest blocks |
| 2 | Simple — default |
| 3 | Balanced |
| 4 | Detailed — exact boxes, 5 cm |
| 5 | Exact — exact boxes, 3 cm (slow, large) |

Internally a preset is just a set of the parameters below; let advanced users override any of them.

## 2. Every parameter

### Input / material filter

| Setting | CLI | Default | Range | Effect |
|---|---|---|---|---|
| Skip emissive | `--keep-emissive` to turn off | on | bool | glows never collide |
| Skip glass | `--skip-glass` | off | bool | windows / balcony glass collide unless this is on |
| Foliage filter | `--foliage REGEX`, `--no-foliage` | see 03 §2 | regex | texture / material names left out |
| Extra skip | `--skip REGEX` | empty | regex | anything else by name |
| Skip meshes | `--skip-mesh 3,17,42` | empty | list | by mesh number from the report |
| Min height | `--min-y` | none | m | ignore geometry below |

### Resolution and trimming

| Setting | CLI | Default | Range | Effect |
|---|---|---|---|---|
| Voxel size | `--voxel` | 0.05 (presets 0.03–0.10) | 0.02–0.15 m | everything scales with it: accuracy, memory (∝ 1/V³), time |
| Trim radius | `--radius` | 0.25 | 0.1–0.6 m | size of the "is this a panel or a stick?" neighbourhood; larger trims thicker poles |
| Trim keep | `--keep` | 0.30 | 0.1–0.8 | fraction of a flat panel's neighbour count needed to stay; higher trims more |
| Min piece | `--min-size` | 0.30 | 0–1 m | loose bits smaller than this vanish |
| Close | `--close` | 0 | 0–0.4 m | exact mode only: fuse double-sided walls (creates corner fillets; snapping repairs most) |
| Thicken | `--thicken` | 0 | 0–3 voxels | grow surfaces |

### Reachability and walking

| Setting | CLI | Default | Range | Effect |
|---|---|---|---|---|
| Spawn | `--spawn x,y,z` | largest floor | point | start of the flood fill — read it from the `.SCENE` SpawnPoint |
| Reach | `--reach` | 2.4 | 0 = off, 2–4 m | collision kept up to this height above reachable floors |
| Margin | `--margin` | 0.6 | 0.3–1.5 m | … and this far beside them |
| Flood step | `--step` | 0.35 | 0.2–0.5 m | generous step for *finding* reachable floors (stairs with real risers count) |
| Walk step | `--walk-step` | 0.12 | 0.05–0.2 m | the real Home step: used for edge walls, protected air, pruning, floor level |

### Edge walls

| Setting | CLI | Default | Range | Effect |
|---|---|---|---|---|
| Edge wall height | `--edge-wall` | 0.30 | 0 = off, 0.2–1.2 m | invisible wall height at open drops |
| Edge drop | `--edge-drop` | 0.40 | 0.2–1 m | a drop deeper than this counts as open |
| Edge thickness | `--edge-thick` | 0.05 | 0.02–0.2 m | |

### Boxing

| Setting | CLI | Default | Range | Effect |
|---|---|---|---|---|
| Big blocks | `--blocks` | off (presets 1–3 on) | bool | lossy big boxes vs exact greedy |
| Fill | `--fill` | 0.80 (preset 2: 0.70) | 0.4–0.95 | **main complexity knob in blocks mode**: lower = fewer, bigger boxes |
| Headroom | `--headroom` | 2.4 | ≥ 2.3 m | air above walkable floors that blocks may never fill. Do not go lower than 2.3. |
| Min box | `--min-box` | 0 | 0–0.3 m | drop leftover tiny boxes |

### Post-processing

| Setting | CLI | Default | Effect |
|---|---|---|---|
| Snap | `--no-snap` to turn off | on | tops and faces moved onto the real model surfaces |
| Snap tolerance | `--snap-tol` | 0.02 | tops this close become equal |
| Prune | `--no-prune` | on | drop boxes the avatar can never touch |
| Prune distance | `--prune-dist` | 0.40 | how close to standable air a box must come |
| Floor tolerance | `--floor-tol` | 0.15 | tops this close to the main floor become exactly the floor; thin lips on the floor removed. 0 = off (use 0 for split-level spaces with real small steps you want kept) |
| Seams | `--seams` | 0.30 | how far boxes are pushed into same-height neighbours; 0 = off |

## 3. Memory and time guidance for the app

* Voxel count = `(sizeX/V) × (sizeY/V) × (sizeZ/V)`. Apartment at 0.08: ~10 M; at 0.03: ~190 M. Stilt-house lot
  (73 × 18 × 116 m) at 0.08: 280 M.
* Store occupancy as 1 byte per voxel (simple) or 1 bit (BitArray / ulong[]) for big scenes. The heavy temporary
  arrays are the box-filter counts (int per voxel) and the summed-area tables in blocks mode (int per voxel of a tile);
  process in slabs / tiles exactly like the reference (`slabs()` with 96-voxel slabs, 160-voxel tiles).
* Show the estimated voxel count before running and warn above ~150 M.
* The flood fill and edge detection are linear; the box filter is linear with separable prefix sums; the blocks grow
  step is the slowest part in blocks mode (O(boxes × growth steps)), the greedy merge is linear.

## 4. What to show the user (UI suggestions)

* Report table per mesh: index, triangles, material type, colour texture, kept/skipped + reason, with checkboxes that
  feed `--skip-mesh`.
* Box count and predicted file size (`0x6D0 + 0x500 + 913 × boxes` bytes) live as settings change.
* A 3-D view: model (grey), collision boxes (orange, semi-transparent), removed voxels (red), edge walls (cyan),
  original HKX (blue) if loaded; plus a horizontal cut-away at spawn height + reach.
* The walk-test result: reachable area, fall-off points, a top-down map (and the comparison against an original HKX).

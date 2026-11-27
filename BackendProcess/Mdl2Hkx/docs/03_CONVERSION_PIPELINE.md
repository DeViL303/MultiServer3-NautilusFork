# 03 — The MDL → HKX conversion pipeline

This is the algorithm in `python_reference/mdl2hkx.py` (v0.5) and its C# port, step by step. Every rule is here
with the reason it exists — most of them were added because something went wrong in game.

Notation: `V` = voxel size (m). Grid indices `(i, j, k)` ↔ `(x, y, z)`; **j / y is up**. A voxel `(i,j,k)` covers
`lo + (i,j,k)·V … lo + (i+1,j+1,k+1)·V`.

```
 MDL ─► 1 triangles ─► 2 material filter ─► 3 voxelise (+ surface index) ─► 4 sticky-out trim
      ─► 5 close / thicken (optional) ─► 6 reachability (floor flood fill, keep band)
      ─► 7 edge walls ─► 8 boxes (exact greedy  OR  big blocks)
      ─► 9 post: snap tops → snap faces → prune → one flat floor + remove lips → bury seams
      ─► 10 write HKX + report + overlay
```

---

## 1. Read triangles

Read every mesh (see 02_MDL_FORMAT.md) and keep, per mesh, its triangles and material info.

## 2. Material filter (which meshes collide)

A mesh is left out when, in this order:

| Rule | Default | Why |
|---|---|---|
| material type is `emissive` | on (`--keep-emissive` turns it off) | glow cards, light beams, signs' halos |
| material type is glass `0x7F05531A` | **off** (`--skip-glass` turns it on) | windows and balcony glass normally should block |
| mesh index in `--skip-mesh` list | empty | manual override from the report |
| material instance name or any texture file name matches the **foliage regex** | on | leaves, fronds, grass, cables, nets, litter |
| … matches the user `--skip` regex | empty | anything else |
| (optional) geometry below `--min-y` | off | underground clutter |

Default foliage regex (case-insensitive):
`leaf|frond|palmleaf|bush|grass|tuft|vine|ivy|fern|flower|petal|moss|weed|hedge|cable|wire|rope|netting|litter`

The report lists every mesh with its triangle count, material type, colour texture and the reason it was kept/skipped,
so the user can add `--skip-mesh` numbers.

## 3. Voxelise the surfaces (and remember where they really are)

* Bounds: `lo = min(vertices) − 3V`, `hi = max(vertices) + 3V`, grid size `n = ceil((hi − lo) / V)`.
* Each triangle `(a, b, c)` is sampled densely: `L` = longest edge, `k = ceil(L / (0.45 V)) + 1`, points
  `a + u(b−a) + w(c−a)` for `u, w ∈ {0, 1/k, …, 1}`, `u + w ≤ 1`. Spacing < 0.45 V guarantees no voxel the triangle
  passes through is missed.
* Every sample marks its voxel solid (`occ`).
* **Surface index** (needed later to snap boxes back onto the real geometry): for each axis `a ∈ {x, y, z}` and each
  voxel, store the **min and max coordinate along `a`** of the samples in that voxel — but only samples from triangles
  that *face* that axis, `|normal_a| > 0.3`. So the y-index knows where floors/tops really are, the x-index where
  x-facing walls are, etc. (Mixing them was a bug: a floor passing through a wall's boundary voxel made the wall
  look like it reached the voxel edge.)

Memory: one bit/byte per voxel. The apartment at 8 cm is ~250×120×350 = 10 M voxels. A big outdoor model at 8 cm can be
280 M voxels, so the reference processes heavy steps in x-slabs (`slabs()` helper) and the box step in tiles.

## 4. Sticky-out trim (leaves, twigs, wires, thin poles)

1. `r = round(radius / V)`, window `w = 2r + 1`.
2. For each solid voxel count the solid voxels in the `w × w × w` cube around it (box filter, zero outside).
3. A flat panel through the cube centre has `w²` voxels. **Seed** = solid voxels with `count ≥ keep × w²`.
   Panels, walls, floors and solids pass; isolated sticks, wire, single leaves fail.
4. **Grow back**: dilate the seed `r` times with the 6-neighbour cross, each time AND-ed with the original solid. This
   restores panel edges and corners that step 3 nibbled, but never adds anything that was not there.
5. **Small pieces**: label 26-connected components; drop any whose bounding box's largest side `< min-size`.

Defaults: radius 0.25 m, keep 0.30, min-size 0.30 m. On the apartment this removes nothing (it is all panels); it is
the safety net for props and plants that the foliage regex did not catch.

## 5. Optional fusing

* `--close d` (exact mode only): morphological closing with `round(d / V / 2)` iterations, then OR with the original.
  Fuses the two faces of a thin wall into one solid so greedy boxing makes one box instead of two.
  **Side effect**: it also fills every inside corner (floor–wall) with a fillet ~1 voxel deep — a lip at your feet.
  That is why big-blocks mode ignores it (it fills cavities its own way) and why the face snapping (step 9b) searches
  inwards.
* `--thicken N`: dilate N times. Rarely needed.

## 6. Reachability — only collide where an avatar can go

### 6a. Standable cells

`floor(i,j,k)` = voxel is solid **and** the `Hc = round(1.7 / V)` voxels directly above it are all empty
(and inside the grid).

### 6b. Start cell

* With a spawn point `p` (from the `.SCENE` SpawnPoint translate): `(i,j,k) = floor((p − lo) / V)`; among the floor
  cells in the column window `i±6, k±6` (all heights) take the one with the smallest `|Δi| + |Δj| + |Δk|`.
* Without: the first cell (x-major order) of the largest 26-connected floor component.

### 6c. Flood fill

BFS over floor cells. From `(i,j,k)` you may move to the 4 horizontal neighbours `(i±1, k)`, `(i, k±1)` at any height
`j + dj` with `|dj| ≤ floor(step / V)` that is a floor cell.

It is run **twice**:

| Run | Step | Result | Used for |
|---|---|---|---|
| generous | `--step` 0.35 m | `seen` | deciding what collision to keep (step 6d). Generous, so a staircase modelled with real 20 cm risers still counts as reachable. |
| realistic | `--walk-step` 0.12 m | `walk` | edge walls, protected air, pruning, main floor level. Matches what the Home avatar can really climb. |

### 6d. Keep band

`band` = for every `seen` cell, its column from 1 voxel below to `round(reach / V)` voxels above (default reach 2.4 m),
then dilated horizontally by a disk of radius `round(margin / V)` (default 0.6 m). Everything solid outside the band
is removed: roofs, ceilings above reach, outer facades, the far side of walls. (On the apartment this removed 175k
voxels — roof, ceiling slats, facade.)

## 7. Edge walls — open drops beside flat floor

For every `walk` cell and each of its 4 horizontal neighbours `(a, c)`:

* if column `(a, c)` has **any** solid voxel from `D = ceil(edgeDrop / V)` below the floor up to head height
  (`j − D … j + Hc`) → not an open edge (floor continues, a step, a ledge or a wall);
* else, if the floor also continues level on the *inner* side (`walk` at `(i − di, k − dk)` within ±1 voxel) → it is a
  flat edge with an open drop: record it.

Neighbouring edge cells on the same line and height are merged into strips and become thin boxes:
thickness `--edge-thick` 0.05 m, height `--edge-wall` **0.30 m** above the floor, placed on the edge face.
(A 30 cm wall is enough because the avatar cannot step 30 cm.) Edge walls are added at the end and are not snapped.

Ledges ≥ ~35 cm in the model already block, so they get nothing extra.

## 8. Boxes

### 8a. Exact mode (default): greedy merge

Lossless — every solid voxel is covered exactly once:

1. Run-length encode along x for every (z, y) row → runs `[x0, x1)`.
2. Merge runs with identical `(z, x0, x1)` on consecutive y → rectangles.
3. Merge rectangles with identical `(x0, x1, y0, y1)` on consecutive z → boxes.

Produces many boxes (apartment: ~1,000 at 5 cm, ~3,000 at 3 cm) but follows the model closely.

### 8b. Big-blocks mode (`--blocks`): lossy, few large boxes

Classify every voxel:

| Class | Definition | Rule |
|---|---|---|
| **solid** | kept voxels | must be covered |
| **protected air** | for every `walk` cell, the `max(Hc, round(headroom / V))` voxels above it, minus solid | **never** inside a box |
| **don't care** | inside the keep band and not protected (wall cavities, under floors, beyond walls, above headroom) | may be swallowed |

`good = solid ∪ (band ∖ protected)` (without a band: `solid ∪ ¬protected`).

**Headroom must be the full reach height (2.4 m), not the avatar height.** Using 1.7 m let a block fill the air under
the recessed ceiling at 4.91 m over a 3.0 m floor — 1.84 m headroom — and the avatar got stuck under it in game.

Algorithm (per tile of 160 × all × 160 voxels, cropped to the solid's bounding box + 2 voxels):

1. Build 3-D summed-area tables `S` (of `good`) and `P` (of `protected`) → any box count in O(1).
2. **Grow**: for each still-uncovered solid voxel in x-major order, start a 1-voxel box and repeatedly try to extend each
   of the 6 faces by one voxel (order: −x, +x, −y, +y, −z, +z). Accept the extension if the new slab contains
   **no protected voxel** and its `good` count ≥ `fill × slabVolume`. Repeat until no face can move. Mark its solid
   voxels covered.
3. **Merge pass** (repeat until nothing changes): visit boxes largest-first; for box `i` find all other boxes `c` whose
   union bounding box with `i` contains no protected voxel and has `good ≥ fill × volume`; merge with the one giving
   the smallest union.
4. **Redundancy pass**: count coverage per voxel; visiting smallest boxes first, drop a box if every solid voxel in it
   is covered at least twice (i.e. by someone else too).

Across tiles: join boxes that have the same cross-section and touch exactly at a tile border (repeat for x and z).
`--min-box` optionally drops leftover boxes whose largest side is below a size.

`fill` is the main complexity control: 0.55 → fewest boxes; 0.80 → more, tighter boxes.

## 9. Post-processing (both modes, in this order)

### 9a. Snap tops to the real surface

For each box, look at its top voxel layer: the highest y of **y-facing** surface samples in it (surface index).
If that value is above the box bottom and not above the current top → set the top there.
Boxes whose top layer holds no real surface (pure fill) adopt the highest snapped top that shares their voxel layer.
Then tops within `--snap-tol` (0.02 m) of each other are made exactly equal (groups span at most the tolerance; no
chaining, otherwise a chain of 2 cm steps drags everything to the highest value).

### 9b. Snap side faces (and tops) to the real surface

For each box face except the **bottom** (snapping bottoms up turns floors into 3 cm slabs floating above the real floor):

1. **Exposed?** Look at the voxel layer just outside the face. If more than 60 % of it is covered by **other boxes**,
   the face is internal — leave it. (Testing against solid voxels instead was a bug: a 1-voxel-thick floor's side looked
   half empty, the face got pulled back and opened holes in the floor.)
2. Search from the face inwards, at most 4 layers (and not past the box), for the first layer that contains surface
   samples facing that axis (surface index for that axis).
3. Move the face to the extreme of those samples (max for +faces, min for −faces), only if that stays inside the box.

This is what makes walls line up: apartment right wall 4.999 vs model 5.000; skirting 4.977 vs 4.977.

### 9c. Prune

Drop boxes that come nowhere near the air an avatar can occupy: a box is kept if its bounding box grown by
`ceil(pruneDist / V)` voxels (default 0.40 m) touches any voxel of `walk` cells' columns from the floor up to the headroom.

### 9d. One flat floor

* **Main floor level** = the most common real y-facing surface height (rounded to 1 cm) at the `walk` cells.
* Every box whose top is within `--floor-tol` (0.15 m) of the main level **and** whose bottom is below it → top set to
  exactly the main level. (Removes 3–8 cm thresholds, door tracks and grid rounding; the avatar cannot step them.)
* **Lips**: boxes lying *on* the floor (bottom ≥ main level, top ≤ main + tol) are removed when floor-level boxes cover
  their whole footprint (sampled every V/2) — so no hole can open.

### 9e. Bury seams (precaution against Havok ghost edges)

Two boxes whose tops are equal (±0.1 mm) and whose side faces meet (gap ≤ 1 cm) make a seam the character may catch on.
For each such box side, push it into the neighbour by the largest of `seams, seams/2, seams/4, V` (default 0.30 m)
for which the extension rectangle is completely covered by footprints of boxes with the same top. The original face is
then buried inside the neighbour. 1 cm tolerance also closes hairline gaps left by snapping.

Known limitation: when the neighbour covers only part of the shared edge (typical with many small boxes in exact mode),
neither side can be pushed over its full length and the seam stays. Apartment: 0 left in every big-blocks preset,
16 short ones left at 3 cm exact. A possible improvement is a "bridge" box covering just the overlapping part of the
seam (inside both boxes, same top) — not implemented.

## 10. Write

Each box → 8 corners → one rigid body (centre = box centre, shape = local corners, 6 planes); then edge walls.
See 01_HKX_FORMAT.md. Write the report and (Python) the overlay pictures.

---

## Order matters — a summary of why

| Step | If you skip it / reorder it |
|---|---|
| 2 foliage before 3 | leaves become collision blobs; the geometric trim alone does not catch flat leaf cards |
| 6 reach before 8 | blocks would fill roofs and facades; many useless boxes |
| `walk` with the real 12 cm step | the protected air misses areas → blocks fill places you can stand |
| 9a/9b before 9d | floor level would be computed from grid-rounded tops |
| 9d before 9e | seams are found between boxes with *final* tops only |
| exposed test against boxes, not voxels | holes in floors |
| headroom = reach, not 1.7 m | avatar sticks under recessed ceilings |

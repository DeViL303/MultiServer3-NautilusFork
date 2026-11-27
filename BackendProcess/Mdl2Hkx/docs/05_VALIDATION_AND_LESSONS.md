# 05 — Validating a collision file, and what we learned in game

## 1. Offline checks (build these into the app)

### 1.1 Read-back check
Write the HKX, read it back with the reader (01 §8), and compare every body's world AABB with the boxes you meant to
write (tolerance 1e-5 m). Catches every layout / fixup mistake before the user goes in game.

### 1.2 Walk test (`hkxwalk.py`, `WalkTest.cs`)
Simulates the avatar on the **collision only** (no model):

1. Voxelise the HKX at 5 cm (boxes filled, rotated hulls point-tested, mesh triangles sampled).
2. Grow the solid horizontally by the avatar radius (0.25 m disk).
3. Standable cell = solid directly below, own cell free, and the grown solid free from step height up to the avatar
   height (use **2.3 m** — 1.7 m misses low ceilings that really trap the avatar).
4. BFS from the spawn: to a horizontal neighbour you may step up at most `step` (0.10 m) or step / drop down at most
   `fall` (1 m).
5. **Fall-off point**: a neighbour where nothing solid lies within `fall` below and the body would fit → you could walk
   off into a hole. Must be 0.
6. Output: reachable area (m²), number of height levels, fall-off points, a top-down map.

Run it on the original HKX too and diff the two reachable maps — red where the original lets you walk and the new one
does not. That diff is how the "stuck in this area" bug was found.

> Map orientation: draw the map as seen **from above** — +x to the right and **+z down** the page. Drawing +z up mirrors
> it and confused us once ("I think you have something flipped").

### 1.3 Seam check (`seamcheck.py`)
List pairs of boxes with equal tops whose side faces meet (gap ≤ 1 cm) where no third same-top box covers across the
join. Should be 0 for floors.

### 1.4 Wall-gap check (`facecheck.py`)
Sample box side faces between 3.1 and 4.8 m (body height), measure distance to the nearest model surface point
(2 cm samples, k-d tree). Median a few cm for blocks, < 1 cm for exact mode.

### 1.5 Box-at-body-height map
Draw the footprints of all boxes that overlap 0.25–1.8 m above the floor on top of the model's floor triangles. Anything
in the middle of a room is a bug.

## 2. Bugs we hit, in order, and the rule each one produced

| # | Symptom in game / tests | Cause | Rule now in the pipeline |
|---|---|---|---|
| 1 | Balcony "has no barrier" | ledge is 34 cm high, flood fill allowed 35 cm steps | two flood fills: generous 0.35 m to find floors, real 0.12 m for walking; edge walls only at flat open drops (30 cm) |
| 2 | Edge walls never fire on the apartment | the balcony has a 50 cm solid upstand everywhere | correct — 0 walls is right; detector proven on the stilt-house dock (184 strips) |
| 3 | Out of memory on big models | 280 M voxel grid, int arrays | slabs for filters, tiles for blocks, int16 cumulative sums |
| 4 | Leaves still collide | leaf cards are flat panels, pass the geometric trim | foliage regex on texture / material names |
| 5 | "Walls in the middle of the apartment", stuck in part of the main room | block filled air between 4.91 m and the ceiling (1.84 m headroom) because only 1.7 m above floors was protected | headroom = 2.4 m; walk test with a 2.3 m avatar |
| 6 | (same report) | floor made of two slabs meeting edge to edge across the room | seam burying (precaution) |
| 7 | Floor 7 cm above the visible floor | box tops on the voxel grid | top snapping to real surfaces |
| 8 | Floor tops chained up to 3.07 | tolerance grouping chained 2 cm steps | groups span at most the tolerance |
| 9 | "Can't get as close to the walls as I'd like" | `--close` filled floor–wall corners → 8 cm × 23 cm lip at the feet | no closing in blocks mode; face snapping searches inwards up to 4 layers |
| 10 | Faces did not snap | floor samples in a wall's boundary voxel | surface index per axis, only from triangles facing that axis |
| 11 | Floor became a 3 cm slab at 3.03 | bottom faces were snapped up | never snap bottoms |
| 12 | Hairline gap between floor slabs (0.3 mm) | snapping moved one face | seam tolerance 1 cm |
| 13 | Holes in the floor at other complexity settings | "exposed face" decided from solid voxels; a 1-voxel floor looked half empty | exposed = not covered by other **boxes** |
| 14 | 3 cm threshold boxes on the floor | lips above the floor were not lowered (bottom above floor level) | remove lips that sit on fully covered floor |
| 15 | File 5× bigger than the original | ~900 bytes per box; 267 boxes, mostly corner-fillet slices | fix #9 → 77 boxes |
| 16 | "Collision in the wrong place / flipped?" | it was not — map drawn mirrored, plus bug #5 | draw maps from above with +z down; read-back check |

## 3. Facts about Home's avatar (measured / reported)

| Fact | Value |
|---|---|
| Step-up it can climb | ~7–8 cm comfortably, ~11 cm hard, 20 cm no, 35 cm no |
| Needs headroom | > 1.84 m definitely sticks; we keep 2.4 m; test with 2.3 m |
| Radius used in tests | 0.25 m (matches how close it gets to walls in game) |
| Coordinates | Y up, metres, MDL = HKX = SCENE frame, identity scene transform |
| Stairs | model risers (~20 cm) must be split in collision into 3 sub-steps per riser (stilt house) — not automated yet |

## 4. Not done yet (ideas for the app)

* **Stairs**: detect runs of risers and add 2–3 sub-steps per riser (or a ramp). Needed for any multi-level space.
* **Curves**: axis-aligned boxes stand off curved walls by up to the curve's bulge (8 cm on the apartment balcony at
  one spot). Rotated boxes or convex hulls following the curve (the format supports any convex hull) would fix it.
* **Bridge boxes** for leftover seams in exact mode (03 §9e).
* **Merging bodies**: one rigid body with an `hkpListShape` of many boxes would make files much smaller than one body
  per box, but needs the list-shape layout worked out and tested in Home.

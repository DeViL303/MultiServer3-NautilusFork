# 01 — The Home collision HKX format (Havok 5 packfile, PS3)

This is everything we have worked out about the collision `.HKX` files PlayStation Home loads, written so that you can
read and write them from C# without any Havok SDK. All of it was checked against:

* the original Sony `BASIC_APARTMENT.HKX` (46 convex bodies + 2 triangle-mesh bodies), which we read back correctly;
* `HKX_TEMPLATE.HKX` in `template/`: a one-body file produced by the HDK 1.86 exporter, used as the layout template;
* our own files (stilt house, Basic Apartment), which load and collide correctly in Home.

> Scope: everything here is what is needed for **static collision made of convex shapes** (boxes or any convex hull).
> Reading triangle-mesh bodies (MOPP) is described too, but writing MOPP data is not; we never need it.

---

## 1. Ground rules

| Thing | Value |
|---|---|
| Byte order | **big-endian** everywhere (PS3) |
| Pointer size | 4 bytes |
| Floats | IEEE-754 single precision, big-endian |
| Coordinate system | Home world space, metres, **Y is up**. Identical to the MDL vertex coordinates and the `.SCENE` translate values. No axis swap, no scale. |
| Alignment | Objects start on 16-byte boundaries; arrays of vectors are 16-byte aligned; strings are padded to 16 |

A packfile is: **file header → section headers → section bodies**. Section bodies hold the data plus, at their end, three
*fixup tables* that turn the stored offsets into pointers at load time.

---

## 2. File header (0x00–0x40)

| Offset | Size | Value in Home files | Meaning |
|---|---|---|---|
| 0x00 | 4 | `57 E0 E0 57` | magic 0 |
| 0x04 | 4 | `10 C0 C0 10` | magic 1 |
| 0x08 | 4 | 0 | user tag |
| 0x0C | 4 | 5 | file version |
| 0x10 | 4 | `04 00 01 01` | layout rules: pointer size 4, little-endian **0** (= big-endian), reuse-padding 1, empty-base-class 1 |
| 0x14 | 4 | 3 | number of sections |
| 0x18 | 4 | 1 | index of the section holding the contents object (`__data__`) |
| 0x1C | 4 | 0 | offset of the contents object inside that section (the root container is at data+0) |
| 0x20 | 4 | 0 | section holding the contents class name (`__classnames__`) |
| 0x24 | 4 | 0x155 | offset of the class name `hkRootLevelContainer` inside `__classnames__` |
| 0x28 | 16 | `Havok-5.0.0-r1\0` + `0xFF` padding | contents version string |
| 0x38 | 8 | `FF FF FF FF FF FF FF FF` | padding / flags |

Copy these 0x40 bytes from the template unchanged.

## 3. Section headers (0x40, 0x70, 0xA0 — 0x30 bytes each)

| Rel. offset | Size | Meaning |
|---|---|---|
| +0x00 | 20 | section name, NUL padded: `__classnames__`, `__data__`, `__types__` |
| +0x14 | 4 | **absolute** file offset where this section's body starts |
| +0x18 | 4 | local fixups table offset (relative to section start) |
| +0x1C | 4 | global fixups table offset (relative) |
| +0x20 | 4 | virtual fixups table offset (relative) |
| +0x24 | 4 | exports offset (relative) — set equal to the end |
| +0x28 | 4 | imports offset (relative) — set equal to the end |
| +0x2C | 4 | end offset (relative) = section size |

Values in the template:

```
__classnames__  start 0x0D0   local/global/virtual/exports/imports/end = 0x600 (no fixups; the whole body is the name table)
__data__        start 0x6D0   local 0x830  global 0x960  virtual 0x9D0  exports/imports/end 0xA30
__types__       start 0x1100  everything 0 (empty section, starts at end of file)
```

When writing a new file you keep `__classnames__` as it is and rewrite only the `__data__` header
(start 0x6D0, then `o1, o2, o3, o4, o4, o4`) and the `__types__` start (= 0x6D0 + o4, rest 0).

## 4. The `__classnames__` section (file 0xD0–0x6D0)

A list of records: `u32 signature`, `u8 0x09`, `class name`, `NUL`. Records are packed back to back; the table ends at 0x6D0.
Objects in `__data__` refer to their class by the **offset of the name text** (record start + 5) relative to the section
start (0xD0). The offsets that matter:

| Class | Name offset | Signature |
|---|---|---|
| `hkRootLevelContainer` | 0x155 | 0xF598A34E |
| `hkxEnvironment` | 0x5F1 | 0x1ADF8FA3 |
| `hkxScene` | 0x42C | 0x1FB22361 |
| `hkpPhysicsData` | 0x319 | 0xC2A461E4 |
| `hkpPhysicsSystem` | 0x351 | 0x1B58F0EF |
| `hkpRigidBody` | **0xB1** | 0x3224DE76 |
| `hkpConvexVerticesShape` | **0x24C** | 0xF65B406A |
| `hkpMoppBvTreeShape` / `hkpSimpleMeshShape` | present in Sony files that contain triangle meshes (not in the template) |

The template's table lists ~70 classes (the full list is in `template/HKX_TEMPLATE_annotated_dump.txt`). Copy it unchanged.

## 5. Fixup tables (at the end of `__data__`)

All offsets are relative to the start of `__data__`.

* **Local fixups** — pairs `(u32 src, u32 dst)`: "the pointer at `src` points to `dst` in this same section".
  Used for arrays and strings owned by an object.
* **Global fixups** — triples `(u32 src, u32 dstSection, u32 dst)`: "the pointer at `src` points to object `dst` in
  section `dstSection`". dstSection is always 1 (`__data__`) here. Used for pointers to whole objects (variants,
  physics system, rigid bodies, shapes).
* **Virtual fixups** — triples `(u32 obj, u32 0, u32 classNameOffset)`: "an object of this class starts at `obj`".
  This is how the loader knows which class each object is (it installs the vtable). Section index is 0 (`__classnames__`).

Each table is padded to a multiple of 16 bytes with `0xFFFFFFFF` words. Readers must skip any entry whose first word is
`0xFFFFFFFF`.

Pointer fields inside objects are stored as 0 in the file; only the fixup tables say where they point.

## 6. The `__data__` section

### 6.1 Fixed prefix (data 0x000–0x500): containers, copied from the template

```
0x000  hkRootLevelContainer        { namedVariants* -> 0x010, count 3 }
0x010  3 × NamedVariant (16 B each): name* , className* , variant* (global)
         [0] "Environment Data" / "hkxEnvironment" -> 0x0B0
         [1] "Scene Data"       / "hkxScene"       -> 0x3A0
         [2] "Physics Data"     / "hkpPhysicsData" -> 0x450
0x040…0x0AF  the six strings above
0x0B0  hkxEnvironment              { variables* -> 0x0C0, count 11 }  (exporter info: Maya, HDK version, paths)
0x0C0…0x39F  11 name/value string pairs
0x3A0  hkxScene                    (modeller "Maya 2012 x64", asset path; a 0.01 appliedTransform — irrelevant to physics)
0x450  hkpPhysicsData              { worldCinfo* = null, systems* -> 0x470 (local), count 1 }
0x470  [ pointer to the physics system ]  (global fixup 0x470 -> 0x480)
0x480  hkpPhysicsSystem
         +0x08 (data 0x488) rigidBodies*  (local fixup -> the body pointer array)
         +0x0C (data 0x48C) rigidBodies count            <- N
         +0x10 (data 0x490) rigidBodies capacity|flags   <- 0xC0000000 | N
         +0x14…           constraints / actions / phantoms arrays, all empty (count 0, flags 0xC0000000)
         +0x38 (data 0x4B8) name* -> 0x4E0 "Default Physics System"
         +0x40 (data 0x4C0) active = 1
0x4D0  body pointer array of the template (1 entry). Our writer moves the array to the end, so this is zeroed.
0x4E0  "Default Physics System"
```

Fixups for this prefix (all copied from the template; the writer keeps every template local fixup with `src < 0x500`
**except** `0x488`, which it re-targets to its own body array):

```
local   (0x000→0x010) (0x010→0x040) (0x014→0x060) (0x020→0x070) (0x024→0x080) (0x030→0x090) (0x034→0x0A0)
        (0x0B0→0x0C0) (0x0C0…0x114 → the environment strings) (0x3A0→0x420) (0x3A4→0x430) (0x45C→0x470) (0x4B8→0x4E0)
global  (0x018→0x0B0) (0x028→0x3A0) (0x038→0x450) (0x470→0x480)
virtual (0x000:0x155) (0x0B0:0x5F1) (0x3A0:0x42C) (0x450:0x319) (0x480:0x351)
```

### 6.2 Body pointer array

Directly after the prefix (data 0x500), `N` × u32 (zero), padded to 16. Local fixup `(0x488 → array)`, and one global
fixup per body `(array + 4·i → body_i)`.

### 6.3 Per body: `hkpRigidBody` (0x200 bytes) — copied from the template body at data 0x500

Fields we **write**:

| Offset in body | Type | Value |
|---|---|---|
| +0x10 | ptr | shape pointer — **global** fixup `(body+0x10 → shape)` |
| +0x70 | ptr | name string — **local** fixup `(body+0x70 → name)` |
| +0x110 | float4 | body translation = box centre `(cx, cy, cz)` (w left as template, 1.0) |
| +0x120 | float4 | swept-transform centre 0 = same centre (w 0) |
| +0x130 | float4 | swept-transform centre 1 = same centre (w 0) |
| +0x180 | float | object radius = length of the half-extent vector `|(hx, hy, hz)|` |

Fields we **keep from the template** (important ones):

| Offset | Value | Meaning (best understanding) |
|---|---|---|
| +0x14 | 0xFFFFFFFF | shape key (none) |
| +0x28…+0x30 | 0x01000001, 1 | collidable / broad-phase type flags (fixed static object) |
| +0x44 | 0x80000000 | broad-phase handle |
| +0x58 | 0x7F7FFFEE | allowed penetration depth (≈ FLT_MAX) |
| +0x80 | 0x01 | material response type: simple contact |
| +0x84 | 0.5 | friction |
| +0x88 | 0.4 | restitution |
| +0xE0, +0xF0, +0x100 | (1,0,0,0) (0,1,0,0) (0,0,1,0) | rotation matrix **columns** (identity). Sony files may hold real rotations here. |
| +0x140, +0x150 | (0,0,0,≈1) | swept-transform rotations (identity quaternions) |
| +0x188 | 0.05 | (motion) — left as template |
| +0x18C | 0x7F7F0100 | motion type / quality flags of a **fixed** (static) body |

The rigid body is static/fixed: nothing moves, so mass, inertia and velocities are irrelevant.

Virtual fixup `(body : 0xB1)`.

### 6.4 Per body: the name string

`"Collision|<name>_<index:00000>"` + NUL, padded to 16. Our files use `Collision|box_00000`, `Collision|box_00001`…
Home does not appear to care about the name, but keeping the `Collision|` prefix matches what the HDK exporter produces.

### 6.5 Per body: `hkpConvexVerticesShape` (0x60 bytes) — copied from the template shape at data 0x720

| Offset in shape | Type | Value |
|---|---|---|
| +0x00 | vtable | (virtual fixup `(shape : 0x24C)`) |
| +0x08 | u32 | 0x5772A7A0 in the template — keep |
| +0x20 | float4 | AABB half extents `(hx, hy, hz, 0)` in **local** space |
| +0x30 | float4 | AABB centre in local space `(0, 0, 0, 0)` |
| +0x40 | ptr | rotated vertices — **local** fixup `(shape+0x40 → vertex blocks)` |
| +0x44 | u32 | number of vertex blocks `nb = ceil(nv / 4)` |
| +0x48 | u32 | capacity|flags `0xC0000000 | nb` |
| +0x4C | u32 | number of vertices `nv` (8 for a box) |
| +0x50 | ptr | plane equations — **local** fixup `(shape+0x50 → planes)` |
| +0x54 | u32 | number of planes `np` |
| +0x58 | u32 | capacity|flags `0xC0000000 | np` |

Shape vertices are **relative to the body translation** (local space). For a box centred on the body they are `(±hx, ±hy, ±hz)`.
The convex radius field is left at the template value (0): boxes are exact, no rounding shell.

### 6.6 Vertex blocks ("FourVectors")

Vertices are stored in blocks of four, **transposed**: each 48-byte block is
`x0 x1 x2 x3 | y0 y1 y2 y3 | z0 z1 z2 z3` (12 floats). If `nv` is not a multiple of 4, repeat the last vertex to fill
the block. A box (8 vertices) = 2 blocks = 96 bytes.

### 6.7 Plane equations

`np` × float4 `(nx, ny, nz, d)` with outward unit normals and `n·p + d = 0` on the plane, `n·p + d ≤ 0` inside
(local space). A box has 6: `(±1,0,0,−hx) (0,±1,0,−hy) (0,0,±1,−hz)`.
Our Python writer gets them from a convex hull (qhull), rounds to 5 decimals and removes duplicates; numpy's
`unique` sorts them lexicographically, so for a box the stored order is:

```
(-1, 0, 0, -hx) (0, -1, 0, -hy) (0, 0, -1, -hz) (0, 0, 1, -hz) (0, 1, 0, -hy) (1, 0, 0, -hx)
```

The C# writer emits the same order so the two produce comparable files. The order does not matter to Havok.
Note: qhull writes some zero components as **−0.0** (`0x80000000`); the C# writer writes +0.0. Both are valid, but it means
the C# and Python outputs are equal in value, not necessarily byte-for-byte.

(The HDK template shape has only 5 planes for its flat placeholder box; Home accepts either.)

### 6.8 Per-body fixups

```
local   (body+0x70 → name) (shape+0x40 → vertex blocks) (shape+0x50 → planes)
global  (array+4·i → body) (body+0x10 → shape)
virtual (body : 0xB1) (shape : 0x24C)
```

### 6.9 Layout of one body in the stream

```
body   0x200   (16-aligned)
name   "Collision|box_00000\0" padded to 16
shape  0x60
verts  48 × nb
planes 16 × np
pad to 16
```

Size per box body = 0x200 + 0x20 + 0x60 + 0x60 + 0x60 = 0x380 (896) bytes of data + 4 bytes in the pointer array
+ fixups (3×8 + 2×12 + 2×12 = 72 bytes) ≈ **913 bytes measured** (file size = 0x6D0 + 0x500 + ~913 × N).
A 77-box apartment collision is about 73 KB; the original Sony file (46 hulls + 2 meshes) is about 49 KB.

## 7. Finishing the file

```
data      = prefix(0x500) + body array + bodies
o1        = len(data)                      (local table starts)
o2        = o1 + len(localTable)           (padded to 16 with FF)
o3        = o2 + len(globalTable)
o4        = o3 + len(virtualTable)
file      = template[0 : 0x6D0] + data + localTable + globalTable + virtualTable
header @0x70+0x14 (data section): 0x6D0, o1, o2, o3, o4, o4, o4
header @0xA0+0x14 (types section): 0x6D0 + o4, 0, 0, 0, 0, 0, 0
```

## 8. Reading any Home HKX (including Sony's)

1. Read the three section headers; `CB` = classnames start, `B0` = data start, `o1…o4` = table offsets.
2. Build `local[src] = dst`, `global[src] = dst`, `class[obj] = name` (skip `0xFFFFFFFF` rows).
3. For every object whose class is `hkpRigidBody`:
   * rotation `R` = columns at +0xE0, +0xF0, +0x100 (first 3 floats each); translation `t` at +0x110.
     Sony bodies can be rotated; ours are identity. If `R` is not a valid rotation use identity.
   * shape = `global[body + 0x10]`, name = string at `local[body + 0x70]`.
4. Shape kinds:
   * `hkpConvexVerticesShape`: `nb` at +0x44, `nv` at +0x4C, blocks at `local[shape + 0x40]` (transposed, see 6.6).
     World vertex = `R · v + t`.
   * `hkpMoppBvTreeShape`: child mesh = `global[shape + 0x34]`, class `hkpSimpleMeshShape`:
     vertices at `local[mesh + 0x18]`, count at +0x1C (float4 each);
     triangles at `local[mesh + 0x24]`, count at +0x28 (16 bytes each: three s32 indices + padding).
     The MOPP code itself is not needed to read the geometry.

Results on the original `BASIC_APARTMENT.HKX`: 46 convex bodies + 2 mesh bodies, bounds
x −11.03…7.44, y −0.26…9.27, z −13.82…15.56 — it lines up with the MDL exactly, which is how we know there is no axis
change between MDL and HKX.

## 9. Things Home is sensitive to (learned in game)

* **Seams** (precaution): two boxes whose tops are at the same height and that merely *touch* edge to edge can make an
  invisible ridge (the Havok "ghost edge" problem: the character catches the side face of the next box). We bury such
  seams by overlapping the boxes (pipeline step 9e). The file verified in game has all floor seams buried. We never
  isolated a seam as the sole cause of a problem in game — the "wall in the middle of the room" we hit turned out to be
  a low ceiling block — so treat this as cheap insurance rather than a proven rule.
* **Steps**: the Home avatar cannot step up much. 35 cm blocks completely, 20 cm blocks, ~11 cm is hard, ~7–8 cm works.
  So floors must be one exact height, with no lips.
* **Headroom**: anything less than about 2.3 m above a floor makes the avatar stick. Keep 2.4 m clear.
* **Big bodies are fine**, overlapping bodies are fine, thousands of bodies load (the stilt house had 4,000+), but every
  body costs ~1 KB, so fewer is better.

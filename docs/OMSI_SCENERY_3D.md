# OMSI scenery objects in the 3D scene view (Task 06)

This document describes `--map <directory> --scene-3d`: the mode that draws real OMSI
scenery objects over the decoded terrain. It covers what is supported, what was read
from documentation versus assumed, how references are resolved safely, how placements
are converted to world coordinates, what is cached, what is counted and reported, and
what remains unverified.

**It does not claim OMSI compatibility.** Nothing here has been run against a real
OMSI installation or on Windows. Every format claim is either marked *documented*
(with its reference) or *assumed* and listed in
[Unverified assumptions](#unverified-assumptions).

## What the mode does

```bat
src\Nymeria.OMSI\bin\Release\net10.0\Nymeria.OMSI.exe --map "<path>\OMSI 2\maps\Grundorf" --scene-3d
```

The host, in order, before any window or GPU device exists:

1. parses the map's configuration and tile files (Tasks 02 and 03),
2. decodes each tile's terrain sidecar and builds the terrain mesh (Task 05),
3. derives the OMSI installation root from the map folder (`<root>\maps\<map>`),
4. resolves and parses a bounded, deterministic subset of the map's scenery
   placements, decodes their meshes and textures, and converts each placement to a
   world matrix,
5. prints a counted summary, then opens the window and uploads the static geometry and
   textures once.

A placement is selected in tile discovery order and then in file order, up to
`OmsiSceneryLibrary.MaxInstances` (2,000). Every later placement is counted, not
silently dropped.

`--scene-3d` requires `--map` and is exclusive with `--render`, `--terrain-3d`, and
`--world`. `--smoke` combines with it, so a run can present 120 frames and exit.

## What is rendered

| Rendered | Not rendered (deliberate) |
| --- | --- |
| Decoded terrain (unchanged from `--terrain-3d`) | Roads and splines (reserved for the next task) |
| Static scenery objects that resolve and decode | Vehicles, traffic, pedestrians |
| Their base-color textures with one-bit alpha | OMSI scripts, animation, interaction, collision |
| Their placement position, heading, pitch, and bank | Night lighting, light maps, dynamic shadows, weather |
| | Terrain holes, water, ground textures |

Nothing is substituted. A missing `.sco`, an undecodable mesh, or an absent texture is
counted and, for the first few occurrences, listed with its tile and line. No
replacement box, placeholder building, or invented asset is ever drawn.

## Reference resolution

A tile's `[object]` record names a scenery definition relative to the installation
root, for example `Sceneryobjects\...\object.sco`. `SceneryPath` resolves it with
these rules, and the same rules apply to every mesh and texture reference inside a
definition:

- Both `\` and `/` separate components; the reference is normalized for inspection but
  the reported path keeps its original spelling.
- A reference that is absolute (`\...`, `/...`, `\\server\...`), drive-qualified
  (`C:\...`), has an empty component (doubled separator), a `:` inside a component, or
  a control character is **rejected** before the filesystem is touched. Nothing is
  repaired or guessed.
- `.` is dropped. `..` is kept by the normalizer and combined with the reference's base
  directory, because OMSI deliberately uses it for sibling collections
  (`..\..\Kreuz_MC\model\...`). The combined result is accepted only while it stays
  below the content root; a reference that climbs above the root is **rejected**.
- Each component is matched against a real directory entry **case-insensitively**, so a
  Windows-authored path resolves on a case-sensitive filesystem. Two entries that
  differ only in case are reported as ambiguous rather than chosen arbitrarily.
- A directory with more than 20,000 entries is reported instead of scanned, which
  bounds the cost of a hostile or damaged layout.
- Only explicit candidate folders are inspected; there is no recursive search. Every
  candidate is required to stay under the content root, so a resolved file cannot
  escape the authorized content tree.

The installation root is derived only from a map folder whose parent directory is
named `maps`, case-insensitively. No Steam, Aerosoft, or default path is ever assumed.

### Asset search order

The documented OMSI layout puts meshes and textures in subfolders of the object
collection, so the resolver tries the following folders in order and takes the first
existing file:

| Asset | Folders tried |
| --- | --- |
| Mesh | the model-configuration folder, `<config>\model`, `<sco>\model`, the `<sco>` folder, the content root |
| Texture | the model-configuration folder, `<config>\texture`, the `<sco>` folder, `<sco>\texture`, the content root, `<root>\Texture` |

For a texture whose declared name has another extension, the `.dds` spelling is tried
first in each folder, matching OMSI's preference for the DDS variant when both files
exist. A missing file is reported with the folders that were searched; an unsupported
mesh extension is reported as such, and a decode failure is reported by the decoder.
The three cases are not conflated.

A definition's failed parse is also cached: a broken `.sco` referenced by hundreds of
placements is parsed once, its diagnostics are listed once, and every skipped placement
is still counted.

## Supported definition subset

### `.sco`

The reader reuses the repository's documented keyword-block rules
(`KeywordBlockReader`): an unindented `[keyword]` line starts a block, its parameters
follow in order, and indented keywords are help text. Two documented mechanisms are
honoured before that split:

- the `-<DISABLED>-` … `-<ENABLED>-` markers remove the lines between them, and
- a parameter line that starts with an apostrophe is a comment and is skipped.

| Keyword | Handling |
| --- | --- |
| `[model]` | Optional. Its first parameter is read as a further configuration file when it ends in `.cfg` or `.sco`, as a mesh when it has a decodable extension, or otherwise ignored with a warning |
| `[mesh]` | A mesh reference, resolved through the documented model folders, then the installation root |
| `[matl]` | A texture name (first parameter), resolved through the documented texture folders; additional parameters, including the texture-occurrence index, are not interpreted |
| `[absheight]` | Standalone flag: the placement height is absolute, not terrain-relative |

`[groups]`, `[friendlyname]`, `[description]`, and similar user-interface metadata are
recognized and deliberately unused. Every other keyword — scripts, animation,
interaction, collision, advanced material effects — is counted, and the first eight
distinct spellings are named in the diagnostics. No meaning is invented for them.

**Documented versus assumed.** The keyword-block syntax, the switch-off markers, the
apostrophe comment, `[absheight]`, the `model`/`texture` asset folders, and the inline
`[mesh]` form are documented (openOMSI `docs/FORMATS.md`, the OMSI WebDisk
configuration-file guide, the OMSI Wiki, and the SGNL file reference, all inspected on
2026-10-22). A scenery definition is **not** required to have a `[model]` block: real
Grundorf objects such as `Sceneryobjects\Ruede\forest_backdrop01.sco` write
`[mesh] forest_backdrop01.x` inline while the file itself lives in `Ruede\model`. The
reader accepts the inline form without a warning, and also accepts the `model.cfg`
indirection and a mesh named directly by `[model]`.

A mesh is only accepted with a decoded extension: `.o3d` and `.x`. Any other extension
is reported as *unsupported format*, not skipped silently.

### Meshes and textures

- Meshes are decoded by the engine's own parsers —
  `Nymeria.Formats.O3d.O3dModelParser` and
  `Nymeria.Formats.DirectX.XModelParser` — through the engine's `IModelParser`
  contract. This repository contains no mesh decoder.
- Textures are decoded by the engine's `TextureLoader`, which dispatches to its BMP,
  DDS, JPEG, PNG, and TGA decoders. A `.bmp`, `.dds`, or `.tga` that the engine cannot
  decode is reported, and that subset falls back to the shared white texture with the
  material tint.
- Material names come from the mesh itself, and OMSI has two name spaces here: the
  mesh material (a key, often a file name, sometimes a bare label such as `Mat1`) and
  the texture file named by the definition's `[matl]` blocks. A subset is resolved in
  one of three ways, and the three are never conflated:

  | Outcome | Rule | Counted as |
  | --- | --- | --- |
  | Declared mapping | the material name matches a `[matl]` entry, whose file decodes | texture decoded |
  | Name-as-file | no `[matl]` match, but a texture file with that name resolves and decodes | texture decoded |
  | Unmapped material | no `[matl]` match and no file of that name | `missing texture mapping`, one line per definition naming its labels |

  An unmapped label is **not** a missing asset: there is no file to be missing, and
  reporting it as one inflated the old counts on every object whose meshes use labels.
  A declared `[matl]` whose file is absent is a missing asset, and one the engine cannot
  decode is an unsupported format. In every failure case the subset is counted and
  drawn with its base color rather than hidden or replaced.

  OMSI binds a material to a texture **by name**. No source checked for this build
  describes binding `[matl]` entries to material slots by declaration order, so
  positional binding is not implemented; adding it would need a real example.
- Every scenery material is drawn with a single 0.5 alpha test, which covers OMSI's
  one-bit transparency for foliage, railings, and window cut-outs. Per-material
  thresholds and blended transparency are not interpreted.

## Placement transforms

`SceneryTransform` is pure and unit-tested; it holds no window, device, or file-system
state.

**Position.** A tile's records are tile-local: tile column/row times the tile edge plus
the placement's `x`/`y`. The tile edge is the same 300 m (or reference-measured 371.9 m
for `[worldcoordinates]` maps) that the terrain mesh uses, so an object and the ground
beneath it cannot disagree about tile origins.

**Height.** A placement's `z` is terrain-relative unless its definition declares
`[absheight]`. A relative height is added to the bilinearly sampled terrain height at
the object's own x/y (`TerrainHeightSampler`). An absolute height is used as written.
A relative height with no decoded terrain under it is skipped with a counted reason
instead of being placed at sea level, and objects are never snapped to the ground.

**Axes.** OMSI is z-up with x/y in the map plane; Nymeria is y-up with terrain spanning
x and −z. The basis change is a proper rotation about x by −90°, so `(x, y, z)` becomes
`(x, z, −y)`. Handedness is preserved and triangle winding is unchanged, which is why
the scenery pipeline can keep culling off, exactly like the engine's own model
renderer.

**Orientation.** Heading is documented as degrees clockwise from north and is applied
as a rotation about the vertical axis. The **order and sign of pitch and bank are not
documented in any source checked here**, so this build uses one explicit convention:
bank about the model's forward axis, then pitch about its right axis, then heading,
with a positive pitch raising the forward axis and a positive bank raising the right
side. If an object appears rotated on Windows, this convention is the first thing to
check; it is a single expression in `SceneryTransform.TryCreate`.

## Scene integration and resource ownership

`ScenePresenter` (renamed from `TerrainPresenter`, because it now presents one scene
that may or may not include scenery) records one depth-tested render pass:

1. terrain draw (unchanged from Task 05), then
2. one indexed draw per drawable subset per instance, through
   `SceneryRenderer`.

`SceneryRenderer` owns every scenery GPU resource and is created only after the terrain
pipeline exists, so both passes share one depth format and one render pass.

| Concern | Where it lives |
| --- | --- |
| Parsing, resolution, decoding, placement math | `src/Nymeria.OMSI/Scenery/`, GPU-free |
| GPU buffers, textures, sampler, pipeline, draws | `src/Nymeria.OMSI/Rendering/SceneryRenderer.cs` |
| Frame recording, depth texture, camera input | `src/Nymeria.OMSI/Rendering/ScenePresenter.cs` |
| Counts and diagnostics text | `src/Nymeria.OMSI/Rendering/ScenerySummaryWriter.cs` |

**Caching.** One resolved `.sco` path maps to one parsed definition and one model; one
mesh file maps to one set of vertex and index buffers; one decoded texture maps to one
GPU texture. A failed definition is cached as a failure, so the same broken file is
parsed once rather than once per placement. Two hundred instances of one building
therefore cost one decode, one upload, and two hundred model-matrix pushes. Nothing is
decoded or uploaded per frame; a frame pushes two matrices per instance and issues one
draw per subset.

**Bounds.** Every limit is a constant with a counted, reported consequence:
2,000 instances, 256 definitions, 512 mesh files, 512 textures, 4,000,000 decoded
vertices, 64 meshes and 256 `[matl]` entries per definition, 512 KiB and 20,000 lines
per configuration file, 40 distinct listed problems with repeated messages counted
separately.

**Failure handling.** A model that cannot be decoded is dropped with a count. A model
that fails to upload is dropped with a count and a console line, so one broken asset
cannot take the window down. Only a scene where nothing at all could be prepared fails
creation, because then the caller must be told the view would be empty. When zero
placements become instances, the first summary line says the view would be terrain only
rather than claiming that scenery loaded.

**Lifetime.** `SceneryRenderer.Dispose` releases buffers, textures, the sampler, the
pipeline, and the shaders after draining the GPU; `ScenePresenter.Dispose` calls it
before releasing the terrain resources. A failed creation releases everything it
created, in reverse order, after `SDL_WaitForGPUIdle`.

## Shaders

`Shaders/Scenery.hlsl` and `Shaders/Scenery.metal` are this repository's own sources,
equivalent to each other, embedded as `Nymeria.OMSI.Shaders.Scenery.*`. Windows
compiles the HLSL to DXIL with the pinned, checksum-verified DirectX Shader Compiler
already used for the terrain shaders; macOS embeds the MSL source and never runs a
compiler.

Bindings: two vertex uniform slots (view-projection, then model), one fragment
texture, one fragment sampler, position/normal/uv vertex attributes at locations 0–2.
Shading is the terrain shader's fixed sun so the two surfaces agree; it is a diagnostic
term, not OMSI's lighting model.

## Reported counts

No number here is estimated, and no line is a success claim. The first line is a
verdict: `scene: incomplete — …` whenever anything was skipped, and a scene where
nothing became drawable says so instead of naming what it would have drawn. The word
*rendered* is never printed as an achievement.

```text
scene: incomplete — 817 of 1095 placement(s) instanced, 278 skipped; the classification is below
scene content: decoded OMSI terrain with loaded OMSI scenery objects (a bounded, partial scene; …)
map: <map folder>
name: Grundorf
content root: <installation root>
asset roots: <root>; <root>\Texture; and, for each definition, its own folder, its 'model' and
             'texture' subfolders, and its model configuration's folder; no recursive search
coordinate grid: 300 m tiles (documented)
terrain tiles: 19 decoded of 19 discovered
placement budget: 2000 instances, 256 definitions, 512 meshes, 512 textures
placements: 1095 considered, 817 instanced, 817 with a loaded definition
definitions: 78 loaded, 45 with drawable geometry, 33 without
meshes: 28 file(s) decoded, 12 skipped (unsupported format 12)
textures: 2 decoded, 0 skipped (none)
subsets: 61 drawable, 58 drawn with their base color (missing texture mapping 58)
placement skips: unsupported format 278
geometry: 4607 vertices across the distinct definitions
skipped: 0 without decoded terrain, 0 invalid transform(s)
unsupported definition keywords (out of scope by design): 974 occurrence(s) across N keyword(s)
  [script] 120
  …
cache: 78 definition(s), 28 mesh file(s), 2 texture(s) keyed by resolved path, each with a
       (size, last-write time) stamp; 0 re-decoded mid-run
scenery diagnostics: 41 distinct
  [unsupported format] …\Haus01c.o3d: warning: The mesh 'Haus01c.o3d' could not be decoded:
      unsupported: scrambled O3D — … (x12)
```

Every skipped asset carries exactly one class:

| Class | Meaning |
| --- | --- |
| `missing asset` | the referenced file does not exist (a `.sco`, a mesh, or a declared texture) |
| `unsupported format` | the file exists and the decoder refused it: scrambled O3D vertices, an unknown texture encoding, or a documented size limit |
| `parse error` | the file exists but its content is malformed, or a placement's transform is unusable |
| `GPU failure` | a resource could not be created or uploaded; counted by the renderer and printed in the upload line |
| `out of scope by design` | scripts, animation, interaction, paths, sounds, traffic lights, collision — present in real content and deliberately not interpreted |
| `not yet attempted` | skipped because a documented budget was reached |
| `missing texture mapping` | a material label with no `[matl]` binding and no file of that name |

Diagnostics are deduplicated by `(canonical path, class)`: one line names the asset and
the reason, and identical failures elsewhere carry an occurrence counter `(xN)`. The path
is the failing asset's own file, the tile file for a placement-level failure, or — when
the failure has no file at all — its message. Repeated occurrences beyond the listed
twelve are counted in a final line, so a truncated list is never mistaken for a complete
one.

The per-run upload summary is printed after the window closes:

```text
scenery upload: 137 instance(s), 402 subset(s), 68 mesh(es), 91 texture(s), 0 dropped by a GPU failure
```

### Cache identity

The definition, mesh, and texture caches are keyed by resolved path and live for one
load. Each entry also records a `(size, last-write time)` stamp; when a cached asset is
reused, the stamp is compared with the file, and a changed size or time makes the entry
be decoded again instead of drawn from stale data. The stamp is a one-run guard, not a
content identity: last-write time is coarse on FAT and network volumes, archiving tools
preserve it, and a size plus a timestamp can collide. Nothing is persisted, so there is
no cross-run cache to invalidate; a future one would need a hash of the bytes the
decoders already read.

## Unverified assumptions

Checked against openOMSI `docs/FORMATS.md`, the OMSI WebDisk configuration-file guide,
and the OMSI Wiki on 2026-10-22 (prose documentation only; no openOMSI source was
read or copied):

| Claim | Status |
| --- | --- |
| Keyword-block syntax, indented keywords are not keywords | Documented, and already used by the map parsers |
| `-<DISABLED>-` / `-<ENABLED>-` switch-off mechanism | Documented |
| Apostrophe starts a comment line | Documented (WebDisk guide) |
| `[absheight]` makes a placement's z absolute | Documented |
| Terrain-relative z otherwise | Documented |
| `[mesh]` names a mesh file; `[matl]` names a texture | Documented |
| Inline `[mesh]` without a `[model]` block is the usual scenery form | Documented; Grundorf's `Ruede\forest_backdrop01.sco` is an example |
| Meshes may live in the object folder or its `model` subfolder | Documented |
| Textures may live in the object folder, its `texture` subfolder, or `<root>\Texture` | Documented |
| `.dds` wins over the declared texture extension when both exist | Documented; the resolver tries `.dds` first |
| `[model]` points at a further configuration file | Documented; the reader still accepts both forms and reports which was used |
| Material-name to texture matching | Documented that a mesh material is a key named by `[matl]`/`[texchanges]`/`[CTCTexture]`; the fallback that treats the name as a file name is **inferred** from OMSI mesh behavior |
| Positional `[matl]` binding (slot order) | **Not implemented**; no source checked describes it |
| Pitch/bank order and sign | **Assumed**; documented only for heading |
| Alpha test threshold 0.5 for all scenery | **Assumed**; per-material thresholds are not interpreted |
| OMSI grid orientation, terrain relief, and object placement on Windows | **Unverified** until the owner runs it |

## Windows validation checklist

1. Update the repository and the pinned submodule if its commit changed, then build:
   `dotnet build Nymeria.OMSI.slnx -c Release`.
2. Confirm `--map` and `--world` still parse Grundorf.
3. Confirm `--map <Grundorf> --render` still shows the diagnostic plan.
4. Confirm `--terrain-3d` still draws terrain without scenery.
5. Run `--scene-3d` and read the verdict and classification first:
   ```bat
   src\Nymeria.OMSI\bin\Release\net10.0\Nymeria.OMSI.exe --map "<path>\OMSI 2\maps\Grundorf" --scene-3d
   ```
   The first line must be `scene: incomplete — …` and the `asset roots` line must name
   the installation root and its `Texture` folder. No named placement anchor exists for
   this task, so the visual check is: whatever *is* listed as instanced appears in the
   expected area at its claimed height and heading, and any building that is absent is
   named by a diagnostic with its reason. Scrambled O3D meshes are expected to be absent
   and classified as `unsupported format` until Nymeria decodes them; see
   [OMSI_SCENERY_06_2_FINDINGS.md](OMSI_SCENERY_06_2_FINDINGS.md).
6. Move with `W/A/S/D`, rise with `Space`, descend with `Ctrl`, speed up with `Shift`,
   look with the right mouse button held, reset with `R`, resize the window, and close
   it normally.
7. Check that the summary explains everything missing: each of `placements`,
   `definitions`, `meshes`, `textures`, `subsets`, `placement skips` must break a total
   down by class, the keyword census must name the unread vocabulary with counts, and
   each listed diagnostic must carry its class and, when it repeated, an `(xN)`
   counter. A missing asset is a diagnostic, never a substituted box.
8. Confirm the scene stays `incomplete` and never prints `scene: complete`, and that the
   upload line reports GPU failures as `dropped by a GPU failure` rather than folding
   them into a generic count.
9. If objects sit too high, too low, mirrored, or rotated, record which one and in what
   way: height semantics, the basis change, and the pitch/bank convention are each one
   clearly isolated expression.
10. Optionally repeat on Berlin-Spandau as a bounded stress check. Incomplete scenery
    there is expected and is not a failure as long as the counts say so.

Useful diagnostic runs:

```bat
rem Budget the run and exit on its own
... --map "<path>\OMSI 2\maps\Grundorf" --scene-3d --smoke

rem Terrain only, for comparison
... --map "<path>\OMSI 2\maps\Grundorf" --terrain-3d
```

## Known limitations

- The scene is a bounded prefix of placements, not the whole map.
- **Scrambled O3D meshes cannot be decoded.** Most stock O3D files store their vertex
  records scrambled with a key such as `0x00000000`, and the pinned engine's decoder
  accepts only `0xFFFFFFFF`. Every such mesh is classified as `unsupported format` with
  the reason in the message. Decoding them needs a change in Nymeria; see
  [OMSI_SCENERY_06_2_FINDINGS.md](OMSI_SCENERY_06_2_FINDINGS.md).
- Splines and roads, vehicles, scripts, animation, collision, night lighting, shadows,
  water, terrain holes, and ground textures are not implemented.
- There is no frustum or distance culling: every loaded instance of every prepared model
  is submitted each frame, which is acceptable for a 2,000-instance diagnostic scene
  and must change before the whole map is streamed.
- Alpha-blended materials are drawn with an alpha test instead of blending, so a
  genuinely semi-transparent window shows fully opaque or fully absent.
- `.x` models whose material names are labels rather than texture file names can show
  as missing textures.
- No Windows runtime verification has been performed.

## What this unblocks for roads and splines

The next task needs the same three things this one built, plus two it does not have:

1. **Spline geometry.** `[spline]` records are already parsed, but a `.sli` definition
   describes a profile and texture pattern that must be swept along a curve; that
   tessellation does not exist yet, and the placement record's gradients, cant, skew,
   and texture offset are parsed but uninterpreted.
2. **Ordering and blending.** Roads need correct multi-textured sweeps and often blended
   or masked edges, which the single-material, alpha-tested pipeline cannot express.
3. **Reuse, which now exists:** safe content-root resolution, the definition-config
   reader, the engine mesh/texture decoders with caching, the terrain-relative height
   sampler, and the depth-tested scene pass are all directly reusable.

The important blocker is therefore curve tessellation and multi-material road drawing,
not asset plumbing.

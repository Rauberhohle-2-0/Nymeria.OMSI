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
  (`C:\...`), contains `.` or `..`, has an empty component (doubled separator), a `:`
  inside a component, or a control character is **rejected** before the filesystem is
  touched. Nothing is repaired or guessed.
- Each component is matched against a real directory entry **case-insensitively**, so a
  Windows-authored path resolves on a case-sensitive filesystem. Two entries that
  differ only in case are reported as ambiguous rather than chosen arbitrarily.
- A directory with more than 20,000 entries is reported instead of scanned, which
  bounds the cost of a hostile or damaged layout.
- Because only normalized components are combined with a directory, and every
  intermediate path is required to stay under the root, a resolved file cannot escape
  the authorized content tree.

The installation root is derived only from a map folder whose parent directory is
named `maps`, case-insensitively. No Steam, Aerosoft, or default path is ever assumed.

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
| `[model]` | The first parameter is read as a further configuration file when it ends in `.cfg` or `.sco`; otherwise the `.sco`'s own inline model sections are used |
| `[mesh]` | A mesh reference, resolved relative to the model folder, then the installation root |
| `[matl]` | A texture name (first parameter); additional parameters, including the material index, are not interpreted |
| `[absheight]` | Standalone flag: the placement height is absolute, not terrain-relative |

`[groups]`, `[friendlyname]`, `[description]`, and similar user-interface metadata are
recognized and deliberately unused. Every other keyword — scripts, animation,
interaction, collision, advanced material effects — is counted, and the first eight
distinct spellings are named in the diagnostics. No meaning is invented for them.

**Documented versus assumed.** The keyword-block syntax, the switch-off markers, the
apostrophe comment, and `[absheight]` are documented (openOMSI `docs/FORMATS.md` and
the OMSI WebDisk configuration-file guide, both inspected on 2026-10-22). The `[model]`
indirection itself is **not** documented in the sources that were checked: whether a
real object points at a `model.cfg` or writes `[mesh]` inline is not verified. The
reader therefore accepts both forms, records which one the file used, and reports it,
so the owner's Windows run can confirm the reading from the loaded-mesh counts instead
of trusting an assumption.

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
- Material names come from the mesh itself. A subset whose material name matches a
  declared `[matl]` entry uses that texture; otherwise the name is resolved as a
  texture reference in the model folder, the installation root, and `<root>\Texture`.
  This is the name-based rule OMSI meshes use, and it means `.x` models whose material
  names are arbitrary labels (rather than texture file names) can show as missing
  textures. That limitation is reported, not hidden.
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
GPU texture. Two hundred instances of one building therefore cost one decode, one
upload, and two hundred model-matrix pushes. Nothing is decoded or uploaded per frame;
a frame pushes two matrices per instance and issues one draw per subset.

**Bounds.** Every limit is a constant with a counted, reported consequence:
2,000 instances, 256 definitions, 512 mesh files, 512 textures, 4,000,000 decoded
vertices, 64 meshes and 256 `[matl]` entries per definition, 512 KiB and 20,000 lines
per configuration file, 40 individually reported problems.

**Failure handling.** A model that cannot be decoded is dropped with a count. A model
that fails to upload is dropped with a count and a console line, so one broken asset
cannot take the window down. Only a scene where nothing at all could be prepared fails
creation, because then the caller must be told the view would be empty.

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

The summary prints measured numbers only, and distinguishes them from the *limits*:

```text
placement budget: 2000 instances, 256 definitions, 512 meshes, 512 textures
placements: N considered, M instanced, K reference(s) resolved
assets: D definition(s), F mesh file(s), T texture(s) decoded, U subset(s) without a usable texture
geometry: V vertices, S drawable subset(s) across the distinct definitions
skipped: R unresolved reference(s), D definition(s) without drawable geometry,
         T without decoded terrain, X invalid transform(s), L beyond a load limit
```

The per-run upload summary is printed after the window closes:

```text
scenery upload: 137 instance(s), 402 subset(s), 68 mesh(es), 91 texture(s), 0 not drawable
```

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
| `[model]` points at a further configuration file | **Not verified**; both forms are accepted and reported |
| Material-name to texture matching, and the `<root>\Texture` fallback | **Inferred**; model-folder-first is the documented asset-folder rule |
| Pitch/bank order and sign | **Assumed**; documented only for heading |
| Alpha test threshold 0.5 for all scenery | **Assumed**; per-material thresholds are not interpreted |
| OMSI grid orientation, terrain relief, and object placement on Windows | **Unverified** until the owner runs it |

## Windows validation checklist

1. Update the repository and the pinned submodule if its commit changed, then build:
   `dotnet build Nymeria.OMSI.slnx -c Release`.
2. Confirm `--map` and `--world` still parse Grundorf.
3. Confirm `--map <Grundorf> --render` still shows the diagnostic plan.
4. Confirm `--terrain-3d` still draws terrain without scenery.
5. Run `--scene-3d` and check that at least one **identifiable real object** — a known
   building, tree, or street lamp — appears at its correct place and orientation, and
   that its texture is the real one:
   ```bat
   src\Nymeria.OMSI\bin\Release\net10.0\Nymeria.OMSI.exe --map "<path>\OMSI 2\maps\Grundorf" --scene-3d
   ```
6. Move with `W/A/S/D`, rise with `Space`, descend with `Ctrl`, speed up with `Shift`,
   look with the right mouse button held, reset with `R`, resize the window, and close
   it normally.
7. Read the summary: `placements`, `assets`, `skipped`, and the listed diagnostics must
   explain anything missing. A missing asset is a diagnostic, never a substituted box.
8. If objects sit too high, too low, mirrored, or rotated, record which one and in what
   way: height semantics, the basis change, and the pitch/bank convention are each one
   clearly isolated expression.
9. Optionally repeat on Berlin-Spandau as a bounded stress check. Incomplete scenery
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

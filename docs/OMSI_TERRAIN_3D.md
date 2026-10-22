# OMSI 3D terrain rendering

This document is the authoritative description of the 3D terrain mode added by
Task 05: how OMSI terrain heights are decoded, how they become geometry, how that
geometry is drawn through the pinned Nymeria engine, and how the owner validates it.

**Status:** implemented, built, and covered by headless tests on macOS. Validated
against real OMSI terrain: `NOT RUN (owner validation pending)`. Windows runtime:
`NOT RUN (owner validation pending)`.

## Scope

Task 05 answers *what is the ground of this map shaped like, and can I walk through
it in three dimensions?* It decodes each tile's terrain sidecar, generates a mesh per
tile, and draws those meshes in a perspective view with a free-fly camera.

The view is **terrain only**. It is not an OMSI scene: scenery objects, splines,
roads, water, terrain holes, and ground textures are not part of it. The window title
and the console summary both say so.

| Read and drawn today | Not read or not drawn today |
| --- | --- |
| `<tile>.map.terrain` heights of every discovered tile | OMSI ground textures (`[groundtex]`), light maps, water planes |
| Tile position and edge from the tile file name and `global.cfg` | Scenery object or spline geometry, and any asset under a content root |
| Height-based diagnostic colour and one fixed light direction | OMSI's lighting model, terrain holes, weather, and time of day |

## Reference and provenance

- Reference documentation: [openOMSI](https://github.com/openOMSI-org/openOMSI),
  `docs/FORMATS.md` at commit
  [`3c51d09fdb4cb437fab228ea785b759d1c6331e6`](https://github.com/openOMSI-org/openOMSI/blob/3c51d09fdb4cb437fab228ea785b759d1c6331e6/docs/FORMATS.md),
  section "Map - units `mc_mapclass`, `mc_terrain_2`, `mc_chrono`", inspected on
  2026-10-22. The same commit is the source Task 03 cites.
- **License note:** openOMSI is licensed under AGPL-3.0. Only its prose
  documentation was read. No openOMSI source code, parser structure, shader, or asset
  was copied, and no text from that documentation is reproduced here. Implementing
  from documented file-format facts is a provenance and license question the
  maintainer must review before this repository ships beyond its own development.
- No OMSI installation and no real terrain file exist on the development machine.
  Nothing here was validated against real data.

## The terrain sidecar format

`<tile>.map.terrain` sits beside `<tile>.map` in the map folder and holds the tile's
ground as a regular square grid:

```text
u32   60                 header: cells per tile edge (documented)
f32   height[row][column]  61 x 61 heights, row-major, little-endian
```

- `4 + 61 * 61 * 4 = 14,888` bytes. This is the exact size Task 03 observed on the 19
  Grundorf terrain files, and the size its loader warns about when a file differs.
- The header is not decoration. Sixty is the number of cells along one edge, so 61
  samples sit on the tile's boundaries and neighbours are `tileEdge / 60` apart, which
  is exactly 5 m for the documented 300 m tile. A file with another header is rejected
  instead of being read at the wrong spacing.
- The tile edge comes from the same reference section: 300 m for a metre map, and the
  reference-measured 371.9 m for a `[worldcoordinates]` map (1/300 degree). The two
  layers that place geometry, the Task 04 plan and this terrain mode, read the edge
  from one place ([OmsiTileGrid](../src/Nymeria.OMSI/World/OmsiTileGrid.cs)).

### Documented versus assumed

The task's own warning applies: a file size alone does not establish an encoding.
What is documented, and what this implementation had to assume, are kept apart.

| Property | Value used | Basis | Verified against real files |
| --- | --- | --- | --- |
| Header value | 60 | Reference | No |
| Grid size | 61 x 61 samples | Reference | No |
| File length | 14,888 bytes | Reference, and Task 03's observed size | Observed size only |
| Sample spacing | `tileEdge / 60` (5 m at 300 m) | Derived from the 61 samples and the 60-cell header | No |
| Byte order | Little-endian | Assumed: a Windows-authored `f32` file | No |
| Height unit | Metres | Object `z` and spline `height` are metres in the reference; assumed identical for terrain | No |
| Row/column orientation | Row 0 = tile local `y = 0`, column 0 = tile local `x = 0` | Assumed: no reference states it | No |
| Height meaning | Absolute elevation, not a delta | Assumed from the reference's terrain-relative object `z` | No |

The orientation and the absolute-elevation assumption are the real open questions.
They cannot be settled on the development machine: they need one run against a map
the owner can compare with OMSI itself. Both the summary line and this table report
them as unverified rather than presenting them as fact. If the owner's run shows the
ground mirrored along one axis, the fix is the two index expressions in
[TerrainMeshBuilder](../src/Nymeria.OMSI/Terrain/TerrainMeshBuilder.cs), not a change
to the decoder.

## Decoding

[OmsiTerrainDecoder](../src/Nymeria.OMSI/Terrain/OmsiTerrainDecoder.cs) accepts only
the documented layout and reports everything else:

- the payload must be exactly 14,888 bytes,
- the header must equal 60,
- every height must be finite and within ±10,000 m, which rejects a file whose bytes
  are some other encoding,
- the first failure names the sample by row and column, or the payload by its size,
  and rejects the whole tile.

[OmsiTerrainSet](../src/Nymeria.OMSI/Terrain/OmsiTerrainSet.cs) walks the tiles the
Task 03 parser discovered and decodes each sidecar:

| Situation | Result |
| --- | --- |
| Tile has no sidecar | Warning; the tile contributes no geometry |
| Sidecar is missing, unreadable, or the wrong size | Warning naming the file; no geometry for that tile |
| Sidecar has another header, a non-finite height, or an implausible height | Warning naming the file and the sample; no geometry for that tile |
| More than 1,024 usable sidecars | Warning; the extra tiles contribute no geometry |

Nothing is substituted for a rejected or missing tile: an absent ground is reported as
absent rather than filled with a placeholder. Decoding reads only files beside an
already discovered tile, writes nothing, and uses no SDL3 or GPU type, so it is
unit-tested without a window or a device.

## Geometry

[TerrainMeshBuilder](../src/Nymeria.OMSI/Terrain/TerrainMeshBuilder.cs) turns one
decoded grid into one mesh, and [OmsiTerrainModel](../src/Nymeria.OMSI/Terrain/OmsiTerrainModel.cs)
concatenates the per-tile meshes into one vertex buffer and one index buffer:

- 3,721 vertices and 7,200 triangles per tile; 32-bit indices, because the merged
  buffer of a large map exceeds 65,535 vertices.
- Samples are placed at
  `x = tileColumn * edge + column * spacing`, `y = height`,
  `z = -(tileRow * edge + row * spacing)`.
  World +Y is up, matching the engine camera; the horizontal axes are the map plane
  the diagnostic plan already uses. Negating the OMSI y axis keeps the mapping rigid,
  so the terrain is never mirrored, and increasing tile and grid rows go away from a
  camera that looks down its own -z axis. The compass direction of the OMSI axes is
  not asserted anywhere, exactly as in the plan view.
- Adjacent tiles share an edge exactly: the lower tile's last row and the upper tile's
  first row land on the same world coordinates, and a unit test compares them sample
  by sample.
- Indices are wound counter-clockwise seen from above.
- Normals come from central differences of the decoded heights, with one-sided
  differences on a tile's own edge so no tile borrows the opposite edge's height. A
  consequence, reported rather than hidden: where two tiles meet, the two meshes can
  compute slightly different normals for the same edge sample, so a lit seam may show
  a faint shading step. The positions are continuous; only the shading normal differs.
- The colour ramp is diagnostic. It maps a height normalized over the whole loaded
  range to blue, green, brown, and near-white, so relief is readable without a
  texture. It is explicitly not an OMSI ground texture.

Geometry is generated once, before the window opens. Nothing is rebuilt per frame.

## Rendering

[TerrainPresenter](../src/Nymeria.OMSI/Rendering/TerrainPresenter.cs) is host-boundary
code, like the Task 04 presenter: it uses the `SDL_GPUDevice*` the engine exposes for
exactly this purpose and records its own pass. It creates no renderer abstraction and
no second graphics framework.

- One graphics pipeline: one interleaved vertex buffer (position, normal, colour as
  three `Float3` attributes at offsets 0, 12, and 24), 32-bit indices, triangle list,
  depth test `Less` with depth writes.
- Both triangle orientations are rasterized (`CullMode.None`), matching every renderer
  in the pinned engine. The terrain is an open surface seen from above, so culling
  adds nothing and the depth test is what resolves occlusion. The counter-clockwise
  winding is nevertheless generated and unit-tested, so culling can be switched on
  later together with GPU validation instead of by guess.
- A depth texture (`D32Float`, then `D24Unorm`, then `D16Unorm`, whichever the backend
  supports) is created at the drawable size and recreated only when that size changes.
- Vertex and index buffers are uploaded once through `GpuDevice.CreateBuffer`; the
  only per-frame GPU write is the 64-byte view-projection uniform pushed for the
  vertex stage, followed by one indexed draw.
- Shading is a fixed-direction diffuse term over the vertex colour. It is diagnostic
  shading, not OMSI's lighting.

### Shader assets and the DXIL build step

The engine ships no shader family for this repository and its examples own theirs, so
Task 05 adds this repository's own shaders and its own reviewed build step:

| File | Purpose |
| --- | --- |
| [src/Nymeria.OMSI/Shaders/Terrain.hlsl](../src/Nymeria.OMSI/Shaders/Terrain.hlsl) | Direct3D 12 source, compiled to DXIL during a Windows build |
| [src/Nymeria.OMSI/Shaders/Terrain.metal](../src/Nymeria.OMSI/Shaders/Terrain.metal) | Metal Shading Language source, compiled by SDL when the shaders are created |
| [src/Nymeria.OMSI/Shaders/DxilShaders.targets](../src/Nymeria.OMSI/Shaders/DxilShaders.targets) | Resolves or provisions the DirectX Shader Compiler, compiles both stages, and leaves the bytecode for embedding |

The names follow the engine's `ShaderProgram` convention, which is the only thing that
knows them: the Metal path reads one `Nymeria.OMSI.Shaders.Terrain.metal` resource
with the entry points `vertex_main` and `fragment_main`, and the Direct3D 12 path reads
`Nymeria.OMSI.Shaders.Terrain.vert.dxil` and `...frag.dxil` with `VSMain` and
`PSMain`. No format is ever relabelled as another; `GpuDevice.CreateShader` rejects a
mismatch.

- DXIL generation is gated on a Windows build, so macOS keeps its "no shader
  compiler" property and never downloads anything.
- On Windows the compiler is Microsoft's DirectX Shader Compiler, pinned to one
  release and verified by SHA-256 before it is extracted or run. `-p:DxcPath=...`
  overrides it, and a `dxc` already on `PATH` is used as-is. The cache lives outside
  the repository and needs no administrator rights.
- Compiled bytecode is never committed; it is written under the intermediate output
  directory and embedded as a resource.

**Verification gap, stated plainly:** the HLSL has not been compiled on macOS, because
this build step only runs on Windows. `PASS` for the shader stage requires the
Windows build, and `NOT RUN (owner validation pending)` applies until then. The Metal
source and its resource name are covered by a headless test that reads the embedded
resource and checks the entry points and the uniform buffer slot.

## Camera and controls

A free-fly camera drives the engine's `Camera`, so the view and projection matrices
stay the engine's:

| Input | Action |
| --- | --- |
| `W` / `S` | Move forward / backward along the horizontal view direction |
| `A` / `D` | Strafe left / right |
| `Space` / left `Ctrl` | Rise / descend along world up |
| Left or right `Shift` | Move four times faster |
| Hold the right mouse button, move the mouse | Look around |
| `R` | Return to the initial view |
| Window close button or `Alt+F4` | Exit |

- The initial view is derived from the loaded terrain bounds: the camera starts just
  outside the terrain and above its highest point, looking at its centre, with the far
  plane set from the terrain diagonal so a large map is not clipped away.
- Movement is scaled by elapsed seconds, so the view travels the same distance per
  real second at any frame rate. A frame longer than 0.1 s counts as 0.1 s, so a stall
  or a debugger pause cannot throw the camera across the map.
- Camera controls are host-boundary input mapping over SDL3's cached state, exactly
  like the Task 04 pan and zoom. They are not a reusable input system, and the engine
  publishes no input layer to reuse.

## Command line

```text
Nymeria.OMSI --map <directory> --terrain-3d
```

- The directory is the map folder that directly contains `global.cfg`, as for `--map`
  and `--world`. No installation is searched for.
- `--terrain-3d` requires `--map`. It cannot be combined with `--render` (the 2D
  diagnostic plan) or `--world` (headless placement inspection), because those are
  different views of different data.
- `--terrain-3d` **adds** a mode; it changes no existing behavior. `--map` alone,
  `--map --render`, `--world`, `--smoke`, `--help`, and the plain bootstrap window all
  keep their Task 01 to Task 04 behavior.
- `--smoke` budgets the run, so `--map <directory> --terrain-3d --smoke` presents 120
  frames and exits without input.

The console summary states what the window shows before the window opens:

```text
terrain: 3D terrain only (OMSI scenery, splines, and ground textures are not rendered)
map: /path/to/Maps/Example
name: Example
coordinate grid: 300 m tiles (documented)
terrain tiles: 2 decoded of 2 discovered (0 without a sidecar, 0 rejected)
height grid: 61 x 61 samples per tile, 5 m apart (documented layout, orientation unverified)
heights: -1.25 m to 12.5 m
geometry: 7442 vertices, 14400 triangles, 1 draw call
extent: 300 x 600 m, centre (150, -300)
shading: fixed-direction diffuse light over a height colour ramp (diagnostic, not an OMSI ground texture)
terrain diagnostics: none
controls: W/A/S/D move, Space/Ctrl up/down, Shift faster, hold the right mouse button and move the mouse to look, R resets the view, close the window to exit
```

A run that decodes no height grid prints the diagnostics and exits 1 without opening
a window; it never shows an empty window as if the map had no relief.

## Limits

| Limit | Value | On exceed |
| --- | --- | --- |
| Grid size | 61 x 61 samples per tile | Any other header value rejects that tile's terrain |
| Sidecar length | Exactly 14,888 bytes | Rejected; the tile has no terrain |
| Absolute height | 10,000 m | Rejected; the file is not treated as metre heights |
| Tiles decoded | 1,024 | Warning; further tiles have no terrain in this view |
| Draw calls | 1 per frame | Not applicable: all terrain is in one buffer |

All terrain of one map is resident. There is no terrain streaming, no LOD, and no
per-frame geometry rebuild, and none is claimed.

## Verification

### On macOS (agent)

```bash
dotnet restore Nymeria.OMSI.slnx
dotnet build Nymeria.OMSI.slnx --configuration Release --no-restore
dotnet test Nymeria.OMSI.slnx --configuration Release --no-build --no-restore
dotnet format Nymeria.OMSI.slnx --verify-no-changes --no-restore
```

The tests cover, with synthetic byte arrays and synthetic map folders written by the
tests themselves:

- decoding of the documented layout, the exact file length, and the 5 m sample
  spacing,
- rejection of another header value, a truncated payload, a non-finite height, and an
  implausible height, each with a diagnostic naming the sample,
- row-major round-tripping of a distinct value at every sample,
- the terrain set's missing, rejected, and `[worldcoordinates]` cases,
- vertex and triangle counts, counter-clockwise winding of every triangle, tile
  placement and 5 m spacing, elevation transfer, upward normals on flat ground, sloped
  normals, and edge continuity between two neighbouring tiles,
- index offsets and bounds of the merged model, the blittable 36-byte vertex layout,
  the colour ramp's endpoints and clamping,
- camera framing, frame-rate independence, the long-frame clamp, reset, and mouse look,
- the console summary's wording, and the embedded Metal resource's name and entry
  points.

**These tests are not OMSI content and their passing is not evidence of Grundorf
compatibility.** The host executable is not run on macOS: the window, the GPU pipeline,
the shader compilation, and the draw itself are unexercised here.

### On Windows (owner)

This is the only validation of actual terrain decoding against real data, of the
Direct3D 12 pipeline, of the DXIL shaders, and of the rendered picture.

```powershell
dotnet restore Nymeria.OMSI.slnx
dotnet build Nymeria.OMSI.slnx --configuration Release --no-restore
dotnet test Nymeria.OMSI.slnx --configuration Release --no-build --no-restore

$map = 'C:\Program Files (x86)\Steam\steamapps\common\OMSI 2\maps\Grundorf'
src\Nymeria.OMSI\bin\Release\net10.0\Nymeria.OMSI.exe --map $map --terrain-3d
```

Expect the console summary first, then a window titled
`Nymeria.OMSI — Grundorf — 3D terrain only`. The build prints one line naming the
DirectX Shader Compiler it provisioned or found, and downloads it once per machine.

What to check:

1. `terrain tiles:` reports the same number as the `.map.terrain` files in the folder,
   and `rejected` is 0. A non-zero `rejected` is a finding, not a silent acceptance:
   report the sidecar size and the diagnostic line.
2. `height grid:` reports 61 x 61 samples and 5 m. `heights:` shows a plausible range
   for Grundorf (roughly flat, tens of metres at most).
3. The window shows a continuous shaded surface, not a black frame, not a single
   triangle, and not a flat plane at a constant height.
4. Elevation is visible: the map's embankments and cuttings read as relief.
5. The surface is continuous across tile borders: no gaps, no overlaps, no obviously
   flipped patches.
6. Compare the shape against OMSI's own editor or minimap. **Orientation is the open
   question:** confirm whether the ground is mirrored along one axis. If it is, report
   which axis and how you can tell; that is the one expected finding this task cannot
   resolve locally.
7. `W/A/S/D`, `Space`/`Ctrl`, `Shift`, the right mouse button, and `R` all respond,
   and the movement feels the same on a slow and a fast frame. Resize the window; the
   picture must not stretch or lose depth.
8. Closing the window prints `frames presented:`, `window closed by the user`, and
   `shutdown: clean` with exit code 0.

Then repeat without a human at the keyboard:

```powershell
src\Nymeria.OMSI\bin\Release\net10.0\Nymeria.OMSI.exe --map $map --terrain-3d --smoke
```

Expected: 120 frames, `terrain run complete`, `shutdown: clean`, exit code 0.

Also confirm that the Task 01 to Task 04 paths still behave: `--smoke`,
`--map <directory>`, `--world <directory>`, `--map <directory> --render`, and
`--help`.

Report back with the command, exit code, full console output, Windows version, GPU
and driver, which map folders you used, whether the terrain looked complete, and the
orientation finding. If a real file shows a layout this decoder does not support, the
diagnostic names the file and the reason; report it rather than editing the map.

## Deliberately not implemented

- OMSI ground textures, terrain holes, water, and light maps.
- Scenery object meshes, spline geometry, or any asset loading and reference
  resolution.
- Advanced lighting, dynamic shadows, and weather.
- Terrain streaming, LOD, or a quadtree.
- A second rendering engine, a renderer abstraction, or an engine fork.
- Vehicles, physics, scripts, timetables, and audio.

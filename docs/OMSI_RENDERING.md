# Diagnostic map rendering

This document is the authoritative description of the render mode added by Task
OMSI-04. It records what the window actually shows, how map coordinates become
window pixels, why the first milestone draws a diagnostic plan rather than OMSI
scenery, and how the owner validates it on Windows.

**Status:** implemented and build-verified on macOS. Executed against a real OMSI
installation and a real GPU backend on Windows: `NOT RUN (owner validation
pending)`.

## What the window shows

`--map <directory> --render` opens the native Nymeria window and draws a
**diagnostic plan of the parsed map**:

- one filled square per discovered tile, with a one-pixel outline;
- one small marker per parsed object placement;
- one small marker per parsed spline placement.

That is all. There is **no** terrain surface, no object mesh, no spline curve, no
texture, no lighting, and no height. The window title and the console summary both
say so, and the console prints:

```text
render: diagnostic tile plan (OMSI scenery and terrain are not rendered)
```

The plan is a truthful picture of parser output: a marker means a record was
parsed, and its position means the record's documented x/y. It is **not** a picture
of Grundorf, and it must not be described as one.

### Why not real geometry yet

Nymeria publishes no scene renderer, no mesh upload path, and no shader family. The
GPU renderers and shaders in the engine live under `examples/`, are example-owned,
and cannot be copied into this repository. Direct3D 12 needs precompiled DXIL, which
would require provisioning the DirectX Shader Compiler here — a dependency decision,
not an implementation detail.

The world layer of OMSI-03 also produces no meshes: it yields placements and
references, and terrain heights are deliberately not decoded. Rendering actual
scenery therefore needs three separate, reviewable pieces of work:

1. an approved engine-level shader family (or a reviewed shader build step here),
2. an asset loader that decodes `.sco`/`.o3d`-referenced meshes and resolves
   references under the content root,
3. terrain height decoding and a terrain mesh.

Task OMSI-04 does none of those. It connects the parser to the application host and
makes the parsed data visible, which is the first vertical slice.

## How the frame is produced

```text
OmsiWorldLoader ──► OmsiWorldModel ──► OmsiMapPlan            (pure geometry)
                                         │
                            MapPlanView  │  centre + metres per pixel
                                         ▼
                              MapPlanRasterizer ──► MapPlanImage (RGBA8, CPU)
                                         │
                                         ▼
                       DiagnosticMapPresenter: GpuDevice.CreateTexture
                                         │
                       SDL_BlitGPUTexture into the swapchain texture
```

- **No shader asset, pipeline object, or shader compiler** is involved. The image is
  uploaded through the engine's public `GpuDevice.CreateTexture`, and the frame is
  presented with SDL's own blit command, which carries SDL's internal blit pipeline.
  The pinned native SDL3 (`SDL3-CS` 3.4.18) exports `SDL_BlitGPUTexture` for both
  `osx-arm64` and `win-x64`; that was checked in the package, not assumed.
- The blit preserves the image's 16:9 aspect ratio and letterboxes or pillarboxes the
  remainder, so a resized window does not stretch the plan.
- The image is 1280 x 720 pixels and independent of the window size. It is
  re-rasterized only when the view changes, and the old texture is released then.
- This is host-boundary code, like the clear frame presenter: the engine exposes the
  native device handle for exactly this purpose. It is not a rendering abstraction
  and must not become a second graphics framework.

The render path is therefore a deliberate, documented use of the engine's public
surface and SDL's blit command. If a later task needs real geometry, the missing
capability is an engine-level shader family; that is a separate, approved task.

## Coordinate mapping

The plan uses OMSI's own map plane, in metres, with no rotation and no unit change.

| OMSI input | Plan value |
| --- | --- |
| Tile column/row from the file name | Tile origin = column/row x tile edge |
| Object/spline `x` (tile-local) | Plan x = tile column x edge + `x` |
| Object/spline `y` (tile-local) | Plan y = tile row x edge + `y` |
| Object `z`, heading, pitch, bank | Read by the parser, **not** used by the plan |
| Terrain heights | Not decoded, not drawn |

Screen mapping:

- plan +x is drawn to the right,
- plan +y is drawn upwards (so a larger OMSI y is higher on screen),
- the image centre is the view centre, and `MetresPerPixel` is the scale.

### Tile edge

| Map kind | Edge used | Status |
| --- | --- | --- |
| Ordinary map | 300 m | Documented: tile edge is 300 m, and a place's global position is tile column/row x 300 m plus the place in the tile |
| `[worldcoordinates]` map | 371.9 m | Reference **measurement** (1/300 degree grid, measured on 271 cross-tile spline links). Reported in the summary as `reference measurement, unverified` |

### What is deliberately not asserted

- **Compass orientation.** The reference documents that a heading is measured
  clockwise from north, but not which of OMSI x and y points north. The plan
  therefore draws OMSI x right and OMSI y up and claims nothing about north, east, or
  a world handedness. No object is rotated, so no handedness conversion is implied.
- **Object `z` semantics.** Documented as terrain-relative except for `[absheight]`
  objects and spline helpers, whose `z` is absolute. Nothing is drawn from `z`.
- **Rotation conventions.** Heading, pitch, and bank stay raw degrees in the model and
  are not applied, because a marker has no orientation to apply them to.

The only conversion implemented is `MapPlanView`, which is pure and unit-tested:

```text
pixelX = (planX - centreX) / metresPerPixel + width / 2
pixelY = height / 2 - (planY - centreY) / metresPerPixel
```

## Content root

A map folder is `<installation>/maps/<map>`, but tile references point into
installation-wide folders. The host derives the installation root only when the map
folder's parent directory is named `maps`, case-insensitively, and treats both `/`
and `\` as separators so a Windows path stays analysable on macOS.

| Map folder | Derived root |
| --- | --- |
| `C:\...\OMSI 2\maps\Grundorf` | `C:\...\OMSI 2` |
| `/games/OMSI 2/maps/Grundorf` | `/games/OMSI 2` |
| `/games/OMSI 2/Grundorf` | none, reported as `(unresolved: ...)` |
| `C:\maps\Grundorf` | none, reported as `(unresolved: ...)` |

No default Steam or Aerosoft path is ever assumed, and no installation is searched.
Because this milestone loads no asset, an unresolved root is reported and rendering
continues; a later asset-loading task must refuse to resolve references without it.

## Controls

| Input | Effect |
| --- | --- |
| `W` / `Up` | Move the view up |
| `S` / `Down` | Move the view down |
| `A` / `Left` | Move the view left |
| `D` / `Right` | Move the view right |
| `+` (`=`) | Zoom in |
| `-` | Zoom out |
| `R` | Reset to the framing that shows the whole map |
| Window close / `Alt+F4` | Exit |

Panning is clamped to the plan plus one screen of slack, so the map cannot be lost.
Zoom is clamped between 0.02 and 50,000 metres per pixel. Rotation and mouse look are
not offered: markers have no orientation, and the engine window wrapper consumes input
events, so this mode reads the keyboard state directly rather than adding an input
subsystem.

## Console summary

Printed before the window opens, deterministic for identical input:

```text
render: diagnostic tile plan (OMSI scenery and terrain are not rendered)
map: /path/to/maps/Example
name: Example
tiles: 12 discovered (12 parsed, 0 unparsed)
coordinate grid: 300 m tiles (documented)
content root: /path/to
placement references: 0 of 4310 resolved (no asset loader in this milestone)
plan: 12 tile square(s), 4096 object marker(s), 214 spline marker(s)
plan exclusions: 0 unusable position(s), 0 beyond the per-category marker limit
unread data: 12 terrain sidecar(s) not decoded, 9 uninterpreted record type(s)
parse diagnostics: none
image: 1280 x 720 pixels, scaled to the window
controls: W/A/S/D or arrows pan, '+'/'-' zoom, R resets, close the window to exit
status: complete
```

`frames presented:` and `shutdown: clean` follow when the window closes, exactly as
in the bootstrap run. A `status: partial` map still renders, and its parse diagnostics
stay available through `--world`.

## Failure behavior

| Situation | Behavior |
| --- | --- |
| No `global.cfg`, unreadable folder, or no usable world data | The parse summary is printed, `render: not started — the map produced no usable world data.` follows, exit code 1, no window opened |
| A readable map with no `tile_x_y.map` files | The parse summary is printed, `render: not started — the map has no discovered tile files` follows, exit code 1, no window opened |
| `.map` tile that cannot be interpreted | That tile is reported and contributes no markers; the rest of the map still renders |
| Marker count beyond 250,000 per category | Extra markers are omitted and counted in `plan exclusions`, never silently dropped |
| Non-finite or absurd placement coordinates | That placement is skipped and counted in `plan exclusions` |
| GPU device or backend unavailable | The existing `Nymeria.OMSI could not initialize or run the Nymeria engine:` message with the engine's reason, exit code 1 |
| Window closed, or `--smoke` frame budget reached | Presenter texture released, device disposed, window destroyed, `shutdown: clean`, exit code 0 |

`--render` cannot be combined with `--world`; `--render` without `--map` is rejected
with an actionable usage error and exit code 2. `--map` alone is unchanged and still
creates no window or device.

## Limits

| Limit | Value | Reason |
| --- | --- | --- |
| Markers per category | 250,000 | Bounds rasterizer work and memory on a malformed map |
| Placement coordinate magnitude | 1,000,000 m | Far beyond any real map; catches a nonsensical tile index |
| Diagnostic image | 1280 x 720 x 4 bytes | Constant cost per view change, far below the engine's texture limits |
| Tile squares | One per discovered tile, no extra cap | Discovery already caps tiles at 10,000 |

## Validation

### On macOS (agent)

```bash
dotnet restore Nymeria.OMSI.slnx
dotnet build Nymeria.OMSI.slnx --configuration Release --no-restore
dotnet test Nymeria.OMSI.slnx --configuration Release --no-build --no-restore
dotnet format Nymeria.OMSI.slnx --verify-no-changes --no-restore
```

The tests cover plan geometry (tile origins, placement offsets, both tile grids,
bounds, skipped and omitted placements, ordering), view math (framing, axis mapping,
round trips, clamped pan and zoom, reset), the rasterizer (tile fill and outline,
marker colors, determinism, clipping, empty map), the aspect fit, content-root
derivation, the render summary, and the `--render` command line.

Their fixtures are synthetic strings written by the tests. **They are not OMSI
content, and a passing test is not evidence that a real map renders.** The host is
not executed on macOS in this task, so the GPU blit itself is not exercised here; it
is covered only by the native-export check below.

Static check performed instead, on the pinned NuGet packages (not the engine):

```bash
nm -gU ~/.nuget/packages/sdl3-cs.macos/3.4.18/runtimes/osx-arm64/native/libSDL3.dylib \
  | grep SDL_BlitGPUTexture
strings ~/.nuget/packages/sdl3-cs.windows/3.4.18/runtimes/win-x64/native/SDL3.dll \
  | grep SDL_BlitGPUTexture
```

### On Windows (owner)

```powershell
dotnet restore Nymeria.OMSI.slnx
dotnet build Nymeria.OMSI.slnx --configuration Release --no-restore

$map = 'C:\Program Files (x86)\Steam\steamapps\common\OMSI 2\maps\Grundorf'
src\Nymeria.OMSI\bin\Release\net10.0\Nymeria.OMSI.exe --map $map --render
```

Checklist:

1. The console summary appears **before** the window, with `asset references: 0 of N
   resolved` and a `status:` line. A `status: failed` map never opens a window.
2. The window opens with a title ending in `diagnostic tile plan`, and the engine line
   reports `direct3d12` and `DXIL` (a `metal` driver on Windows is a reportable
   failure).
3. The plan is visible: tile squares with outlines, yellow object markers, blue spline
   markers, on a dark background. It is letterboxed rather than stretched when the
   window is resized to a non-16:9 shape.
4. `W/A/S/D` and the arrow keys pan continuously; `+` and `-` zoom; `R` returns to the
   whole map.
5. Closing the window prints `frames presented:`, `window closed by the user`, and
   `shutdown: clean`, with exit code `0`. No crash, no hang, no unhandled exception.
6. A deterministic variant that needs no interaction:

   ```powershell
   src\Nymeria.OMSI\bin\Release\net10.0\Nymeria.OMSI.exe --map $map --render --smoke
   ```

   Expected: 120 frames, `render run complete`, `shutdown: clean`, exit code `0`.
7. Repeat for a map that declares `[worldcoordinates]` (Berlin-Spandau) and confirm
   the summary reports `1/300 degree, 371.9 m tiles (reference measurement,
   unverified)` and the plan still frames correctly.
8. Confirm afterwards that nothing under the OMSI installation was created, modified,
   or deleted; the render path reads no asset at all.

Report back with the command, exit code, the console summary, the window title and
engine line, what the plan looked like (tile count and whether markers were visible),
whether the frame loop stayed responsive while panning, and how the window closed. A
black window with a clean shutdown is a failure to report, not a pass.

## Deliberately not implemented

- OMSI scenery, object meshes, textures, materials, and spline curves.
- Terrain heights, terrain meshes, light maps, water, and terrain holes.
- Asset loading, reference resolution, and any `.sco`/`.o3d`/`.sli` decoding.
- A shader family, a pipeline, a DXC build step, or a second renderer.
- Vehicles, traffic, timetables, scripts, audio, physics, streaming, culling, and
  dynamic shadows.
- Any write to the OMSI installation, and any bundled OMSI asset.

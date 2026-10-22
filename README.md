# Nymeria.OMSI

An independent, cross-platform OMSI-compatible bus simulation built on the
[Nymeria](https://github.com/Rauberhohle-2-0/Nymeria) 3D engine.

> **Current state: Task 05 — real 3D terrain from decoded OMSI heights.**
> The host runs the Task 01 bootstrap (window, engine GPU device, clear frames, clean
> shutdown), reads one explicitly named map folder (`--map`, `--world`), shows a
> **diagnostic plan** of the parsed tiles and placements with
> `--map <directory> --render`, and with `--map <directory> --terrain-3d` decodes each
> tile's `.map.terrain` heights and draws the resulting **3D terrain** in a perspective
> view with a free-fly camera. It renders **no** OMSI scenery, splines, or ground
> textures, and it loads no OMSI asset. Nothing here claims OMSI compatibility, and
> the terrain has not yet been compared with a real map on Windows.

## What exists today

- A minimal host at [src/Nymeria.OMSI](src/Nymeria.OMSI) that consumes the engine's
  real public API: `Nymeria.Platform.Window` for the window and event loop and
  `Nymeria.Graphics.GpuDevice` for the backend and presentation claim.
- A read-only, GPU-free map metadata layer: `global.cfg` decoding and keyword
  parsing, `tile_x_y.map` discovery, a typed manifest, and source-aware
  diagnostics. See [docs/OMSI_MAP_PARSING.md](docs/OMSI_MAP_PARSING.md).
- A read-only, GPU-free tile/world layer: version-aware parsing of `[object]`,
  `[spline]`, and `[spline_h]` records into a typed world model, terrain sidecar
  size checks, and a deterministic bounded summary. See
  [docs/OMSI_WORLD_PARSING.md](docs/OMSI_WORLD_PARSING.md).
- A diagnostic render mode: pure plan geometry, view math, and a CPU rasterizer,
  presented through the engine device as an uploaded texture blitted to the
  swapchain with SDL's own blit command. No shader, pipeline, or asset loader. See
  [docs/OMSI_RENDERING.md](docs/OMSI_RENDERING.md).
- A 3D terrain mode: the documented `u32 (=60)` + 61 x 61 `f32` sidecar layout decoded
  into height grids, one mesh per tile with real positions, normals, and winding,
  drawn depth-tested through the engine device with this repository's own HLSL and
  Metal shaders, and a free-fly camera. Terrain only: no scenery, splines, or ground
  textures. See [docs/OMSI_TERRAIN_3D.md](docs/OMSI_TERRAIN_3D.md).
- A clear-only frame path with no shader assets, and a Windows-only DXIL build step
  for the terrain shaders that provisions and verifies the DirectX Shader Compiler
  once per machine.
- Readable startup diagnostics for missing native SDL3, architecture mismatches,
  missing GPU backends, and initialization failures.
- Headless unit tests for the host's command-line contract, the map metadata layer,
  the tile/world parser, terrain decoding, mesh generation, and the terrain camera.
- The engine pinned as a Git submodule at one commit, with no engine source in this
  repository.
- Enforceable policies: [AGENTS.md](AGENTS.md),
  [GIT_GUIDELINES.md](GIT_GUIDELINES.md), architecture and development docs, a
  pull request template, a commit-attribution check, and CI for macOS and Windows.

## Requirements

- .NET SDK **10.0.402** (pinned by [global.json](global.json); the engine pins the
  same SDK), targeting `net10.0`.
- Git.
- No SDL3 installation and no Metal toolchain. Native SDL3 arrives through NuGet with
  the engine's platform project.
- On Windows, no manual shader toolchain either: the build provisions a pinned,
  checksum-verified DirectX Shader Compiler for the terrain DXIL shaders, once per
  machine. `-p:DxcPath=<path to dxc.exe>` overrides it. macOS never invokes a shader
  compiler.

## Clone and build

The engine is a pinned submodule, so a plain clone is not enough:

```bash
git clone --recurse-submodules https://github.com/Rauberhohle-2-0/Nymeria.OMSI.git
cd Nymeria.OMSI

dotnet restore Nymeria.OMSI.slnx
dotnet build Nymeria.OMSI.slnx --configuration Release --no-restore
dotnet test Nymeria.OMSI.slnx --configuration Release --no-build --no-restore
```

For an existing clone:

```bash
git submodule update --init --recursive
```

Verify the engine pin with `git submodule status` and
`git -C external/Nymeria rev-parse HEAD`. Details, including how to update the pin
deliberately, are in [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

## Run on Windows (project owner)

Runtime validation happens on Windows with Direct3D 12 hardware. It is **not**
performed by the development agent and is **not** part of CI.

```bat
dotnet build Nymeria.OMSI.slnx --configuration Release
src\Nymeria.OMSI\bin\Release\net10.0\Nymeria.OMSI.exe --smoke
```

Expected: a 960 × 540 window appears for about two seconds with a dark clear color,
then the process exits with code 0 and prints:

```text
Nymeria.OMSI bootstrap
host: Microsoft Windows ... (<architecture>)
engine: direct3d12 driver, DXIL shaders, <swapchain format> swapchain
window: 960x540 logical
frames presented: 120
smoke run complete
shutdown: clean
```

Run without arguments for an interactive window; close it and expect
`window closed by the user` followed by `shutdown: clean`.

The full checklist, failure-reporting instructions, and troubleshooting are in
[docs/DEVELOPMENT.md](docs/DEVELOPMENT.md#windows-manual-smoke-test).

## Inspect a map folder (read-only)

Map inspection is a separate command path. It creates no window and no GPU device,
so it works on a machine that cannot present frames:

```bat
src\Nymeria.OMSI\bin\Release\net10.0\Nymeria.OMSI.exe --map "<path>\OMSI 2\maps\Grundorf"
```

The path is always supplied explicitly; no installation is searched for. The
summary reports the encoding that was used, the map name, the tile count with
coordinates, any `global.cfg` directives the parser preserved but did not
interpret, and source-aware diagnostics. `status: complete` exits 0,
`status: partial` exits 0 with the warnings shown, and `status: failed` exits 1.

The supported subset, its documented source, its limits, and the exact Windows
validation procedure are in [docs/OMSI_MAP_PARSING.md](docs/OMSI_MAP_PARSING.md).

## Parse tile data (read-only)

`--world` takes the same folder and goes one level deeper: it parses every
discovered tile's documented placement records into a read-only world model.

```bat
src\Nymeria.OMSI\bin\Release\net10.0\Nymeria.OMSI.exe --world "<path>\OMSI 2\maps\Grundorf"
```

The summary reports the map name, discovered and parsed tile counts, tile versions,
total object and spline placements, terrain sidecar sizes, uninterpreted record
types, source-located diagnostics, and the parse status. Object and spline
references are kept verbatim and are never resolved or loaded, and nothing is
rendered: `status: complete` means the documented record layouts were understood,
not that the map is renderable.

`--world` cannot be combined with `--map` (its summary already includes the
discovery metadata) or with `--smoke`. Supported records, version thresholds, known
limitations, and the Windows validation procedure are in
[docs/OMSI_WORLD_PARSING.md](docs/OMSI_WORLD_PARSING.md).

## Show the diagnostic map plan

`--render` turns the same parse into a window. It requires `--map`:

```bat
src\Nymeria.OMSI\bin\Release\net10.0\Nymeria.OMSI.exe --map "<path>\OMSI 2\maps\Grundorf" --render
```

The window shows one square per discovered tile, one marker per parsed object, and
one marker per parsed spline, on OMSI's own x/y plane with the documented 300 m tile
grid (the reference-measured 371.9 m grid for `[worldcoordinates]` maps). Pan with
`W/A/S/D` or the arrow keys, zoom with `+` and `-`, reset with `R`, and close the
window to exit. `--smoke` budgets the run, so
`--map <directory> --render --smoke` presents 120 frames and exits — useful without a
human at the keyboard:

```bat
src\Nymeria.OMSI\bin\Release\net10.0\Nymeria.OMSI.exe --map "<path>\OMSI 2\maps\Grundorf" --render --smoke
```

The console summary states what the window is: a diagnostic tile plan, not OMSI
scenery. Terrain, meshes, textures, and spline curves are not rendered; asset
references are listed as unresolved. The coordinate mapping, controls, limits, and
the Windows checklist are in [docs/OMSI_RENDERING.md](docs/OMSI_RENDERING.md).

## Show the 3D terrain

`--terrain-3d` decodes the map's actual terrain heights and draws them in 3D. It
requires `--map` and cannot be combined with `--render` or `--world`:

```bat
src\Nymeria.OMSI\bin\Release\net10.0\Nymeria.OMSI.exe --map "<path>\OMSI 2\maps\Grundorf" --terrain-3d
```

The window shows the decoded ground of every tile as one continuous, depth-tested
surface, shaded by one fixed light over a height colour ramp. Move with `W/A/S/D`,
rise and descend with `Space` and `Ctrl`, move faster with `Shift`, hold the right
mouse button and move the mouse to look, press `R` to return to the initial view, and
close the window to exit. `--smoke` budgets the run:

```bat
src\Nymeria.OMSI\bin\Release\net10.0\Nymeria.OMSI.exe --map "<path>\OMSI 2\maps\Grundorf" --terrain-3d --smoke
```

It is terrain only: scenery objects, splines, ground textures, water, and terrain
holes are not rendered, and the window title and console summary say so. The decoder
rejects a sidecar of the wrong size or header instead of guessing, and reports every
missing or unreadable one. The format evidence, the documented-versus-assumed table,
the coordinate mapping, limits, and the Windows checklist are in
[docs/OMSI_TERRAIN_3D.md](docs/OMSI_TERRAIN_3D.md).

## Platform status

| Platform | Build | Headless tests | Runtime (window, GPU, shutdown) |
| --- | --- | --- | --- |
| macOS (development host) | `PASS` — local, macOS 27.0.1 arm64, SDK 10.0.402 | `PASS` — local, 193 tests | `NOT RUN` — intentionally not executed on macOS |
| Windows (owner host) | `NOT RUN` — CI job defined, no run recorded yet | `NOT RUN` — CI job defined, no run recorded yet | `NOT RUN (owner validation pending)` |
| Linux | Not supported | — | Not supported; the engine has no Linux GPU backend |

Map metadata, tile/world parsing, terrain decoding, and terrain mesh generation
against synthetic fixtures run headlessly in the unit tests. The same code against a
real Grundorf installation is `NOT RUN (owner validation pending)`: no OMSI files
exist on the development machine, and no substitute files were created to simulate
them. The Direct3D 12 pipeline and the DXIL shaders are compiled and drawn only on
Windows, so their verification is the owner's run.

The engine itself documents Metal/MSL as validated on macOS and Direct3D 12/DXIL as
implemented but unverified on Windows hardware. This repository inherits that gap;
the owner's Windows run is what closes it.

## Engine dependency

Nymeria publishes no NuGet packages, so the engine is consumed as a Git submodule
pinned to commit `6e727248627692a663b0651f09be2c662ed14681` (the `main` tip at the
time of Task 01), referenced by MSBuild project references to `Nymeria.Platform` and
`Nymeria.Graphics`. The engine stays independently buildable and is read-only from
here. The decision, the alternatives that were rejected, and the upgrade procedure
are recorded in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Documentation

- [docs/OMSI_MAP_PARSING.md](docs/OMSI_MAP_PARSING.md) — supported map metadata,
  encoding strategy, reference and provenance, limits, Windows validation.
- [docs/OMSI_WORLD_PARSING.md](docs/OMSI_WORLD_PARSING.md) — interpreted tile
  records and version thresholds, world model and status semantics, terrain sidecar
  checks, limits, Windows validation.
- [docs/OMSI_RENDERING.md](docs/OMSI_RENDERING.md) — what the diagnostic window
  shows, the OMSI-to-plan coordinate mapping, controls, content-root derivation,
  failure behavior, limits, Windows validation.
- [docs/OMSI_TERRAIN_3D.md](docs/OMSI_TERRAIN_3D.md) — the terrain sidecar format and
  its evidence, documented versus assumed properties, decoding and diagnostics, mesh
  generation and coordinate mapping, the shader and DXIL build step, camera controls,
  limits, Windows validation.
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — ownership boundary, integration
  decision, host lifecycle, build graph, platform backends, limitations.
- [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) — SDK setup, submodule commands, macOS
  verification scope, troubleshooting, Windows smoke test.
- [AGENTS.md](AGENTS.md) — binding rules for coding agents.
- [GIT_GUIDELINES.md](GIT_GUIDELINES.md) — branches, commits, attribution,
  dependency pinning, review, and CI enforcement.

## Next milestone

The immediate next step is the owner's Windows validation of `--map --terrain-3d`
against a legally installed Grundorf: whether the decoded relief matches the map,
whether the surface is continuous across tile borders, and whether the ground is
mirrored along an axis. That run is the only thing that can settle the terrain grid's
orientation, and it is reported as unverified until then.

The 3D terrain mode already established this repository's own shader build step, so
scenery becomes possible, but it is not approved. The narrowest useful follow-up is a
read-only scenery **inventory**: deterministic, unit-tested, and asset-free. Far
scenery rendering still needs mesh decoding (`.o3d`/`.x`), reference resolution under
the content root, and materials and textures; spline tessellation, terrain holes,
vehicle physics, world streaming, and OMSI scripting remain unapproved.

## License

BSD 3-Clause. See [LICENSE](LICENSE).

# Development

This document describes how to work on Nymeria.OMSI, what may and may not be
verified where, and how the project owner validates the executable on Windows.

## Two environments, two responsibilities

Development happens on a **MacBook running macOS**. Runtime validation happens on
the project owner's **Windows PC**, which has the original OMSI installation and
legitimately obtained game assets.

| Activity | macOS (agent/developer) | Windows (project owner) |
| --- | --- | --- |
| Inspect repositories, write code and docs | Yes | — |
| `dotnet restore`, `dotnet build`, formatting/static checks | Yes | Yes (build only) |
| Headless, asset-independent unit tests | Yes | Yes |
| Launch the host (window, GPU, audio) | **No** | Yes |
| Load OMSI files or assets | **No** | Yes, later tasks only |
| GPU/window/audio smoke tests in CI | **No** | **No** |

These are binding boundaries, not preferences:

- Do not run the host application on macOS as part of a task. Do not add GPU,
  window, or audio runtime tests to local automation or hosted CI.
- Do not install OMSI, download proprietary game content, or fabricate mock OMSI
  assets to make a test pass.
- Compilation on macOS or a green CI build is **not** runtime validation. Windows
  runtime behavior stays `NOT RUN (owner validation pending)` until the owner
  reports a result.

Status vocabulary for every reported check:

- `PASS` — executed here and succeeded, on the named platform.
- `FAIL` — executed here and failed, with the error.
- `SKIPPED (environment)` — legitimate, stated reason it cannot run here.
- `NOT RUN (owner validation pending)` — reserved for Windows runtime checks.

Always name the platform. "Builds" is not a platform.

## Prerequisites

- Git.
- .NET SDK **10.0.402**, pinned by [global.json](../global.json). `dotnet --version`
  must report `10.0.402`. `rollForward: latestPatch` allows a later patch of the
  same feature band; no other SDK is tested.
- No SDL3 installation and no Xcode shader toolchain is required. Native SDL3 comes
  from the NuGet packages that `Nymeria.Platform` references.
- On Windows, the terrain DXIL shaders need Microsoft's DirectX Shader Compiler. The
  build provisions a pinned, SHA-256-verified release into a per-user cache the first
  time it is needed, so nothing has to be installed by hand. Set
  `-p:DxcPath="<path>\dxc.exe"` to use a compiler you already have, or put `dxc` on
  `PATH`. macOS never invokes a shader compiler.

The engine is a Git submodule pinned to one commit. It is **not** a branch and it
is not self-updating.

## Checkout

Fresh clone, including the engine:

```bash
git clone --recurse-submodules https://github.com/Rauberhohle-2-0/Nymeria.OMSI.git
cd Nymeria.OMSI
```

Existing clone that is missing `external/Nymeria`:

```bash
git submodule update --init --recursive
```

Verify what is checked out:

```bash
git submodule status
git -C external/Nymeria rev-parse HEAD
```

The second command must print the commit recorded by this repository's gitlink
(`git rev-parse HEAD:external/Nymeria`). A different commit means the submodule
was moved locally and must be reviewed as an engine upgrade, not committed
silently.

### Updating the engine

Do not run `git submodule update --remote`; that follows a moving branch. A
deliberate upgrade is:

```bash
git -C external/Nymeria fetch origin
git -C external/Nymeria checkout <engine-commit>
git status                       # the gitlink change must be reviewed
```

Then update the pinned commit in
[ARCHITECTURE.md](ARCHITECTURE.md#decision), rebuild, run the headless tests, and
commit the pointer change on its own with the old and new commit hashes. See
[GIT_GUIDELINES.md](../GIT_GUIDELINES.md).

## Build, static checks, and tests on macOS

From the repository root:

```bash
dotnet restore Nymeria.OMSI.slnx
dotnet build Nymeria.OMSI.slnx --configuration Release --no-restore
dotnet test Nymeria.OMSI.slnx --configuration Release --no-build --no-restore
```

Notes:

- The solution builds the host, the headless test project, and the two consumed
  engine projects. Engine projects are pinned and read-only.
- Builds are warning-free by policy: `TreatWarningsAsErrors` is enabled in
  [Directory.Build.props](../Directory.Build.props) for this repository's
  projects, and the engine keeps its own settings.
- The test project runs 193 headless tests. They cover command-line parsing, the
  GPU-free map metadata layer (text encoding selection and fallbacks, keyword-block
  tokenizing, tile file-name parsing, discovery ordering, duplicates, unsupported
  directives, summary output), the GPU-free tile/world layer (record layouts by
  tile version, keyword-like and empty labels, truncated records, malformed numbers,
  duplicate identifiers, missing versions, uninterpreted record counting, terrain
  size checks, status transitions, summary truncation), the GPU-free plan layer
  (tile origins and both grids, placement offsets, bounds, skipped and omitted
  placements, view framing and clamping, rasterizer colors and determinism,
  content-root derivation, aspect fit, render summary), and the GPU-free terrain
  layer (documented sidecar length and header, row-major sample order, rejection of
  truncated, non-finite, and implausible data, per-tile vertex and triangle counts,
  counter-clockwise winding, 5 m spacing, elevation transfer, normals, tile-edge
  continuity, merged index offsets, the vertex layout, the colour ramp, camera
  framing and frame-rate independence, and the embedded Metal shader resource). They
  create no window, no GPU device, and no audio device, and they use only synthetic
  strings and byte arrays written by the tests.
- The host itself, including `--map`, `--world`, `--render`, and `--terrain-3d`, is
  **not** executed on macOS in any task. `dotnet build` and the tests prove
  compilation, parser, decoder, and geometry behavior, nothing more. A passing tile,
  plan, or terrain test is not evidence of OMSI/Grundorf compatibility, and it does
  not exercise the GPU blit or draw path, the DXIL shaders, or the pipeline.

Optional formatting check, if `dotnet format` is available in the pinned SDK:

```bash
dotnet format Nymeria.OMSI.slnx --verify-no-changes --no-restore
```

## Native dependencies and shaders

- Native SDL3 is deployed by NuGet through `Nymeria.Platform`:
  `SDL3-CS.MacOS` (`libSDL3.dylib` for `osx-arm64`/`osx-x64`) and
  `SDL3-CS.Windows` (`SDL3.dll` for `win-x64`/`win-arm64`/`win-x86`). Never copy a
  native library by hand, install it system-wide, or add a second SDL3 package
  reference here.
- A portable build keeps every RID under `runtimes/<rid>/native/`; the .NET host
  resolves the matching one. A RID-specific build (`-r win-x64`) copies only that
  binary beside the executable.
- The bootstrap path uses **no shader assets**: a cleared frame is a render pass with
  nothing bound, so no MSL source and no DXIL bytecode is needed.
- The 3D terrain mode uses this repository's own shaders in
  `src/Nymeria.OMSI/Shaders/`: `Terrain.metal` is embedded as source and compiled by
  SDL when the shaders are created, and `Terrain.hlsl` is compiled to DXIL during a
  Windows build and embedded as bytecode. `Shaders/DxilShaders.targets` performs that
  step with a pinned, checksum-verified DirectX Shader Compiler; generated `.dxil`
  files live under `obj/` and are never committed. Nymeria's own DXC provisioning is
  example-owned and is not imported or copied here.
- Any further shader must follow the same route: this repository's own source plus
  that reviewed build step, or an engine-level shader family proposed upstream. Never
  copy example shaders or duplicate the DXC pipeline outside it.

### Troubleshooting

| Symptom | Cause and action |
| --- | --- |
| `DllNotFoundException` mentioning `SDL3` | The native runtime is not in the output. Run `dotnet restore` and `dotnet build` for this solution, and check `runtimes/<rid>/native/` under the output directory. |
| `BadImageFormatException` | Architecture mismatch. Build for the process architecture, e.g. `dotnet build -r win-x64` on an x64 host. |
| `GPU rendering is not supported on Linux` (or another platform) | The engine has no backend policy for that platform. Do not add a fallback; that is an engine decision. |
| `No SDL3 GPU driver ... accepts DXIL` / device creation failure on Windows | Direct3D 12 is unavailable: too old a Windows build, outdated graphics driver, or a machine without D3D12 support. Update the driver; report the full message. |
| Build fails because the SDK version is wrong | Install SDK 10.0.402 or a later patch of that band; `global.json` rejects other versions. |
| `external/Nymeria` is empty | `git submodule update --init --recursive`. |
| `--map` reports `status: failed` and “No global.cfg was found” | The path is not a map folder. Pass the folder that directly contains `global.cfg`, not the `maps` folder above it. |
| `--map` reports `status: partial` | One or more warnings name the file and line. Read them before assuming the map is broken; unexpected warnings are a reportable finding. |
| `--map` reports a Latin-1 fallback warning | The file is not valid UTF-8 and has no byte-order mark. OMSI writes UTF-16LE with a mark, so another tool probably saved it. Text in Windows-1252 bytes `0x80`–`0x9F` will not decode as Windows-1252; see [OMSI_MAP_PARSING.md](OMSI_MAP_PARSING.md#text-encoding). |
| `--world` reports a tile as unparsed | The tile has no usable leading `[version]`. The diagnostic names the tile; check its first lines before assuming a parser bug. |
| `--world` reports a truncated or malformed `[object]`/`[spline]` | The record does not match the documented layout for its declared version. The warning gives the file and line; report it rather than editing the map. |
| `--world` reports a terrain size mismatch | The sidecar is not the documented 14,888-byte layout. Heights are not decoded in this task, so the file is only reported. |
| `--world` reports `status: failed` with “no usable world data” | Not one discovered tile could be interpreted, so there is nothing to describe. Per-tile diagnostics say why. |
| `--render` reports `render: not started` | The map produced no usable world data, so there is nothing to draw. Run `--world` on the same folder for the per-tile diagnostics. |
| `--render` opens a window but it stays empty | The plan is drawn with SDL's blit command, not a shader. Check the console for the engine line and any error; report the window size, the driver line, and whether `plan:` reported any tiles or markers. |
| `--render` reports `content root: (unresolved: ...)` | The map folder is not inside a folder named `maps`. This milestone loads no asset, so rendering still works; a later asset loader will require a resolvable root. |
| Windows build fails provisioning the DirectX Shader Compiler, or `DxcPath` is rejected | The archive could not be downloaded, failed its SHA-256 check, or the path is wrong. Work offline with `-p:DxcPath="<path>\dxc.exe"`, or put `dxc` on `PATH`. Do not disable the checksum check. |
| `--terrain-3d` reports `status: failed` before a window opens | Not one discovered tile could be interpreted, so there is no terrain to place. Run `--world` on the same folder for the per-tile diagnostics. |
| `--terrain-3d` reports `terrain: not started — no tile produced a decoded height grid` | Every sidecar was missing or rejected. The terrain diagnostics name each file and the reason; a file of another size or header is a finding to report, not something to work around. |
| `--terrain-3d` reports rejected sidecars | The decoder only accepts the documented `u32 (=60)` + 61 x 61 `f32` layout. Report the file size, the header value, and the diagnostic rather than editing the map. |
| The terrain window is black or empty | Check the console's `engine:` line and any error. A black frame with `geometry:` reporting triangles is a GPU-side finding: report the driver line, depth format availability, window size, and whether the window was resized. |

## Continuous integration

[.github/workflows/ci.yml](../.github/workflows/ci.yml) defines:

- **Build (macos-latest, windows-latest)** — checks out the repository with
  submodules, sets up the SDK from `global.json`, verifies that the checked-out
  engine commit equals the recorded gitlink and that the engine checkout has no
  local changes, then restores, builds in `Release`, and runs the headless tests.
- **Commit policy (ubuntu-latest)** — checks only the commits introduced by the
  change for attribution trailers. It needs the superproject's full history
  (`fetch-depth: 0`) and does not initialize submodules, so engine history is
  never scanned.

CI proves compilation and headless test results on those runners. It does **not**
open a window, create a GPU device, touch an audio device, or load OMSI content,
and it never runs the host executable. No CI result is a substitute for the
owner's Windows run.

## Windows manual smoke test

Performed by the project owner on the Windows PC. This is the only validation of
actual startup, Direct3D 12 device creation, presentation, and shutdown. Record
results as described at the end.

### Preparation

1. Install the .NET SDK 10.0.402.
2. Clone with submodules, or update an existing clone:
   ```bat
   git clone --recurse-submodules https://github.com/Rauberhohle-2-0/Nymeria.OMSI.git
   cd Nymeria.OMSI
   ```
   Existing clone:
   ```bat
   git submodule update --init --recursive
   ```
3. Build:
   ```bat
   dotnet restore Nymeria.OMSI.slnx
   dotnet build Nymeria.OMSI.slnx --configuration Release --no-restore
   ```
   Expect `0 Warning(s)`, `0 Error(s)`. The first build downloads and verifies the
   pinned DirectX Shader Compiler once, prints a line naming it, and compiles
   `Shaders/Terrain.hlsl` to DXIL; later builds reuse the per-user cache.
4. Confirm the native library is deployed. Either
   `src\Nymeria.OMSI\bin\Release\net10.0\runtimes\win-x64\native\SDL3.dll` exists
   (use `win-arm64` on arm64), or `SDL3.dll` sits beside `Nymeria.OMSI.exe` after a
   RID-specific build.

### Checklist

Run these from the repository root in `cmd` or PowerShell. Steps 1–4 need no OMSI
assets and use none. Step 5 needs the owner's installed map folder and reads only
its configuration metadata.

1. **Smoke run.**
   ```bat
   src\Nymeria.OMSI\bin\Release\net10.0\Nymeria.OMSI.exe --smoke
   ```
   Expected console output, in order:
   ```text
   Nymeria.OMSI bootstrap
   host: Microsoft Windows ... (<architecture>)
   engine: direct3d12 driver, DXIL shaders, <swapchain format> swapchain
   window: 960x540 logical
   frames presented: 120
   smoke run complete
   shutdown: clean
   ```
   Expected exit code: `0`. During the run a 960 × 540 window appears for roughly
   two seconds, filled with a dark blue-gray clear color, then closes by itself.
   - `PASS` only if the `engine:` line names the `direct3d12` driver and `DXIL`
     shaders, at least one frame was presented, `shutdown: clean` is printed, and
     the exit code is 0.
   - A `frames presented:` value below 120 with an otherwise clean run means a
     frame was skipped (for example while minimized); it is acceptable only if the
     window was not visible.

2. **Interactive run and close.**
   ```bat
   src\Nymeria.OMSI\bin\Release\net10.0\Nymeria.OMSI.exe
   ```
   The window appears with the same clear color, resizes without errors, and stays
   responsive. Close it with the window close button, then repeat with Alt+F4.
   Expected final lines:
   ```text
   window closed by the user
   shutdown: clean
   ```
   Expected exit code: `0`, with no `DllNotFoundException`,
   `BadImageFormatException`, or device-creation error at any point.

3. **Backend identity.** Confirm the `engine:` line reports `direct3d12` and
   `DXIL`; a `metal` driver or a "no SDL3 GPU driver" message is a `FAIL`, and the
   full message must be reported.

4. **No manual native setup.** Repeat step 1 from a clean build directory
   (`git clean -xdf -e external` is destructive; prefer deleting
   `src\Nymeria.OMSI\bin` and `obj` only) without placing any DLL by hand. The run
   must still pass.

5. **Read-only map metadata (Task 02).** Point the scanner at the installed map
   folder; the path depends on the installation and must be quoted:
   ```bat
   src\Nymeria.OMSI\bin\Release\net10.0\Nymeria.OMSI.exe --map "C:\Program Files (x86)\Steam\steamapps\common\OMSI 2\maps\Grundorf"
   ```
   Expected: no window opens, `text encoding:` reports `UTF-16LE with byte-order
   mark`, `name:` is non-empty, `tiles:` is greater than zero with coordinates that
   match files on disk, and the exit code is `0`. `status: complete` is the
   expected result; `status: partial` is acceptable only when its warnings are
   understood, and `status: failed` is a `FAIL`.

   Confirm afterwards that no file under the OMSI installation changed (compare
   folder timestamps, or use the installation's own file list). Scanning is
   read-only by design. The full procedure and expected observations are in
   [OMSI_MAP_PARSING.md](OMSI_MAP_PARSING.md#on-windows-owner).

6. **Read-only tile and world parsing (Task 03).** Point the parser at the same map
   folder:
   ```bat
   src\Nymeria.OMSI\bin\Release\net10.0\Nymeria.OMSI.exe --world "C:\Program Files (x86)\Steam\steamapps\common\OMSI 2\maps\Grundorf"
   ```
   Expected: no window opens, `tiles discovered:` and `tiles parsed:` equal the
   number of `tile_x_y.map` files, `objects:` and `splines:` are non-zero, tile
   versions are reported, terrain files at the documented size are counted, and the
   exit code is `0`. `status: complete` is expected; `status: partial` is acceptable
   only when its warnings are understood, and `status: failed` is a `FAIL`.

   Repeat for the other stock maps (for example Berlin-Spandau) to cover more tile
   versions, then run one map twice and confirm identical output. Confirm afterwards
   that nothing under the OMSI installation changed. The full procedure, expected
   observations, and what to report are in
   [OMSI_WORLD_PARSING.md](OMSI_WORLD_PARSING.md#on-windows-owner).

7. **Diagnostic map render (Task 04).** Parse first, then open the window:
   ```bat
   src\Nymeria.OMSI\bin\Release\net10.0\Nymeria.OMSI.exe --map "C:\Program Files (x86)\Steam\steamapps\common\OMSI 2\maps\Grundorf" --render
   ```
   Expected: the console summary appears first and says `render: diagnostic tile
   plan`; the window opens with a title ending in `diagnostic tile plan`; tile
   squares and yellow object / blue spline markers are visible; `W/A/S/D` or the
   arrows pan, `+`/`-` zoom, `R` resets; closing the window prints `frames
   presented:`, `window closed by the user`, and `shutdown: clean` with exit code
   `0`. A black window or a hang is a `FAIL`.

   Repeat without a human at the keyboard:
   ```bat
   src\Nymeria.OMSI\bin\Release\net10.0\Nymeria.OMSI.exe --map "C:\Program Files (x86)\Steam\steamapps\common\OMSI 2\maps\Grundorf" --render --smoke
   ```
   Expected: 120 frames, `render run complete`, `shutdown: clean`, exit code `0`.

   The full checklist, including the `[worldcoordinates]` case, is in
   [OMSI_RENDERING.md](OMSI_RENDERING.md#on-windows-owner).

8. **3D terrain (Task 05).** Decode the same map's terrain and open the 3D view:
   ```bat
   src\Nymeria.OMSI\bin\Release\net10.0\Nymeria.OMSI.exe --map "C:\Program Files (x86)\Steam\steamapps\common\OMSI 2\maps\Grundorf" --terrain-3d
   ```
   Expected: the build has already provisioned the DirectX Shader Compiler once; the
   console summary says `terrain: 3D terrain only` and reports every sidecar as
   decoded with none rejected; the window title ends in `3D terrain only` and shows a
   continuous, shaded, three-dimensional surface rather than a flat plane or a plan.
   `W/A/S/D`, `Space`/`Ctrl`, `Shift`, the right mouse button, and `R` respond, and
   closing the window prints `frames presented:`, `window closed by the user`, and
   `shutdown: clean` with exit code 0. A black frame, a gap between tiles, or a hang is
   a `FAIL`.

   Repeat without a human at the keyboard:
   ```bat
   src\Nymeria.OMSI\bin\Release\net10.0\Nymeria.OMSI.exe --map "C:\Program Files (x86)\Steam\steamapps\common\OMSI 2\maps\Grundorf" --terrain-3d --smoke
   ```
   Expected: 120 frames, `terrain run complete`, `shutdown: clean`, exit code `0`.

   Also compare the terrain's shape and orientation with OMSI. The grid's row
   orientation is an assumption recorded in
   [OMSI_TERRAIN_3D.md](OMSI_TERRAIN_3D.md#documented-versus-assumed), and this run is
   the only way to settle it. Report whether the ground is mirrored along an axis.

   The full checklist is in
   [OMSI_TERRAIN_3D.md](OMSI_TERRAIN_3D.md#on-windows-owner).

   The host's other five steps need no OMSI assets; these four need only the owner's
   legally installed map folder.

### If something fails

Report, in the task report or issue:

- Exact command, exit code (`echo %ERRORLEVEL%` in `cmd`, `$LASTEXITCODE` in
  PowerShell), and the complete console output.
- Windows version and build (`winver`), CPU architecture, GPU model, and graphics
  driver version.
- Whether `SDL3.dll` was present in the output and its path.
- Whether the failure is at startup, during presentation, or during shutdown.
- For `--map` and `--world`: the scanned path, the full summary, every diagnostic
  with its file and line, and the tile versions seen. Include whether the OMSI
  installation was modified.
- For `--render`: the full console summary, the window title, the `engine:` line,
  what the plan looked like (tiles and markers visible or not), whether panning and
  zooming stayed responsive, and how the window closed.
- For `--terrain-3d`: the full console summary, the window title, the `engine:` line,
  the decoded/rejected sidecar counts and every terrain diagnostic, what the surface
  looked like (continuous, gaps, mirrored, flat), whether the camera controls stayed
  responsive and felt frame-rate independent, and how the window closed.

Keep Windows runtime results authoritative: if the owner reports a `FAIL`, the
milestone is not verification-complete even when macOS builds and CI are green.

## Reporting a task result

Every task report lists changed files, the exact commands run with their results
and platforms, known gaps, and a suggested next task. Use the status vocabulary
above, and never describe an unexecuted or unavailable check as passed.

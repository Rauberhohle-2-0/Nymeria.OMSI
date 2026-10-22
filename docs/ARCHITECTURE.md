# Architecture

This document is the authoritative description of how Nymeria.OMSI consumes the
Nymeria engine, what the bootstrap host does, and what it deliberately does not
do. It also records the reconnaissance that produced the integration decision, so
the decision can be re-checked instead of assumed.

Scope of this document: Task 01, repository bootstrap. No OMSI subsystem exists
yet, and this document does not pre-approve one.

## Ownership boundary

Nymeria.OMSI is an OMSI runtime and application built on Nymeria. It is not a
fork, a second rendering engine, or a second platform layer.

| Concern | Owner | Notes |
| --- | --- | --- |
| Window creation, event pumping, SDL3 initialization | Nymeria (`Nymeria.Platform`) | Consumed as a pinned project reference. |
| GPU device, backend selection, presentation claim, shader and texture creation | Nymeria (`Nymeria.Graphics`) | `GpuDevice` picks Metal/MSL on macOS and Direct3D 12/DXIL on Windows. |
| Native SDL3 deployment and its NuGet packages | Nymeria (`Nymeria.Platform.csproj`) | Nymeria.OMSI declares no SDL3 package of its own. |
| Model/format-agnostic data, scene, lighting, audio math, animation | Nymeria (`Nymeria.Core` and `Nymeria.Formats.*`) | Not referenced yet; the bootstrap needs no scene or parser. |
| OMSI file formats, map and configuration parsing, OMSI simulation | Nymeria.OMSI | None of this is implemented yet. |
| Host startup/shutdown, host diagnostics, host-level argument parsing | Nymeria.OMSI | `src/Nymeria.OMSI/`. |

Rules that follow from that boundary:

- Anything generic and reusable (a renderer, an input system, a platform backend,
  a new shader family, a format parser for a non-OMSI format) belongs upstream in
  Nymeria and is proposed as a separate change there. It must not be implemented
  here first and "donated" later.
- `external/Nymeria/` is read-only from this repository's perspective. A change
  to the engine requires a separate issue and pull request in
  [Nymeria](https://github.com/Rauberhohle-2-0/Nymeria).
- No engine source, shader, or asset is copied into `src/` as a workaround.

## Engine dependency decision

Reconnaissance was performed on 2026-10-22 against the `main` branch of Nymeria
at commit `6e727248627692a663b0651f09be2c662ed14681`
(`Merge pull request #4 from Rauberhohle-2-0/Windows_DirectX`).

### Findings

- Nymeria is one Git monorepo built as `Nymeria.slnx` with the .NET SDK pinned in
  `global.json` to `10.0.402` (`rollForward: latestPatch`, no prerelease SDKs),
  targeting `net10.0`.
- Engine projects reference each other with MSBuild `ProjectReference`. There is
  no packaging configuration: no `PackageId`, no `IsPackable` on engine projects
  (only `IsPackable=false` on test projects), and no `dotnet pack` step in the
  repository or its workflows.
- No Nymeria package is published on nuget.org. A query for the
  `Nymeria.Graphics` package identifier returns `BlobNotFound`.
- Native SDL3 is delivered as pinned NuGet packages referenced by
  `Nymeria.Platform` (`SDL3-CS` 3.4.18, `SDL3-CS.MacOS` 3.4.18,
  `SDL3-CS.Windows` 3.4.18). Applications get the managed binding and the
  RID-scoped native runtimes transitively; nothing is installed machine-wide.
- The GPU backend policy is implemented in `Nymeria.Graphics`: Metal with MSL on
  macOS, Direct3D 12 with DXIL on Windows, and no backend on Linux (deliberately
  rejected rather than falling back to an untested driver).
- Direct3D 12 shader bytecode is produced at build time by the DirectX Shader
  Compiler, provisioned by `examples/Shared/DxilShaders.targets` inside the
  **examples** tree, and embedded per example. That machinery is not part of a
  reusable engine project.
- Nymeria runs restore/build/headless tests on macOS, Windows, and Linux in CI,
  but never launches a GUI. Its own documentation records that Windows GPU
  behavior on real hardware is still unverified.

### Decision

Consume Nymeria as a **Git submodule pinned to a commit**, at `external/Nymeria/`,
with MSBuild project references to the two engine projects the host needs:

```xml
<ProjectReference Include="../../external/Nymeria/src/platform/Nymeria.Platform/Nymeria.Platform.csproj" />
<ProjectReference Include="../../external/Nymeria/src/graphics/Nymeria.Graphics/Nymeria.Graphics.csproj" />
```

The pinned commit `6e727248627692a663b0651f09be2c662ed14681` is recorded by the
superproject gitlink, i.e. in the committed tree entry for `external/Nymeria`, not
by a branch name. A moving branch is never a build input.

Why this and not the alternatives:

- **Versioned package:** not available today. Nymeria publishes no packages, and
  the task forbids introducing a custom package publication system.
- **Vendored source copy:** forbidden, and it would fork the engine.
- **Submodule with project references:** reproducible by commit, keeps the engine
  independently buildable and reviewable, needs no change to Nymeria, and adds no
  publish pipeline. This is the smallest workable integration.

`Nymeria.OMSI.slnx` also lists the two consumed engine projects. Without that,
MSBuild builds project references that are absent from the solution with their own
default configuration (`Debug`) even during a `Release` solution build. Listing
them makes the solution configuration apply to the engine projects; it does not
change their sources or make them part of this repository.

The solution deliberately does **not** list `Nymeria.slnx`, the engine examples, or
the engine tests. The engine remains independently buildable from its own
repository and its own solution.

### Engine upgrade procedure

Changing the submodule pointer **is** an engine upgrade and requires its own
reviewed pull request:

1. In Nymeria, land and tag/record the engine commit that is wanted.
2. `git -C external/Nymeria fetch origin`
3. `git -C external/Nymeria checkout <commit>`
4. Update the pinned commit in this document and in the pull request description.
5. Build, run the headless tests, and ask the owner for Windows validation of the
   affected runtime behavior.
6. Commit the submodule pointer change on its own, with the old and new commit
   hashes in the message.

`git submodule update --remote` is not an approved upgrade mechanism: it moves the
pointer to whatever the upstream branch contains at that moment.

## Public engine surface used today

Only these types and members are referenced. They exist in the pinned commit and
are covered by Nymeria's own tests where they are GPU-free.

| API | Use | Documented limitation |
| --- | --- | --- |
| `Nymeria.Platform.WindowOptions(string title, int width, int height)` | Validated title and initial client size. | Rejects empty titles and non-positive sizes. |
| `Nymeria.Platform.Window.Create(WindowOptions)` | Initializes SDL video and creates the window. | One window per process; main thread only. |
| `Window.Handle` | Native `SDL_Window*` passed to the GPU device and to presentation. | The window owns the handle. |
| `Window.ProcessEvents(int)` | Pumps events and reports whether the window should stay open. | Blocks up to the timeout; no engine input layer. |
| `Window.Dispose()` | Destroys the window and shuts SDL down. | Must run on the main thread. |
| `Nymeria.Graphics.GpuDevice.Create(IntPtr window)` | Selects the platform backend, creates the device, claims the window for presentation. | Main thread only; throws a descriptive `InvalidOperationException` when the platform has no backend or device creation fails. |
| `GpuDevice.DriverName`, `ShaderFormat`, `SwapchainFormat` | Reported by the host diagnostics. | Values describe the device that was created, not a request. |
| `GpuDevice.Handle` | Native `SDL_GPUDevice*` used by the host's presentation code. | The device owns it. |
| `GpuDevice.Dispose()` | Waits for GPU idle, releases the window claim, destroys the device. | Dispose the device before the window. |

Everything else in Nymeria (`Core`, `Formats.*`, `Audio`, scene, lighting,
culling, LOD, streaming, picking, collision, animation) is intentionally unused in
this milestone.

## Host lifecycle

All steps run on the process main thread.

```text
Main
 ├─ HostOptions.TryParse(args)                     no native library needed
 ├─ OmsiHost.Start()
 │   ├─ Window.Create(WindowOptions)               SDL video init + native window
 │   ├─ GpuDevice.Create(window.Handle)            backend policy + presentation claim
 │   └─ new ClearFramePresenter(...)               stores handles only
 ├─ OmsiHost.ReportDiagnostics(console)
 │   └─ driver, shader format, swapchain format, logical window size
 ├─ OmsiHost.Run(maximumFrames)
 │   └─ while Window.ProcessEvents(timeout):
 │        ClearFramePresenter.Present()            clear render pass, no shaders
 ├─ OmsiHost.Dispose()
 │   ├─ GpuDevice.Dispose()                        releases the window claim
 │   └─ Window.Dispose()                           destroys window, quits SDL
 └─ exit code 0
```

`--map <directory>` and `--world <directory>` bypass this lifecycle entirely. Both
branch before any window or device is created; `--map` prints configuration
metadata, `--world` parses tile placements and prints the world summary. Neither
path makes an SDL call or needs an OMSI asset.

Failure mapping in `Program`:

| Failure | Message | Exit code |
| --- | --- | --- |
| Unknown argument | Names the argument, prints usage | 2 |
| `DllNotFoundException` | Native SDL3 missing; names the expected runtime path layout | 1 |
| `BadImageFormatException` | Native SDL3 architecture mismatch | 1 |
| `InvalidOperationException` | Engine initialization/backend failure message from Nymeria | 1 |
| Anything else | Unexpected error with full exception | 1 |

## Bootstrap rendering

A frame is a **clear-only render pass**: acquire a command buffer, acquire the
swapchain texture, begin a render pass with `LoadOp = Clear` and a fixed color,
end the pass, submit. No pipeline, shader, buffer, texture, or depth target is
created. This is enough to prove that:

- the window can be claimed for presentation,
- the selected backend (Metal on macOS, Direct3D 12 on Windows) produces a working
  swapchain,
- frames are actually presented, and
- the device and window shut down cleanly.

It needs no shader assets at all, which is why the bootstrap has no HLSL/MSL and
no DirectX Shader Compiler provisioning. Nymeria's shader families live in its
**examples** and are example-owned; copying them here would violate the ownership
boundary and would also duplicate the DXC pipeline. When OMSI content rendering
needs shaders, the options are an approved engine-level shader family in Nymeria
or a new, reviewed shader build step in this repository. Task 01 does neither.

The presentation code is host-boundary code by design: Nymeria exposes the native
`SDL_GPUDevice*` handle specifically so a host can record its own passes, and every
engine example does the same. It is not a rendering abstraction, it is not a
platform layer, and it must not grow into one.

## OMSI map metadata layer

The map layer added by Task 02 is OMSI-specific and lives in
`src/Nymeria.OMSI/Maps/`. It references no engine type, no SDL3 type, and no GPU
resource, so metadata can be inspected without a renderer. Its format contract is
[docs/OMSI_MAP_PARSING.md](OMSI_MAP_PARSING.md); this section records only the
architectural boundary.

```text
explicit map folder
   ├─ enumerate top-level file names ──► tile_x_y.map inventory (coordinates, path)
   └─ global.cfg ──► text decoding ──► [keyword] blocks
                        ├─ supported: [name], [friendlyname], [worldcoordinates]
                        └─ everything else: preserved as an unsupported directive
                                 │
                                 ▼
                        read-only OmsiMapManifest
                        (metadata, tiles, diagnostics, status)
```

Rules that keep the boundary intact:

- The layer is read-only. It never writes, renames, converts, or repairs an OMSI
  file, and it never executes content.
- Discovery is folder-based: an explicit directory, top-level files only, no
  search for an installation, no default path.
- Only documented syntax is interpreted. `[map]` is deliberately not parsed
  because its parameter grammar is not documented; tile inventory comes from the
  documented file naming instead.
- Anything unsupported is recorded as an unsupported directive, not dropped and
  not reinterpreted.
- Parsing and discovery are independent of rendering. The manifest holds metadata
  only: no scene, mesh, material, texture, terrain, or transport type.
- Limits and diagnostics are part of the contract: bounded input size and counts,
  source file and line numbers, and an explicit `complete` / `partial` / `failed`
  status so partial results are never presented as complete.
- The host exposes it as `--map <directory>`, which is inspection only and cannot
  be combined with `--smoke`.

Not in this layer: tile contents, terrain heights, Chrono overlays, rendering, and
any Nymeria scene construction. Those need a separately approved scope.

## OMSI tile and world data layer

The layer added by Task 03 lives in `src/Nymeria.OMSI/World/` and reuses the Task 02
`Maps` layer for discovery, decoding, and the keyword rule. Its format contract is
[docs/OMSI_WORLD_PARSING.md](OMSI_WORLD_PARSING.md); this section records only the
architectural boundary.

```text
explicit map folder
   └─ OmsiMapDiscovery ──► OmsiMapManifest (tiles, diagnostics, status)
                                │
                                ▼
   for each tile: ConfigTextReader ──► TileWorldParser
                     ├─ [version]  decides the record layout
                     ├─ [object]   detail level, path, IDCode, pose, labels
                     ├─ [spline] / [spline_h]  path, IDCode, neighbours, pose, radius
                     └─ every other keyword: counted, never reinterpreted
                                │
                                ▼
   immutable OmsiWorldModel (tiles, placements, terrain references,
                             diagnostics, complete/partial/failed)
                                │
                                ▼
   WorldSummaryWriter ──► bounded console summary, status line last
```

Rules that keep the boundary intact:

- Parser output only. The model holds no engine type, no GPU resource, no mesh,
  material, texture, or resolved asset path, and it is not a scene.
- References stay verbatim. `Sceneryobjects\...` and `Splines\...` are never opened,
  normalized, or resolved here, so path traversal cannot reach the filesystem and
  the resolution policy stays in the task that first loads an asset.
- Raw source values stay raw. No coordinate-system conversion, no Euler convention,
  no unit change: heading, pitch, bank, x, y, and z are exactly what the tile wrote.
- Records are read sequentially, not split into blocks first, because a documented
  object label may itself look like a keyword. A record owns exactly the lines its
  layout declares, and a truncated record is reported rather than absorbing the
  next record's fields.
- Unsupported record types are counted with their first line and preserved; they do
  not fail a tile, because the documented keyword list is much wider than the
  interpreted subset.
- Status is explicit and printed: `complete`, `partial`, or `failed`. Parsing never
  claims that a map is renderable.
- The host exposes it as `--world <directory>`, which cannot be combined with
  `--map` or `--smoke` and creates no window or GPU device.

## Platform backends

Nymeria decides the backend; Nymeria.OMSI does not select, override, or force one.

| Platform | Backend policy in the pinned engine | Shader format | Status |
| --- | --- | --- | --- |
| macOS | SDL `metal` driver | MSL | Engine path validated upstream on macOS. This host's runtime is not executed by the agent; owner validation is not requested for macOS in Task 01. |
| Windows | SDL `direct3d12` driver | DXIL | Implemented upstream; not verified on Windows hardware by any task yet. This host's Windows run is `NOT RUN (owner validation pending)`. |
| Linux | No backend; explicitly rejected | none | Not supported. No native SDL3 package is referenced. |

Design intent is to stay portable to a future Linux/Vulkan backend without
claiming it exists. No OS-specific project forks are created.

## Build graph

```text
Nymeria.OMSI.slnx
├── src/Nymeria.OMSI/Nymeria.OMSI.csproj        (Exe, net10.0)
│     ├── external/Nymeria/src/platform/Nymeria.Platform/Nymeria.Platform.csproj
│     │     └── NuGet: SDL3-CS 3.4.18, SDL3-CS.MacOS 3.4.18, SDL3-CS.Windows 3.4.18
│     └── external/Nymeria/src/graphics/Nymeria.Graphics/Nymeria.Graphics.csproj
│           └── NuGet: SDL3-CS 3.4.18
└── tests/Nymeria.OMSI.Tests/Nymeria.OMSI.Tests.csproj   (xUnit, net10.0)
      └── src/Nymeria.OMSI (for HostOptions only; no SDL call is made)
```

- `Directory.Build.props` at this repository's root applies to this repository's
  projects only. MSBuild imports the nearest `Directory.Build.props`, so the
  projects under `external/Nymeria/` keep the engine's own settings.
- `global.json` pins the SDK to `10.0.402`, matching the engine.
- Native SDL3 arrives through NuGet with `Nymeria.Platform`. There is no manual
  DLL copy, no Homebrew requirement, and no machine-wide `PATH` change.

## Known limitations

- One window per process, main thread only; no engine loop or input layer.
- Nothing is drawn beyond a clear color. There is no renderer, scene, camera use,
  texture, model, or shader in this milestone.
- No audio device is created. `Nymeria.Audio` is not referenced.
- Map support is metadata only. Tile contents, terrain, objects, splines,
  textures, vehicles, scripts, timetables, and Chrono overlays are not read, so no
  map is rendered or simulated. Grundorf compatibility is unverified.
- Only `[name]`, `[friendlyname]`, and `[worldcoordinates]` are interpreted from
  `global.cfg`; the tile inventory comes from file names, not from `[map]`.
- The documented primary encoding is UTF-16LE with a byte-order mark. Files in
  Windows-1252 bytes `0x80`–`0x9F` decode through the documented Latin-1 fallback
  rather than as Windows-1252.
- Diagnostics are console text only; there is no logging framework or UI.
- Windows runtime behavior, including SDL3/Direct3D 12/DXIL deployment and the
  real-map scan, is unverified until the project owner runs the checks.

## Future subsystem directions (not implemented)

These are planning boundaries, not interfaces, and none of them is approved for
implementation. They are recorded only to keep Task 01 from pre-building them:

- Read-only inventory of `tile_x_y.map` top-level records once real files have
  validated the metadata layer, with untrusted-input validation and bounded
  resource use.
- OMSI asset conversion into Nymeria's format-independent data, consuming
  engine parsers where they fit and proposing upstream changes where they don't.
- A world/streaming layer that composes `Nymeria.Core.Streaming`, `Scene`, LOD,
  and culling instead of replacing them.
- Vehicle simulation and OMSI script handling, which never executes arbitrary
  external programs or scripts as part of bootstrap.

## Verification status

- Engine consumption, host code, the map metadata layer, and docs were reviewed on
  macOS; restore, build, and 69 headless tests pass locally on macOS arm64.
- The map tests use synthetic strings written by the tests. They verify parser
  behavior, not OMSI compatibility.
- CI jobs for macOS and Windows are defined but have not run for this commit, so
  no CI result is claimed.
- Windows execution of the host, Direct3D 12 device creation, frame presentation,
  and shutdown are `NOT RUN (owner validation pending)`. The checklist is in
  [DEVELOPMENT.md](DEVELOPMENT.md#windows-manual-smoke-test).
- Scanning a real Grundorf folder is `NOT RUN (owner validation pending)`; the
  procedure is in [OMSI_MAP_PARSING.md](OMSI_MAP_PARSING.md#on-windows-owner).

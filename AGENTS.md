# Agent instructions

Read this file, [GIT_GUIDELINES.md](GIT_GUIDELINES.md),
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), and
[docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) before changing anything. They are the
authoritative instructions for this repository. The human maintainer owns
architecture, scope, and dependencies.

## 1. Mission and boundaries

Nymeria.OMSI is an OMSI runtime and application built on the Nymeria 3D engine. It
is **not** a fork, a second rendering engine, or a parallel platform layer.

- Engine code lives in the pinned submodule at `external/Nymeria/` and is read-only
  from here.
- OMSI-specific formats, map and configuration parsing, and simulation belong in
  `src/Nymeria.OMSI/` in later tasks.
- Generic, reusable graphics, platform, input, rendering, and engine features
  belong in Nymeria, proposed as separate upstream work. Do not "upstream later" by
  building them here first.
- Never copy engine source, example code, shader assets, or third-party OMSI
  implementation code into this repository.

## 2. Read first, never invent

- Inspect the current branch, `git status`, the relevant source and docs, and the
  actual upstream API before changing code.
- Read `external/Nymeria/src/**` and `external/Nymeria/docs/**` when a change touches
  engine behavior. The engine is the source of truth for its public surface.
- Never fabricate an API, a namespace, an option, a package version, or a test
  result. If an interface does not exist, report the gap instead of working around
  it with copied code.
- If Nymeria lacks a usable integration point, stop and report the smallest
  upstream change required. Do not patch around it locally.

## 3. Separation of concerns

- OMSI-specific concerns: Nymeria.OMSI.
- Format-independent data, scene, lighting, culling, streaming, picking,
  collision, animation, audio math, windowing, GPU resources, and backend
  selection: Nymeria.
- Host bootstrap, diagnostics, and lifecycle wiring: `src/Nymeria.OMSI/`, and only
  at the boundary where the engine exposes a native handle for exactly that
  purpose.
- Do not introduce a renderer abstraction, dependency-injection framework, ECS,
  plugin system, or speculative subsystem directory.

## 4. Dependency policy

- The engine is a Git submodule pinned to one commit. Never reference a branch,
  `latest`, or `git submodule update --remote` as a build input.
- Never vendor engine source, add a local NuGet feed for the engine, or introduce a
  package publication system.
- Do not add third-party packages without maintainer approval and a documented
  reason. Match versions the engine already uses where a package is genuinely
  needed.
- Do not upgrade the engine pointer automatically or as a side effect of another
  change. A pointer change is an engine upgrade: separate pull request, old and new
  commit hashes recorded, rebuilt, reviewed, and Windows validation assessed.
- No Dependabot or automated dependency bot is configured; do not add one without
  approval.

## 5. Cross-platform

- Preserve both supported paths: macOS/Metal with MSL and Windows/Direct3D 12 with
  DXIL.
- Linux/Vulkan is a design direction, not a supported platform. Do not claim it
  works, and do not add a fallback backend to "make it run".
- Do not create per-OS project forks, conditional source trees, or a second native
  dependency path without a demonstrated need and maintainer approval.
- Platform selection belongs to the engine. Do not force a backend via SDL hints or
  environment variables.

## 6. Incremental scope

- Implement only the current task. Preserve working behavior.
- Refactor only for a demonstrated, stated necessity, not for taste.
- No premature subsystems, placeholders, empty projects, or scaffolding for a
  later milestone.
- Do not implement OMSI parsing, map loading, terrain, world streaming, vehicle
  physics, OMSI scripts, or dynamic shadows unless the current task explicitly
  requires them.

## 7. Verification

Agent environment: **macOS**. Owner environment: **Windows**, with the original
OMSI installation.

Allowed on macOS:

- Inspecting both repositories.
- `dotnet restore`, `dotnet build`, formatting and static checks.
- Headless, asset-independent unit tests (currently `tests/Nymeria.OMSI.Tests`).
- `scripts/check-commit-trailers.sh` against a local commit range.

Not allowed on macOS, and never required of an agent:

- Running `Nymeria.OMSI` or any engine example.
- OMSI integration tests or tests that need OMSI assets.
- GPU, window, or audio smoke tests, locally or in hosted CI.
- Inventing mock OMSI assets to make a test pass.
- Adding GUI/GPU/audio runtime tests to CI without explicit approval.

Every reported check uses exactly one of: `PASS`, `FAIL`,
`SKIPPED (environment)`, `NOT RUN (owner validation pending)`, and names the
platform. Never claim a Windows runtime test passed because macOS compiled or CI
was green. Compilation is not runtime validation. Windows runtime behavior remains
`NOT RUN (owner validation pending)` until the owner reports the result.

## 8. Git conduct

Follow [GIT_GUIDELINES.md](GIT_GUIDELINES.md).

- Work only on the authorized task branch.
- Do not commit directly to `main` unless explicitly instructed.
- Do not rewrite history, force-push, merge, delete branches, or change repository
  settings without explicit authorization.
- Stage explicit paths; never `git add -A`.
- Do not commit build output, caches, generated shader bytecode, downloaded
  toolchains, credentials, or machine-specific paths.
- Run `scripts/check-commit-trailers.sh <range>` before proposing a change when
  commit messages are part of the work.

## 9. Authorship

- No `Co-authored-by` trailers, `Generated-by`/`Assisted-by` footers, or other
  AI-attribution lines in commits, pull request descriptions, or changelogs.
- Do not add the agent as author or committer, do not change Git identity, and do
  not falsify human authorship.
- Do not rewrite existing commits to hide or remove attribution. The repository
  checks reject only newly introduced trailers in the change's commit range.
- Follow any platform-mandated disclosure rules that apply to the tooling in use;
  those disclosure requirements are not a license to add repository trailers.

## 10. Security

- Treat all OMSI assets, configuration files, and future parser input as untrusted:
  validate paths, lengths, counts, and offsets; enforce resource limits; fail with
  readable errors instead of allocating or recursing without bound.
- Reject absolute or traversal paths from untrusted content when a later task reads
  game data.
- Never execute OMSI scripts, arbitrary files, or external programs as part of
  bootstrap or tests.
- Never commit secrets, tokens, or local machine paths; never print credentials.
- Keep the engine's own security rules intact; they are documented under
  `external/Nymeria/docs/SECURITY.md`.

## 11. Completion report

Finish with a report that contains:

- What changed, file by file.
- The exact commands executed, with platform and result (`PASS`/`FAIL`/etc.).
- Known gaps and unverified behavior, explicitly including Windows runtime checks.
- Scope confirmation: what was deliberately not implemented.
- A suggested next task.

Do not expand scope silently, and do not describe an acceptance criterion as met
when the underlying check was not performed.

## Repository map

| Path | Purpose |
| --- | --- |
| `src/Nymeria.OMSI/` | Host application: options, lifecycle, clear-frame presentation, diagnostics. |
| `src/Nymeria.OMSI/Maps/` | Read-only OMSI map metadata discovery and configuration parsing; no engine or SDL types. |
| `src/Nymeria.OMSI/World/` | Read-only OMSI tile-record parsing and the typed world model; no engine, SDL, or GPU types, and no resolved asset paths. |
| `tests/Nymeria.OMSI.Tests/` | Headless tests for GPU-free host code, the map metadata layer, and the tile/world layer (synthetic fixtures only). |
| `external/Nymeria/` | Pinned engine submodule (read-only). |
| `docs/` | Architecture, development, and map/world format documentation. |
| `scripts/` | Repository checks usable locally and in CI. |
| `.github/workflows/ci.yml` | Build/test on macOS and Windows; commit policy check. |

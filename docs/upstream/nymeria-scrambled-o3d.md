# Upstream change request: scrambled O3D vertex data

Status: **implemented** in the engine as commit
`e9baf833909323ed95ee614f766a3ed86c150249`, and consumed here by the submodule
pointer bump. Raised by Task 06.2, approved for Task 06.3.

- Repository: `Nymeria` (engine, pinned submodule at `external/Nymeria/`)
- Area: `src/formats/Nymeria.Formats.O3d/O3dModelParser.cs`, `docs/O3D.md`,
  its test project
- Type: format support, no public API change
- Owner approval: Task 06.3 ("O3D Descrambling (Approved Engine Extension)")

## Problem

`O3dModelParser` accepts only the vertex key `0xFFFFFFFF` and throws for every other
value:

```text
offset 0x00000004: O3D vertex data is scrambled (key 0x00000000), which is not
supported. Only key 0xFFFFFFFF (unscrambled) is accepted.
```

`0x00000000` is the ordinary key in stock OMSI content, not an exotic or damaged
value, so the parser refuses the majority of OMSI scenery meshes. On the owner's
Grundorf run this was 105 mesh files (the bulk of the buildings and trees), each
classified honestly by Task 06.2 as `unsupported format`.

## Requested change

Decode version 4+ files whose key is not `0xFFFFFFFF` by un-scrambling the vertex
records before the existing finite-value and index validation:

- one transform function next to the existing readers, keyed on the documented
  per-vertex state chain;
- the key, version, and flags are already read in `ParseFile`, so they only need to
  reach `ReadVertices`;
- the `0xFFFFFFFF` path stays byte-for-byte unchanged;
- after decoding, a fail-safe rejects implausible geometry with a `ModelFormatException`
  that says so, instead of rendering nonsense.

The full algorithm, its provenance, resolved ambiguities, the patch plan, and the
evidence status are in
[nymeria-o3d-scrambling-algorithm.md](nymeria-o3d-scrambling-algorithm.md).

## Why it belongs in the engine

Format decoding is engine work: `AGENTS.md` §3 puts format-independent mesh and GPU
resource handling in Nymeria, and `external/Nymeria/docs/O3D.md` documents the
supported subset as the engine's own statement of scope. A second O3D decoder in the
OMSI host would duplicate the format and split the truth about what is supported.

## Not part of this request

- No other format changes (textures, `.sco`, `.x`, bones, animation).
- No rendering, shader, or GPU changes.
- No change to the public parser surface (`new O3dModelParser(name).Parse(stream)`).
- No relaxed validation for unscrambled files.

## Resolution

Solved on 2026-10-23.

| Item | Value |
| --- | --- |
| Engine repository, area | `Nymeria`, `src/formats/Nymeria.Formats.O3d`, `docs/O3D.md`, its test project |
| Previous pin | `6e727248627692a663b0651f09be2c662ed14681` |
| Engine commit | `e9baf833909323ed95ee614f766a3ed86c150249` (`feat: decode scrambled O3D vertex records`) |
| Superproject change | the submodule pointer commit that records the new pin |
| Public API | unchanged; `new O3dModelParser(name).Parse(stream)` behaves as before for key `0xFFFFFFFF` |

The implementation follows the algorithm note exactly and keeps the four settled
ambiguities as recorded there. Validation on macOS: the engine solution builds with 0
warnings, and its suite passes (1,133 tests, 2 skipped), including the new scrambled
coverage. Windows rendering of real Grundorf meshes is `NOT RUN (owner validation
pending)`: the decode is proven against an independent writer and a documented byte
fixture, not against the owner's assets.

The engine commit was made locally and is **not pushed** by the agent; the pointer bump
references it, so the owner must push the engine repository first for a fresh clone or
CI to resolve the pin.

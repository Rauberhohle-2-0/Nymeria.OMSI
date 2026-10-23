# Upstream change request: scrambled O3D vertex data

Status: **approved by the owner**, scope fixed, not yet implemented in the engine.
Raised by Task 06.2, approved for Task 06.3.

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

To be completed when the engine commit lands: record the engine commit hash, the date,
and the superproject pointer-bump commit here, then mark this note solved.

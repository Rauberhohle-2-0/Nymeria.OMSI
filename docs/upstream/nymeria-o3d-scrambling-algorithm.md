# O3D vertex scrambling: algorithm, evidence, and patch plan

Discovery deliverable for Task 06.3, committed before any engine code changed.
Read with [nymeria-scrambled-o3d.md](nymeria-scrambled-o3d.md) (the approved upstream
request) and [../OMSI_SCENERY_06_2_FINDINGS.md](../OMSI_SCENERY_06_2_FINDINGS.md)
(how the rejection surfaced).

**Implemented** as engine commit `e9baf833909323ed95ee614f766a3ed86c150249`. The
sections below are unchanged discovery: the implementation followed the patch plan in
D2 and kept all four resolutions from D1.4. Two verification results were added after
the fact and are recorded in [D1.8](#d18-implementation-verification).

Development environment for this note: **macOS, no OMSI installation**. That shapes
what can be proven here and what cannot; see [Evidence status](#evidence-status).

## D1 — The scrambling algorithm

### D1.1 Where the key is, and what it covers

| Offset | Size | Field | Applies to |
| --- | --- | --- | --- |
| 0x00 | 2 | signature `84 19` | whole file |
| 0x02 | 1 | version (`1`, `3`, `4`, `5`, `7` in stock content) | whole file |
| 0x03 | 1 | flags (version ≥ 3): bit 0 = 32-bit triangle indices, bit 1 = scrambling variant | whole file |
| 0x04 | 4 | vertex key (version ≥ 4), little-endian | **the `0x17` vertices section only** |

The key is a file-level header field, not a per-subset or per-mesh value. It changes
the encoding of the vertex records and nothing else: the triangle section (indices and
material indices), the material section (11 floats and a Windows-1252 name), the pivot
matrix, and the bone section are stored plainly and are read identically either way.

### D1.2 What the two keys mean

- `0xFFFFFFFF` — **not scrambled.** The vertex records are the geometry, as
  `{x y z nx ny nz u v}` `f32`.
- Any other value — **scrambled.** Each vertex record had its position, normal, and
  texture-coordinate components permuted, sign-flipped, and shifted by a per-vertex
  state derived from the key, the version, the flags, the vertex count, and the
  fractional parts of the *stored* position of the previous vertex.

`0x00000000` is not a special "corrupt" marker and not a de-facto "plain" value: it is
an ordinary scrambling key with one documented special rule (the state resets at every
vertex), and the reference format notes annotate the field as **usually 0** in stock
content. That is why refusing it costs the majority of OMSI scenery rather than a
handful of mods.

### D1.3 The transform, in testable form

Provenance: reproduced from the reference loader/writer description in openOMSI
`docs/FORMATS.md` and its `omsi-o3d` crate (inspected 2026-10-23), which cites the
original executable's reader at `0x56c74c` and writer at `0x56af20`. Facts only: the
implementation below is this project's own C#, not copied or translated code.

```text
# header values already read: version, flags, key
scrambled = version >= 4 and key != 0xFFFFFFFF

# initial state
state = (key + (version - 4) + (flags & 2 ? 0x17D : 0)) mod 0xFDE8        # u16
n16   = vertex_count mod 0xFDE8                                            # u32
fb    = 0                                                                  # u32

for each stored vertex record (rx ry rz rnx rny rnz ru rv):
    if key == 0:
        state = (flags & 2 ? 0x130 : 0)
    state = (state * n16 + n16 * fb) mod 8000                              # u16/u32
    fb    = trunc(|frac(rx) * frac(ry) * frac(rz)| * 600) mod 256          # from the STORED position

    x, y, z       = rx, ry, rz
    nx, ny, nz    = rnx, rny, rnz
    u, v          = ru, rv

    if state < 1000:      swap(x, y)
    elif state < 3000:    swap(x, z)
    elif state > 7000:    swap(y, z)

    if state % 4 == 0:    nx = -nx
    if state % 6 == 0:    ny = -ny
    if state % 7 == 0:    nz = -nz

    if state < 600:       swap(ny, nz)
    elif state > 4500:    swap(nx, ny)
    elif state > 6500:    swap(nx, nz)        # unreachable, see D1.4

    if state % 5 == 0:    u -= (state % 100)^2 / 10000
    if state % 3 == 0:    v -= (state % 50)^2 / 2500

    frac(x) is x - trunc(x) in double precision, computed on the f32 value
```

`state % 4 == 0` is the reference's `state & 3 == 0`; both spell the same test for
non-negative `state`.

### D1.4 Four ambiguities, and how each was settled

1. **Does the `key == 0` reset happen once or per vertex?** Per vertex. The reference
   implementation performs it at the top of the per-vertex loop, before the state
   update, so with key 0 the state depends only on the vertex count and the previous
   stored position fraction — never on the header arithmetic.
2. **Are the three normal sign tests independent or a chain?** Independent. A state that
   is a multiple of 12 flips both `nx` and `ny`; the reference tests each divisor
   separately.
3. **Is `elif state > 6500` reachable?** No, and not in OMSI either: every state above
   6500 has already taken the `> 4500` branch. The original carries the same dead test
   in both the reader and the writer, so this implementation keeps the branch (written
   as dead code with a comment) rather than "fixing" a file encoding that no OMSI
   content uses.
4. **Which position feeds the next state — stored or decoded?** The stored (still
   scrambled) one, and `fb` for the *first* vertex is 0. The writer of such files
   computes the same byte from the bytes it is about to write.

### D1.5 What the transform preserves — and why that limits validation

The transform only permutes and negates components and shifts UVs:

- triangle indices and material indices are untouched;
- the mean, extents of the bounding box as a *set of axis values*, and every normal's
  length are unchanged by a permutation;
- only the *assignment* of values to axes, signs, and UVs changes.

Consequence for requirement R2: a bounding-box limit or a unit-normal test cannot
detect a wrong key or a wrong state sequence. A wrong state produces a mesh whose
vertices are permuted *inconsistently*, which is visible as exploded or shredded
geometry, not as an out-of-range value. The fail-safe therefore catches corrupt
records, and the empirical proof of a correct decode is visual (owner run), not
numeric. This is recorded rather than papered over.

### D1.6 Hexdumps: synthetic, byte-verified

No OMSI asset exists on macOS and none may be downloaded or synthesized as a stand-in
for one, so the dumps below are **synthetic**: three vertices, written plain and then
scrambled with the algorithm above by an independent calculator (Python, `struct`),
and checked to round-trip back to the originals through the decode direction.

Plain file (`version 4`, `flags 0`, `key 0xFFFFFFFF`), vertex section:

```text
84 19 04 00 FF FF FF FF  17 03 00 00 00
00 00 A0 3F  00 00 20 C0  00 00 70 40  00 00 00 00  9A 99 19 3F  CD CC 4C 3F  CD CC CC 3D  66 66 66 3F
CD CC 24 41  66 66 96 40  66 66 66 BF  00 00 80 3F  00 00 00 00  00 00 00 00  00 00 00 3F  00 00 80 3E
1F 85 E3 C0  C3 F5 A8 3E  CD CC 0C 40  00 00 00 00  00 00 00 00  00 00 80 3F  00 00 40 3F  00 00 00 00
```

The same geometry, scrambled (`key 0x00000000`):

```text
84 19 04 00 00 00 00 00  17 03 00 00 00
00 00 20 C0  00 00 A0 3F  00 00 70 40  00 00 00 80  CD CC 4C BF  9A 99 19 BF  CD CC CC 3D  66 66 66 3F
66 66 96 40  CD CC 24 41  66 66 66 BF  00 00 80 BF  00 00 00 80  00 00 00 80  00 00 00 3F  EE 5A C2 3E
C3 F5 A8 3E  1F 85 E3 C0  CD CC 0C 40  00 00 00 00  00 00 80 3F  00 00 00 00  00 00 40 3F  1A C0 1B 3F
```

Reading the first record by hand: `state = 0` (key 0 resets it, `vb = 0`), so the
position takes the `swap(x, y)` branch (`-2.5, 1.25, 3.75`), the normal is negated on
y and z and then y/z swapped (`0, -0.8, -0.6`), and the UV offsets are zero because
`state = 0` is a multiple of both 5 and 3 but `0²/…` is 0. The `00 00 00 80` words are
`-0.0`, which is numerically zero and therefore irrelevant, but a bit-exact comparison
must not treat the patterns as different.

**Real-file dumps (owner, Windows, not run here).** The D1 requirement asks for actual
Grundorf files; these commands produce them, plus a census that says how much of the
corpus is scrambled:

```powershell
$root = 'E:\SteamLibrary\steamapps\common\OMSI 2'

# Headers of the two files named in the Task 06.2 console output.
Format-Hex -Path "$root\Sceneryobjects\Buildings_RUE\model\Haus01c.o3d" -Count 32
Format-Hex -Path "$root\Sceneryobjects\Kreuz_MC\model\Einm_see_johanna.o3d" -Count 32

# Any unscrambled reference: the first file whose key is 0xFFFFFFFF.
Get-ChildItem $root -Recurse -Filter *.o3d -ErrorAction SilentlyContinue | ForEach-Object {
  $fs = [System.IO.File]::OpenRead($_.FullName)
  try { $b = New-Object byte[] 8; $null = $fs.Read($b, 0, 8) } finally { $fs.Dispose() }
  if ($b[2] -ge 4 -and [BitConverter]::ToUInt32($b, 4) -eq 0xFFFFFFFF) {
    "PLAIN: $($_.FullName)"; Format-Hex -Path $_.FullName -Count 32
  }
} | Select-Object -First 40
```

### D1.7 Verification status

| Claim | Basis | Status |
| --- | --- | --- |
| Key lives at 0x04 for version ≥ 4 and covers only the vertex section | Engine parser + format notes + reference implementation | Established |
| key `0xFFFFFFFF` = plain, anything else = scrambled, key 0 is the stock default | Format notes ("usually 0") + reference implementation | Established |
| The transform above is the decode direction of OMSI's own writer | Reference implementation (`0x56c74c` / `0x56af20`), including the dead branch | Established from that source |
| The implementation is the exact inverse of the writer direction | Synthetic round-trip, bit-exact except `-0.0` equality (engine test) | Verified locally |
| Decoding real Grundorf meshes produces correct geometry | — | **NOT RUN** (no OMSI install; Windows run pending) |
| A wrong decode is caught by the fail-safe | Analysis: permutation preserves extents and normal lengths | **Disproved** — see D1.5; visual check is the gate |

### D1.8 Implementation verification

| Claim | Basis | Status |
| --- | --- | --- |
| The D1.6 fixture decodes to its plain records | Engine test `DecodesTheDocumentedScrambledFixture`; the states it pins are 0, 168, and 339 | PASS (macOS) |
| The decode agrees with an independent writer across versions, flags, and keys | Engine test `DecodesScrambledVertexRecords` with an independently written writer-direction fixture | PASS (macOS) |
| Key `0xFFFFFFFF` still decodes exactly as before | The existing version/flag matrix tests plus `OpenKeyLeavesRecordsPlainEvenWithTheAlternateFlag` | PASS (macOS) |
| Relabelling scrambled bytes as plain does not yield the original geometry | Engine test `ScrambledRecordsAreNotReadAsPlainGeometry` | PASS (macOS) |
| Damaged records are rejected | Engine tests for the extent limit, truncation, a non-finite value, and a bad index after decoding | PASS (macOS) |
| Real Grundorf meshes decode correctly | — | **NOT RUN** (Windows; the visual check in [../OMSI_SCENERY_3D.md](../OMSI_SCENERY_3D.md) is the gate) |

One correction to the arithmetic as first written here: the `key == 0` reset runs at
the top of every vertex iteration, so the state does not accumulate across vertices in
stock files. A first hand check that carried the previous state forward disagreed with
the D1.6 fixture; the fixture is right, and the implementation follows it.

## D2 — The existing parser, and the minimal patch

### D2.1 Current scope

| Concern | Location |
| --- | --- |
| Flags parsed, unknown bits rejected | `O3dModelParser.cs:163-177` |
| **Key read and rejected** | `O3dModelParser.cs:182-193` (`if (key != UnscrambledKey) throw`) |
| Section loop (`0x17` vertices dispatched) | `O3dModelParser.cs:200-290` |
| **Vertex records read** | `O3dModelParser.cs:312` (`ReadVertices(reader, wideCounts, …)`) |
| Existing per-vertex validation | non-finite position/normal/UV checks inside `ReadVertices` |
| Triangle/material/index validation | after the section loop |

There is **no** stream-level encryption or decoding abstraction to extend: the parser
buffers the whole file and reads records directly. The change therefore belongs inside
`ReadVertices`, which needs three values it currently does not receive (`version`,
`flags`, `key`).

Existing tests: `tests/Nymeria.Formats.O3d.Tests/O3dModelParserTests.cs` with
`O3dTestFileBuilder` (a synthetic writer that produces every version/flag combination).
`RejectsScrambledVertexKeys` (line ~291) is the test that must change meaning: its keys
are now decoded, and the plain-key path keeps its own coverage.

### D2.2 Patch plan

| Step | File | Estimated change |
| --- | --- | --- |
| 1. Replace the rejection with `bool scrambled = version >= 4 && key != UnscrambledKey;` and keep the key value | `O3dModelParser.cs` (header) | −8 / +4 |
| 2. Pass `version`, `flags`, `key` into `ReadVertices` | `O3dModelParser.cs` (call site + signature) | +2 |
| 3. Add a private static `VertexScrambling` helper: `Create(version, flags, key, vertexCount)`, `Next(storedPosition)`, `Apply(state, ref position, ref normal, ref uv)`, `FractionByte` | `O3dModelParser.cs` (new private type) | +55 incl. docs |
| 4. After reading each record and only when `scrambled`: advance the state, un-scramble, then validate | `O3dModelParser.cs:ReadVertices` | +12 |
| 5. Fail-safe: per-mesh extent limit and a clear `ModelFormatException` message | `O3dModelParser.cs` (after the vertices loop) | +12 |

Net for the parser: roughly **75 added, 10 removed**, inside the ~100-line budget. No
public signature changes: `O3dModelParser` keeps its constructor and `Parse(Stream)`,
`ModelData`/`MeshData` are untouched.

### D2.3 Engine test plan

- **Round-trip (R3).** Build plain records, scramble them with an *independent* writer
  transcription in the test project (same documented arithmetic, written in the writer
  direction: swaps before negation, UV additions instead of subtractions), parse, and
  compare every vertex. Cases: `key 0` with `flags 0` and `flags 2`, `key 12345`,
  `key 0xDEAD`, `key 40000`, versions 4, 5, and 7.
- **Plain regression.** The existing `0xFFFFFFFF` cases must pass unchanged; that is the
  "the plain path did not move" proof.
- **Fail-safe.** A scrambled file whose decoded extent exceeds the limit, and one with a
  non-finite decoded value, must throw `ModelFormatException` naming the reason.
- **State chain.** A two-vertex fixture where the second state differs from the first,
  proving `fb` is read from the stored position of the previous vertex (a single-vertex
  test cannot distinguish the two interpretations).

### D2.4 Fail-safe, and what it can honestly do

Implemented for scrambled files only (unscrambled files keep today's behavior, so no
existing content changes classification):

- finite position, normal, and UV values (already present, now also after decoding);
- per-mesh bounding-box extent ≤ 10,000 units on every axis;
- failure is a `ModelFormatException` with the extent or the offending value, which the
  OMSI loader classifies as `parse error` (not `unsupported format`).

Deliberately **not** enforced: a unit-length normal test. A permutation cannot change a
normal's length, so the test adds no signal for scrambling, and OMSI content contains
meshes whose stored normals are not unit length; rejecting those would trade a real
compatibility loss for no detection power. If the owner wants it after seeing real
content, it is one constant away.

### D2.5 Risks

| Risk | Mitigation |
| --- | --- |
| The transcription of the state chain is off by one vertex | Two-vertex fixture with a state-dependent `fb`; the owner's run shows shredded geometry immediately if it is wrong |
| A wrong decode passes the fail-safe | Accepted and documented (D1.5); the visual check is the gate, and the banner/console still says which meshes were decoded |
| Real content uses a key this build mishandles | Every key is treated the same way, as the reference does; an unsupported version still fails with its own message |
| The fail-safe rejects legitimate content | An extent limit of 10,000 units per axis is far beyond any OMSI scenery geometry, and no normal-length test is applied |

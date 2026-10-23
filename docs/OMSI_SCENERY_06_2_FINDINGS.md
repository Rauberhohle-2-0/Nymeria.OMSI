# Task 06.2 discovery findings — O3D scrambling, texture mapping, diagnostic truth

Status: discovery complete, and the implementation that stays inside this repository's
boundaries has landed after the owner's decision: diagnostic classification,
deduplication with occurrence counters, per-class counts, the honest verdict, the printed
asset roots, the three-outcome material resolution, and the cache stamp. **Scrambled O3D
decoding was not implemented**: stop condition 1 triggered (see [Stop-condition
events](#stop-condition-events)), because the fix requires a change in
`external/Nymeria/`, which this task did not make. `external/Nymeria/` is unmodified.

This note is the mandated discovery deliverable for Task 06.2 and must be committed
before any code change. It is deliberately blunt about which claims are established
here and which ones need the owner's Windows machine.

## Evidence base

| Source | What it supports | Availability |
| --- | --- | --- |
| Appendix A console output of `--scene-3d` on Grundorf (owner, Windows, D3D12/DXIL) | What the current build actually does on real content: counts, the exact O3D rejection message, the exact texture-resolution message, the diagnostic list | Provided |
| `external/Nymeria/src/formats/Nymeria.Formats.O3d/O3dModelParser.cs` | The engine's decoder contract: which key values it accepts, where it rejects, whether it has an option | Read locally |
| `external/Nymeria/docs/O3D.md` | The engine's own statement of its supported subset and limitations | Read locally |
| openOMSI `docs/FORMATS.md` (fetched 2026-10-23) | The documented O3D header, the vertex-scrambling algorithm, the `.sco` keyword vocabulary, and how OMSI treats material/texture names | Read online |
| `src/Nymeria.OMSI/**` and `tests/Nymeria.OMSI.Tests/**` | What this repository currently parses, resolves, caches, and prints | Read locally |

Environment for this discovery: **macOS (Darwin 27.0.0, arm64)**, .NET SDK 10.0.402.
No OMSI installation is present and none was downloaded. Consequently:

- **No hexdump of a real scrambled or unscrambled O3D file exists in this note.** The
  commands that produce one are given in D1 for the owner to run. This is recorded as
  `NOT RUN (owner validation pending)`, not as a completed deliverable.
- **No complete list of the 974 unsupported keyword occurrences exists in this note.**
  It can only be produced from the owner's installation (D3 gives the command).
- **No placement anchor exists** because the owner has not named one (D4).

Everything below that is not backed by one of the sources above is labelled
*assumption* or *hypothesis*.

## D1 — O3D scrambling

### What the format stores

The documented O3D header for a scrambled-capable file is:

| Offset | Size | Field |
| --- | --- | --- |
| 0x00 | 2 bytes | signature `84 19` |
| 0x02 | 1 byte | version (`1`, `3`, `4`, `5`, `7` observed in stock content) |
| 0x03 | 1 byte | flags, version ≥ 3. Bit 0: 32-bit triangle indices. Bit 1: a scrambling hint |
| 0x04 | 4 bytes | vertex key, little-endian, version ≥ 4. `0xFFFFFFFF` means **not scrambled** |
| 0x08… | | sections, one byte tag each: `0x17` vertices, `0x49` triangles, `0x26` materials, `0x79` pivot matrix, `0x54` bones |

The openOMSI format notes annotate the key field as "**usually 0**" in stock content,
and then describe the transform a scrambled file needs. That annotation is the single
most consequential fact in this note: `0x00000000` is not a corrupt or exotic value,
it is the ordinary stock value.

### What the two keys mean

- `0xFFFFFFFF` — no scrambling. Vertex records are stored directly as
  `{x y z nx ny nz u v}` `f32` values. This is the only value the engine accepts today.
- Any other value, including `0x00000000` — the vertex records were permuted per
  vertex by a scrambling transform keyed on `(key, version, flags, vertex count,
  position)`, and have to be un-permuted before use. **Only the vertex section is
  transformed**: triangles, materials, the pivot matrix and bones are stored plainly,
  so the file's structure, tags, counts, and index data are identical either way.

The documented transform, quoted as prose-and-pseudocode from openOMSI
`docs/FORMATS.md` (documentation only; no implementation code was copied or
translated):

```text
state = (key + version - 4 + (flags&2 ? 0x17D : 0)) mod 0xFDE8 (u16)
n = vertex_count mod 0xFDE8; b = 0
for each vertex (raw x y z nx ny nz u v):
    if key == 0: state = flags&2 ? 0x130 : 0
    state = (state*n + n*b) mod 8000
    b = trunc(|frac(x)·frac(y)·frac(z)|·600) mod 256   (from the raw position)
    if state < 1000: swap(x,y) elif state < 3000: swap(x,z) elif state > 7000: swap(y,z)
    if state % 4 == 0: nx = -nx; if state % 6 == 0: ny = -ny; if state % 7 == 0: nz = -nz
    if state < 600: swap(ny,nz) elif state > 4500: swap(nx,ny) elif state > 6500: swap(nx,nz)
    if state % 5 == 0: u -= (state%100)² / 10000
    if state % 3 == 0: v -= (state%50)² / 2500
```

Two details the note leaves ambiguous, and which an implementer must settle against
the reference loader before writing the transform:

1. whether the `key == 0` reset happens once before the loop or once per vertex (the
   notes show it inside the loop). It matters: with a per-vertex reset,
   `state = (0·n + n·b) mod 8000`, so the permutation reduces to a function of the
   vertex count and the position fractions only;
2. whether the `state % 4` / `% 6` / `% 7` normal operations are independent tests
   (as written, the `if`s are separate) or a chain (as the `if/elif` style of the
   position swaps suggests).

Both are cheap to pin down with a synthetic round-trip test in the engine: build an
unscrambled file, apply the documented transform, parse it back, and compare vertices.
No OMSI asset is needed for that test; the engine already has an
`O3dTestFileBuilder` in `external/Nymeria/tests/Nymeria.Formats.O3d.Tests`.

### Why the current build rejects these files

`O3dModelParser` reads the key and, for any value other than `0xFFFFFFFF`, throws:

```text
offset 0x00000004: O3D vertex data is scrambled (key 0x00000000), which is not
supported. Only key 0xFFFFFFFF (unscrambled) is accepted.
```

That is exactly the message in Appendix A, fired at `offset 0x00000004` — i.e. the
files carry version ≥ 4 (a key exists) and key `0x00000000`. Appendix A reports this
for `Kreuz_MC\model\Einm_see_johanna.o3d` and for every `Buildings_RUE` mesh it lists,
and 278 of the 1,095 placements end with "no drawable geometry".

Arithmetic check against Appendix A: 817 instanced + 278 definition failures = 1,095
considered, so the 278 are placements, not definitions (see D3/classification below,
where the console's wording is shown to be wrong about this).

### Can this be fixed inside this repository's boundaries?

**No.** Established by reading the engine:

- `O3dModelParser` is `sealed`, its key check is unconditional, and it exposes no
  option, flag, or overload that relaxes it. `external/Nymeria/docs/O3D.md` states the
  rejection as a deliberate limitation: "Scrambled vertices are unsupported. Any
  version 4+ key other than `0xFFFFFFFF` fails clearly instead of being heuristically
  decoded."
- The parser lives in the pinned submodule. `AGENTS.md` §1 and §3 put generic format
  decoding in Nymeria, and §2/§4 make the submodule read-only from here.
- The two in-repository alternatives are both explicitly excluded by this task's
  non-goals: a local O3D decoder ("Do not implement a new O3D decoder … without
  explicit owner approval") and a pre-pass that un-scrambles bytes before handing them
  to the engine (a second decoder in all but name, in the wrong repository).

So the fix is one small, well-scoped **engine** change, and it must be approved and
landed as its own engine task and its own submodule-pointer commit:

| Work item | Location |
| --- | --- |
| Accept any key; un-scramble the vertex records before the existing finite/index validation | `external/Nymeria/src/formats/Nymeria.Formats.O3d/O3dModelParser.cs` |
| Document the transform, the key values, and the settled ambiguities | `external/Nymeria/docs/O3D.md` |
| Round-trip test: unscrambled → documented transform → parse → identical vertices; plus key `0` with `flags` bit 1 clear and set | `external/Nymeria/tests/Nymeria.Formats.O3d.Tests` |
| Pointer bump, with old and new commit hashes recorded and its own purpose-stated commit | this repository (engine upgrade, not part of Task 06.2) |

The engine's coordinate conversion (Z negation, winding reversal) and its validation
order are unaffected, because only the eight floats per vertex change on disk; they
are ordinary `f32`s before and after.

Note the registration-list caveat from the format notes: the original loader also
refuses keys that are not in its registration list, and openOMSI does not reproduce
that check. Which keys should be accepted is therefore a decision for the engine
change — accepting any key and applying the transform is the simplest behaviour that
still matches observed content.

### Corpus evidence for the owner to collect (D1 evidence gap)

The engine-side conclusion does not depend on it, but the note is supposed to contain
real hexdumps, so here are the exact commands. Read-only; nothing is installed or
downloaded.

```powershell
# 1. Classify every O3D header under the installation by key (PS 7).
$root = 'E:\SteamLibrary\steamapps\common\OMSI 2'
Get-ChildItem -Path $root -Recurse -Filter *.o3d -ErrorAction SilentlyContinue | ForEach-Object {
  $fs = [System.IO.File]::OpenRead($_.FullName)
  try {
    $b = New-Object byte[] 8
    $read = $fs.Read($b, 0, 8)
  } finally { $fs.Dispose() }
  if ($read -ge 8 -and $b[2] -ge 4) {
    [pscustomobject]@{
      Version = $b[2]
      Flags   = $b[3]
      Key     = ('0x{0:X8}' -f [BitConverter]::ToUInt32($b, 4))
      Path    = $_.FullName
    }
  }
} | Tee-Object -FilePath "$HOME\o3d-headers.csv" |
    Group-Object Key | Sort-Object Count -Descending |
    Select-Object Count, Name | Format-Table -AutoSize

# 2. Two hexdumps for the note: one scrambled, one unscrambled.
Format-Hex -Path 'E:\SteamLibrary\steamapps\common\OMSI 2\Sceneryobjects\Kreuz_MC\model\Einm_see_johanna.o3d' -Count 32
# pick any row with Key = 0xFFFFFFFF from the CSV, then:
# Format-Hex -Path '<that path>' -Count 32
```

Expected result, to be confirmed rather than assumed: `Key = 0x00000000` dominates the
corpus, with a minority of `0xFFFFFFFF`, and `Version` is 4, 5, or 7. If the group
counts come out the other way round, the D1 conclusion still stands (the transform is
needed for whatever keys exist), but the priority of the engine change changes.

The sanity check that proves a fixed build: after descrambling, Grundorf's vertex
count should jump from the current 4,607 by at least an order of magnitude and
"definition(s) without drawable geometry" should collapse toward the population of
genuinely broken files.

## D2 — Texture and material mapping

### Two different names, one resolver

OMSI scenery carries two unrelated name spaces, and the current code runs them
together:

1. the **material name** stored in each mesh's material record (`Mat1`, `wall.bmp`,
   `asphalt`), decoded by the engine from the `.o3d` material section (or from the
   `.x` `MeshMaterialList`);
2. the **texture file name** declared by the definition's `[matl]` blocks.

The format notes describe materials as *keys*: `[texchanges]` master blocks hold "the
texture name as it appears in the o3d", `[CTCTexture] <name> <default-file>` "link each
item to a material texture", and `[matl_freetex]` builds a file name at run time. Every
mechanism the notes describe identifies a material slot **by name**; none describes
binding `[matl]` entries to material slots by declaration order. Name binding is
therefore the rule to implement, and *positional* binding is a hypothesis that must not
be implemented without evidence.

### What the current resolver does

`SceneryDefinitionParser.ResolveAsset` tries, in order:

| Asset | Folders (current code) |
| --- | --- |
| Mesh | `assetDirectory`, `<assetDirectory>\model`, `<scoDirectory>\model`, `<scoDirectory>`, content root |
| Texture | `assetDirectory`, `<assetDirectory>\texture`, `<scoDirectory>`, `<scoDirectory>\texture`, content root, `<root>\Texture` |

`assetDirectory` is the model-configuration folder when the definition points at one,
and the `.sco` folder for the inline form. For a texture with an explicit extension,
the `.dds` spelling is tried first in each folder. No recursive search is performed and
every candidate is bounded by `SceneryPath` (absolute, drive-qualified, traversal, and
control-character references are rejected, not repaired).

`LoadCache.GetMaterial` then applies a two-step rule: match the mesh material name
against the declared `[matl]` references by name; if no declaration matches, **treat the
material name itself as a texture path**.

### The `Mat1` failure is a classification bug, not a missing asset

Appendix A contains:

```text
warning: The texture 'Mat1' could not be resolved: 'Mat1' was not found in any permitted
asset folder (…\Sceneryobjects\Ruede; …\Sceneryobjects\Ruede\texture; …\OMSI 2;
…\OMSI 2\Texture). The subset is drawn with its base color.
```

`Mat1` is a generic mesh material label. The object has no `[matl]` entry of that name,
and no file named `Mat1` exists or should exist. The subset is not missing a texture
because a file is absent; it is a material with **no declared mapping at all**. The
current code cannot express that difference, so it reports the two cases the same way
and inflates "subset(s) without a usable texture" with material labels.

The resolver needs three outcomes per subset, not two:

| Outcome | Evidence | Counted as | Severity |
| --- | --- | --- | --- |
| Declared mapping | material name matches a `[matl]` reference, file resolved | texture decoded | — |
| Name-as-file | no `[matl]` match, a file with that name resolves | texture decoded | — |
| Unmapped material label | no `[matl]` match, no file of that name | **unmapped material**, one line per distinct label | informational, not a missing asset |
| Missing asset | `[matl]` (or name-as-file) resolved to a path that does not exist | missing texture | warning |
| Unsupported format | path resolved, decoder refused it | unsupported texture format | warning |

### Additional resolution gaps worth fixing (all evidence-gated)

- **Extension-less `[matl]` names.** The `.dds` preference is only applied when the
  reference already has an extension (`TryDdsVariant` returns `null` for an empty
  extension), so `[matl] foo` cannot resolve `foo.bmp`. Whether OMSI appends an
  extension is **unverified**; the candidate list (`.dds`, `.bmp`, `.tga`, `.jpg`) must
  not be guessed into the resolver without a real example.
- **Relative texture paths inside `[matl]`.** References such as `texture\foo.bmp` or
  `..\Shared\foo.dds` already work through `SceneryPath` + the candidate folders, but
  no real example has been checked here.
- **Map-local textures.** `<root>\maps\<map>\texture\` (map water, ground textures) is
  not a scenery texture root today. Whether any scenery `[matl]` needs it is
  **unverified**; it must not be added on suspicion.
- **`<sco>\texture` vs `<assetDirectory>\texture`.** Both are tried; for objects that
  point at a `model.cfg` in `model\`, OMSI's own relative base is the config folder,
  which the code already prefers.

### Evidence the owner can collect

```powershell
# Declared [matl] names and the parameter line after each, for one collection.
Get-ChildItem 'E:\SteamLibrary\steamapps\common\OMSI 2\Sceneryobjects\Ruede' -Filter *.sco |
  ForEach-Object {
    "=== $($_.Name)"
    Select-String -Path $_.FullName -Pattern '^\[matl\]' -Context 0,2 |
      ForEach-Object { $_.Context.PostContext -join ' | ' }
  }

# Where that collection's texture files actually live.
Get-ChildItem 'E:\SteamLibrary\steamapps\common\OMSI 2\Sceneryobjects\Ruede' -Recurse -Include *.bmp,*.dds,*.tga,*.jpg |
  Select-Object -ExpandProperty FullName
```

Combined with the object's mesh material names, that settles name-vs-position binding
and the real permitted-root list from evidence rather than assumption.

## D3 — The 974 unsupported keyword occurrences

### Why the complete list cannot be produced here

Two independent blockers:

1. **Environment.** The census needs the owner's `Sceneryobjects` tree; there is no
   OMSI installation on macOS and no asset may be committed or synthesized.
2. **A reporting defect in the build that produced Appendix A.** The summary printed
   `unsupported definition keywords: 974 occurrence(s) counted; the first few are named
   below` — but **nothing was named below**. `SceneryDefinition.UnsupportedKeywords` (the
   first eight distinct spellings the parser collected) was stored and never read by
   `ScenerySummaryWriter` or by anything else (verified by search across
   `src/Nymeria.OMSI/`). Appendix A bears this out: no keyword names appear, so the
   sentence was false and that run could not have shown the names even if the owner had
   looked for them. The implementation phase replaced the single total with a per-keyword
   census that the summary prints with counts, so the next run does produce the list.

That defect is the reason D3's "complete list" was impossible for anyone, not just for
this environment.

### Classification of the documented `.sco` vocabulary

The format notes list the `.sco` vocabulary, plus "the whole `model.cfg` vocabulary
inline". Grouping it by what this build should do with it:

**Out of scope by design** (present in real files, deliberately not interpreted; each
must be counted and named, never guessed at):

`script`, `scriptshare`, `varnamelist`, `stringvarnamelist`, `constfile`, `sound`,
`sound_ai`, `paths`, `passengercabin`, `busstop`, `depot`, `petrolstation`, `carpark_p`,
`entrypoint`, `tree`, `traffic_lights_group`, `traffic_light`, `phase`, `approachdist`,
`traffic_light_stop`, `traffic_light_jump`, `use_traffic_light`, `trafficlight`,
`signal`, `helparrow`, `switch`, `switchdir`, `blockpath`, `crossingproblem`,
`new_attachment`, `attach_trans`, `attach_rot_x`, `attach_rot_y`, `attach_rot_z`,
`triggerbox_new`, `triggerbox_setreverb`, `mass`, `momentofintertia`, `cog`,
`boundingbox`, `crashmode_pole`, `collision_mesh`, `nocollision`, `splinehelper`,
`maplight`, `nomaplighting`, `LightMapMapping`, `NightMapMode`, `onlyeditor`,
`complexity`, `groups`, `friendlyname`, `description`, `author`, `copyright`,
`rail_enh`, `third_rail`, `fixed`, `switch`.

**Candidate for a future task** (influence what is drawn or where, so they are the
honest explanation for "renders but looks wrong" once geometry decodes):

`rendertype` (`presurface`/`surface`/`on_surface` — draw order), `LOD` (which mesh OMSI
picks at a given screen size), `matl_alpha`, `matl_noZwrite`, `alphascale` (transparency),
`texchanges`, `CTC`, `CTCTexture`, `matl_change`, `matl_nightmap`, `matl_lightmap`,
`terrainhole`, `crossing_heightdeformation` (ground fit), `surface` (physics id, no
visual effect), `illumination*`, `shade`, `anim*`, `visible`.

**Possible defects in the current parser's own classification** (each one verifiable
from the code, none of them confirmed to appear in Grundorf):

- `MetadataKeywords` recognizes `texchange` and `render`, which are **not** documented
  `.sco` spellings (the documented ones are `texchanges` and `rendertype`). If a file
  uses `[texchanges]`, it is counted as unsupported even though texture swapping is a
  known, named mechanism; the singular spelling recognized in code may never occur.
- `[camera]` is recognized as metadata, while the documented vocabulary has
  `add_camera_reflexion`. Harmless, but the same kind of guess.
- Keyword counting only reaches definitions that parsed successfully, so 974 is a
  **lower bound** for the loaded subset and says nothing about the objects that failed
  to parse.

**Not classified, pending the census:** mod-specific keywords. Nothing may be named
before it is seen.

### Evidence the owner can collect

```powershell
# Census of keyword lines over the scenery tree (PS 7). Read-only.
Get-ChildItem 'E:\SteamLibrary\steamapps\common\OMSI 2\Sceneryobjects' -Recurse -Include *.sco,*.cfg -ErrorAction SilentlyContinue |
  ForEach-Object { Select-String -Path $_.FullName -Pattern '^\[([^\]\r\n]+)\]' -AllMatches } |
  ForEach-Object { foreach ($m in $_.Matches) { $m.Groups[1].Value.ToLowerInvariant() } } |
  Group-Object | Sort-Object Count -Descending |
  Format-Table Count, Name -AutoSize
```

This censuses the whole tree, which is wider than the loader's bounded subset
(256 definitions, 2,000 placements). The loader-side number that matters is the one the
fixed build will print per keyword after the reporting defect is repaired.

**Stop condition 3 assessment:** the geometry-affecting keywords above change
*appearance* and *which LOD mesh* is chosen, and `terrainhole`/`crossing_heightdeformation`
change *ground fit*, but none of them decide whether an object exists in the scene. A
significant fraction of the 974 being *required* for the anchor object is therefore
unlikely — but that is an assessment, **not a measured fact**, and it stays open until
the census is run.

## D4 — Placement anchor

**Not available — the owner chose to proceed without an anchor.** This environment
cannot propose one either: there is no Grundorf installation to read, the map's
placement list is unreadable here, and picking a building from a memory of the map would
be an invention. The Windows check therefore verifies that whatever the summary says is
instanced appears correctly, and that everything absent is named by a diagnostic; it is
recorded as a limitation, not a failure.

What is needed, in the owner's own words:

1. the object's **`.sco` reference as written in the tile** (for example
   `Sceneryobjects\Buildings_RUE\Haus01c.sco`), or its in-game name;
2. its approximate position — a tile (`tile_x_y.map`), or a heading/distance from a
   known landmark;
3. what it should look like in the screenshot (colour, shape, distinguishing feature).

Until then, Task 06.2 proceeds without a binary visual acceptance anchor, and this is
recorded as a limitation, not a failure. If the anchor turns out to be one of the
`Buildings_RUE` objects, it is currently blocked on the engine change from D1, because
those meshes are key-`0` files.

## D5 — Cache identity

Current cache keys, as implemented:

| Cached thing | Key | Failure cached? |
| --- | --- | --- |
| Parsed definition | resolved `.sco` path (`OrdinalIgnoreCase`) | yes, as `null` |
| Decoded mesh file | resolved mesh path (`OrdinalIgnoreCase`) | yes, as empty list |
| Decoded texture | resolved texture path (`OrdinalIgnoreCase`) | yes, as `null` |
| Material | `materialName \| texturePath` | no (material is rebuilt) |

All four dictionaries are created per `OmsiSceneryLibrary.Load` call and discarded with
it. Nothing is persisted across processes today.

**On the proposed `(absolute path, file size, mtime)` identity:**

- As an **in-run** validity stamp it is fine and cheap: it catches a file replaced
  underneath a long load, and it can be printed for reproduction.
- As a **persisted** cache identity it is weak, and should not be used that way: mtime
  granularity is coarse on FAT/exFAT volumes and network shares, copy tools preserve
  timestamps, and `(size, mtime)` has no collision guarantee — two different files can
  share it. A persisted cache needs a content hash of the bytes the loader already
  reads (the O3D and `.x` parsers buffer the whole file anyway, so hashing costs one
  pass over data that is already in memory).

**Recommendation, and what Task 06.2 should implement when it resumes:** key the
in-memory caches by resolved path exactly as now, and attach `(length,
lastWriteTimeUtc)` to each entry as an audit stamp used only to detect a mid-run
replacement and to make a report reproducible. Document explicitly that equal stamps
are not proof of equal content and that the cache is not persisted. Do not add a
persistent on-disk cache in this task; that is a separate design decision with its own
invalidation, locking, and versioning requirements.

## Diagnostic-truth defects found during discovery

These were code-level facts, verifiable without OMSI. Each was fixed in the
implementation phase of Task 06.2 except where the table says otherwise:

| Defect | Evidence | Fix landed |
| --- | --- | --- |
| `skipped: 278 definition(s) without drawable geometry` counts **placements**, not definitions | `DefinitionFailures` is incremented per placement with a cached failed model; 817 + 278 = 1,095 | rename the counter in the summary, or count distinct failed definitions |
| "the first few are named below" never names anything | `SceneryDefinition.UnsupportedKeywords` has no reader outside the parser | print per-keyword counts |
| `Mat1`-style material labels are reported as unresolved textures | The resolver's name-as-file fallback cannot distinguish an unmapped label from a missing file | three-outcome material classification (D2); an unmapped label is now `missing texture mapping`, one line per definition |
| No diagnostic class, so failure reasons are only distinguishable by reading message text | `SceneryLoadCounts` has one counter per skip site, none per class | classify every skip into the six required classes and count per class |
| Duplicate suppression is by `(severity, message)` with no occurrence counter | `SceneryDiagnostics.Report` increments `Suppressed` but never attaches a count to a listed line | key by `(canonical path, error class)` and print the occurrence count on the listed line |
| The top line claims "loaded OMSI scenery objects" whenever instances > 0, regardless of how much was skipped | `ScenerySummaryWriter.Write` | a derived verdict, `scene: incomplete — …`; `scene: complete` is never printed |
| The permitted asset roots are only echoed inside a failure message, never stated as a list | `ResolveAsset` builds the list only in the not-found error | the roots are now printed in an unconditional `asset roots:` line |

## Stop-condition events

| # | Condition | Status |
| --- | --- | --- |
| 1 | D1 concludes the scrambling fix requires changes inside `external/Nymeria` | **TRIGGERED** (see D1) |
| 2 | D2 concludes a correct fix needs a new decoding backend or file format | Not triggered. Texture mapping is a resolver change over existing decoders |
| 3 | D3 reveals a significant fraction of the 974 keywords is required for the anchor | Not triggered — assessment only, census pending |
| 4 | The D4 anchor cannot be reached with the current `.sco` subset even after texture resolution | Cannot be assessed: no anchor named |
| 5 | A change would touch more than two of {`.sco` parser, asset resolver, O3D decoder, texture pipeline, GPU resource ownership, scene graph} | Not triggered by the plan below. Without scrambling, the plan touches the `.sco` parser (keyword census) and the asset resolver (material mapping) — two. Adding the O3D decoder would make three, which is why scrambling is not attempted here |

Per the task's stopping rule, the note was committed and the owner was asked before any
implementation proceeded. The owner's decision was recorded as: **proceed without
scrambling**, and **no placement anchor** (D4). The implementation described above is the
result; the engine change D1 recommends remains open.

## What Task 06.2 changed

- `Scenery/SceneryDiagnostics.cs` — diagnostic classes, `(path, class)` dedup with
  occurrence counts, the per-class tally, and the decoder-message classifier.
- `Scenery/SceneryAssetStamp.cs` — the one-run cache validity stamp (D5).
- `Scenery/OmsiSceneryLibrary.cs` — per-class counts, the three-outcome material
  resolution, keyword aggregation, cache stamps, verdict inputs.
- `Scenery/SceneryDefinitionParser.cs` — classified parse diagnostics, the per-keyword
  census, and the roots description.
- `Scenery/SceneryDefinition.cs` — keyword counts, mesh failure classes, texture
  resolution status; the unused duplicate diagnostic list was dropped.
- `Rendering/ScenerySummaryWriter.cs` — the derived verdict, per-class breakdowns, the
  roots list, the keyword census, and classed diagnostics with occurrence counters.
- `Rendering/ScenePresenter.cs` — GPU failures are named as such in the upload line.
- `Program.cs` — passes the new summary inputs.
- `tests/Nymeria.OMSI.Tests/` — new tests for dedup and counting, the stamp, the writer
  contract, and the classifier; existing scenery tests updated for the new counts.
- `docs/OMSI_SCENERY_3D.md` — the reporting contract, asset roots, and cache identity.

That is the same two components from stop condition 5 (parser and resolver) plus
reporting; no GPU pipeline change, no scene graph, no decoder.

## Local verification status

| Check | Result |
| --- | --- |
| Read the O3D decoder and the engine's O3D documentation | PASS (macOS) |
| Establish that no engine option enables scrambled decoding | PASS (macOS, source inspection) |
| Build `Nymeria.OMSI.slnx` in Release | PASS (macOS), 0 warnings, 0 errors |
| Run `tests/Nymeria.OMSI.Tests` | PASS (macOS), 282 tests |
| Hexdump a real scrambled and unscrambled O3D | NOT RUN (no OMSI installation; owner command in D1) |
| Census the 974 unsupported keywords against real content | NOT RUN (owner command in D3; the loader now prints its own per-keyword census) |
| Verify the permitted texture roots against real files | NOT RUN (owner commands in D2) |
| Name a placement anchor | NOT RUN (owner chose to skip, D4) |
| Run `--scene-3d` and read a real summary | NOT RUN (Windows; owner checklist) |

## Suggested sequencing

1. **Engine task (still open):** add scrambled-vertex decoding to
   `Nymeria.Formats.O3d` upstream (its own commit in the engine, its own pointer-bump
   commit here, hashes recorded). That is the change that turns 278 failed Grundorf
   placements into candidates; until it lands, no work in this repository can draw them.
2. **Owner validation (next):** run the Windows checklist in
   [OMSI_SCENERY_3D.md](OMSI_SCENERY_3D.md) against this build, so the classification
   reports real measurements instead of the synthetic ones the tests use.
3. **After both:** the appearance questions (LOD selection, alpha modes, `texchanges`,
   ground fit) become visible for the first time, and become their own task.

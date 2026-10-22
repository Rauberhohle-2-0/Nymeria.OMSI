# OMSI map metadata parsing

This document is the authoritative description of the read-only map discovery and
configuration layer added by Task 02. It records what is supported, where the
behavior comes from, what is deliberately not interpreted, and how the owner
validates it against a real OMSI installation.

**Status:** implemented and build-verified on macOS. Validated against real OMSI
files: `NOT RUN (owner validation pending)`.

## Scope

The layer answers one question: *what map is this folder, and which tile files
does it contain?* It does not load or render a map.

> Tile contents are no longer out of scope: Task 03 parses the documented placement
> records into a typed world model, documented separately in
> [OMSI_WORLD_PARSING.md](OMSI_WORLD_PARSING.md). This document stays the contract for
> discovery and `global.cfg` only.

| Read today | Not read today |
| --- | --- |
| `global.cfg` metadata for `[name]`, `[friendlyname]`, `[worldcoordinates]` | Tile file contents here; see [OMSI_WORLD_PARSING.md](OMSI_WORLD_PARSING.md) |
| Presence and location of every `tile_x_y.map` file | `[map]`, `[entrypoints]`, `[groundtex]`, and every other `global.cfg` block |
| Readability of each discovered tile file | Chrono folders and their `.map` patches |
| Text encoding actually used for `global.cfg` | `.map.terrain`, `.map.LM.bmp`, `.map.prt` sidecars |
| | Textures, models, vehicles, scripts, timetables |

Parsing is independent of the engine and of the renderer: the `Maps` sources use
only the base class library, reference no `Nymeria.Graphics`, `Nymeria.Platform`,
or SDL3 type, and create no window or GPU device.

## Reference and provenance

- Reference documentation: [openOMSI](https://github.com/openOMSI-org/openOMSI),
  file `docs/FORMATS.md`, inspected on **2026-10-22** at commit
  [`3c51d09fdb4cb437fab228ea785b759d1c6331e6`](https://github.com/openOMSI-org/openOMSI/blob/3c51d09fdb4cb437fab228ea785b759d1c6331e6/docs/FORMATS.md).
  The relevant sections are "Text encoding", "The `[keyword]` block format", and
  "Map - units `mc_mapclass`, `mc_terrain_2`, `mc_chrono`".
- **License note:** openOMSI is licensed under AGPL-3.0. Only its prose
  documentation was read. No openOMSI source code, parser structure, or asset was
  copied, and no text from that documentation is reproduced here. The behavior
  below is implemented independently from documented file-format facts, which is
  a provenance and license question for the maintainer to review before this
  repository ships beyond its own development.
- No OEM OMSI SDK documentation and no OMSI installation were available on the
  development machine. Claims taken from the reference are therefore **unverified
  against real data** until the owner runs the validation below.

### Documented versus inferred

| Claim used here | Source | Verified against real files |
| --- | --- | --- |
| Map files (`global.cfg`, `tile_x_y.map`, Chrono patches) are UTF-16LE with a byte-order mark and CRLF line endings | Reference | No |
| A keyword is a whole line that starts with `[` in the first column and ends with `]`, with no surrounding whitespace | Reference | No |
| The lines after a keyword are parameters, one per line, consumed in order; an empty line is a legal empty parameter; free text between blocks is ignored | Reference | No |
| Indented `[keyword]` lines are help text or switched-off blocks, not keywords | Reference | No |
| `global.cfg` contains `[name]`, `[friendlyname]`, `[worldcoordinates]`, `[map]`, `[entrypoints]`, `[groundtex]`, and others | Reference (keyword list) | No |
| `[name]` and `[friendlyname]` carry one string value | Inferred from the block format and single-string semantics | No |
| Presence of `[worldcoordinates]` switches the tile grid from 300 m tiles to a degree-based grid | Reference | No |
| Tiles are files named `tile_<column>_<row>.map`; the `[map]` list can contain entries with missing files and can list a tile twice | Reference | No |
| The parameter grammar of the `[map]` list is not documented | Reference (absence) | n/a |
| OMSI resolves content file names without regard to case | Reference | No |

Anything in the "No" column is documented behavior, not confirmed behavior. Do not
describe this parser as Grundorf-compatible until the owner's run succeeds.

## Supported directives

Only these `global.cfg` blocks are interpreted. Keyword comparison ignores case;
the original spelling is kept in diagnostics.

| Directive | Interpretation | Notes |
| --- | --- | --- |
| `[name]` | First parameter line, verbatim | Missing or empty value is a warning, never a fabricated name |
| `[friendlyname]` | First parameter line, verbatim | Same rule |
| `[worldcoordinates]` | Presence means the map declares world coordinates | Its parameter lines are not interpreted |

Every other block is preserved as an `OmsiDirectiveRecord` (keyword + line
number) and listed in the manifest as unsupported. Unknown directives are **not**
fatal and are not silently reinterpreted. The duplicate policy is deterministic:
the first supported block wins and later duplicates are reported as warnings.

### Why `[map]` is not parsed

The reference documents that the `[map]` list indexes tiles, that entries may
point at missing files, and that an entry may repeat. It does not document the
parameter grammar of those entries. Tile inventory therefore comes from the
folder's file names, which the reference does document
(`<map>/tile_x_y.map`). Cross-checking the two is left to a later task, when real
files can show the list's shape.

## Text encoding

`global.cfg` is decoded in this order, and the chosen encoding is reported in the
manifest and the host summary:

1. `FF FE 00 00` — rejected: UTF-32 is not an OMSI map encoding.
2. `FF FE` — UTF-16LE, strict decoding; an odd byte count or an invalid surrogate
   is an error.
3. `EF BB BF` — UTF-8, strict decoding.
4. No mark — strict UTF-8 is attempted first.
5. Invalid UTF-8 without a mark — decoded as ISO-8859-1 **with a warning**, so
   every byte is preserved and no encoding is guessed from text content.

Additional rules:

- A decoded file containing NUL characters produces a warning that it may be
  UTF-16LE without a byte-order mark.
- No code-page package is added. `Encoding.Latin1` and `Encoding.Unicode` are part
  of the base class library.
- **Known limitation:** a Windows-1252 file that uses bytes `0x80`–`0x9F` (smart
  quotes, dashes, the euro sign) decodes to the matching Latin-1 code points, not
  to the Windows-1252 characters. Those bytes are rare in map configuration and
  are reported by the fallback warning. Supporting Windows-1252 would need an
  approved package (`System.Text.Encoding.CodePages`) and is not part of Task 02.
- Line endings may be CRLF, LF, or CR; a trailing line ending does not create an
  extra empty line.

## Discovery rules

The scanner takes one explicit folder. It never searches the machine for an OMSI
installation and never reads a default Steam path.

- The folder must exist and be enumerable, otherwise the scan **fails**.
- Only files directly inside the folder are considered; no recursion.
- `global.cfg` is matched without regard to case. If several spellings exist, the
  first in ordinal order is used and a warning names the others.
- `tile_<x>_<y>.map` is matched without regard to case. Coordinates are invariant
  signed 32-bit integers, so negative rows work and no coordinate range is assumed.
- Tiles are returned sorted by column, then row, then file name (ordinal).
- A `.map` file that does not follow the convention is reported and ignored.
- Duplicate coordinates (for example `tile_0_1.map` and `tile_00_1.map`) are
  reported; the first in file-name order is kept so the result stays deterministic.
- Each discovered tile is opened read-only to prove it is readable. Its contents
  are not parsed. An unreadable tile is still listed, marked unreadable, with a
  warning.
- A folder with no tiles is a warning, not a crash.

### Limits

| Limit | Value | On exceed |
| --- | --- | --- |
| `global.cfg` size | 8 MiB | Error, scan fails |
| `global.cfg` lines | 200,000 | Error, scan fails |
| Entries in the map folder | 50,000 | Error, scan fails |
| Tile files | 10,000 | Error, scan fails |

Limits exist so malformed or pointed-at-wrong inputs cannot cause unbounded work.
They are far above the largest documented stock map.

## Diagnostics and status

Every message is a `MapDiagnostic` with a severity, message, and (when applicable)
the file and one-based line number.

| Status | Meaning | Host exit code |
| --- | --- | --- |
| `complete` | No warnings, no errors | 0 |
| `partial` | A usable manifest exists, but warnings mean it is not complete | 0, with `status: partial` printed |
| `failed` | No trustworthy manifest; at least one error | 1 |

`partial` is printed explicitly and is never presented as complete. Missing
`global.cfg`, an unreadable or undecodable `global.cfg`, an invalid folder, and an
exceeded limit fail the scan. A missing `[name]`, an odd tile file name, a
duplicate coordinate, a non-tile `.map` file, or an unreadable tile are warnings.

## Host usage

Inspection is a separate command path that never creates a window or GPU device:

```text
Nymeria.OMSI --map <directory>
```

Example output shape (values come from the scanned folder):

```text
map: /path/to/Maps/Example
global.cfg: /path/to/Maps/Example/global.cfg
text encoding: UTF-16LE with byte-order mark
name: Example
friendly name: Example Map
world coordinates: no
tiles: 2
  (0,-1) tile_0_-1.map
  (0,-2) tile_0_-2.map
unsupported directives: 9 ([version], [entrypoints], [groundtex], ...)
diagnostics: none
status: complete
```

`--map` cannot be combined with `--smoke`: inspection presents no frames.
Ordinary startup without `--map` behaves exactly as before and needs no OMSI data.

## Security

Map folders are untrusted input.

- Only the folder the caller names is read; no environment variable, registry
  entry, or default installation path is consulted.
- No file name, path, or reference is taken from file contents, so configuration
  content cannot redirect reads outside that folder.
- Nothing is executed; scripts, models, and textures are never loaded.
- Every file read is bounded by the byte, line, entry, and tile limits above.
- Nothing is written, moved, renamed, or deleted, including inside the map folder.

## Validation

### On macOS (agent)

```bash
dotnet restore Nymeria.OMSI.slnx
dotnet build Nymeria.OMSI.slnx --configuration Release --no-restore
dotnet test Nymeria.OMSI.slnx --configuration Release --no-build --no-restore
```

The tests cover encoding selection and fallbacks, line splitting, keyword-block
rules, malformed-line diagnostics, tile file-name parsing, discovery ordering,
duplicates, missing configuration, unsupported directives, and summary output.
Their fixtures are small strings written by the tests themselves. **They are not
OMSI content and their passing is not evidence of Grundorf compatibility.**

The host executable is not run on macOS as part of this task; the `--map` command
is exercised through these unit tests only.

### On Windows (owner)

1. Build:

   ```bat
   dotnet restore Nymeria.OMSI.slnx
   dotnet build Nymeria.OMSI.slnx --configuration Release --no-restore
   ```

2. Point the scanner at the installed map folder, for example:

   ```bat
   src\Nymeria.OMSI\bin\Release\net10.0\Nymeria.OMSI.exe --map "C:\Program Files (x86)\Steam\steamapps\common\OMSI 2\maps\Grundorf"
   ```

   The exact path depends on the installation; quote it. Nothing is downloaded and
   no other folder is searched.

3. Expected observations for a stock Grundorf installation:
   - `global.cfg:` names the file that was found, and `text encoding:` reports
     `UTF-16LE with byte-order mark`.
   - `name:` is non-empty and matches the map's name in OMSI.
   - `tiles:` is greater than zero, and each listed coordinate has a matching
     `tile_<x>_<y>.map` on disk.
   - `status:` is `complete` and the exit code is `0`, **or** the warnings explain
     what differed (for example an unexpected top-level `.map` file). A
     `failed` status with a `global.cfg` error is a real problem to report.
   - No file in the OMSI installation is created, modified, or deleted. Check the
     folder's timestamps before and after.

4. Also run the same command twice and confirm identical output for identical
   input.

5. Report back with the command, exit code, complete output, Windows version,
   installation path, and tile count. If real files reveal syntax this parser does
   not support, report the file and line rather than assuming success; the
   smallest follow-up will be scoped separately.

## Deliberately not implemented

- Rendering a map, creating a Nymeria scene, terrain, or any GPU work.
- Tile contents in this layer: objects, splines, terrain heights, water, light maps,
  and rules are the subject of [OMSI_WORLD_PARSING.md](OMSI_WORLD_PARSING.md), not of
  discovery.
- `[map]`, `[entrypoints]`, `[groundtex]`, and all other `global.cfg` blocks.
- Chrono overlays, timetables, traffic, vehicles, scripts, and textures.
- Editing, repairing, converting, or writing OMSI files.
- Bundling or redistributing any OMSI asset.

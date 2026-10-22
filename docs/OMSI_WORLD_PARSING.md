# OMSI tile and world data parsing

This document is the authoritative description of the read-only tile/world layer
added by Task 03. It records exactly which tile records are interpreted, where that
behavior comes from, what stays uninterpreted, and how the owner validates it
against a real OMSI installation.

**Status:** implemented and build-verified on macOS. Validated against real OMSI
files: `NOT RUN (owner validation pending)`.

## Scope

Task 02 answered *what map is this folder and which tiles does it contain?* Task 03
answers *what does each tile place, where, and with which references?* It still does
not build a scene.

| Read today | Not read today |
| --- | --- |
| `[version]`, `[object]`, `[spline]`, `[spline_h]` from every discovered tile | `[attachObj]`, `[splineAttachement]`, `[splineAttachement_repeater]` |
| Object reference, detail level, identifier, position, orientation, labels | `[rule]`, `[kill_rule]`, `[varparent]`, `[selspline]`, `[relabel]` |
| Spline reference, identifier, neighbours, position, heading, length, radius | Spline gradients, cant, skew, texture offset, alignment length, mirror flag |
| Existence and byte length of each `<tile>.map.terrain` sidecar | Terrain heights, light maps, water planes, terrain holes |
| Every other tile keyword as a counted, uninterpreted record | Object/spline assets, textures, meshes, Chrono patches, vehicles, scripts |

Parsing stays independent of the engine: the `World` sources use the base class
library and the existing `Maps` layer only. They reference no `Nymeria.Graphics`,
`Nymeria.Platform`, or SDL3 type, create no window or GPU device, and load no asset.

## Reference and provenance

- Reference documentation: [openOMSI](https://github.com/openOMSI-org/openOMSI),
  files `docs/FORMATS.md` and `docs/HISTORY.md`, inspected on **2026-10-22** at commit
  [`3c51d09fdb4cb437fab228ea785b759d1c6331e6`](https://github.com/openOMSI-org/openOMSI/blob/3c51d09fdb4cb437fab228ea785b759d1c6331e6/docs/FORMATS.md).
  Relevant sections: "Map - units `mc_mapclass`, `mc_terrain_2`, `mc_chrono`" in
  `FORMATS.md` and the "Tiles" note in `HISTORY.md`.
- **License note:** openOMSI is licensed under AGPL-3.0. Only its prose
  documentation was read. No openOMSI source code, parser structure, or asset was
  copied, and no text from that documentation is reproduced here. Implementing
  from documented file-format facts is a provenance and license question the
  maintainer must review before this repository ships beyond its own development.
- No OMSI installation and no real tile file exist on the development machine.
  Claims taken from the reference are **documented but unverified against real
  data** until the owner runs the validation below.

### Documented versus inferred

| Claim used here | Source | Verified against real files |
| --- | --- | --- |
| `tile_x_y.map` is UTF-16LE with a byte-order mark, using the same `[keyword]` block rules as `global.cfg` | Reference; the same reader as Task 02 | No |
| `[object]` lines are: leading detail level (`0`), path, IDCode, x, y, z, heading, pitch, bank, label count, exactly that many label lines | Reference | No |
| The leading detail level exists from tile version 9, IDCodes from 6, labels from 4, pitch and bank from 12 | Reference | No |
| An object label is any line "whatever it says", including an empty line or one that looks like a keyword | Reference | No |
| `[spline]` / `[spline_h]` lines are: `0`, path, IDCode, previous, next, x, height, y, heading, length, radius | Reference (field order cross-checked upstream by neighbour continuity) | No |
| A spline has one neighbour line before version 11 and two from version 11; `-1` means "none" | Reference | No |
| x, y, z are raw tile/metre values in the file's own order; heading/pitch/bank are degrees with heading clockwise from north | Reference | No |
| Object z is terrain-relative except for `[absheight]` objects and spline helpers, whose z is absolute | Reference | No |
| `<tile>.map.terrain` is `u32 (=60)` followed by 61 x 61 `f32` heights, that is 14,888 bytes | Reference | No |
| `[rule]` belongs to the preceding object or spline | Reference | No |
| The `[map]` parameter grammar remains undocumented | Reference (absence) | n/a |

Anything in the "No" column is documented behavior, not confirmed behavior. The
parser must not be described as Grundorf-correct until the owner's run succeeds.

## Interpreted tile records

Keyword comparison ignores case. Line numbers are one-based and always refer to the
physical line in the tile file.

### `[version]`

- Only the **leading** `[version]` block decides record layouts. Scanning for it
  stops at the first keyword that is not `[version]`, so a label or free text later
  in the file that happens to look like `[version]` cannot change the layout.
- The value must be a positive integer. A missing, duplicate, or non-integer value is
  a warning on the tile; a tile without a usable version is not interpreted further.

### `[object]`

Field order for a version-12 tile, one line each:

```text
[object]
0                  detail level        (from version 9)
Sceneryobjects\... path, as written
17                 IDCode              (from version 6)
12.5               x
-3.25              y
1.75               z                   terrain-relative unless [absheight]
90                 heading, degrees
-1.5               pitch, degrees      (from version 12)
2.5                bank, degrees       (from version 12)
2                  label count         (from version 4)
Label one          label lines, exactly as many as the count
Label two
```

Shorter versions drop fields from the front in the documented order, so the parser
derives every index from the tile's own version instead of assuming version 12.

- The record owns exactly as many lines as its layout declares. Label lines are
  consumed even when they look like `[keyword]`, because the reference states any
  line is a label. This is why records are scanned sequentially instead of split
  into blocks first.
- A record that ends before its declared fields, declares a label count that does
  not match the lines it owns, or carries a value that is not a number is reported
  with its keyword line and skipped. The rest of the tile is still parsed.
- A declared label count larger than the remaining file is capped by the file, and
  the mismatch is reported instead of looping.
- Duplicate object identifiers are a warning; both records are kept, because the
  reference documents that identifiers are not unique across a joined map.
- Nothing is resolved or converted: the path stays an unresolved source reference,
  and x/y/z/heading/pitch/bank stay the raw source numbers.

### `[spline]` and `[spline_h]`

Field order for a version-12 tile:

```text
[spline]
0                  leading field
Splines\...        path, as written
42                 IDCode              (from version 6)
7                  previous spline     (-1 = none)
8                  next spline         (from version 11; -1 = none)
10.5               x
0.25               height
20.5               y
90                 heading, degrees
30                 length
-15.5              radius              (0 = straight, > 0 turns right)
```

- The documented trailing fields (gradient start/end, cant from version 5, skew from
  version 14, texture offset from version 11, alignment length, optional `mirror`
  from version 7) are **not** consumed and **not** interpreted. They stay in the
  file as ordinary parameter lines, so they produce no per-record diagnostic and no
  guessed value.
- `[spline_h]` is parsed with the same layout and flagged as a height spline.
- `-1` in an identifier or neighbour field means "none" and becomes `null`.

### Every other tile keyword

`[terrain]`, `[water]`, `[attachObj]`, `[rule]`, `[variable_terrain]`, and the rest
are counted per keyword with the line of the first occurrence, and exposed as
`OmsiUnsupportedTileRecord`. They are never reinterpreted. Because the documented
keyword list is far wider than the records interpreted here, uninterpreted record
types do not lower the parse status; every real tile is expected to carry several.

## Terrain sidecar

For each tile the loader reports whether `<tile>.map.terrain` exists and how long it
is.

- The documented layout is a 4-byte value followed by 61 x 61 32-bit floats, so
  14,888 bytes. A different length is a warning that names the actual and expected
  sizes.
- Heights are **not** decoded, no mesh is built, and nothing is written. The sidecar
  is metadata for the next task, not geometry for this one.

## World model

`OmsiWorldModel` (`src/Nymeria.OMSI/World/OmsiWorldModel.cs`) is an immutable view of
the attempt:

| Member | Meaning |
| --- | --- |
| `MapRoot`, `GlobalConfigPath` | The folder that was read and the configuration file used |
| `Name`, `FriendlyName` | From `global.cfg`, or `null` when unavailable |
| `Tiles` | One `OmsiWorldTile` per discovered tile, in manifest order |
| `Diagnostics` | Map-level and per-tile warnings and errors, in discovery then tile order |
| `Status` | `Complete`, `Partial`, or `Failed` |
| `ParsedTileCount`, `ObjectPlacementCount`, `SplinePlacementCount` | Totals over the model |

`OmsiWorldTile` exposes the tile coordinates from its file name, its path, its
version, `IsParsed`, its `Objects`, `Splines`, `Terrain`, `UnsupportedRecords`, and
its own diagnostics.

The model is deliberately **not** a scene: it holds no engine type, no GPU resource,
no mesh, material, or texture, and no resolved asset. `IsParsed` means the documented
record layout was understood. It does not mean the map is renderable, that a
reference exists on disk, or that OMSI would accept the tile.

### Status semantics

| Status | Meaning | Host exit code |
| --- | --- | --- |
| `complete` | Every discovered tile declared a supported version and no diagnostic was raised | 0 |
| `partial` | At least one warning was raised, or at least one tile stayed uninterpreted | 0 |
| `failed` | Discovery failed, a diagnostic was an error, or not one tile could be interpreted, so there is no usable world data | 1 |

The summary always prints the status, so a partial parse is never presented as a
successful one.

## Limits

Tile files reuse the Task 02 reader with larger bounds, because they hold thousands
of placements:

| Limit | Value | On exceed |
| --- | --- | --- |
| Tile file size | 64 MiB | Error for that tile, recorded as a tile diagnostic |
| Tile file lines | 1,000,000 | Error for that tile |
| Discovered tiles | 10,000 (Task 02) | Discovery error, whole scan fails |
| Entries in the map folder | 50,000 (Task 02) | Discovery error, whole scan fails |

A tile that exceeds a limit is reported and still listed; the remaining tiles are
parsed. No limit is expressed in world units or record counts, because none of those
are documented as bounded.

## Paths and safety

- Only the explicitly named map folder is read. No installation path, environment
  variable, or registry entry is consulted.
- Tile references (`Sceneryobjects\...`, `Splines\...`) are stored verbatim and never
  opened, normalized, or resolved. Path traversal therefore cannot reach the
  filesystem in this task; the resolution policy belongs to the task that first
  loads an asset.
- Windows separators are preserved as written. Nothing is normalized to the host's
  separator, so a macOS run reports exactly what the tile contains.
- Nothing is written, moved, renamed, converted, or deleted, including inside the
  map folder.
- Nothing is executed: no OMSI script, mesh, or texture is loaded.

## Host usage

```text
Nymeria.OMSI --world <directory>
```

The folder is the same one `--map` takes: the folder that directly contains
`global.cfg`. `--world` includes the discovery metadata in its summary, so
`--map` and `--world` cannot be combined, and neither can be combined with
`--smoke`. Neither inspection path creates a window or GPU device.

Example output shape (values come from the parsed folder):

```text
map: /path/to/Maps/Example
name: Example
friendly name: Example Map
tiles discovered: 2
tiles parsed: 2
tile versions: 12 (2 tiles)
objects: 71 placements
splines: 19 placements
terrain files: 2 (2 at the documented size)
uninterpreted tile record types: 9
  tile (0,-1): 38 object(s), 11 spline(s), version 12, terrain 14888 bytes, 0 diagnostic(s)
  tile (0,-2): 33 object(s), 8 spline(s), version 12, terrain 14888 bytes, 0 diagnostic(s)
diagnostics: none
status: complete
```

The tile list is capped at 50 lines and the diagnostic list at 25 lines; anything
beyond that is summarised by count, so a large map cannot flood the console. Totals
always cover the whole model.

## Validation

### On macOS (agent)

```bash
dotnet restore Nymeria.OMSI.slnx
dotnet build Nymeria.OMSI.slnx --configuration Release --no-restore
dotnet test Nymeria.OMSI.slnx --configuration Release --no-build --no-restore
dotnet format Nymeria.OMSI.slnx --verify-no-changes --no-restore
```

The tests cover record layouts for versions 3, 9, 10, 11, and 12, keyword-like and
empty labels, truncated records, malformed numbers, duplicate identifiers, missing
and duplicate versions, uninterpreted record counting, terrain size checks, world
status transitions, and summary truncation. Their fixtures are small strings written
by the tests themselves. **They are not OMSI content and their passing is not
evidence of Grundorf compatibility.**

The host executable is not run on macOS in this task; `--world` is exercised through
those unit tests only.

### On Windows (owner)

```powershell
dotnet restore Nymeria.OMSI.slnx
dotnet build Nymeria.OMSI.slnx --configuration Release --no-restore
dotnet test Nymeria.OMSI.slnx --configuration Release --no-build --no-restore

$map = 'C:\Program Files (x86)\Steam\steamapps\common\OMSI 2\maps\Grundorf'
src\Nymeria.OMSI\bin\Release\net10.0\Nymeria.OMSI.exe --world $map
```

Replace the path with the real installation. Then extend the run to the rest of the
stock maps, for example Berlin-Spandau, to exercise more versions:

```powershell
Get-ChildItem 'C:\Program Files (x86)\Steam\steamapps\common\OMSI 2\maps' -Directory |
    ForEach-Object {
        Write-Host "=== $($_.Name)"
        src\Nymeria.OMSI\bin\Release\net10.0\Nymeria.OMSI.exe --world $_.FullName
        Write-Host "exit code: $LASTEXITCODE"
    }
```

Expected observations:

1. `name:` and `friendly name:` match what OMSI shows for the map.
2. `tiles discovered:` equals the number of `tile_x_y.map` files in the folder, and
   `tiles parsed:` equals it as well for stock maps.
3. `objects:` and `splines:` are non-zero and plausible for the map size.
4. `tile versions:` shows the versions the stock tiles actually declare.
5. `terrain files:` is non-zero where `.map.terrain` files exist, and the
   `at the documented size` count equals it. A size mismatch is a finding, not a
   silent acceptance.
6. `status:` is `complete`, or the diagnostics name the file and line that differed.
7. No file in the OMSI installation is created, modified, or deleted. Compare folder
   timestamps before and after.
8. Running the same command twice produces identical output.

Report back with the command, exit code, full output, Windows version, installation
path, and the map names you ran. If real files show a layout this parser does not
support, report the file, the line, and the version rather than assuming success;
that becomes a separately scoped follow-up.

## Deliberately not implemented

- Rendering, scenes, GPU resources, meshes, terrain generation, spline tessellation,
  and asset loading.
- `[attachObj]`, spline attachments and repeaters, rules, variable terrain, water,
  light maps, Chrono overlays.
- Spline trailing fields and any coordinate or rotation transformation.
- Vehicles, traffic, timetables, scripts, audio, physics.
- Editing, repairing, converting, or writing OMSI files, and bundling any OMSI
  asset.

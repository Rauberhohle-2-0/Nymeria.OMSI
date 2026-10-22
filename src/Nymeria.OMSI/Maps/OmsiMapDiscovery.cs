namespace Nymeria.Omsi.Maps;

/// <summary>
/// Discovers an OMSI map folder from an explicit path: its <c>global.cfg</c>
/// metadata and its tile configuration files.
/// </summary>
/// <remarks>
/// <para>
/// This layer is read-only and deliberately narrow. It never searches the machine
/// for an OMSI installation, never loads terrain, objects, splines, textures,
/// vehicles, or scripts, and never writes to the map folder. It also never
/// initializes SDL, a GPU device, or a window, so metadata can be inspected
/// without a renderer.
/// </para>
/// <para>
/// Discovery is limited to the files directly inside the supplied folder.
/// Subfolders such as <c>Chrono/</c> and the sidecar files
/// (<c>tile_x_y.map.terrain</c>, <c>tile_x_y.map.LM.bmp</c>) are not part of this
/// milestone. Unknown <c>global.cfg</c> directives are preserved in the manifest
/// instead of being interpreted or dropped.
/// </para>
/// <para>
/// All limits, the accepted text encodings, and the supported directives are
/// documented in docs/OMSI_MAP_PARSING.md.
/// </para>
/// </remarks>
public static class OmsiMapDiscovery
{
    /// <summary>The largest number of entries read from the map folder.</summary>
    internal const int MaxRootFiles = 50_000;

    /// <summary>The largest number of tile files accepted from one map.</summary>
    internal const int MaxTileFiles = 10_000;

    private const string GlobalConfigFileName = "global.cfg";

    /// <summary>
    /// Scans one map folder.
    /// </summary>
    /// <param name="mapDirectory">
    /// The folder that contains <c>global.cfg</c>. It is used exactly as given; no
    /// default installation path is searched.
    /// </param>
    /// <returns>
    /// A manifest that always describes the scan. A missing folder, a missing or
    /// unreadable <c>global.cfg</c>, or an exceeded limit produces
    /// <see cref="MapScanStatus.Failed"/> with the reason in
    /// <see cref="OmsiMapManifest.Diagnostics"/>.
    /// </returns>
    /// <exception cref="ArgumentException">The path is null, empty, or whitespace.</exception>
    public static OmsiMapManifest Scan(string mapDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapDirectory);

        var diagnostics = new List<MapDiagnostic>();

        string root;

        try
        {
            root = Path.GetFullPath(mapDirectory);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failed(
                mapDirectory,
                diagnostics,
                $"The map directory path is not valid: {exception.Message}");
        }

        if (!Directory.Exists(root))
        {
            return Failed(
                root,
                diagnostics,
                $"The map directory does not exist or is not accessible: {root}");
        }

        List<string> files;

        try
        {
            files = ListFiles(root);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Failed(
                root,
                diagnostics,
                $"The map directory could not be enumerated: {exception.Message}");
        }

        if (files.Count > MaxRootFiles)
        {
            return Failed(
                root,
                diagnostics,
                $"The map directory contains more than {MaxRootFiles} files, which " +
                "exceeds the discovery limit. Point the scanner at the map folder " +
                "itself, not at a content folder above it.");
        }

        files.Sort(StringComparer.Ordinal);

        string? globalConfigPath = SelectGlobalConfig(root, files, diagnostics);

        if (globalConfigPath is null)
        {
            return Failed(
                root,
                diagnostics,
                $"No {GlobalConfigFileName} was found directly inside {root}.");
        }

        GlobalConfigParseResult parsed = GlobalConfigParser.Parse(globalConfigPath);
        diagnostics.AddRange(parsed.Diagnostics);

        if (parsed.Data is null)
        {
            return Failed(
                root,
                diagnostics,
                message: null,
                globalConfigPath: globalConfigPath);
        }

        List<OmsiMapTile> tiles = DiscoverTiles(files, root, diagnostics);

        if (tiles.Count == 0)
        {
            diagnostics.Add(new MapDiagnostic(
                MapDiagnosticSeverity.Warning,
                $"No tile files matching tile_<x>_<y>.map were found in {root}."));
        }

        if (parsed.Data.Name is null)
        {
            diagnostics.Add(new MapDiagnostic(
                MapDiagnosticSeverity.Warning,
                "global.cfg has no usable [name] block, so the map name is " +
                "unavailable.",
                globalConfigPath));
        }

        MapScanStatus status = DetermineStatus(diagnostics);

        return new OmsiMapManifest(
            root,
            globalConfigPath,
            parsed.Data.EncodingName,
            parsed.Data.Name,
            parsed.Data.FriendlyName,
            parsed.Data.UsesWorldCoordinates,
            tiles,
            parsed.Data.UnsupportedDirectives,
            diagnostics,
            status);
    }

    private static List<string> ListFiles(string root)
    {
        var files = new List<string>();

        foreach (string file in Directory.EnumerateFiles(
                     root,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            files.Add(file);

            if (files.Count > MaxRootFiles)
            {
                break;
            }
        }

        return files;
    }

    /// <summary>
    /// Finds <c>global.cfg</c>, comparing the name without regard to case because
    /// OMSI resolves content file names that way, and reports ambiguous variants.
    /// </summary>
    private static string? SelectGlobalConfig(
        string root,
        IReadOnlyList<string> files,
        List<MapDiagnostic> diagnostics)
    {
        var matches = new List<string>();

        foreach (string file in files)
        {
            if (string.Equals(
                    Path.GetFileName(file),
                    GlobalConfigFileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(file);
            }
        }

        if (matches.Count == 0)
        {
            return null;
        }

        if (matches.Count > 1)
        {
            string chosen = Path.GetFileName(matches[0]);
            var others = new List<string>();

            for (int index = 1; index < matches.Count; index++)
            {
                others.Add(Path.GetFileName(matches[index]));
            }

            diagnostics.Add(new MapDiagnostic(
                MapDiagnosticSeverity.Warning,
                $"Several files match {GlobalConfigFileName} when compared without " +
                $"regard to case. Using '{chosen}' and ignoring: " +
                $"{string.Join(", ", others)}.",
                root));
        }

        return matches[0];
    }

    private static List<OmsiMapTile> DiscoverTiles(
        IReadOnlyList<string> files,
        string root,
        List<MapDiagnostic> diagnostics)
    {
        var tiles = new List<OmsiMapTile>();
        var byCoordinate = new Dictionary<(int X, int Y), OmsiMapTile>();
        int candidateCount = 0;

        foreach (string file in files)
        {
            string fileName = Path.GetFileName(file);

            if (!fileName.EndsWith(TileFileNames.Extension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TileFileNames.TryParse(fileName, out int x, out int y))
            {
                string message = TileFileNames.HasTilePrefix(fileName)
                    ? $"The file '{fileName}' does not match the documented " +
                      "tile_<x>_<y>.map convention and is ignored."
                    : $"The file '{fileName}' is not a tile file " +
                      "(tile_<x>_<y>.map) and is ignored.";

                diagnostics.Add(new MapDiagnostic(
                    MapDiagnosticSeverity.Warning,
                    message,
                    file));
                continue;
            }

            candidateCount++;

            if (candidateCount > MaxTileFiles)
            {
                diagnostics.Add(new MapDiagnostic(
                    MapDiagnosticSeverity.Error,
                    $"The map folder contains more than {MaxTileFiles} tile files, " +
                    "which exceeds the discovery limit.",
                    root));
                return [];
            }

            if (byCoordinate.TryGetValue((x, y), out OmsiMapTile? existing))
            {
                diagnostics.Add(new MapDiagnostic(
                    MapDiagnosticSeverity.Warning,
                    $"Tile coordinates ({x},{y}) are provided by both " +
                    $"'{existing.FileName}' and '{fileName}'. Keeping " +
                    $"'{existing.FileName}' for deterministic results.",
                    file));
                continue;
            }

            bool isReadable = TryOpenForRead(file, out string? readError);

            if (!isReadable)
            {
                diagnostics.Add(new MapDiagnostic(
                    MapDiagnosticSeverity.Warning,
                    $"The tile file '{fileName}' could not be read: {readError}",
                    file));
            }

            var tile = new OmsiMapTile(x, y, fileName, file, isReadable);
            byCoordinate.Add((x, y), tile);
            tiles.Add(tile);
        }

        tiles.Sort(CompareTiles);
        return tiles;
    }

    private static int CompareTiles(OmsiMapTile left, OmsiMapTile right)
    {
        int byColumn = left.X.CompareTo(right.X);

        if (byColumn != 0)
        {
            return byColumn;
        }

        int byRow = left.Y.CompareTo(right.Y);

        return byRow != 0
            ? byRow
            : string.CompareOrdinal(left.FileName, right.FileName);
    }

    private static bool TryOpenForRead(string path, out string? error)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);
            error = null;
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static MapScanStatus DetermineStatus(List<MapDiagnostic> diagnostics)
    {
        bool hasWarning = false;

        foreach (MapDiagnostic diagnostic in diagnostics)
        {
            if (diagnostic.Severity == MapDiagnosticSeverity.Error)
            {
                return MapScanStatus.Failed;
            }

            hasWarning = true;
        }

        return hasWarning ? MapScanStatus.Partial : MapScanStatus.Complete;
    }

    private static OmsiMapManifest Failed(
        string mapRoot,
        List<MapDiagnostic> diagnostics,
        string? message,
        string? path = null,
        string? globalConfigPath = null)
    {
        if (message is not null)
        {
            diagnostics.Add(new MapDiagnostic(
                MapDiagnosticSeverity.Error,
                message,
                path));
        }

        return new OmsiMapManifest(
            mapRoot,
            globalConfigPath,
            textEncodingName: null,
            name: null,
            friendlyName: null,
            usesWorldCoordinates: false,
            tiles: [],
            unsupportedDirectives: [],
            diagnostics,
            MapScanStatus.Failed);
    }
}

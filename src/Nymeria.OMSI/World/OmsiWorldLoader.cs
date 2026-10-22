using System.Globalization;
using Nymeria.Omsi.Maps;

namespace Nymeria.Omsi.World;

/// <summary>
/// Builds a read-only world-data model from an explicitly named OMSI map folder.
/// </summary>
/// <remarks>
/// <para>
/// This is the Task 03 entry point. It reuses the OMSI-02 discovery and encoding
/// layer, parses each discovered tile's documented placement records, probes the
/// terrain sidecar metadata, and reports per-tile and map-level diagnostics.
/// </para>
/// <para>
/// It creates no window, GPU device, or SDL3 state, resolves no asset reference,
/// and never writes to the map folder. Turning the model into a Nymeria scene is a
/// separate task.
/// </para>
/// </remarks>
public static class OmsiWorldLoader
{
    /// <summary>
    /// The documented terrain sidecar layout: a 4-byte value followed by a
    /// 61 x 61 grid of 32-bit heights.
    /// </summary>
    internal const long ExpectedTerrainFileLength = 4 + 61 * 61 * 4;

    private const string TerrainExtension = ".terrain";

    /// <summary>
    /// Loads the world data of one map folder.
    /// </summary>
    /// <param name="mapDirectory">
    /// The folder that contains <c>global.cfg</c>. It is used exactly as given; no
    /// installation path is searched.
    /// </param>
    /// <returns>
    /// A world model that always describes the attempt. Diagnostics explain a
    /// partial or failed result, and the status is never reported as complete when
    /// warnings remain.
    /// </returns>
    /// <exception cref="ArgumentException">The path is null, empty, or whitespace.</exception>
    public static OmsiWorldModel Load(string mapDirectory)
    {
        OmsiMapManifest manifest = OmsiMapDiscovery.Scan(mapDirectory);

        var tiles = new List<OmsiWorldTile>();
        var diagnostics = new List<MapDiagnostic>(manifest.Diagnostics);

        foreach (OmsiMapTile discovered in manifest.Tiles)
        {
            var tileDiagnostics = new List<MapDiagnostic>();
            TileParseResult? parsed = null;

            ConfigReadResult read = ConfigTextReader.Read(
                discovered.FilePath,
                ConfigTextReader.MaxTileFileBytes,
                ConfigTextReader.MaxTileLineCount);

            tileDiagnostics.AddRange(read.Diagnostics);

            if (read.Text is not null)
            {
                parsed = TileWorldParser.Parse(read.Text, discovered.FilePath);
                tileDiagnostics.AddRange(parsed.Diagnostics);
            }

            OmsiTerrainReference? terrain =
                ReadTerrainReference(discovered.FilePath, tileDiagnostics);

            diagnostics.AddRange(tileDiagnostics);

            tiles.Add(new OmsiWorldTile(
                discovered.X,
                discovered.Y,
                discovered.FilePath,
                parsed?.Version,
                parsed?.IsParsed ?? false,
                parsed?.Objects ?? [],
                parsed?.Splines ?? [],
                terrain,
                parsed?.UnsupportedRecords ?? [],
                tileDiagnostics));
        }

        bool hasError = diagnostics.Exists(
            diagnostic => diagnostic.Severity == MapDiagnosticSeverity.Error);

        if (!hasError && tiles.Count > 0 && tiles.TrueForAll(tile => !tile.IsParsed))
        {
            diagnostics.Add(new MapDiagnostic(
                MapDiagnosticSeverity.Error,
                "None of the discovered tiles could be interpreted, so the map has " +
                "no usable world data. The per-tile diagnostics say why.",
                manifest.MapRoot));
            hasError = true;
        }

        MapScanStatus status = DetermineStatus(manifest, tiles, diagnostics, hasError);

        return new OmsiWorldModel(
            manifest.MapRoot,
            manifest.GlobalConfigPath,
            manifest.Name,
            manifest.FriendlyName,
            tiles,
            diagnostics,
            status);
    }

    private static MapScanStatus DetermineStatus(
        OmsiMapManifest manifest,
        List<OmsiWorldTile> tiles,
        List<MapDiagnostic> diagnostics,
        bool hasError)
    {
        if (manifest.Status == MapScanStatus.Failed || hasError)
        {
            return MapScanStatus.Failed;
        }

        if (diagnostics.Count > 0 || tiles.Exists(tile => !tile.IsParsed))
        {
            return MapScanStatus.Partial;
        }

        return MapScanStatus.Complete;
    }

    /// <summary>
    /// Reports the terrain sidecar's existence and size without decoding heights.
    /// </summary>
    private static OmsiTerrainReference? ReadTerrainReference(
        string tilePath,
        List<MapDiagnostic> diagnostics)
    {
        string terrainPath = tilePath + TerrainExtension;

        try
        {
            var file = new FileInfo(terrainPath);

            if (!file.Exists)
            {
                return null;
            }

            long length = file.Length;
            bool hasDocumentedSize = length == ExpectedTerrainFileLength;

            if (!hasDocumentedSize)
            {
                diagnostics.Add(new MapDiagnostic(
                    MapDiagnosticSeverity.Warning,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "The terrain sidecar is {0} bytes; the documented layout is " +
                        "{1} bytes (a 4-byte value and 61 x 61 32-bit heights). Its " +
                        "heights are not decoded in this task.",
                        length,
                        ExpectedTerrainFileLength),
                    terrainPath));
            }

            return new OmsiTerrainReference(terrainPath, length, hasDocumentedSize);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            diagnostics.Add(new MapDiagnostic(
                MapDiagnosticSeverity.Warning,
                $"The terrain sidecar could not be inspected: {exception.Message}",
                terrainPath));
            return null;
        }
    }
}

using System.Globalization;
using Nymeria.Omsi.Maps;
using Nymeria.Omsi.World;

namespace Nymeria.Omsi.Terrain;

/// <summary>
/// The decoded terrain height grids of one parsed map.
/// </summary>
/// <remarks>
/// <para>
/// This is the Task 05 entry point for terrain data. It walks the tiles the Task 03
/// parser discovered, decodes each tile's <c>.map.terrain</c> sidecar, and records a
/// diagnostic for every tile whose terrain is missing, malformed, truncated, or
/// unsupported. A tile with a rejected sidecar contributes no geometry; nothing is
/// invented in its place.
/// </para>
/// <para>
/// It reads only files directly beside an already discovered tile, creates no
/// window, GPU device, or SDL3 state, and writes nothing. Decoding does not depend
/// on the tile's record version: the sidecar carries its own grid, and the tile
/// coordinates it is placed at come from the file name.
/// </para>
/// </remarks>
internal sealed class OmsiTerrainSet
{
    /// <summary>
    /// The largest number of tiles decoded into height grids. It bounds the memory a
    /// hostile or broken map can commit; a real map such as Grundorf has 19 terrain
    /// sidecars and Berlin-Spandau has 329.
    /// </summary>
    internal const int MaxDecodedTiles = 1024;

    private OmsiTerrainSet(
        double tileEdgeMeters,
        bool usesDocumentedTileEdge,
        IReadOnlyList<OmsiTerrainHeightmap> heightmaps,
        IReadOnlyList<MapDiagnostic> diagnostics,
        int missingTerrainTiles,
        int rejectedTerrainTiles,
        int omittedTerrainTiles)
    {
        TileEdgeMeters = tileEdgeMeters;
        UsesDocumentedTileEdge = usesDocumentedTileEdge;
        Heightmaps = [.. heightmaps];
        Diagnostics = [.. diagnostics];
        MissingTerrainTiles = missingTerrainTiles;
        RejectedTerrainTiles = rejectedTerrainTiles;
        OmittedTerrainTiles = omittedTerrainTiles;
    }

    /// <summary>Gets the tile edge in metres used to place the grids.</summary>
    internal double TileEdgeMeters { get; }

    /// <summary>
    /// Gets a value indicating whether <see cref="TileEdgeMeters"/> is the documented
    /// 300 m constant rather than the reference-measured degree grid.
    /// </summary>
    internal bool UsesDocumentedTileEdge { get; }

    /// <summary>Gets the decoded grids, in tile discovery order.</summary>
    internal IReadOnlyList<OmsiTerrainHeightmap> Heightmaps { get; }

    /// <summary>Gets the decode diagnostics, in tile discovery order.</summary>
    internal IReadOnlyList<MapDiagnostic> Diagnostics { get; }

    /// <summary>Gets how many discovered tiles have no terrain sidecar at all.</summary>
    internal int MissingTerrainTiles { get; }

    /// <summary>
    /// Gets how many sidecars existed but were rejected by decoding, including the
    /// ones omitted by <see cref="MaxDecodedTiles"/>.
    /// </summary>
    internal int RejectedTerrainTiles { get; }

    /// <summary>
    /// Gets how many otherwise usable grids were dropped by
    /// <see cref="MaxDecodedTiles"/>.
    /// </summary>
    internal int OmittedTerrainTiles { get; }

    /// <summary>
    /// Decodes the terrain of every tile in a parsed world.
    /// </summary>
    /// <param name="world">The world model produced by the tile parser.</param>
    /// <returns>
    /// The decoded grids and their diagnostics. An empty result is not an exception:
    /// the diagnostics say why, and the caller refuses to open a window.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> is null.</exception>
    internal static OmsiTerrainSet Load(OmsiWorldModel world)
    {
        ArgumentNullException.ThrowIfNull(world);

        double edge = OmsiTileGrid.EdgeMeters(world.UsesWorldCoordinates);

        var heightmaps = new List<OmsiTerrainHeightmap>();
        var diagnostics = new List<MapDiagnostic>();
        int missing = 0;
        int rejected = 0;
        int omitted = 0;

        foreach (OmsiWorldTile tile in world.Tiles)
        {
            if (tile.Terrain is not OmsiTerrainReference terrain)
            {
                missing++;
                diagnostics.Add(new MapDiagnostic(
                    MapDiagnosticSeverity.Warning,
                    "The tile has no terrain sidecar, so no terrain is generated " +
                    "for it. The tile's own content is unaffected.",
                    tile.FilePath));
                continue;
            }

            if (heightmaps.Count >= MaxDecodedTiles)
            {
                omitted++;
                continue;
            }

            string? readError = TryReadPayload(terrain.FilePath, out byte[]? payload);

            if (readError is not null)
            {
                rejected++;
                diagnostics.Add(new MapDiagnostic(
                    MapDiagnosticSeverity.Warning,
                    readError,
                    terrain.FilePath));
                continue;
            }

            if (!OmsiTerrainDecoder.TryDecode(
                    terrain.FilePath,
                    payload,
                    tile.X,
                    tile.Y,
                    edge,
                    out OmsiTerrainHeightmap? heightmap,
                    out string? error) || heightmap is null)
            {
                rejected++;
                diagnostics.Add(new MapDiagnostic(
                    MapDiagnosticSeverity.Warning,
                    error ?? "The terrain sidecar could not be decoded.",
                    terrain.FilePath));
                continue;
            }

            heightmaps.Add(heightmap);
        }

        if (omitted > 0)
        {
            diagnostics.Add(new MapDiagnostic(
                MapDiagnosticSeverity.Warning,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} terrain sidecar(s) beyond the {1}-tile limit were not " +
                    "decoded, so their tiles have no terrain in this view.",
                    omitted,
                    MaxDecodedTiles)));
        }

        if (missing > 0)
        {
            diagnostics.Add(new MapDiagnostic(
                MapDiagnosticSeverity.Warning,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} of {1} discovered tile(s) have no terrain sidecar. Their " +
                    "terrain cannot be shown, because none is documented for them.",
                    missing,
                    world.Tiles.Count)));
        }

        return new OmsiTerrainSet(
            edge,
            OmsiTileGrid.UsesDocumentedEdge(world.UsesWorldCoordinates),
            heightmaps,
            diagnostics,
            missing,
            rejected,
            omitted);
    }

    /// <summary>
    /// Reads exactly the documented payload length, refusing a file of another size
    /// before allocating anything for it.
    /// </summary>
    /// <returns>A readable reason when the file cannot be used, otherwise null.</returns>
    private static string? TryReadPayload(string path, out byte[]? payload)
    {
        payload = null;

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                FileOptions.SequentialScan);

            if (stream.Length != OmsiTerrainDecoder.DocumentedFileLength)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "The terrain sidecar is {0} bytes; the documented layout is {1} " +
                    "bytes (a 4-byte value and 61 x 61 32-bit heights), so its " +
                    "heights were not decoded and no terrain is generated for this " +
                    "tile.",
                    stream.Length,
                    OmsiTerrainDecoder.DocumentedFileLength);
            }

            var bytes = new byte[OmsiTerrainDecoder.DocumentedFileLength];
            stream.ReadExactly(bytes);
            payload = bytes;
            return null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                NotSupportedException)
        {
            return $"The terrain sidecar could not be read: {exception.Message}";
        }
    }
}

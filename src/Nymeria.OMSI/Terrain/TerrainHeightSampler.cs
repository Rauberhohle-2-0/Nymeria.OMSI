namespace Nymeria.Omsi.Terrain;

/// <summary>
/// Samples decoded terrain heights at arbitrary OMSI map positions.
/// </summary>
/// <remarks>
/// <para>
/// The terrain mesh draws each tile from its own 61 x 61 height grid. Object placement
/// needs the same surface at a point that usually does not land on a grid sample, so
/// this type looks up the tile that contains the point and interpolates bilinearly
/// between its four surrounding samples. Both grid axes increase with OMSI x and y,
/// exactly as the mesh builds them, so a sampled height and the drawn surface agree.
/// </para>
/// <para>
/// A point outside every decoded tile has no height. It is reported as unsampled
/// rather than treated as zero, so a caller cannot silently place an object at sea
/// level on a tile whose terrain is missing.
/// </para>
/// <para>
/// This is CPU-only terrain data: no engine, SDL, or GPU type is referenced.
/// </para>
/// </remarks>
internal sealed class TerrainHeightSampler
{
    private readonly Dictionary<(int X, int Y), OmsiTerrainHeightmap> _tiles;
    private readonly double _tileEdgeMeters;

    private TerrainHeightSampler(
        Dictionary<(int X, int Y), OmsiTerrainHeightmap> tiles,
        double tileEdgeMeters)
    {
        _tiles = tiles;
        _tileEdgeMeters = tileEdgeMeters;
    }

    /// <summary>Gets the number of tiles that can be sampled.</summary>
    internal int TileCount => _tiles.Count;

    /// <summary>
    /// Builds a sampler for the decoded tiles of a terrain set.
    /// </summary>
    /// <param name="terrain">The decoded terrain.</param>
    /// <returns>
    /// A sampler over every decoded tile. Duplicate tile coordinates keep the first
    /// tile, which cannot normally happen because tile files are unique.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="terrain"/> is null.</exception>
    internal static TerrainHeightSampler Create(OmsiTerrainSet terrain)
    {
        ArgumentNullException.ThrowIfNull(terrain);

        return Create(terrain.Heightmaps, terrain.TileEdgeMeters);
    }

    /// <summary>
    /// Builds a sampler from decoded grids and the tile edge they were placed with.
    /// </summary>
    /// <param name="heightmaps">The decoded grids.</param>
    /// <param name="tileEdgeMeters">The tile edge the grids were built for.</param>
    /// <returns>A sampler over every grid.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="heightmaps"/> is null.</exception>
    internal static TerrainHeightSampler Create(
        IReadOnlyList<OmsiTerrainHeightmap> heightmaps,
        double tileEdgeMeters)
    {
        ArgumentNullException.ThrowIfNull(heightmaps);

        var tiles = new Dictionary<(int X, int Y), OmsiTerrainHeightmap>(
            heightmaps.Count);

        foreach (OmsiTerrainHeightmap heightmap in heightmaps)
        {
            tiles.TryAdd((heightmap.TileX, heightmap.TileY), heightmap);
        }

        return new TerrainHeightSampler(tiles, tileEdgeMeters);
    }

    /// <summary>
    /// Samples the terrain height at an OMSI map position.
    /// </summary>
    /// <param name="omsiX">The OMSI x coordinate in metres.</param>
    /// <param name="omsiY">The OMSI y coordinate in metres.</param>
    /// <param name="height">The interpolated height in metres, when sampled.</param>
    /// <returns>
    /// <see langword="true"/> when a decoded tile covers the position.
    /// </returns>
    internal bool TrySample(double omsiX, double omsiY, out float height)
    {
        height = 0.0f;

        if (!double.IsFinite(omsiX) || !double.IsFinite(omsiY) ||
            !double.IsFinite(_tileEdgeMeters) || _tileEdgeMeters <= 0.0)
        {
            return false;
        }

        double columnValue = Math.Floor(omsiX / _tileEdgeMeters);
        double rowValue = Math.Floor(omsiY / _tileEdgeMeters);

        if (columnValue is < int.MinValue or > int.MaxValue ||
            rowValue is < int.MinValue or > int.MaxValue)
        {
            return false;
        }

        int column = (int)columnValue;
        int row = (int)rowValue;

        if (!_tiles.TryGetValue((column, row), out OmsiTerrainHeightmap? tile))
        {
            return false;
        }

        double spacing = tile.SampleSpacingMeters;
        double localX = omsiX - (column * _tileEdgeMeters);
        double localY = omsiY - (row * _tileEdgeMeters);

        float gridX = Clamp((float)(localX / spacing));
        float gridY = Clamp((float)(localY / spacing));

        int left = (int)MathF.Floor(gridX);
        int top = (int)MathF.Floor(gridY);
        int right = Math.Min(left + 1, OmsiTerrainHeightmap.CellsPerEdge);
        int bottom = Math.Min(top + 1, OmsiTerrainHeightmap.CellsPerEdge);

        left = Math.Min(left, OmsiTerrainHeightmap.CellsPerEdge);
        top = Math.Min(top, OmsiTerrainHeightmap.CellsPerEdge);

        float fractionX = gridX - left;
        float fractionY = gridY - top;

        float topLeft = tile.HeightAt(top, left);
        float topRight = tile.HeightAt(top, right);
        float bottomLeft = tile.HeightAt(bottom, left);
        float bottomRight = tile.HeightAt(bottom, right);

        float upper = topLeft + ((topRight - topLeft) * fractionX);
        float lower = bottomLeft + ((bottomRight - bottomLeft) * fractionX);

        height = upper + ((lower - upper) * fractionY);

        return float.IsFinite(height);
    }

    private static float Clamp(float value)
    {
        float maximum = OmsiTerrainHeightmap.CellsPerEdge;

        if (!float.IsFinite(value))
        {
            return 0.0f;
        }

        return Math.Clamp(value, 0.0f, maximum);
    }
}

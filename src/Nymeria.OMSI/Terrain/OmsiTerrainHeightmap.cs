namespace Nymeria.Omsi.Terrain;

/// <summary>
/// One decoded OMSI terrain height grid: a square grid of heights that covers one
/// map tile.
/// </summary>
/// <remarks>
/// <para>
/// The documented sidecar layout is a <c>u32</c> header holding 60 followed by
/// <c>61 x 61</c> 32-bit heights. Sixty is the number of cells along one edge, so
/// the grid has 61 samples per edge and the samples sit on the tile's own
/// boundaries: a sample spacing of <c>tileEdge / 60</c> metres, which is exactly
/// 5 m for the documented 300 m tile.
/// </para>
/// <para>
/// Rows and columns are stored exactly as the file writes them, without a flip or
/// a rotation. Row 0 is taken as the tile's local <c>y = 0</c> edge and column 0 as
/// its local <c>x = 0</c> edge; that orientation is an assumption, because no
/// reference states it, and it is reported as unverified rather than presented as
/// a documented fact.
/// </para>
/// <para>
/// Heights are the file's own 32-bit floating-point metres. Nothing is scaled,
/// offset, or re-based on a map-wide minimum. This type holds no engine, SDL3, GPU,
/// or mesh type, so decoding is tested without a window or a device.
/// </para>
/// </remarks>
internal sealed class OmsiTerrainHeightmap
{
    /// <summary>The number of height samples along one tile edge.</summary>
    internal const int SamplesPerEdge = 61;

    /// <summary>
    /// The number of cells along one tile edge, which is the documented header
    /// value.
    /// </summary>
    internal const int CellsPerEdge = SamplesPerEdge - 1;

    /// <summary>The total number of stored height samples.</summary>
    internal const int SampleCount = SamplesPerEdge * SamplesPerEdge;

    private readonly float[] _heights;

    /// <summary>
    /// Creates a height grid from an already validated sample array.
    /// </summary>
    /// <param name="tileX">The tile column from the discovered file name.</param>
    /// <param name="tileY">The tile row from the discovered file name.</param>
    /// <param name="filePath">The sidecar file the samples came from.</param>
    /// <param name="heights">Exactly <see cref="SampleCount"/> row-major heights.</param>
    /// <param name="tileEdgeMeters">The tile edge in metres.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="heights"/> does not hold exactly <see cref="SampleCount"/>
    /// finite samples.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="tileEdgeMeters"/> is not a positive finite number.
    /// </exception>
    internal OmsiTerrainHeightmap(
        int tileX,
        int tileY,
        string filePath,
        float[] heights,
        double tileEdgeMeters)
    {
        ArgumentNullException.ThrowIfNull(heights);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (heights.Length != SampleCount)
        {
            throw new ArgumentException(
                $"A terrain grid holds exactly {SampleCount} samples, but " +
                $"{heights.Length} were supplied.",
                nameof(heights));
        }

        if (!double.IsFinite(tileEdgeMeters) || tileEdgeMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tileEdgeMeters),
                tileEdgeMeters,
                "The tile edge must be a positive finite number of metres.");
        }

        float minimum = float.PositiveInfinity;
        float maximum = float.NegativeInfinity;

        for (int index = 0; index < heights.Length; index++)
        {
            float height = heights[index];

            if (!float.IsFinite(height))
            {
                throw new ArgumentException(
                    $"Height sample {index} is {height} and must be finite.",
                    nameof(heights));
            }

            minimum = MathF.Min(minimum, height);
            maximum = MathF.Max(maximum, height);
        }

        _heights = (float[])heights.Clone();

        TileX = tileX;
        TileY = tileY;
        FilePath = filePath;
        TileEdgeMeters = tileEdgeMeters;
        MinHeight = minimum;
        MaxHeight = maximum;
    }

    /// <summary>Gets the tile column from the discovered file name.</summary>
    internal int TileX { get; }

    /// <summary>Gets the tile row from the discovered file name.</summary>
    internal int TileY { get; }

    /// <summary>Gets the sidecar path these heights were decoded from.</summary>
    internal string FilePath { get; }

    /// <summary>Gets the tile edge in metres.</summary>
    internal double TileEdgeMeters { get; }

    /// <summary>
    /// Gets the distance between two neighbouring samples, in metres:
    /// <see cref="TileEdgeMeters"/> divided by <see cref="CellsPerEdge"/>.
    /// </summary>
    internal double SampleSpacingMeters => TileEdgeMeters / CellsPerEdge;

    /// <summary>Gets the lowest decoded height, in metres.</summary>
    internal float MinHeight { get; }

    /// <summary>Gets the highest decoded height, in metres.</summary>
    internal float MaxHeight { get; }

    /// <summary>
    /// Reads one height sample in file order.
    /// </summary>
    /// <param name="row">The row, from 0 to <see cref="SamplesPerEdge"/> - 1.</param>
    /// <param name="column">The column, from 0 to <see cref="SamplesPerEdge"/> - 1.</param>
    /// <returns>The height in metres.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The position is off the grid.</exception>
    internal float HeightAt(int row, int column)
    {
        if ((uint)row >= SamplesPerEdge)
        {
            throw new ArgumentOutOfRangeException(
                nameof(row),
                row,
                $"Rows run from 0 to {SamplesPerEdge - 1}.");
        }

        if ((uint)column >= SamplesPerEdge)
        {
            throw new ArgumentOutOfRangeException(
                nameof(column),
                column,
                $"Columns run from 0 to {SamplesPerEdge - 1}.");
        }

        return _heights[(row * SamplesPerEdge) + column];
    }

}

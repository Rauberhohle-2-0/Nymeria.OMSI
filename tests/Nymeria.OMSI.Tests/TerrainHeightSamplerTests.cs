using Nymeria.Omsi.Terrain;

namespace Nymeria.OMSI.Tests;

/// <summary>
/// Headless tests for terrain height sampling under scenery placements.
/// </summary>
/// <remarks>
/// The grids are synthetic and independently authored here; they are not OMSI content.
/// </remarks>
public sealed class TerrainHeightSamplerTests
{
    private const double TileEdge = 300.0;

    [Fact]
    public void SamplesASamplePointExactly()
    {
        TerrainHeightSampler sampler = Sampler(Grid(0, 0, height: 7.5f));

        Assert.True(sampler.TrySample(0.0, 0.0, out float height));
        Assert.Equal(7.5f, height, precision: 4);
    }

    [Fact]
    public void InterpolatesBilinearlyBetweenSamples()
    {
        // Height rises 5 m per grid column (5 m spacing) and 0.5 m per row.
        var heights = new float[OmsiTerrainHeightmap.SampleCount];

        for (int row = 0; row < 61; row++)
        {
            for (int column = 0; column < 61; column++)
            {
                heights[(row * 61) + column] = (column * 5.0f) + (row * 0.5f);
            }
        }

        TerrainHeightSampler sampler = Sampler(
            new OmsiTerrainHeightmap(0, 0, "memory.terrain", heights, TileEdge));

        // Half a cell along both axes: the mean of the four surrounding samples
        // (0, 5, 0.5, 5.5) is 2.75.
        Assert.True(sampler.TrySample(2.5, 2.5, out float height));
        Assert.Equal(2.75f, height, precision: 4);
    }

    [Fact]
    public void UsesTheTileThatContainsThePosition()
    {
        TerrainHeightSampler sampler = Sampler(
            Grid(tileX: 0, tileY: 0, height: 1.0f),
            Grid(tileX: 1, tileY: 0, height: 2.0f),
            Grid(tileX: 0, tileY: 1, height: 3.0f));

        Assert.True(sampler.TrySample(150.0, 150.0, out float positive));
        Assert.True(sampler.TrySample(315.0, 150.0, out float nextColumn));
        Assert.True(sampler.TrySample(150.0, 315.0, out float nextRow));

        Assert.Equal(1.0f, positive, precision: 4);
        Assert.Equal(2.0f, nextColumn, precision: 4);
        Assert.Equal(3.0f, nextRow, precision: 4);
    }

    [Fact]
    public void ReportsPositionsOutsideDecodedTerrainAsUnsampled()
    {
        TerrainHeightSampler sampler = Sampler(Grid(0, 0, height: 1.0f));

        Assert.False(sampler.TrySample(400.0, 0.0, out float height));
        Assert.Equal(0.0f, height);
    }

    [Fact]
    public void RejectsNonFinitePositions()
    {
        TerrainHeightSampler sampler = Sampler(Grid(0, 0, height: 1.0f));

        Assert.False(sampler.TrySample(double.NaN, 0.0, out _));
        Assert.False(sampler.TrySample(0.0, double.PositiveInfinity, out _));
    }

    [Fact]
    public void TheTileEdgeIsTheBoundaryBetweenTiles()
    {
        TerrainHeightSampler sampler = Sampler(
            Grid(tileX: 0, tileY: 0, height: 1.0f),
            Grid(tileX: 1, tileY: 0, height: 9.0f));

        Assert.True(sampler.TrySample(TileEdge - 0.01, 0.0, out float endOfTile));
        Assert.True(sampler.TrySample(TileEdge, 0.0, out float startOfNextTile));

        Assert.Equal(1.0f, endOfTile, precision: 3);
        Assert.Equal(9.0f, startOfNextTile, precision: 3);
    }

    private static TerrainHeightSampler Sampler(params OmsiTerrainHeightmap[] grids)
    {
        return TerrainHeightSampler.Create(grids, TileEdge);
    }

    private static OmsiTerrainHeightmap Grid(int tileX, int tileY, float height)
    {
        var heights = new float[OmsiTerrainHeightmap.SampleCount];
        Array.Fill(heights, height);

        return new OmsiTerrainHeightmap(tileX, tileY, "memory.terrain", heights, TileEdge);
    }
}

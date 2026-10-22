using Nymeria.Omsi.Maps;

namespace Nymeria.OMSI.Tests;

/// <summary>
/// Headless tests for the <c>tile_x_y.map</c> convention.
/// </summary>
public sealed class TileFileNamesTests
{
    [Theory]
    [InlineData("tile_0_-1.map", 0, -1)]
    [InlineData("tile_0_-2.map", 0, -2)]
    [InlineData("tile_12_3.map", 12, 3)]
    [InlineData("TILE_7_-8.MAP", 7, -8)]
    [InlineData("tile_-2147483648_2147483647.map", int.MinValue, int.MaxValue)]
    [InlineData("tile_00_01.map", 0, 1)]
    public void ParsesDocumentedCoordinates(string fileName, int expectedX, int expectedY)
    {
        bool parsed = TileFileNames.TryParse(fileName, out int x, out int y);

        Assert.True(parsed);
        Assert.Equal(expectedX, x);
        Assert.Equal(expectedY, y);
    }

    [Theory]
    [InlineData("tile_0_1")]
    [InlineData("tile_0_1.mapx")]
    [InlineData("tile_0.map")]
    [InlineData("tile_0_1_2.map")]
    [InlineData("tile__1.map")]
    [InlineData("tile_1_.map")]
    [InlineData("tile_1.5_2.map")]
    [InlineData("tile_1_2.5.map")]
    [InlineData("tile_+_1.map")]
    [InlineData("tile_2147483648_0.map")]
    [InlineData("tile_0_1.map.terrain")]
    [InlineData("mytile_0_1.map")]
    [InlineData("global.cfg")]
    public void RejectsNamesOutsideTheConvention(string fileName)
    {
        Assert.False(TileFileNames.TryParse(fileName, out _, out _));
    }

    [Theory]
    [InlineData("tile_0_1.map")]
    [InlineData("TILE_0_1.MAP")]
    [InlineData("tile_broken.map")]
    public void HasTilePrefixRecognizesCandidatesForDiagnostics(string fileName)
    {
        Assert.True(TileFileNames.HasTilePrefix(fileName));
    }

    [Fact]
    public void HasTilePrefixIgnoresOtherFiles()
    {
        Assert.False(TileFileNames.HasTilePrefix("global.cfg"));
        Assert.False(TileFileNames.HasTilePrefix("sidecar.map.terrain"));
    }
}

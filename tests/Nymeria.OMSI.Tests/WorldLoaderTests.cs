using Nymeria.Omsi.Maps;
using Nymeria.Omsi.World;

namespace Nymeria.OMSI.Tests;

/// <summary>
/// Headless tests for world loading. The map folders are synthetic strings written
/// by these tests, never an OMSI installation, and nothing here proves that a real
/// map loads.
/// </summary>
public sealed class WorldLoaderTests
{
    private const string GlobalConfig =
        "[name]\r\n" +
        "Test Map\r\n" +
        "[friendlyname]\r\n" +
        "Test Map Friendly\r\n";

    private const string Version12Tile =
        "[version]\r\n" +
        "12\r\n" +
        "[object]\r\n" +
        "0\r\n" +
        "Sceneryobjects\\Test\\Box.sco\r\n" +
        "1\r\n" +
        "10\r\n" +
        "20\r\n" +
        "3\r\n" +
        "45\r\n" +
        "0\r\n" +
        "0\r\n" +
        "0\r\n" +
        "[spline]\r\n" +
        "0\r\n" +
        "Splines\\Test\\Road.sli\r\n" +
        "2\r\n" +
        "-1\r\n" +
        "-1\r\n" +
        "1\r\n" +
        "2\r\n" +
        "3\r\n" +
        "0\r\n" +
        "20\r\n" +
        "0\r\n";

    [Fact]
    public void CompleteSyntheticMapLoadsEveryTileInManifestOrder()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf16Le("global.cfg", GlobalConfig);
        directory.WriteUtf16Le("tile_1_0.map", Version12Tile);
        directory.WriteUtf16Le("tile_0_0.map", Version12Tile);
        WriteTerrain(directory, "tile_0_0.map.terrain");
        WriteTerrain(directory, "tile_1_0.map.terrain");

        OmsiWorldModel world = OmsiWorldLoader.Load(directory.Path);

        Assert.Equal(MapScanStatus.Complete, world.Status);
        Assert.Empty(world.Diagnostics);
        Assert.Equal("Test Map", world.Name);
        Assert.Equal("Test Map Friendly", world.FriendlyName);
        Assert.Equal(directory.Path, world.MapRoot);

        Assert.Equal(new[] { "0,0", "1,0" }, world.Tiles.Select(tile => $"{tile.X},{tile.Y}"));
        Assert.Equal(2, world.ParsedTileCount);
        Assert.Equal(2, world.ObjectPlacementCount);
        Assert.Equal(2, world.SplinePlacementCount);

        OmsiWorldTile first = world.Tiles[0];
        Assert.Equal(12, first.Version);
        Assert.True(first.IsParsed);
        Assert.Empty(first.UnsupportedRecords);

        OmsiObjectPlacement placement = Assert.Single(first.Objects);
        Assert.Equal("Sceneryobjects\\Test\\Box.sco", placement.ModelPath);
        Assert.Equal(10, placement.X);
        Assert.Equal(20, placement.Y);
        Assert.Equal(3, placement.Z);
        Assert.Equal(45, placement.HeadingDegrees);

        OmsiSplinePlacement spline = Assert.Single(first.Splines);
        Assert.Equal("Splines\\Test\\Road.sli", spline.ModelPath);
        Assert.Equal(20, spline.Length);

        Assert.NotNull(first.Terrain);
        Assert.True(first.Terrain.HasDocumentedSize);
        Assert.Equal(14_888, first.Terrain.Length);
    }

    [Fact]
    public void TerrainSidecarOfTheWrongSizeIsReportedWithoutDecodingHeights()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf16Le("global.cfg", GlobalConfig);
        directory.WriteUtf16Le("tile_0_0.map", Version12Tile);
        directory.WriteRaw("tile_0_0.map.terrain", 1, 2, 3, 4);

        OmsiWorldModel world = OmsiWorldLoader.Load(directory.Path);

        Assert.Equal(MapScanStatus.Partial, world.Status);

        OmsiTerrainReference terrain = Assert.IsType<OmsiTerrainReference>(
            Assert.Single(world.Tiles).Terrain);
        Assert.False(terrain.HasDocumentedSize);
        Assert.Equal(4, terrain.Length);

        Assert.Contains(
            world.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "documented layout is 14888 bytes",
                StringComparison.Ordinal));
    }

    [Fact]
    public void TileWithoutVersionLeavesTheMapWithoutUsableWorldData()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf16Le("global.cfg", GlobalConfig);
        directory.WriteUtf16Le("tile_0_0.map", "[object]\r\n0\r\nBox.sco\r\n");

        OmsiWorldModel world = OmsiWorldLoader.Load(directory.Path);

        // The only tile could not be interpreted, so there is no world data to
        // describe: that is a failure, not a partial result.
        Assert.Equal(MapScanStatus.Failed, world.Status);
        Assert.Equal(0, world.ParsedTileCount);
        Assert.Empty(world.Tiles[0].Objects);
        Assert.Contains(
            world.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "no usable leading [version]",
                StringComparison.Ordinal));
        Assert.Contains(
            world.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "no usable world data",
                StringComparison.Ordinal));
    }

    [Fact]
    public void OneUnparsedTileAmongSeveralKeepsTheMapPartial()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf16Le("global.cfg", GlobalConfig);
        directory.WriteUtf16Le("tile_0_0.map", Version12Tile);
        directory.WriteUtf16Le("tile_1_0.map", "[object]\r\n0\r\nBox.sco\r\n");

        OmsiWorldModel world = OmsiWorldLoader.Load(directory.Path);

        Assert.Equal(MapScanStatus.Partial, world.Status);
        Assert.Equal(1, world.ParsedTileCount);
        Assert.Equal(1, world.ObjectPlacementCount);
    }

    [Fact]
    public void UnknownTileRecordsArePreservedWithoutFailingTheTile()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf16Le(
            "global.cfg",
            GlobalConfig);
        directory.WriteUtf16Le(
            "tile_0_0.map",
            Version12Tile + "[variable_terrain]\r\n1\r\n");

        OmsiWorldModel world = OmsiWorldLoader.Load(directory.Path);

        OmsiWorldTile tile = Assert.Single(world.Tiles);
        Assert.True(tile.IsParsed);
        Assert.Single(tile.Objects);
        OmsiUnsupportedTileRecord record = Assert.Single(tile.UnsupportedRecords);
        Assert.Equal("variable_terrain", record.Keyword);
        Assert.Equal(1, record.Count);
        Assert.Equal(MapScanStatus.Complete, world.Status);
    }

    [Fact]
    public void MissingMapFolderFailsWithoutThrowing()
    {
        string missing = Path.Combine(
            Path.GetTempPath(),
            "nymeria-omsi-tests-world-missing-" + Guid.NewGuid().ToString("N"));

        OmsiWorldModel world = OmsiWorldLoader.Load(missing);

        Assert.Equal(MapScanStatus.Failed, world.Status);
        Assert.Empty(world.Tiles);
        Assert.Contains(
            world.Diagnostics,
            diagnostic => diagnostic.Severity == MapDiagnosticSeverity.Error);
    }

    [Fact]
    public void EveryTileFailingToDecodeFailsTheWorld()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf16Le("global.cfg", GlobalConfig);
        directory.WriteRaw("tile_0_0.map", 0xFF, 0xFE, 0x41);

        OmsiWorldModel world = OmsiWorldLoader.Load(directory.Path);

        Assert.Equal(MapScanStatus.Failed, world.Status);
        Assert.Equal(0, world.ParsedTileCount);
        Assert.Contains(
            world.Diagnostics,
            diagnostic => diagnostic.Severity == MapDiagnosticSeverity.Error);
    }

    [Fact]
    public void MapLevelWarningKeepsTheWorldPartial()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf16Le("global.cfg", "[friendlyname]\r\nOnly Friendly\r\n");
        directory.WriteUtf16Le("tile_0_0.map", Version12Tile);

        OmsiWorldModel world = OmsiWorldLoader.Load(directory.Path);

        Assert.Equal(MapScanStatus.Partial, world.Status);
        Assert.Null(world.Name);
        Assert.Equal(1, world.ParsedTileCount);
        Assert.Equal(1, world.ObjectPlacementCount);
    }

    [Fact]
    public void WhitespaceOnlyPathIsRejected()
    {
        Assert.Throws<ArgumentException>(() => OmsiWorldLoader.Load("   "));
    }

    private static void WriteTerrain(TemporaryDirectory directory, string fileName)
    {
        directory.WriteRaw(fileName, new byte[OmsiWorldLoader.ExpectedTerrainFileLength]);
    }
}

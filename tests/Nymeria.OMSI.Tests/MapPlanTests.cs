using System.Globalization;
using Nymeria.Omsi.Maps;
using Nymeria.Omsi.Rendering;
using Nymeria.Omsi.World;

namespace Nymeria.OMSI.Tests;

/// <summary>
/// Headless tests for the plan geometry derived from parsed world data. The map
/// folders are synthetic strings written here, not OMSI content.
/// </summary>
public sealed class MapPlanTests
{
    private const string GlobalConfig = "[name]\r\nTest Map\r\n";

    [Fact]
    public void TileOriginsUseTheDocumentedThreeHundredMetreEdge()
    {
        OmsiMapPlan plan = BuildPlan(
            ("tile_0_0.map", Version12Tile(0, 0)),
            ("tile_1_0.map", Version12Tile(0, 0)));

        Assert.Equal(300.0, plan.TileEdgeMeters);
        Assert.True(plan.HasDocumentedTileEdge);
        Assert.Equal(2, plan.Tiles.Count);
        Assert.Equal(new PlanRect(0, 0, 300, 300), plan.Tiles[0]);
        Assert.Equal(new PlanRect(300, 0, 600, 300), plan.Tiles[1]);
    }

    [Fact]
    public void PlacementPositionsAddTheTileOrigin()
    {
        OmsiMapPlan plan = BuildPlan(
            ("tile_0_0.map", Version12Tile(10, 20)),
            ("tile_2_-1.map", Version12Tile(-5, 7.5)));

        Assert.Equal(new PlanPoint(10, 20), plan.Objects[0]);
        Assert.Equal(new PlanPoint(595, -292.5), plan.Objects[1]);
    }

    [Fact]
    public void WorldCoordinateMapsUseTheMeasuredEdgeAndAreFlagged()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf16Le(
            "global.cfg",
            GlobalConfig + "[worldcoordinates]\r\n");
        directory.WriteUtf16Le("tile_0_0.map", Version12Tile(0, 0));

        OmsiMapPlan plan = OmsiMapPlan.Create(OmsiWorldLoader.Load(directory.Path));

        Assert.Equal(OmsiMapPlan.WorldCoordinateTileEdgeMeters, plan.TileEdgeMeters);
        Assert.False(plan.HasDocumentedTileEdge);
    }

    [Fact]
    public void BoundsCoverTilesAndPlacements()
    {
        OmsiMapPlan plan = BuildPlan(
            ("tile_0_0.map", Version12Tile(-20, -30)),
            ("tile_1_1.map", Version12Tile(10, 10)));

        // The object at (-20, -30) pulls the minimum; the second tile's 300 m square
        // sets the maximum at 600 on both axes.
        Assert.Equal(-20, plan.Bounds.MinX);
        Assert.Equal(-30, plan.Bounds.MinY);
        Assert.Equal(600, plan.Bounds.MaxX);
        Assert.Equal(600, plan.Bounds.MaxY);
    }

    [Fact]
    public void UnparsedTilesTerrainAndRecordTypesAreCounted()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf16Le("global.cfg", GlobalConfig);
        directory.WriteUtf16Le("tile_0_0.map", Version12Tile(1, 1) + "[terrain]\r\n12\r\n");
        directory.WriteUtf16Le("tile_1_0.map", "[version]\r\n12\r\n[variable_terrain]\r\n1\r\n");
        directory.WriteRaw("tile_0_0.map.terrain", new byte[14_888]);

        OmsiMapPlan plan = OmsiMapPlan.Create(OmsiWorldLoader.Load(directory.Path));

        Assert.Equal(0, plan.UnparsedTiles);
        Assert.Equal(1, plan.TerrainSidecars);
        Assert.Equal(2, plan.UninterpretedRecordTypes);
        Assert.Single(plan.Objects);
    }

    [Fact]
    public void AbsurdAndNonFinitePositionsAreSkippedAndCounted()
    {
        OmsiMapPlan plan = BuildPlan(
            ("tile_0_0.map", Version12Tile(1e9, double.NaN)));

        Assert.Empty(plan.Objects);
        Assert.Equal(1, plan.SkippedObjects);

        // The tile itself still occupies its documented square.
        Assert.Single(plan.Tiles);
    }

    [Fact]
    public void PlacementsAreListedInTileAndRecordOrder()
    {
        OmsiMapPlan plan = BuildPlan(
            ("tile_1_0.map", Version12Tile(1, 0)),
            ("tile_0_0.map", Version12Tile(0, 0)));

        // Tiles come back in manifest order (x, then y), not in the order the files
        // were written.
        Assert.Equal(new[] { new PlanPoint(0, 0), new PlanPoint(301, 0) }, plan.Objects);
    }

    [Fact]
    public void MarkerLimitIsEnforcedAndReported()
    {
        var placements = new List<OmsiObjectPlacement>(
            OmsiMapPlan.MaxPointsPerCategory + 1);

        for (int index = 0; index <= OmsiMapPlan.MaxPointsPerCategory; index++)
        {
            placements.Add(new OmsiObjectPlacement(
                "Box.sco",
                index,
                0,
                index,
                index,
                0,
                0,
                0,
                0,
                [],
                1));
        }

        var world = new OmsiWorldModel(
            "/synthetic",
            null,
            "Synthetic",
            null,
            usesWorldCoordinates: false,
            [
                new OmsiWorldTile(
                    0,
                    0,
                    "/synthetic/tile_0_0.map",
                    12,
                    isParsed: true,
                    placements,
                    [],
                    null,
                    [],
                    []),
            ],
            [],
            MapScanStatus.Complete);

        OmsiMapPlan plan = OmsiMapPlan.Create(world);

        Assert.Equal(OmsiMapPlan.MaxPointsPerCategory, plan.Objects.Count);
        Assert.Equal(1, plan.OmittedObjects);
    }

    private static OmsiMapPlan BuildPlan(params (string FileName, string Content)[] tiles)
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf16Le("global.cfg", GlobalConfig);

        foreach ((string fileName, string content) in tiles)
        {
            directory.WriteUtf16Le(fileName, content);
        }

        return OmsiMapPlan.Create(OmsiWorldLoader.Load(directory.Path));
    }

    /// <summary>
    /// A version-12 tile with one object at the given tile-local position.
    /// </summary>
    private static string Version12Tile(double localX, double localY)
    {
        return "[version]\r\n" +
            "12\r\n" +
            "[object]\r\n" +
            "0\r\n" +
            "Sceneryobjects\\Test\\Box.sco\r\n" +
            "1\r\n" +
            Number(localX) + "\r\n" +
            Number(localY) + "\r\n" +
            "0\r\n" +
            "0\r\n" +
            "0\r\n" +
            "0\r\n" +
            "0\r\n";
    }

    private static string Number(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }
}

using Nymeria.Omsi.Maps;
using Nymeria.Omsi.Rendering;
using Nymeria.Omsi.World;

namespace Nymeria.OMSI.Tests;

/// <summary>
/// Headless tests for the diagnostic rasterizer. The images are produced from
/// synthetic parsed records; nothing here is OMSI content or a GPU resource.
/// </summary>
public sealed class MapPlanRasterizerTests
{
    [Fact]
    public void OneTileIsDrawnAsAFilledSquareWithAnOutline()
    {
        OmsiMapPlan plan = BuildPlan(
            ("tile_0_0.map", Version12Tile(150, 150, null)));

        var view = new MapPlanView(300, 300, 150, 150, 1.0);
        var image = new MapPlanImage(300, 300);

        PlanDrawStatistics statistics = MapPlanRasterizer.Render(plan, view, image);

        // The tile covers the whole image at this view; the outline sits on its
        // edges and the object marker sits in the middle.
        Assert.Equal(1, statistics.Tiles);
        Assert.Equal(PlanColor.TileBorder, image[0, 0]);
        Assert.Equal(PlanColor.TileFill, image[50, 50]);
        Assert.Equal(PlanColor.Object, image[150, 150]);
    }

    [Fact]
    public void ObjectAndSplineMarkersUseTheirOwnColors()
    {
        OmsiMapPlan plan = BuildPlan(
            ("tile_0_0.map", Version12Tile(100, 100, (140, 100))));

        var view = new MapPlanView(300, 300, 150, 150, 2.0);
        var image = new MapPlanImage(300, 300);

        PlanDrawStatistics statistics = MapPlanRasterizer.Render(plan, view, image);

        // World (100, 100) is pixel (125, 175); world (140, 100) is (145, 175).
        Assert.Equal(1, statistics.Objects);
        Assert.Equal(1, statistics.Splines);
        Assert.Equal(PlanColor.Object, image[125, 175]);
        Assert.Equal(PlanColor.Spline, image[145, 175]);
        Assert.Equal(PlanColor.Background, image[10, 10]);
    }

    [Fact]
    public void RenderingIsDeterministic()
    {
        OmsiMapPlan plan = BuildPlan(
            ("tile_0_0.map", Version12Tile(100, 100, (140, 100))),
            ("tile_1_0.map", Version12Tile(10, 10, null)));

        var view = new MapPlanView(320, 200, 150, 150, 2.0);
        var first = new MapPlanImage(320, 200);
        var second = new MapPlanImage(320, 200);

        MapPlanRasterizer.Render(plan, view, first);
        MapPlanRasterizer.Render(plan, view, second);

        Assert.Equal(first.Pixels, second.Pixels);
    }

    [Fact]
    public void MarkersOutsideTheImageAreSkippedWithoutDrawing()
    {
        OmsiMapPlan plan = BuildPlan(
            ("tile_0_0.map", Version12Tile(100, 100, null)));

        var view = new MapPlanView(100, 100, 5000, 5000, 1.0);
        var image = new MapPlanImage(100, 100);

        PlanDrawStatistics statistics = MapPlanRasterizer.Render(plan, view, image);

        Assert.Equal(0, statistics.Tiles);
        Assert.Equal(0, statistics.Objects);
        Assert.All(
            Enumerable.Range(0, image.Height),
            y => Assert.Equal(PlanColor.Background, image[0, y]));
    }

    [Fact]
    public void AnImageWithNoMapDataStaysAtTheBackgroundColor()
    {
        var plan = OmsiMapPlan.Create(new OmsiWorldModel(
            "/synthetic",
            null,
            null,
            null,
            usesWorldCoordinates: false,
            [],
            [],
            MapScanStatus.Complete));

        var view = MapPlanView.Frame(plan.Bounds, 64, 64);
        var image = new MapPlanImage(64, 64);

        MapPlanRasterizer.Render(plan, view, image);

        Assert.Equal(PlanColor.Background, image[32, 32]);

        for (int offset = 3; offset < image.Pixels.Length; offset += 4)
        {
            Assert.Equal(0xFF, image.Pixels[offset]);
        }
    }

    private static OmsiMapPlan BuildPlan(params (string FileName, string Content)[] tiles)
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf16Le("global.cfg", "[name]\r\nTest Map\r\n");

        foreach ((string fileName, string content) in tiles)
        {
            directory.WriteUtf16Le(fileName, content);
        }

        return OmsiMapPlan.Create(OmsiWorldLoader.Load(directory.Path));
    }

    /// <summary>
    /// A version-12 tile with one object and, optionally, one spline.
    /// </summary>
    private static string Version12Tile(
        double objectX,
        double objectY,
        (double X, double Y)? spline)
    {
        string text =
            "[version]\r\n" +
            "12\r\n" +
            "[object]\r\n" +
            "0\r\n" +
            "Box.sco\r\n" +
            "1\r\n" +
            Number(objectX) + "\r\n" +
            Number(objectY) + "\r\n" +
            "0\r\n" +
            "0\r\n" +
            "0\r\n" +
            "0\r\n" +
            "0\r\n";

        if (spline is not (double splineX, double splineY))
        {
            return text;
        }

        return text +
            "[spline]\r\n" +
            "0\r\n" +
            "Road.sli\r\n" +
            "2\r\n" +
            "-1\r\n" +
            "-1\r\n" +
            Number(splineX) + "\r\n" +
            "0\r\n" +
            Number(splineY) + "\r\n" +
            "0\r\n" +
            "10\r\n" +
            "0\r\n";
    }

    private static string Number(double value)
    {
        return value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    }
}

using System.Globalization;
using Nymeria.Omsi.Maps;
using Nymeria.Omsi.Rendering;
using Nymeria.Omsi.World;

namespace Nymeria.OMSI.Tests;

/// <summary>
/// Headless tests for the console summary printed before the diagnostic window
/// opens. They assert wording and counts only, never a rendering result.
/// </summary>
public sealed class RenderSummaryWriterTests
{
    [Fact]
    public void SummaryStatesWhatIsShownAndLabelsTheViewAsDiagnostic()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf16Le("global.cfg", "[name]\r\nTest Map\r\n");
        directory.WriteUtf16Le("tile_0_0.map", TileWithOneObject);

        string text = Write(directory.Path, out _, out _);

        Assert.StartsWith(
            "render: diagnostic tile plan",
            text,
            StringComparison.Ordinal);
        Assert.Contains("OMSI scenery and terrain are not rendered", text, StringComparison.Ordinal);
        Assert.Contains("name: Test Map", text, StringComparison.Ordinal);
        Assert.Contains("tiles: 1 discovered (1 parsed, 0 unparsed)", text, StringComparison.Ordinal);
        Assert.Contains("coordinate grid: 300 m tiles (documented)", text, StringComparison.Ordinal);
        Assert.Contains("plan: 1 tile square(s), 1 object marker(s), 0 spline marker(s)", text, StringComparison.Ordinal);
        Assert.Contains("placement references: 0 of 1 resolved", text, StringComparison.Ordinal);
        Assert.Contains("parse diagnostics: none", text, StringComparison.Ordinal);
        Assert.Contains("status: complete", text, StringComparison.Ordinal);
        Assert.Contains("close the window to exit", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DerivedContentRootIsPrinted()
    {
        using var directory = new TemporaryDirectory();
        string mapFolder = Path.Combine(directory.Path, "maps", "Grundorf");
        Directory.CreateDirectory(mapFolder);
        File.WriteAllText(Path.Combine(mapFolder, "global.cfg"), "[name]\r\nTest Map\r\n");
        File.WriteAllText(Path.Combine(mapFolder, "tile_0_0.map"), TileWithOneObject);

        string text = Write(mapFolder, out string? contentRoot, out _);

        Assert.NotNull(contentRoot);
        Assert.Contains($"content root: {contentRoot}", text, StringComparison.Ordinal);
    }

    [Fact]
    public void UnresolvedContentRootIsExplained()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf16Le("global.cfg", "[name]\r\nTest Map\r\n");
        directory.WriteUtf16Le("tile_0_0.map", TileWithOneObject);

        string text = Write(directory.Path, out string? contentRoot, out string reason);

        Assert.Null(contentRoot);
        Assert.Contains("content root: (unresolved: ", text, StringComparison.Ordinal);
        Assert.Contains(reason, text, StringComparison.Ordinal);
    }

    [Fact]
    public void WorldCoordinateMapsAreReportedAsUnverified()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf16Le(
            "global.cfg",
            "[name]\r\nTest Map\r\n[worldcoordinates]\r\n");
        directory.WriteUtf16Le("tile_0_0.map", TileWithOneObject);

        string text = Write(directory.Path, out _, out _);

        Assert.Contains("reference measurement, unverified", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SummaryIsDeterministic()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf16Le("global.cfg", "[name]\r\nTest Map\r\n");
        directory.WriteUtf16Le("tile_0_0.map", TileWithOneObject);

        Assert.Equal(
            Write(directory.Path, out _, out _),
            Write(directory.Path, out _, out _));
    }

    private static string Write(
        string mapDirectory,
        out string? contentRoot,
        out string contentRootReason)
    {
        OmsiWorldModel world = OmsiWorldLoader.Load(mapDirectory);
        OmsiMapPlan plan = OmsiMapPlan.Create(world);
        bool derived = OmsiContentRoot.TryDerive(
            world.MapRoot,
            out contentRoot,
            out contentRootReason);
        string? root = derived ? contentRoot : null;

        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        RenderSummaryWriter.Write(writer, world, plan, root, contentRootReason);
        return writer.ToString();
    }

    private const string TileWithOneObject =
        "[version]\r\n" +
        "12\r\n" +
        "[object]\r\n" +
        "0\r\n" +
        "Box.sco\r\n" +
        "1\r\n" +
        "10\r\n" +
        "20\r\n" +
        "0\r\n" +
        "0\r\n" +
        "0\r\n" +
        "0\r\n" +
        "0\r\n";
}

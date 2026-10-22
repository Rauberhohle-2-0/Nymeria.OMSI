using System.Globalization;
using Nymeria.Omsi.Maps;
using Nymeria.Omsi.World;

namespace Nymeria.OMSI.Tests;

/// <summary>
/// Headless tests for the world summary text. They assert the shape of the output,
/// not any property of a real map.
/// </summary>
public sealed class WorldSummaryWriterTests
{
    [Fact]
    public void SummaryReportsCountsAndAStatusLine()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf16Le(
            "global.cfg",
            "[name]\r\nTest Map\r\n[friendlyname]\r\nTest Map Friendly\r\n");
        directory.WriteUtf16Le(
            "tile_0_0.map",
            "[version]\r\n12\r\n" +
            "[object]\r\n0\r\nBox.sco\r\n1\r\n0\r\n0\r\n0\r\n0\r\n0\r\n0\r\n0\r\n" +
            "[spline]\r\n0\r\nRoad.sli\r\n2\r\n-1\r\n-1\r\n0\r\n0\r\n0\r\n0\r\n10\r\n0\r\n");

        string text = Write(OmsiWorldLoader.Load(directory.Path));

        Assert.Contains($"map: {directory.Path}", text, StringComparison.Ordinal);
        Assert.Contains("name: Test Map", text, StringComparison.Ordinal);
        Assert.Contains("friendly name: Test Map Friendly", text, StringComparison.Ordinal);
        Assert.Contains("tiles discovered: 1", text, StringComparison.Ordinal);
        Assert.Contains("tiles parsed: 1", text, StringComparison.Ordinal);
        Assert.Contains("tile versions: 12 (1 tiles)", text, StringComparison.Ordinal);
        Assert.Contains("objects: 1 placements", text, StringComparison.Ordinal);
        Assert.Contains("splines: 1 placements", text, StringComparison.Ordinal);
        Assert.Contains("terrain files: 0 ", text, StringComparison.Ordinal);
        Assert.Contains("tile (0,0): 1 object(s), 1 spline(s), version 12", text, StringComparison.Ordinal);
        Assert.Contains("diagnostics: none", text, StringComparison.Ordinal);
        Assert.Contains("status: complete", text, StringComparison.Ordinal);
        Assert.EndsWith("status: complete" + Environment.NewLine, text, StringComparison.Ordinal);
    }

    [Fact]
    public void SummaryIsDeterministicForTheSameInput()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf16Le("global.cfg", "[name]\r\nTest Map\r\n");
        directory.WriteUtf16Le("tile_0_0.map", "[version]\r\n12\r\n");
        directory.WriteUtf16Le("tile_0_1.map", "[version]\r\n11\r\n");

        OmsiWorldModel world = OmsiWorldLoader.Load(directory.Path);

        Assert.Equal(Write(world), Write(world));
    }

    [Fact]
    public void SummaryOfAFailedScanStatesTheFailure()
    {
        string missing = Path.Combine(
            Path.GetTempPath(),
            "nymeria-omsi-tests-summary-missing-" + Guid.NewGuid().ToString("N"));

        string text = Write(OmsiWorldLoader.Load(missing));

        Assert.Contains("status: failed", text, StringComparison.Ordinal);
        Assert.Contains("name: (unavailable)", text, StringComparison.Ordinal);
        Assert.Contains("diagnostics: ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsBeyondTheListingLimitAreCountedNotDumped()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf16Le("global.cfg", "[name]\r\nTest Map\r\n");

        // Each tile lacks a version, so every one contributes a warning; with more
        // than the listing limits, both lists must be truncated rather than dumped.
        for (int index = 0; index < 60; index++)
        {
            directory.WriteUtf16Le(
                $"tile_0_{index.ToString(CultureInfo.InvariantCulture)}.map",
                "[object]\r\n0\r\nBox.sco\r\n");
        }

        string text = Write(OmsiWorldLoader.Load(directory.Path));

        Assert.Contains("more diagnostic(s)", text, StringComparison.Ordinal);
        Assert.Contains("more tile(s) not listed", text, StringComparison.Ordinal);

        // Not one tile could be interpreted, so the model is a failure, not a
        // partial result, no matter how many diagnostics were produced.
        Assert.Contains("status: failed", text, StringComparison.Ordinal);
    }

    private static string Write(OmsiWorldModel world)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        WorldSummaryWriter.Write(writer, world);
        return writer.ToString();
    }
}

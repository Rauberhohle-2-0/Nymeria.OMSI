using Nymeria.Omsi.Maps;

namespace Nymeria.OMSI.Tests;

/// <summary>
/// Headless tests for the console summary. The manifests come from synthetic
/// folders; no OMSI content is involved.
/// </summary>
public sealed class MapSummaryWriterTests
{
    [Fact]
    public void CompleteManifestPrintsMetadataTilesAndStatus()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf8("global.cfg", "[name]\nTest Map\n[friendlyname]\nTest\n");
        directory.CreateEmptyFile("tile_0_-1.map");

        OmsiMapManifest manifest = OmsiMapDiscovery.Scan(directory.Path);
        string summary = Render(manifest);

        Assert.Contains("name: Test Map", summary, StringComparison.Ordinal);
        Assert.Contains("friendly name: Test", summary, StringComparison.Ordinal);
        Assert.Contains("tiles: 1", summary, StringComparison.Ordinal);
        Assert.Contains("  (0,-1) tile_0_-1.map", summary, StringComparison.Ordinal);
        Assert.Contains("diagnostics: none", summary, StringComparison.Ordinal);
        Assert.Contains("status: complete", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void FailedManifestPrintsMissingConfigAndDiagnostics()
    {
        using var directory = new TemporaryDirectory();

        OmsiMapManifest manifest = OmsiMapDiscovery.Scan(directory.Path);
        string summary = Render(manifest);

        Assert.Contains("global.cfg: (not found)", summary, StringComparison.Ordinal);
        Assert.Contains("name: (unavailable)", summary, StringComparison.Ordinal);
        Assert.Contains("tiles: 0", summary, StringComparison.Ordinal);
        Assert.Contains("diagnostics: 1", summary, StringComparison.Ordinal);
        Assert.Contains("status: failed", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedDirectivesAreSummarized()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf8(
            "global.cfg",
            "[name]\nTest Map\n[entrypoints]\n0\n[map]\n0\n");
        directory.CreateEmptyFile("tile_0_0.map");

        OmsiMapManifest manifest = OmsiMapDiscovery.Scan(directory.Path);
        string summary = Render(manifest);

        Assert.Contains(
            "unsupported directives: 2 ([entrypoints], [map])",
            summary,
            StringComparison.Ordinal);
    }

    private static string Render(OmsiMapManifest manifest)
    {
        using var writer = new StringWriter();
        MapSummaryWriter.Write(writer, manifest);
        return writer.ToString();
    }
}

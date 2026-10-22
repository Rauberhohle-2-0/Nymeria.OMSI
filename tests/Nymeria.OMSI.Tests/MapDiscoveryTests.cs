using Nymeria.Omsi.Maps;

namespace Nymeria.OMSI.Tests;

/// <summary>
/// Headless tests for map discovery. Every folder is created from synthetic
/// strings authored here; none of it is OMSI content, and passing these tests is
/// not evidence of compatibility with a real map.
/// </summary>
public sealed class MapDiscoveryTests
{
    private const string NamedConfig =
        "[name]\r\n" +
        "Test Map\r\n" +
        "[friendlyname]\r\n" +
        "Test Map Friendly\r\n" +
        "[version]\r\n" +
        "1\r\n";

    [Fact]
    public void MissingDirectoryFailsWithAnActionableError()
    {
        string missing = Path.Combine(
            Path.GetTempPath(),
            "nymeria-omsi-tests-missing-" + Guid.NewGuid().ToString("N"));

        OmsiMapManifest manifest = OmsiMapDiscovery.Scan(missing);

        Assert.Equal(MapScanStatus.Failed, manifest.Status);
        Assert.Null(manifest.GlobalConfigPath);
        Assert.Empty(manifest.Tiles);
        Assert.Contains(
            manifest.Diagnostics,
            diagnostic => diagnostic.Severity == MapDiagnosticSeverity.Error &&
                diagnostic.Message.Contains("does not exist", StringComparison.Ordinal));
        Assert.Equal(missing, manifest.MapRoot);
    }

    [Fact]
    public void MissingGlobalConfigFails()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateEmptyFile("tile_0_0.map");

        OmsiMapManifest manifest = OmsiMapDiscovery.Scan(directory.Path);

        Assert.Equal(MapScanStatus.Failed, manifest.Status);
        Assert.Empty(manifest.Tiles);
        Assert.Contains(
            manifest.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "global.cfg",
                StringComparison.Ordinal));
    }

    [Fact]
    public void CompleteSyntheticMapIsScannedAndSorted()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf16Le(
            "global.cfg",
            NamedConfig + "[worldcoordinates]\r\n[entrypoints]\r\n0\r\n");
        directory.CreateEmptyFile("tile_1_0.map");
        directory.CreateEmptyFile("tile_0_-1.map");
        directory.CreateEmptyFile("tile_0_-2.map");

        OmsiMapManifest manifest = OmsiMapDiscovery.Scan(directory.Path);

        Assert.Equal(MapScanStatus.Complete, manifest.Status);
        Assert.Empty(manifest.Diagnostics);
        Assert.Equal("Test Map", manifest.Name);
        Assert.Equal("Test Map Friendly", manifest.FriendlyName);
        Assert.True(manifest.UsesWorldCoordinates);
        Assert.Equal("UTF-16LE with byte-order mark", manifest.TextEncodingName);
        Assert.Equal(Path.Combine(directory.Path, "global.cfg"), manifest.GlobalConfigPath);

        Assert.Equal(new[] { "0,-2", "0,-1", "1,0" }, Coordinates(manifest));
        Assert.All(manifest.Tiles, tile => Assert.True(tile.IsReadable));
        Assert.Contains(
            manifest.UnsupportedDirectives,
            directive => directive.Keyword == "entrypoints");
        Assert.Contains(
            manifest.UnsupportedDirectives,
            directive => directive.Keyword == "version");
    }

    [Fact]
    public void CaseInsensitiveGlobalConfigNameIsFound()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf8("GLOBAL.CFG", "[name]\nTest Map\n");
        directory.CreateEmptyFile("tile_0_0.map");

        OmsiMapManifest manifest = OmsiMapDiscovery.Scan(directory.Path);

        Assert.Equal(MapScanStatus.Complete, manifest.Status);
        Assert.Equal("Test Map", manifest.Name);
    }

    [Fact]
    public void InvalidTileFileNameIsReportedAndIgnored()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf8("global.cfg", "[name]\nTest Map\n");
        directory.CreateEmptyFile("tile_0_1_2.map");

        OmsiMapManifest manifest = OmsiMapDiscovery.Scan(directory.Path);

        Assert.Equal(MapScanStatus.Partial, manifest.Status);
        Assert.Empty(manifest.Tiles);
        Assert.Contains(
            manifest.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "does not match the documented",
                StringComparison.Ordinal));
        Assert.Contains(
            manifest.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "No tile files",
                StringComparison.Ordinal));
    }

    [Fact]
    public void UnrelatedMapFileIsReportedAndIgnored()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf8("global.cfg", "[name]\nTest Map\n");
        directory.CreateEmptyFile("tile_0_0.map");
        directory.CreateEmptyFile("notes.map");

        OmsiMapManifest manifest = OmsiMapDiscovery.Scan(directory.Path);

        Assert.Equal(MapScanStatus.Partial, manifest.Status);
        Assert.Single(manifest.Tiles);
        Assert.Contains(
            manifest.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "notes.map",
                StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateTileCoordinatesAreReportedDeterministically()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf8("global.cfg", "[name]\nTest Map\n");
        directory.CreateEmptyFile("tile_0_1.map");
        directory.CreateEmptyFile("tile_00_1.map");

        OmsiMapManifest manifest = OmsiMapDiscovery.Scan(directory.Path);

        Assert.Equal(MapScanStatus.Partial, manifest.Status);
        OmsiMapTile tile = Assert.Single(manifest.Tiles);
        Assert.Equal("tile_00_1.map", tile.FileName);
        Assert.Contains(
            manifest.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "both 'tile_00_1.map' and 'tile_0_1.map'",
                StringComparison.Ordinal));
    }

    [Fact]
    public void MissingNameIsAWarningAndNotAFabricatedValue()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf8("global.cfg", "[friendlyname]\nOnly Friendly\n");
        directory.CreateEmptyFile("tile_0_0.map");

        OmsiMapManifest manifest = OmsiMapDiscovery.Scan(directory.Path);

        Assert.Equal(MapScanStatus.Partial, manifest.Status);
        Assert.Null(manifest.Name);
        Assert.Equal("Only Friendly", manifest.FriendlyName);
        Assert.Contains(
            manifest.Diagnostics,
            diagnostic => diagnostic.Message.Contains("[name]", StringComparison.Ordinal));
    }

    [Fact]
    public void EmptyGlobalConfigWarnsAboutTheMissingName()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateEmptyFile("global.cfg");

        OmsiMapManifest manifest = OmsiMapDiscovery.Scan(directory.Path);

        Assert.Equal(MapScanStatus.Partial, manifest.Status);
        Assert.Null(manifest.Name);
        Assert.Contains(
            manifest.Diagnostics,
            diagnostic => diagnostic.Message.Contains("No tile files", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateSupportedDirectiveKeepsTheFirstBlock()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf8(
            "global.cfg",
            "[name]\nFirst Name\n[name]\nSecond Name\n");
        directory.CreateEmptyFile("tile_0_0.map");

        OmsiMapManifest manifest = OmsiMapDiscovery.Scan(directory.Path);

        Assert.Equal(MapScanStatus.Partial, manifest.Status);
        Assert.Equal("First Name", manifest.Name);
        Assert.Contains(
            manifest.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "Duplicate [name]",
                StringComparison.Ordinal));
    }

    [Fact]
    public void WhitespaceOnlyPathIsRejected()
    {
        Assert.Throws<ArgumentException>(() => OmsiMapDiscovery.Scan("   "));
    }

    private static string[] Coordinates(OmsiMapManifest manifest)
    {
        return [.. manifest.Tiles.Select(tile => $"{tile.X},{tile.Y}")];
    }
}

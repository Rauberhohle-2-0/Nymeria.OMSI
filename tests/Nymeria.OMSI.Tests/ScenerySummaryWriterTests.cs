using Nymeria.Omsi.Maps;
using Nymeria.Omsi.Rendering;
using Nymeria.Omsi.Scenery;
using Nymeria.Omsi.World;

namespace Nymeria.OMSI.Tests;

/// <summary>
/// Headless tests for the scenery summary text.
/// </summary>
/// <remarks>
/// The counts here are synthetic values, not measurements of any map. They pin the
/// reporting contract: a verdict that never claims success, per-class breakdowns, the
/// permitted roots, the keyword census, and one deduplicated line per asset failure.
/// </remarks>
public sealed class ScenerySummaryWriterTests
{
    [Fact]
    public void ReportsAnIncompleteVerdictAndCountsPerClass()
    {
        var counts = new SceneryLoadCounts(
            new SceneryPlacementCounts(
                Considered: 10,
                Instanced: 6,
                Resolved: 8,
                Failures: Tally(
                    (SceneryDiagnosticClass.MissingAsset, 1),
                    (SceneryDiagnosticClass.UnsupportedFormat, 3)),
                WithoutTerrain: 1,
                InvalidTransform: 1),
            new SceneryDefinitionCounts(Loaded: 2, WithoutGeometry: 1),
            new SceneryAssetCounts(
                Decoded: 4,
                Failures: Tally((SceneryDiagnosticClass.UnsupportedFormat, 3))),
            new SceneryAssetCounts(
                Decoded: 1,
                Failures: Tally()),
            new ScenerySubsetCounts(
                Drawable: 9,
                WithoutTexture: 5,
                Failures: Tally((SceneryDiagnosticClass.MissingTextureMapping, 5))),
            Vertices: 1234,
            UnsupportedKeywords: 42,
            SuppressedProblems: 0,
            Cache: new SceneryCacheCounts(
                Definitions: 3,
                Meshes: 4,
                Textures: 1,
                ReplacedMidRun: 0));

        string text = Write(
            counts,
            [
                new SceneryDiagnosticEntry(
                    SceneryDiagnosticClass.UnsupportedFormat,
                    new MapDiagnostic(
                        MapDiagnosticSeverity.Warning,
                        "unsupported: scrambled O3D",
                        "assets/a.o3d"),
                    3),
            ],
            [new SceneryKeywordCount("script", 42)],
            contentRoot: "/root");

        Assert.StartsWith("scene: incomplete", text, StringComparison.Ordinal);
        Assert.Contains(
            "scene: incomplete — 6 of 10 placement(s) instanced, 4 skipped",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "scene content: decoded OMSI terrain with loaded OMSI scenery objects",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "definitions: 3 loaded, 2 with drawable geometry, 1 without",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "meshes: 4 file(s) decoded, 3 skipped (unsupported format 3)",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "textures: 1 decoded, 0 skipped (none)",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "subsets: 9 drawable, 5 drawn with their base color " +
            "(missing texture mapping 5)",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "placement skips: missing asset 1, unsupported format 3",
            text,
            StringComparison.Ordinal);
        Assert.Contains("asset roots: /root; /root/Texture", text, StringComparison.Ordinal);
        Assert.Contains(
            "unsupported definition keywords (out of scope by design): " +
            "42 occurrence(s) across 1 keyword(s)",
            text,
            StringComparison.Ordinal);
        Assert.Contains("  [script] 42", text, StringComparison.Ordinal);
        Assert.Contains(
            "  [unsupported format] assets/a.o3d: warning: unsupported: scrambled O3D (x3)",
            text,
            StringComparison.Ordinal);
        Assert.Contains("0 re-decoded mid-run", text, StringComparison.Ordinal);
        Assert.DoesNotContain("scene: complete", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NeverClaimsSuccessWhenNothingWasInstanced()
    {
        var counts = new SceneryLoadCounts(
            new SceneryPlacementCounts(
                Considered: 4,
                Instanced: 0,
                Resolved: 0,
                Failures: Tally((SceneryDiagnosticClass.UnsupportedFormat, 4)),
                WithoutTerrain: 0,
                InvalidTransform: 0),
            new SceneryDefinitionCounts(Loaded: 0, WithoutGeometry: 2),
            new SceneryAssetCounts(Decoded: 0, Failures: Tally()),
            new SceneryAssetCounts(Decoded: 0, Failures: Tally()),
            new ScenerySubsetCounts(0, 0, Tally()),
            Vertices: 0,
            UnsupportedKeywords: 0,
            SuppressedProblems: 0,
            Cache: new SceneryCacheCounts(2, 0, 0, 0));

        string text = Write(counts, [], [], contentRoot: null);

        Assert.StartsWith(
            "scene: incomplete — no placement produced drawable geometry",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "scene content: decoded OMSI terrain only",
            text,
            StringComparison.Ordinal);
        Assert.Contains("unsupported definition keywords: none", text);
        Assert.Contains("scenery diagnostics: none", text);
        Assert.Contains("asset roots: none", text);
    }

    [Fact]
    public void ListsOnlyTheFirstKeywordsAndSaysHowManyAreLeft()
    {
        var counts = new SceneryLoadCounts(
            new SceneryPlacementCounts(
                Considered: 1,
                Instanced: 1,
                Resolved: 1,
                Failures: Tally(),
                WithoutTerrain: 0,
                InvalidTransform: 0),
            new SceneryDefinitionCounts(Loaded: 1, WithoutGeometry: 0),
            new SceneryAssetCounts(1, Tally()),
            new SceneryAssetCounts(1, Tally()),
            new ScenerySubsetCounts(1, 0, Tally()),
            Vertices: 3,
            UnsupportedKeywords: 30,
            SuppressedProblems: 2,
            Cache: new SceneryCacheCounts(1, 1, 1, 1));

        var keywords = new List<SceneryKeywordCount>(30);

        for (int index = 0; index < 30; index++)
        {
            keywords.Add(new SceneryKeywordCount($"keyword{index}", 1));
        }

        string text = Write(counts, [], keywords, contentRoot: null);

        Assert.Contains("across 30 keyword(s)", text, StringComparison.Ordinal);
        Assert.Contains("... and 6 more keyword(s)", text, StringComparison.Ordinal);
        Assert.Contains(
            "2 further occurrence(s) of already listed failures were counted but not listed",
            text,
            StringComparison.Ordinal);
    }

    private static string Write(
        SceneryLoadCounts counts,
        IReadOnlyList<SceneryDiagnosticEntry> diagnostics,
        IReadOnlyList<SceneryKeywordCount> keywords,
        string? contentRoot)
    {
        var world = new OmsiWorldModel(
            Path.Combine("maps", "Test"),
            globalConfigPath: null,
            name: "Test Map",
            friendlyName: null,
            usesWorldCoordinates: false,
            tiles: [],
            diagnostics: [],
            status: MapScanStatus.Complete);

        using var writer = new StringWriter();

        ScenerySummaryWriter.Write(
            writer,
            world,
            terrain: null,
            contentRoot,
            counts,
            diagnostics,
            keywords);

        return writer.ToString();
    }

    private static SceneryClassTally Tally(
        params (SceneryDiagnosticClass Kind, int Count)[] counts)
    {
        var tally = new SceneryClassTally();

        foreach ((SceneryDiagnosticClass kind, int count) in counts)
        {
            for (int index = 0; index < count; index++)
            {
                tally.Add(kind);
            }
        }

        return tally;
    }
}

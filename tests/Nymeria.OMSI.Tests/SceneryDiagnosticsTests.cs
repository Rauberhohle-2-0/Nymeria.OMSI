using Nymeria.Omsi.Maps;
using Nymeria.Omsi.Scenery;

namespace Nymeria.OMSI.Tests;

/// <summary>
/// Headless tests for the scenery diagnostic collector.
/// </summary>
/// <remarks>
/// Every path here is a synthetic string. These tests exercise the deduplication rule —
/// one line per <c>(canonical path, class)</c> with an occurrence counter — not any real
/// OMSI content.
/// </remarks>
public sealed class SceneryDiagnosticsTests
{
    [Fact]
    public void MergesTheSamePathAndClassIntoOneEntryWithAnOccurrenceCount()
    {
        var diagnostics = new SceneryDiagnostics();

        diagnostics.Report(
            SceneryDiagnosticClass.UnsupportedFormat,
            MapDiagnosticSeverity.Warning,
            "first description",
            "Sceneryobjects/A/model/a.o3d");
        diagnostics.Report(
            SceneryDiagnosticClass.UnsupportedFormat,
            MapDiagnosticSeverity.Warning,
            "a later description of the same failure",
            "Sceneryobjects/A/model/a.o3d");

        SceneryDiagnosticEntry entry = Assert.Single(diagnostics.Entries);

        Assert.Equal(SceneryDiagnosticClass.UnsupportedFormat, entry.Kind);
        Assert.Equal(2, entry.Occurrences);
        Assert.Equal("first description", entry.Diagnostic.Message);
        Assert.Equal(0, diagnostics.Suppressed);
    }

    [Fact]
    public void KeepsDifferentClassesOfTheSameAssetApart()
    {
        var diagnostics = new SceneryDiagnostics();

        diagnostics.Report(
            SceneryDiagnosticClass.UnsupportedFormat,
            MapDiagnosticSeverity.Warning,
            "decoder refused it",
            "Sceneryobjects/A/model/a.o3d");
        diagnostics.Report(
            SceneryDiagnosticClass.ParseError,
            MapDiagnosticSeverity.Warning,
            "also malformed",
            "Sceneryobjects/A/model/a.o3d");

        Assert.Equal(2, diagnostics.Entries.Count);
        Assert.Equal(
            SceneryDiagnosticClass.UnsupportedFormat,
            diagnostics.Entries[0].Kind);
        Assert.Equal(SceneryDiagnosticClass.ParseError, diagnostics.Entries[1].Kind);
    }

    [Fact]
    public void KeysADiagnosticWithoutAPathByItsMessage()
    {
        var diagnostics = new SceneryDiagnostics();

        diagnostics.Report(
            SceneryDiagnosticClass.MissingAsset,
            MapDiagnosticSeverity.Warning,
            "no terrain was decoded");
        diagnostics.Report(
            SceneryDiagnosticClass.MissingAsset,
            MapDiagnosticSeverity.Warning,
            "no terrain was decoded");
        diagnostics.Report(
            SceneryDiagnosticClass.MissingAsset,
            MapDiagnosticSeverity.Warning,
            "no installation root was derived");

        Assert.Equal(2, diagnostics.Entries.Count);
        Assert.Equal(2, diagnostics.Entries[0].Occurrences);
        Assert.Equal(1, diagnostics.Entries[1].Occurrences);
    }

    [Fact]
    public void CountsOccurrencesBeyondTheListingLimitAsSuppressed()
    {
        var diagnostics = new SceneryDiagnostics(maximum: 1);

        diagnostics.Report(
            SceneryDiagnosticClass.MissingAsset,
            MapDiagnosticSeverity.Warning,
            "first",
            "a.sco");
        diagnostics.Report(
            SceneryDiagnosticClass.MissingAsset,
            MapDiagnosticSeverity.Warning,
            "second",
            "b.sco");
        diagnostics.Report(
            SceneryDiagnosticClass.MissingAsset,
            MapDiagnosticSeverity.Warning,
            "second",
            "b.sco");

        Assert.Single(diagnostics.Entries);
        Assert.Equal(2, diagnostics.Suppressed);
    }

    [Fact]
    public void PreservesTheClassAndCountOfParsedEntries()
    {
        var diagnostics = new SceneryDiagnostics();

        diagnostics.ReportEntries(
        [
            new SceneryDiagnosticEntry(
                SceneryDiagnosticClass.NotAttempted,
                new MapDiagnostic(
                    MapDiagnosticSeverity.Warning,
                    "beyond the mesh limit",
                    "Sceneryobjects/A/a.sco"),
                3),
        ]);

        SceneryDiagnosticEntry entry = Assert.Single(diagnostics.Entries);

        Assert.Equal(SceneryDiagnosticClass.NotAttempted, entry.Kind);
        Assert.Equal(3, entry.Occurrences);
    }

    [Fact]
    public void ClassTallyCountsPerClassAndDescribesOnlyWhatWasCounted()
    {
        var tally = new SceneryClassTally();

        tally.Add(SceneryDiagnosticClass.UnsupportedFormat);
        tally.Add(SceneryDiagnosticClass.UnsupportedFormat);
        tally.Add(SceneryDiagnosticClass.NotAttempted);

        Assert.Equal(2, tally[SceneryDiagnosticClass.UnsupportedFormat]);
        Assert.Equal(0, tally[SceneryDiagnosticClass.MissingAsset]);
        Assert.Equal(3, tally.Total);
        Assert.Equal("unsupported format 2, not yet attempted 1", tally.Describe());
    }

    [Fact]
    public void ClassifiesDecoderMessagesWithoutInventingACode()
    {
        Assert.True(SceneryDecodeFailure.IsScrambledO3d(
            "a.o3d: offset 0x00000004: O3D vertex data is scrambled (key 0x00000000)"));

        Assert.Equal(
            SceneryDiagnosticClass.UnsupportedFormat,
            SceneryDecodeFailure.Classify(
                "a.o3d: offset 0x00000004: O3D vertex data is scrambled (key 0x00000000)"));
        Assert.Equal(
            SceneryDiagnosticClass.UnsupportedFormat,
            SceneryDecodeFailure.Classify(
                "a.x: the top-level object 'AnimTicksPerSecond' is not supported."));
        Assert.Equal(
            SceneryDiagnosticClass.ParseError,
            SceneryDecodeFailure.Classify(
                "a.x: offset 0x00000012: unexpected end of file while reading the vertex records."));
    }
}

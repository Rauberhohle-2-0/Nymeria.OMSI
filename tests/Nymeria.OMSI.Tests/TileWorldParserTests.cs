using Nymeria.Omsi.Maps;
using Nymeria.Omsi.World;

namespace Nymeria.OMSI.Tests;

/// <summary>
/// Headless tests for the tile-record parser. Every tile text here is authored in
/// this file; none of it is OMSI content, and passing these tests is not evidence
/// that a real Grundorf tile parses the same way.
/// </summary>
public sealed class TileWorldParserTests
{
    private const string TilePath = "/synthetic/tile_0_0.map";

    [Fact]
    public void Version12ObjectCapturesEveryDocumentedField()
    {
        TileParseResult result = Parse(
            "[version]\r\n" +
            "12\r\n" +
            "[object]\r\n" +
            "0\r\n" +
            "Sceneryobjects\\Test\\Box.sco\r\n" +
            "17\r\n" +
            "12.5\r\n" +
            "-3.25\r\n" +
            "1.75\r\n" +
            "90\r\n" +
            "-1.5\r\n" +
            "2.5\r\n" +
            "2\r\n" +
            "Label one\r\n" +
            "Label two\r\n");

        Assert.True(result.IsParsed);
        Assert.Equal(12, result.Version);
        Assert.Empty(result.Diagnostics);

        OmsiObjectPlacement placement = Assert.Single(result.Objects);
        Assert.Equal("Sceneryobjects\\Test\\Box.sco", placement.ModelPath);
        Assert.Equal(0, placement.DetailLevel);
        Assert.Equal(17, placement.IdCode);
        Assert.Equal(12.5, placement.X);
        Assert.Equal(-3.25, placement.Y);
        Assert.Equal(1.75, placement.Z);
        Assert.Equal(90, placement.HeadingDegrees);
        Assert.Equal(-1.5, placement.PitchDegrees);
        Assert.Equal(2.5, placement.BankDegrees);
        Assert.Equal(new[] { "Label one", "Label two" }, placement.Labels);
        Assert.Equal(3, placement.LineNumber);
    }

    [Fact]
    public void Version3ObjectHasNeitherDetailLevelNorIdentifierNorLabels()
    {
        TileParseResult result = Parse(
            "[version]\n" +
            "3\n" +
            "[object]\n" +
            "Sceneryobjects\\Old.sco\n" +
            "1\n" +
            "2\n" +
            "3\n" +
            "45\n");

        Assert.True(result.IsParsed);
        Assert.Empty(result.Diagnostics);

        OmsiObjectPlacement placement = Assert.Single(result.Objects);
        Assert.Equal("Sceneryobjects\\Old.sco", placement.ModelPath);
        Assert.Null(placement.DetailLevel);
        Assert.Null(placement.IdCode);
        Assert.Null(placement.PitchDegrees);
        Assert.Null(placement.BankDegrees);
        Assert.Empty(placement.Labels);
        Assert.Equal(1, placement.X);
        Assert.Equal(2, placement.Y);
        Assert.Equal(3, placement.Z);
        Assert.Equal(45, placement.HeadingDegrees);
    }

    [Fact]
    public void Version9ObjectReadsDetailLevelButNotPitchOrBank()
    {
        TileParseResult result = Parse(
            "[version]\n" +
            "9\n" +
            "[object]\n" +
            "1\n" +
            "Sceneryobjects\\Nine.sco\n" +
            "5\n" +
            "0\n" +
            "0\n" +
            "0\n" +
            "0\n" +
            "0\n");

        OmsiObjectPlacement placement = Assert.Single(result.Objects);
        Assert.Equal(1, placement.DetailLevel);
        Assert.Equal(5, placement.IdCode);
        Assert.Null(placement.PitchDegrees);
        Assert.Null(placement.BankDegrees);
        Assert.Empty(placement.Labels);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ObjectWithTooFewLinesIsReportedAndSkipped()
    {
        TileParseResult result = Parse(
            "[version]\n" +
            "12\n" +
            "[object]\n" +
            "0\n" +
            "Sceneryobjects\\Short.sco\n" +
            "1\n" +
            "0\n" +
            "0\n");

        Assert.Empty(result.Objects);
        MapDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(MapDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("documented layout needs 10", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(3, diagnostic.LineNumber);
    }

    [Fact]
    public void MalformedNumberIsReportedWithItsOwnLineAndTheRestOfTheTileIsKept()
    {
        TileParseResult result = Parse(
            "[version]\n" +
            "12\n" +
            "[object]\n" +
            "0\n" +
            "Sceneryobjects\\Broken.sco\n" +
            "1\n" +
            "0\n" +
            "not-a-number\n" +
            "0\n" +
            "0\n" +
            "0\n" +
            "0\n" +
            "0\n" +
            "0\n" +
            "[object]\n" +
            "0\n" +
            "Sceneryobjects\\Good.sco\n" +
            "2\n" +
            "0\n" +
            "0\n" +
            "0\n" +
            "0\n" +
            "0\n" +
            "0\n" +
            "0\n");

        OmsiObjectPlacement placement = Assert.Single(result.Objects);
        Assert.Equal("Sceneryobjects\\Good.sco", placement.ModelPath);

        MapDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Contains("y", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(8, diagnostic.LineNumber);
    }

    [Fact]
    public void NumericReferenceWhereAPathBelongsIsRejectedAsALayoutMismatch()
    {
        TileParseResult result = Parse(
            "[version]\n" +
            "3\n" +
            "[object]\n" +
            "0\n" +
            "1\n" +
            "2\n" +
            "3\n" +
            "4\n");

        // Version 3 has no leading detail level, so this record does not start
        // where the documented layout says; the parser must say so instead of
        // reporting an object whose path is the digit "0".
        Assert.Empty(result.Objects);
        MapDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Contains("expected a file reference", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Version12SplineReadsBothNeighboursAndTheRawGeometry()
    {
        TileParseResult result = Parse(
            "[version]\n" +
            "12\n" +
            "[spline]\n" +
            "0\n" +
            "Splines\\Test\\Road.sli\n" +
            "42\n" +
            "7\n" +
            "8\n" +
            "10.5\n" +
            "0.25\n" +
            "20.5\n" +
            "90\n" +
            "30\n" +
            "-15.5\n");

        Assert.True(result.IsParsed);
        Assert.Empty(result.Diagnostics);

        OmsiSplinePlacement spline = Assert.Single(result.Splines);
        Assert.Equal("Splines\\Test\\Road.sli", spline.ModelPath);
        Assert.Equal(42, spline.IdCode);
        Assert.Equal(7, spline.PreviousId);
        Assert.Equal(8, spline.NextId);
        Assert.Equal(10.5, spline.X);
        Assert.Equal(0.25, spline.Height);
        Assert.Equal(20.5, spline.Y);
        Assert.Equal(90, spline.HeadingDegrees);
        Assert.Equal(30, spline.Length);
        Assert.Equal(-15.5, spline.Radius);
        Assert.False(spline.IsHeightSpline);
        Assert.Equal(3, spline.LineNumber);
    }

    [Fact]
    public void SplineBeforeVersion11HasOneNeighbourAndNextIsNull()
    {
        TileParseResult result = Parse(
            "[version]\n" +
            "10\n" +
            "[spline]\n" +
            "0\n" +
            "Splines\\Test\\Road.sli\n" +
            "5\n" +
            "4\n" +
            "1\n" +
            "2\n" +
            "3\n" +
            "10\n" +
            "20\n" +
            "0\n");

        OmsiSplinePlacement spline = Assert.Single(result.Splines);
        Assert.Equal(5, spline.IdCode);
        Assert.Equal(4, spline.PreviousId);
        Assert.Null(spline.NextId);
        Assert.Equal(1, spline.X);
        Assert.Equal(2, spline.Height);
        Assert.Equal(3, spline.Y);
        Assert.Equal(10, spline.HeadingDegrees);
        Assert.Equal(20, spline.Length);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void SplineIdentifiersOfMinusOneMeanNone()
    {
        TileParseResult result = Parse(
            "[version]\n" +
            "12\n" +
            "[spline_h]\n" +
            "0\n" +
            "Splines\\Test\\Road.sli\n" +
            "-1\n" +
            "-1\n" +
            "-1\n" +
            "0\n" +
            "0\n" +
            "0\n" +
            "0\n" +
            "0\n" +
            "0\n");

        OmsiSplinePlacement spline = Assert.Single(result.Splines);
        Assert.True(spline.IsHeightSpline);
        Assert.Null(spline.IdCode);
        Assert.Null(spline.PreviousId);
        Assert.Null(spline.NextId);
    }

    [Fact]
    public void MissingVersionCountsRecordsWithoutInterpretingThem()
    {
        TileParseResult result = Parse(
            "[object]\n" +
            "0\n" +
            "Sceneryobjects\\A.sco\n" +
            "1\n" +
            "0\n" +
            "0\n" +
            "0\n" +
            "0\n" +
            "0\n" +
            "0\n" +
            "0\n" +
            "[spline]\n" +
            "0\n" +
            "Splines\\B.sli\n" +
            "1\n" +
            "-1\n" +
            "-1\n" +
            "0\n" +
            "0\n" +
            "0\n" +
            "0\n" +
            "0\n" +
            "0\n");

        Assert.False(result.IsParsed);
        Assert.Null(result.Version);
        Assert.Empty(result.Objects);
        Assert.Empty(result.Splines);

        MapDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Contains("no usable leading [version]", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("1 [object] and 1 [spline]", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonPositiveVersionIsRejectedAndTheTileStaysUnparsed()
    {
        TileParseResult result = Parse(
            "[version]\n" +
            "0\n" +
            "[object]\n");

        Assert.False(result.IsParsed);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "not a positive integer",
                StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateVersionKeepsTheFirstOne()
    {
        TileParseResult result = Parse(
            "[version]\n" +
            "12\n" +
            "[version]\n" +
            "9\n");

        Assert.Equal(12, result.Version);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "Duplicate [version]",
                StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateObjectIdentifierIsAWarningAndBothRecordsAreKept()
    {
        TileParseResult result = Parse(
            "[version]\n" +
            "12\n" +
            Object(3, "First.sco") +
            Object(3, "Second.sco"));

        Assert.Equal(2, result.Objects.Count);
        MapDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Contains("Duplicate object identifier 3", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownTileKeywordsAreCountedAndNotInterpreted()
    {
        TileParseResult result = Parse(
            "[version]\n" +
            "12\n" +
            "[terrain]\n" +
            "12\n" +
            "[terrain]\n" +
            "13\n" +
            "[rule]\n" +
            "0\n" +
            "[rule]\n" +
            "1\n" +
            Object(1, "Only.sco"));

        Assert.Single(result.Objects);

        OmsiUnsupportedTileRecord terrain = Assert.Single(
            result.UnsupportedRecords,
            record => record.Keyword == "terrain");
        Assert.Equal(2, terrain.Count);
        Assert.Equal(3, terrain.FirstLineNumber);

        OmsiUnsupportedTileRecord rule = Assert.Single(
            result.UnsupportedRecords,
            record => record.Keyword == "rule");
        Assert.Equal(2, rule.Count);
    }

    [Fact]
    public void LabelLinesThatLookLikeKeywordsAreLabels()
    {
        TileParseResult result = Parse(
            "[version]\n" +
            "12\n" +
            "[object]\n" +
            "0\n" +
            "Sign.sco\n" +
            "1\n" +
            "0\n" +
            "0\n" +
            "0\n" +
            "0\n" +
            "0\n" +
            "0\n" +
            "2\n" +
            "[notakeyword]\n" +
            "\n");

        OmsiObjectPlacement placement = Assert.Single(result.Objects);
        Assert.Equal(new[] { "[notakeyword]", string.Empty }, placement.Labels);
    }

    [Fact]
    public void EmptyReferenceIsRejectedWithItsLine()
    {
        TileParseResult result = Parse(
            "[version]\n" +
            "12\n" +
            "[object]\n" +
            "0\n" +
            "\n" +
            "1\n" +
            "0\n" +
            "0\n" +
            "0\n" +
            "0\n" +
            "0\n" +
            "0\n" +
            "0\n");

        Assert.Empty(result.Objects);
        MapDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Contains("empty file reference", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(5, diagnostic.LineNumber);
    }

    private static string Object(int id, string path)
    {
        return "[object]\n" +
            "0\n" +
            path + "\n" +
            id + "\n" +
            "1\n" +
            "2\n" +
            "3\n" +
            "0\n" +
            "0\n" +
            "0\n" +
            "0\n";
    }

    private static TileParseResult Parse(string content)
    {
        using var directory = new TemporaryDirectory();
        string tilePath = directory.WriteUtf8("tile_0_0.map", content);

        ConfigReadResult read = ConfigTextReader.Read(tilePath);

        Assert.NotNull(read.Text);
        Assert.Null(read.FatalError);

        // Reading a synthetic UTF-8 file must not itself produce warnings, so the
        // assertions below only see the parser's own diagnostics.
        Assert.Empty(read.Diagnostics);

        return TileWorldParser.Parse(read.Text, TilePath);
    }
}

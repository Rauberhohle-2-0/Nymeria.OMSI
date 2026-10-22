using Nymeria.Omsi.Maps;

namespace Nymeria.OMSI.Tests;

/// <summary>
/// Headless tests for the <c>[keyword]</c> block tokenizer. The input strings are
/// synthetic and authored for the tests.
/// </summary>
public sealed class KeywordBlockReaderTests
{
    [Fact]
    public void ParsesKeywordBlocksWithVerbatimParameters()
    {
        IReadOnlyList<KeywordBlock> blocks = Tokenize(
            "[name]\nTest Map\n[friendlyname]\nTest Map Friendly\n",
            out List<MapDiagnostic> diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(2, blocks.Count);
        Assert.Equal("name", blocks[0].Keyword);
        Assert.Equal(1, blocks[0].LineNumber);
        Assert.Equal("Test Map", Assert.Single(blocks[0].Parameters).Text);
        Assert.Equal("friendlyname", blocks[1].Keyword);
        Assert.Equal(3, blocks[1].LineNumber);
    }

    [Fact]
    public void KeepsEmptyLinesAsLegalEmptyParameters()
    {
        IReadOnlyList<KeywordBlock> blocks = Tokenize(
            "[groundtex]\n\n[map]\n0\n",
            out _);

        Assert.Equal(2, blocks.Count);
        Assert.Equal(string.Empty, Assert.Single(blocks[0].Parameters).Text);
        Assert.Equal("0", Assert.Single(blocks[1].Parameters).Text);
    }

    [Fact]
    public void IndentedKeywordIsTreatedAsParameterWithoutAWarning()
    {
        // Stock files use indented keyword lines as help text and switched-off
        // blocks; they must not become blocks and must not warn.
        IReadOnlyList<KeywordBlock> blocks = Tokenize(
            "[name]\nTest\n  [switched-off]\n1\n",
            out List<MapDiagnostic> diagnostics);

        Assert.Empty(diagnostics);
        KeywordBlock name = Assert.Single(blocks);
        Assert.Equal("name", name.Keyword);
        Assert.Equal(3, name.Parameters.Count);
        Assert.Equal("  [switched-off]", name.Parameters[1].Text);
    }

    [Fact]
    public void TrailingWhitespaceAfterAKeywordWarnsAndIsNotAKeyword()
    {
        IReadOnlyList<KeywordBlock> blocks = Tokenize(
            "[name]\nTest\n[version] \n1\n",
            out List<MapDiagnostic> diagnostics);

        MapDiagnostic warning = AssertWarning(diagnostics);
        Assert.Equal(3, warning.LineNumber ?? 0);
        Assert.Contains("trailing whitespace", warning.Message, StringComparison.Ordinal);

        KeywordBlock name = Assert.Single(blocks);
        Assert.Equal("name", name.Keyword);
        Assert.Equal(3, name.Parameters.Count);
    }

    [Fact]
    public void UnclosedKeywordLineWarns()
    {
        IReadOnlyList<KeywordBlock> blocks = Tokenize(
            "[name\nTest\n",
            out List<MapDiagnostic> diagnostics);

        Assert.Empty(blocks);
        MapDiagnostic warning = AssertWarning(diagnostics);
        Assert.Contains("not closed", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyKeywordWarns()
    {
        IReadOnlyList<KeywordBlock> blocks = Tokenize(
            "[]\nvalue\n",
            out List<MapDiagnostic> diagnostics);

        Assert.Empty(blocks);
        MapDiagnostic warning = AssertWarning(diagnostics);
        Assert.Contains("empty", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FreeTextBeforeTheFirstKeywordIsIgnored()
    {
        IReadOnlyList<KeywordBlock> blocks = Tokenize(
            "This is free text.\n\n[name]\nTest\n",
            out List<MapDiagnostic> diagnostics);

        Assert.Empty(diagnostics);
        KeywordBlock name = Assert.Single(blocks);
        Assert.Equal(3, name.LineNumber);
    }

    [Fact]
    public void LastBlockIsReturnedWithoutATrailingKeyword()
    {
        IReadOnlyList<KeywordBlock> blocks = Tokenize(
            "[name]\nTest",
            out _);

        KeywordBlock name = Assert.Single(blocks);
        Assert.Equal("Test", Assert.Single(name.Parameters).Text);
    }

    private static IReadOnlyList<KeywordBlock> Tokenize(
        string content,
        out List<MapDiagnostic> diagnostics)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.WriteUtf8("config.cfg", content);

        ConfigReadResult read = ConfigTextReader.Read(path);
        Assert.NotNull(read.Text);
        Assert.Null(read.FatalError);

        diagnostics = [.. read.Diagnostics];
        return KeywordBlockReader.Read(read.Text, path, diagnostics);
    }

    private static MapDiagnostic AssertWarning(List<MapDiagnostic> diagnostics)
    {
        MapDiagnostic warning = Assert.Single(diagnostics);
        Assert.Equal(MapDiagnosticSeverity.Warning, warning.Severity);
        return warning;
    }
}

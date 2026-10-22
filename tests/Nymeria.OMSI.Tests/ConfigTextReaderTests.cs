using System.Text;
using Nymeria.Omsi.Maps;

namespace Nymeria.OMSI.Tests;

/// <summary>
/// Headless tests for configuration text decoding and line splitting. The files
/// are synthetic strings created by the tests, not OMSI content.
/// </summary>
public sealed class ConfigTextReaderTests
{
    [Fact]
    public void ReadsUtf16LeWithByteOrderMark()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.WriteUtf16Le(
            "global.cfg",
            "[name]\r\nTest Map\r\n");

        ConfigReadResult result = ConfigTextReader.Read(path);

        Assert.Null(result.FatalError);
        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Text);
        Assert.Equal("UTF-16LE with byte-order mark", result.Text.EncodingName);
        Assert.Collection(
            result.Text.Lines,
            line =>
            {
                Assert.Equal(1, line.Number);
                Assert.Equal("[name]", line.Text);
            },
            line =>
            {
                Assert.Equal(2, line.Number);
                Assert.Equal("Test Map", line.Text);
            });
    }

    [Fact]
    public void ReadsUtf8WithByteOrderMark()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.WriteUtf8("global.cfg", "[name]\nTest", byteOrderMark: true);

        ConfigReadResult result = ConfigTextReader.Read(path);

        Assert.Null(result.FatalError);
        Assert.NotNull(result.Text);
        Assert.Equal("UTF-8 with byte-order mark", result.Text.EncodingName);
        Assert.Equal(2, result.Text.Lines.Count);
    }

    [Fact]
    public void ReadsUtf8WithoutByteOrderMark()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.WriteUtf8("global.cfg", "[name]\nTest");

        ConfigReadResult result = ConfigTextReader.Read(path);

        Assert.Null(result.FatalError);
        Assert.NotNull(result.Text);
        Assert.Equal("UTF-8", result.Text.EncodingName);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void InvalidUtf8FallsBackToLatin1WithAWarning()
    {
        using var directory = new TemporaryDirectory();

        byte[] prefix = Encoding.ASCII.GetBytes("[name]\nM");
        byte[] suffix = Encoding.ASCII.GetBytes("p");
        byte[] bytes = [.. prefix, 0xE4, .. suffix];

        string path = directory.WriteRaw("global.cfg", bytes);

        ConfigReadResult result = ConfigTextReader.Read(path);

        Assert.Null(result.FatalError);
        Assert.NotNull(result.Text);
        Assert.Contains("ISO-8859-1", result.Text.EncodingName, StringComparison.Ordinal);
        Assert.Equal("M\u00E4p", result.Text.Lines[1].Text);
        MapDiagnostic warning = Assert.Single(result.Diagnostics);
        Assert.Equal(MapDiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("not valid UTF-8", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Utf16LeWithOddByteCountIsRejected()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.WriteRaw("global.cfg", 0xFF, 0xFE, 0x41, 0x00, 0x42);

        ConfigReadResult result = ConfigTextReader.Read(path);

        Assert.Null(result.Text);
        Assert.NotNull(result.FatalError);
        Assert.Contains("odd number of bytes", result.FatalError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Utf16LeWithInvalidSurrogatesIsRejected()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.WriteRaw("global.cfg", 0xFF, 0xFE, 0x00, 0xD8);

        ConfigReadResult result = ConfigTextReader.Read(path);

        Assert.Null(result.Text);
        Assert.NotNull(result.FatalError);
    }

    [Fact]
    public void Utf32ByteOrderMarkIsRejected()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.WriteRaw("global.cfg", 0xFF, 0xFE, 0x00, 0x00, 0x41, 0x00);

        ConfigReadResult result = ConfigTextReader.Read(path);

        Assert.Null(result.Text);
        Assert.NotNull(result.FatalError);
        Assert.Contains("UTF-32", result.FatalError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NulCharactersProduceAUtf16WithoutBomWarning()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.WriteRaw(
            "global.cfg",
            0x6E, 0x00, 0x61, 0x00, 0x6D, 0x00, 0x65, 0x00);

        ConfigReadResult result = ConfigTextReader.Read(path);

        Assert.Null(result.FatalError);
        MapDiagnostic warning = Assert.Single(result.Diagnostics);
        Assert.Contains("NUL", warning.Message, StringComparison.Ordinal);
        Assert.Contains("UTF-16LE", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SplitsCrLfCrAndLfWithoutCreatingATrailingEmptyLine()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.WriteUtf8("global.cfg", "a\r\nb\rc\nd\n");

        ConfigReadResult result = ConfigTextReader.Read(path);

        Assert.NotNull(result.Text);
        Assert.Collection(
            result.Text.Lines,
            line => Assert.Equal(("a", 1), (line.Text, line.Number)),
            line => Assert.Equal(("b", 2), (line.Text, line.Number)),
            line => Assert.Equal(("c", 3), (line.Text, line.Number)),
            line => Assert.Equal(("d", 4), (line.Text, line.Number)));
    }

    [Fact]
    public void EmptyFileDecodesToZeroLines()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.CreateEmptyFile("global.cfg");

        ConfigReadResult result = ConfigTextReader.Read(path);

        Assert.Null(result.FatalError);
        Assert.NotNull(result.Text);
        Assert.Empty(result.Text.Lines);
    }

    [Fact]
    public void FileBeyondTheByteLimitIsRejected()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.WriteRaw(
            "global.cfg",
            new byte[ConfigTextReader.MaxFileBytes + 1]);

        ConfigReadResult result = ConfigTextReader.Read(path);

        Assert.Null(result.Text);
        Assert.NotNull(result.FatalError);
        Assert.Contains("exceeds", result.FatalError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingFileIsReportedWithItsPath()
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, "absent.cfg");

        ConfigReadResult result = ConfigTextReader.Read(path);

        Assert.Null(result.Text);
        Assert.NotNull(result.FatalError);
        Assert.Equal(path, result.FatalError.FilePath);
        Assert.Contains("could not be read", result.FatalError.Message, StringComparison.Ordinal);
    }
}

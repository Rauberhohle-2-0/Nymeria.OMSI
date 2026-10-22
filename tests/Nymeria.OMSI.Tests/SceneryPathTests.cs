using Nymeria.Omsi.Scenery;

namespace Nymeria.OMSI.Tests;

/// <summary>
/// Headless tests for scenery content path resolution.
/// </summary>
/// <remarks>
/// These tests use synthetic folders created by the test itself. They prove the
/// traversal and absolute-path rules, not that a real OMSI installation resolves.
/// </remarks>
public sealed class SceneryPathTests
{
    [Theory]
    [InlineData(@"..\..\Windows\System32\evil.sco")]
    [InlineData("../maps/TestMap/global.cfg")]
    [InlineData(@"Sceneryobjects\..\..\secret.sco")]
    [InlineData(@"Sceneryobjects\.\object.sco")]
    [InlineData(@"C:\Windows\notepad.exe")]
    [InlineData(@"C:object.sco")]
    [InlineData(@"\\server\share\object.sco")]
    [InlineData(@"/etc/passwd")]
    [InlineData(@"Sceneryobjects\\object.sco")]
    [InlineData(@"Sceneryobjects\object.sco:stream")]
    [InlineData("")]
    public void RejectsReferencesThatAreAbsoluteOrContainRelativeComponents(string reference)
    {
        Assert.False(SceneryPath.TryNormalize(reference, out string[] segments, out string? error));

        Assert.Empty(segments);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void RejectsControlCharacters()
    {
        Assert.False(SceneryPath.TryNormalize("Sceneryobjects/a\u0007b.sco", out _, out _));
    }

    [Fact]
    public void NormalizesBothSeparators()
    {
        Assert.True(SceneryPath.TryNormalize(
            @"Sceneryobjects\Test\object.sco",
            out string[] segments,
            out string? error));

        Assert.Null(error);
        Assert.Equal(["Sceneryobjects", "Test", "object.sco"], segments);
    }

    [Fact]
    public void ResolvesARelativeReferenceInsideTheRoot()
    {
        using var root = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(root.Path, "Sceneryobjects", "Object"));
        string expected = root.WriteUtf8(
            Path.Combine("Sceneryobjects", "Object", "object.sco"),
            "[model]\r\nmodel.cfg\r\n");

        SceneryPathResult result = SceneryPath.Resolve(root.Path, "Sceneryobjects/Object/object.sco");

        Assert.Equal(SceneryPathStatus.Resolved, result.Status);
        Assert.Equal(Path.GetFullPath(expected), Path.GetFullPath(result.Path!));
    }

    [Fact]
    public void ReportsAMissingEntryWithoutThrowing()
    {
        using var root = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(root.Path, "Sceneryobjects"));

        SceneryPathResult result = SceneryPath.Resolve(root.Path, "Sceneryobjects/Absent.sco");

        Assert.Equal(SceneryPathStatus.NotFound, result.Status);
        Assert.Null(result.Path);
        Assert.Contains("Absent.sco", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsATraversalBeforeTouchingTheFileSystem()
    {
        using var root = new TemporaryDirectory();

        SceneryPathResult result = SceneryPath.Resolve(root.Path, "../outside.sco");

        Assert.Equal(SceneryPathStatus.Rejected, result.Status);
        Assert.Null(result.Path);
        Assert.Contains("..", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsAMissingRootAsNotFound()
    {
        using var root = new TemporaryDirectory();

        SceneryPathResult result = SceneryPath.Resolve(
            Path.Combine(root.Path, "does-not-exist"),
            "object.sco");

        Assert.Equal(SceneryPathStatus.NotFound, result.Status);
        Assert.Contains("does not exist", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsADirectoryMatchAsNotAFile()
    {
        using var root = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(root.Path, "Sceneryobjects"));

        SceneryPathResult result = SceneryPath.Resolve(root.Path, "Sceneryobjects");

        Assert.Equal(SceneryPathStatus.NotFound, result.Status);
        Assert.Contains("not a file", result.Error, StringComparison.Ordinal);
    }
}

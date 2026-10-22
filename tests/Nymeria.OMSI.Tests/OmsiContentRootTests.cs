using Nymeria.Omsi.Maps;

namespace Nymeria.OMSI.Tests;

/// <summary>
/// Headless tests for deriving the OMSI installation root from a map folder path.
/// They use path strings only; no OMSI installation is read or required.
/// </summary>
public sealed class OmsiContentRootTests
{
    [Theory]
    [InlineData(@"C:\Program Files (x86)\Steam\steamapps\common\OMSI 2\maps\Grundorf",
        @"C:\Program Files (x86)\Steam\steamapps\common\OMSI 2")]
    [InlineData("/games/OMSI 2/maps/Grundorf", "/games/OMSI 2")]
    [InlineData("/games/OMSI 2/Maps/Grundorf", "/games/OMSI 2")]
    [InlineData("/games/OMSI 2/maps/Grundorf/", "/games/OMSI 2")]
    [InlineData(@"C:\OMSI 2\maps\Grundorf\", @"C:\OMSI 2")]
    public void MapFolderInsideMapsDerivesTheRoot(string mapDirectory, string expected)
    {
        bool derived = OmsiContentRoot.TryDerive(
            mapDirectory,
            out string? root,
            out string reason);

        Assert.True(derived);
        Assert.Equal(expected, root);
        Assert.NotEmpty(reason);
    }

    [Theory]
    [InlineData("/games/OMSI 2/Grundorf")]
    [InlineData("/games/OMSI 2/maps")]
    [InlineData(@"C:\maps\Grundorf")]
    [InlineData("maps/Grundorf")]
    [InlineData("Grundorf")]
    [InlineData("")]
    public void AnythingElseIsReportedAsUnresolved(string mapDirectory)
    {
        bool derived = OmsiContentRoot.TryDerive(
            mapDirectory,
            out string? root,
            out string reason);

        Assert.False(derived);
        Assert.Null(root);
        Assert.NotEmpty(reason);
    }

    [Fact]
    public void MixedSeparatorsStillResolveTheMapsParent()
    {
        Assert.True(OmsiContentRoot.TryDerive(
            @"C:\OMSI 2/maps/Grundorf",
            out string? root,
            out _));
        Assert.Equal(@"C:\OMSI 2", root);
    }

    [Fact]
    public void NullPathIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            OmsiContentRoot.TryDerive(null!, out _, out _));
    }
}

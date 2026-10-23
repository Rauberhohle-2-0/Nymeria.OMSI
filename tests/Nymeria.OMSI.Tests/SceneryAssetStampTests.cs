using Nymeria.Omsi.Scenery;

namespace Nymeria.OMSI.Tests;

/// <summary>
/// Headless tests for the cache validity stamp.
/// </summary>
/// <remarks>
/// The stamp is a cheap in-run guard, not a content identity: these tests pin down what
/// it does and does not detect, so the documentation and the code cannot drift apart.
/// </remarks>
public sealed class SceneryAssetStampTests
{
    [Fact]
    public void ReadsTheSizeAndWriteTimeOfAnExistingFile()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.WriteRaw("mesh.o3d", 1, 2, 3, 4);

        Assert.True(SceneryAssetStamp.TryRead(path, out SceneryAssetStamp stamp));
        Assert.Equal(4, stamp.Length);
        Assert.NotEqual(default, stamp.LastWriteTimeUtc);
    }

    [Fact]
    public void ChangesWhenTheFileSizeChanges()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.WriteRaw("mesh.o3d", 1, 2, 3, 4);

        Assert.True(SceneryAssetStamp.TryRead(path, out SceneryAssetStamp before));

        directory.WriteRaw("mesh.o3d", 1, 2, 3, 4, 5);

        Assert.True(SceneryAssetStamp.TryRead(path, out SceneryAssetStamp after));
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void ChangesWhenOnlyTheWriteTimeChanges()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.WriteRaw("mesh.o3d", 1, 2, 3, 4);

        Assert.True(SceneryAssetStamp.TryRead(path, out SceneryAssetStamp before));

        File.SetLastWriteTimeUtc(path, before.LastWriteTimeUtc.AddMinutes(-5));

        Assert.True(SceneryAssetStamp.TryRead(path, out SceneryAssetStamp after));
        Assert.Equal(before.Length, after.Length);
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void FailsForAMissingFile()
    {
        using var directory = new TemporaryDirectory();

        Assert.False(
            SceneryAssetStamp.TryRead(
                Path.Combine(directory.Path, "absent.o3d"),
                out _));
    }
}

namespace Nymeria.Omsi.Scenery;

/// <summary>
/// The identity stamp attached to a cached scenery asset within one load.
/// </summary>
/// <remarks>
/// <para>
/// The loader caches by resolved path, not by this stamp. The stamp is a validity
/// record beside the entry: when a cached asset is reused, the stamp is compared with
/// the file on disk, and a changed size or last-write time makes the entry be decoded
/// again. That catches the only case a one-process cache can get wrong — a file
/// replaced while the scene is still being loaded.
/// </para>
/// <para>
/// The stamp is not a content identity and must not be persisted as one. Last-write
/// time granularity is coarse on FAT volumes and network shares, archiving tools
/// preserve it, and two different files can share a size and a timestamp. A future
/// on-disk cache needs a hash of the bytes the decoders already read.
/// </para>
/// </remarks>
/// <param name="Length">The file length in bytes.</param>
/// <param name="LastWriteTimeUtc">The file's last write time, in UTC.</param>
internal readonly record struct SceneryAssetStamp(long Length, DateTime LastWriteTimeUtc)
{
    /// <summary>
    /// Reads the stamp of an existing file.
    /// </summary>
    /// <param name="path">The resolved asset path.</param>
    /// <param name="stamp">The stamp, when the file exists and can be inspected.</param>
    /// <returns><see langword="true"/> when a stamp was read.</returns>
    internal static bool TryRead(string path, out SceneryAssetStamp stamp)
    {
        stamp = default;

        try
        {
            var file = new FileInfo(path);

            if (!file.Exists)
            {
                return false;
            }

            stamp = new SceneryAssetStamp(file.Length, file.LastWriteTimeUtc);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException or
            System.Security.SecurityException)
        {
            return false;
        }
    }
}

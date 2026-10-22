using System.Globalization;

namespace Nymeria.Omsi.Maps;

/// <summary>
/// Parses the documented <c>tile_x_y.map</c> file-name convention.
/// </summary>
/// <remarks>
/// The coordinates are the tile column and row. They are parsed as invariant
/// signed 32-bit integers, so negative coordinates work and no coordinate range is
/// assumed. Matching is case-insensitive because OMSI resolves content file names
/// without regard to case; the original spelling is preserved in the manifest.
/// </remarks>
internal static class TileFileNames
{
    internal const string Prefix = "tile_";
    internal const string Extension = ".map";

    /// <summary>
    /// Reports whether a file name is a candidate tile file for diagnostics, i.e.
    /// it uses the <c>tile_</c> prefix and the <c>.map</c> extension.
    /// </summary>
    /// <param name="fileName">The file name without a directory.</param>
    internal static bool HasTilePrefix(string fileName)
    {
        return fileName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) &&
            fileName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parses a tile file name into its coordinates.
    /// </summary>
    /// <param name="fileName">The file name without a directory.</param>
    /// <param name="x">The parsed column, when the name is valid.</param>
    /// <param name="y">The parsed row, when the name is valid.</param>
    /// <returns><see langword="true"/> when the name matches the convention.</returns>
    internal static bool TryParse(string fileName, out int x, out int y)
    {
        x = 0;
        y = 0;

        if (!HasTilePrefix(fileName))
        {
            return false;
        }

        string inner = fileName[Prefix.Length..^Extension.Length];
        string[] parts = inner.Split('_');

        if (parts.Length != 2)
        {
            return false;
        }

        return TryParseCoordinate(parts[0], out x) &&
            TryParseCoordinate(parts[1], out y);
    }

    private static bool TryParseCoordinate(string text, out int value)
    {
        return int.TryParse(
            text,
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out value);
    }
}

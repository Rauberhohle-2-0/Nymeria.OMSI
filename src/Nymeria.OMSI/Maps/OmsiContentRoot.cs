namespace Nymeria.Omsi.Maps;

/// <summary>
/// Derives the OMSI content root from a map folder path.
/// </summary>
/// <remarks>
/// <para>
/// OMSI keeps its content in installation-wide folders beside <c>maps</c>:
/// <c>Sceneryobjects</c>, <c>Splines</c>, <c>Vehicles</c>, and the rest. A tile's
/// references are therefore resolved against that root, not against the map folder.
/// </para>
/// <para>
/// The derivation is deliberately narrow: a map folder counts as
/// <c>&lt;root&gt;/maps/&lt;map&gt;</c> only when its parent directory is named
/// <c>maps</c>, case-insensitively. Anything else is reported as unresolved instead
/// of guessing an installation path, and no default Steam or Aerosoft location is
/// ever assumed.
/// </para>
/// <para>
/// Both <c>/</c> and <c>\</c> are treated as separators, so a Windows path stays
/// analysable on macOS and a reported path keeps its original spelling.
/// </para>
/// </remarks>
internal static class OmsiContentRoot
{
    private const string MapsFolderName = "maps";

    /// <summary>
    /// Tries to derive the content root of a map folder.
    /// </summary>
    /// <param name="mapDirectory">The map folder that contains <c>global.cfg</c>.</param>
    /// <param name="root">
    /// The derived root, or <see langword="null"/> when the path is not inside a
    /// folder named <c>maps</c>.
    /// </param>
    /// <param name="reason">A readable explanation of the outcome.</param>
    /// <returns><see langword="true"/> when a root was derived.</returns>
    internal static bool TryDerive(
        string mapDirectory,
        out string? root,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(mapDirectory);

        root = null;

        string trimmed = TrimTrailingSeparators(mapDirectory);

        if (trimmed.Length == 0)
        {
            reason = "The map folder path is empty.";
            return false;
        }

        int separator = LastSeparator(trimmed);

        if (separator < 0)
        {
            reason =
                "'" + mapDirectory + "' has no parent folder, so it is not a map " +
                "folder inside an installation.";
            return false;
        }

        string parent = trimmed[..separator];
        int parentSeparator = LastSeparator(parent);

        string parentName = parentSeparator < 0
            ? parent
            : parent[(parentSeparator + 1)..];

        if (!string.Equals(parentName, MapsFolderName, StringComparison.OrdinalIgnoreCase))
        {
            reason =
                "The folder '" + parentName + "' is not named 'maps', so the " +
                "installation root cannot be derived from this path.";
            return false;
        }

        string candidate = parentSeparator < 0 ? string.Empty : parent[..parentSeparator];

        if (candidate.Length == 0 || candidate == "/" || IsDriveRoot(candidate))
        {
            reason =
                "'" + mapDirectory + "' has no folder above 'maps', so it is not " +
                "inside an installation.";
            return false;
        }

        root = candidate;
        reason = "The map folder is inside a 'maps' folder.";
        return true;
    }

    private static string TrimTrailingSeparators(string path)
    {
        string trimmed = path.TrimEnd('/', '\\');

        // Keep a bare root ("/" or "C:\") intact rather than trimming it to nothing.
        return trimmed.Length == 0 ? path : trimmed;
    }

    private static int LastSeparator(string path)
    {
        return path.LastIndexOfAny(['/', '\\']);
    }

    private static bool IsDriveRoot(string path)
    {
        return path.Length == 2 && path[1] == ':';
    }
}

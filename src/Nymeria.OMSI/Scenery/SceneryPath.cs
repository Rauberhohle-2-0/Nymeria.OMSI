using System.Globalization;

namespace Nymeria.Omsi.Scenery;

/// <summary>The outcome of resolving one scenery content reference.</summary>
internal enum SceneryPathStatus
{
    /// <summary>The reference was resolved to an existing file.</summary>
    Resolved,

    /// <summary>
    /// The reference is not an acceptable relative content path, or it would leave
    /// the permitted asset root, so it was rejected before touching the filesystem.
    /// </summary>
    Rejected,

    /// <summary>The reference is acceptable but no matching entry exists.</summary>
    NotFound,
}

/// <summary>
/// One resolved or rejected scenery content reference.
/// </summary>
/// <param name="Status">Whether the reference was resolved, rejected, or absent.</param>
/// <param name="Path">
/// The full path of the existing entry, or <see langword="null"/> when there is none.
/// </param>
/// <param name="Reference">The reference exactly as it appeared in the source file.</param>
/// <param name="Error">A readable explanation when the reference was not resolved.</param>
internal readonly record struct SceneryPathResult(
    SceneryPathStatus Status,
    string? Path,
    string Reference,
    string? Error)
{
    /// <summary>Gets a value indicating whether an existing file was found.</summary>
    internal bool IsResolved => Status == SceneryPathStatus.Resolved && Path is not null;
}

/// <summary>
/// Resolves OMSI scenery references against a content root without leaving it.
/// </summary>
/// <remarks>
/// <para>
/// OMSI content paths are written for Windows: <c>\</c> and <c>/</c> both separate
/// components, and the file system that holds the original installation is
/// case-insensitive. A reference is therefore normalized to <c>/</c>-separated
/// components for inspection, and every component is then matched against a real
/// directory entry case-insensitively, so an exact-case path resolves on Linux or
/// macOS without weakening the check.
/// </para>
/// <para>
/// OMSI content also uses <c>..</c> deliberately: an object may point at a mesh in a
/// sibling collection, for example
/// <c>..\..\Kreuz_MC\model\Einm_altonaer.o3d</c>. Such a reference is accepted as
/// long as the normalized result stays below the permitted content root. A reference
/// that is absolute, drive-qualified, has an empty component, a control character, a
/// colon inside a component, or that would climb above the root is rejected rather
/// than repaired.
/// </para>
/// <para>
/// Because only normalized components are combined with a directory, and each
/// combined path is required to stay under the root, a resolved file cannot escape
/// the authorized content tree.
/// </para>
/// <para>
/// This type performs file-system lookups only. It creates no GPU resource and reads
/// no content file.
/// </para>
/// </remarks>
internal static class SceneryPath
{
    /// <summary>The longest accepted reference, in characters.</summary>
    internal const int MaxReferenceLength = 512;

    /// <summary>The largest accepted component count.</summary>
    internal const int MaxSegmentCount = 32;

    /// <summary>
    /// The number of directory entries inspected for one case-insensitive match.
    /// </summary>
    /// <remarks>
    /// A wider directory than this is reported instead of scanned, which bounds the
    /// cost of a hostile or damaged installation layout.
    /// </remarks>
    internal const int MaxDirectoryEntries = 20_000;

    /// <summary>
    /// Normalizes a reference into its components.
    /// </summary>
    /// <remarks>
    /// <c>.</c> components are dropped. <c>..</c> components are kept for the caller,
    /// which must combine them with a base directory and prove that the result stays
    /// inside the content root; a reference on its own cannot show that.
    /// </remarks>
    /// <param name="reference">The reference exactly as written in the source file.</param>
    /// <param name="segments">The normalized components, or empty on rejection.</param>
    /// <param name="error">A readable explanation when the reference is rejected.</param>
    /// <returns><see langword="true"/> when the reference is an acceptable relative path.</returns>
    internal static bool TryNormalize(
        string reference,
        out string[] segments,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(reference);

        segments = [];
        error = null;

        if (reference.Length == 0)
        {
            error = "The reference is empty.";
            return false;
        }

        if (reference.Length > MaxReferenceLength)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"The reference is {reference.Length} characters long, which exceeds the {MaxReferenceLength}-character limit.");
            return false;
        }

        if (reference[0] == '/' || reference[0] == '\\')
        {
            error =
                "The reference starts with a path separator, so it is absolute " +
                "rather than relative to the content root.";
            return false;
        }

        if (reference.Length >= 2 && reference[1] == ':')
        {
            error =
                "The reference is drive-qualified, so it is absolute rather than " +
                "relative to the content root.";
            return false;
        }

        string[] parts = reference.Split(['/', '\\']);

        if (parts.Length > MaxSegmentCount)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"The reference has {parts.Length} components, which exceeds the {MaxSegmentCount}-component limit.");
            return false;
        }

        var normalized = new List<string>(parts.Length);

        foreach (string part in parts)
        {
            if (part.Length == 0)
            {
                error =
                    "The reference contains an empty path component, which OMSI " +
                    "never writes.";
                return false;
            }

            if (part == ".")
            {
                continue;
            }

            if (part.Contains(':', StringComparison.Ordinal))
            {
                error =
                    "The reference contains ':' inside a component, which is not a " +
                    "valid content path.";
                return false;
            }

            foreach (char character in part)
            {
                if (char.IsControl(character))
                {
                    error = "The reference contains a control character.";
                    return false;
                }
            }

            normalized.Add(part);
        }

        segments = [.. normalized];
        return true;
    }

    /// <summary>
    /// Resolves a reference relative to the content root.
    /// </summary>
    /// <param name="root">The existing directory that bounds every resolution.</param>
    /// <param name="reference">The reference exactly as written in the source file.</param>
    /// <returns>The resolution outcome; it never throws for absent or invalid input.</returns>
    internal static SceneryPathResult Resolve(string root, string reference)
    {
        return ResolveRelative(root, root, reference);
    }

    /// <summary>
    /// Resolves a reference relative to a base directory that must stay under the
    /// content root.
    /// </summary>
    /// <param name="root">
    /// The existing directory that bounds the result, normally the OMSI installation
    /// root.
    /// </param>
    /// <param name="baseDirectory">
    /// The directory the reference is written relative to, normally the folder of the
    /// configuration file that contains it.
    /// </param>
    /// <param name="reference">The reference exactly as written in the source file.</param>
    /// <returns>The resolution outcome; it never throws for absent or invalid input.</returns>
    internal static SceneryPathResult ResolveRelative(
        string root,
        string baseDirectory,
        string reference)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(baseDirectory);

        if (!TryNormalize(reference, out string[] segments, out string? error))
        {
            return new SceneryPathResult(
                SceneryPathStatus.Rejected,
                null,
                reference,
                error);
        }

        if (!Directory.Exists(root))
        {
            return new SceneryPathResult(
                SceneryPathStatus.NotFound,
                null,
                reference,
                $"The root folder '{root}' does not exist.");
        }

        string fullRoot = Path.GetFullPath(root);
        string fullBase = Path.GetFullPath(baseDirectory);

        if (!TryGetBaseSegments(fullRoot, fullBase, out List<string> combined, out error))
        {
            return new SceneryPathResult(
                SceneryPathStatus.Rejected,
                null,
                reference,
                error);
        }

        foreach (string segment in segments)
        {
            if (segment == "..")
            {
                if (combined.Count == 0)
                {
                    return new SceneryPathResult(
                        SceneryPathStatus.Rejected,
                        null,
                        reference,
                        $"The reference '{reference}' climbs above the permitted " +
                        $"content root '{root}'.");
                }

                combined.RemoveAt(combined.Count - 1);
                continue;
            }

            if (combined.Count >= MaxSegmentCount)
            {
                return new SceneryPathResult(
                    SceneryPathStatus.Rejected,
                    null,
                    reference,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The resolved reference has more than {MaxSegmentCount} components."));
            }

            combined.Add(segment);
        }

        string current = fullRoot;

        foreach (string segment in combined)
        {
            if (!TryResolveSegment(current, segment, out string? resolved, out error))
            {
                return new SceneryPathResult(
                    SceneryPathStatus.NotFound,
                    null,
                    reference,
                    error);
            }

            current = resolved!;
        }

        return File.Exists(current)
            ? new SceneryPathResult(SceneryPathStatus.Resolved, current, reference, null)
            : new SceneryPathResult(
                SceneryPathStatus.NotFound,
                null,
                reference,
                $"'{reference}' resolved to '{current}', which is not a file.");
    }

    /// <summary>
    /// Computes the components of a base directory relative to the content root.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the base directory is the root itself or a
    /// directory below it.
    /// </returns>
    private static bool TryGetBaseSegments(
        string fullRoot,
        string fullBase,
        out List<string> segments,
        out string? error)
    {
        segments = [];
        error = null;

        string relative = Path.GetRelativePath(fullRoot, fullBase);

        if (relative == ".")
        {
            return true;
        }

        if (Path.IsPathRooted(relative) ||
            relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            relative.StartsWith("../", StringComparison.Ordinal))
        {
            error =
                $"The folder '{fullBase}' is not below the permitted content root " +
                $"'{fullRoot}'.";
            return false;
        }

        segments = [.. relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries)];

        return true;
    }

    /// <summary>
    /// Finds the entry in one directory whose name matches a component
    /// case-insensitively.
    /// </summary>
    /// <param name="directory">The directory to look in.</param>
    /// <param name="segment">The component name from the reference.</param>
    /// <param name="resolved">The matching entry's full path, when one was found.</param>
    /// <param name="error">A readable explanation when no entry matched.</param>
    /// <returns><see langword="true"/> when an entry matched.</returns>
    private static bool TryResolveSegment(
        string directory,
        string segment,
        out string? resolved,
        out string? error)
    {
        resolved = null;
        error = null;

        // The fast path is the exact spelling, which is what a Windows-authored
        // reference gives when the case was preserved on disk.
        string exact = Path.Combine(directory, segment);

        if (File.Exists(exact) || Directory.Exists(exact))
        {
            resolved = exact;
            return true;
        }

        string? match = null;
        int inspected = 0;

        try
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (inspected++ >= MaxDirectoryEntries)
                {
                    error = string.Create(
                        CultureInfo.InvariantCulture,
                        $"'{directory}' has more than {MaxDirectoryEntries} entries, so the case-insensitive match for '{segment}' was not attempted.");
                    return false;
                }

                string name = Path.GetFileName(entry);

                if (!string.Equals(name, segment, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (match is not null)
                {
                    error =
                        $"'{directory}' contains more than one entry that matches " +
                        $"'{segment}' ignoring case, so the reference is ambiguous.";
                    return false;
                }

                match = entry;
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            System.Security.SecurityException)
        {
            error = $"'{directory}' could not be listed: {exception.Message}";
            return false;
        }

        if (match is null)
        {
            error = $"'{segment}' was not found in '{directory}'.";
            return false;
        }

        resolved = match;
        return true;
    }
}

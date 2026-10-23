using System.Globalization;
using Nymeria.Omsi.Maps;

namespace Nymeria.Omsi.Scenery;

/// <summary>
/// The class every skipped or partially rendered scenery asset is counted under.
/// </summary>
/// <remarks>
/// <para>
/// A diagnose-first scene has to say why an asset is absent, not only that it is. These
/// classes are the vocabulary the console summary and the diagnostics use, and each
/// reported failure carries exactly one of them.
/// </para>
/// <para>
/// <see cref="MissingTextureMapping"/> is deliberately separate from
/// <see cref="MissingAsset"/>: a mesh material whose name is a label such as
/// <c>Mat1</c> and that no <c>[matl]</c> entry binds to a file has no texture to be
/// missing, and counting it as a missing file would overstate the damage on every
/// object that uses label materials.
/// </para>
/// </remarks>
internal enum SceneryDiagnosticClass
{
    /// <summary>A referenced file does not exist, or a declared texture file is absent.</summary>
    MissingAsset,

    /// <summary>
    /// The file exists and the decoder refused it: scrambled O3D vertices, an
    /// unsupported texture encoding, or a documented size or feature limit.
    /// </summary>
    UnsupportedFormat,

    /// <summary>The file exists but its content is malformed or unreadable.</summary>
    ParseError,

    /// <summary>
    /// A GPU resource could not be created or uploaded. Produced by the renderer, never
    /// by the CPU-side loader.
    /// </summary>
    GpuFailure,

    /// <summary>
    /// Present in real content and deliberately not interpreted: scripts, animations,
    /// interaction, paths, sounds, traffic lights, collision, and similar.
    /// </summary>
    OutOfScopeByDesign,

    /// <summary>The asset was not attempted because a documented load budget was hit.</summary>
    NotAttempted,

    /// <summary>
    /// A mesh material has no texture binding at all, so the subset is drawn with its
    /// base color.
    /// </summary>
    MissingTextureMapping,
}

/// <summary>
/// Reads and writes the readable name of a <see cref="SceneryDiagnosticClass"/>.
/// </summary>
internal static class SceneryDiagnosticClasses
{
    /// <summary>Gets every class, in declaration order.</summary>
    internal static IReadOnlyList<SceneryDiagnosticClass> All { get; } =
        [.. Enum.GetValues<SceneryDiagnosticClass>()];

    /// <summary>
    /// Returns the short lower-case name used in the console summary.
    /// </summary>
    /// <param name="kind">The class to name.</param>
    internal static string Describe(SceneryDiagnosticClass kind)
    {
        return kind switch
        {
            SceneryDiagnosticClass.MissingAsset => "missing asset",
            SceneryDiagnosticClass.UnsupportedFormat => "unsupported format",
            SceneryDiagnosticClass.ParseError => "parse error",
            SceneryDiagnosticClass.GpuFailure => "GPU failure",
            SceneryDiagnosticClass.OutOfScopeByDesign => "out of scope by design",
            SceneryDiagnosticClass.NotAttempted => "not yet attempted",
            SceneryDiagnosticClass.MissingTextureMapping => "missing texture mapping",
            _ => kind.ToString(),
        };
    }
}

/// <summary>
/// One listed diagnostic together with the class it was reported under and how many
/// times an equivalent failure occurred.
/// </summary>
/// <param name="Kind">The failure class.</param>
/// <param name="Diagnostic">The first occurrence, which names the asset and the reason.</param>
/// <param name="Occurrences">How many equivalent failures were counted.</param>
internal readonly record struct SceneryDiagnosticEntry(
    SceneryDiagnosticClass Kind,
    MapDiagnostic Diagnostic,
    int Occurrences);

/// <summary>
/// Counts occurrences per <see cref="SceneryDiagnosticClass"/>.
/// </summary>
/// <remarks>
/// One tally is kept per asset kind (placements, meshes, textures, subsets), so a
/// summary line can say how many of that kind failed for each reason instead of
/// collapsing them into a single total.
/// </remarks>
internal sealed class SceneryClassTally
{
    private readonly int[] _counts = new int[SceneryDiagnosticClasses.All.Count];

    /// <summary>Gets the number of occurrences counted under one class.</summary>
    /// <param name="kind">The class to read.</param>
    internal int this[SceneryDiagnosticClass kind] => _counts[(int)kind];

    /// <summary>Gets the number of occurrences counted across all classes.</summary>
    internal int Total { get; private set; }

    /// <summary>Counts one occurrence of a class.</summary>
    /// <param name="kind">The class to count.</param>
    internal void Add(SceneryDiagnosticClass kind)
    {
        _counts[(int)kind]++;
        Total++;
    }

    /// <summary>
    /// Formats the non-zero classes as <c>name N, name N</c>, in declaration order.
    /// </summary>
    internal string Describe()
    {
        var parts = new List<string>(SceneryDiagnosticClasses.All.Count);

        foreach (SceneryDiagnosticClass kind in SceneryDiagnosticClasses.All)
        {
            int count = this[kind];

            if (count > 0)
            {
                parts.Add(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{SceneryDiagnosticClasses.Describe(kind)} {count}"));
            }
        }

        return parts.Count == 0 ? "none" : string.Join(", ", parts);
    }
}

/// <summary>
/// Collects the diagnostics of one scenery load without repeating an asset failure.
/// </summary>
/// <remarks>
/// <para>
/// OMSI content is shared: one damaged <c>.sco</c> can be referenced by hundreds of
/// placements, and one missing texture can be named by many subsets. A failure is
/// therefore deduplicated by <c>(canonical path, class)</c> — not by message — so the
/// same asset failing for the same reason is one line with an occurrence counter, while
/// the same asset failing for a different reason stays a second, distinguishable line.
/// A diagnostic without a path is keyed by its message instead.
/// </para>
/// <para>
/// A bounded number of distinct failures is listed. Every occurrence beyond that bound
/// is counted in <see cref="Suppressed"/>, and the summary prints that total, so a
/// truncated list is never mistaken for a complete one.
/// </para>
/// <para>
/// This type holds CPU data only and is used before any window or GPU device exists.
/// </para>
/// </remarks>
internal sealed class SceneryDiagnostics
{
    /// <summary>The number of distinct diagnostics listed by a load.</summary>
    internal const int DefaultMaximum = 40;

    private readonly int _maximum;
    private readonly List<SceneryDiagnosticEntry> _entries = [];
    private readonly Dictionary<DiagnosticIdentity, int> _positions = [];

    /// <summary>
    /// Creates a collector.
    /// </summary>
    /// <param name="maximum">
    /// The largest number of distinct diagnostics to list. The default is the number
    /// the scene summary reports; a parser that must return every diagnostic can pass a
    /// larger value.
    /// </param>
    internal SceneryDiagnostics(int maximum = DefaultMaximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximum);
        _maximum = maximum;
    }

    /// <summary>Gets the listed diagnostics, in first-seen order.</summary>
    internal IReadOnlyList<SceneryDiagnosticEntry> Entries => _entries;

    /// <summary>
    /// Gets the number of occurrences that were not listed because the same
    /// <c>(path, class)</c> was already recorded or the listing limit was reached.
    /// </summary>
    internal int Suppressed { get; private set; }

    /// <summary>Records one classified diagnostic unless an equivalent one is listed.</summary>
    /// <param name="kind">The failure class.</param>
    /// <param name="diagnostic">The diagnostic to record.</param>
    internal void Report(SceneryDiagnosticClass kind, MapDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        Report(
            kind,
            diagnostic.Severity,
            diagnostic.Message,
            diagnostic.FilePath,
            diagnostic.LineNumber);
    }

    /// <summary>Records one classified diagnostic unless an equivalent one is listed.</summary>
    /// <param name="kind">The failure class.</param>
    /// <param name="severity">Whether the message is a warning or an error.</param>
    /// <param name="message">The readable description.</param>
    /// <param name="path">The asset the message refers to, when it has one.</param>
    /// <param name="lineNumber">The one-based line number, when it has one.</param>
    /// <param name="occurrences">How many equivalent failures this record stands for.</param>
    internal void Report(
        SceneryDiagnosticClass kind,
        MapDiagnosticSeverity severity,
        string message,
        string? path = null,
        int? lineNumber = null,
        int occurrences = 1)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentOutOfRangeException.ThrowIfNegative(occurrences);

        if (occurrences == 0)
        {
            return;
        }

        var identity = new DiagnosticIdentity(kind, IdentityOf(path, message));

        if (_positions.TryGetValue(identity, out int position))
        {
            SceneryDiagnosticEntry existing = _entries[position];
            _entries[position] = existing with
            {
                Occurrences = existing.Occurrences + occurrences,
            };

            return;
        }

        if (_entries.Count >= _maximum)
        {
            Suppressed += occurrences;
            return;
        }

        _positions.Add(identity, _entries.Count);
        _entries.Add(new SceneryDiagnosticEntry(
            kind,
            new MapDiagnostic(severity, message, path, lineNumber),
            occurrences));
    }

    /// <summary>
    /// Records every entry of a parsed definition, preserving its class and occurrence
    /// count.
    /// </summary>
    /// <param name="entries">The entries to record.</param>
    internal void ReportEntries(IEnumerable<SceneryDiagnosticEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        foreach (SceneryDiagnosticEntry entry in entries)
        {
            Report(
                entry.Kind,
                entry.Diagnostic,
                entry.Occurrences);
        }
    }

    private void Report(
        SceneryDiagnosticClass kind,
        MapDiagnostic diagnostic,
        int occurrences)
    {
        Report(
            kind,
            diagnostic.Severity,
            diagnostic.Message,
            diagnostic.FilePath,
            diagnostic.LineNumber,
            occurrences);
    }

    /// <summary>
    /// Builds the identity part of a diagnostic: the canonical path when the diagnostic
    /// names an asset, otherwise the message.
    /// </summary>
    private static string IdentityOf(string? path, string message)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "<no path> " + message;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or
            PathTooLongException or System.Security.SecurityException)
        {
            return path;
        }
    }

    private readonly record struct DiagnosticIdentity(
        SceneryDiagnosticClass Kind,
        string Location);
}

/// <summary>
/// Decides which <see cref="SceneryDiagnosticClass"/> a decoder failure belongs to.
/// </summary>
/// <remarks>
/// <para>
/// The engine's <c>ModelFormatException</c> and texture <c>FormatException</c> carry a
/// message and no error code, so a decoder refusal has to be classified from the
/// message the engine itself documents. The engine's O3D and <c>.x</c> parsers phrase a
/// recognized-but-unsupported feature as "is not supported", a scrambled O3D vertex
/// key as "scrambled", and a documented size limit as "exceeds the supported limit";
/// everything else they reject is malformed input.
/// </para>
/// <para>
/// The engine is pinned, so these phrases are stable for this build. If a future
/// engine version adds an error code, this classifier should be replaced by it.
/// </para>
/// </remarks>
internal static class SceneryDecodeFailure
{
    private static readonly string[] UnsupportedPhrases =
    [
        "scrambled",
        "not supported",
        "unsupported",
        "exceeds the supported limit",
        "larger than the supported limit",
    ];

    /// <summary>Tests whether a decoder message reports scrambled O3D vertices.</summary>
    /// <param name="message">The decoder message.</param>
    internal static bool IsScrambledO3d(string message)
    {
        return message.Contains("scrambled", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Classifies a decoder message.</summary>
    /// <param name="message">The decoder message.</param>
    internal static SceneryDiagnosticClass Classify(string message)
    {
        foreach (string phrase in UnsupportedPhrases)
        {
            if (message.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                return SceneryDiagnosticClass.UnsupportedFormat;
            }
        }

        return SceneryDiagnosticClass.ParseError;
    }
}

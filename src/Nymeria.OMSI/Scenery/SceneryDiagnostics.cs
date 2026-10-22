using Nymeria.Omsi.Maps;

namespace Nymeria.Omsi.Scenery;

/// <summary>
/// Collects the diagnostics of one scenery load without repeating an asset failure.
/// </summary>
/// <remarks>
/// <para>
/// OMSI content is often shared: one damaged <c>.sco</c> can be referenced by hundreds
/// of placements, and one missing texture can be named by many subsets. The same asset
/// failure is therefore recorded once, by severity and message, and every later
/// occurrence is only counted in <see cref="Suppressed"/>. A bounded number of distinct
/// messages is listed so a large damaged installation cannot flood the console.
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
    private readonly List<MapDiagnostic> _listed = [];
    private readonly HashSet<DiagnosticIdentity> _identities = [];
    private readonly HashSet<MessageIdentity> _messages = [];

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
    internal IReadOnlyList<MapDiagnostic> Diagnostics => _listed;

    /// <summary>
    /// Gets the number of occurrences that were not listed because the message was
    /// already recorded or the listing limit was reached.
    /// </summary>
    internal int Suppressed { get; private set; }

    /// <summary>
    /// Records one diagnostic unless an equivalent one is already listed.
    /// </summary>
    /// <param name="diagnostic">The diagnostic to record.</param>
    internal void Report(MapDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        Report(
            diagnostic.Severity,
            diagnostic.Message,
            diagnostic.FilePath,
            diagnostic.LineNumber);
    }

    /// <summary>
    /// Records one diagnostic unless an equivalent one is already listed.
    /// </summary>
    /// <param name="severity">Whether the message is a warning or an error.</param>
    /// <param name="message">The readable description.</param>
    /// <param name="path">The file the message refers to, when it has one.</param>
    /// <param name="lineNumber">The one-based line number, when it has one.</param>
    internal void Report(
        MapDiagnosticSeverity severity,
        string message,
        string? path = null,
        int? lineNumber = null)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Asset diagnostics are keyed by severity and message so the same failure is
        // listed once even when it is reached from a different placement or file.
        var identity = new DiagnosticIdentity(severity, message, path, lineNumber);
        var messageIdentity = new MessageIdentity(severity, message);

        if (!_identities.Add(identity))
        {
            Suppressed++;
            return;
        }

        if (!_messages.Add(messageIdentity))
        {
            Suppressed++;
            return;
        }

        if (_listed.Count >= _maximum)
        {
            Suppressed++;
            return;
        }

        _listed.Add(new MapDiagnostic(severity, message, path, lineNumber));
    }

    /// <summary>
    /// Records every diagnostic in a sequence.
    /// </summary>
    /// <param name="diagnostics">The diagnostics to record.</param>
    internal void ReportRange(IEnumerable<MapDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        foreach (MapDiagnostic diagnostic in diagnostics)
        {
            Report(diagnostic);
        }
    }

    /// <summary>
    /// Appends a note that must be listed even when the listing limit is reached.
    /// </summary>
    /// <param name="diagnostic">The note to append.</param>
    internal void AddNote(MapDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        _listed.Add(diagnostic);
    }

    private readonly record struct DiagnosticIdentity(
        MapDiagnosticSeverity Severity,
        string Message,
        string? FilePath,
        int? LineNumber);

    private readonly record struct MessageIdentity(
        MapDiagnosticSeverity Severity,
        string Message);
}

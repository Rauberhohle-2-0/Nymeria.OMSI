namespace Nymeria.Omsi.Maps;

/// <summary>
/// The severity of a map discovery or parsing problem.
/// </summary>
public enum MapDiagnosticSeverity
{
    /// <summary>
    /// The scan completed, but something was ignored or looks suspicious. The
    /// resulting manifest is usable and is reported as partial.
    /// </summary>
    Warning,

    /// <summary>
    /// The scan could not produce a trustworthy manifest. No partial manifest is
    /// presented as complete.
    /// </summary>
    Error,
}

/// <summary>
/// One source-aware message produced while discovering or parsing a map.
/// </summary>
/// <param name="Severity">Whether the message is a warning or an error.</param>
/// <param name="Message">The readable description.</param>
/// <param name="FilePath">The file the message refers to, when it has one.</param>
/// <param name="LineNumber">The one-based line number, when the message has one.</param>
public sealed record MapDiagnostic(
    MapDiagnosticSeverity Severity,
    string Message,
    string? FilePath = null,
    int? LineNumber = null)
{
    /// <summary>
    /// Formats the diagnostic as <c>path:line: warning: message</c>, omitting the
    /// parts that do not apply.
    /// </summary>
    public override string ToString()
    {
        string severity = Severity == MapDiagnosticSeverity.Warning
            ? "warning"
            : "error";

        if (string.IsNullOrEmpty(FilePath))
        {
            return $"{severity}: {Message}";
        }

        return LineNumber is int line
            ? $"{FilePath}:{line}: {severity}: {Message}"
            : $"{FilePath}: {severity}: {Message}";
    }
}

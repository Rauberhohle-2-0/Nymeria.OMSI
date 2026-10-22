namespace Nymeria.Omsi.Maps;

/// <summary>
/// The <c>global.cfg</c> metadata this milestone understands.
/// </summary>
/// <param name="EncodingName">The encoding the file was decoded with.</param>
/// <param name="Name">The <c>[name]</c> value, when present and non-empty.</param>
/// <param name="FriendlyName">The <c>[friendlyname]</c> value, when present and non-empty.</param>
/// <param name="UsesWorldCoordinates">Whether <c>[worldcoordinates]</c> is declared.</param>
/// <param name="UnsupportedDirectives">Blocks that were preserved but not interpreted.</param>
internal sealed record GlobalConfigData(
    string EncodingName,
    string? Name,
    string? FriendlyName,
    bool UsesWorldCoordinates,
    IReadOnlyList<OmsiDirectiveRecord> UnsupportedDirectives);

/// <summary>
/// The outcome of reading a map's <c>global.cfg</c>.
/// </summary>
/// <param name="Data">The supported metadata, or <see langword="null"/> on failure.</param>
/// <param name="Diagnostics">Every warning and error produced while reading.</param>
/// <param name="FatalError">The error that prevented a manifest, when one did.</param>
internal sealed record GlobalConfigParseResult(
    GlobalConfigData? Data,
    IReadOnlyList<MapDiagnostic> Diagnostics,
    MapDiagnostic? FatalError);

/// <summary>
/// Reads the supported subset of a map's <c>global.cfg</c>.
/// </summary>
/// <remarks>
/// <para>
/// Only directives whose meaning is documented are interpreted:
/// </para>
/// <list type="bullet">
///   <item><description><c>[name]</c> and <c>[friendlyname]</c>: one value line each, read verbatim.</description></item>
///   <item><description><c>[worldcoordinates]</c>: presence is the documented signal for the degree-based tile grid; its parameter lines are not interpreted.</description></item>
/// </list>
/// <para>
/// Every other block, including <c>[map]</c> and <c>[entrypoints]</c>, is preserved
/// as an <see cref="OmsiDirectiveRecord"/> and is never reinterpreted. Tile
/// inventory therefore comes from the folder's file names, not from the
/// <c>[map]</c> list, whose parameter grammar is not documented.
/// </para>
/// <para>
/// Duplicate supported directives produce a warning and the first block wins, so
/// the result is deterministic rather than dependent on a last-wins guess.
/// </para>
/// </remarks>
internal static class GlobalConfigParser
{
    private const string NameKeyword = "name";
    private const string FriendlyNameKeyword = "friendlyname";
    private const string WorldCoordinatesKeyword = "worldcoordinates";

    /// <summary>
    /// Reads and parses one <c>global.cfg</c>.
    /// </summary>
    /// <param name="path">The full path of the file.</param>
    internal static GlobalConfigParseResult Parse(string path)
    {
        ConfigReadResult read = ConfigTextReader.Read(path);
        var diagnostics = new List<MapDiagnostic>(read.Diagnostics);

        if (read.Text is null)
        {
            return new GlobalConfigParseResult(
                Data: null,
                diagnostics,
                FatalError: read.FatalError!);
        }

        IReadOnlyList<KeywordBlock> blocks =
            KeywordBlockReader.Read(read.Text, path, diagnostics);

        string? name = null;
        string? friendlyName = null;
        bool usesWorldCoordinates = false;
        var unsupported = new List<OmsiDirectiveRecord>();
        var firstSeenLines = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (KeywordBlock block in blocks)
        {
            if (!IsSupported(block.Keyword))
            {
                unsupported.Add(new OmsiDirectiveRecord(block.Keyword, block.LineNumber));
                continue;
            }

            if (firstSeenLines.TryGetValue(block.Keyword, out int firstLine))
            {
                diagnostics.Add(new MapDiagnostic(
                    MapDiagnosticSeverity.Warning,
                    $"Duplicate [{block.Keyword}] block: the first one at line " +
                    $"{firstLine} is used and this one is ignored.",
                    path,
                    block.LineNumber));
                continue;
            }

            firstSeenLines.Add(block.Keyword, block.LineNumber);

            if (string.Equals(block.Keyword, WorldCoordinatesKeyword, StringComparison.OrdinalIgnoreCase))
            {
                usesWorldCoordinates = true;
                continue;
            }

            string? value = ReadSingleValue(block, path, diagnostics);

            if (string.Equals(block.Keyword, NameKeyword, StringComparison.OrdinalIgnoreCase))
            {
                name = value;
            }
            else
            {
                friendlyName = value;
            }
        }

        return new GlobalConfigParseResult(
            new GlobalConfigData(
                read.Text.EncodingName,
                name,
                friendlyName,
                usesWorldCoordinates,
                unsupported),
            diagnostics,
            FatalError: null);
    }

    private static bool IsSupported(string keyword)
    {
        return string.Equals(keyword, NameKeyword, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(keyword, FriendlyNameKeyword, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(keyword, WorldCoordinatesKeyword, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadSingleValue(
        KeywordBlock block,
        string path,
        List<MapDiagnostic> diagnostics)
    {
        if (block.Parameters.Count == 0)
        {
            diagnostics.Add(new MapDiagnostic(
                MapDiagnosticSeverity.Warning,
                $"[{block.Keyword}] has no value line.",
                path,
                block.LineNumber));
            return null;
        }

        ConfigLine valueLine = block.Parameters[0];

        if (valueLine.Text.Length == 0)
        {
            diagnostics.Add(new MapDiagnostic(
                MapDiagnosticSeverity.Warning,
                $"[{block.Keyword}] has an empty value.",
                path,
                valueLine.Number));
            return null;
        }

        return valueLine.Text;
    }
}

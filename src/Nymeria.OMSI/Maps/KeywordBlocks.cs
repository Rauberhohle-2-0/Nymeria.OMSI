namespace Nymeria.Omsi.Maps;

/// <summary>
/// One <c>[keyword]</c> block: the keyword and the parameter lines that follow it.
/// </summary>
/// <param name="Keyword">The text between the brackets, as spelled in the file.</param>
/// <param name="LineNumber">The one-based line of the keyword.</param>
/// <param name="Parameters">
/// Every physical line after the keyword and before the next keyword, verbatim.
/// An empty line is a legal (empty) parameter.
/// </param>
internal readonly record struct KeywordBlock(
    string Keyword,
    int LineNumber,
    IReadOnlyList<ConfigLine> Parameters);

/// <summary>
/// Splits configuration text into <c>[keyword]</c> blocks.
/// </summary>
/// <remarks>
/// <para>
/// The rules follow the documented OMSI block format: a keyword is a whole line
/// that starts with <c>[</c> at the first column and ends with <c>]</c>, with no
/// surrounding whitespace. Every following line, including an empty one, is a
/// parameter of that block, consumed in order. Free text before the first keyword
/// is ignored.
/// </para>
/// <para>
/// A keyword with leading whitespace is therefore not a keyword: the stock files
/// use indented <c>[keyword]</c> lines as help text and as switched-off blocks.
/// Such lines are treated as parameters and are not reported, because they are
/// legitimate content. Only an unindented, keyword-like line that fails the rule
/// (trailing whitespace, missing bracket, empty keyword) produces a warning, since
/// it is usually a typo that would otherwise shift a parameter.
/// </para>
/// <para>
/// Keyword names are compared case-insensitively by the callers of this reader and
/// are preserved verbatim here, so an unknown spelling can still be reported.
/// </para>
/// </remarks>
internal static class KeywordBlockReader
{
    /// <summary>
    /// Splits decoded text into blocks, collecting warnings about malformed
    /// keyword-like lines.
    /// </summary>
    /// <param name="text">The decoded file.</param>
    /// <param name="path">The file path used in diagnostics.</param>
    /// <param name="diagnostics">The destination for warnings.</param>
    /// <returns>The blocks in file order.</returns>
    internal static IReadOnlyList<KeywordBlock> Read(
        ConfigText text,
        string path,
        List<MapDiagnostic> diagnostics)
    {
        var blocks = new List<KeywordBlock>();
        string? currentKeyword = null;
        int currentLineNumber = 0;
        List<ConfigLine>? currentParameters = null;

        foreach (ConfigLine line in text.Lines)
        {
            if (TryReadKeyword(line.Text, out string keyword))
            {
                if (currentKeyword is not null)
                {
                    blocks.Add(new KeywordBlock(
                        currentKeyword,
                        currentLineNumber,
                        currentParameters!));
                }

                currentKeyword = keyword;
                currentLineNumber = line.Number;
                currentParameters = [];
                continue;
            }

            ReportMalformedKeywordLikeLine(line, path, diagnostics);

            currentParameters?.Add(line);
        }

        if (currentKeyword is not null)
        {
            blocks.Add(new KeywordBlock(
                currentKeyword,
                currentLineNumber,
                currentParameters!));
        }

        return blocks;
    }

    private static bool TryReadKeyword(string line, out string keyword)
    {
        keyword = string.Empty;

        if (line.Length < 2 || line[0] != '[' || line[^1] != ']')
        {
            return false;
        }

        keyword = line[1..^1];
        return keyword.Length > 0;
    }

    private static void ReportMalformedKeywordLikeLine(
        ConfigLine line,
        string path,
        List<MapDiagnostic> diagnostics)
    {
        // Indented keyword lines are documented help text and switched-off blocks.
        if (line.Text.Length == 0 || line.Text[0] != '[')
        {
            return;
        }

        string message;

        if (line.Text.Length == 2)
        {
            message =
                "The line '[]' is not a keyword because the brackets are empty, " +
                "so it is read as a parameter.";
        }
        else if (line.Text.TrimEnd().Length != line.Text.Length)
        {
            message =
                "The keyword-like line has trailing whitespace, so OMSI's block " +
                "format does not treat it as a keyword; it is read as a parameter.";
        }
        else if (line.Text[^1] != ']')
        {
            message =
                "The keyword-like line is not closed with ']', so it is read as " +
                "a parameter.";
        }
        else
        {
            return;
        }

        diagnostics.Add(new MapDiagnostic(
            MapDiagnosticSeverity.Warning,
            message,
            path,
            line.Number));
    }
}

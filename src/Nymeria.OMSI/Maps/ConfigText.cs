using System.Text;

namespace Nymeria.Omsi.Maps;

/// <summary>
/// One physical line of a configuration file, without its line ending.
/// </summary>
/// <param name="Number">The one-based line number.</param>
/// <param name="Text">The line exactly as decoded; no trimming is applied.</param>
internal readonly record struct ConfigLine(int Number, string Text);

/// <summary>
/// A decoded configuration file: its encoding and its physical lines.
/// </summary>
internal sealed class ConfigText
{
    internal ConfigText(string encodingName, IReadOnlyList<ConfigLine> lines)
    {
        EncodingName = encodingName;
        Lines = lines;
    }

    /// <summary>Gets the readable encoding name reported to users.</summary>
    internal string EncodingName { get; }

    /// <summary>Gets the physical lines in file order.</summary>
    internal IReadOnlyList<ConfigLine> Lines { get; }
}

/// <summary>
/// The outcome of reading a configuration file.
/// </summary>
/// <param name="Text">The decoded file, or <see langword="null"/> on failure.</param>
/// <param name="Diagnostics">Warnings that came with a successful decode.</param>
/// <param name="FatalError">Why no text could be produced, when it could not.</param>
internal sealed record ConfigReadResult(
    ConfigText? Text,
    IReadOnlyList<MapDiagnostic> Diagnostics,
    MapDiagnostic? FatalError);

/// <summary>
/// Reads an OMSI configuration file as bounded text.
/// </summary>
/// <remarks>
/// <para>
/// The openOMSI format documentation states that map files (<c>global.cfg</c>,
/// <c>tile_x_y.map</c>, chronological patches) are UTF-16LE with a byte-order
/// mark. That is the primary path. Because mods and editors save files with other
/// tools, this reader falls back explicitly and reports what it did instead of
/// assuming UTF-8:
/// </para>
/// <list type="bullet">
///   <item><description>A UTF-16LE byte-order mark selects UTF-16LE.</description></item>
///   <item><description>A UTF-8 byte-order mark selects UTF-8.</description></item>
///   <item><description>Otherwise strict UTF-8 is attempted without replacement.</description></item>
///   <item><description>Invalid UTF-8 without a mark is decoded as ISO-8859-1 with a warning, so every byte is preserved and nothing is guessed from text content.</description></item>
/// </list>
/// <para>
/// No encoding package is added: <see cref="Encoding.Latin1"/> and
/// <see cref="Encoding.Unicode"/> are part of the base class library. A
/// Windows-1252 file that uses bytes 0x80-0x9F therefore decodes to the
/// corresponding Latin-1 code points; that limitation is documented in
/// docs/OMSI_MAP_PARSING.md.
/// </para>
/// <para>
/// The reader never executes content, never resolves a reference from the file,
/// and enforces a byte and line limit before allocating.
/// </para>
/// </remarks>
internal static class ConfigTextReader
{
    /// <summary>The largest configuration file this reader accepts.</summary>
    internal const int MaxFileBytes = 8 * 1024 * 1024;

    /// <summary>The largest number of physical lines this reader accepts.</summary>
    internal const int MaxLineCount = 200_000;

    /// <summary>
    /// The largest tile file this reader accepts. Tile files hold many placement
    /// records, so they are allowed to be larger than the map configuration.
    /// </summary>
    internal const int MaxTileFileBytes = 64 * 1024 * 1024;

    /// <summary>The largest number of physical lines accepted from a tile file.</summary>
    internal const int MaxTileLineCount = 1_000_000;

    private static readonly UTF8Encoding s_strictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly UnicodeEncoding s_strictUtf16Le = new(
        bigEndian: false,
        byteOrderMark: false,
        throwOnInvalidBytes: true);

    /// <summary>
    /// Reads and decodes one configuration file.
    /// </summary>
    /// <param name="path">The full path of the file.</param>
    /// <param name="maxFileBytes">The byte limit for this file kind.</param>
    /// <param name="maxLineCount">The line limit for this file kind.</param>
    /// <returns>The decoded text, warnings, and a fatal error when reading failed.</returns>
    internal static ConfigReadResult Read(
        string path,
        int maxFileBytes = MaxFileBytes,
        int maxLineCount = MaxLineCount)
    {
        byte[] bytes;

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);

            if (stream.Length > maxFileBytes)
            {
                return Failure(
                    path,
                    $"The file is {stream.Length} bytes, which exceeds the " +
                    $"{maxFileBytes}-byte limit for this file kind.");
            }

            bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Failure(path, $"The file could not be read: {exception.Message}");
        }

        var diagnostics = new List<MapDiagnostic>();
        string text;
        string encodingName;

        try
        {
            if (!TryDecode(path, bytes, diagnostics, out text, out encodingName))
            {
                return new ConfigReadResult(null, diagnostics, diagnostics[^1]);
            }
        }
        catch (DecoderFallbackException exception)
        {
            return Failure(path, $"The file could not be decoded: {exception.Message}");
        }

        if (text.Contains('\0', StringComparison.Ordinal))
        {
            diagnostics.Add(new MapDiagnostic(
                MapDiagnosticSeverity.Warning,
                "The file contains NUL characters. It may be UTF-16LE without a " +
                "byte-order mark, which OMSI does not write, so the text is " +
                "probably not what the author intended.",
                path));
        }

        if (!TrySplitLines(text, maxLineCount, out List<ConfigLine> lines))
        {
            return Failure(
                path,
                $"The file has more than {maxLineCount} lines, which exceeds the " +
                "limit for this file kind.");
        }

        return new ConfigReadResult(
            new ConfigText(encodingName, lines),
            diagnostics,
            FatalError: null);

        static bool TrySplitLines(
            string content,
            int lineLimit,
            out List<ConfigLine> result)
        {
            var parsed = new List<ConfigLine>();
            int number = 1;
            int start = 0;

            for (int index = 0; index < content.Length; index++)
            {
                char character = content[index];

                if (character != '\r' && character != '\n')
                {
                    continue;
                }

                parsed.Add(new ConfigLine(number, content[start..index]));

                if (character == '\r' &&
                    index + 1 < content.Length &&
                    content[index + 1] == '\n')
                {
                    index++;
                }

                start = index + 1;
                number++;

                if (parsed.Count > lineLimit)
                {
                    result = [];
                    return false;
                }
            }

            // A trailing line ending does not create an extra empty line.
            if (start < content.Length)
            {
                parsed.Add(new ConfigLine(number, content[start..]));

                if (parsed.Count > lineLimit)
                {
                    result = [];
                    return false;
                }
            }

            result = parsed;
            return true;
        }
    }

    private static bool TryDecode(
        string path,
        byte[] bytes,
        List<MapDiagnostic> diagnostics,
        out string text,
        out string encodingName)
    {
        text = string.Empty;
        encodingName = string.Empty;

        if (HasPrefix(bytes, 0xFF, 0xFE, 0x00, 0x00))
        {
            diagnostics.Add(new MapDiagnostic(
                MapDiagnosticSeverity.Error,
                "The file starts with a UTF-32LE byte-order mark. OMSI map files " +
                "are UTF-16LE with a byte-order mark, and UTF-32 is not supported.",
                path));
            return false;
        }

        if (HasPrefix(bytes, 0xFF, 0xFE))
        {
            int length = bytes.Length - 2;

            if (length % 2 != 0)
            {
                diagnostics.Add(new MapDiagnostic(
                    MapDiagnosticSeverity.Error,
                    "The file declares UTF-16LE but has an odd number of bytes " +
                    "after the byte-order mark.",
                    path));
                return false;
            }

            text = s_strictUtf16Le.GetString(bytes, 2, length);
            encodingName = "UTF-16LE with byte-order mark";
            return true;
        }

        if (HasPrefix(bytes, 0xEF, 0xBB, 0xBF))
        {
            text = s_strictUtf8.GetString(bytes, 3, bytes.Length - 3);
            encodingName = "UTF-8 with byte-order mark";
            return true;
        }

        try
        {
            text = s_strictUtf8.GetString(bytes);
            encodingName = "UTF-8";
            return true;
        }
        catch (DecoderFallbackException)
        {
            // Explicit, documented fallback: one byte becomes one code point, so
            // no data is lost and no encoding is guessed from text content.
            text = Encoding.Latin1.GetString(bytes);
            encodingName = "ISO-8859-1 (fallback: not valid UTF-8)";
            diagnostics.Add(new MapDiagnostic(
                MapDiagnosticSeverity.Warning,
                "The file is not valid UTF-8 and has no byte-order mark, so it was " +
                "decoded as ISO-8859-1. OMSI writes map files as UTF-16LE with a " +
                "byte-order mark, so another tool probably saved this file.",
                path));
            return true;
        }
    }

    private static bool HasPrefix(byte[] bytes, params byte[] prefix)
    {
        if (bytes.Length < prefix.Length)
        {
            return false;
        }

        for (int index = 0; index < prefix.Length; index++)
        {
            if (bytes[index] != prefix[index])
            {
                return false;
            }
        }

        return true;
    }

    private static ConfigReadResult Failure(string path, string message)
    {
        var diagnostic = new MapDiagnostic(
            MapDiagnosticSeverity.Error,
            message,
            path);

        return new ConfigReadResult(null, [diagnostic], diagnostic);
    }
}

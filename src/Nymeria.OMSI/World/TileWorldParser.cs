using System.Globalization;
using Nymeria.Omsi.Maps;

namespace Nymeria.Omsi.World;

/// <summary>
/// The mutable result of parsing one tile file, converted into the public model
/// by <see cref="OmsiWorldLoader"/>.
/// </summary>
internal sealed class TileParseResult
{
    internal int? Version { get; set; }

    internal bool IsParsed { get; set; }

    internal List<OmsiObjectPlacement> Objects { get; } = [];

    internal List<OmsiSplinePlacement> Splines { get; } = [];

    internal List<OmsiUnsupportedTileRecord> UnsupportedRecords { get; } = [];

    internal List<MapDiagnostic> Diagnostics { get; } = [];
}

/// <summary>
/// Parses the documented placement records of one OMSI tile file.
/// </summary>
/// <remarks>
/// <para>
/// The layout follows the documented tile-record version thresholds:
/// </para>
/// <list type="bullet">
///   <item><description>a leading detail level on objects from version 9 and identifiers from version 6;</description></item>
///   <item><description>object labels from version 4, as a count followed by exactly that many lines;</description></item>
///   <item><description>object pitch and bank from version 12;</description></item>
///   <item><description>a spline with one neighbour reference before version 11 and two from version 11;</description></item>
///   <item><description>a spline's documented coordinate order is x, height, y.</description></item>
/// </list>
/// <para>
/// Records are scanned sequentially rather than split into blocks first, because the
/// documentation states that an object label may itself look like a keyword. A record
/// therefore owns the lines its own layout says it has. Only <c>[object]</c>,
/// <c>[spline]</c>, and <c>[spline_h]</c> are interpreted; every other tile keyword is
/// counted and reported as uninterpreted instead of being guessed at.
/// </para>
/// <para>
/// Values are read with invariant numeric parsing and stored without any coordinate or
/// rotation conversion. A record whose shape disagrees with the layout for the declared
/// version is reported with its file and line and skipped, and the rest of the tile is
/// still parsed. Records are never silently reinterpreted to make a shape fit.
/// </para>
/// <para>
/// The documented trailing spline fields (gradients, cant, skew, texture offset,
/// alignment length, mirror) are not consumed and not interpreted; they are left in
/// the file as ordinary parameter lines and are not reported individually.
/// </para>
/// </remarks>
internal static class TileWorldParser
{
    private const string VersionKeyword = "version";
    private const string ObjectKeyword = "object";
    private const string SplineKeyword = "spline";
    private const string HeightSplineKeyword = "spline_h";

    /// <summary>
    /// Parses one decoded tile file.
    /// </summary>
    /// <param name="text">The decoded tile text.</param>
    /// <param name="path">The full tile path used in diagnostics.</param>
    internal static TileParseResult Parse(ConfigText text, string path)
    {
        ArgumentNullException.ThrowIfNull(text);

        var result = new TileParseResult();
        IReadOnlyList<ConfigLine> lines = text.Lines;

        var unsupportedIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var objectIds = new Dictionary<int, int>();
        int uninterpretedObjects = 0;
        int uninterpretedSplines = 0;

        int index = ReadLeadingVersion(lines, path, result);

        while (index < lines.Count)
        {
            ConfigLine line = lines[index];

            if (!KeywordSyntax.TryReadKeyword(line.Text, out string keyword))
            {
                KeywordSyntax.ReportMalformedKeywordLikeLine(line, path, result.Diagnostics);
                index++;
                continue;
            }

            index++;

            if (string.Equals(keyword, VersionKeyword, StringComparison.OrdinalIgnoreCase))
            {
                // The leading version decides the record layouts. A later [version]
                // block cannot change them, so it is only reported.
                SkipVersionValue(lines, ref index);
                result.Diagnostics.Add(new MapDiagnostic(
                    MapDiagnosticSeverity.Warning,
                    "A [version] block outside the tile's leading block is ignored; " +
                    "record layouts always use the first one.",
                    path,
                    line.Number));
                continue;
            }

            if (result.Version is not int version)
            {
                // The record layouts depend on the tile version, so guessing here
                // would fabricate semantics. Count the records and say so.
                if (IsSplineKeyword(keyword))
                {
                    uninterpretedSplines++;
                }
                else if (string.Equals(keyword, ObjectKeyword, StringComparison.OrdinalIgnoreCase))
                {
                    uninterpretedObjects++;
                }

                AddUnsupported(keyword, line.Number, unsupportedIndex, result.UnsupportedRecords);
                continue;
            }

            if (string.Equals(keyword, ObjectKeyword, StringComparison.OrdinalIgnoreCase))
            {
                TryParseObject(lines, ref index, line, version, path, result, objectIds);
                continue;
            }

            if (IsSplineKeyword(keyword))
            {
                TryParseSpline(lines, ref index, line, version, path, result);
                continue;
            }

            AddUnsupported(keyword, line.Number, unsupportedIndex, result.UnsupportedRecords);
        }

        if (!result.Version.HasValue)
        {
            result.Diagnostics.Add(new MapDiagnostic(
                MapDiagnosticSeverity.Warning,
                "The tile has no usable leading [version], so its record layout is " +
                $"unknown. {uninterpretedObjects} [object] and {uninterpretedSplines} " +
                "[spline] record(s) were counted but not interpreted.",
                path));
        }

        result.IsParsed = result.Version.HasValue;
        return result;
    }

    /// <summary>
    /// Reads the documented leading <c>[version]</c> blocks and returns the index of
    /// the first line that is not part of the leading region.
    /// </summary>
    /// <remarks>
    /// Scanning stops at the first keyword that is not <c>[version]</c>, so free text
    /// or a label further down the file that happens to look like <c>[version]</c>
    /// cannot change the record layout.
    /// </remarks>
    private static int ReadLeadingVersion(
        IReadOnlyList<ConfigLine> lines,
        string path,
        TileParseResult result)
    {
        int index = 0;
        int? firstKeywordLine = null;

        while (index < lines.Count)
        {
            ConfigLine line = lines[index];

            if (!KeywordSyntax.TryReadKeyword(line.Text, out string keyword))
            {
                KeywordSyntax.ReportMalformedKeywordLikeLine(line, path, result.Diagnostics);
                index++;
                continue;
            }

            if (!string.Equals(keyword, VersionKeyword, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }

            index++;

            if (firstKeywordLine is not null)
            {
                result.Diagnostics.Add(new MapDiagnostic(
                    MapDiagnosticSeverity.Warning,
                    $"Duplicate [version] block: the first one at line " +
                    $"{firstKeywordLine} is used and this one is ignored.",
                    path,
                    line.Number));
                SkipVersionValue(lines, ref index);
                continue;
            }

            firstKeywordLine = line.Number;

            if (index >= lines.Count ||
                KeywordSyntax.TryReadKeyword(lines[index].Text, out _))
            {
                result.Diagnostics.Add(new MapDiagnostic(
                    MapDiagnosticSeverity.Warning,
                    "[version] has no value line.",
                    path,
                    line.Number));
                continue;
            }

            ConfigLine valueLine = lines[index];
            index++;

            if (!int.TryParse(
                    valueLine.Text,
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out int version) ||
                version <= 0)
            {
                result.Diagnostics.Add(new MapDiagnostic(
                    MapDiagnosticSeverity.Warning,
                    $"[version] value '{valueLine.Text}' is not a positive integer.",
                    path,
                    valueLine.Number));
                continue;
            }

            result.Version = version;
        }

        return index;
    }

    private static void SkipVersionValue(IReadOnlyList<ConfigLine> lines, ref int index)
    {
        if (index < lines.Count &&
            !KeywordSyntax.TryReadKeyword(lines[index].Text, out _))
        {
            index++;
        }
    }

    private static void TryParseObject(
        IReadOnlyList<ConfigLine> lines,
        ref int index,
        ConfigLine keywordLine,
        int version,
        string path,
        TileParseResult result,
        Dictionary<int, int> objectIds)
    {
        bool hasDetailLevel = version >= 9;
        bool hasIdCode = version >= 6;
        bool hasPitchBank = version >= 12;
        bool hasLabels = version >= 4;

        int pathIndex = hasDetailLevel ? 1 : 0;
        int idIndex = hasIdCode ? pathIndex + 1 : -1;
        int xIndex = pathIndex + (hasIdCode ? 1 : 0) + 1;
        int yIndex = xIndex + 1;
        int zIndex = xIndex + 2;
        int headingIndex = xIndex + 3;
        int pitchIndex = hasPitchBank ? headingIndex + 1 : -1;
        int bankIndex = hasPitchBank ? headingIndex + 2 : -1;
        int labelCountIndex = hasLabels
            ? (hasPitchBank ? bankIndex + 1 : headingIndex + 1)
            : -1;
        int requiredFields = (hasLabels ? labelCountIndex : headingIndex) + 1;

        if (!TryTakeFields(
                lines,
                ref index,
                requiredFields,
                keywordLine,
                ObjectKeyword,
                path,
                result.Diagnostics,
                out List<ConfigLine> fields))
        {
            return;
        }

        if (hasLabels)
        {
            TryTakeLabelLines(lines, ref index, fields, labelCountIndex);
        }

        var block = new KeywordBlock(
            ObjectKeyword,
            keywordLine.Number,
            fields);

        if (block.Parameters.Count < requiredFields)
        {
            return;
        }

        if (!TryReadReference(block, pathIndex, path, result.Diagnostics, out string modelPath) ||
            !TryReadDouble(block, xIndex, "x", path, result.Diagnostics, out double x) ||
            !TryReadDouble(block, yIndex, "y", path, result.Diagnostics, out double y) ||
            !TryReadDouble(block, zIndex, "z", path, result.Diagnostics, out double z) ||
            !TryReadDouble(block, headingIndex, "heading", path, result.Diagnostics, out double heading))
        {
            return;
        }

        int? detailLevel = null;

        if (hasDetailLevel)
        {
            ConfigLine detailLine = block.Parameters[0];

            if (!int.TryParse(
                    detailLine.Text,
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out int detail))
            {
                result.Diagnostics.Add(new MapDiagnostic(
                    MapDiagnosticSeverity.Warning,
                    $"[object] detail level '{detailLine.Text}' is not an integer.",
                    path,
                    detailLine.Number));
                return;
            }

            detailLevel = detail;
        }

        int? idCode = null;

        if (hasIdCode)
        {
            ConfigLine idLine = block.Parameters[idIndex];

            if (!int.TryParse(
                    idLine.Text,
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out int id))
            {
                result.Diagnostics.Add(new MapDiagnostic(
                    MapDiagnosticSeverity.Warning,
                    $"[object] identifier '{idLine.Text}' is not an integer.",
                    path,
                    idLine.Number));
                return;
            }

            idCode = id;
        }

        double? pitch = null;
        double? bank = null;

        if (hasPitchBank)
        {
            if (!TryReadDouble(
                    block,
                    pitchIndex,
                    "pitch",
                    path,
                    result.Diagnostics,
                    out double pitchValue) ||
                !TryReadDouble(
                    block,
                    bankIndex,
                    "bank",
                    path,
                    result.Diagnostics,
                    out double bankValue))
            {
                return;
            }

            pitch = pitchValue;
            bank = bankValue;
        }

        List<string> labels = [];

        if (hasLabels)
        {
            ConfigLine countLine = block.Parameters[labelCountIndex];

            if (!int.TryParse(
                    countLine.Text,
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out int labelCount) ||
                labelCount < 0)
            {
                result.Diagnostics.Add(new MapDiagnostic(
                    MapDiagnosticSeverity.Warning,
                    $"[object] label count '{countLine.Text}' is not a non-negative " +
                    "integer. The record is not interpreted.",
                    path,
                    countLine.Number));
                return;
            }

            int labelStart = labelCountIndex + 1;
            int available = block.Parameters.Count - labelStart;

            if (labelCount != available)
            {
                result.Diagnostics.Add(new MapDiagnostic(
                    MapDiagnosticSeverity.Warning,
                    $"[object] declares {labelCount} label line(s) but {available} " +
                    "follow. The record is not interpreted.",
                    path,
                    countLine.Number));
                return;
            }

            for (int labelIndex = 0; labelIndex < labelCount; labelIndex++)
            {
                labels.Add(block.Parameters[labelStart + labelIndex].Text);
            }
        }

        if (idCode is int parsedId)
        {
            if (objectIds.TryGetValue(parsedId, out int firstLine))
            {
                result.Diagnostics.Add(new MapDiagnostic(
                    MapDiagnosticSeverity.Warning,
                    $"Duplicate object identifier {parsedId}; it was already used at " +
                    $"line {firstLine}. Both records are kept.",
                    path,
                    block.LineNumber));
            }
            else
            {
                objectIds.Add(parsedId, block.LineNumber);
            }
        }

        result.Objects.Add(new OmsiObjectPlacement(
            modelPath,
            idCode,
            detailLevel,
            x,
            y,
            z,
            heading,
            pitch,
            bank,
            labels,
            block.LineNumber));
    }

    private static void TryParseSpline(
        IReadOnlyList<ConfigLine> lines,
        ref int index,
        ConfigLine keywordLine,
        int version,
        string path,
        TileParseResult result)
    {
        string keyword = keywordLine.Text[1..^1];

        // The documented spline record begins with a leading field (documented as
        // 0), then the reference, an optional identifier, neighbour references, and
        // the x, height, y, heading, length, and radius values. Only these fields are
        // read; the trailing gradient/cant/skew/texture-offset/mirror fields are not
        // interpreted.
        int pathIndex = 1;
        int idIndex = version >= 6 ? 2 : -1;
        int previousIndex = version >= 6 ? 3 : 2;
        int nextIndex = version >= 11 ? previousIndex + 1 : -1;
        int xIndex = (nextIndex >= 0 ? nextIndex : previousIndex) + 1;
        int heightIndex = xIndex + 1;
        int yIndex = xIndex + 2;
        int headingIndex = xIndex + 3;
        int lengthIndex = xIndex + 4;
        int radiusIndex = xIndex + 5;
        int requiredFields = radiusIndex + 1;

        if (!TryTakeFields(
                lines,
                ref index,
                requiredFields,
                keywordLine,
                keyword,
                path,
                result.Diagnostics,
                out List<ConfigLine> fields))
        {
            return;
        }

        var block = new KeywordBlock(keyword, keywordLine.Number, fields);

        if (!TryReadReference(block, pathIndex, path, result.Diagnostics, out string modelPath) ||
            !TryReadDouble(block, xIndex, "x", path, result.Diagnostics, out double x) ||
            !TryReadDouble(block, heightIndex, "height", path, result.Diagnostics, out double height) ||
            !TryReadDouble(block, yIndex, "y", path, result.Diagnostics, out double y) ||
            !TryReadDouble(block, headingIndex, "heading", path, result.Diagnostics, out double heading) ||
            !TryReadDouble(block, lengthIndex, "length", path, result.Diagnostics, out double length) ||
            !TryReadDouble(block, radiusIndex, "radius", path, result.Diagnostics, out double radius))
        {
            return;
        }

        int? idCode = null;
        int? previousId = null;
        int? nextId = null;

        if (idIndex >= 0 &&
            !TryReadOptionalIdentifier(block, idIndex, "identifier", path, result.Diagnostics, out idCode))
        {
            return;
        }

        if (!TryReadOptionalIdentifier(
                block,
                previousIndex,
                "previous spline",
                path,
                result.Diagnostics,
                out previousId))
        {
            return;
        }

        if (nextIndex >= 0 &&
            !TryReadOptionalIdentifier(
                block,
                nextIndex,
                "next spline",
                path,
                result.Diagnostics,
                out nextId))
        {
            return;
        }

        result.Splines.Add(new OmsiSplinePlacement(
            modelPath,
            idCode,
            previousId,
            nextId,
            x,
            height,
            y,
            heading,
            length,
            radius,
            string.Equals(keyword, HeightSplineKeyword, StringComparison.OrdinalIgnoreCase),
            block.LineNumber));
    }

    /// <summary>
    /// Takes the fixed parameter lines of one record.
    /// </summary>
    /// <remarks>
    /// Consumption stops at the next keyword or the end of the file, so a truncated
    /// record is reported against its own keyword line instead of consuming the
    /// following record's fields.
    /// </remarks>
    private static bool TryTakeFields(
        IReadOnlyList<ConfigLine> lines,
        ref int index,
        int count,
        ConfigLine keywordLine,
        string keyword,
        string path,
        List<MapDiagnostic> diagnostics,
        out List<ConfigLine> fields)
    {
        fields = new List<ConfigLine>(count);

        while (fields.Count < count)
        {
            if (index >= lines.Count ||
                KeywordSyntax.TryReadKeyword(lines[index].Text, out _))
            {
                diagnostics.Add(new MapDiagnostic(
                    MapDiagnosticSeverity.Warning,
                    $"[{keyword}] at line {keywordLine.Number} has {fields.Count} " +
                    $"parameter line(s) before the next keyword or the end of the " +
                    $"file, but the documented layout needs {count}. The record is " +
                    "not interpreted.",
                    path,
                    keywordLine.Number));
                return false;
            }

            fields.Add(lines[index]);
            index++;
        }

        return true;
    }

    /// <summary>
    /// Takes exactly as many label lines as the already-read label count declares.
    /// </summary>
    /// <remarks>
    /// Label lines are taken even when they look like a keyword, because the
    /// documentation states that any line, "whatever it says", is a label. A count
    /// larger than the remaining lines is capped by the remaining lines so the
    /// caller reports the mismatch instead of looping, and an unreadable count is
    /// reported by the caller's own validation of the same line.
    /// </remarks>
    private static void TryTakeLabelLines(
        IReadOnlyList<ConfigLine> lines,
        ref int index,
        List<ConfigLine> fields,
        int labelCountIndex)
    {
        ConfigLine countLine = fields[labelCountIndex];

        if (!int.TryParse(
                countLine.Text,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out int labelCount) ||
            labelCount < 0)
        {
            return;
        }

        int available = lines.Count - index;
        int take = Math.Min(labelCount, available);

        for (int taken = 0; taken < take; taken++)
        {
            fields.Add(lines[index]);
            index++;
        }
    }

    /// <summary>
    /// Reads a spline neighbour or identifier, where the documented value -1 means
    /// "none" and becomes <see langword="null"/>.
    /// </summary>
    private static bool TryReadOptionalIdentifier(
        KeywordBlock block,
        int index,
        string fieldName,
        string path,
        List<MapDiagnostic> diagnostics,
        out int? value)
    {
        ConfigLine line = block.Parameters[index];

        if (!int.TryParse(
                line.Text,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out int parsed))
        {
            diagnostics.Add(new MapDiagnostic(
                MapDiagnosticSeverity.Warning,
                $"[{block.Keyword}] {fieldName} '{line.Text}' is not an integer. " +
                "The record is not interpreted.",
                path,
                line.Number));
            value = null;
            return false;
        }

        value = parsed == -1 ? null : parsed;
        return true;
    }

    private static bool TryReadReference(
        KeywordBlock block,
        int index,
        string path,
        List<MapDiagnostic> diagnostics,
        out string reference)
    {
        ConfigLine line = block.Parameters[index];
        reference = line.Text;

        if (reference.Length == 0)
        {
            diagnostics.Add(new MapDiagnostic(
                MapDiagnosticSeverity.Warning,
                $"[{block.Keyword}] has an empty file reference. The record is not " +
                "interpreted.",
                path,
                line.Number));
            return false;
        }

        if (double.TryParse(
                reference,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out _))
        {
            // A numeric reference is impossible in the documented layout, so this
            // catches a record whose fields do not start where the version says.
            diagnostics.Add(new MapDiagnostic(
                MapDiagnosticSeverity.Warning,
                $"[{block.Keyword}] expected a file reference but found the number " +
                $"'{reference}'. The record is not interpreted.",
                path,
                line.Number));
            return false;
        }

        return true;
    }

    private static bool TryReadDouble(
        KeywordBlock block,
        int index,
        string fieldName,
        string path,
        List<MapDiagnostic> diagnostics,
        out double value)
    {
        ConfigLine line = block.Parameters[index];

        if (double.TryParse(
                line.Text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value))
        {
            return true;
        }

        diagnostics.Add(new MapDiagnostic(
            MapDiagnosticSeverity.Warning,
            $"[{block.Keyword}] {fieldName} '{line.Text}' is not a valid number. The " +
            "record is not interpreted.",
            path,
            line.Number));
        return false;
    }

    private static bool IsSplineKeyword(string keyword)
    {
        return string.Equals(keyword, SplineKeyword, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(keyword, HeightSplineKeyword, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddUnsupported(
        string keyword,
        int lineNumber,
        Dictionary<string, int> indexByKeyword,
        List<OmsiUnsupportedTileRecord> records)
    {
        if (indexByKeyword.TryGetValue(keyword, out int index))
        {
            OmsiUnsupportedTileRecord existing = records[index];
            records[index] = existing with { Count = existing.Count + 1 };
            return;
        }

        indexByKeyword.Add(keyword, records.Count);
        records.Add(new OmsiUnsupportedTileRecord(keyword, 1, lineNumber));
    }
}

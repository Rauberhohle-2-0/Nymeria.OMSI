namespace Nymeria.Omsi.Maps;

/// <summary>
/// Writes a map manifest as a concise, stable console summary.
/// </summary>
/// <remarks>
/// The summary is the host's inspection output. It names the file a problem came
/// from, prints the scanned tiles in manifest order, and ends with the scan status
/// so a partial result is never mistaken for a complete one.
/// </remarks>
internal static class MapSummaryWriter
{
    /// <summary>
    /// Writes the summary.
    /// </summary>
    /// <param name="writer">The destination, normally standard output.</param>
    /// <param name="manifest">The scan result to describe.</param>
    internal static void Write(TextWriter writer, OmsiMapManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(manifest);

        writer.WriteLine($"map: {manifest.MapRoot}");
        writer.WriteLine($"global.cfg: {manifest.GlobalConfigPath ?? "(not found)"}");
        writer.WriteLine($"text encoding: {manifest.TextEncodingName ?? "(not decoded)"}");
        writer.WriteLine($"name: {manifest.Name ?? "(unavailable)"}");
        writer.WriteLine($"friendly name: {manifest.FriendlyName ?? "(unavailable)"}");
        writer.WriteLine(
            $"world coordinates: {(manifest.UsesWorldCoordinates ? "yes" : "no")}");
        writer.WriteLine($"tiles: {manifest.Tiles.Count}");

        foreach (OmsiMapTile tile in manifest.Tiles)
        {
            writer.WriteLine(
                $"  {tile.Coordinates} {tile.FileName}" +
                (tile.IsReadable ? string.Empty : " (unreadable)"));
        }

        WriteUnsupportedDirectives(writer, manifest);

        if (manifest.Diagnostics.Count == 0)
        {
            writer.WriteLine("diagnostics: none");
        }
        else
        {
            writer.WriteLine($"diagnostics: {manifest.Diagnostics.Count}");

            foreach (MapDiagnostic diagnostic in manifest.Diagnostics)
            {
                writer.WriteLine($"  {diagnostic}");
            }
        }

        writer.WriteLine($"status: {DescribeStatus(manifest.Status)}");
    }

    private static void WriteUnsupportedDirectives(
        TextWriter writer,
        OmsiMapManifest manifest)
    {
        if (manifest.UnsupportedDirectives.Count == 0)
        {
            writer.WriteLine("unsupported directives: none");
            return;
        }

        var distinctKeywords = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (OmsiDirectiveRecord directive in manifest.UnsupportedDirectives)
        {
            if (seen.Add(directive.Keyword))
            {
                distinctKeywords.Add(directive.Keyword);
            }
        }

        writer.WriteLine(
            $"unsupported directives: {manifest.UnsupportedDirectives.Count} " +
            $"({string.Join(", ", distinctKeywords.ConvertAll(k => $"[{k}]"))})");
    }

    private static string DescribeStatus(MapScanStatus status)
    {
        return status switch
        {
            MapScanStatus.Complete => "complete",
            MapScanStatus.Partial => "partial",
            _ => "failed",
        };
    }
}

using System.Globalization;
using Nymeria.Omsi.Maps;

namespace Nymeria.Omsi.World;

/// <summary>
/// Writes a world model as a concise, deterministic console summary.
/// </summary>
/// <remarks>
/// The summary is bounded: at most <see cref="MaxListedTiles"/> tile lines and
/// <see cref="MaxListedDiagnostics"/> diagnostics are printed, followed by a count
/// of the remainder, so a large map cannot flood the console. Totals always cover
/// the whole model.
/// </remarks>
internal static class WorldSummaryWriter
{
    private const int MaxListedTiles = 50;
    private const int MaxListedDiagnostics = 25;

    /// <summary>
    /// Writes the summary.
    /// </summary>
    /// <param name="writer">The destination, normally standard output.</param>
    /// <param name="world">The parsed world to describe.</param>
    internal static void Write(TextWriter writer, OmsiWorldModel world)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(world);

        writer.WriteLine($"map: {world.MapRoot}");
        writer.WriteLine($"name: {world.Name ?? "(unavailable)"}");
        writer.WriteLine($"friendly name: {world.FriendlyName ?? "(unavailable)"}");
        writer.WriteLine($"tiles discovered: {Format(world.Tiles.Count)}");
        writer.WriteLine($"tiles parsed: {Format(world.ParsedTileCount)}");
        writer.WriteLine($"tile versions: {DescribeVersions(world)}");
        writer.WriteLine($"objects: {Format(world.ObjectPlacementCount)} placements");
        writer.WriteLine($"splines: {Format(world.SplinePlacementCount)} placements");

        int terrainFiles = 0;
        int expectedTerrainFiles = 0;

        foreach (OmsiWorldTile tile in world.Tiles)
        {
            if (tile.Terrain is not null)
            {
                terrainFiles++;

                if (tile.Terrain.HasDocumentedSize)
                {
                    expectedTerrainFiles++;
                }
            }
        }

        writer.WriteLine(
            $"terrain files: {Format(terrainFiles)} " +
            $"({Format(expectedTerrainFiles)} at the documented size)");
        writer.WriteLine(
            $"uninterpreted tile record types: {Format(CountUninterpretedTypes(world))}");

        int listedTiles = Math.Min(world.Tiles.Count, MaxListedTiles);

        for (int index = 0; index < listedTiles; index++)
        {
            WriteTileLine(writer, world.Tiles[index]);
        }

        if (world.Tiles.Count > listedTiles)
        {
            writer.WriteLine(
                $"  ... {Format(world.Tiles.Count - listedTiles)} more tile(s) not listed");
        }

        WriteDiagnostics(writer, world.Diagnostics);
        writer.WriteLine($"status: {DescribeStatus(world.Status)}");
    }

    private static void WriteTileLine(TextWriter writer, OmsiWorldTile tile)
    {
        string version = tile.Version is int number
            ? Format(number)
            : "unavailable";

        string terrain = tile.Terrain is null
            ? "none"
            : tile.Terrain.HasDocumentedSize
                ? $"{Format(tile.Terrain.Length)} bytes"
                : $"{Format(tile.Terrain.Length)} bytes, unexpected size";

        writer.WriteLine(
            $"  tile ({Format(tile.X)},{Format(tile.Y)}): " +
            $"{Format(tile.Objects.Count)} object(s), " +
            $"{Format(tile.Splines.Count)} spline(s), " +
            $"version {version}, terrain {terrain}, " +
            $"{Format(tile.Diagnostics.Count)} diagnostic(s)");
    }

    private static void WriteDiagnostics(
        TextWriter writer,
        IReadOnlyList<MapDiagnostic> diagnostics)
    {
        if (diagnostics.Count == 0)
        {
            writer.WriteLine("diagnostics: none");
            return;
        }

        writer.WriteLine($"diagnostics: {Format(diagnostics.Count)}");

        int listed = Math.Min(diagnostics.Count, MaxListedDiagnostics);

        for (int index = 0; index < listed; index++)
        {
            writer.WriteLine($"  {diagnostics[index]}");
        }

        if (diagnostics.Count > listed)
        {
            writer.WriteLine(
                $"  ... {Format(diagnostics.Count - listed)} more diagnostic(s)");
        }
    }

    private static int CountUninterpretedTypes(OmsiWorldModel world)
    {
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (OmsiWorldTile tile in world.Tiles)
        {
            foreach (OmsiUnsupportedTileRecord record in tile.UnsupportedRecords)
            {
                keywords.Add(record.Keyword);
            }
        }

        return keywords.Count;
    }

    private static string DescribeVersions(OmsiWorldModel world)
    {
        var counts = new SortedDictionary<int, int>();

        foreach (OmsiWorldTile tile in world.Tiles)
        {
            if (tile.Version is not int version)
            {
                continue;
            }

            counts.TryGetValue(version, out int count);
            counts[version] = count + 1;
        }

        if (counts.Count == 0)
        {
            return "(unavailable)";
        }

        var parts = new List<string>(counts.Count);

        foreach ((int version, int count) in counts)
        {
            parts.Add($"{Format(version)} ({Format(count)} tiles)");
        }

        return string.Join(", ", parts);
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

    private static string Format(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string Format(long value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}

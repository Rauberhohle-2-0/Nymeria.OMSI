using System.Globalization;
using Nymeria.Omsi.Maps;
using Nymeria.Omsi.Terrain;
using Nymeria.Omsi.World;

namespace Nymeria.Omsi.Rendering;

/// <summary>
/// Writes the console summary printed before the 3D terrain window opens.
/// </summary>
/// <remarks>
/// <para>
/// The summary states what the window shows and what it does not. It never claims
/// that the map was rendered as OMSI renders it: scenery objects, splines, and
/// textures are not part of this view, and the first line says so.
/// </para>
/// <para>
/// Output is deterministic for the same input and bounded: per-tile geometry is
/// reported as totals, and the diagnostics list is capped.
/// </para>
/// </remarks>
internal static class TerrainSummaryWriter
{
    /// <summary>The largest number of diagnostics listed individually.</summary>
    internal const int MaximumListedDiagnostics = 12;

    /// <summary>
    /// Writes the summary.
    /// </summary>
    /// <param name="writer">The destination, normally standard output.</param>
    /// <param name="world">The parsed world the terrain came from.</param>
    /// <param name="terrain">The decoded terrain grids.</param>
    /// <param name="model">The merged geometry that will be drawn.</param>
    internal static void Write(
        TextWriter writer,
        OmsiWorldModel world,
        OmsiTerrainSet terrain,
        OmsiTerrainModel model)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(model);

        writer.WriteLine(
            "terrain: 3D terrain only (OMSI scenery, splines, and ground textures " +
            "are not rendered)");
        writer.WriteLine($"map: {world.MapRoot}");
        writer.WriteLine($"name: {world.Name ?? "(unavailable)"}");

        writer.WriteLine(
            model.UsesDocumentedTileEdge
                ? $"coordinate grid: {Format(model.TileEdgeMeters)} m tiles (documented)"
                : $"coordinate grid: 1/300 degree, {Format(model.TileEdgeMeters)} m " +
                    "tiles (reference measurement, unverified)");

        writer.WriteLine(
            $"terrain tiles: {Format(model.TileCount)} decoded of " +
            $"{Format(world.Tiles.Count)} discovered " +
            $"({Format(terrain.MissingTerrainTiles)} without a sidecar, " +
            $"{Format(terrain.RejectedTerrainTiles)} rejected)");

        writer.WriteLine(
            "height grid: " +
            $"{Format(OmsiTerrainHeightmap.SamplesPerEdge)} x " +
            $"{Format(OmsiTerrainHeightmap.SamplesPerEdge)} samples per tile, " +
            $"{Format(model.TileEdgeMeters / OmsiTerrainHeightmap.CellsPerEdge)} m apart " +
            "(documented layout, orientation unverified)");

        if (model.Bounds is null)
        {
            writer.WriteLine("geometry: none (no tile produced a height grid)");
        }
        else
        {
            TerrainBounds bounds = model.Bounds.Value;

            writer.WriteLine(
                $"heights: {Format(model.MinHeight)} m to {Format(model.MaxHeight)} m");

            writer.WriteLine(
                $"geometry: {Format(model.VertexCount)} vertices, " +
                $"{Format(model.TriangleCount)} triangles, 1 draw call");

            writer.WriteLine(
                $"extent: {Format(bounds.Width)} x {Format(bounds.Depth)} m, " +
                $"centre ({Format(bounds.Center.X)}, {Format(bounds.Center.Z)})");
        }

        writer.WriteLine(
            "shading: fixed-direction diffuse light over a height colour ramp " +
            "(diagnostic, not an OMSI ground texture)");

        WriteDiagnostics(writer, terrain.Diagnostics);

        writer.WriteLine(
            "controls: W/A/S/D move, Space/Ctrl up/down, Shift faster, hold the right " +
            "mouse button and move the mouse to look, R resets the view, close the " +
            "window to exit");
    }

    private static void WriteDiagnostics(
        TextWriter writer,
        IReadOnlyList<MapDiagnostic> diagnostics)
    {
        if (diagnostics.Count == 0)
        {
            writer.WriteLine("terrain diagnostics: none");
            return;
        }

        writer.WriteLine($"terrain diagnostics: {Format(diagnostics.Count)}");

        int listed = Math.Min(diagnostics.Count, MaximumListedDiagnostics);

        for (int index = 0; index < listed; index++)
        {
            writer.WriteLine($"  {diagnostics[index]}");
        }

        if (diagnostics.Count > listed)
        {
            writer.WriteLine(
                $"  ... and {Format(diagnostics.Count - listed)} more");
        }
    }

    private static string Format(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string Format(long value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string Format(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}

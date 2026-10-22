using System.Globalization;
using Nymeria.Omsi.Maps;
using Nymeria.Omsi.Scenery;
using Nymeria.Omsi.Terrain;
using Nymeria.Omsi.World;

namespace Nymeria.Omsi.Rendering;

/// <summary>
/// Writes the console summary printed before the terrain + scenery window opens.
/// </summary>
/// <remarks>
/// <para>
/// Every number here is measured during the load that already happened, not estimated:
/// how many placements were considered, how many became instances, how many assets were
/// decoded, and how many placements were skipped for each distinct reason. The summary
/// therefore doubles as the honest scope statement required of a partial scene.
/// </para>
/// <para>
/// The first line names what is and is not drawn. Roads and splines, vehicles, scripts,
/// animations, night lighting, and shadows are absent, and the summary never implies
/// that the map is rendered as OMSI renders it.
/// </para>
/// </remarks>
internal static class ScenerySummaryWriter
{
    /// <summary>The largest number of diagnostics listed individually.</summary>
    internal const int MaximumListedDiagnostics = 12;

    /// <summary>
    /// Writes the summary.
    /// </summary>
    /// <param name="writer">The destination, normally standard output.</param>
    /// <param name="world">The parsed world the scene came from.</param>
    /// <param name="terrain">The decoded terrain grids, or null when none decoded.</param>
    /// <param name="scene">The loaded scenery.</param>
    /// <param name="contentRoot">The derived OMSI installation root, or null.</param>
    internal static void Write(
        TextWriter writer,
        OmsiWorldModel world,
        OmsiTerrainSet? terrain,
        OmsiSceneryLibrary scene,
        string? contentRoot)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(scene);

        double tileEdge = OmsiTileGrid.EdgeMeters(world.UsesWorldCoordinates);
        SceneryLoadCounts counts = scene.Counts;

        if (counts.InstancesCreated == 0)
        {
            writer.WriteLine(
                "scene: decoded OMSI terrain only; no scenery object could be loaded " +
                "(a bounded, partial scene; roads, splines, vehicles, and scripts are " +
                "not rendered)");
        }
        else
        {
            writer.WriteLine(
                "scene: decoded OMSI terrain with loaded OMSI scenery objects " +
                "(a bounded, partial scene; roads, splines, vehicles, and scripts are " +
                "not rendered)");
        }

        writer.WriteLine($"map: {world.MapRoot}");
        writer.WriteLine("name: " + (world.Name ?? "(unavailable)"));
        writer.WriteLine("content root: " + (contentRoot ?? "(not derived)"));

        writer.WriteLine(
            OmsiTileGrid.UsesDocumentedEdge(world.UsesWorldCoordinates)
                ? $"coordinate grid: {Format(tileEdge)} m tiles (documented)"
                : $"coordinate grid: 1/300 degree, {Format(tileEdge)} m tiles " +
                    "(reference measurement, unverified)");

        writer.WriteLine(
            $"terrain tiles: {(terrain is null ? 0 : terrain.Heightmaps.Count)} decoded " +
            $"of {world.Tiles.Count} discovered");

        writer.WriteLine(
            $"placement budget: {OmsiSceneryLibrary.MaxInstances} instances, " +
            $"{OmsiSceneryLibrary.MaxUniqueModels} definitions, " +
            $"{OmsiSceneryLibrary.MaxUniqueMeshes} meshes, " +
            $"{OmsiSceneryLibrary.MaxUniqueTextures} textures");

        writer.WriteLine(
            $"placements: {counts.PlacementsConsidered} considered, " +
            $"{counts.InstancesCreated} instanced, " +
            $"{counts.InstancesResolved} reference(s) resolved");

        writer.WriteLine(
            $"assets: {counts.ModelsLoaded} definition(s), " +
            $"{counts.MeshesDecoded} mesh file(s), " +
            $"{counts.TexturesDecoded} texture(s) decoded, " +
            $"{counts.TexturesMissing} subset(s) without a usable texture");

        writer.WriteLine(
            $"geometry: {counts.Vertices} vertices, {counts.Subsets} drawable subset(s) " +
            "across the distinct definitions");

        writer.WriteLine(
            $"skipped: {counts.UnresolvedReferences} unresolved reference(s), " +
            $"{counts.DefinitionFailures} definition(s) without drawable geometry, " +
            $"{counts.SkippedWithoutTerrain} without decoded terrain, " +
            $"{counts.TransformFailures} invalid transform(s), " +
            $"{counts.SkippedByLimit} beyond a load limit");

        if (counts.UnsupportedKeywords > 0)
        {
            writer.WriteLine(
                $"unsupported definition keywords: {counts.UnsupportedKeywords} " +
                "occurrence(s) counted; the first few are named below");
        }

        WriteDiagnostics(writer, scene.Diagnostics);

        writer.WriteLine(
            "semantics: placement x/y are tile-local and added to the tile origin; " +
            "z is terrain-relative unless the definition declares [absheight]; heading " +
            "is clockwise, and the pitch/bank order is this build's assumption, " +
            "unverified against OMSI");

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
            writer.WriteLine("scenery diagnostics: none");
            return;
        }

        writer.WriteLine($"scenery diagnostics: {diagnostics.Count}");

        int listed = Math.Min(diagnostics.Count, MaximumListedDiagnostics);

        for (int index = 0; index < listed; index++)
        {
            writer.WriteLine($"  {diagnostics[index]}");
        }

        if (diagnostics.Count > listed)
        {
            writer.WriteLine($"  ... and {diagnostics.Count - listed} more");
        }
    }

    private static string Format(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string Format(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}

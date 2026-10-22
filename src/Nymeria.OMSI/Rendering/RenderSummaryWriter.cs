using System.Globalization;
using Nymeria.Omsi.Maps;
using Nymeria.Omsi.World;

namespace Nymeria.Omsi.Rendering;

/// <summary>
/// Writes the console summary printed before the diagnostic plan window opens.
/// </summary>
/// <remarks>
/// <para>
/// The summary states what is being shown and what is not. It never says the map was
/// "loaded" or "rendered" in the sense of OMSI scenery: every line names parsed
/// records, and the first line says the view is diagnostic.
/// </para>
/// <para>
/// The output is deterministic for the same input and bounded: it reports counts
/// only, never a per-placement list.
/// </para>
/// </remarks>
internal static class RenderSummaryWriter
{
    /// <summary>
    /// Writes the summary.
    /// </summary>
    /// <param name="writer">The destination, normally standard output.</param>
    /// <param name="world">The parsed world the plan came from.</param>
    /// <param name="plan">The plan that will be drawn.</param>
    /// <param name="contentRoot">The derived content root, or null when unresolved.</param>
    /// <param name="contentRootReason">Why the root was or was not derived.</param>
    internal static void Write(
        TextWriter writer,
        OmsiWorldModel world,
        OmsiMapPlan plan,
        string? contentRoot,
        string contentRootReason)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(plan);

        writer.WriteLine(
            "render: diagnostic tile plan (OMSI scenery and terrain are not " +
            "rendered)");
        writer.WriteLine($"map: {world.MapRoot}");
        writer.WriteLine($"name: {world.Name ?? "(unavailable)"}");

        int unparsedTiles = plan.UnparsedTiles;
        int parsedTiles = world.Tiles.Count - unparsedTiles;

        writer.WriteLine(
            $"tiles: {Format(world.Tiles.Count)} discovered " +
            $"({Format(parsedTiles)} parsed, {Format(unparsedTiles)} unparsed)");

        writer.WriteLine(
            plan.HasDocumentedTileEdge
                ? $"coordinate grid: {Format(plan.TileEdgeMeters)} m tiles (documented)"
                : $"coordinate grid: 1/300 degree, {Format(plan.TileEdgeMeters)} m " +
                    "tiles (reference measurement, unverified)");

        writer.WriteLine(
            contentRoot is null
                ? $"content root: (unresolved: {contentRootReason})"
                : $"content root: {contentRoot}");

        writer.WriteLine(
            "placement references: 0 of " +
            $"{Format(world.ObjectPlacementCount + world.SplinePlacementCount)} " +
            "resolved (no asset loader in this milestone)");

        writer.WriteLine(
            $"plan: {Format(plan.Tiles.Count)} tile square(s), " +
            $"{Format(plan.Objects.Count)} object marker(s), " +
            $"{Format(plan.Splines.Count)} spline marker(s)");

        int skipped = plan.SkippedObjects + plan.SkippedSplines;
        int omitted = plan.OmittedObjects + plan.OmittedSplines;

        writer.WriteLine(
            $"plan exclusions: {Format(skipped)} unusable position(s), " +
            $"{Format(omitted)} beyond the per-category marker limit");

        writer.WriteLine(
            $"unread data: {Format(plan.TerrainSidecars)} terrain sidecar(s) not " +
            $"decoded, {Format(plan.UninterpretedRecordTypes)} uninterpreted record " +
            "type(s)");

        int diagnostics = world.Diagnostics.Count;

        writer.WriteLine(
            diagnostics == 0
                ? "parse diagnostics: none"
                : $"parse diagnostics: {Format(diagnostics)} " +
                    "(run with --world for the full list)");

        writer.WriteLine(
            $"image: {Format(DiagnosticMapPresenter.ImageWidth)} x " +
            $"{Format(DiagnosticMapPresenter.ImageHeight)} pixels, scaled to the window");

        writer.WriteLine(
            "controls: W/A/S/D or arrows pan, '+'/'-' zoom, R resets, close the " +
            "window to exit");

        writer.WriteLine($"status: {DescribeStatus(world.Status)}");
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

    private static string Format(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}

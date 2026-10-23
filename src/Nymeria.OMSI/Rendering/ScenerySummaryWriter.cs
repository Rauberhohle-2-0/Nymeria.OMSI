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
/// decoded or skipped, and how many were skipped for each <see
/// cref="SceneryDiagnosticClass"/>. The summary therefore doubles as the honest scope
/// statement required of a partial scene.
/// </para>
/// <para>
/// The first line is a verdict, never a success claim: the scene is incomplete whenever
/// anything was skipped, and a scene where nothing became drawable says so instead of
/// naming what it would have drawn. The second line names what is and is not drawn.
/// Roads and splines, vehicles, scripts, animations, night lighting, and shadows are
/// absent, and the summary never implies that the map is rendered as OMSI renders it.
/// </para>
/// </remarks>
internal static class ScenerySummaryWriter
{
    /// <summary>The largest number of diagnostics listed individually.</summary>
    internal const int MaximumListedDiagnostics = 12;

    /// <summary>The largest number of keywords listed individually.</summary>
    internal const int MaximumListedKeywords = 24;

    /// <summary>
    /// Writes the summary.
    /// </summary>
    /// <param name="writer">The destination, normally standard output.</param>
    /// <param name="world">The parsed world the scene came from.</param>
    /// <param name="terrain">The decoded terrain grids, or null when none decoded.</param>
    /// <param name="contentRoot">The derived OMSI installation root, or null.</param>
    /// <param name="counts">The measured counts of the load.</param>
    /// <param name="diagnostics">The classified, deduplicated diagnostics.</param>
    /// <param name="keywords">
    /// The uninterpreted definition keywords, most frequent first.
    /// </param>
    internal static void Write(
        TextWriter writer,
        OmsiWorldModel world,
        OmsiTerrainSet? terrain,
        string? contentRoot,
        SceneryLoadCounts counts,
        IReadOnlyList<SceneryDiagnosticEntry> diagnostics,
        IReadOnlyList<SceneryKeywordCount> keywords)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(counts);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(keywords);

        double tileEdge = OmsiTileGrid.EdgeMeters(world.UsesWorldCoordinates);
        SceneryPlacementCounts placements = counts.Placements;

        WriteVerdict(writer, placements);
        WriteSceneContent(writer, placements);

        writer.WriteLine($"map: {world.MapRoot}");
        writer.WriteLine("name: " + (world.Name ?? "(unavailable)"));
        writer.WriteLine("content root: " + (contentRoot ?? "(not derived)"));

        writer.WriteLine(
            "asset roots: " +
            (contentRoot is null
                ? "none — no installation root was derived"
                : SceneryDefinitionParser.DescribeSearchRoots(contentRoot)));

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
            $"placements: {Format(placements.Considered)} considered, " +
            $"{Format(placements.Instanced)} instanced, " +
            $"{Format(placements.Resolved)} with a loaded definition");

        writer.WriteLine(
            $"definitions: {Format(counts.Cache.Definitions)} loaded, " +
            $"{Format(counts.Definitions.Loaded)} with drawable geometry, " +
            $"{Format(counts.Definitions.WithoutGeometry)} without");

        writer.WriteLine(
            $"meshes: {Format(counts.Meshes.Decoded)} file(s) decoded, " +
            $"{Format(counts.Meshes.Failures.Total)} skipped " +
            $"({counts.Meshes.Failures.Describe()})");

        writer.WriteLine(
            $"textures: {Format(counts.Textures.Decoded)} decoded, " +
            $"{Format(counts.Textures.Failures.Total)} skipped " +
            $"({counts.Textures.Failures.Describe()})");

        writer.WriteLine(
            $"subsets: {Format(counts.Subsets.Drawable)} drawable, " +
            $"{Format(counts.Subsets.WithoutTexture)} drawn with their base color " +
            $"({counts.Subsets.Failures.Describe()})");

        writer.WriteLine(
            $"placement skips: {placements.Failures.Describe()}");

        writer.WriteLine(
            $"geometry: {Format(counts.Vertices)} vertices across the distinct " +
            "definitions");

        writer.WriteLine(
            $"skipped: {Format(placements.WithoutTerrain)} without decoded terrain, " +
            $"{Format(placements.InvalidTransform)} invalid transform(s)");

        WriteKeywords(writer, counts, keywords);

        writer.WriteLine(
            $"cache: {Format(counts.Cache.Definitions)} definition(s), " +
            $"{Format(counts.Cache.Meshes)} mesh file(s), " +
            $"{Format(counts.Cache.Textures)} texture(s) keyed by resolved path, each " +
            "with a (size, last-write time) stamp; " +
            $"{Format(counts.Cache.ReplacedMidRun)} re-decoded mid-run");

        WriteDiagnostics(writer, counts, diagnostics);

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

    private static void WriteVerdict(TextWriter writer, SceneryPlacementCounts placements)
    {
        if (placements.Instanced == 0)
        {
            writer.WriteLine(
                "scene: incomplete — no placement produced drawable geometry, so the " +
                "view would be terrain only; the classification below says why");

            return;
        }

        int skipped = placements.Considered - placements.Instanced;

        // The verdict never says "complete": the scene is a bounded subset and roads,
        // splines, vehicles, and scripts are absent even when nothing was skipped.
        writer.WriteLine(
            skipped > 0
                ? $"scene: incomplete — {Format(placements.Instanced)} of " +
                    $"{Format(placements.Considered)} placement(s) instanced, " +
                    $"{Format(skipped)} skipped; the classification is below"
                : $"scene: incomplete — {Format(placements.Instanced)} of " +
                    $"{Format(placements.Considered)} placement(s) instanced; roads, " +
                    "splines, vehicles, and scripts are not rendered");
    }

    private static void WriteSceneContent(
        TextWriter writer,
        SceneryPlacementCounts placements)
    {
        writer.WriteLine(
            placements.Instanced == 0
                ? "scene content: decoded OMSI terrain only; no scenery object produced " +
                    "drawable geometry (a bounded, partial scene; roads, splines, " +
                    "vehicles, and scripts are not rendered)"
                : "scene content: decoded OMSI terrain with loaded OMSI scenery objects " +
                    "(a bounded, partial scene; roads, splines, vehicles, and scripts " +
                    "are not rendered)");
    }

    private static void WriteKeywords(
        TextWriter writer,
        SceneryLoadCounts counts,
        IReadOnlyList<SceneryKeywordCount> keywords)
    {
        if (counts.UnsupportedKeywords == 0 || keywords.Count == 0)
        {
            writer.WriteLine("unsupported definition keywords: none");
            return;
        }

        writer.WriteLine(
            $"unsupported definition keywords (out of scope by design): " +
            $"{Format(counts.UnsupportedKeywords)} occurrence(s) across " +
            $"{Format(keywords.Count)} keyword(s)");

        int listed = Math.Min(keywords.Count, MaximumListedKeywords);

        for (int index = 0; index < listed; index++)
        {
            SceneryKeywordCount keyword = keywords[index];

            writer.WriteLine(
                $"  [{keyword.Keyword}] {Format(keyword.Occurrences)}");
        }

        if (keywords.Count > listed)
        {
            writer.WriteLine(
                $"  ... and {Format(keywords.Count - listed)} more keyword(s)");
        }
    }

    private static void WriteDiagnostics(
        TextWriter writer,
        SceneryLoadCounts counts,
        IReadOnlyList<SceneryDiagnosticEntry> diagnostics)
    {
        if (diagnostics.Count == 0)
        {
            writer.WriteLine("scenery diagnostics: none");
        }
        else
        {
            writer.WriteLine(
                $"scenery diagnostics: {Format(diagnostics.Count)} distinct");

            int listed = Math.Min(diagnostics.Count, MaximumListedDiagnostics);

            for (int index = 0; index < listed; index++)
            {
                SceneryDiagnosticEntry entry = diagnostics[index];
                string occurrences = entry.Occurrences > 1
                    ? string.Create(
                        CultureInfo.InvariantCulture,
                        $" (x{entry.Occurrences})")
                    : string.Empty;

                writer.WriteLine(
                    $"  [{SceneryDiagnosticClasses.Describe(entry.Kind)}] " +
                    $"{entry.Diagnostic}{occurrences}");
            }

            if (diagnostics.Count > listed)
            {
                writer.WriteLine(
                    $"  ... and {Format(diagnostics.Count - listed)} more distinct");
            }
        }

        if (counts.SuppressedProblems > 0)
        {
            writer.WriteLine(
                $"  {Format(counts.SuppressedProblems)} further occurrence(s) of already " +
                "listed failures were counted but not listed");
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

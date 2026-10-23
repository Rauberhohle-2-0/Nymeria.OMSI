using System.Globalization;
using System.Numerics;
using Nymeria.Core.Materials;
using Nymeria.Core.Models;
using Nymeria.Formats.DirectX;
using Nymeria.Formats.O3d;
using Nymeria.Formats.Textures;
using Nymeria.Omsi.Maps;
using Nymeria.Omsi.Terrain;
using Nymeria.Omsi.World;

namespace Nymeria.Omsi.Scenery;

/// <summary>
/// Loads a bounded, deterministic subset of a map's scenery placements.
/// </summary>
/// <remarks>
/// <para>
/// This type is the CPU half of the scene view: it resolves <c>.sco</c> definitions
/// under the derived OMSI installation root, parses them, decodes meshes through the
/// engine's own <c>.o3d</c> and <c>.x</c> parsers, decodes textures through the
/// engine's texture loader, and converts each placement into a world matrix. It never
/// creates a window, a GPU device, or a GPU resource, so it runs before the host opens
/// anything and is testable headlessly.
/// </para>
/// <para>
/// Everything is cached by resolved path, including a failed definition. Twenty
/// instances of one building share one parsed definition, one decoded mesh, one decoded
/// texture, and later one set of GPU buffers, and a damaged definition is parsed once
/// instead of once per placement. Nothing is decoded per frame. A cached entry also
/// carries a <see cref="SceneryAssetStamp"/> so a file replaced during the load is
/// decoded again instead of being drawn from stale data.
/// </para>
/// <para>
/// The scene is a documented partial scene, not a claim about OMSI compatibility.
/// Placements are taken in tile and file order up to <see cref="MaxInstances"/>, and
/// each one that is not drawn is counted under a <see cref="SceneryDiagnosticClass"/>,
/// so the summary can say how many placements, meshes, textures, and subsets failed for
/// each reason instead of reporting a single unexplained total.
/// </para>
/// </remarks>
internal sealed class OmsiSceneryLibrary
{
    /// <summary>
    /// The largest number of placements drawn in the scene view. The limit keeps
    /// startup time and GPU memory bounded on a large map and makes the selected
    /// subset deterministic.
    /// </summary>
    internal const int MaxInstances = 2_000;

    /// <summary>The largest number of distinct scenery definitions loaded.</summary>
    internal const int MaxUniqueModels = 256;

    /// <summary>The largest number of distinct mesh files decoded.</summary>
    internal const int MaxUniqueMeshes = 512;

    /// <summary>The largest number of distinct texture files decoded.</summary>
    internal const int MaxUniqueTextures = 512;

    /// <summary>The largest number of decoded vertices across all mesh files.</summary>
    internal const int MaxVertices = 4_000_000;

    /// <summary>
    /// The largest number of distinct problem diagnostics recorded. Repeated failures of
    /// the same asset for the same reason are only counted.
    /// </summary>
    internal const int MaxReportedProblems = 40;

    private OmsiSceneryLibrary(
        IReadOnlyList<SceneryInstance> instances,
        IReadOnlyList<SceneryModel> models,
        IReadOnlyList<SceneryDiagnosticEntry> diagnostics,
        IReadOnlyList<SceneryKeywordCount> unsupportedKeywords,
        SceneryLoadCounts counts)
    {
        Instances = instances;
        Models = models;
        Diagnostics = diagnostics;
        UnsupportedKeywords = unsupportedKeywords;
        Counts = counts;
    }

    /// <summary>Gets the placements to draw, in tile and file order.</summary>
    internal IReadOnlyList<SceneryInstance> Instances { get; }

    /// <summary>Gets the distinct loaded models.</summary>
    internal IReadOnlyList<SceneryModel> Models { get; }

    /// <summary>
    /// Gets the classified load diagnostics, deduplicated by asset path and class.
    /// </summary>
    internal IReadOnlyList<SceneryDiagnosticEntry> Diagnostics { get; }

    /// <summary>
    /// Gets the uninterpreted definition keywords, most frequent first, aggregated over
    /// every definition that parsed.
    /// </summary>
    internal IReadOnlyList<SceneryKeywordCount> UnsupportedKeywords { get; }

    /// <summary>Gets the measured loading counts.</summary>
    internal SceneryLoadCounts Counts { get; }

    /// <summary>
    /// Loads the scenery of a parsed world.
    /// </summary>
    /// <param name="world">The parsed world model.</param>
    /// <param name="terrain">
    /// The decoded terrain used for terrain-relative heights, or <see langword="null"/>
    /// when no terrain was decoded.
    /// </param>
    /// <param name="contentRoot">
    /// The OMSI installation root every reference is resolved under, or
    /// <see langword="null"/> when it could not be derived.
    /// </param>
    /// <returns>
    /// The loaded library. A map with no loadable scenery is not an error: the counts
    /// and diagnostics say exactly what was found and why.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> is null.</exception>
    internal static OmsiSceneryLibrary Load(
        OmsiWorldModel world,
        TerrainHeightSampler? terrain,
        string? contentRoot)
    {
        ArgumentNullException.ThrowIfNull(world);

        var diagnostics = new SceneryDiagnostics(MaxReportedProblems);
        var instances = new List<SceneryInstance>();
        var placementFailures = new SceneryClassTally();
        var keywords = new KeywordTotals();

        // A null value is a cached parse failure: the same damaged .sco is parsed once,
        // no matter how many placements reference it. The failure class travels with it
        // so every skipped placement can still be counted under its reason.
        var models = new Dictionary<string, SceneryModel?>(StringComparer.OrdinalIgnoreCase);
        var failureClasses =
            new Dictionary<string, SceneryDiagnosticClass>(StringComparer.OrdinalIgnoreCase);
        var loadedModels = new List<SceneryModel>();
        var cache = new LoadCache(diagnostics);

        int considered = 0;
        int resolvedDefinitions = 0;
        int missingTerrain = 0;
        int transformFailures = 0;
        int skippedByLimit = 0;
        int skippedByModelLimit = 0;
        long vertices = 0;

        if (contentRoot is null)
        {
            diagnostics.Report(
                SceneryDiagnosticClass.MissingAsset,
                MapDiagnosticSeverity.Warning,
                "The OMSI installation root could not be derived from the map folder, " +
                "so no scenery reference can be resolved and no object is loaded. The " +
                "map folder must be inside a folder named 'maps'.");

            return new OmsiSceneryLibrary(
                [],
                [],
                diagnostics.Entries,
                [],
                new SceneryLoadCounts(
                    new SceneryPlacementCounts(0, 0, 0, placementFailures, 0, 0),
                    new SceneryDefinitionCounts(0, 0),
                    new SceneryAssetCounts(0, new SceneryClassTally()),
                    new SceneryAssetCounts(0, new SceneryClassTally()),
                    new ScenerySubsetCounts(0, 0, new SceneryClassTally()),
                    0,
                    0,
                    0,
                    new SceneryCacheCounts(0, 0, 0, 0)));
        }

        if (terrain is null)
        {
            diagnostics.Report(
                SceneryDiagnosticClass.MissingAsset,
                MapDiagnosticSeverity.Warning,
                "No terrain was decoded, so every object whose height is " +
                "terrain-relative is skipped instead of being placed at an invented " +
                "height. Objects that declare [absheight] are still placed.");
        }

        bool stopped = false;

        foreach (OmsiWorldTile tile in world.Tiles)
        {
            if (stopped)
            {
                break;
            }

            foreach (OmsiObjectPlacement placement in tile.Objects)
            {
                if (instances.Count >= MaxInstances)
                {
                    // The placement is inspected and classified, then declined for
                    // budget reasons, so it stays in "considered" and the verdict's
                    // skip total always equals the classified total.
                    considered++;
                    skippedByLimit++;
                    placementFailures.Add(SceneryDiagnosticClass.NotAttempted);

                    if (skippedByLimit == 1)
                    {
                        diagnostics.Report(
                            SceneryDiagnosticClass.NotAttempted,
                            MapDiagnosticSeverity.Warning,
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "The scene view draws at most {0} placements. Later " +
                                "placements are not loaded, so this is a deliberately " +
                                "partial scene.",
                                MaxInstances));
                    }

                    continue;
                }

                if (stopped)
                {
                    break;
                }

                considered++;

                SceneryPathResult reference = SceneryPath.Resolve(
                    contentRoot,
                    placement.ModelPath);

                if (!reference.IsResolved)
                {
                    SceneryDiagnosticClass kind =
                        reference.Status == SceneryPathStatus.Rejected
                            ? SceneryDiagnosticClass.ParseError
                            : SceneryDiagnosticClass.MissingAsset;

                    placementFailures.Add(kind);
                    diagnostics.Report(
                        kind,
                        MapDiagnosticSeverity.Warning,
                        "The scenery reference '" + placement.ModelPath +
                        "' could not be resolved: " + (reference.Error ?? "unknown reason"),
                        tile.FilePath,
                        placement.LineNumber);
                    continue;
                }

                string scoPath = reference.Path!;

                if (!models.TryGetValue(scoPath, out SceneryModel? model))
                {
                    if (models.Count >= MaxUniqueModels)
                    {
                        skippedByModelLimit++;
                        placementFailures.Add(SceneryDiagnosticClass.NotAttempted);
                        stopped = true;
                        continue;
                    }

                    model = LoadModel(
                        contentRoot,
                        scoPath,
                        placement.ModelPath,
                        cache,
                        keywords,
                        ref vertices,
                        out SceneryDiagnosticClass failureClass);

                    models.Add(scoPath, model);
                    failureClasses.Add(scoPath, failureClass);

                    if (model is not null)
                    {
                        loadedModels.Add(model);
                    }
                }

                // A cached failure is still a failure for this placement: the count
                // says how many placements were not drawn, while the parse happened
                // once and its diagnostics were listed once.
                if (model is null)
                {
                    placementFailures.Add(failureClasses[scoPath]);
                    continue;
                }

                resolvedDefinitions++;

                float? terrainHeight = null;

                if (!model.UsesAbsoluteHeight)
                {
                    double absoluteX = (tile.X * OmsiTileGrid.EdgeMeters(world.UsesWorldCoordinates))
                        + placement.X;
                    double absoluteY = (tile.Y * OmsiTileGrid.EdgeMeters(world.UsesWorldCoordinates))
                        + placement.Y;

                    if (terrain is not null &&
                        terrain.TrySample(absoluteX, absoluteY, out float sampled))
                    {
                        terrainHeight = sampled;
                    }
                }

                if (!SceneryTransform.TryCreate(
                        placement,
                        tile.X,
                        tile.Y,
                        OmsiTileGrid.EdgeMeters(world.UsesWorldCoordinates),
                        model.UsesAbsoluteHeight,
                        terrainHeight,
                        out Matrix4x4 transform,
                        out string? transformError))
                {
                    // A missing terrain sample and an unusable transform are placement
                    // geometry problems, not asset problems: they are counted apart from
                    // the diagnostic classes so neither is mislabelled as a bad file.
                    if (!model.UsesAbsoluteHeight && terrainHeight is null)
                    {
                        missingTerrain++;
                        placementFailures.Add(SceneryDiagnosticClass.MissingAsset);

                        diagnostics.Report(
                            SceneryDiagnosticClass.MissingAsset,
                            MapDiagnosticSeverity.Warning,
                            "The placement of '" + placement.ModelPath +
                            "' is skipped: " +
                            (transformError ?? "the transform could not be computed."),
                            tile.FilePath,
                            placement.LineNumber);
                    }
                    else
                    {
                        transformFailures++;
                        placementFailures.Add(SceneryDiagnosticClass.ParseError);

                        diagnostics.Report(
                            SceneryDiagnosticClass.ParseError,
                            MapDiagnosticSeverity.Warning,
                            "The placement of '" + placement.ModelPath +
                            "' has an unusable transform: " +
                            (transformError ?? "the transform could not be computed."),
                            tile.FilePath,
                            placement.LineNumber);
                    }

                    continue;
                }

                instances.Add(new SceneryInstance(
                    model,
                    transform,
                    tile.X,
                    tile.Y,
                    placement.LineNumber));
            }
        }

        if (skippedByModelLimit > 0)
        {
            diagnostics.Report(
                SceneryDiagnosticClass.NotAttempted,
                MapDiagnosticSeverity.Warning,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The scene view loads at most {0} distinct scenery definitions. " +
                    "{1} further placement(s) were not attempted.",
                    MaxUniqueModels,
                    skippedByModelLimit));
        }

        if (skippedByLimit > 0)
        {
            diagnostics.Report(
                SceneryDiagnosticClass.NotAttempted,
                MapDiagnosticSeverity.Warning,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} placement(s) beyond the {1}-instance scene limit were not " +
                    "loaded.",
                    skippedByLimit,
                    MaxInstances));
        }

        if (keywords.Total > 0)
        {
            diagnostics.Report(
                SceneryDiagnosticClass.OutOfScopeByDesign,
                MapDiagnosticSeverity.Warning,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} occurrence(s) of {1} definition keyword(s) were not " +
                    "interpreted. Scripts, animation, interaction, paths, sounds, " +
                    "traffic lights, and collision are out of scope for this build; the " +
                    "per-keyword census is printed above.",
                    keywords.Total,
                    keywords.Distinct));
        }

        var counts = new SceneryLoadCounts(
            new SceneryPlacementCounts(
                considered,
                instances.Count,
                resolvedDefinitions,
                placementFailures,
                missingTerrain,
                transformFailures),
            new SceneryDefinitionCounts(loadedModels.Count, models.Count - loadedModels.Count),
            new SceneryAssetCounts(cache.MeshesDecoded, cache.MeshFailures),
            new SceneryAssetCounts(cache.TexturesDecoded, cache.TextureFailures),
            new ScenerySubsetCounts(
                loadedModels.Sum(model => model.Subsets.Count),
                cache.SubsetsWithoutTexture,
                cache.MaterialFailures),
            vertices,
            keywords.Total,
            diagnostics.Suppressed,
            new SceneryCacheCounts(
                models.Count,
                cache.MeshEntries,
                cache.TextureEntries,
                cache.Replacements));

        return new OmsiSceneryLibrary(
            instances,
            loadedModels,
            diagnostics.Entries,
            keywords.Results(),
            counts);
    }

    /// <summary>
    /// Parses one definition and decodes its meshes and textures.
    /// </summary>
    /// <param name="contentRoot">The installation root that bounds every resolution.</param>
    /// <param name="scoPath">The resolved definition file.</param>
    /// <param name="reference">The reference as written in the tile file.</param>
    /// <param name="cache">The per-load asset cache.</param>
    /// <param name="keywords">The keyword census to add to.</param>
    /// <param name="vertices">The decoded vertex total to add to.</param>
    /// <param name="failureClass">
    /// Why nothing can be drawn, when the definition produces no geometry.
    /// </param>
    /// <returns>The loaded model, or <see langword="null"/> when nothing can be drawn.</returns>
    private static SceneryModel? LoadModel(
        string contentRoot,
        string scoPath,
        string reference,
        LoadCache cache,
        KeywordTotals keywords,
        ref long vertices,
        out SceneryDiagnosticClass failureClass)
    {
        failureClass = SceneryDiagnosticClass.UnsupportedFormat;

        SceneryParseResult parsed = SceneryDefinitionParser.Parse(
            scoPath,
            reference,
            contentRoot);

        cache.Diagnostics.ReportEntries(parsed.Diagnostics);

        if (parsed.Definition is not SceneryDefinition definition)
        {
            failureClass = MostSevere(parsed.Diagnostics);
            return null;
        }

        keywords.Add(definition.UnsupportedKeywords, definition.UnsupportedKeywordCount);

        var subsets = new List<ScenerySubset>();
        var unmappedLabels = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        BoundingBox? bounds = null;
        SceneryDiagnosticClass? meshFailure = null;

        foreach (SceneryMeshReference meshReference in definition.Meshes)
        {
            if (!meshReference.IsResolved)
            {
                // The definition's own failure class names the first mesh that could
                // not even be found, so a missing file is not reported as an
                // unsupported format.
                meshFailure ??= meshReference.Failure;
                continue;
            }

            IReadOnlyList<SceneryMesh>? meshes = cache.GetMeshes(
                meshReference.Path!,
                ref vertices,
                out SceneryDiagnosticClass? meshError);

            if (meshes is null)
            {
                continue;
            }

            if (meshes.Count == 0)
            {
                meshFailure ??= meshError;
                continue;
            }

            foreach (SceneryMesh mesh in meshes)
            {
                bounds = bounds is BoundingBox existing
                    ? new BoundingBox(
                        Vector3.Min(existing.Min, mesh.Data.Bounds.Min),
                        Vector3.Max(existing.Max, mesh.Data.Bounds.Max))
                    : mesh.Data.Bounds;

                foreach (MeshSubset subset in mesh.Data.Subsets)
                {
                    if (subset.IndexCount <= 0)
                    {
                        continue;
                    }

                    SceneryMaterialResolution material = cache.GetMaterial(
                        definition,
                        contentRoot,
                        subset.MaterialName);

                    switch (material.Kind)
                    {
                        case SceneryMaterialKind.Untextured:
                        case SceneryMaterialKind.Decoded:
                        case SceneryMaterialKind.NamedFile:
                            break;

                        case SceneryMaterialKind.Unmapped:
                            cache.SubsetsWithoutTexture++;
                            cache.MaterialFailures.Add(
                                SceneryDiagnosticClass.MissingTextureMapping);

                            if (material.Reference is string label)
                            {
                                unmappedLabels.Add(label);
                            }

                            break;

                        default:
                            cache.SubsetsWithoutTexture++;
                            cache.MaterialFailures.Add(
                                SceneryMaterialKindToClass(material.Kind));
                            break;
                    }

                    subsets.Add(new ScenerySubset(
                        mesh,
                        subset.IndexOffset,
                        subset.IndexCount,
                        material.Material,
                        subset.MaterialName));
                }
            }
        }

        if (subsets.Count == 0 || bounds is null)
        {
            failureClass = meshFailure ?? SceneryDiagnosticClass.UnsupportedFormat;

            cache.Diagnostics.Report(
                failureClass,
                MapDiagnosticSeverity.Warning,
                "No mesh of this definition could be decoded, so the object is not " +
                "drawn.",
                scoPath);

            return null;
        }

        if (unmappedLabels.Count > 0)
        {
            string labels = string.Join("', '", unmappedLabels);

            cache.Diagnostics.Report(
                SceneryDiagnosticClass.MissingTextureMapping,
                MapDiagnosticSeverity.Warning,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} mesh material label(s) have no texture mapping: '{1}'. No " +
                    "[matl] entry names them and no texture file of that name was found " +
                    "in the permitted asset folders, so those subsets are drawn with " +
                    "their base color.",
                    unmappedLabels.Count,
                    labels),
                scoPath);
        }

        return new SceneryModel(
            reference,
            scoPath,
            subsets,
            bounds.Value,
            definition.UsesAbsoluteHeight);
    }

    /// <summary>Maps a material outcome to the class its subset is counted under.</summary>
    private static SceneryDiagnosticClass SceneryMaterialKindToClass(SceneryMaterialKind kind)
    {
        return kind switch
        {
            SceneryMaterialKind.MissingFile => SceneryDiagnosticClass.MissingAsset,
            SceneryMaterialKind.UnsupportedFormat => SceneryDiagnosticClass.UnsupportedFormat,
            SceneryMaterialKind.ParseError => SceneryDiagnosticClass.ParseError,
            _ => SceneryDiagnosticClass.MissingTextureMapping,
        };
    }

    /// <summary>
    /// Returns the class of the most important diagnostic of a failed parse: the first
    /// error, or the first warning when the parse only produced warnings.
    /// </summary>
    private static SceneryDiagnosticClass MostSevere(
        IReadOnlyList<SceneryDiagnosticEntry> diagnostics)
    {
        foreach (SceneryDiagnosticEntry entry in diagnostics)
        {
            if (entry.Diagnostic.Severity == MapDiagnosticSeverity.Error)
            {
                return entry.Kind;
            }
        }

        return diagnostics.Count > 0
            ? diagnostics[0].Kind
            : SceneryDiagnosticClass.ParseError;
    }

    /// <summary>
    /// Holds the per-load asset caches, the stamp checks, and the counting that goes
    /// with them.
    /// </summary>
    private sealed class LoadCache
    {
        private readonly Dictionary<string, MeshEntry> _meshes =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TextureEntry> _textures =
            new(StringComparer.OrdinalIgnoreCase);

        internal LoadCache(SceneryDiagnostics diagnostics)
        {
            Diagnostics = diagnostics;
        }

        internal SceneryDiagnostics Diagnostics { get; }

        internal int MeshesDecoded { get; private set; }

        internal SceneryClassTally MeshFailures { get; } = new();

        internal int TexturesDecoded { get; private set; }

        internal SceneryClassTally TextureFailures { get; } = new();

        internal SceneryClassTally MaterialFailures { get; } = new();

        internal int SubsetsWithoutTexture { get; set; }

        internal int Replacements { get; private set; }

        internal int MeshEntries => _meshes.Count;

        internal int TextureEntries => _textures.Count;

        /// <summary>
        /// Returns the decoded meshes of one file, decoding and caching it on first use.
        /// </summary>
        /// <param name="path">The resolved mesh file.</param>
        /// <param name="vertices">The decoded vertex total to add to.</param>
        /// <param name="failure">
        /// Why the file produced no geometry, when that is what happened. Cached, so a
        /// definition can report the same class for every placement that uses it.
        /// </param>
        internal IReadOnlyList<SceneryMesh>? GetMeshes(
            string path,
            ref long vertices,
            out SceneryDiagnosticClass? failure)
        {
            failure = null;

            if (_meshes.TryGetValue(path, out MeshEntry? cached) &&
                IsCurrent(path, cached.Stamp, cached.Meshes.Count > 0))
            {
                failure = cached.Failure;
                return cached.Meshes;
            }

            if (cached is not null)
            {
                // The file changed under the load: drop the stale geometry and decode it
                // again rather than drawing what is no longer on disk.
                vertices -= cached.Vertices;
                _meshes.Remove(path);
                Replacements++;
            }

            if (_meshes.Count >= MaxUniqueMeshes)
            {
                Diagnostics.Report(
                    SceneryDiagnosticClass.NotAttempted,
                    MapDiagnosticSeverity.Warning,
                    "The scene view decodes at most " + MaxUniqueMeshes +
                    " mesh files; this one is not decoded.",
                    path);
                MeshFailures.Add(SceneryDiagnosticClass.NotAttempted);
                return null;
            }

            SceneryAssetStamp.TryRead(path, out SceneryAssetStamp stamp);
            ModelData model;

            try
            {
                using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                IModelParser parser = Path.GetExtension(path).Equals(
                    ".x",
                    StringComparison.OrdinalIgnoreCase)
                    ? new XModelParser(Path.GetFileName(path))
                    : new O3dModelParser(Path.GetFileName(path));

                model = parser.Parse(stream);
            }
            catch (Exception exception) when (
                exception is ModelFormatException or IOException or
                UnauthorizedAccessException or NotSupportedException or
                ArgumentException or InvalidOperationException)
            {
                SceneryDiagnosticClass kind = exception switch
                {
                    ModelFormatException format => SceneryDecodeFailure.Classify(format.Message),
                    FileNotFoundException or DirectoryNotFoundException
                        => SceneryDiagnosticClass.MissingAsset,
                    _ => SceneryDiagnosticClass.ParseError,
                };

                MeshFailures.Add(kind);

                Diagnostics.Report(
                    kind,
                    MapDiagnosticSeverity.Warning,
                    "The mesh '" + Path.GetFileName(path) +
                    "' could not be decoded: " + exception.Message,
                    path);

                failure = kind;
                _meshes.Add(path, new MeshEntry([], stamp, 0, kind));
                return [];
            }

            long added = 0;

            foreach (MeshData data in model.Meshes)
            {
                added += data.Positions.Length;
            }

            if (vertices + added > MaxVertices)
            {
                Diagnostics.Report(
                    SceneryDiagnosticClass.NotAttempted,
                    MapDiagnosticSeverity.Warning,
                    "The decoded geometry limit of " + MaxVertices +
                    " vertices was reached, so this mesh is not decoded.",
                    path);
                MeshFailures.Add(SceneryDiagnosticClass.NotAttempted);
                failure = SceneryDiagnosticClass.NotAttempted;
                _meshes.Add(path, new MeshEntry([], stamp, 0, failure));
                return [];
            }

            var meshes = new List<SceneryMesh>(model.Meshes.Count);

            foreach (MeshData data in model.Meshes)
            {
                meshes.Add(new SceneryMesh(data, path));
            }

            vertices += added;
            MeshesDecoded++;
            _meshes.Add(path, new MeshEntry(meshes, stamp, added, null));

            return meshes;
        }

        /// <summary>
        /// Resolves the material of a mesh subset through the two OMSI name spaces: the
        /// definition's <c>[matl]</c> declarations, then the material name as a file.
        /// </summary>
        internal SceneryMaterialResolution GetMaterial(
            SceneryDefinition definition,
            string contentRoot,
            string? materialName)
        {
            if (string.IsNullOrWhiteSpace(materialName))
            {
                return new SceneryMaterialResolution(
                    Untextured,
                    SceneryMaterialKind.Untextured,
                    null);
            }

            foreach (SceneryTextureReference declaredTexture in definition.Textures)
            {
                if (!Matches(declaredTexture.Reference, materialName))
                {
                    continue;
                }

                if (declaredTexture.Path is string declaredPath)
                {
                    Texture? declared = LoadTexture(declaredPath, out SceneryMaterialKind kind);

                    return new SceneryMaterialResolution(
                        CreateMaterial(materialName, declared),
                        kind,
                        declaredTexture.Reference);
                }

                SceneryDiagnosticClass failure =
                    declaredTexture.Status == SceneryPathStatus.Rejected
                        ? SceneryDiagnosticClass.ParseError
                        : SceneryDiagnosticClass.MissingAsset;

                Diagnostics.Report(
                    failure,
                    MapDiagnosticSeverity.Warning,
                    "The texture '" + declaredTexture.Reference +
                    "' declared by [matl] could not be resolved: " +
                    (declaredTexture.Error ?? "unknown reason") +
                    " The subset is drawn with its base color.",
                    definition.ScoPath);

                return new SceneryMaterialResolution(
                    CreateMaterial(materialName, null),
                    declaredTexture.Status == SceneryPathStatus.Rejected
                        ? SceneryMaterialKind.ParseError
                        : SceneryMaterialKind.MissingFile,
                    declaredTexture.Reference);
            }

            // A material with no [matl] declaration is resolved by its own name, which
            // is how OMSI's own meshes name a texture file. A name that resolves to
            // nothing is an unmapped label, not a missing file.
            SceneryPathResult resolution = SceneryDefinitionParser.ResolveAsset(
                contentRoot,
                definition.ScoDirectory,
                definition.AssetDirectory,
                materialName,
                SceneryAssetKind.Texture);

            if (resolution.IsResolved)
            {
                Texture? texture = LoadTexture(resolution.Path!, out SceneryMaterialKind kind);

                return new SceneryMaterialResolution(
                    CreateMaterial(materialName, texture),
                    texture is not null ? SceneryMaterialKind.NamedFile : kind,
                    materialName);
            }

            return new SceneryMaterialResolution(
                CreateMaterial(materialName, null),
                SceneryMaterialKind.Unmapped,
                materialName);
        }

        private Texture? LoadTexture(string path, out SceneryMaterialKind kind)
        {
            kind = SceneryMaterialKind.Decoded;

            if (_textures.TryGetValue(path, out TextureEntry? cached))
            {
                if (IsCurrent(path, cached.Stamp, cached.Texture is not null))
                {
                    kind = cached.Kind;
                    return cached.Texture;
                }

                _textures.Remove(path);
                Replacements++;
            }

            if (_textures.Count >= MaxUniqueTextures)
            {
                Diagnostics.Report(
                    SceneryDiagnosticClass.NotAttempted,
                    MapDiagnosticSeverity.Warning,
                    "The scene view decodes at most " + MaxUniqueTextures +
                    " texture files; this one is not decoded.",
                    path);
                TextureFailures.Add(SceneryDiagnosticClass.NotAttempted);
                kind = SceneryMaterialKind.NotAttempted;
                return null;
            }

            SceneryAssetStamp.TryRead(path, out SceneryAssetStamp stamp);
            Texture? texture = null;

            try
            {
                using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                texture = new TextureLoader().Load(stream, path);
            }
            catch (Exception exception) when (
                exception is FormatException or NotSupportedException)
            {
                kind = SceneryMaterialKind.UnsupportedFormat;
                TextureFailures.Add(SceneryDiagnosticClass.UnsupportedFormat);
                Diagnostics.Report(
                    SceneryDiagnosticClass.UnsupportedFormat,
                    MapDiagnosticSeverity.Warning,
                    "The texture '" + Path.GetFileName(path) +
                    "' could not be decoded: " + exception.Message,
                    path);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                kind = SceneryMaterialKind.MissingFile;
                TextureFailures.Add(SceneryDiagnosticClass.MissingAsset);
                Diagnostics.Report(
                    SceneryDiagnosticClass.MissingAsset,
                    MapDiagnosticSeverity.Warning,
                    "The texture '" + Path.GetFileName(path) +
                    "' could not be read: " + exception.Message,
                    path);
            }
            catch (Exception exception) when (
                exception is ArgumentException or InvalidOperationException)
            {
                kind = SceneryMaterialKind.ParseError;
                TextureFailures.Add(SceneryDiagnosticClass.ParseError);
                Diagnostics.Report(
                    SceneryDiagnosticClass.ParseError,
                    MapDiagnosticSeverity.Warning,
                    "The texture '" + Path.GetFileName(path) +
                    "' could not be decoded: " + exception.Message,
                    path);
            }

            if (texture is not null)
            {
                kind = SceneryMaterialKind.Decoded;
                TexturesDecoded++;
            }

            _textures.Add(path, new TextureEntry(texture, kind, stamp));

            return texture;
        }

        /// <summary>
        /// Tests whether a cached entry still describes the file on disk. A file that
        /// cannot be stamped (for example because it was removed) is not treated as
        /// current.
        /// </summary>
        private static bool IsCurrent(string path, SceneryAssetStamp stamp, bool decoded)
        {
            if (!SceneryAssetStamp.TryRead(path, out SceneryAssetStamp current))
            {
                // A recorded failure whose file is still absent stays valid; a decoded
                // asset whose file vanished is no longer current.
                return !decoded;
            }

            return current == stamp;
        }

        private static readonly Material Untextured = new(
            Vector3.One,
            null,
            null,
            MaterialTransparency.Mask);

        private static Material CreateMaterial(string? reference, Texture? texture)
        {
            return new Material(
                Vector3.One,
                texture,
                reference,
                MaterialTransparency.Mask);
        }

        private static bool Matches(string reference, string materialName)
        {
            return string.Equals(reference, materialName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    Path.GetFileName(reference),
                    Path.GetFileName(materialName),
                    StringComparison.OrdinalIgnoreCase);
        }

        private sealed record MeshEntry(
            IReadOnlyList<SceneryMesh> Meshes,
            SceneryAssetStamp Stamp,
            long Vertices,
            SceneryDiagnosticClass? Failure);

        private sealed record TextureEntry(
            Texture? Texture,
            SceneryMaterialKind Kind,
            SceneryAssetStamp Stamp);
    }

    /// <summary>
    /// Aggregates per-definition keyword censuses into one list for the summary.
    /// </summary>
    private sealed class KeywordTotals
    {
        private readonly Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _order = [];

        internal int Total { get; private set; }

        internal int Distinct => _order.Count;

        internal void Add(IReadOnlyList<SceneryKeywordCount> keywords, int total)
        {
            Total += total;

            foreach (SceneryKeywordCount keyword in keywords)
            {
                if (_counts.TryGetValue(keyword.Keyword, out int count))
                {
                    _counts[keyword.Keyword] = count + keyword.Occurrences;
                    continue;
                }

                _counts.Add(keyword.Keyword, keyword.Occurrences);
                _order.Add(keyword.Keyword);
            }
        }

        internal IReadOnlyList<SceneryKeywordCount> Results()
        {
            var results = new List<SceneryKeywordCount>(_order.Count);

            foreach (string keyword in _order)
            {
                results.Add(new SceneryKeywordCount(keyword, _counts[keyword]));
            }

            results.Sort(static (left, right) =>
            {
                int byCount = right.Occurrences.CompareTo(left.Occurrences);

                return byCount != 0
                    ? byCount
                    : string.Compare(left.Keyword, right.Keyword, StringComparison.Ordinal);
            });

            return results;
        }
    }
}

/// <summary>How a mesh subset's material name was turned into a texture.</summary>
internal enum SceneryMaterialKind
{
    /// <summary>A declared <c>[matl]</c> entry named the material and its file decoded.</summary>
    Decoded,

    /// <summary>The material name resolved to a texture file that decoded.</summary>
    NamedFile,

    /// <summary>No <c>[matl]</c> entry names the material and no file of that name exists.</summary>
    Unmapped,

    /// <summary>The material names a texture file that is absent.</summary>
    MissingFile,

    /// <summary>The texture file exists and the engine's decoder refused it.</summary>
    UnsupportedFormat,

    /// <summary>The texture file exists but is malformed or unreadable.</summary>
    ParseError,

    /// <summary>The texture was not decoded because a documented cache limit was hit.</summary>
    NotAttempted,

    /// <summary>The subset names no material at all.</summary>
    Untextured,
}

/// <summary>One subset's resolved material and how it was obtained.</summary>
/// <param name="Material">The engine material, with its optional texture.</param>
/// <param name="Kind">How the material was resolved.</param>
/// <param name="Reference">The name that was resolved, for reporting.</param>
internal sealed record SceneryMaterialResolution(
    Material Material,
    SceneryMaterialKind Kind,
    string? Reference);

/// <summary>Measured placement counts for one scenery load.</summary>
/// <param name="Considered">Placements whose reference was resolved and inspected.</param>
/// <param name="Instanced">Placements added to the scene.</param>
/// <param name="Resolved">Placements that reused or loaded a model.</param>
/// <param name="Failures">Skipped placements, counted per diagnostic class.</param>
/// <param name="WithoutTerrain">Placements skipped because their terrain-relative height had no terrain.</param>
/// <param name="InvalidTransform">Placements whose transform could not be computed.</param>
internal sealed record SceneryPlacementCounts(
    int Considered,
    int Instanced,
    int Resolved,
    SceneryClassTally Failures,
    int WithoutTerrain,
    int InvalidTransform);

/// <summary>Measured definition counts for one scenery load.</summary>
/// <param name="Loaded">Distinct definitions with drawable geometry.</param>
/// <param name="WithoutGeometry">Distinct definitions that produced no drawable geometry.</param>
internal sealed record SceneryDefinitionCounts(int Loaded, int WithoutGeometry);

/// <summary>Measured counts for one kind of decoded asset.</summary>
/// <param name="Decoded">Files of this kind the engine decoded.</param>
/// <param name="Failures">Files of this kind that were skipped, counted per diagnostic class.</param>
internal sealed record SceneryAssetCounts(int Decoded, SceneryClassTally Failures);

/// <summary>Measured subset counts for one scenery load.</summary>
/// <param name="Drawable">Subsets across the distinct loaded definitions.</param>
/// <param name="WithoutTexture">Subsets drawn with their base color.</param>
/// <param name="Failures">Those subsets, counted per diagnostic class.</param>
internal sealed record ScenerySubsetCounts(
    int Drawable,
    int WithoutTexture,
    SceneryClassTally Failures);

/// <summary>Cache identity and mid-run replacement counts for one scenery load.</summary>
/// <param name="Definitions">Definitions cached by resolved path.</param>
/// <param name="Meshes">Mesh files cached by resolved path.</param>
/// <param name="Textures">Textures cached by resolved path.</param>
/// <param name="ReplacedMidRun">Cached assets whose size or last-write time changed during the load.</param>
internal sealed record SceneryCacheCounts(int Definitions, int Meshes, int Textures, int ReplacedMidRun);

/// <summary>
/// Measured counts for one scenery load, grouped so each summary line reads one group.
/// </summary>
/// <param name="Placements">Placement counts and their failure classes.</param>
/// <param name="Definitions">Definition counts.</param>
/// <param name="Meshes">Mesh counts and their failure classes.</param>
/// <param name="Textures">Texture counts and their failure classes.</param>
/// <param name="Subsets">Subset counts and their material failure classes.</param>
/// <param name="Vertices">Decoded vertex count across all mesh files.</param>
/// <param name="UnsupportedKeywords">Occurrences of definition keywords this build does not interpret.</param>
/// <param name="SuppressedProblems">Diagnostic occurrences counted but not listed.</param>
/// <param name="Cache">Cache identity counts.</param>
internal sealed record SceneryLoadCounts(
    SceneryPlacementCounts Placements,
    SceneryDefinitionCounts Definitions,
    SceneryAssetCounts Meshes,
    SceneryAssetCounts Textures,
    ScenerySubsetCounts Subsets,
    long Vertices,
    int UnsupportedKeywords,
    int SuppressedProblems,
    SceneryCacheCounts Cache);

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
/// Everything is cached by resolved path. Twenty instances of one building share one
/// parsed definition, one decoded mesh, one decoded texture, and later one set of GPU
/// buffers. Nothing is decoded per frame.
/// </para>
/// <para>
/// The scene is a documented partial scene, not a claim about OMSI compatibility.
/// Placements are taken in tile and file order up to <see cref="MaxInstances"/>, and
/// each one that is not drawn is counted under a reason: an unresolvable reference, an
/// unparsable definition, no decoded terrain under a terrain-relative height, or a
/// transform that cannot be computed. Diagnostics name the tile and line for the first
/// few of each kind, and the total number of suppressed messages is reported, so a
/// truncated list is never mistaken for a complete one.
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
    /// The largest number of individual problem diagnostics recorded. Further problems
    /// are only counted.
    /// </summary>
    internal const int MaxReportedProblems = 40;

    private OmsiSceneryLibrary(
        IReadOnlyList<SceneryInstance> instances,
        IReadOnlyList<SceneryModel> models,
        IReadOnlyList<MapDiagnostic> diagnostics,
        SceneryLoadCounts counts)
    {
        Instances = instances;
        Models = models;
        Diagnostics = diagnostics;
        Counts = counts;
    }

    /// <summary>Gets the placements to draw, in tile and file order.</summary>
    internal IReadOnlyList<SceneryInstance> Instances { get; }

    /// <summary>Gets the distinct loaded models.</summary>
    internal IReadOnlyList<SceneryModel> Models { get; }

    /// <summary>Gets the load warnings and errors.</summary>
    internal IReadOnlyList<MapDiagnostic> Diagnostics { get; }

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

        var diagnostics = new List<MapDiagnostic>();
        var reporter = new ProblemReporter(diagnostics);
        var instances = new List<SceneryInstance>();
        var models = new Dictionary<string, SceneryModel>(StringComparer.OrdinalIgnoreCase);
        var loadedModels = new List<SceneryModel>();
        var meshFiles = new Dictionary<string, IReadOnlyList<SceneryMesh>>(
            StringComparer.OrdinalIgnoreCase);
        var textures = new Dictionary<string, Texture?>(StringComparer.OrdinalIgnoreCase);
        var cache = new LoadCache(
            meshFiles,
            textures,
            diagnostics,
            reporter);

        int considered = 0;
        int resolvedDefinitions = 0;
        int unresolvedReferences = 0;
        int definitionFailures = 0;
        int missingTerrain = 0;
        int transformFailures = 0;
        int skippedByLimit = 0;
        int skippedByModelLimit = 0;
        long vertices = 0;

        if (contentRoot is null)
        {
            diagnostics.Add(new MapDiagnostic(
                MapDiagnosticSeverity.Warning,
                "The OMSI installation root could not be derived from the map folder, " +
                "so no scenery reference can be resolved and no object is loaded. The " +
                "map folder must be inside a folder named 'maps'."));

            return new OmsiSceneryLibrary(
                [],
                [],
                diagnostics,
                new SceneryLoadCounts(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        }

        if (terrain is null)
        {
            diagnostics.Add(new MapDiagnostic(
                MapDiagnosticSeverity.Warning,
                "No terrain was decoded, so every object whose height is " +
                "terrain-relative is skipped instead of being placed at an invented " +
                "height. Objects that declare [absheight] are still placed."));
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
                    skippedByLimit++;

                    if (instances.Count == MaxInstances)
                    {
                        diagnostics.Add(new MapDiagnostic(
                            MapDiagnosticSeverity.Warning,
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "The scene view draws at most {0} placements. Later " +
                                "placements are not loaded, so this is a deliberately " +
                                "partial scene.",
                                MaxInstances)));
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
                    unresolvedReferences++;
                    reporter.Report(
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
                        stopped = true;
                        continue;
                    }

                    model = LoadModel(
                        contentRoot,
                        scoPath,
                        placement.ModelPath,
                        cache,
                        ref vertices);

                    if (model is null)
                    {
                        definitionFailures++;
                        continue;
                    }

                    models.Add(scoPath, model);
                    loadedModels.Add(model);
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
                    if (!model.UsesAbsoluteHeight && terrainHeight is null)
                    {
                        missingTerrain++;
                    }
                    else
                    {
                        transformFailures++;
                    }

                    reporter.Report(
                        MapDiagnosticSeverity.Warning,
                        "The placement of '" + placement.ModelPath + "' is skipped: " +
                        (transformError ?? "the transform could not be computed."),
                        tile.FilePath,
                        placement.LineNumber);
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
            diagnostics.Add(new MapDiagnostic(
                MapDiagnosticSeverity.Warning,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The scene view loads at most {0} distinct scenery definitions. " +
                    "{1} further placement(s) were not attempted.",
                    MaxUniqueModels,
                    skippedByModelLimit)));
        }

        if (skippedByLimit > 0)
        {
            diagnostics.Add(new MapDiagnostic(
                MapDiagnosticSeverity.Warning,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} placement(s) beyond the {1}-instance scene limit were not " +
                    "loaded.",
                    skippedByLimit,
                    MaxInstances)));
        }

        if (reporter.Suppressed > 0)
        {
            diagnostics.Add(new MapDiagnostic(
                MapDiagnosticSeverity.Warning,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} further problem(s) of the kinds already reported were counted " +
                    "but not listed.",
                    reporter.Suppressed)));
        }

        var counts = new SceneryLoadCounts(
            considered,
            instances.Count,
            loadedModels.Count,
            resolvedDefinitions,
            unresolvedReferences,
            definitionFailures,
            cache.MeshesDecoded,
            cache.TexturesDecoded,
            cache.TexturesMissing,
            missingTerrain,
            transformFailures,
            skippedByLimit + skippedByModelLimit,
            vertices,
            loadedModels.Sum(model => model.Subsets.Count),
            cache.UnsupportedKeywordCount,
            reporter.Suppressed);

        return new OmsiSceneryLibrary(instances, loadedModels, diagnostics, counts);
    }

    /// <summary>
    /// Parses one definition and decodes its meshes and textures.
    /// </summary>
    /// <returns>The loaded model, or <see langword="null"/> when nothing can be drawn.</returns>
    private static SceneryModel? LoadModel(
        string contentRoot,
        string scoPath,
        string reference,
        LoadCache cache,
        ref long vertices)
    {
        SceneryParseResult parsed = SceneryDefinitionParser.Parse(
            scoPath,
            reference,
            contentRoot);

        foreach (MapDiagnostic diagnostic in parsed.Diagnostics)
        {
            cache.Diagnostics.Add(diagnostic);
        }

        if (parsed.Definition is not SceneryDefinition definition)
        {
            return null;
        }

        cache.UnsupportedKeywordCount += definition.UnsupportedKeywordCount;

        var subsets = new List<ScenerySubset>();
        BoundingBox? bounds = null;

        foreach (SceneryMeshReference meshReference in definition.Meshes)
        {
            if (!meshReference.IsResolved)
            {
                continue;
            }

            IReadOnlyList<SceneryMesh>? meshes = cache.GetMeshes(
                meshReference.Path!,
                ref vertices);

            if (meshes is null)
            {
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

                    Material material = cache.GetMaterial(
                        definition,
                        contentRoot,
                        subset.MaterialName);

                    subsets.Add(new ScenerySubset(
                        mesh,
                        subset.IndexOffset,
                        subset.IndexCount,
                        material,
                        subset.MaterialName));
                }
            }
        }

        if (subsets.Count == 0 || bounds is null)
        {
            cache.Diagnostics.Add(new MapDiagnostic(
                MapDiagnosticSeverity.Warning,
                "No mesh of this definition could be decoded, so the object is not " +
                "drawn.",
                scoPath));

            return null;
        }

        return new SceneryModel(
            reference,
            scoPath,
            subsets,
            bounds.Value,
            definition.UsesAbsoluteHeight);
    }

    /// <summary>
    /// Holds the per-load caches and the counting that goes with them.
    /// </summary>
    private sealed class LoadCache
    {
        private readonly Dictionary<string, IReadOnlyList<SceneryMesh>> _meshes;
        private readonly Dictionary<string, Texture?> _textures;
        private readonly Dictionary<string, Material> _materials =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly ProblemReporter _reporter;

        internal LoadCache(
            Dictionary<string, IReadOnlyList<SceneryMesh>> meshes,
            Dictionary<string, Texture?> textures,
            List<MapDiagnostic> diagnostics,
            ProblemReporter reporter)
        {
            _meshes = meshes;
            _textures = textures;
            _reporter = reporter;
            Diagnostics = diagnostics;
        }

        internal List<MapDiagnostic> Diagnostics { get; }

        internal int MeshesDecoded { get; private set; }

        internal int TexturesDecoded { get; private set; }

        internal int TexturesMissing { get; private set; }

        internal int UnsupportedKeywordCount { get; set; }

        /// <summary>
        /// Returns the decoded meshes of one file, decoding and caching it on first use.
        /// </summary>
        internal IReadOnlyList<SceneryMesh>? GetMeshes(string path, ref long vertices)
        {
            if (_meshes.TryGetValue(path, out IReadOnlyList<SceneryMesh>? cached))
            {
                return cached;
            }

            if (_meshes.Count >= MaxUniqueMeshes)
            {
                _reporter.Report(
                    MapDiagnosticSeverity.Warning,
                    "The scene view decodes at most " + MaxUniqueMeshes +
                    " mesh files; this one is not decoded.",
                    path);
                return null;
            }

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
                _reporter.Report(
                    MapDiagnosticSeverity.Warning,
                    "The mesh '" + Path.GetFileName(path) +
                    "' could not be decoded: " + exception.Message,
                    path);

                _meshes.Add(path, []);
                return [];
            }

            long added = 0;

            foreach (MeshData data in model.Meshes)
            {
                added += data.Positions.Length;
            }

            if (vertices + added > MaxVertices)
            {
                _reporter.Report(
                    MapDiagnosticSeverity.Warning,
                    "The decoded geometry limit of " + MaxVertices +
                    " vertices was reached, so this mesh is not decoded.",
                    path);
                return [];
            }

            var meshes = new List<SceneryMesh>(model.Meshes.Count);

            foreach (MeshData data in model.Meshes)
            {
                meshes.Add(new SceneryMesh(data, path));
            }

            vertices += added;
            MeshesDecoded++;
            _meshes.Add(path, meshes);

            return meshes;
        }

        /// <summary>
        /// Resolves the material of a mesh subset: its texture name, then the engine
        /// material with that texture.
        /// </summary>
        internal Material GetMaterial(
            SceneryDefinition definition,
            string contentRoot,
            string? materialName)
        {
            string? texturePath = null;
            string? reference = null;

            if (!string.IsNullOrWhiteSpace(materialName))
            {
                reference = materialName;
                string? declared = null;

                foreach (SceneryTextureReference declaredTexture in definition.Textures)
                {
                    if (Matches(declaredTexture.Reference, materialName))
                    {
                        declared = declaredTexture.Reference;
                        texturePath = declaredTexture.Path;
                        break;
                    }
                }

                // A declared [matl] texture wins, including when the file is absent.
                // A subset with no declaration is resolved by its own name, which is
                // the name OMSI's own meshes use for a texture file.
                if (declared is null)
                {
                    SceneryPathResult resolution = SceneryDefinitionParser.ResolveAsset(
                        contentRoot,
                        definition.AssetDirectory,
                        materialName,
                        extraFolder: "Texture");

                    texturePath = resolution.Path;
                }
            }

            string cacheKey = reference is null
                ? "<untextured>"
                : reference + "|" + (texturePath ?? "<missing>");

            if (_materials.TryGetValue(cacheKey, out Material? cached))
            {
                return cached;
            }

            Texture? texture = texturePath is null
                ? null
                : LoadTexture(texturePath);

            // A subset that names a material but has no usable texture is a reported
            // limitation; a subset that names none is simply untextured.
            if (texturePath is null && reference is not null)
            {
                TexturesMissing++;
            }

            var material = new Material(
                Vector3.One,
                texture,
                reference,
                MaterialTransparency.Mask);

            _materials.Add(cacheKey, material);

            return material;
        }

        private Texture? LoadTexture(string path)
        {
            if (_textures.TryGetValue(path, out Texture? cached))
            {
                return cached;
            }

            if (_textures.Count >= MaxUniqueTextures)
            {
                _reporter.Report(
                    MapDiagnosticSeverity.Warning,
                    "The scene view decodes at most " + MaxUniqueTextures +
                    " texture files; this one is not decoded.",
                    path);
                return null;
            }

            Texture? texture = null;

            try
            {
                using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                texture = new TextureLoader().Load(stream, path);
            }
            catch (Exception exception) when (
                exception is FormatException or IOException or
                UnauthorizedAccessException or NotSupportedException or
                ArgumentException or InvalidOperationException)
            {
                _reporter.Report(
                    MapDiagnosticSeverity.Warning,
                    "The texture '" + Path.GetFileName(path) +
                    "' could not be decoded: " + exception.Message,
                    path);
            }

            if (texture is not null)
            {
                TexturesDecoded++;
            }

            _textures.Add(path, texture);

            return texture;
        }

        private static bool Matches(string reference, string materialName)
        {
            return string.Equals(reference, materialName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    Path.GetFileName(reference),
                    Path.GetFileName(materialName),
                    StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Records the first few problems of a kind and counts the rest.
    /// </summary>
    private sealed class ProblemReporter
    {
        private readonly List<MapDiagnostic> _diagnostics;
        private int _reported;

        internal ProblemReporter(List<MapDiagnostic> diagnostics)
        {
            _diagnostics = diagnostics;
        }

        internal int Suppressed { get; private set; }

        internal void Report(
            MapDiagnosticSeverity severity,
            string message,
            string? path = null,
            int? lineNumber = null)
        {
            if (_reported >= MaxReportedProblems)
            {
                Suppressed++;
                return;
            }

            _reported++;
            _diagnostics.Add(new MapDiagnostic(severity, message, path, lineNumber));
        }
    }
}

/// <summary>
/// Measured counts for one scenery load.
/// </summary>
/// <param name="PlacementsConsidered">Placements whose reference was resolved and inspected.</param>
/// <param name="InstancesCreated">Placements added to the scene.</param>
/// <param name="ModelsLoaded">Distinct definitions loaded.</param>
/// <param name="InstancesResolved">Placements that reused or loaded a model.</param>
/// <param name="UnresolvedReferences">Placements whose <c>.sco</c> file was not found or was rejected.</param>
/// <param name="DefinitionFailures">Definitions that parsed but produced no drawable geometry.</param>
/// <param name="MeshesDecoded">Distinct mesh files decoded.</param>
/// <param name="TexturesDecoded">Distinct texture files decoded.</param>
/// <param name="TexturesMissing">Subsets whose texture could not be resolved or decoded.</param>
/// <param name="SkippedWithoutTerrain">Placements skipped because their terrain-relative height had no terrain.</param>
/// <param name="TransformFailures">Placements whose transform could not be computed.</param>
/// <param name="SkippedByLimit">Placements not attempted because of an instance or model limit.</param>
/// <param name="Vertices">Decoded vertex count across all mesh files.</param>
/// <param name="Subsets">Drawable subsets across all distinct models.</param>
/// <param name="UnsupportedKeywords">Occurrences of definition keywords this build does not interpret.</param>
/// <param name="SuppressedProblems">Problems counted but not listed individually.</param>
internal readonly record struct SceneryLoadCounts(
    int PlacementsConsidered,
    int InstancesCreated,
    int ModelsLoaded,
    int InstancesResolved,
    int UnresolvedReferences,
    int DefinitionFailures,
    int MeshesDecoded,
    int TexturesDecoded,
    int TexturesMissing,
    int SkippedWithoutTerrain,
    int TransformFailures,
    int SkippedByLimit,
    long Vertices,
    int Subsets,
    int UnsupportedKeywords,
    int SuppressedProblems);

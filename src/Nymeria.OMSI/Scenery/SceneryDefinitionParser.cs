using System.Globalization;
using Nymeria.Omsi.Maps;

namespace Nymeria.Omsi.Scenery;

/// <summary>The kind of asset a reference names, which selects the search folders.</summary>
internal enum SceneryAssetKind
{
    /// <summary>A mesh file, looked for in the definition's model locations.</summary>
    Mesh,

    /// <summary>A texture file, looked for in the definition's texture locations.</summary>
    Texture,
}

/// <summary>
/// Reads the supported subset of an OMSI <c>.sco</c> scenery definition and the model
/// configuration it names.
/// </summary>
/// <remarks>
/// <para>
/// The reader follows the same documented keyword-block rules as the map parsers
/// (<see cref="KeywordBlockReader"/>): an unindented line of the form <c>[keyword]</c>
/// starts a block, its parameters follow in order, and indented keywords are help text
/// or switched-off blocks. The documented <c>-&lt;DISABLED&gt;-</c> /
/// <c>-&lt;ENABLED&gt;-</c> markers remove the lines between them before that split, and
/// a parameter line that starts with an apostrophe is treated as a comment, which is
/// the documented way to comment out one line.
/// </para>
/// <para>
/// Interpreted keywords are <c>[model]</c> (an optional further configuration file, or
/// an inline model mesh), <c>[mesh]</c> (a mesh file), <c>[matl]</c> (a texture name
/// used by a mesh subset), and <c>[absheight]</c> (a standalone flag meaning the
/// placement height is absolute). A scenery definition does not need a <c>[model]</c>
/// block: an inline <c>[mesh]</c> list is the usual form. Everything else is counted,
/// named, and left uninterpreted: scripts, animations, interaction, collision, and
/// advanced material effects are out of scope, and no meaning is invented for a keyword
/// this reader does not know.
/// </para>
/// <para>
/// Mesh and texture files are resolved while parsing, under the same traversal-safe
/// rules as <see cref="SceneryPath"/>. OMSI writes these references relative to the
/// definition's own folder or to its documented <c>model</c> and <c>texture</c>
/// subfolders, so each documented location is tried in order without a recursive
/// search. A missing file is a diagnostic, not an exception, so one damaged object
/// cannot stop the map from loading.
/// </para>
/// </remarks>
internal static class SceneryDefinitionParser
{
    /// <summary>The byte limit for a scenery definition or model configuration.</summary>
    internal const int MaxConfigBytes = 512 * 1024;

    /// <summary>The line limit for a scenery definition or model configuration.</summary>
    internal const int MaxConfigLines = 20_000;

    /// <summary>The largest number of meshes read from one definition.</summary>
    internal const int MaxMeshes = 64;

    /// <summary>The largest number of <c>[matl]</c> entries read from one definition.</summary>
    internal const int MaxTextures = 256;

    /// <summary>The largest number of distinct unsupported keywords reported.</summary>
    internal const int MaxReportedUnsupportedKeywords = 8;

    /// <summary>The mesh file extensions Nymeria decodes.</summary>
    internal static readonly IReadOnlyList<string> SupportedMeshExtensions = [".o3d", ".x"];

    private const string SwitchOffMarker = "-<DISABLED>-";
    private const string SwitchOnMarker = "-<ENABLED>-";

    /// <summary>
    /// Keywords that are metadata for the OMSI user interface and carry no geometry.
    /// They are recognized so they are not reported as unsupported constructs.
    /// </summary>
    private static readonly HashSet<string> MetadataKeywords = new(
        [
            "groups",
            "friendlyname",
            "description",
            "author",
            "copyright",
            "addcamera",
            "camera",
            "entrypoint",
            "nocollision",
            "noshadow",
            "notvisible",
            "render",
            "texchange",
        ],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Parses one scenery definition.
    /// </summary>
    /// <param name="scoPath">The resolved <c>.sco</c> file.</param>
    /// <param name="reference">The reference as written in the tile file.</param>
    /// <param name="contentRoot">
    /// The OMSI installation root that bounds every resolution.
    /// </param>
    /// <returns>
    /// The parsed definition, or diagnostics explaining why there is none. It never
    /// throws for unreadable or malformed content.
    /// </returns>
    internal static SceneryParseResult Parse(
        string scoPath,
        string reference,
        string contentRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scoPath);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);

        var diagnostics = new SceneryDiagnostics(int.MaxValue);

        IReadOnlyList<KeywordBlock> blocks = ReadBlocks(
            scoPath,
            diagnostics,
            out bool readFailed);

        if (readFailed)
        {
            return new SceneryParseResult(null, diagnostics.Diagnostics);
        }

        if (blocks.Count == 0)
        {
            diagnostics.Report(
                MapDiagnosticSeverity.Warning,
                "The scenery definition has no [keyword] blocks, so it describes no " +
                "model.",
                scoPath);

            return new SceneryParseResult(null, diagnostics.Diagnostics);
        }

        var unsupported = new OrderedKeywordSet();
        bool usesAbsoluteHeight = false;

        foreach (KeywordBlock block in blocks)
        {
            string keyword = block.Keyword;

            if (keyword.Equals("absheight", StringComparison.OrdinalIgnoreCase))
            {
                usesAbsoluteHeight = true;
                continue;
            }

            // Metadata keywords are recognized and deliberately unused; everything
            // else that is not interpreted is counted and named.
            if (IsHandledKeyword(keyword) || MetadataKeywords.Contains(keyword))
            {
                continue;
            }

            unsupported.Add(keyword);
        }

        string scoDirectory = Path.GetDirectoryName(scoPath) ?? contentRoot;
        string assetDirectory = scoDirectory;
        string? modelConfigPath = null;
        string? inlineModelMesh = null;
        int inlineModelLine = 0;
        bool usesExternalModelConfig = false;

        if (TryFindBlock(blocks, "model", out KeywordBlock modelBlock))
        {
            string? modelReference = FirstParameter(modelBlock);

            if (modelReference is null)
            {
                diagnostics.Report(
                    MapDiagnosticSeverity.Warning,
                    "The [model] block has no parameter, so no model file is named.",
                    scoPath,
                    modelBlock.LineNumber);
            }
            else if (LooksLikeConfigurationFile(modelReference))
            {
                SceneryPathResult resolution = SceneryPath.ResolveRelative(
                    contentRoot,
                    scoDirectory,
                    modelReference);

                if (resolution.IsResolved)
                {
                    modelConfigPath = resolution.Path;
                    usesExternalModelConfig = true;
                    assetDirectory = Path.GetDirectoryName(resolution.Path!) ?? assetDirectory;
                }
                else
                {
                    diagnostics.Report(
                        MapDiagnosticSeverity.Error,
                        "The [model] reference '" + modelReference +
                        "' could not be resolved: " + (resolution.Error ?? "unknown reason") +
                        " The definition names a separate model configuration, so no " +
                        "geometry is available without it.",
                        scoPath,
                        modelBlock.LineNumber);
                }
            }
            else if (IsSupportedMeshReference(modelReference))
            {
                // The documented grammar also allows [model] to name the mesh directly
                // instead of a further configuration file.
                inlineModelMesh = modelReference;
                inlineModelLine = modelBlock.LineNumber;
            }
            else
            {
                diagnostics.Report(
                    MapDiagnosticSeverity.Warning,
                    "The [model] parameter '" + modelReference + "' is neither an " +
                    "existing model configuration nor a decodable mesh path, so it is " +
                    "not interpreted.",
                    scoPath,
                    modelBlock.LineNumber);
            }
        }

        IReadOnlyList<KeywordBlock> meshBlocks = blocks;

        if (!usesExternalModelConfig &&
            inlineModelMesh is null &&
            !HasBlock(blocks, "mesh"))
        {
            diagnostics.Report(
                MapDiagnosticSeverity.Error,
                "The definition names no model configuration, no model mesh, and no " +
                "inline [mesh] block, so it describes no geometry.",
                scoPath);

            return new SceneryParseResult(null, diagnostics.Diagnostics);
        }

        if (usesExternalModelConfig)
        {
            meshBlocks = ReadBlocks(
                modelConfigPath!,
                diagnostics,
                out readFailed);

            if (readFailed)
            {
                return new SceneryParseResult(null, diagnostics.Diagnostics);
            }

            foreach (KeywordBlock block in meshBlocks)
            {
                if (IsHandledKeyword(block.Keyword) ||
                    MetadataKeywords.Contains(block.Keyword))
                {
                    continue;
                }

                unsupported.Add(block.Keyword);
            }
        }

        var meshes = new List<SceneryMeshReference>();
        var textures = new List<SceneryTextureReference>();
        int skippedMeshes = 0;
        int skippedTextures = 0;

        if (inlineModelMesh is not null)
        {
            if (meshes.Count >= MaxMeshes)
            {
                skippedMeshes++;
            }
            else
            {
                meshes.Add(ResolveMesh(
                    contentRoot,
                    scoDirectory,
                    assetDirectory,
                    inlineModelMesh,
                    scoPath,
                    inlineModelLine,
                    diagnostics));
            }
        }

        foreach (KeywordBlock block in meshBlocks)
        {
            if (block.Keyword.Equals("mesh", StringComparison.OrdinalIgnoreCase))
            {
                string? meshReference = FirstParameter(block);

                if (meshReference is null)
                {
                    diagnostics.Report(
                        MapDiagnosticSeverity.Warning,
                        "The [mesh] block has no parameter, so no mesh is named.",
                        scoPath,
                        block.LineNumber);
                    continue;
                }

                if (meshes.Count >= MaxMeshes)
                {
                    skippedMeshes++;
                    continue;
                }

                meshes.Add(ResolveMesh(
                    contentRoot,
                    scoDirectory,
                    assetDirectory,
                    meshReference,
                    scoPath,
                    block.LineNumber,
                    diagnostics));
                continue;
            }

            if (block.Keyword.Equals("matl", StringComparison.OrdinalIgnoreCase))
            {
                string? textureReference = FirstParameter(block);

                if (textureReference is null)
                {
                    continue;
                }

                if (textures.Count >= MaxTextures)
                {
                    skippedTextures++;
                    continue;
                }

                textures.Add(ResolveTexture(
                    contentRoot,
                    scoDirectory,
                    assetDirectory,
                    textureReference));
            }
        }

        if (skippedMeshes > 0)
        {
            diagnostics.Report(
                MapDiagnosticSeverity.Warning,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} [mesh] block(s) beyond the {1}-mesh limit were not read.",
                    skippedMeshes,
                    MaxMeshes),
                scoPath);
        }

        if (skippedTextures > 0)
        {
            diagnostics.Report(
                MapDiagnosticSeverity.Warning,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} [matl] block(s) beyond the {1}-texture limit were not read.",
                    skippedTextures,
                    MaxTextures),
                scoPath);
        }

        if (meshes.Count == 0)
        {
            diagnostics.Report(
                MapDiagnosticSeverity.Error,
                "No [mesh] block named a mesh file, so the definition describes no " +
                "geometry.",
                scoPath);

            return new SceneryParseResult(null, diagnostics.Diagnostics);
        }

        var definition = new SceneryDefinition(
            reference,
            scoPath,
            scoDirectory,
            assetDirectory,
            modelConfigPath,
            usesExternalModelConfig,
            usesAbsoluteHeight,
            meshes,
            textures,
            unsupported.Names(MaxReportedUnsupportedKeywords),
            unsupported.Count,
            diagnostics.Diagnostics);

        return new SceneryParseResult(definition, diagnostics.Diagnostics);
    }

    /// <summary>
    /// Resolves one mesh reference against the documented model locations and then the
    /// content root.
    /// </summary>
    private static SceneryMeshReference ResolveMesh(
        string contentRoot,
        string scoDirectory,
        string assetDirectory,
        string reference,
        string sourcePath,
        int lineNumber,
        SceneryDiagnostics diagnostics)
    {
        if (!SceneryPath.TryNormalize(reference, out _, out string? referenceError))
        {
            string rejected =
                "The mesh '" + reference + "' could not be resolved: " + referenceError;

            diagnostics.Report(
                MapDiagnosticSeverity.Warning,
                rejected,
                sourcePath,
                lineNumber);

            return new SceneryMeshReference(reference, null, rejected);
        }

        if (!IsSupportedMeshReference(reference))
        {
            string extension = Path.GetExtension(reference);
            string message =
                "The mesh '" + reference + "' has the extension '" + extension +
                "', which this build does not decode. Supported mesh extensions are " +
                string.Join(", ", SupportedMeshExtensions) + ".";

            diagnostics.Report(
                MapDiagnosticSeverity.Warning,
                message,
                sourcePath,
                lineNumber);

            return new SceneryMeshReference(reference, null, message);
        }

        SceneryPathResult resolution = ResolveAsset(
            contentRoot,
            scoDirectory,
            assetDirectory,
            reference,
            SceneryAssetKind.Mesh);

        if (!resolution.IsResolved)
        {
            string message =
                "The mesh '" + reference + "' could not be resolved: " +
                (resolution.Error ?? "unknown reason");

            diagnostics.Report(
                MapDiagnosticSeverity.Warning,
                message,
                sourcePath,
                lineNumber);
        }

        return new SceneryMeshReference(reference, resolution.Path, resolution.Error);
    }

    /// <summary>
    /// Resolves one texture reference against the documented texture locations. Textures
    /// are optional: a mesh subset with a missing texture still draws with its base
    /// color, so the failure is carried on the reference and is reported only when a
    /// drawn subset actually needs it.
    /// </summary>
    private static SceneryTextureReference ResolveTexture(
        string contentRoot,
        string scoDirectory,
        string assetDirectory,
        string reference)
    {
        SceneryPathResult resolution = ResolveAsset(
            contentRoot,
            scoDirectory,
            assetDirectory,
            reference,
            SceneryAssetKind.Texture);

        return new SceneryTextureReference(reference, resolution.Path, resolution.Error);
    }

    /// <summary>
    /// Tries the documented asset locations in order and returns the first existing
    /// file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Meshes are looked for beside the configuration file, in its <c>model</c>
    /// subfolder, in the definition's <c>model</c> subfolder, beside the definition, and
    /// finally at the content root. Textures use the same idea with the documented
    /// <c>texture</c> subfolders and the installation-wide <c>Texture</c> folder.
    /// </para>
    /// <para>
    /// Only those explicit folders are inspected; there is no recursive search. Every
    /// candidate is resolved through <see cref="SceneryPath.ResolveRelative(string,
    /// string, string)"/>, so a reference that climbs above the content root is rejected
    /// rather than repaired.
    /// </para>
    /// <para>
    /// A texture with no extension preference is first tried as <c>.dds</c> in each
    /// folder, because OMSI prefers the DDS variant when both files exist.
    /// </para>
    /// </remarks>
    /// <param name="contentRoot">The installation root that bounds every candidate.</param>
    /// <param name="scoDirectory">The folder of the <c>.sco</c> definition.</param>
    /// <param name="assetDirectory">
    /// The folder of the model configuration, or the <c>.sco</c> folder for an inline
    /// model.
    /// </param>
    /// <param name="reference">The reference exactly as written.</param>
    /// <param name="kind">Whether the reference names a mesh or a texture.</param>
    /// <returns>The resolution outcome; it never throws for absent or invalid input.</returns>
    internal static SceneryPathResult ResolveAsset(
        string contentRoot,
        string scoDirectory,
        string assetDirectory,
        string reference,
        SceneryAssetKind kind)
    {
        ArgumentNullException.ThrowIfNull(contentRoot);
        ArgumentNullException.ThrowIfNull(scoDirectory);
        ArgumentNullException.ThrowIfNull(assetDirectory);
        ArgumentNullException.ThrowIfNull(reference);

        if (!SceneryPath.TryNormalize(reference, out _, out string? error))
        {
            return new SceneryPathResult(
                SceneryPathStatus.Rejected,
                null,
                reference,
                error);
        }

        List<string> candidates = CandidateDirectories(
            contentRoot,
            scoDirectory,
            assetDirectory,
            kind);

        var searched = new List<string>(candidates.Count);

        foreach (string directory in candidates)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            searched.Add(directory);

            if (kind == SceneryAssetKind.Texture)
            {
                SceneryPathResult? preferred = TryDdsVariant(contentRoot, directory, reference);

                if (preferred is SceneryPathResult dds)
                {
                    if (dds.IsResolved || dds.Status == SceneryPathStatus.Rejected)
                    {
                        return dds;
                    }
                }
            }

            SceneryPathResult resolution = SceneryPath.ResolveRelative(
                contentRoot,
                directory,
                reference);

            if (resolution.IsResolved || resolution.Status == SceneryPathStatus.Rejected)
            {
                return resolution;
            }
        }

        string folders = searched.Count == 0
            ? "no candidate folder exists"
            : string.Join("; ", searched);

        return new SceneryPathResult(
            SceneryPathStatus.NotFound,
            null,
            reference,
            $"'{reference}' was not found in any permitted asset folder ({folders}).");
    }

    /// <summary>
    /// Builds the documented search folders in order, without duplicates.
    /// </summary>
    private static List<string> CandidateDirectories(
        string contentRoot,
        string scoDirectory,
        string assetDirectory,
        SceneryAssetKind kind)
    {
        var candidates = new List<string>(8);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string directory)
        {
            if (seen.Add(Path.GetFullPath(directory)))
            {
                candidates.Add(directory);
            }
        }

        if (kind == SceneryAssetKind.Mesh)
        {
            Add(assetDirectory);
            Add(Path.Combine(assetDirectory, "model"));
            Add(Path.Combine(scoDirectory, "model"));
            Add(scoDirectory);
        }
        else
        {
            Add(assetDirectory);
            Add(Path.Combine(assetDirectory, "texture"));
            Add(scoDirectory);
            Add(Path.Combine(scoDirectory, "texture"));
        }

        Add(contentRoot);

        if (kind == SceneryAssetKind.Texture)
        {
            Add(Path.Combine(contentRoot, "Texture"));
        }

        return candidates;
    }

    /// <summary>
    /// Resolves the <c>.dds</c> spelling of a texture reference when the reference names
    /// another extension.
    /// </summary>
    /// <returns>
    /// The DDS outcome when it is resolved or rejected, or <see langword="null"/> when
    /// the DDS spelling does not exist and the caller should try the declared name.
    /// </returns>
    private static SceneryPathResult? TryDdsVariant(
        string contentRoot,
        string directory,
        string reference)
    {
        string extension = Path.GetExtension(reference);

        if (string.IsNullOrEmpty(extension) ||
            extension.Equals(".dds", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string preferred = Path.ChangeExtension(reference, ".dds");

        if (string.Equals(preferred, reference, StringComparison.Ordinal))
        {
            return null;
        }

        SceneryPathResult resolution = SceneryPath.ResolveRelative(
            contentRoot,
            directory,
            preferred);

        return resolution.IsResolved || resolution.Status == SceneryPathStatus.Rejected
            ? resolution
            : null;
    }

    /// <summary>
    /// Tests whether a mesh reference has a file extension this build can decode.
    /// </summary>
    private static bool IsSupportedMeshReference(string reference)
    {
        return SupportedMeshExtensions.Contains(
            Path.GetExtension(reference),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads one configuration file into keyword blocks, with switched-off lines
    /// removed.
    /// </summary>
    private static IReadOnlyList<KeywordBlock> ReadBlocks(
        string path,
        SceneryDiagnostics diagnostics,
        out bool failed)
    {
        failed = false;

        ConfigReadResult read = ConfigTextReader.Read(path, MaxConfigBytes, MaxConfigLines);

        diagnostics.ReportRange(read.Diagnostics);

        if (read.Text is null)
        {
            failed = true;

            if (read.FatalError is not null)
            {
                diagnostics.Report(read.FatalError);
            }

            return [];
        }

        ConfigText filtered = RemoveSwitchedOffLines(read.Text);

        var blockDiagnostics = new List<MapDiagnostic>();
        IReadOnlyList<KeywordBlock> blocks = KeywordBlockReader.Read(
            filtered,
            path,
            blockDiagnostics);

        diagnostics.ReportRange(blockDiagnostics);

        return blocks;
    }

    /// <summary>
    /// Drops the lines between the documented switch-off markers and the whole-line
    /// comments, keeping physical line numbers intact for diagnostics.
    /// </summary>
    private static ConfigText RemoveSwitchedOffLines(ConfigText text)
    {
        var lines = new List<ConfigLine>(text.Lines.Count);
        bool disabled = false;

        foreach (ConfigLine line in text.Lines)
        {
            string trimmed = line.Text.TrimStart();

            if (trimmed.StartsWith(SwitchOffMarker, StringComparison.OrdinalIgnoreCase))
            {
                disabled = true;
                continue;
            }

            if (trimmed.StartsWith(SwitchOnMarker, StringComparison.OrdinalIgnoreCase))
            {
                disabled = false;
                continue;
            }

            if (disabled || trimmed.StartsWith('\''))
            {
                continue;
            }

            lines.Add(line);
        }

        return new ConfigText(text.EncodingName, lines);
    }

    private static bool IsHandledKeyword(string keyword)
    {
        return keyword.Equals("model", StringComparison.OrdinalIgnoreCase) ||
            keyword.Equals("mesh", StringComparison.OrdinalIgnoreCase) ||
            keyword.Equals("matl", StringComparison.OrdinalIgnoreCase) ||
            keyword.Equals("absheight", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasBlock(IReadOnlyList<KeywordBlock> blocks, string keyword)
    {
        return TryFindBlock(blocks, keyword, out _);
    }

    private static bool TryFindBlock(
        IReadOnlyList<KeywordBlock> blocks,
        string keyword,
        out KeywordBlock found)
    {
        foreach (KeywordBlock block in blocks)
        {
            if (block.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase))
            {
                found = block;
                return true;
            }
        }

        found = default;
        return false;
    }

    /// <summary>
    /// Returns the first parameter line of a block that is neither empty nor a
    /// comment.
    /// </summary>
    private static string? FirstParameter(KeywordBlock block)
    {
        foreach (ConfigLine parameter in block.Parameters)
        {
            string trimmed = parameter.Text.Trim();

            if (trimmed.Length == 0 || trimmed.StartsWith('\''))
            {
                continue;
            }

            return trimmed;
        }

        return null;
    }

    /// <summary>
    /// Tests whether a <c>[model]</c> parameter names another configuration file.
    /// </summary>
    /// <remarks>
    /// A configuration reference ends in <c>.cfg</c> or <c>.sco</c>. A bare mesh path
    /// with a decodable extension is accepted as an inline model reference instead,
    /// because the documented grammar permits both forms and this reader must not
    /// invent which one an object uses.
    /// </remarks>
    private static bool LooksLikeConfigurationFile(string reference)
    {
        string extension = Path.GetExtension(reference);

        return extension.Equals(".cfg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".sco", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Records unsupported keywords once, in first-seen order.
    /// </summary>
    private sealed class OrderedKeywordSet
    {
        private readonly List<string> _names = [];
        private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

        internal int Count { get; private set; }

        internal void Add(string keyword)
        {
            Count++;

            if (_seen.Add(keyword))
            {
                _names.Add(keyword);
            }
        }

        internal IReadOnlyList<string> Names(int maximum)
        {
            return _names.Count <= maximum ? _names : [.. _names.Take(maximum)];
        }
    }
}

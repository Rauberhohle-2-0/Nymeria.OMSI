using System.Globalization;
using Nymeria.Omsi.Maps;

namespace Nymeria.Omsi.Scenery;

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
/// Interpreted keywords are <c>[model]</c> (a further configuration file, or inline
/// model sections), <c>[mesh]</c> (a mesh file), <c>[matl]</c> (a texture name used by a
/// mesh subset), and <c>[absheight]</c> (a standalone flag meaning the placement
/// height is absolute). Everything else is counted, named, and left uninterpreted:
/// scripts, animations, interaction, collision, and advanced material effects are out
/// of scope, and no meaning is invented for a keyword this reader does not know.
/// </para>
/// <para>
/// Mesh and texture files are resolved while parsing, under the same
/// traversal-safe rules as <see cref="SceneryPath"/>. A missing file is a diagnostic,
/// not an exception, so one damaged object cannot stop the map from loading.
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

        var diagnostics = new List<MapDiagnostic>();

        IReadOnlyList<KeywordBlock> blocks = ReadBlocks(
            scoPath,
            diagnostics,
            out bool readFailed);

        if (readFailed)
        {
            return new SceneryParseResult(null, diagnostics);
        }

        if (blocks.Count == 0)
        {
            diagnostics.Add(new MapDiagnostic(
                MapDiagnosticSeverity.Warning,
                "The scenery definition has no [keyword] blocks, so it describes no " +
                "model.",
                scoPath));

            return new SceneryParseResult(null, diagnostics);
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

        string assetDirectory = Path.GetDirectoryName(scoPath) ?? contentRoot;
        string? modelConfigPath = null;
        bool usesExternalModelConfig = false;

        if (TryFindBlock(blocks, "model", out KeywordBlock modelBlock))
        {
            string? modelReference = FirstParameter(modelBlock);

            if (modelReference is null)
            {
                diagnostics.Add(new MapDiagnostic(
                    MapDiagnosticSeverity.Warning,
                    "The [model] block has no parameter, so no model file is named.",
                    scoPath,
                    modelBlock.LineNumber));
            }
            else if (LooksLikeConfigurationFile(modelReference))
            {
                SceneryPathResult resolution = SceneryPath.Resolve(
                    assetDirectory,
                    modelReference);

                if (resolution.IsResolved)
                {
                    modelConfigPath = resolution.Path;
                    usesExternalModelConfig = true;
                    assetDirectory = Path.GetDirectoryName(resolution.Path!) ?? assetDirectory;
                }
                else
                {
                    diagnostics.Add(new MapDiagnostic(
                        MapDiagnosticSeverity.Error,
                        "The [model] reference '" + modelReference +
                        "' could not be resolved: " + (resolution.Error ?? "unknown reason") +
                        " The definition names a separate model configuration, so no " +
                        "geometry is available without it.",
                        scoPath,
                        modelBlock.LineNumber));
                }
            }
            else
            {
                diagnostics.Add(new MapDiagnostic(
                    MapDiagnosticSeverity.Warning,
                    "The [model] parameter '" + modelReference + "' is neither an " +
                    "existing model configuration nor a mesh path, so it is not " +
                    "interpreted.",
                    scoPath,
                    modelBlock.LineNumber));
            }
        }
        else
        {
            diagnostics.Add(new MapDiagnostic(
                MapDiagnosticSeverity.Warning,
                "The definition has no [model] block. Inline [mesh] blocks are used " +
                "when present.",
                scoPath));
        }

        IReadOnlyList<KeywordBlock> meshBlocks = blocks;

        if (!usesExternalModelConfig && !HasBlock(blocks, "mesh"))
        {
            diagnostics.Add(new MapDiagnostic(
                MapDiagnosticSeverity.Error,
                "The definition names no model configuration and has no inline [mesh] " +
                "block, so it describes no geometry.",
                scoPath));

            return new SceneryParseResult(null, diagnostics);
        }

        if (usesExternalModelConfig)
        {
            meshBlocks = ReadBlocks(
                modelConfigPath!,
                diagnostics,
                out readFailed);

            if (readFailed)
            {
                return new SceneryParseResult(null, diagnostics);
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

        foreach (KeywordBlock block in meshBlocks)
        {
            if (block.Keyword.Equals("mesh", StringComparison.OrdinalIgnoreCase))
            {
                string? meshReference = FirstParameter(block);

                if (meshReference is null)
                {
                    diagnostics.Add(new MapDiagnostic(
                        MapDiagnosticSeverity.Warning,
                        "The [mesh] block has no parameter, so no mesh is named.",
                        scoPath,
                        block.LineNumber));
                    continue;
                }

                if (meshes.Count >= MaxMeshes)
                {
                    skippedMeshes++;
                    continue;
                }

                meshes.Add(ResolveMesh(
                    contentRoot,
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
                    assetDirectory,
                    textureReference));
            }
        }

        if (skippedMeshes > 0)
        {
            diagnostics.Add(new MapDiagnostic(
                MapDiagnosticSeverity.Warning,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} [mesh] block(s) beyond the {1}-mesh limit were not read.",
                    skippedMeshes,
                    MaxMeshes),
                scoPath));
        }

        if (skippedTextures > 0)
        {
            diagnostics.Add(new MapDiagnostic(
                MapDiagnosticSeverity.Warning,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} [matl] block(s) beyond the {1}-texture limit were not read.",
                    skippedTextures,
                    MaxTextures),
                scoPath));
        }

        if (meshes.Count == 0)
        {
            diagnostics.Add(new MapDiagnostic(
                MapDiagnosticSeverity.Error,
                "No [mesh] block named a mesh file, so the definition describes no " +
                "geometry.",
                scoPath));

            return new SceneryParseResult(null, diagnostics);
        }

        var definition = new SceneryDefinition(
            reference,
            scoPath,
            assetDirectory,
            modelConfigPath,
            usesExternalModelConfig,
            usesAbsoluteHeight,
            meshes,
            textures,
            unsupported.Names(MaxReportedUnsupportedKeywords),
            unsupported.Count,
            diagnostics);

        return new SceneryParseResult(definition, diagnostics);
    }

    /// <summary>
    /// Resolves one mesh reference against the model folder and then the content root.
    /// </summary>
    private static SceneryMeshReference ResolveMesh(
        string contentRoot,
        string assetDirectory,
        string reference,
        string sourcePath,
        int lineNumber,
        List<MapDiagnostic> diagnostics)
    {
        string extension = Path.GetExtension(reference);

        if (!SupportedMeshExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            string message =
                "The mesh '" + reference + "' has the extension '" + extension +
                "', which this build does not decode. Supported mesh extensions are " +
                string.Join(", ", SupportedMeshExtensions) + ".";

            diagnostics.Add(new MapDiagnostic(
                MapDiagnosticSeverity.Warning,
                message,
                sourcePath,
                lineNumber));

            return new SceneryMeshReference(reference, null, message);
        }

        SceneryPathResult resolution = ResolveAsset(
            contentRoot,
            assetDirectory,
            reference,
            extraFolder: null);

        if (!resolution.IsResolved)
        {
            string message =
                "The mesh '" + reference + "' could not be resolved: " +
                (resolution.Error ?? "unknown reason");

            diagnostics.Add(new MapDiagnostic(
                MapDiagnosticSeverity.Warning,
                message,
                sourcePath,
                lineNumber));
        }

        return new SceneryMeshReference(reference, resolution.Path, resolution.Error);
    }

    /// <summary>
    /// Resolves one texture reference. Textures are optional: a mesh subset with a
    /// missing texture still draws with its base color, so this records the failure
    /// without a diagnostic of its own.
    /// </summary>
    private static SceneryTextureReference ResolveTexture(
        string contentRoot,
        string assetDirectory,
        string reference)
    {
        SceneryPathResult resolution = ResolveAsset(
            contentRoot,
            assetDirectory,
            reference,
            extraFolder: "Texture");

        return new SceneryTextureReference(reference, resolution.Path, resolution.Error);
    }

    /// <summary>
    /// Tries the model folder, then the content root, then an optional shared folder
    /// under the content root. The first existing file wins.
    /// </summary>
    internal static SceneryPathResult ResolveAsset(
        string contentRoot,
        string assetDirectory,
        string reference,
        string? extraFolder)
    {
        SceneryPathResult local = SceneryPath.Resolve(assetDirectory, reference);

        if (local.IsResolved || local.Status == SceneryPathStatus.Rejected)
        {
            return local;
        }

        SceneryPathResult installation = SceneryPath.Resolve(contentRoot, reference);

        if (installation.IsResolved || installation.Status == SceneryPathStatus.Rejected)
        {
            return installation;
        }

        if (extraFolder is not null)
        {
            SceneryPathResult shared = SceneryPath.Resolve(
                Path.Combine(contentRoot, extraFolder),
                reference);

            if (shared.IsResolved)
            {
                return shared;
            }
        }

        // Report the model-folder failure, which is where OMSI looks first.
        return local;
    }

    /// <summary>
    /// Reads one configuration file into keyword blocks, with switched-off lines
    /// removed.
    /// </summary>
    private static IReadOnlyList<KeywordBlock> ReadBlocks(
        string path,
        List<MapDiagnostic> diagnostics,
        out bool failed)
    {
        failed = false;

        ConfigReadResult read = ConfigTextReader.Read(path, MaxConfigBytes, MaxConfigLines);

        diagnostics.AddRange(read.Diagnostics);

        if (read.Text is null)
        {
            failed = true;

            if (read.FatalError is not null)
            {
                diagnostics.Add(read.FatalError);
            }

            return [];
        }

        ConfigText filtered = RemoveSwitchedOffLines(read.Text);

        return KeywordBlockReader.Read(filtered, path, diagnostics);
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

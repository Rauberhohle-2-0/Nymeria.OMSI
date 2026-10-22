using Nymeria.Omsi.Maps;

namespace Nymeria.Omsi.Scenery;

/// <summary>
/// One mesh reference a scenery definition resolves to.
/// </summary>
/// <param name="Reference">The reference exactly as written in the source file.</param>
/// <param name="Path">The resolved file, or <see langword="null"/> when it is absent.</param>
/// <param name="Error">A readable explanation when no file was resolved.</param>
internal readonly record struct SceneryMeshReference(
    string Reference,
    string? Path,
    string? Error)
{
    /// <summary>Gets a value indicating whether the mesh file exists.</summary>
    internal bool IsResolved => Path is not null;
}

/// <summary>
/// One <c>[matl]</c> texture reference a scenery definition declares.
/// </summary>
/// <param name="Reference">The texture name as written, without resolution attempts.</param>
/// <param name="Path">The resolved texture file, or <see langword="null"/>.</param>
/// <param name="Error">A readable explanation when no file was resolved.</param>
internal readonly record struct SceneryTextureReference(
    string Reference,
    string? Path,
    string? Error);

/// <summary>
/// A parsed OMSI scenery definition: the subset needed to load and place a static
/// object, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// A definition is CPU data produced before any window or GPU device exists. It holds
/// resolved absolute paths for the meshes and textures it names, because path
/// resolution is bounded filesystem work that belongs beside parsing, not inside the
/// render loop.
/// </para>
/// <para>
/// The <c>[model]</c> keyword is read in both forms the documented file grammar
/// permits: it either names a further configuration file (the usual
/// <c>model.cfg</c> indirection) or is followed by inline <c>[mesh]</c> and
/// <c>[matl]</c> blocks in the same file. Which form a real object uses is recorded in
/// <see cref="UsesExternalModelConfig"/>, so a Windows run can confirm the reading
/// from the loaded-mesh counts instead of trusting an assumption.
/// </para>
/// <para>
/// Scripts, animations, interaction, collision, seasonal behavior, and advanced
/// material effects are not interpreted. Every keyword this reader does not handle is
/// counted and named in <see cref="UnsupportedKeywords"/>.
/// </para>
/// </remarks>
internal sealed class SceneryDefinition
{
    internal SceneryDefinition(
        string reference,
        string scoPath,
        string assetDirectory,
        string? modelConfigPath,
        bool usesExternalModelConfig,
        bool usesAbsoluteHeight,
        IReadOnlyList<SceneryMeshReference> meshes,
        IReadOnlyList<SceneryTextureReference> textures,
        IReadOnlyList<string> unsupportedKeywords,
        int unsupportedKeywordCount,
        IReadOnlyList<MapDiagnostic> diagnostics)
    {
        Reference = reference;
        ScoPath = scoPath;
        AssetDirectory = assetDirectory;
        ModelConfigPath = modelConfigPath;
        UsesExternalModelConfig = usesExternalModelConfig;
        UsesAbsoluteHeight = usesAbsoluteHeight;
        Meshes = [.. meshes];
        Textures = [.. textures];
        UnsupportedKeywords = [.. unsupportedKeywords];
        UnsupportedKeywordCount = unsupportedKeywordCount;
        Diagnostics = [.. diagnostics];
    }

    /// <summary>Gets the reference as written in the tile file.</summary>
    internal string Reference { get; }

    /// <summary>Gets the resolved <c>.sco</c> file.</summary>
    internal string ScoPath { get; }

    /// <summary>
    /// Gets the folder that mesh and texture references are relative to: the model
    /// configuration's folder, or the <c>.sco</c> folder when the model is inline.
    /// </summary>
    internal string AssetDirectory { get; }

    /// <summary>
    /// Gets the resolved model configuration file, or <see langword="null"/> when the
    /// definition describes its model inline.
    /// </summary>
    internal string? ModelConfigPath { get; }

    /// <summary>
    /// Gets a value indicating whether the definition pointed at a separate model
    /// configuration file.
    /// </summary>
    internal bool UsesExternalModelConfig { get; }

    /// <summary>
    /// Gets a value indicating whether the definition declares <c>[absheight]</c>, so
    /// its placement <c>z</c> is an absolute height rather than terrain-relative.
    /// </summary>
    internal bool UsesAbsoluteHeight { get; }

    /// <summary>Gets the resolved mesh references, in declaration order.</summary>
    internal IReadOnlyList<SceneryMeshReference> Meshes { get; }

    /// <summary>Gets the declared <c>[matl]</c> texture references.</summary>
    internal IReadOnlyList<SceneryTextureReference> Textures { get; }

    /// <summary>
    /// Gets the distinct keywords that were not interpreted, in first-seen order.
    /// </summary>
    internal IReadOnlyList<string> UnsupportedKeywords { get; }

    /// <summary>Gets the number of occurrences of uninterpreted keywords.</summary>
    internal int UnsupportedKeywordCount { get; }

    /// <summary>Gets the parse warnings and errors, each with its line number.</summary>
    internal IReadOnlyList<MapDiagnostic> Diagnostics { get; }
}

/// <summary>
/// The outcome of parsing one scenery definition.
/// </summary>
/// <param name="Definition">The parsed definition, or <see langword="null"/> on failure.</param>
/// <param name="Diagnostics">Why parsing failed or what was not interpreted.</param>
internal sealed record SceneryParseResult(
    SceneryDefinition? Definition,
    IReadOnlyList<MapDiagnostic> Diagnostics);

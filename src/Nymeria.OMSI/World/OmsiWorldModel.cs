using Nymeria.Omsi.Maps;

namespace Nymeria.Omsi.World;

/// <summary>
/// One scenery-object placement read from a tile file.
/// </summary>
/// <remarks>
/// <para>
/// Every value is stored exactly as the tile file wrote it. No coordinate-system
/// conversion and no rotation convention is applied: heading, pitch, and bank are
/// the raw degrees from the file, and X/Y/Z keep the file's field order
/// (x, y, z, where the documented z is the height).
/// </para>
/// <para>
/// <see cref="ModelPath"/> is an unresolved source reference. It is never opened,
/// normalized, or resolved against the map folder in this milestone.
/// </para>
/// </remarks>
/// <param name="ModelPath">The object reference exactly as written in the tile file.</param>
/// <param name="IdCode">The record identifier, or null before tile version 6.</param>
/// <param name="DetailLevel">The leading detail level, or null before tile version 9.</param>
/// <param name="X">The raw x value.</param>
/// <param name="Y">The raw y value.</param>
/// <param name="Z">
/// The raw z value, documented as the height. Documented as terrain-relative for
/// ordinary objects and absolute for <c>[absheight]</c> objects and spline helpers;
/// neither case is resolved here.
/// </param>
/// <param name="HeadingDegrees">The raw heading in degrees.</param>
/// <param name="PitchDegrees">The raw pitch, or null before tile version 12.</param>
/// <param name="BankDegrees">The raw bank, or null before tile version 12.</param>
/// <param name="Labels">The object's label lines, verbatim, in file order.</param>
/// <param name="LineNumber">The one-based line of the <c>[object]</c> keyword.</param>
public sealed record OmsiObjectPlacement(
    string ModelPath,
    int? IdCode,
    int? DetailLevel,
    double X,
    double Y,
    double Z,
    double HeadingDegrees,
    double? PitchDegrees,
    double? BankDegrees,
    IReadOnlyList<string> Labels,
    int LineNumber);

/// <summary>
/// One spline placement read from a tile file.
/// </summary>
/// <remarks>
/// Values are raw source values; no geometry, tessellation, or frame conversion is
/// performed. <see cref="ModelPath"/> is an unresolved reference. The documented
/// trailing fields (gradients, cant, skew, texture offset, alignment length, optional
/// mirror) are left unconsumed and are not interpreted.
/// </remarks>
/// <param name="ModelPath">The spline reference exactly as written in the tile file.</param>
/// <param name="IdCode">The record identifier, or null before tile version 6.</param>
/// <param name="PreviousId">The previous spline reference, or null when absent.</param>
/// <param name="NextId">The next spline reference; null before version 11 and when absent.</param>
/// <param name="X">The raw x value.</param>
/// <param name="Height">The raw height value, in the file's documented field order.</param>
/// <param name="Y">The raw y value.</param>
/// <param name="HeadingDegrees">The raw heading in degrees.</param>
/// <param name="Length">The raw length.</param>
/// <param name="Radius">The raw radius (0 = straight, &gt; 0 turns right).</param>
/// <param name="IsHeightSpline">Whether the source record was <c>[spline_h]</c>.</param>
/// <param name="LineNumber">The one-based line of the keyword.</param>
public sealed record OmsiSplinePlacement(
    string ModelPath,
    int? IdCode,
    int? PreviousId,
    int? NextId,
    double X,
    double Height,
    double Y,
    double HeadingDegrees,
    double Length,
    double Radius,
    bool IsHeightSpline,
    int LineNumber);

/// <summary>
/// Metadata about a tile's terrain sidecar file.
/// </summary>
/// <remarks>
/// Only existence and size are inspected. The documented height grid is not
/// decoded, and nothing is written. The expected size is the documented
/// <c>u32 (=60) + 61 x 61 f32</c> layout, 14,888 bytes.
/// </remarks>
/// <param name="FilePath">The sidecar path derived from the tile file path.</param>
/// <param name="Length">The file length in bytes.</param>
/// <param name="HasDocumentedSize">Whether the length matches the documented layout.</param>
public sealed record OmsiTerrainReference(
    string FilePath,
    long Length,
    bool HasDocumentedSize);

/// <summary>
/// A tile-file record type that was recognized but not interpreted.
/// </summary>
/// <param name="Keyword">The keyword as spelled in the file.</param>
/// <param name="Count">How many times it occurs in the tile.</param>
/// <param name="FirstLineNumber">The one-based line of its first occurrence.</param>
public sealed record OmsiUnsupportedTileRecord(
    string Keyword,
    int Count,
    int FirstLineNumber);

/// <summary>
/// One parsed tile and its world placements.
/// </summary>
/// <remarks>
/// <see cref="X"/> and <see cref="Y"/> come from the discovered file name, not from
/// the tile contents. <see cref="IsParsed"/> is true when the text was decoded and
/// the tile declared a usable leading <c>[version]</c>; it does not mean the tile was
/// rendered, validated against OMSI, or that its references exist on disk.
/// </remarks>
public sealed class OmsiWorldTile
{
    internal OmsiWorldTile(
        int x,
        int y,
        string filePath,
        int? version,
        bool isParsed,
        IReadOnlyList<OmsiObjectPlacement> objects,
        IReadOnlyList<OmsiSplinePlacement> splines,
        OmsiTerrainReference? terrain,
        IReadOnlyList<OmsiUnsupportedTileRecord> unsupportedRecords,
        IReadOnlyList<MapDiagnostic> diagnostics)
    {
        X = x;
        Y = y;
        FilePath = filePath;
        Version = version;
        IsParsed = isParsed;
        Objects = [.. objects];
        Splines = [.. splines];
        Terrain = terrain;
        UnsupportedRecords = [.. unsupportedRecords];
        Diagnostics = [.. diagnostics];
    }

    /// <summary>Gets the tile column taken from the discovered file name.</summary>
    public int X { get; }

    /// <summary>Gets the tile row taken from the discovered file name.</summary>
    public int Y { get; }

    /// <summary>Gets the full path of the tile file.</summary>
    public string FilePath { get; }

    /// <summary>
    /// Gets the tile's declared <c>[version]</c>, or <see langword="null"/> when it
    /// was missing or not a valid integer.
    /// </summary>
    public int? Version { get; }

    /// <summary>
    /// Gets a value indicating whether the tile text was decoded and its record
    /// layout is supported by the declared version.
    /// </summary>
    public bool IsParsed { get; }

    /// <summary>Gets the object placements in file order.</summary>
    public IReadOnlyList<OmsiObjectPlacement> Objects { get; }

    /// <summary>Gets the spline placements in file order.</summary>
    public IReadOnlyList<OmsiSplinePlacement> Splines { get; }

    /// <summary>Gets the terrain sidecar metadata, or null when no sidecar exists.</summary>
    public OmsiTerrainReference? Terrain { get; }

    /// <summary>Gets the aggregated record types that were not interpreted.</summary>
    public IReadOnlyList<OmsiUnsupportedTileRecord> UnsupportedRecords { get; }

    /// <summary>Gets this tile's warnings and errors.</summary>
    public IReadOnlyList<MapDiagnostic> Diagnostics { get; }
}

/// <summary>
/// A read-only, platform-neutral description of a map's tiles and their world
/// placements, produced by <see cref="OmsiWorldLoader"/>.
/// </summary>
/// <remarks>
/// This is parser output, not a scene. It holds no engine type, no GPU resource,
/// no SDL3 state, and no mesh, material, or texture. Turning placements into a
/// Nymeria scene is a separate, not-yet-approved task. A successful parse means
/// the documented record layout was understood; it does not mean the map is
/// renderable or that any referenced asset exists.
/// </remarks>
public sealed class OmsiWorldModel
{
    internal OmsiWorldModel(
        string mapRoot,
        string? globalConfigPath,
        string? name,
        string? friendlyName,
        bool usesWorldCoordinates,
        IReadOnlyList<OmsiWorldTile> tiles,
        IReadOnlyList<MapDiagnostic> diagnostics,
        MapScanStatus status)
    {
        MapRoot = mapRoot;
        GlobalConfigPath = globalConfigPath;
        Name = name;
        FriendlyName = friendlyName;
        UsesWorldCoordinates = usesWorldCoordinates;
        Tiles = [.. tiles];
        Diagnostics = [.. diagnostics];
        Status = status;
    }

    /// <summary>Gets the resolved full path of the scanned map folder.</summary>
    public string MapRoot { get; }

    /// <summary>Gets the full path of <c>global.cfg</c>, or null when it was not read.</summary>
    public string? GlobalConfigPath { get; }

    /// <summary>Gets the map's <c>[name]</c> value, or null when unavailable.</summary>
    public string? Name { get; }

    /// <summary>Gets the map's <c>[friendlyname]</c> value, or null when unavailable.</summary>
    public string? FriendlyName { get; }

    /// <summary>
    /// Gets a value indicating whether <c>global.cfg</c> declared
    /// <c>[worldcoordinates]</c>, which the reference documents as switching the tile
    /// grid from 300 m tiles to a 1/300-degree grid.
    /// </summary>
    /// <remarks>
    /// This is carried through from the manifest because the grid decides how tile
    /// coordinates map to world distances. It does not mean the conversion is
    /// verified; see docs/OMSI_RENDERING.md.
    /// </remarks>
    public bool UsesWorldCoordinates { get; }

    /// <summary>Gets the discovered tiles in manifest order.</summary>
    public IReadOnlyList<OmsiWorldTile> Tiles { get; }

    /// <summary>
    /// Gets the map-level and tile-level warnings and errors, in discovery and then
    /// tile order.
    /// </summary>
    public IReadOnlyList<MapDiagnostic> Diagnostics { get; }

    /// <summary>
    /// Gets the parse status.
    /// </summary>
    /// <remarks>
    /// <see cref="MapScanStatus.Complete"/> means every discovered tile declared a
    /// supported <c>[version]</c> and no diagnostic was raised.
    /// <see cref="MapScanStatus.Partial"/> means at least one warning was raised or at
    /// least one tile stayed uninterpreted.
    /// <see cref="MapScanStatus.Failed"/> means discovery failed, a diagnostic was an
    /// error, or not one discovered tile could be interpreted, so the model carries
    /// no usable world data.
    /// <para>
    /// Uninterpreted tile record types are preserved as
    /// <see cref="OmsiWorldTile.UnsupportedRecords"/> but do not lower the status: the
    /// documented keyword list is much wider than the records interpreted here, so
    /// every real tile is expected to carry some.
    /// </para>
    /// </remarks>
    public MapScanStatus Status { get; }

    /// <summary>Gets how many discovered tiles were parsed.</summary>
    public int ParsedTileCount => Tiles.Count(tile => tile.IsParsed);

    /// <summary>Gets the total number of object placements across parsed tiles.</summary>
    public int ObjectPlacementCount => Tiles.Sum(tile => tile.Objects.Count);

    /// <summary>Gets the total number of spline placements across parsed tiles.</summary>
    public int SplinePlacementCount => Tiles.Sum(tile => tile.Splines.Count);
}

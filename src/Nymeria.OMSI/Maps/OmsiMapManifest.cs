using System.Globalization;

namespace Nymeria.Omsi.Maps;

/// <summary>
/// How far the metadata scan got.
/// </summary>
public enum MapScanStatus
{
    /// <summary>
    /// The configuration and the tile inventory were read without warnings.
    /// </summary>
    Complete,

    /// <summary>
    /// A usable manifest exists, but at least one warning means it is not
    /// complete. Warnings name the file and line they came from.
    /// </summary>
    Partial,

    /// <summary>
    /// No trustworthy manifest could be produced. The diagnostics explain why.
    /// </summary>
    Failed,
}

/// <summary>
/// One discovered tile configuration file.
/// </summary>
/// <param name="X">The tile column parsed from the file name.</param>
/// <param name="Y">The tile row parsed from the file name.</param>
/// <param name="FileName">The file name as it exists on disk.</param>
/// <param name="FilePath">The full path of the file.</param>
/// <param name="IsReadable">
/// Whether the file could be opened for reading. Discovery never parses tile
/// contents in this milestone.
/// </param>
public sealed record OmsiMapTile(
    int X,
    int Y,
    string FileName,
    string FilePath,
    bool IsReadable)
{
    /// <summary>
    /// Gets the coordinates in the documented <c>(x,y)</c> spelling, invariant of
    /// the current culture.
    /// </summary>
    public string Coordinates =>
        string.Create(CultureInfo.InvariantCulture, $"({X},{Y})");
}

/// <summary>
/// A <c>global.cfg</c> directive that was recognized as a block but deliberately
/// not interpreted, such as <c>[entrypoints]</c> or <c>[map]</c>.
/// </summary>
/// <param name="Keyword">The keyword inside the brackets, as spelled in the file.</param>
/// <param name="LineNumber">The one-based line of the keyword.</param>
public sealed record OmsiDirectiveRecord(string Keyword, int LineNumber);

/// <summary>
/// A read-only description of an OMSI map folder: its configuration metadata and
/// its tile inventory.
/// </summary>
/// <remarks>
/// <para>
/// The manifest intentionally holds only what the scanner can support from
/// documented behavior. It is metadata, not a scene: nothing here contains tile
/// contents, terrain, objects, splines, textures, vehicles, or scripts, and no
/// rendering type is referenced.
/// </para>
/// <para>
/// Instances are created by <see cref="OmsiMapDiscovery.Scan"/>. A manifest can
/// describe a failed scan, in which case <see cref="Status"/> is
/// <see cref="MapScanStatus.Failed"/> and <see cref="Diagnostics"/> explains what
/// prevented a trustworthy result.
/// </para>
/// </remarks>
public sealed class OmsiMapManifest
{
    internal OmsiMapManifest(
        string mapRoot,
        string? globalConfigPath,
        string? textEncodingName,
        string? name,
        string? friendlyName,
        bool usesWorldCoordinates,
        IReadOnlyList<OmsiMapTile> tiles,
        IReadOnlyList<OmsiDirectiveRecord> unsupportedDirectives,
        IReadOnlyList<MapDiagnostic> diagnostics,
        MapScanStatus status)
    {
        MapRoot = mapRoot;
        GlobalConfigPath = globalConfigPath;
        TextEncodingName = textEncodingName;
        Name = name;
        FriendlyName = friendlyName;
        UsesWorldCoordinates = usesWorldCoordinates;
        // Snapshot the collections so the public manifest cannot be mutated
        // through a caller's list reference.
        Tiles = [.. tiles];
        UnsupportedDirectives = [.. unsupportedDirectives];
        Diagnostics = [.. diagnostics];
        Status = status;
    }

    /// <summary>Gets the resolved, full path of the map folder that was scanned.</summary>
    public string MapRoot { get; }

    /// <summary>
    /// Gets the full path of the map's <c>global.cfg</c>, or <see langword="null"/>
    /// when it was not found or not readable.
    /// </summary>
    public string? GlobalConfigPath { get; }

    /// <summary>
    /// Gets a readable name for the encoding used to decode <c>global.cfg</c>, or
    /// <see langword="null"/> when the file was not decoded.
    /// </summary>
    public string? TextEncodingName { get; }

    /// <summary>
    /// Gets the map's <c>[name]</c> value, or <see langword="null"/> when the
    /// directive is absent or has no usable value.
    /// </summary>
    public string? Name { get; }

    /// <summary>
    /// Gets the map's <c>[friendlyname]</c> value, or <see langword="null"/> when
    /// the directive is absent or has no usable value.
    /// </summary>
    public string? FriendlyName { get; }

    /// <summary>
    /// Gets a value indicating whether the map declares <c>[worldcoordinates]</c>,
    /// which the documentation describes as switching the tile grid from metres to
    /// degrees.
    /// </summary>
    public bool UsesWorldCoordinates { get; }

    /// <summary>
    /// Gets the discovered tiles ordered by column, then row, then file name. The
    /// list is empty for a failed scan.
    /// </summary>
    public IReadOnlyList<OmsiMapTile> Tiles { get; }

    /// <summary>
    /// Gets the <c>global.cfg</c> blocks that were recognized but not interpreted,
    /// in file order.
    /// </summary>
    public IReadOnlyList<OmsiDirectiveRecord> UnsupportedDirectives { get; }

    /// <summary>
    /// Gets every warning and error, in discovery order.
    /// </summary>
    public IReadOnlyList<MapDiagnostic> Diagnostics { get; }

    /// <summary>
    /// Gets how far the scan got. Only
    /// <see cref="MapScanStatus.Complete"/> means the manifest is complete.
    /// </summary>
    public MapScanStatus Status { get; }
}

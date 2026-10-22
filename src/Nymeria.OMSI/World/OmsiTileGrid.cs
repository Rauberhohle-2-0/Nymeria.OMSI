namespace Nymeria.Omsi.World;

/// <summary>
/// The horizontal tile grid of an OMSI map: how long one tile edge is.
/// </summary>
/// <remarks>
/// <para>
/// The reference documents a 300 m tile edge for a metre map. It also documents
/// that <c>[worldcoordinates]</c> in <c>global.cfg</c> switches the grid to
/// 1/300 degree, and reports the edge of that grid as a measurement of 371.9 m on
/// both axes rather than as a constant.
/// </para>
/// <para>
/// Both layers that place geometry in the world read the edge from here, so the
/// diagnostic plan and the terrain mesh can never disagree about it. A caller that
/// reports <see cref="WorldCoordinateTileEdgeMeters"/> must still report that it is
/// a measurement and not a documented constant.
/// </para>
/// </remarks>
internal static class OmsiTileGrid
{
    /// <summary>The documented tile edge of a metre map.</summary>
    internal const double DocumentedTileEdgeMeters = 300.0;

    /// <summary>
    /// The reference-measured tile edge of a <c>[worldcoordinates]</c> map
    /// (1/300 degree, 371.9 m on both axes). It is unverified against real files.
    /// </summary>
    internal const double WorldCoordinateTileEdgeMeters = 371.9;

    /// <summary>
    /// Resolves the tile edge in metres for a map.
    /// </summary>
    /// <param name="usesWorldCoordinates">
    /// Whether <c>global.cfg</c> declared <c>[worldcoordinates]</c>.
    /// </param>
    internal static double EdgeMeters(bool usesWorldCoordinates)
    {
        return usesWorldCoordinates
            ? WorldCoordinateTileEdgeMeters
            : DocumentedTileEdgeMeters;
    }

    /// <summary>
    /// Reports whether the edge is the documented 300 m constant rather than the
    /// measured degree grid.
    /// </summary>
    /// <param name="usesWorldCoordinates">
    /// Whether <c>global.cfg</c> declared <c>[worldcoordinates]</c>.
    /// </param>
    internal static bool UsesDocumentedEdge(bool usesWorldCoordinates)
    {
        return !usesWorldCoordinates;
    }
}

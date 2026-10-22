using Nymeria.Omsi.World;

namespace Nymeria.Omsi.Rendering;

/// <summary>
/// One horizontal position in OMSI's own map plane, in metres.
/// </summary>
/// <remarks>
/// The plan uses OMSI's x and y axes as the parser read them. No compass
/// orientation is implied: the reference documents that a heading is measured
/// clockwise from north but does not document which of x and y points north, so
/// this milestone does not assert one.
/// </remarks>
/// <param name="X">The OMSI x coordinate in metres.</param>
/// <param name="Y">The OMSI y coordinate in metres.</param>
internal readonly record struct PlanPoint(double X, double Y);

/// <summary>
/// An axis-aligned rectangle in OMSI's map plane, in metres.
/// </summary>
/// <param name="MinX">The smaller x edge.</param>
/// <param name="MinY">The smaller y edge.</param>
/// <param name="MaxX">The larger x edge.</param>
/// <param name="MaxY">The larger y edge.</param>
internal readonly record struct PlanRect(double MinX, double MinY, double MaxX, double MaxY)
{
    internal double Width => MaxX - MinX;

    internal double Height => MaxY - MinY;
}

/// <summary>
/// The extent of a plan in OMSI's map plane, in metres.
/// </summary>
/// <param name="MinX">The smallest x coordinate.</param>
/// <param name="MinY">The smallest y coordinate.</param>
/// <param name="MaxX">The largest x coordinate.</param>
/// <param name="MaxY">The largest y coordinate.</param>
internal readonly record struct PlanBounds(double MinX, double MinY, double MaxX, double MaxY)
{
    internal double Width => MaxX - MinX;

    internal double Height => MaxY - MinY;

    /// <summary>
    /// Gets a bounds that contains everything known about a map, or a one-metre
    /// square around the origin when the map places nothing.
    /// </summary>
    internal static PlanBounds FromUnion(PlanBounds? accumulated)
    {
        if (accumulated is not PlanBounds bounds ||
            !double.IsFinite(bounds.MinX) ||
            !double.IsFinite(bounds.MinY) ||
            !double.IsFinite(bounds.MaxX) ||
            !double.IsFinite(bounds.MaxY))
        {
            return new PlanBounds(-0.5, -0.5, 0.5, 0.5);
        }

        return bounds;
    }
}

/// <summary>
/// The plan-view geometry that can be derived truthfully from a parsed world.
/// </summary>
/// <remarks>
/// <para>
/// Only two things are derived, and both come from documented facts:
/// </para>
/// <list type="bullet">
///   <item><description>
///   A tile occupies a square of <see cref="TileEdgeMeters"/> whose origin is the
///   tile column and row times that edge. The reference documents 300 m for a
///   metre map, and for a <c>[worldcoordinates]</c> map a 1/300-degree grid with a
///   measured edge of 371.9 m.
///   </description></item>
///   <item><description>
///   A placement's position is its tile origin plus the x and y the tile file
///   wrote. The reference documents that convention (tile column and row times the
///   edge plus the place within the tile).
///   </description></item>
/// </list>
/// <para>
/// Nothing else is invented: there is no terrain surface, no object mesh, no spline
/// curve, and no height. This is a diagnostic plan of parsed records, not a render
/// of OMSI scenery.
/// </para>
/// </remarks>
internal sealed class OmsiMapPlan
{
    /// <summary>The documented tile edge of a metre map.</summary>
    internal const double DocumentedTileEdgeMeters = 300.0;

    /// <summary>
    /// The tile edge the reference measured for a <c>[worldcoordinates]</c> map
    /// (1/300 degree, 371.9 m on both axes). It is a measurement, not a documented
    /// constant, so it is reported as unverified.
    /// </summary>
    internal const double WorldCoordinateTileEdgeMeters = 371.9;

    /// <summary>
    /// The largest number of points drawn per category. A malformed or enormous map
    /// cannot make the plan allocate without bound; the omitted count is reported.
    /// </summary>
    internal const int MaxPointsPerCategory = 250_000;

    /// <summary>
    /// The largest magnitude, in metres, accepted for a derived coordinate. It is
    /// far beyond any real map and catches a nonsensical tile index or placement.
    /// </summary>
    internal const double CoordinateLimitMeters = 1_000_000.0;

    private OmsiMapPlan(
        double tileEdgeMeters,
        bool hasDocumentedTileEdge,
        IReadOnlyList<PlanRect> tiles,
        IReadOnlyList<PlanPoint> objects,
        IReadOnlyList<PlanPoint> splines,
        PlanBounds bounds,
        int skippedObjects,
        int skippedSplines,
        int omittedObjects,
        int omittedSplines,
        int unparsedTiles,
        int terrainSidecars,
        int uninterpretedRecordTypes)
    {
        TileEdgeMeters = tileEdgeMeters;
        HasDocumentedTileEdge = hasDocumentedTileEdge;
        Tiles = [.. tiles];
        Objects = [.. objects];
        Splines = [.. splines];
        Bounds = bounds;
        SkippedObjects = skippedObjects;
        SkippedSplines = skippedSplines;
        OmittedObjects = omittedObjects;
        OmittedSplines = omittedSplines;
        UnparsedTiles = unparsedTiles;
        TerrainSidecars = terrainSidecars;
        UninterpretedRecordTypes = uninterpretedRecordTypes;
    }

    /// <summary>Gets the tile edge in metres used to place tiles.</summary>
    internal double TileEdgeMeters { get; }

    /// <summary>
    /// Gets a value indicating whether <see cref="TileEdgeMeters"/> is the
    /// documented 300 m constant rather than the reference-measured degree grid.
    /// </summary>
    internal bool HasDocumentedTileEdge { get; }

    /// <summary>Gets the tile squares in manifest order.</summary>
    internal IReadOnlyList<PlanRect> Tiles { get; }

    /// <summary>Gets the object positions in tile and record order.</summary>
    internal IReadOnlyList<PlanPoint> Objects { get; }

    /// <summary>Gets the spline positions in tile and record order.</summary>
    internal IReadOnlyList<PlanPoint> Splines { get; }

    /// <summary>Gets the extent that contains every tile and placement.</summary>
    internal PlanBounds Bounds { get; }

    /// <summary>Gets how many object placements had an unusable position.</summary>
    internal int SkippedObjects { get; }

    /// <summary>Gets how many spline placements had an unusable position.</summary>
    internal int SkippedSplines { get; }

    /// <summary>Gets how many object placements were dropped by the point cap.</summary>
    internal int OmittedObjects { get; }

    /// <summary>Gets how many spline placements were dropped by the point cap.</summary>
    internal int OmittedSplines { get; }

    /// <summary>Gets how many discovered tiles could not be interpreted.</summary>
    internal int UnparsedTiles { get; }

    /// <summary>Gets how many terrain sidecars exist but are not decoded.</summary>
    internal int TerrainSidecars { get; }

    /// <summary>Gets how many distinct tile record types were left uninterpreted.</summary>
    internal int UninterpretedRecordTypes { get; }

    /// <summary>
    /// Derives the plan geometry of a parsed world.
    /// </summary>
    /// <param name="world">The world model produced by the tile parser.</param>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> is null.</exception>
    internal static OmsiMapPlan Create(OmsiWorldModel world)
    {
        ArgumentNullException.ThrowIfNull(world);

        double edge = world.UsesWorldCoordinates
            ? WorldCoordinateTileEdgeMeters
            : DocumentedTileEdgeMeters;

        var tiles = new List<PlanRect>(world.Tiles.Count);
        var objects = new List<PlanPoint>();
        var splines = new List<PlanPoint>();
        int skippedObjects = 0;
        int skippedSplines = 0;
        int omittedObjects = 0;
        int omittedSplines = 0;
        int unparsedTiles = 0;
        int terrainSidecars = 0;

        var recordTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        PlanBounds? bounds = null;

        foreach (OmsiWorldTile tile in world.Tiles)
        {
            if (tile.Terrain is not null)
            {
                terrainSidecars++;
            }

            foreach (OmsiUnsupportedTileRecord record in tile.UnsupportedRecords)
            {
                recordTypes.Add(record.Keyword);
            }

            if (!tile.IsParsed)
            {
                unparsedTiles++;
            }

            if (!TryPlace(edge, tile.X, tile.Y, 0.0, 0.0, out double originX, out double originY))
            {
                continue;
            }

            var rect = new PlanRect(originX, originY, originX + edge, originY + edge);
            tiles.Add(rect);
            bounds = Union(bounds, rect.MinX, rect.MinY);
            bounds = Union(bounds, rect.MaxX, rect.MaxY);

            foreach (OmsiObjectPlacement placement in tile.Objects)
            {
                if (!TryPlace(
                        edge,
                        tile.X,
                        tile.Y,
                        placement.X,
                        placement.Y,
                        out double x,
                        out double y))
                {
                    skippedObjects++;
                    continue;
                }

                if (objects.Count >= MaxPointsPerCategory)
                {
                    omittedObjects++;
                    continue;
                }

                objects.Add(new PlanPoint(x, y));
                bounds = Union(bounds, x, y);
            }

            foreach (OmsiSplinePlacement placement in tile.Splines)
            {
                if (!TryPlace(
                        edge,
                        tile.X,
                        tile.Y,
                        placement.X,
                        placement.Y,
                        out double x,
                        out double y))
                {
                    skippedSplines++;
                    continue;
                }

                if (splines.Count >= MaxPointsPerCategory)
                {
                    omittedSplines++;
                    continue;
                }

                splines.Add(new PlanPoint(x, y));
                bounds = Union(bounds, x, y);
            }
        }

        return new OmsiMapPlan(
            edge,
            !world.UsesWorldCoordinates,
            tiles,
            objects,
            splines,
            PlanBounds.FromUnion(bounds),
            skippedObjects,
            skippedSplines,
            omittedObjects,
            omittedSplines,
            unparsedTiles,
            terrainSidecars,
            recordTypes.Count);
    }

    /// <summary>
    /// Places one position in the map plane, rejecting values that are not finite or
    /// that lie absurdly far from the origin.
    /// </summary>
    private static bool TryPlace(
        double tileEdgeMeters,
        int tileX,
        int tileY,
        double localX,
        double localY,
        out double x,
        out double y)
    {
        x = (tileX * tileEdgeMeters) + localX;
        y = (tileY * tileEdgeMeters) + localY;

        return double.IsFinite(x) &&
            double.IsFinite(y) &&
            Math.Abs(x) <= CoordinateLimitMeters &&
            Math.Abs(y) <= CoordinateLimitMeters;
    }

    private static PlanBounds? Union(PlanBounds? bounds, double x, double y)
    {
        if (bounds is not PlanBounds current)
        {
            return new PlanBounds(x, y, x, y);
        }

        return new PlanBounds(
            Math.Min(current.MinX, x),
            Math.Min(current.MinY, y),
            Math.Max(current.MaxX, x),
            Math.Max(current.MaxY, y));
    }
}

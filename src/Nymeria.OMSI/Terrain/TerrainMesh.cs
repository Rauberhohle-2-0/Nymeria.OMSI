namespace Nymeria.Omsi.Terrain;

/// <summary>
/// The triangle mesh of one decoded terrain tile.
/// </summary>
/// <remarks>
/// A mesh is built once from a tile's height grid and is never rebuilt per frame.
/// Vertices are in world space, so a mesh does not need a model matrix and adjacent
/// tiles line up by construction: two neighbouring tiles place their shared edge at
/// the same world coordinates and use the same heights.
/// </remarks>
internal sealed class TerrainMesh
{
    /// <summary>
    /// Creates a mesh from validated geometry.
    /// </summary>
    /// <param name="tileX">The tile column the geometry belongs to.</param>
    /// <param name="tileY">The tile row the geometry belongs to.</param>
    /// <param name="vertices">The world-space vertices, row-major over the grid.</param>
    /// <param name="indices">The triangle indices, three per triangle.</param>
    /// <param name="bounds">The world-space bounds of the vertices.</param>
    /// <param name="minHeight">The lowest height in the tile, in metres.</param>
    /// <param name="maxHeight">The highest height in the tile, in metres.</param>
    /// <exception cref="ArgumentException">The geometry is not usable.</exception>
    internal TerrainMesh(
        int tileX,
        int tileY,
        TerrainVertex[] vertices,
        uint[] indices,
        TerrainBounds bounds,
        float minHeight,
        float maxHeight)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);

        if (vertices.Length == 0)
        {
            throw new ArgumentException(
                "A terrain mesh needs at least one vertex.",
                nameof(vertices));
        }

        if (indices.Length == 0 || indices.Length % 3 != 0)
        {
            throw new ArgumentException(
                "Terrain indices must contain whole triangles.",
                nameof(indices));
        }

        TileX = tileX;
        TileY = tileY;
        Vertices = vertices;
        Indices = indices;
        Bounds = bounds;
        MinHeight = minHeight;
        MaxHeight = maxHeight;
    }

    /// <summary>Gets the tile column.</summary>
    internal int TileX { get; }

    /// <summary>Gets the tile row.</summary>
    internal int TileY { get; }

    /// <summary>Gets the world-space vertices.</summary>
    internal TerrainVertex[] Vertices { get; }

    /// <summary>Gets the triangle indices.</summary>
    internal uint[] Indices { get; }

    /// <summary>Gets the world-space bounds of this tile's vertices.</summary>
    internal TerrainBounds Bounds { get; }

    /// <summary>Gets the lowest height in this tile, in metres.</summary>
    internal float MinHeight { get; }

    /// <summary>Gets the highest height in this tile, in metres.</summary>
    internal float MaxHeight { get; }

    /// <summary>Gets the number of triangles.</summary>
    internal int TriangleCount => Indices.Length / 3;
}

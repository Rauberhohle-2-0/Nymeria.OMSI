namespace Nymeria.Omsi.Terrain;

/// <summary>
/// All decoded terrain of one map as one set of GPU-ready geometry.
/// </summary>
/// <remarks>
/// <para>
/// Per-tile meshes stay separate so each one's geometry and bounds can be inspected
/// and tested, and the model also exposes one merged vertex and index array so the
/// presenter uploads a single vertex buffer and a single index buffer and draws the
/// whole map in one call. The terrain is static, so this happens once, not per frame.
/// </para>
/// <para>
/// The model holds no engine, SDL3, or GPU handle. Uploading and drawing belong to
/// the host-boundary presenter.
/// </para>
/// </remarks>
internal sealed class OmsiTerrainModel
{
    private OmsiTerrainModel(
        double tileEdgeMeters,
        bool usesDocumentedTileEdge,
        IReadOnlyList<TerrainMesh> meshes,
        TerrainVertex[] vertices,
        uint[] indices,
        TerrainBounds? bounds,
        float minHeight,
        float maxHeight)
    {
        TileEdgeMeters = tileEdgeMeters;
        UsesDocumentedTileEdge = usesDocumentedTileEdge;
        Meshes = [.. meshes];
        Vertices = vertices;
        Indices = indices;
        Bounds = bounds;
        MinHeight = minHeight;
        MaxHeight = maxHeight;
    }

    /// <summary>Gets the tile edge in metres used to place the meshes.</summary>
    internal double TileEdgeMeters { get; }

    /// <summary>
    /// Gets a value indicating whether <see cref="TileEdgeMeters"/> is the documented
    /// 300 m constant rather than the reference-measured degree grid.
    /// </summary>
    internal bool UsesDocumentedTileEdge { get; }

    /// <summary>Gets the per-tile meshes, in tile discovery order.</summary>
    internal IReadOnlyList<TerrainMesh> Meshes { get; }

    /// <summary>Gets every tile's vertices, concatenated for one buffer.</summary>
    internal TerrainVertex[] Vertices { get; }

    /// <summary>Gets every tile's indices, offset into <see cref="Vertices"/>.</summary>
    internal uint[] Indices { get; }

    /// <summary>
    /// Gets the world-space bounds of all terrain, or <see langword="null"/> when the
    /// map produced no mesh.
    /// </summary>
    internal TerrainBounds? Bounds { get; }

    /// <summary>Gets the lowest loaded height, in metres.</summary>
    internal float MinHeight { get; }

    /// <summary>Gets the highest loaded height, in metres.</summary>
    internal float MaxHeight { get; }

    /// <summary>Gets the number of tiles that produced a mesh.</summary>
    internal int TileCount => Meshes.Count;

    /// <summary>Gets the number of vertices across all tiles.</summary>
    internal int VertexCount => Vertices.Length;

    /// <summary>Gets the number of triangles across all tiles.</summary>
    internal int TriangleCount => Indices.Length / 3;

    /// <summary>
    /// Builds the merged terrain geometry of a decoded terrain set.
    /// </summary>
    /// <param name="terrain">The decoded height grids.</param>
    /// <returns>
    /// A model that is empty, with no bounds, when no tile decoded. The caller decides
    /// whether that is an error; nothing is substituted.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="terrain"/> is null.</exception>
    internal static OmsiTerrainModel Create(OmsiTerrainSet terrain)
    {
        ArgumentNullException.ThrowIfNull(terrain);

        if (terrain.Heightmaps.Count == 0)
        {
            return new OmsiTerrainModel(
                terrain.TileEdgeMeters,
                terrain.UsesDocumentedTileEdge,
                [],
                [],
                [],
                null,
                0.0f,
                0.0f);
        }

        float minHeight = float.PositiveInfinity;
        float maxHeight = float.NegativeInfinity;

        foreach (OmsiTerrainHeightmap heightmap in terrain.Heightmaps)
        {
            minHeight = MathF.Min(minHeight, heightmap.MinHeight);
            maxHeight = MathF.Max(maxHeight, heightmap.MaxHeight);
        }

        var meshes = new List<TerrainMesh>(terrain.Heightmaps.Count);
        var vertices = new TerrainVertex[
            terrain.Heightmaps.Count * TerrainMeshBuilder.VerticesPerTile];
        var indices = new uint[
            terrain.Heightmaps.Count * TerrainMeshBuilder.IndicesPerTile];

        TerrainBounds? bounds = null;
        int vertexOffset = 0;
        int indexOffset = 0;

        foreach (OmsiTerrainHeightmap heightmap in terrain.Heightmaps)
        {
            TerrainMesh mesh = TerrainMeshBuilder.Build(heightmap, minHeight, maxHeight);

            meshes.Add(mesh);

            Array.Copy(mesh.Vertices, 0, vertices, vertexOffset, mesh.Vertices.Length);

            uint indexBase = (uint)vertexOffset;

            for (int index = 0; index < mesh.Indices.Length; index++)
            {
                indices[indexOffset + index] = indexBase + mesh.Indices[index];
            }

            vertexOffset += mesh.Vertices.Length;
            indexOffset += mesh.Indices.Length;

            bounds = bounds is TerrainBounds current
                ? current.Union(mesh.Bounds)
                : mesh.Bounds;
        }

        return new OmsiTerrainModel(
            terrain.TileEdgeMeters,
            terrain.UsesDocumentedTileEdge,
            meshes,
            vertices,
            indices,
            bounds,
            minHeight,
            maxHeight);
    }
}

using System.Numerics;

namespace Nymeria.Omsi.Terrain;

/// <summary>
/// Generates the triangle mesh of one decoded terrain height grid.
/// </summary>
/// <remarks>
/// <para>
/// The grid is a regular 61 x 61 sample lattice over one tile, so the mesh is a fixed
/// number of vertices and triangles: every sample becomes a vertex and every one of
/// the 60 x 60 cells becomes two triangles. Indices are wound counter-clockwise when
/// the surface is viewed from above (+Y), which is the front face the pipeline
/// culls against.
/// </para>
/// <para>
/// Normals come from central differences of the decoded heights, so a slope shades
/// differently from a plain. Height extremes use one-sided differences instead of
/// wrapping around to the opposite tile edge, which keeps neighbouring tiles from
/// disagreeing about a shared edge's normal.
/// </para>
/// <para>
/// Samples are placed with one mapping for the whole task: world x = tile column *
/// tile edge + column * spacing, world y = height, world z = -(tile row * tile edge +
/// row * spacing). World y is up, matching the engine camera, and the horizontal axes
/// are the map plane the diagnostic plan already uses. Negating the OMSI y axis keeps
/// the mapping rigid, so the terrain is never mirrored, and it puts increasing tile
/// and grid rows away from a camera that looks down its own -z axis. The compass
/// direction of the OMSI axes is not asserted, exactly as in the plan view.
/// </para>
/// <para>
/// Nothing here touches the engine, SDL3, or a GPU, so mesh generation is tested on
/// synthetic height grids.
/// </para>
/// </remarks>
internal static class TerrainMeshBuilder
{
    /// <summary>The number of vertices one tile contributes.</summary>
    internal const int VerticesPerTile = OmsiTerrainHeightmap.SampleCount;

    /// <summary>The number of triangles one tile contributes.</summary>
    internal const int TrianglesPerTile =
        OmsiTerrainHeightmap.CellsPerEdge * OmsiTerrainHeightmap.CellsPerEdge * 2;

    /// <summary>The number of indices one tile contributes.</summary>
    internal const int IndicesPerTile = TrianglesPerTile * 3;

    /// <summary>
    /// Builds the world-space mesh of one tile.
    /// </summary>
    /// <param name="heightmap">The decoded grid to build from.</param>
    /// <param name="rangeMin">The lowest height over all loaded terrain, in metres.</param>
    /// <param name="rangeMax">The highest height over all loaded terrain, in metres.</param>
    /// <returns>The mesh, placed at the tile's world origin.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="heightmap"/> is null.</exception>
    internal static TerrainMesh Build(
        OmsiTerrainHeightmap heightmap,
        float rangeMin,
        float rangeMax)
    {
        ArgumentNullException.ThrowIfNull(heightmap);

        var vertices = new TerrainVertex[VerticesPerTile];
        var indices = new uint[IndicesPerTile];

        float spacing = (float)heightmap.SampleSpacingMeters;
        float originX = (float)(heightmap.TileX * heightmap.TileEdgeMeters);
        float originZ = -(float)(heightmap.TileY * heightmap.TileEdgeMeters);

        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float minZ = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;
        float maxZ = float.NegativeInfinity;

        for (int row = 0; row < OmsiTerrainHeightmap.SamplesPerEdge; row++)
        {
            for (int column = 0; column < OmsiTerrainHeightmap.SamplesPerEdge; column++)
            {
                float height = heightmap.HeightAt(row, column);
                var position = new Vector3(
                    originX + (column * spacing),
                    height,
                    originZ - (row * spacing));

                Vector3 normal = ComputeNormal(heightmap, row, column, spacing);
                Vector3 color = TerrainColor.FromHeight(height, rangeMin, rangeMax);

                vertices[(row * OmsiTerrainHeightmap.SamplesPerEdge) + column] =
                    new TerrainVertex(position, normal, color);

                minX = MathF.Min(minX, position.X);
                minY = MathF.Min(minY, position.Y);
                minZ = MathF.Min(minZ, position.Z);
                maxX = MathF.Max(maxX, position.X);
                maxY = MathF.Max(maxY, position.Y);
                maxZ = MathF.Max(maxZ, position.Z);
            }
        }

        int next = 0;

        for (int row = 0; row < OmsiTerrainHeightmap.CellsPerEdge; row++)
        {
            for (int column = 0; column < OmsiTerrainHeightmap.CellsPerEdge; column++)
            {
                uint topLeft = (uint)((row * OmsiTerrainHeightmap.SamplesPerEdge) + column);
                uint topRight = topLeft + 1;
                uint bottomLeft = topLeft + OmsiTerrainHeightmap.SamplesPerEdge;
                uint bottomRight = bottomLeft + 1;

                // Both triangles are counter-clockwise seen from +Y: the first
                // walks +x then +x/-z, the second +x/-z then -z.
                indices[next++] = topLeft;
                indices[next++] = topRight;
                indices[next++] = bottomRight;

                indices[next++] = topLeft;
                indices[next++] = bottomRight;
                indices[next++] = bottomLeft;
            }
        }

        return new TerrainMesh(
            heightmap.TileX,
            heightmap.TileY,
            vertices,
            indices,
            new TerrainBounds(minX, minY, minZ, maxX, maxY, maxZ),
            heightmap.MinHeight,
            heightmap.MaxHeight);
    }

    /// <summary>
    /// Computes the world-space normal of one grid sample from its neighbours.
    /// </summary>
    /// <remarks>
    /// With world x along the grid columns and world z along the negated grid rows,
    /// the tangents are <c>(1, dh/du, 0)</c> and <c>(0, dh/dv, -1)</c>, and their
    /// cross product is <c>(-dh/du, 1, dh/dv)</c>. It always has a positive y
    /// component, so the normal points up and the winding above is front-facing.
    /// </remarks>
    private static Vector3 ComputeNormal(
        OmsiTerrainHeightmap heightmap,
        int row,
        int column,
        float spacing)
    {
        float heightChangeAlongColumns = HeightChange(
            heightmap,
            row,
            column,
            alongRows: false,
            spacing);
        float heightChangeAlongRows = HeightChange(
            heightmap,
            row,
            column,
            alongRows: true,
            spacing);

        var normal = new Vector3(
            -heightChangeAlongColumns,
            1.0f,
            heightChangeAlongRows);

        float length = normal.Length();

        return length > 1e-6f ? normal / length : Vector3.UnitY;
    }

    private static float HeightChange(
        OmsiTerrainHeightmap heightmap,
        int row,
        int column,
        bool alongRows,
        float spacing)
    {
        int index = alongRows ? row : column;
        const int last = OmsiTerrainHeightmap.SamplesPerEdge - 1;

        if (index == 0)
        {
            float next = Sample(heightmap, row, column, alongRows, 1);
            float current = Sample(heightmap, row, column, alongRows, 0);
            return (next - current) / spacing;
        }

        if (index == last)
        {
            float previous = Sample(heightmap, row, column, alongRows, -1);
            float current = Sample(heightmap, row, column, alongRows, 0);
            return (current - previous) / spacing;
        }

        float ahead = Sample(heightmap, row, column, alongRows, 1);
        float behind = Sample(heightmap, row, column, alongRows, -1);
        return (ahead - behind) / (2.0f * spacing);
    }

    private static float Sample(
        OmsiTerrainHeightmap heightmap,
        int row,
        int column,
        bool alongRows,
        int offset)
    {
        return alongRows
            ? heightmap.HeightAt(row + offset, column)
            : heightmap.HeightAt(row, column + offset);
    }
}

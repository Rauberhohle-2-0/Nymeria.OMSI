using System.Numerics;
using System.Runtime.InteropServices;
using Nymeria.Omsi.Terrain;

namespace Nymeria.OMSI.Tests;

/// <summary>
/// Headless tests for terrain geometry generated from synthetic height grids.
/// </summary>
public sealed class TerrainMeshTests
{
    private const double TileEdge = 300.0;

    [Fact]
    public void FlatTileProducesAFullGridOfTriangles()
    {
        TerrainMesh mesh = TerrainMeshBuilder.Build(Heightmap(0, 0), 0.0f, 0.0f);

        Assert.Equal(61 * 61, mesh.Vertices.Length);
        Assert.Equal(60 * 60 * 2, mesh.TriangleCount);
        Assert.Equal(60 * 60 * 2 * 3, mesh.Indices.Length);
        Assert.Equal(TerrainMeshBuilder.VerticesPerTile, mesh.Vertices.Length);
        Assert.Equal(TerrainMeshBuilder.IndicesPerTile, mesh.Indices.Length);
    }

    [Fact]
    public void EveryTriangleIsWoundCounterClockwiseSeenFromAbove()
    {
        TerrainMesh mesh = TerrainMeshBuilder.Build(Heightmap(0, 0), 0.0f, 0.0f);

        for (int index = 0; index < mesh.Indices.Length; index += 3)
        {
            Vector3 a = mesh.Vertices[mesh.Indices[index]].Position;
            Vector3 b = mesh.Vertices[mesh.Indices[index + 1]].Position;
            Vector3 c = mesh.Vertices[mesh.Indices[index + 2]].Position;

            Vector3 normal = Vector3.Cross(b - a, c - a);

            Assert.True(
                normal.Y > 0.0f,
                $"Triangle {index / 3} is not front-facing from above: {normal}.");
        }
    }

    [Fact]
    public void SamplesSitOnTheTileGridWithFiveMetreSpacing()
    {
        TerrainMesh mesh = TerrainMeshBuilder.Build(Heightmap(tileX: 2, tileY: -1), 0.0f, 0.0f);

        // Tile (2, -1) starts at world (600, *, 300) and grows towards -z.
        Assert.Equal(600.0f, mesh.Bounds.MinX);
        Assert.Equal(900.0f, mesh.Bounds.MaxX);
        Assert.Equal(0.0f, mesh.Bounds.MinZ);
        Assert.Equal(300.0f, mesh.Bounds.MaxZ);

        Vector3 first = mesh.Vertices[0].Position;
        Vector3 rightNeighbour = mesh.Vertices[1].Position;
        Vector3 rowNeighbour = mesh.Vertices[61].Position;

        Assert.Equal(new Vector3(600.0f, 0.0f, 300.0f), first);
        Assert.Equal(5.0f, rightNeighbour.X - first.X, precision: 4);
        Assert.Equal(0.0f, rightNeighbour.Z - first.Z, precision: 4);
        Assert.Equal(-5.0f, rowNeighbour.Z - first.Z, precision: 4);
    }

    [Fact]
    public void DecodedHeightsBecomeVertexElevations()
    {
        float[] heights = new float[OmsiTerrainHeightmap.SampleCount];

        for (int row = 0; row < 61; row++)
        {
            for (int column = 0; column < 61; column++)
            {
                heights[(row * 61) + column] = (row * 0.5f) + (column * 0.25f);
            }
        }

        TerrainMesh mesh = TerrainMeshBuilder.Build(
            Heightmap(0, 0, heights),
            heights.Min(),
            heights.Max());

        Assert.Equal(0.0f, mesh.Vertices[0].Position.Y);
        Assert.Equal(0.25f, mesh.Vertices[1].Position.Y, precision: 4);
        Assert.Equal(0.5f, mesh.Vertices[61].Position.Y, precision: 4);
        Assert.Equal(30.0f + 15.0f, mesh.Vertices[^1].Position.Y, precision: 4);

        Assert.Equal(heights.Min(), mesh.MinHeight);
        Assert.Equal(heights.Max(), mesh.MaxHeight);
    }

    [Fact]
    public void FlatTerrainHasUpwardUnitNormals()
    {
        TerrainMesh mesh = TerrainMeshBuilder.Build(Heightmap(0, 0), 0.0f, 0.0f);

        foreach (TerrainVertex vertex in mesh.Vertices)
        {
            Assert.Equal(Vector3.UnitY, vertex.Normal);
            Assert.Equal(1.0f, vertex.Normal.Length(), precision: 5);
        }
    }

    [Fact]
    public void RisingGroundTiltsNormalsAgainstTheSlope()
    {
        float[] heights = new float[OmsiTerrainHeightmap.SampleCount];

        for (int row = 0; row < 61; row++)
        {
            for (int column = 0; column < 61; column++)
            {
                heights[(row * 61) + column] = column * 1.0f;
            }
        }

        TerrainMesh mesh = TerrainMeshBuilder.Build(
            Heightmap(0, 0, heights),
            0.0f,
            60.0f);

        // A 1 m rise per 5 m step gives a normal of (-0.2, 1, 0), normalized.
        foreach (TerrainVertex vertex in mesh.Vertices)
        {
            Assert.True(vertex.Normal.X < 0.0f);
            Assert.InRange(vertex.Normal.Length(), 0.999f, 1.001f);
        }
    }

    [Fact]
    public void NeighbouringTilesMeetWithoutAGap()
    {
        float[] heights = HeightsWithEdge(2.5f);

        TerrainMesh lower = TerrainMeshBuilder.Build(Heightmap(0, 0, heights), 0.0f, 5.0f);
        TerrainMesh upper = TerrainMeshBuilder.Build(Heightmap(0, 1, heights), 0.0f, 5.0f);

        // The last row of the lower tile and the first row of the upper tile must
        // describe the same world edge, sample by sample.
        Assert.Equal(lower.Bounds.MinZ, upper.Bounds.MaxZ, precision: 4);

        for (int column = 0; column < 61; column++)
        {
            Vector3 lowerVertex = lower.Vertices[(60 * 61) + column].Position;
            Vector3 upperVertex = upper.Vertices[column].Position;

            Assert.Equal(lowerVertex.X, upperVertex.X, precision: 4);
            Assert.Equal(lowerVertex.Z, upperVertex.Z, precision: 4);
            Assert.Equal(lowerVertex.Y, upperVertex.Y, precision: 4);
        }
    }

    [Fact]
    public void HeightRampUsesTheLowestAndHighestLoadedValues()
    {
        Assert.Equal(
            TerrainColor.FromNormalizedHeight(0.0f),
            TerrainColor.FromHeight(10.0f, 10.0f, 20.0f));
        Assert.Equal(
            TerrainColor.FromNormalizedHeight(1.0f),
            TerrainColor.FromHeight(20.0f, 10.0f, 20.0f));
        Assert.Equal(
            TerrainColor.Flat,
            TerrainColor.FromHeight(15.0f, 15.0f, 15.0f));
        Assert.Equal(
            TerrainColor.FromNormalizedHeight(0.0f),
            TerrainColor.FromNormalizedHeight(-3.0f));
        Assert.Equal(
            TerrainColor.FromNormalizedHeight(1.0f),
            TerrainColor.FromNormalizedHeight(3.0f));
    }

    [Fact]
    public void MergedModelOffsetsIndicesAndCombinesBounds()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf16Le("global.cfg", "[name]\r\nTest Map\r\n");
        directory.WriteUtf16Le("tile_0_0.map", "[version]\r\n12\r\n");
        directory.WriteUtf16Le("tile_1_0.map", "[version]\r\n12\r\n");
        directory.WriteRaw("tile_0_0.map.terrain", TerrainDecoderTests.Payload(height: 1.0f));
        directory.WriteRaw("tile_1_0.map.terrain", TerrainDecoderTests.Payload(height: 3.0f));

        OmsiTerrainSet terrain = OmsiTerrainSet.Load(
            Nymeria.Omsi.World.OmsiWorldLoader.Load(directory.Path));
        OmsiTerrainModel model = OmsiTerrainModel.Create(terrain);

        Assert.Equal(2, model.TileCount);
        Assert.Equal(2 * TerrainMeshBuilder.VerticesPerTile, model.VertexCount);
        Assert.Equal(2 * TerrainMeshBuilder.IndicesPerTile, model.Indices.Length);
        Assert.Equal(TerrainMeshBuilder.TrianglesPerTile * 2, model.TriangleCount);

        Assert.NotNull(model.Bounds);
        Assert.Equal(0.0f, model.Bounds.Value.MinX);
        Assert.Equal(600.0f, model.Bounds.Value.MaxX);
        Assert.Equal(1.0f, model.MinHeight);
        Assert.Equal(3.0f, model.MaxHeight);

        // Every index must address a vertex of the merged buffer, which proves the
        // second tile was shifted past the first one's vertices.
        Assert.All(model.Indices, index => Assert.InRange(index, 0u, (uint)model.VertexCount - 1));
        Assert.Contains(model.Indices, index => index >= TerrainMeshBuilder.VerticesPerTile);
    }

    [Fact]
    public void EmptyTerrainSetProducesAnEmptyModelWithoutBounds()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf16Le("global.cfg", "[name]\r\nTest Map\r\n");
        directory.WriteUtf16Le("tile_0_0.map", "[version]\r\n12\r\n");

        OmsiTerrainSet terrain = OmsiTerrainSet.Load(
            Nymeria.Omsi.World.OmsiWorldLoader.Load(directory.Path));
        OmsiTerrainModel model = OmsiTerrainModel.Create(terrain);

        Assert.Equal(0, model.TileCount);
        Assert.Equal(0, model.VertexCount);
        Assert.Empty(model.Indices);
        Assert.Null(model.Bounds);
    }

    [Fact]
    public void VertexLayoutIsBlittableWithTheDocumentedOffsets()
    {
        Assert.Equal(36, TerrainVertex.SizeInBytes);
        Assert.Equal(TerrainVertex.SizeInBytes, Marshal.SizeOf<TerrainVertex>());

        var vertex = new TerrainVertex(
            new Vector3(1.0f, 2.0f, 3.0f),
            new Vector3(0.0f, 1.0f, 0.0f),
            new Vector3(0.5f, 0.25f, 0.125f));

        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(
            MemoryMarshal.CreateReadOnlySpan(ref vertex, 1));

        Assert.Equal(TerrainVertex.PositionOffsetBytes, 0);
        Assert.Equal(TerrainVertex.NormalOffsetBytes, 12);
        Assert.Equal(TerrainVertex.ColorOffsetBytes, 24);

        Assert.Equal(1.0f, BitConverter.ToSingle(bytes[..4]));
        Assert.Equal(3.0f, BitConverter.ToSingle(bytes[8..12]));
        Assert.Equal(1.0f, BitConverter.ToSingle(bytes[16..20]));
        Assert.Equal(0.5f, BitConverter.ToSingle(bytes[24..28]));
        Assert.Equal(0.125f, BitConverter.ToSingle(bytes[32..36]));
    }

    private static OmsiTerrainHeightmap Heightmap(
        int tileX,
        int tileY,
        float[]? heights = null)
    {
        return new OmsiTerrainHeightmap(
            tileX,
            tileY,
            "memory.terrain",
            heights ?? new float[OmsiTerrainHeightmap.SampleCount],
            TileEdge);
    }

    private static float[] HeightsWithEdge(float edgeHeight)
    {
        var heights = new float[OmsiTerrainHeightmap.SampleCount];

        for (int row = 0; row < 61; row++)
        {
            for (int column = 0; column < 61; column++)
            {
                heights[(row * 61) + column] = row == 60 || row == 0
                    ? edgeHeight
                    : row * 0.1f;
            }
        }

        return heights;
    }
}

using System.Buffers.Binary;
using Nymeria.Omsi.Maps;
using Nymeria.Omsi.Terrain;
using Nymeria.Omsi.World;

namespace Nymeria.OMSI.Tests;

/// <summary>
/// Headless tests for the documented terrain sidecar decoder and the terrain set
/// loader. Every fixture is a synthetic byte array or a synthetic map folder written by
/// these tests; none of it is OMSI content, and passing these tests is not evidence of
/// Grundorf compatibility.
/// </summary>
public sealed class TerrainDecoderTests
{
    private const int DocumentedTileEdge = 300;

    [Fact]
    public void DocumentedLayoutDecodesIntoA61By61Grid()
    {
        byte[] payload = Payload(height: 4.0f);

        bool decoded = OmsiTerrainDecoder.TryDecode(
            "tile_0_0.map.terrain",
            payload,
            tileX: 0,
            tileY: 0,
            DocumentedTileEdge,
            out OmsiTerrainHeightmap? heightmap,
            out string? error);

        Assert.True(decoded);
        Assert.Null(error);
        Assert.NotNull(heightmap);
        Assert.Equal(61, OmsiTerrainHeightmap.SamplesPerEdge);
        Assert.Equal(3721, OmsiTerrainHeightmap.SampleCount);
        Assert.Equal(4.0f, heightmap.MinHeight);
        Assert.Equal(4.0f, heightmap.MaxHeight);

        // 61 samples across a 300 m tile put neighbours exactly 5 m apart.
        Assert.Equal(5.0, heightmap.SampleSpacingMeters, precision: 9);
        Assert.Equal(4.0f, heightmap.HeightAt(0, 0));
        Assert.Equal(4.0f, heightmap.HeightAt(60, 60));
    }

    [Fact]
    public void DocumentedFileLengthIsTheLayoutTheLoaderChecks()
    {
        Assert.Equal(4 + (61 * 61 * 4), OmsiTerrainDecoder.DocumentedFileLength);
        Assert.Equal(14_888, OmsiTerrainDecoder.DocumentedFileLength);
    }

    [Fact]
    public void SampleHeightsAreKeptAsMetres()
    {
        byte[] payload = Payload(height: 0.0f);
        WriteSample(payload, row: 3, column: 4, height: 12.5f);
        WriteSample(payload, row: 60, column: 0, height: -2.25f);

        Assert.True(OmsiTerrainDecoder.TryDecode(
            "tile_0_0.map.terrain",
            payload,
            0,
            0,
            DocumentedTileEdge,
            out OmsiTerrainHeightmap? heightmap,
            out _));

        Assert.NotNull(heightmap);
        Assert.Equal(12.5f, heightmap.HeightAt(3, 4));
        Assert.Equal(-2.25f, heightmap.HeightAt(60, 0));
        Assert.Equal(-2.25f, heightmap.MinHeight);
        Assert.Equal(12.5f, heightmap.MaxHeight);
    }

    [Fact]
    public void AnotherHeaderValueIsRejectedInsteadOfReadAtTheWrongSpacing()
    {
        byte[] payload = Payload(header: 61);

        Assert.False(OmsiTerrainDecoder.TryDecode(
            "tile_0_0.map.terrain",
            payload,
            0,
            0,
            DocumentedTileEdge,
            out OmsiTerrainHeightmap? heightmap,
            out string? error));

        Assert.Null(heightmap);
        Assert.NotNull(error);
        Assert.Contains("header", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TruncatedPayloadIsRejectedWithBothLengths()
    {
        byte[] payload = Payload()[..^4];

        Assert.False(OmsiTerrainDecoder.TryDecode(
            "tile_0_0.map.terrain",
            payload,
            0,
            0,
            DocumentedTileEdge,
            out _,
            out string? error));

        Assert.NotNull(error);
        Assert.Contains("14884", error, StringComparison.Ordinal);
        Assert.Contains("14888", error, StringComparison.Ordinal);
    }

    [Fact]
    public void NonFiniteHeightIsRejectedWithItsRowAndColumn()
    {
        byte[] payload = Payload();
        WriteSample(payload, row: 5, column: 7, height: float.NaN);

        Assert.False(OmsiTerrainDecoder.TryDecode(
            "tile_0_0.map.terrain",
            payload,
            0,
            0,
            DocumentedTileEdge,
            out _,
            out string? error));

        Assert.NotNull(error);
        Assert.Contains("row 5", error, StringComparison.Ordinal);
        Assert.Contains("column 7", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ImplausibleHeightIsRejectedAsNotMetres()
    {
        byte[] payload = Payload();
        WriteSample(payload, row: 0, column: 0, height: 50_000.0f);

        Assert.False(OmsiTerrainDecoder.TryDecode(
            "tile_0_0.map.terrain",
            payload,
            0,
            0,
            DocumentedTileEdge,
            out _,
            out string? error));

        Assert.NotNull(error);
        Assert.Contains("limit", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TerrainSetDecodesSidecarsAndReportsTilesWithoutOne()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf16Le("global.cfg", "[name]\r\nTest Map\r\n");
        directory.WriteUtf16Le("tile_0_0.map", "[version]\r\n12\r\n");
        directory.WriteUtf16Le("tile_1_0.map", "[version]\r\n12\r\n");
        directory.WriteRaw("tile_0_0.map.terrain", Payload(height: 1.0f));

        OmsiTerrainSet terrain = OmsiTerrainSet.Load(OmsiWorldLoader.Load(directory.Path));

        Assert.Single(terrain.Heightmaps);
        Assert.Equal(0, terrain.Heightmaps[0].TileX);
        Assert.Equal(0, terrain.Heightmaps[0].TileY);
        Assert.Equal(1, terrain.MissingTerrainTiles);
        Assert.Equal(0, terrain.RejectedTerrainTiles);
        Assert.True(terrain.UsesDocumentedTileEdge);
        Assert.Contains(
            terrain.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "no terrain sidecar",
                StringComparison.Ordinal));
    }

    [Fact]
    public void TerrainSetRejectsASidecarOfTheWrongLength()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf16Le("global.cfg", "[name]\r\nTest Map\r\n");
        directory.WriteUtf16Le("tile_0_0.map", "[version]\r\n12\r\n");
        directory.WriteRaw("tile_0_0.map.terrain", new byte[100]);

        OmsiTerrainSet terrain = OmsiTerrainSet.Load(OmsiWorldLoader.Load(directory.Path));

        Assert.Empty(terrain.Heightmaps);
        Assert.Equal(1, terrain.RejectedTerrainTiles);
        Assert.Contains(
            terrain.Diagnostics,
            diagnostic => diagnostic.Severity == MapDiagnosticSeverity.Warning &&
                diagnostic.Message.Contains("100 bytes", StringComparison.Ordinal));
    }

    [Fact]
    public void TerrainSetUsesTheWorldCoordinateEdgeAndFlagsIt()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteUtf16Le(
            "global.cfg",
            "[name]\r\nTest Map\r\n[worldcoordinates]\r\n");
        directory.WriteUtf16Le("tile_0_0.map", "[version]\r\n12\r\n");
        directory.WriteRaw("tile_0_0.map.terrain", Payload());

        OmsiTerrainSet terrain = OmsiTerrainSet.Load(OmsiWorldLoader.Load(directory.Path));

        Assert.False(terrain.UsesDocumentedTileEdge);
        Assert.Equal(OmsiTileGrid.WorldCoordinateTileEdgeMeters, terrain.TileEdgeMeters);
        Assert.Single(terrain.Heightmaps);
    }

    [Fact]
    public void EncodedHeightsRoundTripThroughTheDocumentedLayout()
    {
        // A distinct value at every sample proves the decoder reads row-major
        // little-endian floats in file order without a flip or a stride mistake.
        byte[] payload = Payload();

        for (int index = 0; index < OmsiTerrainHeightmap.SampleCount; index++)
        {
            float height = index - 1800.0f;
            BinaryPrimitives.WriteInt32LittleEndian(
                payload.AsSpan(4 + (index * 4)),
                BitConverter.SingleToInt32Bits(height));
        }

        Assert.True(OmsiTerrainDecoder.TryDecode(
            "tile_0_0.map.terrain",
            payload,
            0,
            0,
            DocumentedTileEdge,
            out OmsiTerrainHeightmap? heightmap,
            out _));

        Assert.NotNull(heightmap);

        for (int row = 0; row < OmsiTerrainHeightmap.SamplesPerEdge; row++)
        {
            for (int column = 0; column < OmsiTerrainHeightmap.SamplesPerEdge; column++)
            {
                int index = (row * OmsiTerrainHeightmap.SamplesPerEdge) + column;
                Assert.Equal(index - 1800.0f, heightmap.HeightAt(row, column));
            }
        }
    }

    internal static byte[] Payload(float height = 0.0f, uint header = 60)
    {
        var bytes = new byte[OmsiTerrainDecoder.DocumentedFileLength];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, header);

        for (int index = 0; index < OmsiTerrainHeightmap.SampleCount; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(4 + (index * 4)),
                BitConverter.SingleToInt32Bits(height));
        }

        return bytes;
    }

    internal static void WriteSample(
        byte[] payload,
        int row,
        int column,
        float height)
    {
        int index = (row * OmsiTerrainHeightmap.SamplesPerEdge) + column;

        BinaryPrimitives.WriteInt32LittleEndian(
            payload.AsSpan(4 + (index * 4)),
            BitConverter.SingleToInt32Bits(height));
    }

}

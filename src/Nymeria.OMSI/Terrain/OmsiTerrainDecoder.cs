using System.Buffers.Binary;
using System.Globalization;

namespace Nymeria.Omsi.Terrain;

/// <summary>
/// Decodes one OMSI terrain sidecar into a height grid.
/// </summary>
/// <remarks>
/// <para>
/// The layout this decoder accepts is the documented one: a little-endian
/// <c>u32</c> that must equal 60, followed by 3721 little-endian 32-bit
/// floating-point heights, 14,888 bytes in total. The header is validated instead
/// of ignored, so a file with another grid size is rejected with a diagnostic
/// rather than read at the wrong sample spacing. Little-endian byte order is the
/// only order a Windows-written <c>f32</c> file can sanely have; it is an
/// assumption, not something the reference states.
/// </para>
/// <para>
/// Nothing is substituted when a file does not match: the tile produces a
/// diagnostic and no geometry. Decoding is pure and GPU-free, so it is tested with
/// synthetic byte arrays and no OMSI asset.
/// </para>
/// </remarks>
internal static class OmsiTerrainDecoder
{
    /// <summary>The documented header value, which is the cell count per edge.</summary>
    internal const uint DocumentedHeaderValue = OmsiTerrainHeightmap.CellsPerEdge;

    /// <summary>The exact documented sidecar length in bytes.</summary>
    internal const long DocumentedFileLength =
        4 + (OmsiTerrainHeightmap.SampleCount * 4);

    /// <summary>
    /// The largest absolute height accepted, in metres. It is far beyond any real
    /// map and rejects a file whose floats are actually some other encoding.
    /// </summary>
    internal const float MaximumAbsoluteHeightMeters = 10_000.0f;

    /// <summary>
    /// Decodes a sidecar payload.
    /// </summary>
    /// <param name="filePath">The sidecar path, used for diagnostics.</param>
    /// <param name="payload">The file bytes.</param>
    /// <param name="tileX">The tile column from the discovered file name.</param>
    /// <param name="tileY">The tile row from the discovered file name.</param>
    /// <param name="tileEdgeMeters">The tile edge in metres.</param>
    /// <param name="heightmap">The decoded grid when decoding succeeds.</param>
    /// <param name="error">A readable reason when decoding fails.</param>
    /// <returns><see langword="true"/> when the payload was decoded.</returns>
    internal static bool TryDecode(
        string filePath,
        ReadOnlySpan<byte> payload,
        int tileX,
        int tileY,
        double tileEdgeMeters,
        out OmsiTerrainHeightmap? heightmap,
        out string? error)
    {
        heightmap = null;
        error = null;

        if (payload.Length != DocumentedFileLength)
        {
            error = string.Format(
                CultureInfo.InvariantCulture,
                "The terrain sidecar is {0} bytes; the documented layout is {1} " +
                "bytes (a 4-byte value and 61 x 61 32-bit heights). Its heights " +
                "were not decoded.",
                payload.Length,
                DocumentedFileLength);
            return false;
        }

        uint header = BinaryPrimitives.ReadUInt32LittleEndian(payload);

        if (header != DocumentedHeaderValue)
        {
            error = string.Format(
                CultureInfo.InvariantCulture,
                "The terrain sidecar header is {0}; the documented value is {1} " +
                "(cells per tile edge). A grid of another size is not supported, " +
                "so its heights were not decoded.",
                header,
                DocumentedHeaderValue);
            return false;
        }

        var heights = new float[OmsiTerrainHeightmap.SampleCount];
        ReadOnlySpan<byte> samples = payload[4..];

        for (int index = 0; index < heights.Length; index++)
        {
            int bits = BinaryPrimitives.ReadInt32LittleEndian(samples[(index * 4)..]);
            float height = BitConverter.Int32BitsToSingle(bits);

            if (!float.IsFinite(height))
            {
                error = string.Format(
                    CultureInfo.InvariantCulture,
                    "Height sample {0} (row {1}, column {2}) is {3} and must be " +
                    "finite. The tile's heights were not decoded.",
                    index,
                    index / OmsiTerrainHeightmap.SamplesPerEdge,
                    index % OmsiTerrainHeightmap.SamplesPerEdge,
                    height);
                return false;
            }

            if (MathF.Abs(height) > MaximumAbsoluteHeightMeters)
            {
                error = string.Format(
                    CultureInfo.InvariantCulture,
                    "Height sample {0} (row {1}, column {2}) is {3} m, beyond the " +
                    "{4} m limit this task accepts. The file does not look like " +
                    "metre heights, so its heights were not decoded.",
                    index,
                    index / OmsiTerrainHeightmap.SamplesPerEdge,
                    index % OmsiTerrainHeightmap.SamplesPerEdge,
                    height,
                    MaximumAbsoluteHeightMeters);
                return false;
            }

            heights[index] = height;
        }

        heightmap = new OmsiTerrainHeightmap(
            tileX,
            tileY,
            filePath,
            heights,
            tileEdgeMeters);
        return true;
    }
}

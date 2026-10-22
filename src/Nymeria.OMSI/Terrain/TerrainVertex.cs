using System.Numerics;
using System.Runtime.InteropServices;

namespace Nymeria.Omsi.Terrain;

/// <summary>
/// One interleaved terrain vertex: position, surface normal, and diagnostic colour.
/// </summary>
/// <remarks>
/// <para>
/// The layout is sequential and blittable, so a <see cref="TerrainVertex"/> array can
/// be copied to a GPU vertex buffer with <c>MemoryMarshal.AsBytes</c> and described
/// by three <c>Float3</c> attributes at the offsets below. Positions and normals are
/// in world space, with +Y up.
/// </para>
/// <para>
/// The colour is a CPU-side diagnostic ramp over the decoded height, not an OMSI
/// ground texture. It exists so that elevation changes are visible without an asset
/// loader or a material system.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct TerrainVertex
{
    /// <summary>The number of 32-bit floats in one vertex.</summary>
    internal const int FloatCount = 9;

    /// <summary>The size of one vertex in bytes.</summary>
    internal const int SizeInBytes = FloatCount * sizeof(float);

    /// <summary>The byte offset of <see cref="Position"/> inside the vertex.</summary>
    internal const int PositionOffsetBytes = 0;

    /// <summary>The byte offset of <see cref="Normal"/> inside the vertex.</summary>
    internal const int NormalOffsetBytes = 3 * sizeof(float);

    /// <summary>The byte offset of <see cref="Color"/> inside the vertex.</summary>
    internal const int ColorOffsetBytes = 6 * sizeof(float);

    /// <summary>Creates a vertex from world-space position, normal, and colour.</summary>
    internal TerrainVertex(Vector3 position, Vector3 normal, Vector3 color)
    {
        Position = position;
        Normal = normal;
        Color = color;
    }

    /// <summary>The world-space position in metres, with +Y up.</summary>
    internal readonly Vector3 Position;

    /// <summary>The world-space surface normal, normalized and pointing up.</summary>
    internal readonly Vector3 Normal;

    /// <summary>The diagnostic height-ramp colour, one 0..1 value per channel.</summary>
    internal readonly Vector3 Color;
}

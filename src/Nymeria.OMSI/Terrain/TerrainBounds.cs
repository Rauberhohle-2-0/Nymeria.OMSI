using System.Numerics;

namespace Nymeria.Omsi.Terrain;

/// <summary>
/// An axis-aligned box in the terrain's world frame, in metres.
/// </summary>
/// <remarks>
/// World +Y is up. The horizontal axes are the map plane, mapped from OMSI's x and y
/// as described on <see cref="OmsiTerrainHeightmap.SamplePosition"/>. This type holds
/// no engine or GPU type, so framing and clamping can be tested without a device.
/// </remarks>
internal readonly record struct TerrainBounds(
    float MinX,
    float MinY,
    float MinZ,
    float MaxX,
    float MaxY,
    float MaxZ)
{
    /// <summary>Gets the extent along world x, in metres.</summary>
    internal float Width => MaxX - MinX;

    /// <summary>Gets the extent along world y, in metres.</summary>
    internal float Height => MaxY - MinY;

    /// <summary>Gets the extent along world z, in metres.</summary>
    internal float Depth => MaxZ - MinZ;

    /// <summary>Gets the larger of the two horizontal extents, in metres.</summary>
    internal float HorizontalExtent => MathF.Max(Width, Depth);

    /// <summary>Gets the centre of the box.</summary>
    internal Vector3 Center => new(
        (MinX + MaxX) * 0.5f,
        (MinY + MaxY) * 0.5f,
        (MinZ + MaxZ) * 0.5f);

    /// <summary>Gets the length of the box diagonal, in metres.</summary>
    internal float Diagonal => MathF.Sqrt(
        (Width * Width) + (Height * Height) + (Depth * Depth));

    /// <summary>Grows the box so it contains another one.</summary>
    /// <param name="other">The box to include.</param>
    internal TerrainBounds Union(in TerrainBounds other)
    {
        return new TerrainBounds(
            MathF.Min(MinX, other.MinX),
            MathF.Min(MinY, other.MinY),
            MathF.Min(MinZ, other.MinZ),
            MathF.Max(MaxX, other.MaxX),
            MathF.Max(MaxY, other.MaxY),
            MathF.Max(MaxZ, other.MaxZ));
    }
}

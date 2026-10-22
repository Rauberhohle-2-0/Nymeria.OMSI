using System.Numerics;
using System.Runtime.InteropServices;

namespace Nymeria.Omsi.Rendering;

/// <summary>
/// One interleaved scenery vertex: position, normal, and texture coordinate.
/// </summary>
/// <remarks>
/// The layout is the engine's model geometry flattened for one GPU buffer:
/// <c>float3 position</c>, <c>float3 normal</c>, <c>float2 texCoord</c>, 32 bytes in
/// total. Attributes use locations 0, 1, and 2, matching
/// <c>Shaders/Scenery.hlsl</c> and <c>Shaders/Scenery.metal</c>.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct SceneryVertex
{
    internal SceneryVertex(Vector3 position, Vector3 normal, Vector2 textureCoordinate)
    {
        Position = position;
        Normal = normal;
        TextureCoordinate = textureCoordinate;
    }

    /// <summary>The byte size of one vertex.</summary>
    internal const int SizeInBytes = 32;

    /// <summary>The byte offset of the position attribute.</summary>
    internal const int PositionOffsetBytes = 0;

    /// <summary>The byte offset of the normal attribute.</summary>
    internal const int NormalOffsetBytes = 12;

    /// <summary>The byte offset of the texture-coordinate attribute.</summary>
    internal const int TextureCoordinateOffsetBytes = 24;

    /// <summary>Gets the model-space position.</summary>
    internal Vector3 Position { get; }

    /// <summary>Gets the model-space normal.</summary>
    internal Vector3 Normal { get; }

    /// <summary>Gets the texture coordinate.</summary>
    internal Vector2 TextureCoordinate { get; }
}

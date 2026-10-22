using System.Numerics;

namespace Nymeria.Omsi.Terrain;

/// <summary>
/// The initial view of a loaded terrain: where the camera starts, which way it looks,
/// and how far it can see.
/// </summary>
/// <remarks>
/// <para>
/// The framing follows from the loaded terrain bounds only, so opening a map starts
/// above and just outside it looking at its middle. It uses <see cref="System.Numerics"/>
/// exclusively; applying it to the engine camera is the presenter's job, which keeps
/// the terrain layer free of engine types.
/// </para>
/// <para>
/// Distances are in metres. The far plane is set from the terrain diagonal instead of
/// a fixed value, so a large map is not clipped away and a small one does not lose
/// depth precision.
/// </para>
/// </remarks>
internal readonly record struct TerrainCameraFraming(
    Vector3 Position,
    float YawRadians,
    float PitchRadians,
    float NearPlaneMeters,
    float FarPlaneMeters,
    float MoveSpeedMetersPerSecond)
{
    /// <summary>The vertical field of view used by the terrain view, in radians.</summary>
    internal const float FieldOfViewRadians = MathF.PI / 3.0f;

    /// <summary>The smallest movement speed used by the terrain view, in m/s.</summary>
    internal const float DefaultMoveSpeedMetersPerSecond = 12.0f;

    /// <summary>
    /// Derives a framing that shows a whole terrain from just outside its bounds.
    /// </summary>
    /// <param name="bounds">The world-space bounds of the loaded terrain.</param>
    internal static TerrainCameraFraming FromBounds(in TerrainBounds bounds)
    {
        Vector3 center = bounds.Center;

        // A terrain smaller than a single tile still needs a usable standoff, and a
        // deep map needs more height than a flat one.
        float horizontalExtent = MathF.Max(bounds.HorizontalExtent, 1.0f);
        float standoff = MathF.Max(horizontalExtent * 0.25f, 20.0f);
        float eyeRise = MathF.Max(bounds.Height + (horizontalExtent * 0.20f), 25.0f);

        var position = new Vector3(
            center.X,
            center.Y + eyeRise,
            bounds.MaxZ + standoff);

        float deltaX = center.X - position.X;
        float deltaZ = center.Z - position.Z;
        float deltaY = center.Y - position.Y;
        float groundDistance = MathF.Sqrt((deltaX * deltaX) + (deltaZ * deltaZ));

        // The engine camera looks along -z at yaw 0 and along +x at a quarter turn.
        float yaw = MathF.Atan2(deltaX, -deltaZ);
        float pitch = MathF.Atan2(deltaY, MathF.Max(groundDistance, 1e-3f));

        float toCenter = position.Length();
        float farPlane = MathF.Max(
            (bounds.Diagonal + toCenter) * 1.25f,
            DefaultMoveSpeedMetersPerSecond * 10.0f);

        float moveSpeed = MathF.Max(
            horizontalExtent * 0.25f,
            DefaultMoveSpeedMetersPerSecond);

        return new TerrainCameraFraming(
            position,
            yaw,
            pitch,
            NearPlaneMeters: 0.5f,
            farPlane,
            moveSpeed);
    }
}

using System.Numerics;
using Nymeria.Graphics;
using Nymeria.Omsi.Terrain;

namespace Nymeria.Omsi.Rendering;

/// <summary>
/// Maps one frame of terrain-view input onto the engine camera.
/// </summary>
/// <remarks>
/// <para>
/// This is input mapping and camera state for the terrain view, not a reusable input
/// system: SDL3 state is sampled by the presenter, and only the resulting snapshot
/// reaches this type. The engine's <see cref="Camera"/> owns the view and projection
/// matrices.
/// </para>
/// <para>
/// Movement is scaled by elapsed seconds, so the view travels the same distance per
/// real second at any frame rate. A frame longer than
/// <see cref="MaximumFrameSeconds"/> is clamped, so a stall or a debugger pause cannot
/// throw the camera across the map.
/// </para>
/// </remarks>
internal sealed class TerrainCameraController
{
    /// <summary>
    /// The longest frame time applied to movement, in seconds. A longer frame counts
    /// as this much time instead of its full length.
    /// </summary>
    internal const float MaximumFrameSeconds = 0.1f;

    private readonly Camera _camera;
    private readonly TerrainCameraFraming _startView;

    /// <summary>
    /// Creates a controller and applies the initial framing to the camera.
    /// </summary>
    /// <param name="camera">The engine camera the controller drives.</param>
    /// <param name="startView">Where the view starts.</param>
    /// <exception cref="ArgumentNullException"><paramref name="camera"/> is null.</exception>
    internal TerrainCameraController(Camera camera, TerrainCameraFraming startView)
    {
        ArgumentNullException.ThrowIfNull(camera);

        _camera = camera;
        _startView = startView;

        Apply(startView);
    }

    /// <summary>Gets the camera this controller drives.</summary>
    internal Camera Camera => _camera;

    /// <summary>Gets the framing the reset key returns to.</summary>
    internal TerrainCameraFraming StartView => _startView;

    /// <summary>
    /// Applies one frame of input.
    /// </summary>
    /// <param name="input">The sampled input for this frame.</param>
    /// <param name="deltaSeconds">
    /// Elapsed seconds since the previous frame. Non-finite or non-positive values do
    /// nothing, so a first frame or a clock anomaly cannot move the camera.
    /// </param>
    internal void Update(in TerrainInputState input, float deltaSeconds)
    {
        if (!float.IsFinite(deltaSeconds) || deltaSeconds <= 0.0f)
        {
            return;
        }

        float step = MathF.Min(deltaSeconds, MaximumFrameSeconds);

        if (input.ResetView)
        {
            Apply(_startView);
            return;
        }

        if (input.Looking)
        {
            _camera.LookByMouse(input.MouseDeltaX, input.MouseDeltaY);
        }

        var direction = new Vector3(
            Axis(input.Right, input.Left),
            Axis(input.Up, input.Down),
            Axis(input.Forward, input.Backward));

        _camera.Move(direction, step, input.Sprint);
    }

    private void Apply(TerrainCameraFraming framing)
    {
        _camera.Position = framing.Position;
        _camera.YawRadians = framing.YawRadians;
        _camera.PitchRadians = framing.PitchRadians;
        _camera.FieldOfViewRadians = TerrainCameraFraming.FieldOfViewRadians;
        _camera.NearPlane = framing.NearPlaneMeters;
        _camera.FarPlane = framing.FarPlaneMeters;
        _camera.MoveSpeed = framing.MoveSpeedMetersPerSecond;
        _camera.SprintMultiplier = 4.0f;
    }

    private static float Axis(bool positive, bool negative)
    {
        return (positive ? 1.0f : 0.0f) - (negative ? 1.0f : 0.0f);
    }
}

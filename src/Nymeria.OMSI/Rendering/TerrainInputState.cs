namespace Nymeria.Omsi.Rendering;

/// <summary>
/// One frame of terrain-view input, with SDL3 state already sampled.
/// </summary>
/// <remarks>
/// The presenter reads SDL's cached keyboard and mouse state and builds this value;
/// <see cref="TerrainCameraController"/> consumes it and owns the camera math. Keeping
/// the snapshot separate means movement and look rules are unit-tested without SDL,
/// a window, or a device.
/// </remarks>
/// <param name="Forward">Whether the forward key is held.</param>
/// <param name="Backward">Whether the backward key is held.</param>
/// <param name="Left">Whether the left key is held.</param>
/// <param name="Right">Whether the right key is held.</param>
/// <param name="Up">Whether the ascend key is held.</param>
/// <param name="Down">Whether the descend key is held.</param>
/// <param name="Sprint">Whether a sprint key is held.</param>
/// <param name="ResetView">Whether the reset key is held.</param>
/// <param name="Looking">
/// Whether mouse look is active this frame, normally while the right button is held.
/// </param>
/// <param name="MouseDeltaX">Horizontal mouse movement in pixels; positive is right.</param>
/// <param name="MouseDeltaY">Vertical mouse movement in pixels; positive is down.</param>
internal readonly record struct TerrainInputState(
    bool Forward,
    bool Backward,
    bool Left,
    bool Right,
    bool Up,
    bool Down,
    bool Sprint,
    bool ResetView,
    bool Looking,
    float MouseDeltaX,
    float MouseDeltaY);

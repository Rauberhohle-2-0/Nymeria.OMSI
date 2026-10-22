using System.Numerics;
using Nymeria.Graphics;
using Nymeria.Omsi.Rendering;
using Nymeria.Omsi.Terrain;

namespace Nymeria.OMSI.Tests;

/// <summary>
/// Headless tests for the terrain view's initial framing and input mapping. They use
/// the engine camera's pure math only: no window, device, or SDL call is made.
/// </summary>
public sealed class TerrainCameraTests
{
    private static readonly TerrainBounds Bounds = new(
        MinX: 0.0f,
        MinY: -2.0f,
        MinZ: -900.0f,
        MaxX: 300.0f,
        MaxY: 40.0f,
        MaxZ: 0.0f);

    [Fact]
    public void FramingStartsAboveTheTerrainLookingAtIt()
    {
        TerrainCameraFraming framing = TerrainCameraFraming.FromBounds(Bounds);

        Assert.True(framing.Position.Y > Bounds.MaxY);
        Assert.True(framing.Position.Z > Bounds.MaxZ);
        Assert.True(framing.PitchRadians < 0.0f);
        Assert.True(framing.FarPlaneMeters > framing.NearPlaneMeters);
        Assert.True(framing.MoveSpeedMetersPerSecond > 0.0f);

        Assert.True(float.IsFinite(framing.Position.X));
        Assert.True(float.IsFinite(framing.Position.Y));
        Assert.True(float.IsFinite(framing.Position.Z));
        Assert.True(float.IsFinite(framing.PitchRadians));
        Assert.True(float.IsFinite(framing.YawRadians));
        Assert.True(float.IsFinite(framing.FarPlaneMeters));
    }

    [Fact]
    public void FramingLooksTowardsTheTerrainCentre()
    {
        TerrainCameraFraming framing = TerrainCameraFraming.FromBounds(Bounds);

        var camera = new Camera(framing.Position)
        {
            YawRadians = framing.YawRadians,
            PitchRadians = framing.PitchRadians,
        };

        Vector3 centre = Bounds.Center;
        Vector3 toCentre = Vector3.Normalize(centre - framing.Position);

        Assert.True(Vector3.Dot(camera.Forward, toCentre) > 0.999f);
    }

    [Fact]
    public void ControllerAppliesTheFramingToTheEngineCamera()
    {
        TerrainCameraFraming framing = TerrainCameraFraming.FromBounds(Bounds);
        var camera = new Camera(Vector3.Zero);

        var controller = new TerrainCameraController(camera, framing);

        Assert.Equal(framing.Position, controller.Camera.Position);
        Assert.Equal(framing.YawRadians, controller.Camera.YawRadians);
        Assert.Equal(framing.FarPlaneMeters, controller.Camera.FarPlane);
        Assert.Equal(
            TerrainCameraFraming.FieldOfViewRadians,
            controller.Camera.FieldOfViewRadians);
    }

    [Fact]
    public void MovementIsFrameRateIndependent()
    {
        TerrainCameraFraming framing = TerrainCameraFraming.FromBounds(Bounds);

        var cameraA = new Camera(Vector3.Zero);
        var controllerA = new TerrainCameraController(cameraA, framing);
        controllerA.Update(Forward(), 0.05f);

        var cameraB = new Camera(Vector3.Zero);
        var controllerB = new TerrainCameraController(cameraB, framing);
        controllerB.Update(Forward(), 0.025f);
        controllerB.Update(Forward(), 0.025f);

        Assert.Equal(
            controllerA.Camera.Position.Z,
            controllerB.Camera.Position.Z,
            precision: 3);
        Assert.Equal(
            controllerA.Camera.Position.X,
            controllerB.Camera.Position.X,
            precision: 3);
    }

    [Fact]
    public void ALongFrameIsClampedSoAStallCannotThrowTheCamera()
    {
        TerrainCameraFraming framing = TerrainCameraFraming.FromBounds(Bounds);

        var stalled = new Camera(Vector3.Zero);
        var stalledController = new TerrainCameraController(stalled, framing);
        stalledController.Update(Forward(), 5.0f);

        var oneStep = new Camera(Vector3.Zero);
        var oneStepController = new TerrainCameraController(oneStep, framing);
        oneStepController.Update(
            Forward(),
            TerrainCameraController.MaximumFrameSeconds);

        Assert.Equal(
            oneStepController.Camera.Position.Z,
            stalledController.Camera.Position.Z,
            precision: 3);
    }

    [Fact]
    public void NonPositiveOrNonFiniteFrameTimesDoNotMoveTheCamera()
    {
        TerrainCameraFraming framing = TerrainCameraFraming.FromBounds(Bounds);
        var camera = new Camera(Vector3.Zero);
        var controller = new TerrainCameraController(camera, framing);

        Vector3 start = controller.Camera.Position;

        controller.Update(Forward(), 0.0f);
        controller.Update(Forward(), -1.0f);
        controller.Update(Forward(), float.NaN);

        Assert.Equal(start, controller.Camera.Position);
    }

    [Fact]
    public void ResetReturnsToTheFraming()
    {
        TerrainCameraFraming framing = TerrainCameraFraming.FromBounds(Bounds);
        var camera = new Camera(Vector3.Zero);
        var controller = new TerrainCameraController(camera, framing);

        controller.Update(Forward(), 0.05f);
        controller.Update(StrafeRight(), 0.05f);
        Assert.NotEqual(framing.Position, controller.Camera.Position);

        var reset = new TerrainInputState(
            false, false, false, false, false, false, false,
            ResetView: true, Looking: false, 0.0f, 0.0f);
        controller.Update(reset, 0.05f);

        Assert.Equal(framing.Position, controller.Camera.Position);
        Assert.Equal(framing.YawRadians, controller.Camera.YawRadians);
        Assert.Equal(framing.PitchRadians, controller.Camera.PitchRadians);
    }

    [Fact]
    public void MouseLookOnlyAppliesWhileLooking()
    {
        TerrainCameraFraming framing = TerrainCameraFraming.FromBounds(Bounds);
        var camera = new Camera(Vector3.Zero);
        var controller = new TerrainCameraController(camera, framing);

        float yaw = controller.Camera.YawRadians;

        var notLooking = new TerrainInputState(
            false, false, false, false, false, false, false,
            ResetView: false, Looking: false, MouseDeltaX: 100.0f, MouseDeltaY: 0.0f);
        controller.Update(notLooking, 0.016f);
        Assert.Equal(yaw, controller.Camera.YawRadians);

        var looking = notLooking with { Looking = true };
        controller.Update(looking, 0.016f);
        Assert.NotEqual(yaw, controller.Camera.YawRadians);
    }

    [Fact]
    public void VerticalInputMovesAlongWorldUpRegardlessOfPitch()
    {
        TerrainCameraFraming framing = TerrainCameraFraming.FromBounds(Bounds);
        var camera = new Camera(Vector3.Zero);
        var controller = new TerrainCameraController(camera, framing);

        controller.Camera.PitchRadians = -0.7f;
        Vector3 start = controller.Camera.Position;

        var ascend = new TerrainInputState(
            false, false, false, false, Up: true, false, false,
            ResetView: false, Looking: false, 0.0f, 0.0f);
        controller.Update(ascend, 0.05f);

        Vector3 moved = controller.Camera.Position - start;

        Assert.True(moved.Y > 0.0f);
        Assert.Equal(0.0f, moved.X, precision: 4);
        Assert.Equal(0.0f, moved.Z, precision: 4);
    }

    private static TerrainInputState Forward()
    {
        return new TerrainInputState(
            Forward: true,
            Backward: false,
            Left: false,
            Right: false,
            Up: false,
            Down: false,
            Sprint: false,
            ResetView: false,
            Looking: false,
            MouseDeltaX: 0.0f,
            MouseDeltaY: 0.0f);
    }

    private static TerrainInputState StrafeRight()
    {
        return Forward() with { Forward = false, Right = true };
    }
}

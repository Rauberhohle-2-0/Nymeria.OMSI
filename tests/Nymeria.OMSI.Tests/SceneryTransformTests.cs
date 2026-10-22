using System.Numerics;
using Nymeria.Omsi.Scenery;
using Nymeria.Omsi.World;

namespace Nymeria.OMSI.Tests;

/// <summary>
/// Headless tests for the OMSI placement-to-world transform.
/// </summary>
/// <remarks>
/// The conventions asserted here are the ones documented in
/// <c>docs/OMSI_SCENERY_3D.md</c>: tile-local x/y added to the tile origin, terrain-relative
/// z unless <c>[absheight]</c>, a <c>(x, y, z)</c> to <c>(x, z, -y)</c> basis change, and
/// bank-then-pitch-then-heading rotation. The heading direction is documented by the OMSI
/// reference; the pitch and bank order is this build's explicit assumption and remains
/// unverified against the game until the owner runs it on Windows.
/// </remarks>
public sealed class SceneryTransformTests
{
    private const double TileEdge = 300.0;

    [Fact]
    public void TileLocalPositionIsAddedToTheTileOrigin()
    {
        Matrix4x4 matrix = Transform(
            placement: Placement(x: 10.0, y: 20.0, z: 5.0),
            tileColumn: 2,
            tileRow: 3,
            usesAbsoluteHeight: true);

        Vector3 origin = Vector3.Transform(Vector3.Zero, matrix);

        Assert.Equal(610.0f, origin.X, precision: 4);
        Assert.Equal(5.0f, origin.Y, precision: 4);
        Assert.Equal(-920.0f, origin.Z, precision: 4);
    }

    [Fact]
    public void OmsiZBecomesWorldYAndOmsiYBecomesNegativeWorldZ()
    {
        Matrix4x4 matrix = Transform(
            Placement(x: 0.0, y: 0.0, z: 0.0),
            usesAbsoluteHeight: true);

        Vector3 omsiUp = Vector3.Transform(Vector3.UnitZ, matrix);
        Vector3 omsiNorth = Vector3.Transform(Vector3.UnitY, matrix);

        Assert.Equal(Vector3.UnitY, Round(omsiUp));
        Assert.Equal(-Vector3.UnitZ, Round(omsiNorth));
    }

    [Fact]
    public void HeadingIsClockwiseFromOmsiNorth()
    {
        Matrix4x4 matrix = Transform(
            Placement(x: 0.0, y: 0.0, z: 0.0, heading: 90.0),
            usesAbsoluteHeight: true);

        // A model whose forward axis is OMSI +y faces OMSI +x after a 90 degree
        // clockwise heading, which is world +x.
        Assert.Equal(new Vector3(1.0f, 0.0f, 0.0f), Round(Vector3.Transform(Vector3.UnitY, matrix)));
    }

    [Fact]
    public void BankRotatesAboutTheOmsiForwardAxis()
    {
        Matrix4x4 matrix = Transform(
            Placement(x: 0.0, y: 0.0, z: 0.0, bank: 90.0),
            usesAbsoluteHeight: true);

        // A point on the model's right side rises, which is a right-hand roll.
        Assert.Equal(new Vector3(0.0f, 1.0f, 0.0f), Round(Vector3.Transform(Vector3.UnitX, matrix)));
    }

    [Fact]
    public void PitchRaisesTheForwardAxis()
    {
        Matrix4x4 matrix = Transform(
            Placement(x: 0.0, y: 0.0, z: 0.0, pitch: 90.0),
            usesAbsoluteHeight: true);

        Assert.Equal(new Vector3(0.0f, 1.0f, 0.0f), Round(Vector3.Transform(Vector3.UnitY, matrix)));
    }

    [Fact]
    public void RotationPreservesLengthAndHandedness()
    {
        Matrix4x4 matrix = Transform(
            Placement(x: 1.0, y: 2.0, z: 3.0, heading: 37.0, pitch: -12.0, bank: 8.0),
            usesAbsoluteHeight: true);

        Vector3 axis = Vector3.Transform(Vector3.UnitX, matrix) -
            Vector3.Transform(Vector3.Zero, matrix);

        Assert.Equal(1.0f, axis.Length(), precision: 5);
        Assert.True(matrix.GetDeterminant() > 0.0f);
    }

    [Fact]
    public void TerrainRelativeHeightIsAddedToTheSampledGround()
    {
        Matrix4x4 withTerrain = Transform(
            Placement(x: 0.0, y: 0.0, z: 2.5),
            usesAbsoluteHeight: false,
            terrainHeight: 12.0f);

        Assert.Equal(14.5f, Vector3.Transform(Vector3.Zero, withTerrain).Y, precision: 4);
    }

    [Fact]
    public void AbsoluteHeightIgnoresTheSampledGround()
    {
        Matrix4x4 matrix = Transform(
            Placement(x: 0.0, y: 0.0, z: 2.5),
            usesAbsoluteHeight: true,
            terrainHeight: 12.0f);

        Assert.Equal(2.5f, Vector3.Transform(Vector3.Zero, matrix).Y, precision: 4);
    }

    [Fact]
    public void TerrainRelativeHeightWithoutTerrainIsReportedInsteadOfGuessed()
    {
        bool created = SceneryTransform.TryCreate(
            Placement(x: 0.0, y: 0.0, z: 2.5),
            tileColumn: 0,
            tileRow: 0,
            TileEdge,
            usesAbsoluteHeight: false,
            terrainHeightMeters: null,
            out _,
            out string? error);

        Assert.False(created);
        Assert.Contains("terrain-relative", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void NonFiniteCoordinatesAreRejected(double value)
    {
        bool created = SceneryTransform.TryCreate(
            Placement(x: value, y: 0.0, z: 0.0),
            tileColumn: 0,
            tileRow: 0,
            TileEdge,
            usesAbsoluteHeight: true,
            terrainHeightMeters: null,
            out _,
            out string? error);

        Assert.False(created);
        Assert.Contains("not finite", error, StringComparison.Ordinal);
    }

    [Fact]
    public void NonPositiveTileEdgeIsRejected()
    {
        bool created = SceneryTransform.TryCreate(
            Placement(x: 0.0, y: 0.0, z: 0.0),
            tileColumn: 0,
            tileRow: 0,
            tileEdgeMeters: 0.0,
            usesAbsoluteHeight: true,
            terrainHeightMeters: null,
            out _,
            out string? error);

        Assert.False(created);
        Assert.Contains("tile edge", error, StringComparison.Ordinal);
    }

    private static Matrix4x4 Transform(
        OmsiObjectPlacement placement,
        int tileColumn = 0,
        int tileRow = 0,
        bool usesAbsoluteHeight = false,
        float? terrainHeight = null)
    {
        bool created = SceneryTransform.TryCreate(
            placement,
            tileColumn,
            tileRow,
            TileEdge,
            usesAbsoluteHeight,
            terrainHeight,
            out Matrix4x4 matrix,
            out string? error);

        Assert.True(created, error);

        return matrix;
    }

    private static OmsiObjectPlacement Placement(
        double x = 0.0,
        double y = 0.0,
        double z = 0.0,
        double heading = 0.0,
        double? pitch = null,
        double? bank = null)
    {
        return new OmsiObjectPlacement(
            @"Sceneryobjects\Test\object.sco",
            IdCode: 1,
            DetailLevel: 0,
            X: x,
            Y: y,
            Z: z,
            HeadingDegrees: heading,
            PitchDegrees: pitch,
            BankDegrees: bank,
            Labels: [],
            LineNumber: 1);
    }

    private static Vector3 Round(Vector3 value)
    {
        return new Vector3(
            MathF.Round(value.X, 4),
            MathF.Round(value.Y, 4),
            MathF.Round(value.Z, 4));
    }
}

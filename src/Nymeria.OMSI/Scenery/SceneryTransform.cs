using System.Globalization;
using System.Numerics;
using Nymeria.Omsi.World;

namespace Nymeria.Omsi.Scenery;

/// <summary>
/// Converts one OMSI object placement into a world matrix for the engine renderer.
/// </summary>
/// <remarks>
/// <para>
/// The conversion is pure: it takes a parsed placement, its tile, the tile edge, and
/// the terrain height under it, and produces a matrix. It holds no window, device, or
/// file-system state, so every rule below is unit-tested without a GPU.
/// </para>
/// <para>
/// <b>Position.</b> A tile's records are tile-local: the documented rule is tile
/// column/row times the tile edge plus the placement's own x/y. The tile edge is
/// 300 m for an ordinary map and 371.9 m for a <c>[worldcoordinates]</c> map, which is
/// exactly what the terrain mesh uses, so an object and the ground beneath it cannot
/// disagree about tile origins.
/// </para>
/// <para>
/// <b>Height.</b> A placement's <c>z</c> is terrain-relative unless the object's
/// definition declares <c>[absheight]</c>. A relative height is added to the
/// bilinearly sampled terrain height at the object's own x/y; an absolute height is
/// used as written. A relative height without decoded terrain is not guessed: the
/// transform fails with a reason, and the caller reports the placement as skipped.
/// </para>
/// <para>
/// <b>Axes.</b> OMSI is z-up with x and y in the map plane; Nymeria is y-up with the
/// terrain spanning x and -z. The basis change is a proper rotation about x by
/// -90 degrees, so <c>(x, y, z)</c> becomes <c>(x, z, -y)</c>. A right-handed source
/// therefore stays right-handed, and triangle winding is unchanged.
/// </para>
/// <para>
/// <b>Orientation.</b> Heading, pitch, and bank are degrees. The documented statement
/// is only that heading is measured clockwise; the order and the sign of pitch and
/// bank are not stated anywhere this repository has verified. This type therefore uses
/// one explicit convention, applied to a row-vector model matrix: bank about the
/// model's y axis, then pitch about its x axis, then heading clockwise about the
/// vertical axis. The convention is recorded in docs/OMSI_SCENERY_3D.md and is the
/// first thing to check if a building appears rotated on Windows.
/// </para>
/// </remarks>
internal static class SceneryTransform
{
    /// <summary>The radians in one degree.</summary>
    internal const double DegreesToRadians = Math.PI / 180.0;

    private const double Tolerance = 1e-9;

    /// <summary>
    /// Builds the world matrix of one placement.
    /// </summary>
    /// <param name="placement">The parsed placement, with raw source values.</param>
    /// <param name="tileColumn">The tile column taken from the tile file name.</param>
    /// <param name="tileRow">The tile row taken from the tile file name.</param>
    /// <param name="tileEdgeMeters">The tile edge used by the map.</param>
    /// <param name="usesAbsoluteHeight">
    /// Whether the object's definition declares <c>[absheight]</c>, which makes the
    /// placement height absolute instead of terrain-relative.
    /// </param>
    /// <param name="terrainHeightMeters">
    /// The sampled terrain height under the placement. It is required for a
    /// terrain-relative placement and ignored for an absolute one.
    /// </param>
    /// <param name="matrix">The world matrix, when the placement can be converted.</param>
    /// <param name="error">A readable reason when it cannot.</param>
    /// <returns><see langword="true"/> when a matrix was produced.</returns>
    internal static bool TryCreate(
        OmsiObjectPlacement placement,
        int tileColumn,
        int tileRow,
        double tileEdgeMeters,
        bool usesAbsoluteHeight,
        float? terrainHeightMeters,
        out Matrix4x4 matrix,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(placement);

        matrix = Matrix4x4.Identity;
        error = null;

        if (!double.IsFinite(tileEdgeMeters) || tileEdgeMeters <= 0.0)
        {
            error = "The tile edge is not a positive length.";
            return false;
        }

        if (!IsFinite(placement.X) || !IsFinite(placement.Y) || !IsFinite(placement.Z))
        {
            error = "The placement position is not finite.";
            return false;
        }

        double heading = placement.HeadingDegrees;
        double pitch = placement.PitchDegrees ?? 0.0;
        double bank = placement.BankDegrees ?? 0.0;

        if (!IsFinite(heading) || !IsFinite(pitch) || !IsFinite(bank))
        {
            error = "The placement orientation is not finite.";
            return false;
        }

        double height = placement.Z;

        if (!usesAbsoluteHeight)
        {
            if (terrainHeightMeters is not float terrain ||
                !float.IsFinite(terrain))
            {
                error =
                    "The object's height is terrain-relative, but no decoded terrain " +
                    "covers its position.";
                return false;
            }

            height += terrain;
        }

        double x = (tileColumn * tileEdgeMeters) + placement.X;
        double y = (tileRow * tileEdgeMeters) + placement.Y;

        if (!IsFinite(x) || !IsFinite(y) || !IsFinite(height))
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"The tile origin plus the placement ({x}, {y}, {height}) is not finite.");
            return false;
        }

        // The signs make a positive pitch raise the forward axis and a positive bank
        // raise the right side, which is the reading documented in the remarks
        // above. Heading stays clockwise, as OMSI documents.
        Matrix4x4 rotation =
            Matrix4x4.CreateRotationZ((float)(-heading * DegreesToRadians)) *
            Matrix4x4.CreateRotationX((float)(pitch * DegreesToRadians)) *
            Matrix4x4.CreateRotationY((float)(-bank * DegreesToRadians));

        rotation.Translation = new Vector3((float)x, (float)y, (float)height);

        // The model matrix is built in OMSI axes, and the basis change is applied
        // last, so a model-space point becomes an OMSI world point and then a renderer
        // world point: p_world = p_model * rotation * WorldFromOmsi.
        matrix = rotation * WorldFromOmsi;

        if (!IsFinite(matrix))
        {
            error = "The computed world matrix is not finite.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// The basis change from OMSI coordinates to Nymeria world coordinates, as a
    /// row-vector matrix: <c>(x, y, z)</c> becomes <c>(x, z, -y)</c>.
    /// </summary>
    internal static Matrix4x4 WorldFromOmsi => new(
        1.0f, 0.0f, 0.0f, 0.0f,
        0.0f, 0.0f, -1.0f, 0.0f,
        0.0f, 1.0f, 0.0f, 0.0f,
        0.0f, 0.0f, 0.0f, 1.0f);

    /// <summary>
    /// The inverse of <see cref="WorldFromOmsi"/>. It is the transpose, because the
    /// basis change is a rotation.
    /// </summary>
    internal static Matrix4x4 OmsiFromWorld => Matrix4x4.Transpose(WorldFromOmsi);

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static bool IsFinite(Matrix4x4 value)
    {
        return
            float.IsFinite(value.M11) && float.IsFinite(value.M12) &&
            float.IsFinite(value.M13) && float.IsFinite(value.M14) &&
            float.IsFinite(value.M21) && float.IsFinite(value.M22) &&
            float.IsFinite(value.M23) && float.IsFinite(value.M24) &&
            float.IsFinite(value.M31) && float.IsFinite(value.M32) &&
            float.IsFinite(value.M33) && float.IsFinite(value.M34) &&
            float.IsFinite(value.M41) && float.IsFinite(value.M42) &&
            float.IsFinite(value.M43) && float.IsFinite(value.M44);
    }

    private static bool IsFinite(float value) => float.IsFinite(value);
}

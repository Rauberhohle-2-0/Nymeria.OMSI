using System.Numerics;

namespace Nymeria.Omsi.Terrain;

/// <summary>
/// The diagnostic colour ramp used to make elevation visible.
/// </summary>
/// <remarks>
/// <para>
/// This is not an OMSI ground texture and does not pretend to be one. It maps a
/// height normalized over the loaded terrain to a readable sequence of colours, so a
/// hill and a depression are distinguishable at a glance. When every decoded height
/// is the same, the ramp returns its neutral middle colour instead of dividing by
/// zero.
/// </para>
/// <para>
/// The ramp is pure, so its stops and clamping are unit-tested.
/// </para>
/// </remarks>
internal static class TerrainColor
{
    private static readonly Vector3 Low = new(0.10f, 0.26f, 0.46f);

    private static readonly Vector3 LowMid = new(0.24f, 0.55f, 0.27f);

    private static readonly Vector3 HighMid = new(0.56f, 0.44f, 0.30f);

    private static readonly Vector3 High = new(0.93f, 0.93f, 0.96f);

    /// <summary>Gets the colour used when the terrain has no height range.</summary>
    internal static Vector3 Flat => LowMid;

    /// <summary>
    /// Maps a normalized height to a colour.
    /// </summary>
    /// <param name="normalizedHeight">
    /// The height as a 0..1 share of the loaded range. Values outside the range are
    /// clamped, and a non-finite value maps to <see cref="Flat"/>.
    /// </param>
    internal static Vector3 FromNormalizedHeight(float normalizedHeight)
    {
        if (!float.IsFinite(normalizedHeight))
        {
            return Flat;
        }

        float t = Math.Clamp(normalizedHeight, 0.0f, 1.0f);

        return t switch
        {
            <= 0.33f => Lerp(Low, LowMid, t / 0.33f),
            <= 0.66f => Lerp(LowMid, HighMid, (t - 0.33f) / 0.33f),
            _ => Lerp(HighMid, High, (t - 0.66f) / 0.34f),
        };
    }

    /// <summary>
    /// Maps a height in metres to a colour, normalized over the loaded range.
    /// </summary>
    /// <param name="height">The height in metres.</param>
    /// <param name="minimum">The lowest loaded height, in metres.</param>
    /// <param name="maximum">The highest loaded height, in metres.</param>
    internal static Vector3 FromHeight(float height, float minimum, float maximum)
    {
        float range = maximum - minimum;

        if (!float.IsFinite(height) || !float.IsFinite(range) || range <= 0.0f)
        {
            return Flat;
        }

        return FromNormalizedHeight((height - minimum) / range);
    }

    private static Vector3 Lerp(Vector3 from, Vector3 to, float amount)
    {
        return from + ((to - from) * Math.Clamp(amount, 0.0f, 1.0f));
    }
}

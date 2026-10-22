namespace Nymeria.Omsi.Rendering;

/// <summary>
/// The mapping between OMSI map metres and diagnostic-image pixels.
/// </summary>
/// <remarks>
/// <para>
/// The plan is drawn with OMSI x to the right and OMSI y upwards, so a larger y is
/// higher on screen. As <see cref="OmsiMapPlan"/> notes, that is a data-axis choice,
/// not a claim about north. The centre of the image is
/// (<see cref="CenterX"/>, <see cref="CenterY"/>) and one pixel covers
/// <see cref="MetresPerPixel"/> metres.
/// </para>
/// <para>
/// This type is pure: it holds no GPU, window, or SDL state, so framing, panning,
/// and zooming are unit-tested without a device.
/// </para>
/// </remarks>
internal sealed class MapPlanView
{
    /// <summary>The closest zoom accepted, in metres per pixel.</summary>
    internal const double MinimumMetresPerPixel = 0.02;

    /// <summary>The furthest zoom accepted, in metres per pixel.</summary>
    internal const double MaximumMetresPerPixel = 50_000.0;

    /// <summary>The fraction of the image kept clear around a framed plan.</summary>
    internal const double DefaultMarginFraction = 0.04;

    internal MapPlanView(
        int pixelWidth,
        int pixelHeight,
        double centerX,
        double centerY,
        double metresPerPixel)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pixelWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pixelHeight, 1);

        if (!double.IsFinite(centerX) || !double.IsFinite(centerY))
        {
            throw new ArgumentException(
                "The view centre must be finite.",
                nameof(centerX));
        }

        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        CenterX = centerX;
        CenterY = centerY;
        MetresPerPixel = Math.Clamp(
            metresPerPixel,
            MinimumMetresPerPixel,
            MaximumMetresPerPixel);
    }

    internal int PixelWidth { get; }

    internal int PixelHeight { get; }

    internal double CenterX { get; private set; }

    internal double CenterY { get; private set; }

    internal double MetresPerPixel { get; private set; }

    /// <summary>
    /// Creates a view that shows the whole plan with a small margin.
    /// </summary>
    /// <param name="bounds">The extent to frame.</param>
    /// <param name="pixelWidth">The image width in pixels.</param>
    /// <param name="pixelHeight">The image height in pixels.</param>
    internal static MapPlanView Frame(PlanBounds bounds, int pixelWidth, int pixelHeight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pixelWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pixelHeight, 1);

        double spanX = Math.Max(bounds.Width, 1.0);
        double spanY = Math.Max(bounds.Height, 1.0);
        double scale = Math.Max(spanX / pixelWidth, spanY / pixelHeight);
        double metresPerPixel =
            scale * (1.0 + (2.0 * DefaultMarginFraction));

        return new MapPlanView(
            pixelWidth,
            pixelHeight,
            (bounds.MinX + bounds.MaxX) / 2.0,
            (bounds.MinY + bounds.MaxY) / 2.0,
            metresPerPixel);
    }

    /// <summary>Converts a map position to a pixel position.</summary>
    /// <returns>
    /// The pixel position, where (0,0) is the top-left corner. The result is not
    /// rounded and may lie outside the image.
    /// </returns>
    internal (double X, double Y) WorldToPixel(double worldX, double worldY)
    {
        double pixelX = ((worldX - CenterX) / MetresPerPixel) + (PixelWidth / 2.0);
        double pixelY = (PixelHeight / 2.0) - ((worldY - CenterY) / MetresPerPixel);

        return (pixelX, pixelY);
    }

    /// <summary>Converts a pixel position to a map position.</summary>
    internal (double X, double Y) PixelToWorld(double pixelX, double pixelY)
    {
        double worldX = ((pixelX - (PixelWidth / 2.0)) * MetresPerPixel) + CenterX;
        double worldY = (((PixelHeight / 2.0) - pixelY) * MetresPerPixel) + CenterY;

        return (worldX, worldY);
    }

    /// <summary>
    /// Moves the view by a drag in pixels, keeping the map reachable.
    /// </summary>
    /// <param name="deltaPixelsX">Horizontal drag; positive moves the plan right.</param>
    /// <param name="deltaPixelsY">Vertical drag; positive moves the plan down.</param>
    /// <param name="bounds">The extent the centre is kept near.</param>
    internal void PanPixels(double deltaPixelsX, double deltaPixelsY, PlanBounds bounds)
    {
        if (!double.IsFinite(deltaPixelsX) || !double.IsFinite(deltaPixelsY))
        {
            return;
        }

        CenterX -= deltaPixelsX * MetresPerPixel;
        CenterY += deltaPixelsY * MetresPerPixel;

        ClampCenter(bounds);
    }

    /// <summary>
    /// Scales the view by a factor, keeping the centre fixed.
    /// </summary>
    /// <param name="factor">
    /// Values below one zoom in, values above one zoom out. A non-finite or
    /// non-positive factor is ignored.
    /// </param>
    internal void Zoom(double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0.0)
        {
            return;
        }

        MetresPerPixel = Math.Clamp(
            MetresPerPixel * factor,
            MinimumMetresPerPixel,
            MaximumMetresPerPixel);
    }

    /// <summary>Restores the framing that shows the whole plan.</summary>
    internal void Reset(PlanBounds bounds)
    {
        MapPlanView framed = Frame(bounds, PixelWidth, PixelHeight);

        CenterX = framed.CenterX;
        CenterY = framed.CenterY;
        MetresPerPixel = framed.MetresPerPixel;
    }

    /// <summary>
    /// Keeps the centre inside the plan plus one screen of slack, so panning cannot
    /// lose the map entirely.
    /// </summary>
    private void ClampCenter(PlanBounds bounds)
    {
        double slackX = PixelWidth * MetresPerPixel;
        double slackY = PixelHeight * MetresPerPixel;

        CenterX = Math.Clamp(CenterX, bounds.MinX - slackX, bounds.MaxX + slackX);
        CenterY = Math.Clamp(CenterY, bounds.MinY - slackY, bounds.MaxY + slackY);
    }
}

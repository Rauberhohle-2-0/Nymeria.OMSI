namespace Nymeria.Omsi.Rendering;

/// <summary>
/// What one rasterization pass actually drew.
/// </summary>
/// <param name="Tiles">How many tile squares were drawn.</param>
/// <param name="Objects">How many object markers were drawn.</param>
/// <param name="Splines">How many spline markers were drawn.</param>
internal readonly record struct PlanDrawStatistics(
    int Tiles,
    int Objects,
    int Splines);

/// <summary>
/// Draws a parsed map plan into a <see cref="MapPlanImage"/>.
/// </summary>
/// <remarks>
/// <para>
/// The drawing is deliberately plain: tile squares with an outline, then spline
/// markers, then object markers on top. Every mark corresponds to a record the
/// parser actually read. Nothing here interpolates, extrudes, or textures, so a
/// dense cluster of markers means a dense cluster of records, not scenery.
/// </para>
/// <para>
/// The pass is deterministic and independent of the graphics device, so it is
/// unit-tested headlessly.
/// </para>
/// </remarks>
internal static class MapPlanRasterizer
{
    /// <summary>The marker radius used for placements, in pixels.</summary>
    internal const int MarkerRadius = 1;

    /// <summary>
    /// Renders the plan into the image.
    /// </summary>
    /// <param name="plan">The geometry to draw.</param>
    /// <param name="view">The projection to draw it with.</param>
    /// <param name="image">The destination image.</param>
    /// <returns>What was actually drawn.</returns>
    internal static PlanDrawStatistics Render(
        OmsiMapPlan plan,
        MapPlanView view,
        MapPlanImage image)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(image);

        image.Fill(PlanColor.Background);

        int tilesDrawn = 0;

        foreach (PlanRect tile in plan.Tiles)
        {
            (double left, double top) = view.WorldToPixel(tile.MinX, tile.MaxY);
            (double right, double bottom) = view.WorldToPixel(tile.MaxX, tile.MinY);

            int x = (int)Math.Floor(left);
            int y = (int)Math.Floor(top);
            int width = (int)Math.Ceiling(right) - x;
            int height = (int)Math.Ceiling(bottom) - y;

            if (width <= 0 || height <= 0 ||
                x >= image.Width || y >= image.Height ||
                x + width <= 0 || y + height <= 0)
            {
                continue;
            }

            image.FillRectangle(x, y, width, height, PlanColor.TileFill);
            image.DrawRectangleOutline(x, y, width, height, PlanColor.TileBorder);
            tilesDrawn++;
        }

        int splinesDrawn = 0;

        foreach (PlanPoint point in plan.Splines)
        {
            (double pixelX, double pixelY) = view.WorldToPixel(point.X, point.Y);

            if (!IsInside(image, pixelX, pixelY))
            {
                continue;
            }

            image.DrawMarker(pixelX, pixelY, MarkerRadius, PlanColor.Spline);
            splinesDrawn++;
        }

        int objectsDrawn = 0;

        foreach (PlanPoint point in plan.Objects)
        {
            (double pixelX, double pixelY) = view.WorldToPixel(point.X, point.Y);

            if (!IsInside(image, pixelX, pixelY))
            {
                continue;
            }

            image.DrawMarker(pixelX, pixelY, MarkerRadius, PlanColor.Object);
            objectsDrawn++;
        }

        return new PlanDrawStatistics(tilesDrawn, objectsDrawn, splinesDrawn);
    }

    private static bool IsInside(MapPlanImage image, double pixelX, double pixelY)
    {
        return pixelX >= 0.0 &&
            pixelY >= 0.0 &&
            pixelX < image.Width &&
            pixelY < image.Height;
    }
}

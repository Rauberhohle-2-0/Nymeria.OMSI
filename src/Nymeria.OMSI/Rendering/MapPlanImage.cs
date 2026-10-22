namespace Nymeria.Omsi.Rendering;

/// <summary>
/// An opaque RGBA8 color for the diagnostic plan.
/// </summary>
/// <param name="R">The red channel.</param>
/// <param name="G">The green channel.</param>
/// <param name="B">The blue channel.</param>
internal readonly record struct PlanColor(byte R, byte G, byte B)
{
    internal static PlanColor Background => new(0x0D, 0x12, 0x19);

    internal static PlanColor TileFill => new(0x16, 0x20, 0x2B);

    internal static PlanColor TileBorder => new(0x3E, 0x55, 0x66);

    internal static PlanColor Spline => new(0x4F, 0xA3, 0xD1);

    internal static PlanColor Object => new(0xE0, 0xB0, 0x40);
}

/// <summary>
/// A tightly packed RGBA8 image the diagnostic plan is drawn into.
/// </summary>
/// <remarks>
/// The buffer is plain managed memory with no graphics dependency, so the
/// rasterizer is unit-tested without a window or a device. Every pixel is fully
/// opaque; the image is uploaded once per view change and scaled to the window by
/// the presenter.
/// </remarks>
internal sealed class MapPlanImage
{
    internal const int BytesPerPixel = 4;

    internal MapPlanImage(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        Width = width;
        Height = height;
        Pixels = new byte[checked(width * height * BytesPerPixel)];
    }

    internal int Width { get; }

    internal int Height { get; }

    internal byte[] Pixels { get; }

    /// <summary>Reads one pixel, or returns the background outside the image.</summary>
    internal PlanColor this[int x, int y]
    {
        get
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height)
            {
                return PlanColor.Background;
            }

            int offset = ((y * Width) + x) * BytesPerPixel;
            return new PlanColor(Pixels[offset], Pixels[offset + 1], Pixels[offset + 2]);
        }
    }

    /// <summary>Fills every pixel with one color.</summary>
    internal void Fill(PlanColor color)
    {
        for (int offset = 0; offset < Pixels.Length; offset += BytesPerPixel)
        {
            Write(offset, color);
        }
    }

    /// <summary>Sets one pixel, ignoring positions outside the image.</summary>
    internal void SetPixel(int x, int y, PlanColor color)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
        {
            return;
        }

        Write(((y * Width) + x) * BytesPerPixel, color);
    }

    /// <summary>Fills a rectangle, clipping it to the image.</summary>
    internal void FillRectangle(int x, int y, int width, int height, PlanColor color)
    {
        int left = Math.Max(x, 0);
        int top = Math.Max(y, 0);
        int right = (int)Math.Min((long)x + width, Width);
        int bottom = (int)Math.Min((long)y + height, Height);

        for (int row = top; row < bottom; row++)
        {
            for (int column = left; column < right; column++)
            {
                Write(((row * Width) + column) * BytesPerPixel, color);
            }
        }
    }

    /// <summary>Draws a one-pixel rectangle outline, clipping it to the image.</summary>
    internal void DrawRectangleOutline(int x, int y, int width, int height, PlanColor color)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        int right = x + width - 1;
        int bottom = y + height - 1;

        FillRectangle(x, y, width, 1, color);
        FillRectangle(x, bottom, width, 1, color);
        FillRectangle(x, y, 1, height, color);
        FillRectangle(right, y, 1, height, color);
    }

    /// <summary>
    /// Draws a filled square of the given radius around a pixel position.
    /// </summary>
    /// <param name="centerX">The horizontal center, in pixels.</param>
    /// <param name="centerY">The vertical center, in pixels.</param>
    /// <param name="radius">Half the square's edge; 1 draws a 3 x 3 marker.</param>
    /// <param name="color">The marker color.</param>
    internal void DrawMarker(double centerX, double centerY, int radius, PlanColor color)
    {
        if (!double.IsFinite(centerX) || !double.IsFinite(centerY))
        {
            return;
        }

        int x = (int)Math.Round(centerX);
        int y = (int)Math.Round(centerY);
        int size = (2 * radius) + 1;

        FillRectangle(x - radius, y - radius, size, size, color);
    }

    private void Write(int offset, PlanColor color)
    {
        Pixels[offset] = color.R;
        Pixels[offset + 1] = color.G;
        Pixels[offset + 2] = color.B;
        Pixels[offset + 3] = 0xFF;
    }
}

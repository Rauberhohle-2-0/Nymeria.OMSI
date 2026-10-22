using Nymeria.Omsi.Rendering;

namespace Nymeria.OMSI.Tests;

/// <summary>
/// Headless tests for the plan-view math. They create no window and no device.
/// </summary>
public sealed class MapPlanViewTests
{
    private static readonly PlanBounds UnitBounds = new(-100, -100, 100, 100);

    [Fact]
    public void FramingCentresThePlanAndFitsItInsideTheImage()
    {
        MapPlanView view = MapPlanView.Frame(UnitBounds, 400, 200);

        Assert.Equal(0.0, view.CenterX);
        Assert.Equal(0.0, view.CenterY);
        Assert.True(view.MetresPerPixel > 0.5);

        (double left, double top) = view.WorldToPixel(-100, 100);
        (double right, double bottom) = view.WorldToPixel(100, -100);

        Assert.True(left > 0 && left < 400);
        Assert.True(top > 0 && top < 200);
        Assert.True(right > 0 && right < 400);
        Assert.True(bottom > 0 && bottom < 200);
    }

    [Fact]
    public void ThePlansCentreMapsToTheImagesCentre()
    {
        MapPlanView view = MapPlanView.Frame(UnitBounds, 400, 200);

        (double x, double y) = view.WorldToPixel(0, 0);

        Assert.Equal(200.0, x, 6);
        Assert.Equal(100.0, y, 6);
    }

    [Fact]
    public void LargerXIsRightAndLargerYIsUp()
    {
        var view = new MapPlanView(400, 200, 0, 0, 1.0);

        (double rightX, double rightY) = view.WorldToPixel(10, 0);
        (double upX, double upY) = view.WorldToPixel(0, 10);

        Assert.True(rightX > 200);
        Assert.Equal(100.0, rightY, 6);
        Assert.Equal(200.0, upX, 6);
        Assert.True(upY < 100);
    }

    [Fact]
    public void WorldAndPixelConversionsRoundTrip()
    {
        var view = new MapPlanView(640, 480, 1234.5, -678.25, 2.5);

        (double worldX, double worldY) = view.PixelToWorld(100, 400);
        (double pixelX, double pixelY) = view.WorldToPixel(worldX, worldY);

        Assert.Equal(100.0, pixelX, 6);
        Assert.Equal(400.0, pixelY, 6);
    }

    [Fact]
    public void PanningMovesThePlanAndKeepsItReachable()
    {
        MapPlanView view = MapPlanView.Frame(UnitBounds, 400, 200);

        view.PanPixels(40, 0, UnitBounds);
        Assert.True(view.CenterX < 0);

        // A huge drag cannot lose the plan entirely.
        view.PanPixels(1e9, 1e9, UnitBounds);

        Assert.True(view.CenterX >= UnitBounds.MinX - (400 * view.MetresPerPixel));
        Assert.True(view.CenterY <= UnitBounds.MaxY + (200 * view.MetresPerPixel));
    }

    [Fact]
    public void ZoomIsClampedToItsLimits()
    {
        var view = new MapPlanView(400, 200, 0, 0, 1.0);

        view.Zoom(1e-9);
        Assert.Equal(MapPlanView.MinimumMetresPerPixel, view.MetresPerPixel);

        view.Zoom(1e9);
        Assert.Equal(MapPlanView.MaximumMetresPerPixel, view.MetresPerPixel);
    }

    [Fact]
    public void InvalidZoomFactorsAreIgnored()
    {
        var view = new MapPlanView(400, 200, 0, 0, 2.0);

        view.Zoom(double.NaN);
        view.Zoom(0);
        view.Zoom(-1);

        Assert.Equal(2.0, view.MetresPerPixel);
    }

    [Fact]
    public void ResetRestoresTheFramedView()
    {
        MapPlanView view = MapPlanView.Frame(UnitBounds, 400, 200);
        double framedScale = view.MetresPerPixel;

        view.Zoom(0.25);
        view.PanPixels(500, 500, UnitBounds);
        view.Reset(UnitBounds);

        Assert.Equal(0.0, view.CenterX, 9);
        Assert.Equal(0.0, view.CenterY, 9);
        Assert.Equal(framedScale, view.MetresPerPixel, 9);
    }

    [Fact]
    public void NonFiniteCentreIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new MapPlanView(100, 100, double.NaN, 0, 1.0));
    }
}

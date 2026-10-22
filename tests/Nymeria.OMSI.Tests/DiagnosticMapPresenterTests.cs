using Nymeria.Omsi.Rendering;

namespace Nymeria.OMSI.Tests;

/// <summary>
/// Headless tests for the presenter's pure geometry. No window, device, or native
/// library is created, so the GPU blit itself is not exercised here.
/// </summary>
public sealed class DiagnosticMapPresenterTests
{
    [Fact]
    public void SameAspectFillsTheDestination()
    {
        (uint width, uint height) = DiagnosticMapPresenter.Fit(1280, 720, 1920, 1080);

        Assert.Equal(1920u, width);
        Assert.Equal(1080u, height);
    }

    [Fact]
    public void ATallerDestinationLetterboxesTheImage()
    {
        (uint width, uint height) = DiagnosticMapPresenter.Fit(1280, 720, 1000, 1000);

        Assert.Equal(1000u, width);
        Assert.Equal(563u, height);
    }

    [Fact]
    public void AWiderDestinationPillarboxesTheImage()
    {
        (uint width, uint height) = DiagnosticMapPresenter.Fit(1280, 720, 2000, 500);

        Assert.Equal(889u, width);
        Assert.Equal(500u, height);
    }

    [Theory]
    [InlineData(0u, 720u, 800u, 600u)]
    [InlineData(1280u, 0u, 800u, 600u)]
    [InlineData(1280u, 720u, 0u, 600u)]
    [InlineData(1280u, 720u, 800u, 0u)]
    public void ADegenerateSizeFitsNothing(
        uint sourceWidth,
        uint sourceHeight,
        uint destinationWidth,
        uint destinationHeight)
    {
        (uint width, uint height) = DiagnosticMapPresenter.Fit(
            sourceWidth,
            sourceHeight,
            destinationWidth,
            destinationHeight);

        Assert.Equal(0u, width);
        Assert.Equal(0u, height);
    }

    [Fact]
    public void TheDiagnosticImageIsSixteenByNine()
    {
        Assert.Equal(
            DiagnosticMapPresenter.ImageWidth * 9,
            DiagnosticMapPresenter.ImageHeight * 16);
    }
}

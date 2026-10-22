using Nymeria.Omsi;

namespace Nymeria.OMSI.Tests;

/// <summary>
/// Headless tests for the bootstrap host's command-line contract. They create no
/// window, no GPU device, and no audio device, so they run on CI.
/// </summary>
public sealed class HostOptionsTests
{
    [Fact]
    public void NoArgumentsRunsUntilTheWindowCloses()
    {
        bool parsed = HostOptions.TryParse(
            Array.Empty<string>(),
            out HostOptions options,
            out string? error);

        Assert.True(parsed);
        Assert.Null(error);
        Assert.False(options.ShowHelp);
        Assert.Null(options.MaximumFrames);
        Assert.Null(options.MapDirectory);
    }

    [Fact]
    public void SmokeArgumentLimitsTheFrameLoop()
    {
        bool parsed = HostOptions.TryParse(
            new[] { "--smoke" },
            out HostOptions options,
            out string? error);

        Assert.True(parsed);
        Assert.Null(error);
        Assert.Equal(HostOptions.SmokeFrameCount, options.MaximumFrames);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public void HelpArgumentRequestsUsage(string argument)
    {
        bool parsed = HostOptions.TryParse(
            new[] { argument },
            out HostOptions options,
            out string? error);

        Assert.True(parsed);
        Assert.Null(error);
        Assert.True(options.ShowHelp);
        Assert.Null(options.MaximumFrames);
    }

    [Fact]
    public void HelpWinsOverSmokeSoNoNativeLibraryIsNeededForUsage()
    {
        bool parsed = HostOptions.TryParse(
            new[] { "--smoke", "--help" },
            out HostOptions options,
            out _);

        Assert.True(parsed);
        Assert.True(options.ShowHelp);
    }

    [Fact]
    public void UnknownArgumentIsRejectedWithItsName()
    {
        bool parsed = HostOptions.TryParse(
            new[] { "--visualize", "Grundorf" },
            out _,
            out string? error);

        Assert.False(parsed);
        Assert.NotNull(error);
        Assert.Contains("--visualize", error, StringComparison.Ordinal);
    }

    [Fact]
    public void MapArgumentCapturesTheDirectory()
    {
        bool parsed = HostOptions.TryParse(
            new[] { "--map", "maps/TestMap" },
            out HostOptions options,
            out string? error);

        Assert.True(parsed);
        Assert.Null(error);
        Assert.Equal("maps/TestMap", options.MapDirectory);
        Assert.Null(options.MaximumFrames);
    }

    [Fact]
    public void MapArgumentWithoutAValueIsRejected()
    {
        bool parsed = HostOptions.TryParse(
            new[] { "--map" },
            out _,
            out string? error);

        Assert.False(parsed);
        Assert.NotNull(error);
        Assert.Contains("--map", error, StringComparison.Ordinal);
    }

    [Fact]
    public void MapArgumentRejectsAnotherOptionAsItsValue()
    {
        bool parsed = HostOptions.TryParse(
            new[] { "--map", "--smoke" },
            out _,
            out string? error);

        Assert.False(parsed);
        Assert.NotNull(error);
        Assert.Contains("--map", error, StringComparison.Ordinal);
    }

    [Fact]
    public void MapArgumentCannotBeRepeated()
    {
        bool parsed = HostOptions.TryParse(
            new[] { "--map", "first", "--map", "second" },
            out _,
            out string? error);

        Assert.False(parsed);
        Assert.NotNull(error);
        Assert.Contains("more than once", error, StringComparison.Ordinal);
    }

    [Fact]
    public void MapCannotBeCombinedWithSmoke()
    {
        bool parsed = HostOptions.TryParse(
            new[] { "--map", "maps/TestMap", "--smoke" },
            out _,
            out string? error);

        Assert.False(parsed);
        Assert.NotNull(error);
        Assert.Contains("cannot be combined", error, StringComparison.Ordinal);
    }

    [Fact]
    public void WorldArgumentCapturesTheDirectory()
    {
        bool parsed = HostOptions.TryParse(
            new[] { "--world", "maps/TestMap" },
            out HostOptions options,
            out string? error);

        Assert.True(parsed);
        Assert.Null(error);
        Assert.Equal("maps/TestMap", options.WorldDirectory);
        Assert.Null(options.MapDirectory);
        Assert.Null(options.MaximumFrames);
    }

    [Fact]
    public void WorldArgumentWithoutAValueIsRejected()
    {
        bool parsed = HostOptions.TryParse(
            new[] { "--world" },
            out _,
            out string? error);

        Assert.False(parsed);
        Assert.NotNull(error);
        Assert.Contains("--world", error, StringComparison.Ordinal);
    }

    [Fact]
    public void WorldArgumentCannotBeRepeated()
    {
        bool parsed = HostOptions.TryParse(
            new[] { "--world", "first", "--world", "second" },
            out _,
            out string? error);

        Assert.False(parsed);
        Assert.NotNull(error);
        Assert.Contains("more than once", error, StringComparison.Ordinal);
    }

    [Fact]
    public void WorldCannotBeCombinedWithSmokeOrMap()
    {
        Assert.False(HostOptions.TryParse(
            new[] { "--world", "maps/TestMap", "--smoke" },
            out _,
            out string? smokeError));
        Assert.Contains("cannot be combined", smokeError, StringComparison.Ordinal);

        Assert.False(HostOptions.TryParse(
            new[] { "--map", "maps/TestMap", "--world", "maps/TestMap" },
            out _,
            out string? mapError));
        Assert.Contains("cannot be combined", mapError, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderRequiresAMapAndIsOffByDefault()
    {
        bool parsed = HostOptions.TryParse(
            Array.Empty<string>(),
            out HostOptions plain,
            out _);

        Assert.True(parsed);
        Assert.False(plain.RenderMap);

        Assert.False(HostOptions.TryParse(
            new[] { "--render" },
            out _,
            out string? error));
        Assert.NotNull(error);
        Assert.Contains("--render requires --map", error, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderIsAcceptedWithAMap()
    {
        bool parsed = HostOptions.TryParse(
            new[] { "--map", "maps/TestMap", "--render" },
            out HostOptions options,
            out string? error);

        Assert.True(parsed);
        Assert.Null(error);
        Assert.True(options.RenderMap);
        Assert.Equal("maps/TestMap", options.MapDirectory);
        Assert.Null(options.MaximumFrames);
    }

    [Fact]
    public void SmokeCanBudgetARenderRun()
    {
        bool parsed = HostOptions.TryParse(
            new[] { "--map", "maps/TestMap", "--render", "--smoke" },
            out HostOptions options,
            out string? error);

        Assert.True(parsed);
        Assert.Null(error);
        Assert.True(options.RenderMap);
        Assert.Equal(HostOptions.SmokeFrameCount, options.MaximumFrames);
    }

    [Fact]
    public void RenderCannotBeCombinedWithWorldInspection()
    {
        bool parsed = HostOptions.TryParse(
            new[] { "--world", "maps/TestMap", "--render" },
            out _,
            out string? error);

        Assert.False(parsed);
        Assert.NotNull(error);
        Assert.Contains("cannot be combined", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Terrain3DIsOffByDefaultAndRequiresAMap()
    {
        Assert.True(HostOptions.TryParse(
            Array.Empty<string>(),
            out HostOptions plain,
            out _));
        Assert.False(plain.Terrain3D);

        Assert.False(HostOptions.TryParse(
            new[] { "--terrain-3d" },
            out _,
            out string? error));
        Assert.NotNull(error);
        Assert.Contains("--terrain-3d requires --map", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Terrain3DIsAcceptedWithAMap()
    {
        bool parsed = HostOptions.TryParse(
            new[] { "--map", "maps/TestMap", "--terrain-3d" },
            out HostOptions options,
            out string? error);

        Assert.True(parsed);
        Assert.Null(error);
        Assert.True(options.Terrain3D);
        Assert.False(options.RenderMap);
        Assert.Equal("maps/TestMap", options.MapDirectory);
        Assert.Null(options.MaximumFrames);
    }

    [Fact]
    public void Terrain3DCannotBeCombinedWithRenderOrWorld()
    {
        Assert.False(HostOptions.TryParse(
            new[] { "--map", "maps/TestMap", "--render", "--terrain-3d" },
            out _,
            out string? renderError));
        Assert.Contains("cannot be combined", renderError, StringComparison.Ordinal);

        Assert.False(HostOptions.TryParse(
            new[] { "--world", "maps/TestMap", "--terrain-3d" },
            out _,
            out string? worldError));
        Assert.Contains("cannot be combined", worldError, StringComparison.Ordinal);
    }

    [Fact]
    public void SmokeCanBudgetATerrainRun()
    {
        bool parsed = HostOptions.TryParse(
            new[] { "--map", "maps/TestMap", "--terrain-3d", "--smoke" },
            out HostOptions options,
            out string? error);

        Assert.True(parsed);
        Assert.Null(error);
        Assert.True(options.Terrain3D);
        Assert.Equal(HostOptions.SmokeFrameCount, options.MaximumFrames);
    }

    [Fact]
    public void UsageTextDocumentsEverySupportedArgument()
    {
        Assert.Contains("--smoke", HostOptions.UsageText, StringComparison.Ordinal);
        Assert.Contains("--map", HostOptions.UsageText, StringComparison.Ordinal);
        Assert.Contains("--render", HostOptions.UsageText, StringComparison.Ordinal);
        Assert.Contains("--terrain-3d", HostOptions.UsageText, StringComparison.Ordinal);
        Assert.Contains("--world", HostOptions.UsageText, StringComparison.Ordinal);
        Assert.Contains("--help", HostOptions.UsageText, StringComparison.Ordinal);
    }

    [Fact]
    public void UsageTextDocumentsTheTerrainCameraControls()
    {
        Assert.Contains("W/A/S/D", HostOptions.UsageText, StringComparison.Ordinal);
        Assert.Contains("right mouse button", HostOptions.UsageText, StringComparison.Ordinal);
        Assert.Contains("R resets the view", HostOptions.UsageText, StringComparison.Ordinal);
    }
}

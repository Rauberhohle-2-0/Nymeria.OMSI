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
            new[] { "--render", "Grundorf" },
            out _,
            out string? error);

        Assert.False(parsed);
        Assert.NotNull(error);
        Assert.Contains("--render", error, StringComparison.Ordinal);
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
    public void UsageTextDocumentsEverySupportedArgument()
    {
        Assert.Contains("--smoke", HostOptions.UsageText, StringComparison.Ordinal);
        Assert.Contains("--map", HostOptions.UsageText, StringComparison.Ordinal);
        Assert.Contains("--help", HostOptions.UsageText, StringComparison.Ordinal);
    }
}

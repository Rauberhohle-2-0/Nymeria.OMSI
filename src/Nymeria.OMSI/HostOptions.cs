namespace Nymeria.Omsi;

/// <summary>
/// The command-line contract of the bootstrap host.
/// </summary>
/// <remarks>
/// Parsing lives here, not in <see cref="Program"/>, so it can be tested without
/// creating a window, a GPU device, or a native library.
/// </remarks>
internal sealed class HostOptions
{
    /// <summary>
    /// The window title used by the bootstrap host.
    /// </summary>
    public const string WindowTitle = "Nymeria.OMSI Bootstrap";

    /// <summary>
    /// The number of frame-loop iterations a <c>--smoke</c> run performs before
    /// shutting down on its own.
    /// </summary>
    public const int SmokeFrameCount = 120;

    /// <summary>
    /// The accepted argument summary, printed for <c>--help</c> and for invalid
    /// input.
    /// </summary>
    public const string UsageText =
        "Usage: Nymeria.OMSI [-h|--help] [--smoke] [--map <directory>] " +
        "[--render] [--world <directory>]\n" +
        "  Opens a native window, initializes the Nymeria GPU device, presents\n" +
        "  cleared frames, and shuts down cleanly. It loads no OMSI content.\n" +
        "  --smoke            Present a fixed number of frames, then exit without input.\n" +
        "  --map <directory>  Inspect an OMSI map folder (the one containing\n" +
        "                     global.cfg): print its metadata, tile inventory, and\n" +
        "                     diagnostics. No window or GPU device is created.\n" +
        "  --render           With --map: open the window and show a diagnostic\n" +
        "                     plan of the parsed tiles and placements. OMSI scenery\n" +
        "                     and terrain are not rendered.\n" +
        "  --world <directory>  Parse the same map folder's tile files and print a\n" +
        "                     bounded summary of object and spline placements. It\n" +
        "                     renders nothing and creates no window or GPU device.\n" +
        "  -h, --help         Show this help and exit.";

    private HostOptions(
        bool showHelp,
        int? maximumFrames,
        string? mapDirectory,
        string? worldDirectory,
        bool renderMap)
    {
        ShowHelp = showHelp;
        MaximumFrames = maximumFrames;
        MapDirectory = mapDirectory;
        WorldDirectory = worldDirectory;
        RenderMap = renderMap;
    }

    /// <summary>
    /// Gets a value indicating whether the host should print usage and exit.
    /// </summary>
    public bool ShowHelp { get; }

    /// <summary>
    /// Gets the frame-loop iteration limit, or <see langword="null"/> to run
    /// until the window is closed.
    /// </summary>
    public int? MaximumFrames { get; }

    /// <summary>
    /// Gets the explicit OMSI map folder to inspect, or <see langword="null"/> to
    /// run the frame loop instead.
    /// </summary>
    public string? MapDirectory { get; }

    /// <summary>
    /// Gets the explicit OMSI map folder whose tile files should be parsed, or
    /// <see langword="null"/> when no world inspection was requested.
    /// </summary>
    public string? WorldDirectory { get; }

    /// <summary>
    /// Gets a value indicating whether the parsed map should be shown in a window
    /// instead of only reported.
    /// </summary>
    public bool RenderMap { get; }

    /// <summary>
    /// Parses command-line arguments.
    /// </summary>
    /// <param name="arguments">The arguments passed to the process.</param>
    /// <param name="options">The parsed options when parsing succeeds.</param>
    /// <param name="error">A readable explanation when parsing fails.</param>
    /// <returns><see langword="true"/> when the arguments were accepted.</returns>
    public static bool TryParse(
        IReadOnlyList<string> arguments,
        out HostOptions options,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        bool showHelp = false;
        bool isSmokeRun = false;
        string? mapDirectory = null;
        string? worldDirectory = null;
        bool renderMap = false;

        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];

            switch (argument)
            {
                case "-h":
                case "--help":
                    showHelp = true;
                    break;

                case "--smoke":
                    isSmokeRun = true;
                    break;

                case "--render":
                    renderMap = true;
                    break;

                case "--map":
                    if (index + 1 >= arguments.Count)
                    {
                        return Fail(
                            "--map requires a map directory path.",
                            out options,
                            out error);
                    }

                    string value = arguments[++index];

                    if (value.Length == 0 || value[0] == '-')
                    {
                        return Fail(
                            "--map requires a map directory path, but got " +
                            $"'{value}'. A path that starts with '-' is not accepted.",
                            out options,
                            out error);
                    }

                    if (mapDirectory is not null)
                    {
                        return Fail(
                            "--map was given more than once.",
                            out options,
                            out error);
                    }

                    mapDirectory = value;
                    break;

                case "--world":
                    if (index + 1 >= arguments.Count)
                    {
                        return Fail(
                            "--world requires a map directory path.",
                            out options,
                            out error);
                    }

                    string worldValue = arguments[++index];

                    if (worldValue.Length == 0 || worldValue[0] == '-')
                    {
                        return Fail(
                            "--world requires a map directory path, but got " +
                            $"'{worldValue}'. A path that starts with '-' is not " +
                            "accepted.",
                            out options,
                            out error);
                    }

                    if (worldDirectory is not null)
                    {
                        return Fail(
                            "--world was given more than once.",
                            out options,
                            out error);
                    }

                    worldDirectory = worldValue;
                    break;

                default:
                    return Fail(
                        $"Unknown argument: {argument}",
                        out options,
                        out error);
            }
        }

        if (isSmokeRun && mapDirectory is not null && !renderMap)
        {
            return Fail(
                "--smoke and --map cannot be combined without --render: --map on " +
                "its own inspects configuration and diagnostics only, without " +
                "presenting frames.",
                out options,
                out error);
        }

        if (renderMap && worldDirectory is not null)
        {
            return Fail(
                "--render and --world cannot be combined: --world is the headless " +
                "inspection of parsed placements, while --render shows them in a " +
                "window.",
                out options,
                out error);
        }

        if (renderMap && mapDirectory is null)
        {
            return Fail(
                "--render requires --map <directory>: there is no map to visualize " +
                "otherwise.",
                out options,
                out error);
        }

        if (isSmokeRun && worldDirectory is not null)
        {
            return Fail(
                "--smoke and --world cannot be combined: --world parses tile data " +
                "only, without presenting frames.",
                out options,
                out error);
        }

        if (mapDirectory is not null && worldDirectory is not null)
        {
            return Fail(
                "--map and --world cannot be combined: --map reports discovery " +
                "metadata only, while --world parses tile placements and includes " +
                "that metadata in its summary.",
                out options,
                out error);
        }

        options = new HostOptions(
            showHelp,
            isSmokeRun ? SmokeFrameCount : null,
            mapDirectory,
            worldDirectory,
            renderMap);
        error = null;
        return true;
    }

    private static bool Fail(
        string message,
        out HostOptions options,
        out string? error)
    {
        options = new HostOptions(false, null, null, null, false);
        error = message;
        return false;
    }
}

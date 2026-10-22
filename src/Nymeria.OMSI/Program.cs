using System.Runtime.InteropServices;
using Nymeria.Omsi.Maps;
using Nymeria.Omsi.World;

namespace Nymeria.Omsi;

/// <summary>
/// Process entry point for the Nymeria.OMSI host.
/// </summary>
/// <remarks>
/// <para>
/// With no map argument the host initializes the pinned Nymeria engine, presents
/// cleared frames, and shuts down cleanly. With <c>--map &lt;directory&gt;</c> it
/// reads only map configuration metadata and diagnostics, and with
/// <c>--world &lt;directory&gt;</c> it additionally parses the tile files into a
/// read-only world model. Neither inspection path creates a window, GPU device, or
/// native SDL call.
/// </para>
/// <para>
/// It loads no OMSI assets, maps no terrain, and claims no OMSI runtime
/// compatibility. Startup, rendering, and shutdown behavior on Windows, and
/// validation against a real Grundorf installation, are performed manually by the
/// project owner; see docs/DEVELOPMENT.md and docs/OMSI_MAP_PARSING.md.
/// </para>
/// </remarks>
internal static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitFailure = 1;
    private const int ExitUsage = 2;

    private static int Main(string[] args)
    {
        if (!HostOptions.TryParse(args, out HostOptions options, out string? error))
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
            Console.Error.WriteLine(HostOptions.UsageText);
            return ExitUsage;
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(HostOptions.UsageText);
            return ExitSuccess;
        }

        try
        {
            if (options.MapDirectory is not null)
            {
                return RunMapInspection(options.MapDirectory);
            }

            if (options.WorldDirectory is not null)
            {
                return RunWorldInspection(options.WorldDirectory);
            }

            return Run(options);
        }
        catch (DllNotFoundException exception)
        {
            Console.Error.WriteLine(
                "Nymeria.OMSI could not load the native SDL3 library.");
            Console.Error.WriteLine(
                "Restore and rebuild so NuGet deploys the platform runtime beside " +
                "the application, for example " +
                "bin/<Configuration>/net10.0/runtimes/win-x64/native/SDL3.dll on " +
                "Windows or libSDL3.dylib under runtimes/osx-arm64/native on macOS.");
            Console.Error.WriteLine("  dotnet restore Nymeria.OMSI.slnx");
            Console.Error.WriteLine("  dotnet build Nymeria.OMSI.slnx");
            Console.Error.WriteLine($"Details: {exception}");
            return ExitFailure;
        }
        catch (BadImageFormatException exception)
        {
            Console.Error.WriteLine(
                "The native SDL3 library does not match this process architecture.");
            Console.Error.WriteLine(
                "Build for the architecture of the running process, for example " +
                "`dotnet build -r win-x64` on an x64 host or `-r win-arm64` on an " +
                "arm64 host, instead of mixing binaries from another architecture.");
            Console.Error.WriteLine($"Details: {exception}");
            return ExitFailure;
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(
                $"Nymeria.OMSI could not initialize or run the Nymeria engine: " +
                $"{exception.Message}");
            return ExitFailure;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                "Nymeria.OMSI stopped because of an unexpected error.");
            Console.Error.WriteLine(exception);
            return ExitFailure;
        }
    }

    private static int Run(HostOptions options)
    {
        Console.WriteLine("Nymeria.OMSI bootstrap");
        Console.WriteLine(
            $"host: {RuntimeInformation.OSDescription} " +
            $"({RuntimeInformation.ProcessArchitecture})");

        using (OmsiHost host = OmsiHost.Start())
        {
            host.ReportDiagnostics(Console.Out);

            HostRunResult result = host.Run(options.MaximumFrames);

            Console.WriteLine($"frames presented: {result.PresentedFrames}");

            if (options.MaximumFrames is null)
            {
                Console.WriteLine(
                    result.QuitRequested
                        ? "window closed by the user"
                        : "frame loop ended without a quit request");
            }
            else
            {
                Console.WriteLine(
                    result.QuitRequested
                        ? "smoke run ended by a quit request"
                        : "smoke run complete");
            }
        }

        Console.WriteLine("shutdown: clean");

        return ExitSuccess;
    }

    private static int RunMapInspection(string mapDirectory)
    {
        // Discovery is deliberately GPU-free: inspecting configuration must work
        // without a window, a device, or any OMSI asset.
        OmsiMapManifest manifest = OmsiMapDiscovery.Scan(mapDirectory);

        MapSummaryWriter.Write(Console.Out, manifest);

        return manifest.Status == MapScanStatus.Failed
            ? ExitFailure
            : ExitSuccess;
    }

    private static int RunWorldInspection(string mapDirectory)
    {
        // Parsing tile placements is also GPU-free. The summary states whether the
        // map parsed completely, partially, or not at all; it never claims that the
        // map is renderable.
        OmsiWorldModel world = OmsiWorldLoader.Load(mapDirectory);

        WorldSummaryWriter.Write(Console.Out, world);

        return world.Status == MapScanStatus.Failed
            ? ExitFailure
            : ExitSuccess;
    }
}

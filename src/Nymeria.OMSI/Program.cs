using System.Runtime.InteropServices;
using Nymeria.Omsi.Maps;
using Nymeria.Omsi.Rendering;
using Nymeria.Omsi.Scenery;
using Nymeria.Omsi.Terrain;
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
/// native SDL call. <c>--map &lt;directory&gt; --render</c> parses the same data and
/// then opens the window to show a clearly labelled diagnostic plan of the parsed
/// tiles and placements; it does not render OMSI scenery or terrain.
/// <c>--map &lt;directory&gt; --terrain-3d</c> decodes each tile's documented terrain
/// sidecar and opens a 3D view of the resulting mesh with a free-fly camera. It
/// renders terrain and nothing else: no scenery, splines, or ground textures.
/// <c>--map &lt;directory&gt; --scene-3d</c> additionally resolves, parses, and decodes a
/// bounded, deterministic subset of the map's scenery objects from the OMSI
/// installation and draws them over the same terrain. Roads and splines, vehicles,
/// scripts, and animation are still not rendered.
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
                if (options.Scene3D)
                {
                    return RunSceneRender(options.MapDirectory, options.MaximumFrames);
                }

                if (options.Terrain3D)
                {
                    return RunTerrainRender(options.MapDirectory, options.MaximumFrames);
                }

                return options.RenderMap
                    ? RunMapRender(options.MapDirectory, options.MaximumFrames)
                    : RunMapInspection(options.MapDirectory);
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

    private static int RunMapRender(string mapDirectory, int? maximumFrames)
    {
        // Parsing happens before any window or device exists, so a map that cannot
        // be read fails with parser diagnostics instead of a GPU failure.
        OmsiWorldModel world = OmsiWorldLoader.Load(mapDirectory);

        if (world.Status == MapScanStatus.Failed)
        {
            WorldSummaryWriter.Write(Console.Out, world);
            Console.Error.WriteLine(
                "render: not started — the map produced no usable world data.");
            return ExitFailure;
        }

        OmsiMapPlan plan = OmsiMapPlan.Create(world);

        if (plan.Tiles.Count == 0)
        {
            WorldSummaryWriter.Write(Console.Out, world);
            Console.Error.WriteLine(
                "render: not started — the map has no discovered tile files, so " +
                "there is nothing to visualize.");
            return ExitFailure;
        }

        bool hasContentRoot = OmsiContentRoot.TryDerive(
            world.MapRoot,
            out string? contentRoot,
            out string contentRootReason);

        RenderSummaryWriter.Write(
            Console.Out,
            world,
            plan,
            hasContentRoot ? contentRoot : null,
            contentRootReason);

        string title =
            $"Nymeria.OMSI — {world.Name ?? world.MapRoot} — diagnostic tile plan";

        using (OmsiHost host = OmsiHost.Start(
            title,
            (window, device) => new DiagnosticMapPresenter(window, device, plan),
            interactiveEventWaitMilliseconds: 16))
        {
            host.ReportDiagnostics(Console.Out);

            HostRunResult result = host.Run(maximumFrames);

            Console.WriteLine($"frames presented: {result.PresentedFrames}");

            if (maximumFrames is null)
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
                        ? "render run ended by a quit request"
                        : "render run complete");
            }
        }

        Console.WriteLine("shutdown: clean");

        return ExitSuccess;
    }

    private static int RunTerrainRender(string mapDirectory, int? maximumFrames)
    {
        // Parsing and decoding happen before any window or device exists, so a map
        // that cannot be read or has no decodable terrain fails with parser
        // diagnostics instead of a GPU failure or an empty window.
        OmsiWorldModel world = OmsiWorldLoader.Load(mapDirectory);

        if (world.Status == MapScanStatus.Failed)
        {
            WorldSummaryWriter.Write(Console.Out, world);
            Console.Error.WriteLine(
                "terrain: not started — the map produced no usable world data.");
            return ExitFailure;
        }

        OmsiTerrainSet terrain = OmsiTerrainSet.Load(world);
        OmsiTerrainModel model = OmsiTerrainModel.Create(terrain);

        TerrainSummaryWriter.Write(Console.Out, world, terrain, model);

        if (model.Bounds is null)
        {
            Console.Error.WriteLine(
                "terrain: not started — no tile produced a decoded height grid, so " +
                "there is no terrain to draw. The terrain diagnostics above say why.");
            return ExitFailure;
        }

        string title =
            $"Nymeria.OMSI — {world.Name ?? world.MapRoot} — 3D terrain only";

        using (OmsiHost host = OmsiHost.Start(
            title,
            (window, device) => ScenePresenter.Create(window, device, model),
            interactiveEventWaitMilliseconds: 16))
        {
            host.ReportDiagnostics(Console.Out);

            HostRunResult result = host.Run(maximumFrames);

            Console.WriteLine($"frames presented: {result.PresentedFrames}");

            if (maximumFrames is null)
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
                        ? "terrain run ended by a quit request"
                        : "terrain run complete");
            }
        }

        Console.WriteLine("shutdown: clean");

        return ExitSuccess;
    }

    private static int RunSceneRender(string mapDirectory, int? maximumFrames)
    {
        // Parsing, terrain decoding, and scenery loading all happen before any window
        // or device exists. A map that cannot be read, has no terrain, or has no
        // loadable scenery therefore fails with counted diagnostics instead of an
        // empty window or a GPU failure.
        OmsiWorldModel world = OmsiWorldLoader.Load(mapDirectory);

        if (world.Status == MapScanStatus.Failed)
        {
            WorldSummaryWriter.Write(Console.Out, world);
            Console.Error.WriteLine(
                "scene: not started — the map produced no usable world data.");
            return ExitFailure;
        }

        OmsiTerrainSet terrain = OmsiTerrainSet.Load(world);
        OmsiTerrainModel model = OmsiTerrainModel.Create(terrain);

        bool hasContentRoot = OmsiContentRoot.TryDerive(
            world.MapRoot,
            out string? contentRoot,
            out string contentRootReason);

        TerrainHeightSampler? heights = terrain.Heightmaps.Count == 0
            ? null
            : TerrainHeightSampler.Create(terrain);

        OmsiSceneryLibrary scene = OmsiSceneryLibrary.Load(
            world,
            heights,
            hasContentRoot ? contentRoot : null);

        ScenerySummaryWriter.Write(
            Console.Out,
            world,
            terrain.Heightmaps.Count == 0 ? null : terrain,
            scene,
            hasContentRoot ? contentRoot : null);

        if (!hasContentRoot)
        {
            Console.Error.WriteLine(
                "scene: not started — " + contentRootReason + " Scenery references " +
                "are resolved against the installation root, so they cannot be loaded.");
            return ExitFailure;
        }

        if (model.Bounds is null)
        {
            Console.Error.WriteLine(
                "scene: not started — no tile produced a decoded height grid, so there " +
                "is no terrain to draw. The terrain diagnostics above say why.");
            return ExitFailure;
        }

        if (scene.Instances.Count == 0)
        {
            Console.Error.WriteLine(
                "scene: not started — no scenery placement could be loaded, so the " +
                "view would be terrain only. The scenery diagnostics above say why. " +
                "Use --terrain-3d for the terrain-only view.");
            return ExitFailure;
        }

        string title =
            $"Nymeria.OMSI — {world.Name ?? world.MapRoot} — 3D terrain + scenery " +
            $"({scene.Instances.Count} objects)";

        ScenePresenter? presenter = null;

        using (OmsiHost host = OmsiHost.Start(
            title,
            (window, device) => presenter = ScenePresenter.Create(window, device, model, scene),
            interactiveEventWaitMilliseconds: 16))
        {
            host.ReportDiagnostics(Console.Out);

            HostRunResult result = host.Run(maximumFrames);

            Console.WriteLine($"frames presented: {result.PresentedFrames}");

            if (maximumFrames is null)
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
                        ? "scene run ended by a quit request"
                        : "scene run complete");
            }
        }

        if (presenter?.SceneryUploadSummary is string upload)
        {
            Console.WriteLine(upload);
        }

        Console.WriteLine("shutdown: clean");

        return ExitSuccess;
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

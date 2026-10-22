using Nymeria.Graphics;
using Nymeria.Platform;
using SDL3;

namespace Nymeria.Omsi;

/// <summary>
/// Owns the host lifetime: one Nymeria window, one engine GPU device, and the frame
/// loop that presents frames from an <see cref="IFramePresenter"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every member runs on the process main thread because SDL windowing and the
/// Nymeria GPU device require it. The window outlives the device: the device
/// claims the window for presentation and releases that claim when disposed, so
/// disposal happens presenter first, then device, then window.
/// </para>
/// <para>
/// The host deliberately contains no OMSI parsing, no map loading, no scene, and no
/// renderer abstraction. It knows only how to create a window and a device, hand
/// them to a presenter, run the loop, and shut everything down.
/// </para>
/// </remarks>
internal sealed class OmsiHost : IDisposable
{
    private const int InteractiveEventWaitMilliseconds = 100;
    private const int SmokeEventWaitMilliseconds = 16;

    private readonly Window _window;
    private readonly GpuDevice _device;
    private readonly IFramePresenter _presenter;
    private readonly int _interactiveEventWaitMilliseconds;
    private bool _disposed;

    private OmsiHost(
        Window window,
        GpuDevice device,
        IFramePresenter presenter,
        int interactiveEventWaitMilliseconds)
    {
        _window = window;
        _device = device;
        _presenter = presenter;
        _interactiveEventWaitMilliseconds = interactiveEventWaitMilliseconds;
    }

    /// <summary>
    /// Creates the native window and the engine GPU device and presents cleared
    /// frames.
    /// </summary>
    /// <returns>An initialized host that must be disposed on the main thread.</returns>
    /// <exception cref="InvalidOperationException">
    /// SDL video initialization, window creation, or GPU device creation failed,
    /// including when the platform has no supported GPU backend.
    /// </exception>
    public static OmsiHost Start()
    {
        return Start(
            HostOptions.WindowTitle,
            static (window, device) => new ClearFramePresenter(window, device));
    }

    /// <summary>
    /// Creates the native window and the engine GPU device and presents frames from
    /// a caller-supplied presenter.
    /// </summary>
    /// <param name="windowTitle">The title of the native window.</param>
    /// <param name="createPresenter">
    /// Creates the presenter for the window handle and device just created. The
    /// presenter is disposed before the device and the window.
    /// </param>
    /// <param name="interactiveEventWaitMilliseconds">
    /// How long an interactive frame waits for an event. A presenter that animates
    /// asks for a shorter wait so it keeps a steady frame rate.
    /// </param>
    /// <returns>An initialized host that must be disposed on the main thread.</returns>
    /// <exception cref="InvalidOperationException">
    /// SDL video initialization, window creation, GPU device creation, or presenter
    /// creation failed, including when the platform has no supported GPU backend.
    /// </exception>
    public static OmsiHost Start(
        string windowTitle,
        Func<IntPtr, GpuDevice, IFramePresenter> createPresenter,
        int interactiveEventWaitMilliseconds = InteractiveEventWaitMilliseconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowTitle);
        ArgumentNullException.ThrowIfNull(createPresenter);
        ArgumentOutOfRangeException.ThrowIfNegative(interactiveEventWaitMilliseconds);

        Window? window = null;
        GpuDevice? device = null;
        IFramePresenter? presenter = null;

        try
        {
            window = Window.Create(new WindowOptions(windowTitle));
            device = GpuDevice.Create(window.Handle);
            presenter = createPresenter(window.Handle, device);

            return new OmsiHost(
                window,
                device,
                presenter,
                interactiveEventWaitMilliseconds);
        }
        catch
        {
            // Dispose in the reverse order of creation so a failed start does not
            // leave SDL initialized or a window claimed.
            presenter?.Dispose();
            device?.Dispose();
            window?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Writes the diagnostics that identify the backend and window the host
    /// actually obtained.
    /// </summary>
    /// <param name="writer">The destination, normally standard output.</param>
    public void ReportDiagnostics(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteLine(
            $"engine: {_device.DriverName ?? "unknown"} driver, " +
            $"{_device.ShaderFormat} shaders, " +
            $"{_device.SwapchainFormat} swapchain");

        if (!SDL.GetWindowSize(_window.Handle, out int width, out int height))
        {
            writer.WriteLine(
                $"window: created (size unavailable: {SDL.GetError()})");
            return;
        }

        writer.WriteLine($"window: {width}x{height} logical");
    }

    /// <summary>
    /// Runs the frame loop until the window closes or the frame budget is spent.
    /// </summary>
    /// <param name="maximumFrames">
    /// The number of loop iterations to run, or <see langword="null"/> to run
    /// until a quit request arrives.
    /// </param>
    /// <returns>What the loop presented and why it stopped.</returns>
    public HostRunResult Run(int? maximumFrames)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (maximumFrames is int requestedFrames && requestedFrames <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumFrames),
                requestedFrames,
                "A frame budget must be at least one frame.");
        }

        int eventWaitMilliseconds = maximumFrames is null
            ? _interactiveEventWaitMilliseconds
            : SmokeEventWaitMilliseconds;

        int presentedFrames = 0;
        int iterations = 0;

        while (_window.ProcessEvents(eventWaitMilliseconds))
        {
            if (_presenter.Present())
            {
                presentedFrames++;
            }

            iterations++;

            // Count loop iterations, not presented frames, so a minimized window
            // that skips frames still ends a smoke run.
            if (maximumFrames is int limit && iterations >= limit)
            {
                return new HostRunResult(presentedFrames, QuitRequested: false);
            }
        }

        return new HostRunResult(presentedFrames, QuitRequested: true);
    }

    /// <summary>
    /// Disposes the GPU device and then the window it claimed.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _presenter.Dispose();
        _device.Dispose();
        _window.Dispose();
    }
}

/// <summary>
/// Presents one frame into a window claimed by the engine device.
/// </summary>
/// <remarks>
/// The host owns the window and the device; a presenter owns only what it creates
/// for its own frames and releases that when the host disposes it. This is a
/// two-implementation seam (cleared frames and the diagnostic map plan), not a
/// rendering abstraction: it deliberately exposes nothing about passes, pipelines,
/// or resources.
/// </remarks>
internal interface IFramePresenter : IDisposable
{
    /// <summary>
    /// Presents one frame.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when a frame was submitted for presentation, and
    /// <see langword="false"/> when this frame had nothing to draw into.
    /// </returns>
    bool Present();
}

/// <summary>
/// Describes how one frame loop run ended.
/// </summary>
/// <param name="PresentedFrames">The number of frames submitted for presentation.</param>
/// <param name="QuitRequested">Whether the window closed during the run.</param>
internal readonly record struct HostRunResult(int PresentedFrames, bool QuitRequested);

using Nymeria.Graphics;
using Nymeria.Platform;
using SDL3;

namespace Nymeria.Omsi;

/// <summary>
/// Owns the bootstrap host lifetime: one Nymeria window, one engine GPU device,
/// and the frame loop that presents cleared frames.
/// </summary>
/// <remarks>
/// <para>
/// Every member runs on the process main thread because SDL windowing and the
/// Nymeria GPU device require it. The window outlives the device: the device
/// claims the window for presentation and releases that claim when disposed, so
/// disposal happens device first.
/// </para>
/// <para>
/// The host deliberately contains no OMSI parsing, no map loading, no scene, and
/// no renderer abstraction. It exists to prove that the pinned engine dependency
/// initializes, presents, and shuts down.
/// </para>
/// </remarks>
internal sealed class OmsiHost : IDisposable
{
    private const int InteractiveEventWaitMilliseconds = 100;
    private const int SmokeEventWaitMilliseconds = 16;

    private readonly Window _window;
    private readonly GpuDevice _device;
    private readonly ClearFramePresenter _presenter;
    private bool _disposed;

    private OmsiHost(Window window, GpuDevice device, ClearFramePresenter presenter)
    {
        _window = window;
        _device = device;
        _presenter = presenter;
    }

    /// <summary>
    /// Creates the native window and the engine GPU device.
    /// </summary>
    /// <returns>An initialized host that must be disposed on the main thread.</returns>
    /// <exception cref="InvalidOperationException">
    /// SDL video initialization, window creation, or GPU device creation failed,
    /// including when the platform has no supported GPU backend.
    /// </exception>
    public static OmsiHost Start()
    {
        Window? window = null;
        GpuDevice? device = null;

        try
        {
            window = Window.Create(new WindowOptions(HostOptions.WindowTitle));
            device = GpuDevice.Create(window.Handle);

            return new OmsiHost(
                window,
                device,
                new ClearFramePresenter(window.Handle, device));
        }
        catch
        {
            // Dispose in the reverse order of creation so a failed start does not
            // leave SDL initialized or a window claimed.
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
            ? InteractiveEventWaitMilliseconds
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

        _device.Dispose();
        _window.Dispose();
    }
}

/// <summary>
/// Describes how one frame loop run ended.
/// </summary>
/// <param name="PresentedFrames">The number of frames submitted for presentation.</param>
/// <param name="QuitRequested">Whether the window closed during the run.</param>
internal readonly record struct HostRunResult(int PresentedFrames, bool QuitRequested);

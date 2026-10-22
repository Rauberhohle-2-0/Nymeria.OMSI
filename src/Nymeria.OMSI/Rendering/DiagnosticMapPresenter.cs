using System.Diagnostics;
using Nymeria.Graphics;
using SDL3;

namespace Nymeria.Omsi.Rendering;

/// <summary>
/// Presents a diagnostic plan of the parsed map into a window claimed by the engine
/// device.
/// </summary>
/// <remarks>
/// <para>
/// The image is built on the CPU from parser output, uploaded once as an RGBA8
/// texture through <see cref="GpuDevice.CreateTexture"/>, and blitted to the
/// swapchain with SDL's own blit command. That command carries SDL's internal blit
/// pipeline, so this milestone needs no shader asset, no pipeline object, and no
/// shader compiler, and it does not copy any engine example or shader.
/// </para>
/// <para>
/// What the window shows is a <em>plan</em>: tile squares and placement markers.
/// OMSI terrain, object meshes, and spline curves are not rendered, so the frame is
/// labelled as diagnostic in the window title and in the console summary. It is not
/// a claim that the map is rendered.
/// </para>
/// <para>
/// This is host-boundary code, like <see cref="ClearFramePresenter"/>: the engine
/// exposes the native device for exactly this purpose. All members run on the
/// process main thread, and the window and device must outlive the presenter.
/// </para>
/// </remarks>
internal sealed class DiagnosticMapPresenter : IFramePresenter
{
    /// <summary>The diagnostic image width in pixels.</summary>
    internal const int ImageWidth = 1280;

    /// <summary>The diagnostic image height in pixels.</summary>
    internal const int ImageHeight = 720;

    /// <summary>Pan speed in image pixels per second.</summary>
    internal const double PanPixelsPerSecond = 420.0;

    /// <summary>Zoom speed, as a factor per second held.</summary>
    internal const double ZoomFactorPerSecond = 1.9;

    /// <summary>The largest frame time used for movement, in seconds.</summary>
    private const double MaximumFrameSeconds = 0.1;

    private static readonly SDL.FColor ClearColor = new()
    {
        R = 0.05f,
        G = 0.07f,
        B = 0.10f,
        A = 1.0f,
    };

    private readonly IntPtr _window;
    private readonly IntPtr _device;
    private readonly GpuDevice _engineDevice;
    private readonly OmsiMapPlan _plan;
    private readonly MapPlanView _view;
    private readonly MapPlanImage _image;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private IntPtr _texture;
    private double _lastFrameSeconds;
    private bool _disposed;

    /// <summary>
    /// Creates a presenter and uploads the first frame of the plan.
    /// </summary>
    /// <param name="window">The native <c>SDL_Window*</c> handle.</param>
    /// <param name="device">The engine device that claimed the window.</param>
    /// <param name="plan">The plan derived from the parsed map.</param>
    /// <exception cref="ObjectDisposedException">The device has been disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The diagnostic texture could not be created or uploaded.
    /// </exception>
    internal DiagnosticMapPresenter(IntPtr window, GpuDevice device, OmsiMapPlan plan)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(plan);

        _window = window;
        _engineDevice = device;
        _device = device.Handle;
        _plan = plan;
        _image = new MapPlanImage(ImageWidth, ImageHeight);
        _view = MapPlanView.Frame(plan.Bounds, ImageWidth, ImageHeight);

        DrawStatistics = MapPlanRasterizer.Render(plan, _view, _image);
        _texture = UploadImage();
    }

    /// <summary>Gets what the most recent rasterization actually drew.</summary>
    internal PlanDrawStatistics DrawStatistics { get; private set; }

    /// <summary>Gets the view that was last rasterized.</summary>
    internal MapPlanView View => _view;

    /// <summary>
    /// Presents one frame, applying keyboard navigation first.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when a frame was submitted, and <see langword="false"/>
    /// when this frame had nothing to draw into, such as while minimized.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// SDL could not acquire a command buffer or the swapchain texture, or could not
    /// submit the frame.
    /// </exception>
    public bool Present()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        UpdateView();

        if ((SDL.GetWindowFlags(_window) & SDL.WindowFlags.Minimized) != 0)
        {
            return false;
        }

        IntPtr commandBuffer = SDL.AcquireGPUCommandBuffer(_device);

        if (commandBuffer == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"SDL could not acquire a GPU command buffer: {SDL.GetError()}");
        }

        if (!SDL.AcquireGPUSwapchainTexture(
                commandBuffer,
                _window,
                out IntPtr swapchainTexture,
                out uint width,
                out uint height))
        {
            SDL.CancelGPUCommandBuffer(commandBuffer);

            throw new InvalidOperationException(
                $"SDL could not acquire a GPU swapchain texture: {SDL.GetError()}");
        }

        if (swapchainTexture == IntPtr.Zero)
        {
            SDL.CancelGPUCommandBuffer(commandBuffer);
            return false;
        }

        if (width == 0 || height == 0)
        {
            SDL.SubmitGPUCommandBuffer(commandBuffer);
            return false;
        }

        // Clear the whole swapchain first. The blit below writes only its
        // destination region, so a letterboxed window would otherwise show
        // undefined pixels in the bars instead of the background colour.
        if (!TryClearSwapchain(commandBuffer, swapchainTexture, width, height))
        {
            SDL.SubmitGPUCommandBuffer(commandBuffer);
            return false;
        }

        // Preserve the plan's aspect ratio and letterbox the rest, so a resized
        // window shows a correctly proportioned plan instead of a stretched one.
        (uint destinationWidth, uint destinationHeight) = Fit(
            ImageWidth,
            ImageHeight,
            width,
            height);

        var info = new SDL.GPUBlitInfo
        {
            Source = new SDL.GPUBlitRegion
            {
                Texture = _texture,
                MipLevel = 0,
                LayerOrDepthPlane = 0,
                X = 0,
                Y = 0,
                W = ImageWidth,
                H = ImageHeight,
            },
            Destination = new SDL.GPUBlitRegion
            {
                Texture = swapchainTexture,
                MipLevel = 0,
                LayerOrDepthPlane = 0,
                X = (width - destinationWidth) / 2,
                Y = (height - destinationHeight) / 2,
                W = destinationWidth,
                H = destinationHeight,
            },
            LoadOp = SDL.GPULoadOp.Load,
            ClearColor = ClearColor,
            FlipMode = SDL.FlipMode.None,
            Filter = SDL.GPUFilter.Nearest,
        };

        info.Cycle = false;

        SDL.BlitGPUTexture(commandBuffer, in info);

        if (!SDL.SubmitGPUCommandBuffer(commandBuffer))
        {
            throw new InvalidOperationException(
                $"SDL could not submit the GPU command buffer: {SDL.GetError()}");
        }

        return true;
    }

    /// <summary>
    /// Releases the diagnostic texture. The device and window belong to the host.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_texture != IntPtr.Zero)
        {
            SDL.ReleaseGPUTexture(_device, _texture);
            _texture = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Computes the largest centered rectangle of the source aspect ratio that fits
    /// the destination.
    /// </summary>
    internal static (uint Width, uint Height) Fit(
        uint sourceWidth,
        uint sourceHeight,
        uint destinationWidth,
        uint destinationHeight)
    {
        if (sourceWidth == 0 || sourceHeight == 0 ||
            destinationWidth == 0 || destinationHeight == 0)
        {
            return (0, 0);
        }

        double scale = Math.Min(
            (double)destinationWidth / sourceWidth,
            (double)destinationHeight / sourceHeight);

        uint width = (uint)Math.Max(
            1,
            Math.Round(sourceWidth * scale, MidpointRounding.AwayFromZero));
        uint height = (uint)Math.Max(
            1,
            Math.Round(sourceHeight * scale, MidpointRounding.AwayFromZero));

        return (Math.Min(width, destinationWidth), Math.Min(height, destinationHeight));
    }

    private void UpdateView()
    {
        double elapsedSeconds = _clock.Elapsed.TotalSeconds - _lastFrameSeconds;
        _lastFrameSeconds += elapsedSeconds;

        elapsedSeconds = Math.Clamp(elapsedSeconds, 0.0, MaximumFrameSeconds);

        ReadOnlySpan<bool> keys = SDL.GetKeyboardState(out _);
        bool panLeft = IsDown(keys, SDL.Scancode.A) || IsDown(keys, SDL.Scancode.Left);
        bool panRight = IsDown(keys, SDL.Scancode.D) || IsDown(keys, SDL.Scancode.Right);
        bool panUp = IsDown(keys, SDL.Scancode.W) || IsDown(keys, SDL.Scancode.Up);
        bool panDown = IsDown(keys, SDL.Scancode.S) || IsDown(keys, SDL.Scancode.Down);
        bool zoomIn = IsDown(keys, SDL.Scancode.Equals);
        bool zoomOut = IsDown(keys, SDL.Scancode.Minus);
        bool reset = IsDown(keys, SDL.Scancode.R);

        bool changed = false;

        // Moving the view left drags the plan right; moving it up drags the plan
        // down. That keeps the gesture identical to dragging the map itself.
        if (panLeft != panRight || panUp != panDown)
        {
            double pixels = PanPixelsPerSecond * elapsedSeconds;

            _view.PanPixels(
                (panLeft ? pixels : 0.0) - (panRight ? pixels : 0.0),
                (panUp ? pixels : 0.0) - (panDown ? pixels : 0.0),
                _plan.Bounds);
            changed = true;
        }

        if (zoomIn != zoomOut && elapsedSeconds > 0.0)
        {
            double factor = Math.Pow(ZoomFactorPerSecond, elapsedSeconds);

            _view.Zoom(zoomIn ? 1.0 / factor : factor);
            changed = true;
        }

        if (reset)
        {
            _view.Reset(_plan.Bounds);
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        DrawStatistics = MapPlanRasterizer.Render(_plan, _view, _image);

        IntPtr texture = UploadImage();

        if (_texture != IntPtr.Zero)
        {
            SDL.ReleaseGPUTexture(_device, _texture);
        }

        _texture = texture;
    }

    /// <summary>
    /// Clears the acquired swapchain texture to the background colour.
    /// </summary>
    /// <returns><see langword="false"/> when the render pass could not be begun.</returns>
    private bool TryClearSwapchain(
        IntPtr commandBuffer,
        IntPtr swapchainTexture,
        uint width,
        uint height)
    {
        var colorTarget = new SDL.GPUColorTargetInfo
        {
            Texture = swapchainTexture,
            ClearColor = ClearColor,
            LoadOp = SDL.GPULoadOp.Clear,
            StoreOp = SDL.GPUStoreOp.Store,
        };

        Span<SDL.GPUColorTargetInfo> colorTargets = stackalloc SDL.GPUColorTargetInfo[1];
        colorTargets[0] = colorTarget;

        IntPtr renderPass = SDL.BeginGPURenderPass(
            commandBuffer,
            colorTargets,
            1,
            IntPtr.Zero);

        if (renderPass == IntPtr.Zero)
        {
            return false;
        }

        SDL.EndGPURenderPass(renderPass);
        return true;
    }

    private IntPtr UploadImage()
    {
        GpuDevice.TextureUploadRegion[] regions =
        [
            new(0, _image.Pixels.Length, ImageWidth, ImageHeight),
        ];

        return _engineDevice.CreateTexture(
            SDL.GPUTextureFormat.R8G8B8A8Unorm,
            ImageWidth,
            ImageHeight,
            regions,
            _image.Pixels);
    }

    private static bool IsDown(ReadOnlySpan<bool> keys, SDL.Scancode scancode)
    {
        int index = (int)scancode;

        return index >= 0 && index < keys.Length && keys[index];
    }
}

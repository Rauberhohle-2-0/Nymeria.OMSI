using SDL3;

namespace Nymeria.Omsi;

/// <summary>
/// Presents a cleared frame into a window already claimed by
/// <see cref="Nymeria.Graphics.GpuDevice"/>.
/// </summary>
/// <remarks>
/// <para>
/// The bootstrap milestone deliberately draws nothing: no shader asset is
/// created, so no MSL source and no build-generated DXIL is needed and the
/// DirectX Shader Compiler never runs. A render pass with a clear load
/// operation is a complete frame, which exercises window presentation and the
/// device's real backend on both supported platforms.
/// </para>
/// <para>
/// This is host-boundary code. Nymeria exposes the native <c>SDL_GPUDevice</c>
/// handle for exactly this purpose; every example in the engine does the same.
/// It is not a rendering abstraction and must not grow into one.
/// </para>
/// <para>
/// Construction, use, and disposal happen on the process main thread, and the
/// window and device must outlive this object.
/// </para>
/// </remarks>
internal sealed class ClearFramePresenter : IFramePresenter
{
    private static readonly SDL.FColor ClearColor = new()
    {
        R = 0.05f,
        G = 0.07f,
        B = 0.10f,
        A = 1.0f,
    };

    private readonly IntPtr _window;
    private readonly IntPtr _device;

    /// <summary>
    /// Creates a presenter for an existing window and GPU device.
    /// </summary>
    /// <param name="window">The native <c>SDL_Window*</c> handle.</param>
    /// <param name="device">The engine device that claimed the window.</param>
    /// <exception cref="ObjectDisposedException">The device has been disposed.</exception>
    public ClearFramePresenter(IntPtr window, Nymeria.Graphics.GpuDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        _window = window;
        _device = device.Handle;
    }

    /// <summary>
    /// Presents one cleared frame.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when a frame was submitted for presentation, and
    /// <see langword="false"/> when this frame had nothing to draw into, such as
    /// while the window is minimized or the swapchain is full. A skipped frame is
    /// not an error.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// SDL could not acquire a command buffer, acquire the swapchain texture, or
    /// submit the frame.
    /// </exception>
    public bool Present()
    {
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

        // Do not block on a swapchain texture: resize and minimize must not stall
        // the event loop. A null texture means "skip this frame".
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

        // A swapchain texture has been acquired, so cancellation is no longer
        // allowed. A zero-sized drawable is submitted empty instead.
        if (width == 0 || height == 0)
        {
            SDL.SubmitGPUCommandBuffer(commandBuffer);
            return false;
        }

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
            // The acquired texture cannot be handed back, so submit the empty
            // command buffer before reporting the failure.
            string error = SDL.GetError();
            SDL.SubmitGPUCommandBuffer(commandBuffer);

            throw new InvalidOperationException(
                $"SDL could not begin a GPU render pass: {error}");
        }

        SDL.EndGPURenderPass(renderPass);

        if (!SDL.SubmitGPUCommandBuffer(commandBuffer))
        {
            throw new InvalidOperationException(
                $"SDL could not submit the GPU command buffer: {SDL.GetError()}");
        }

        return true;
    }

    /// <summary>
    /// Releases nothing: the device and the window belong to the host.
    /// </summary>
    public void Dispose()
    {
    }
}

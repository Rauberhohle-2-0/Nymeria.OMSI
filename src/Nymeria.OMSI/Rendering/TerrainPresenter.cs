using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Nymeria.Graphics;
using Nymeria.Omsi.Terrain;
using SDL3;

namespace Nymeria.Omsi.Rendering;

/// <summary>
/// Draws decoded OMSI terrain through the engine's GPU device into one window.
/// </summary>
/// <remarks>
/// <para>
/// The device, its backend selection, and the shader-format policy belong to
/// Nymeria; this type is host-boundary code that records one render pass with the
/// engine device handle, exactly as the engine's own examples do. It creates no
/// second renderer and no abstraction over passes or resources.
/// </para>
/// <para>
/// Static terrain geometry is uploaded once: one vertex buffer, one index buffer, one
/// graphics pipeline, and one depth texture that is recreated only when the drawable
/// size changes. The camera comes from <see cref="TerrainCameraController"/>; the
/// only per-frame GPU writes are the view-projection uniform and the draw itself.
/// </para>
/// <para>
/// What the window shows is terrain only. Scenery objects, splines, ground textures,
/// and water are not part of this view, and the window title and console summary both
/// say so.
/// </para>
/// <para>
/// Every member runs on the process main thread. The window and device must outlive
/// the presenter.
/// </para>
/// </remarks>
internal sealed class TerrainPresenter : IFramePresenter
{
    /// <summary>
    /// The logical resource-name prefix of the terrain shader family. The engine
    /// resolves the per-format resource names and entry points from it.
    /// </summary>
    internal const string ShaderResourcePrefix = "Nymeria.OMSI.Shaders.Terrain";

    private static readonly SDL.FColor ClearColor = new()
    {
        R = 0.04f,
        G = 0.05f,
        B = 0.07f,
        A = 1.0f,
    };

    private readonly IntPtr _window;
    private readonly IntPtr _device;
    private readonly GpuDevice _engineDevice;
    private readonly OmsiTerrainModel _model;
    private readonly TerrainCameraController _controller;
    private readonly IntPtr _vertexShader;
    private readonly IntPtr _fragmentShader;
    private readonly IntPtr _vertexBuffer;
    private readonly IntPtr _indexBuffer;
    private readonly IntPtr _pipeline;
    private readonly SDL.GPUTextureFormat _depthFormat;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private IntPtr _depthTexture;
    private uint _depthWidth;
    private uint _depthHeight;
    private double _lastFrameSeconds;
    private bool _isLooking;
    private bool _disposed;

    private TerrainPresenter(
        IntPtr window,
        GpuDevice device,
        OmsiTerrainModel model,
        TerrainCameraController controller,
        IntPtr vertexShader,
        IntPtr fragmentShader,
        IntPtr vertexBuffer,
        IntPtr indexBuffer,
        IntPtr pipeline,
        SDL.GPUTextureFormat depthFormat)
    {
        _window = window;
        _engineDevice = device;
        _device = device.Handle;
        _model = model;
        _controller = controller;
        _vertexShader = vertexShader;
        _fragmentShader = fragmentShader;
        _vertexBuffer = vertexBuffer;
        _indexBuffer = indexBuffer;
        _pipeline = pipeline;
        _depthFormat = depthFormat;
    }

    /// <summary>
    /// Creates the shaders, buffers, depth format, and pipeline for a terrain model.
    /// </summary>
    /// <param name="window">The native <c>SDL_Window*</c> handle.</param>
    /// <param name="device">The engine device that claimed the window.</param>
    /// <param name="model">The merged terrain geometry to draw.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The window handle is zero.</exception>
    /// <exception cref="InvalidOperationException">
    /// The device has no shader asset in its format, the model has no geometry, or GPU
    /// resource creation fails.
    /// </exception>
    internal static TerrainPresenter Create(
        IntPtr window,
        GpuDevice device,
        OmsiTerrainModel model)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(model);

        if (window == IntPtr.Zero)
        {
            throw new ArgumentException(
                "A native window handle is required.",
                nameof(window));
        }

        if (model.Bounds is not TerrainBounds bounds || model.Indices.Length == 0)
        {
            throw new InvalidOperationException(
                "The terrain model has no geometry, so there is nothing to draw.");
        }

        Assembly assembly = typeof(TerrainPresenter).Assembly;
        ShaderProgram program = ShaderProgram.Select(
            ShaderResourcePrefix,
            device.ShaderFormat);

        byte[] vertexCode = program.Vertex.ReadBytes(assembly);

        // The Metal shader family keeps both stages in one source resource.
        byte[] fragmentCode = program.Fragment.ResourceName == program.Vertex.ResourceName
            ? vertexCode
            : program.Fragment.ReadBytes(assembly);

        IntPtr vertexShader = IntPtr.Zero;
        IntPtr fragmentShader = IntPtr.Zero;
        IntPtr vertexBuffer = IntPtr.Zero;
        IntPtr indexBuffer = IntPtr.Zero;
        IntPtr pipeline = IntPtr.Zero;

        try
        {
            vertexShader = device.CreateShader(
                vertexCode,
                program.Vertex.Format,
                SDL.GPUShaderStage.Vertex,
                program.Vertex.EntryPoint,
                uniformBufferCount: 1);
            fragmentShader = device.CreateShader(
                fragmentCode,
                program.Fragment.Format,
                SDL.GPUShaderStage.Fragment,
                program.Fragment.EntryPoint);
            vertexBuffer = device.CreateBuffer(
                MemoryMarshal.AsBytes(model.Vertices.AsSpan()),
                SDL.GPUBufferUsageFlags.Vertex);
            indexBuffer = device.CreateBuffer(
                MemoryMarshal.AsBytes(model.Indices.AsSpan()),
                SDL.GPUBufferUsageFlags.Index);

            SDL.GPUTextureFormat depthFormat = ChooseDepthFormat(device.Handle);
            pipeline = CreatePipeline(
                device.Handle,
                vertexShader,
                fragmentShader,
                device.SwapchainFormat,
                depthFormat);

            var controller = new TerrainCameraController(
                new Camera(bounds.Center),
                TerrainCameraFraming.FromBounds(bounds));

            return new TerrainPresenter(
                window,
                device,
                model,
                controller,
                vertexShader,
                fragmentShader,
                vertexBuffer,
                indexBuffer,
                pipeline,
                depthFormat);
        }
        catch
        {
            // Buffer creation submits its own upload command buffer, so drain pending
            // work before releasing anything the GPU may still reference.
            SDL.WaitForGPUIdle(device.Handle);

            if (pipeline != IntPtr.Zero)
            {
                SDL.ReleaseGPUGraphicsPipeline(device.Handle, pipeline);
            }

            if (indexBuffer != IntPtr.Zero)
            {
                SDL.ReleaseGPUBuffer(device.Handle, indexBuffer);
            }

            if (vertexBuffer != IntPtr.Zero)
            {
                SDL.ReleaseGPUBuffer(device.Handle, vertexBuffer);
            }

            if (fragmentShader != IntPtr.Zero)
            {
                SDL.ReleaseGPUShader(device.Handle, fragmentShader);
            }

            if (vertexShader != IntPtr.Zero)
            {
                SDL.ReleaseGPUShader(device.Handle, vertexShader);
            }

            throw;
        }
    }

    /// <summary>
    /// Applies input, then draws one depth-tested frame of the terrain.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when a frame was submitted, and <see langword="false"/>
    /// when this frame had nothing to draw into, such as while minimized.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// SDL could not acquire a command buffer, the swapchain texture, or the render
    /// pass, or could not submit the frame.
    /// </exception>
    public bool Present()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        UpdateCamera();

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

        Matrix4x4 viewProjection;

        try
        {
            EnsureDepthTexture(width, height);
            viewProjection =
                _controller.Camera.ViewMatrix *
                _controller.Camera.CreateProjectionMatrix((float)width / height);
        }
        catch
        {
            // A swapchain texture is already acquired, so the command buffer cannot be
            // cancelled; submit the still-empty buffer instead of leaking it.
            SDL.SubmitGPUCommandBuffer(commandBuffer);
            throw;
        }

        IntPtr renderPass = BeginRenderPass(commandBuffer, swapchainTexture);

        if (renderPass == IntPtr.Zero)
        {
            string error = SDL.GetError();
            SDL.SubmitGPUCommandBuffer(commandBuffer);

            throw new InvalidOperationException(
                $"SDL could not begin a GPU render pass: {error}");
        }

        SDL.BindGPUGraphicsPipeline(renderPass, _pipeline);

        Span<SDL.GPUBufferBinding> vertexBindings = stackalloc SDL.GPUBufferBinding[1];
        vertexBindings[0] = new SDL.GPUBufferBinding
        {
            Buffer = _vertexBuffer,
            Offset = 0,
        };
        SDL.BindGPUVertexBuffers(renderPass, 0, vertexBindings, 1);

        var indexBinding = new SDL.GPUBufferBinding
        {
            Buffer = _indexBuffer,
            Offset = 0,
        };
        SDL.BindGPUIndexBuffer(
            renderPass,
            in indexBinding,
            SDL.GPUIndexElementSize.IndexElementSize32Bit);

        var viewport = new SDL.GPUViewport
        {
            X = 0.0f,
            Y = 0.0f,
            W = width,
            H = height,
            MinDepth = 0.0f,
            MaxDepth = 1.0f,
        };
        var scissor = new SDL.Rect
        {
            X = 0,
            Y = 0,
            W = (int)width,
            H = (int)height,
        };
        SDL.SetGPUViewport(renderPass, in viewport);
        SDL.SetGPUScissor(renderPass, in scissor);

        Span<Matrix4x4> uniform = stackalloc Matrix4x4[1];
        uniform[0] = viewProjection;
        ReadOnlySpan<byte> uniformBytes = MemoryMarshal.AsBytes(uniform);

        SDL.PushGPUVertexUniformData(
            commandBuffer,
            0,
            uniformBytes,
            (uint)uniformBytes.Length);

        SDL.DrawGPUIndexedPrimitives(
            renderPass,
            (uint)_model.Indices.Length,
            1,
            0,
            0,
            0);

        SDL.EndGPURenderPass(renderPass);

        if (!SDL.SubmitGPUCommandBuffer(commandBuffer))
        {
            throw new InvalidOperationException(
                $"SDL could not submit the GPU command buffer: {SDL.GetError()}");
        }

        return true;
    }

    /// <summary>
    /// Releases the terrain resources. The device and window belong to the host.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_isLooking)
        {
            _isLooking = false;
            SDL.SetWindowRelativeMouseMode(_window, false);
        }

        SDL.WaitForGPUIdle(_device);

        if (_depthTexture != IntPtr.Zero)
        {
            SDL.ReleaseGPUTexture(_device, _depthTexture);
            _depthTexture = IntPtr.Zero;
        }

        SDL.ReleaseGPUGraphicsPipeline(_device, _pipeline);
        SDL.ReleaseGPUBuffer(_device, _indexBuffer);
        SDL.ReleaseGPUBuffer(_device, _vertexBuffer);
        SDL.ReleaseGPUShader(_device, _fragmentShader);
        SDL.ReleaseGPUShader(_device, _vertexShader);
    }

    /// <summary>
    /// Reads one frame of SDL3 input and hands it to the camera controller.
    /// </summary>
    private void UpdateCamera()
    {
        double now = _clock.Elapsed.TotalSeconds;
        float deltaSeconds = (float)(now - _lastFrameSeconds);
        _lastFrameSeconds = now;

        SDL.WindowFlags windowFlags = SDL.GetWindowFlags(_window);
        bool canLook =
            (windowFlags & SDL.WindowFlags.InputFocus) != 0 &&
            (windowFlags & SDL.WindowFlags.Minimized) == 0;

        SDL.MouseButtonFlags buttons = SDL.GetMouseState(out _, out _);
        bool wantsToLook =
            canLook && (buttons & SDL.MouseButtonFlags.Right) != 0;
        float mouseDeltaX = 0.0f;
        float mouseDeltaY = 0.0f;

        if (wantsToLook != _isLooking)
        {
            // Discard motion accumulated before capture changed so the view does not
            // jump, and keep the cursor visible whenever look is not active.
            SDL.GetRelativeMouseState(out _, out _);
            SDL.SetWindowRelativeMouseMode(_window, wantsToLook);
            _isLooking = wantsToLook;
        }
        else if (wantsToLook)
        {
            SDL.GetRelativeMouseState(out mouseDeltaX, out mouseDeltaY);
        }

        ReadOnlySpan<bool> keys = SDL.GetKeyboardState(out _);

        var input = new TerrainInputState(
            Forward: IsKeyDown(keys, SDL.Scancode.W),
            Backward: IsKeyDown(keys, SDL.Scancode.S),
            Left: IsKeyDown(keys, SDL.Scancode.A),
            Right: IsKeyDown(keys, SDL.Scancode.D),
            Up: IsKeyDown(keys, SDL.Scancode.Space),
            Down: IsKeyDown(keys, SDL.Scancode.LCtrl),
            Sprint:
                IsKeyDown(keys, SDL.Scancode.LShift) ||
                IsKeyDown(keys, SDL.Scancode.RShift),
            ResetView: IsKeyDown(keys, SDL.Scancode.R),
            Looking: wantsToLook,
            MouseDeltaX: mouseDeltaX,
            MouseDeltaY: mouseDeltaY);

        _controller.Update(input, deltaSeconds);
    }

    /// <summary>
    /// Acquires a render pass that clears the colour and depth targets.
    /// </summary>
    private IntPtr BeginRenderPass(IntPtr commandBuffer, IntPtr swapchainTexture)
    {
        Span<SDL.GPUColorTargetInfo> colorTargets = stackalloc SDL.GPUColorTargetInfo[1];
        colorTargets[0] = new SDL.GPUColorTargetInfo
        {
            Texture = swapchainTexture,
            ClearColor = ClearColor,
            LoadOp = SDL.GPULoadOp.Clear,
            StoreOp = SDL.GPUStoreOp.Store,
        };

        var depthTarget = new SDL.GPUDepthStencilTargetInfo
        {
            Texture = _depthTexture,
            ClearDepth = 1.0f,
            LoadOp = SDL.GPULoadOp.Clear,
            StoreOp = SDL.GPUStoreOp.DontCare,
            StencilLoadOp = SDL.GPULoadOp.DontCare,
            StencilStoreOp = SDL.GPUStoreOp.DontCare,
            Cycle = 1,
        };

        return SDL.BeginGPURenderPass(
            commandBuffer,
            colorTargets,
            1,
            in depthTarget);
    }

    /// <summary>
    /// Creates the depth texture, or replaces it when the drawable size changed.
    /// </summary>
    private void EnsureDepthTexture(uint width, uint height)
    {
        if (_depthTexture != IntPtr.Zero && _depthWidth == width && _depthHeight == height)
        {
            return;
        }

        IntPtr depthTexture = SDL.CreateGPUTexture(
            _device,
            new SDL.GPUTextureCreateInfo
            {
                Type = SDL.GPUTextureType.TextureType2D,
                Format = _depthFormat,
                Usage = SDL.GPUTextureUsageFlags.DepthStencilTarget,
                Width = width,
                Height = height,
                LayerCountOrDepth = 1,
                NumLevels = 1,
                SampleCount = SDL.GPUSampleCount.SampleCount1,
            });

        if (depthTexture == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"SDL could not create a {_depthFormat} depth texture: {SDL.GetError()}");
        }

        if (_depthTexture != IntPtr.Zero)
        {
            // The previous frame's command buffer may still reference the old
            // texture. Resizes are rare, so draining the GPU keeps lifetime handling
            // simple and safe.
            if (!SDL.WaitForGPUIdle(_device))
            {
                SDL.ReleaseGPUTexture(_device, depthTexture);

                throw new InvalidOperationException(
                    $"SDL could not wait for the GPU while resizing the depth " +
                    $"texture: {SDL.GetError()}");
            }

            SDL.ReleaseGPUTexture(_device, _depthTexture);
        }

        _depthTexture = depthTexture;
        _depthWidth = width;
        _depthHeight = height;
    }

    /// <summary>
    /// Chooses the first depth format the backend supports for a depth target.
    /// </summary>
    private static SDL.GPUTextureFormat ChooseDepthFormat(IntPtr device)
    {
        ReadOnlySpan<SDL.GPUTextureFormat> candidates =
        [
            SDL.GPUTextureFormat.D32Float,
            SDL.GPUTextureFormat.D24Unorm,
            SDL.GPUTextureFormat.D16Unorm,
        ];

        foreach (SDL.GPUTextureFormat format in candidates)
        {
            if (SDL.GPUTextureSupportsFormat(
                    device,
                    format,
                    SDL.GPUTextureType.TextureType2D,
                    SDL.GPUTextureUsageFlags.DepthStencilTarget))
            {
                return format;
            }
        }

        throw new InvalidOperationException(
            "The GPU backend supports none of the required 2D depth formats " +
            "(D32Float, D24Unorm, D16Unorm), so depth-tested terrain cannot be drawn.");
    }

    /// <summary>
    /// Creates the terrain graphics pipeline: one interleaved vertex buffer with
    /// position, normal, and colour, 32-bit indices, and a depth test that keeps the
    /// nearest surface.
    /// </summary>
    /// <remarks>
    /// Both triangle orientations are rasterized, matching every renderer in the
    /// pinned engine: the terrain is a single open surface seen from above, so culling
    /// adds nothing, and the depth test is what resolves occlusion. The mesh is still
    /// wound counter-clockwise seen from above and that winding is unit-tested, so
    /// culling can be enabled later together with GPU validation instead of by guess.
    /// </remarks>
    private static IntPtr CreatePipeline(
        IntPtr device,
        IntPtr vertexShader,
        IntPtr fragmentShader,
        SDL.GPUTextureFormat swapchainFormat,
        SDL.GPUTextureFormat depthFormat)
    {
        var vertexBuffer = new SDL.GPUVertexBufferDescription
        {
            Slot = 0,
            Pitch = TerrainVertex.SizeInBytes,
            InputRate = SDL.GPUVertexInputRate.Vertex,
        };

        SDL.GPUVertexAttribute[] attributes =
        [
            new()
            {
                Location = 0,
                BufferSlot = 0,
                Format = SDL.GPUVertexElementFormat.Float3,
                Offset = TerrainVertex.PositionOffsetBytes,
            },
            new()
            {
                Location = 1,
                BufferSlot = 0,
                Format = SDL.GPUVertexElementFormat.Float3,
                Offset = TerrainVertex.NormalOffsetBytes,
            },
            new()
            {
                Location = 2,
                BufferSlot = 0,
                Format = SDL.GPUVertexElementFormat.Float3,
                Offset = TerrainVertex.ColorOffsetBytes,
            },
        ];

        SDL.GPUColorTargetDescription[] colorTargets =
        [
            new() { Format = swapchainFormat },
        ];

        IntPtr pipeline = SDL.CreateGPUGraphicsPipeline(
            device,
            new SDL.GPUGraphicsPipelineCreateInfo
            {
                VertexShader = vertexShader,
                FragmentShader = fragmentShader,
                PrimitiveType = SDL.GPUPrimitiveType.TriangleList,
                VertexInputState = new SDL.GPUVertexInputState
                {
                    NumVertexBuffers = 1,
                    NumVertexAttributes = 3,
                },
                RasterizerState = new SDL.GPURasterizerState
                {
                    FillMode = SDL.GPUFillMode.Fill,
                    CullMode = SDL.GPUCullMode.None,
                    FrontFace = SDL.GPUFrontFace.CounterClockwise,
                    EnableDepthClip = true,
                },
                MultisampleState = new SDL.GPUMultisampleState
                {
                    SampleCount = SDL.GPUSampleCount.SampleCount1,
                },
                DepthStencilState = new SDL.GPUDepthStencilState
                {
                    EnableDepthTest = true,
                    EnableDepthWrite = true,
                    EnableStencilTest = false,
                    CompareOp = SDL.GPUCompareOp.Less,
                },
                TargetInfo = new SDL.GPUGraphicsPipelineTargetInfo
                {
                    NumColorTargets = 1,
                    HasDepthStencilTarget = true,
                    DepthStencilFormat = depthFormat,
                },
            },
            new ReadOnlySpan<SDL.GPUVertexBufferDescription>(in vertexBuffer),
            attributes,
            colorTargets);

        if (pipeline == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"SDL could not create the terrain graphics pipeline for swapchain " +
                $"format '{swapchainFormat}' and depth format '{depthFormat}': " +
                SDL.GetError());
        }

        return pipeline;
    }

    private static bool IsKeyDown(ReadOnlySpan<bool> keys, SDL.Scancode scancode)
    {
        int index = (int)scancode;

        return index >= 0 && index < keys.Length && keys[index];
    }
}

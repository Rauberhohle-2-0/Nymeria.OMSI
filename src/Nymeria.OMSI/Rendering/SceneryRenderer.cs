using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Nymeria.Core.Materials;
using Nymeria.Core.Models;
using Nymeria.Graphics;
using Nymeria.Omsi.Scenery;
using SDL3;

namespace Nymeria.Omsi.Rendering;

/// <summary>
/// Uploads loaded scenery once and draws its instances into an existing render pass.
/// </summary>
/// <remarks>
/// <para>
/// This is host-boundary code, like <see cref="ScenePresenter"/>: it records draws with
/// the engine's GPU device handle and creates no second renderer abstraction. All
/// geometry and texture decoding happened earlier, on the CPU, in
/// <see cref="OmsiSceneryLibrary"/>.
/// </para>
/// <para>
/// Every distinct mesh file becomes one vertex buffer and one index buffer, and every
/// distinct decoded texture becomes one GPU texture, no matter how many placements use
/// them. Static buffers are never rewritten: a frame only pushes two uniform matrices,
/// binds buffers, and issues one indexed draw per drawable subset.
/// </para>
/// <para>
/// A model that fails to upload is dropped with a counted reason instead of taking the
/// window down; only a scene where nothing at all could be uploaded fails creation,
/// because then there is nothing to show and the caller must know.
/// </para>
/// </remarks>
internal sealed class SceneryRenderer : IDisposable
{
    /// <summary>
    /// The logical resource-name prefix of the scenery shader family. The engine
    /// resolves the per-format resource names and entry points from it.
    /// </summary>
    internal const string ShaderResourcePrefix = "Nymeria.OMSI.Shaders.Scenery";

    private readonly GpuDevice _engineDevice;
    private readonly IntPtr _device;
    private readonly IntPtr _pipeline;
    private readonly IntPtr _vertexShader;
    private readonly IntPtr _fragmentShader;
    private readonly IntPtr _sampler;
    private readonly IntPtr _fallbackTexture;
    private readonly IReadOnlyList<SceneryInstance> _instances;
    private readonly Dictionary<SceneryMesh, MeshBuffers> _buffers = [];
    private readonly Dictionary<Texture, IntPtr> _textures = [];
    private readonly HashSet<SceneryModel> _drawableModels = [];
    private int _drawnInstances;
    private bool _disposed;

    private SceneryRenderer(
        GpuDevice device,
        IntPtr pipeline,
        IntPtr vertexShader,
        IntPtr fragmentShader,
        IntPtr sampler,
        IntPtr fallbackTexture,
        IReadOnlyList<SceneryInstance> instances)
    {
        _engineDevice = device;
        _device = device.Handle;
        _pipeline = pipeline;
        _vertexShader = vertexShader;
        _fragmentShader = fragmentShader;
        _sampler = sampler;
        _fallbackTexture = fallbackTexture;
        _instances = instances;
    }

    /// <summary>Gets the number of instances that will be drawn.</summary>
    internal int DrawnInstances => _drawnInstances;

    /// <summary>Gets the number of distinct mesh files uploaded.</summary>
    internal int UploadedMeshes => _buffers.Count;

    /// <summary>Gets the number of distinct textures uploaded.</summary>
    internal int UploadedTextures => _textures.Count;

    /// <summary>Gets the number of instances whose model could not be uploaded.</summary>
    internal int FailedInstances { get; private set; }

    /// <summary>Gets the number of drawable subsets across uploaded models.</summary>
    internal int DrawnSubsets { get; private set; }

    /// <summary>
    /// Creates the scenery pipeline, uploads the library's geometry and textures, and
    /// prepares its instances.
    /// </summary>
    /// <param name="device">The engine device that owns the window.</param>
    /// <param name="library">The CPU-side scenery loaded before the window existed.</param>
    /// <param name="swapchainFormat">The swapchain colour format of the frame.</param>
    /// <param name="depthFormat">The depth format the frame's render pass uses.</param>
    /// <returns>The renderer, ready to draw into a compatible render pass.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The device has no shader asset in its format, GPU resource creation fails, or no
    /// instance at all could be prepared.
    /// </exception>
    internal static SceneryRenderer Create(
        GpuDevice device,
        OmsiSceneryLibrary library,
        SDL.GPUTextureFormat swapchainFormat,
        SDL.GPUTextureFormat depthFormat)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(library);

        if (library.Instances.Count == 0)
        {
            throw new InvalidOperationException(
                "The scenery library contains no placement, so there is nothing to draw.");
        }

        Assembly assembly = typeof(SceneryRenderer).Assembly;
        ShaderProgram program = ShaderProgram.Select(ShaderResourcePrefix, device.ShaderFormat);

        byte[] vertexCode = program.Vertex.ReadBytes(assembly);
        byte[] fragmentCode = program.Fragment.ResourceName == program.Vertex.ResourceName
            ? vertexCode
            : program.Fragment.ReadBytes(assembly);

        IntPtr vertexShader = IntPtr.Zero;
        IntPtr fragmentShader = IntPtr.Zero;
        IntPtr pipeline = IntPtr.Zero;
        IntPtr sampler = IntPtr.Zero;
        IntPtr fallbackTexture = IntPtr.Zero;
        SceneryRenderer? renderer = null;

        try
        {
            vertexShader = device.CreateShader(
                vertexCode,
                program.Vertex.Format,
                SDL.GPUShaderStage.Vertex,
                program.Vertex.EntryPoint,
                uniformBufferCount: 2);
            fragmentShader = device.CreateShader(
                fragmentCode,
                program.Fragment.Format,
                SDL.GPUShaderStage.Fragment,
                program.Fragment.EntryPoint,
                samplerCount: 1);

            sampler = CreateSampler(device.Handle);
            fallbackTexture = device.CreateTexture(
                new byte[] { 255, 255, 255, 255 },
                1u,
                1u);

            pipeline = CreatePipeline(
                device.Handle,
                vertexShader,
                fragmentShader,
                swapchainFormat,
                depthFormat);

            renderer = new SceneryRenderer(
                device,
                pipeline,
                vertexShader,
                fragmentShader,
                sampler,
                fallbackTexture,
                library.Instances);

            renderer.Prepare(library);

            if (renderer._drawableModels.Count == 0)
            {
                throw new InvalidOperationException(
                    "No scenery model could be uploaded to the GPU, so the scene would " +
                    "be empty.");
            }

            return renderer;
        }
        catch
        {
            // Buffer and texture creation submit their own upload command buffers, so
            // drain pending work before releasing anything the GPU may still reference.
            SDL.WaitForGPUIdle(device.Handle);

            renderer?.ReleaseResources();

            if (pipeline != IntPtr.Zero)
            {
                SDL.ReleaseGPUGraphicsPipeline(device.Handle, pipeline);
            }

            if (sampler != IntPtr.Zero)
            {
                SDL.ReleaseGPUSampler(device.Handle, sampler);
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
    /// Draws every prepared instance into an already-open, depth-tested render pass.
    /// </summary>
    /// <param name="renderPass">The render pass to record into.</param>
    /// <param name="commandBuffer">The command buffer that owns the render pass.</param>
    /// <param name="viewProjection">The frame's view-projection matrix.</param>
    /// <exception cref="ObjectDisposedException">The renderer was disposed.</exception>
    internal void Draw(IntPtr renderPass, IntPtr commandBuffer, Matrix4x4 viewProjection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_drawableModels.Count == 0)
        {
            return;
        }

        SDL.BindGPUGraphicsPipeline(renderPass, _pipeline);

        Span<Matrix4x4> frame = stackalloc Matrix4x4[1];
        frame[0] = viewProjection;
        ReadOnlySpan<byte> frameBytes = MemoryMarshal.AsBytes(frame);

        SDL.PushGPUVertexUniformData(
            commandBuffer,
            0,
            frameBytes,
            (uint)frameBytes.Length);

        Span<Matrix4x4> model = stackalloc Matrix4x4[1];
        Span<SDL.GPUBufferBinding> vertexBindings = stackalloc SDL.GPUBufferBinding[1];
        Span<SDL.GPUTextureSamplerBinding> samplerBindings =
            stackalloc SDL.GPUTextureSamplerBinding[1];

        foreach (SceneryInstance instance in _instances)
        {
            if (!_drawableModels.Contains(instance.Model))
            {
                continue;
            }

            model[0] = instance.Transform;
            ReadOnlySpan<byte> modelBytes = MemoryMarshal.AsBytes(model);

            SDL.PushGPUVertexUniformData(
                commandBuffer,
                1,
                modelBytes,
                (uint)modelBytes.Length);

            foreach (ScenerySubset subset in instance.Model.Subsets)
            {
                if (!_buffers.TryGetValue(subset.Mesh, out MeshBuffers gpuMesh))
                {
                    continue;
                }

                vertexBindings[0] = new SDL.GPUBufferBinding
                {
                    Buffer = gpuMesh.VertexBuffer,
                    Offset = 0,
                };
                SDL.BindGPUVertexBuffers(renderPass, 0, vertexBindings, 1);

                var indexBinding = new SDL.GPUBufferBinding
                {
                    Buffer = gpuMesh.IndexBuffer,
                    Offset = 0,
                };
                SDL.BindGPUIndexBuffer(
                    renderPass,
                    in indexBinding,
                    SDL.GPUIndexElementSize.IndexElementSize32Bit);

                samplerBindings[0] = new SDL.GPUTextureSamplerBinding
                {
                    Texture = ResolveTexture(subset.Material),
                    Sampler = _sampler,
                };
                SDL.BindGPUFragmentSamplers(renderPass, 0, samplerBindings, 1);

                SDL.DrawGPUIndexedPrimitives(
                    renderPass,
                    (uint)subset.IndexCount,
                    1,
                    (uint)subset.IndexOffset,
                    0,
                    0);
            }
        }
    }

    /// <summary>
    /// Releases every GPU resource this renderer owns. The device and window belong to
    /// the host.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        SDL.WaitForGPUIdle(_device);

        ReleaseResources();

        SDL.ReleaseGPUGraphicsPipeline(_device, _pipeline);
        SDL.ReleaseGPUSampler(_device, _sampler);
        SDL.ReleaseGPUTexture(_device, _fallbackTexture);
        SDL.ReleaseGPUShader(_device, _fragmentShader);
        SDL.ReleaseGPUShader(_device, _vertexShader);
    }

    /// <summary>
    /// Uploads the geometry and textures of every model the library holds.
    /// </summary>
    private void Prepare(OmsiSceneryLibrary library)
    {
        foreach (SceneryModel model in library.Models)
        {
            try
            {
                foreach (ScenerySubset subset in model.Subsets)
                {
                    EnsureMeshUploaded(subset.Mesh);

                    if (subset.Material.BaseColorTexture is Texture texture)
                    {
                        EnsureTextureUploaded(texture);
                    }
                }

                _drawableModels.Add(model);
                DrawnSubsets += model.Subsets.Count;
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or NotSupportedException or
                ArgumentException)
            {
                Console.Error.WriteLine(
                    "scenery: the model '" + model.SourceReference +
                    "' could not be uploaded and is not drawn: " + exception.Message);
            }
        }

        foreach (SceneryInstance instance in _instances)
        {
            if (_drawableModels.Contains(instance.Model))
            {
                _drawnInstances++;
            }
            else
            {
                FailedInstances++;
            }
        }
    }

    private void EnsureMeshUploaded(SceneryMesh mesh)
    {
        if (_buffers.ContainsKey(mesh))
        {
            return;
        }

        SceneryVertex[] vertices = BuildVertices(mesh.Data);

        IntPtr vertexBuffer = IntPtr.Zero;
        IntPtr indexBuffer = IntPtr.Zero;

        try
        {
            vertexBuffer = CreateBuffer(vertices, SDL.GPUBufferUsageFlags.Vertex);
            indexBuffer = CreateIndexBuffer(mesh.Data.Indices.Span);
        }
        catch
        {
            if (vertexBuffer != IntPtr.Zero)
            {
                SDL.ReleaseGPUBuffer(_device, vertexBuffer);
            }

            if (indexBuffer != IntPtr.Zero)
            {
                SDL.ReleaseGPUBuffer(_device, indexBuffer);
            }

            throw;
        }

        _buffers.Add(mesh, new MeshBuffers(vertexBuffer, indexBuffer));
    }

    private IntPtr CreateBuffer(SceneryVertex[] vertices, SDL.GPUBufferUsageFlags usage)
    {
        // The engine's helper owns the transfer buffer and the one-off copy pass, so
        // the host does not hand-roll upload bookkeeping.
        return _engineDevice.CreateBuffer(
            MemoryMarshal.AsBytes(vertices.AsSpan()),
            usage);
    }

    private IntPtr CreateIndexBuffer(ReadOnlySpan<uint> indices)
    {
        return _engineDevice.CreateBuffer(
            MemoryMarshal.AsBytes(indices),
            SDL.GPUBufferUsageFlags.Index);
    }

    private void EnsureTextureUploaded(Texture texture)
    {
        if (_textures.ContainsKey(texture))
        {
            return;
        }

        ResolveTextureHandle(texture, out IntPtr handle);
        _textures.Add(texture, handle);
    }

    /// <summary>
    /// Uploads one decoded texture, falling back to the shared white pixel when the
    /// format cannot be sampled by the backend.
    /// </summary>
    private void ResolveTextureHandle(Texture texture, out IntPtr handle)
    {
        handle = IntPtr.Zero;

        try
        {
            handle = UploadTexture(texture);
        }
        catch (NotSupportedException exception)
        {
            Console.Error.WriteLine(
                "scenery: the texture '" + (texture.Name ?? "<unnamed>") +
                "' is not uploaded: " + exception.Message);
        }

        if (handle == IntPtr.Zero)
        {
            handle = _fallbackTexture;
        }
    }

    private IntPtr UploadTexture(Texture texture)
    {
        switch (texture)
        {
            case TextureData rgba:
                return UploadLevels(
                    SDL.GPUTextureFormat.R8G8B8A8Unorm,
                    rgba.Width,
                    rgba.Height,
                    rgba.MipLevels,
                    rgba.MipLevelCount);

            case CompressedTextureData compressed:
                {
                    SDL.GPUTextureFormat format = MapFormat(compressed);

                    int levels = 0;

                    for (int level = 0; level < compressed.MipLevelCount; level++)
                    {
                        // Only a block-aligned prefix of a compressed chain is uploaded;
                        // SDL's backends disagree about smaller block edges.
                        if (compressed.GetMipWidth(level) % 4 != 0 ||
                            compressed.GetMipHeight(level) % 4 != 0)
                        {
                            break;
                        }

                        levels++;
                    }

                    if (levels == 0)
                    {
                        throw new NotSupportedException(
                            $"{CompressedTextureData.GetFormatName(compressed.Format)} " +
                            $"{compressed.Width} x {compressed.Height} is not block " +
                            "aligned.");
                    }

                    return UploadLevels(
                        format,
                        compressed.Width,
                        compressed.Height,
                        compressed.MipLevels,
                        levels);
                }

            default:
                throw new NotSupportedException(
                    "The texture type '" + texture.GetType().Name + "' is not " +
                    "supported.");
        }
    }

    private IntPtr UploadLevels(
        SDL.GPUTextureFormat format,
        int width,
        int height,
        IReadOnlyList<ReadOnlyMemory<byte>> levels,
        int levelCount)
    {
        var regions = new GpuDevice.TextureUploadRegion[levelCount];
        int totalBytes = 0;

        for (int level = 0; level < levelCount; level++)
        {
            regions[level] = new GpuDevice.TextureUploadRegion(
                totalBytes,
                levels[level].Length,
                (uint)Math.Max(1, width >> level),
                (uint)Math.Max(1, height >> level));

            totalBytes = checked(totalBytes + levels[level].Length);
        }

        var payload = new byte[totalBytes];

        for (int level = 0; level < levelCount; level++)
        {
            levels[level].Span.CopyTo(
                payload.AsSpan(regions[level].Offset, regions[level].Length));
        }

        return _engineDevice.CreateTexture(
            format,
            (uint)width,
            (uint)height,
            regions,
            payload);
    }

    private IntPtr ResolveTexture(Material material)
    {
        return material.BaseColorTexture is Texture texture &&
            _textures.TryGetValue(texture, out IntPtr handle)
            ? handle
            : _fallbackTexture;
    }

    private void ReleaseResources()
    {
        foreach (MeshBuffers buffers in _buffers.Values)
        {
            SDL.ReleaseGPUBuffer(_device, buffers.VertexBuffer);
            SDL.ReleaseGPUBuffer(_device, buffers.IndexBuffer);
        }

        _buffers.Clear();

        foreach (IntPtr texture in _textures.Values)
        {
            if (texture != _fallbackTexture && texture != IntPtr.Zero)
            {
                SDL.ReleaseGPUTexture(_device, texture);
            }
        }

        _textures.Clear();
        _drawableModels.Clear();
    }

    private static SceneryVertex[] BuildVertices(MeshData mesh)
    {
        ReadOnlySpan<Vector3> positions = mesh.Positions.Span;
        ReadOnlySpan<Vector3> normals = mesh.Normals.Span;
        ReadOnlySpan<Vector2> textureCoordinates = mesh.TextureCoordinates.Span;

        var vertices = new SceneryVertex[positions.Length];
        bool hasNormals = normals.Length == positions.Length;
        bool hasTextureCoordinates = textureCoordinates.Length == positions.Length;

        for (int index = 0; index < positions.Length; index++)
        {
            vertices[index] = new SceneryVertex(
                positions[index],
                hasNormals ? normals[index] : Vector3.UnitY,
                hasTextureCoordinates ? textureCoordinates[index] : Vector2.Zero);
        }

        return vertices;
    }

    private static SDL.GPUTextureFormat MapFormat(CompressedTextureData texture)
    {
        return (texture.Format, texture.IsSrgb) switch
        {
            (CompressedTextureFormat.Bc1, false) => SDL.GPUTextureFormat.BC1RGBAUnorm,
            (CompressedTextureFormat.Bc1, true) => SDL.GPUTextureFormat.BC1RGBAUnormSRGB,
            (CompressedTextureFormat.Bc2, false) => SDL.GPUTextureFormat.BC2RGBAUnorm,
            (CompressedTextureFormat.Bc2, true) => SDL.GPUTextureFormat.BC2RGBAUnormSRGB,
            (CompressedTextureFormat.Bc3, false) => SDL.GPUTextureFormat.BC3RGBAUnorm,
            (CompressedTextureFormat.Bc3, true) => SDL.GPUTextureFormat.BC3RGBAUnormSRGB,
            (CompressedTextureFormat.Bc4, _) => SDL.GPUTextureFormat.BC4RUnorm,
            (CompressedTextureFormat.Bc5, _) => SDL.GPUTextureFormat.BC5RGUnorm,
            (CompressedTextureFormat.Bc7, false) => SDL.GPUTextureFormat.BC7RGBAUnorm,
            (CompressedTextureFormat.Bc7, true) => SDL.GPUTextureFormat.BC7RGBAUnormSRGB,
            _ => throw new NotSupportedException(
                $"The compressed format '{texture.Format}' is not supported."),
        };
    }

    private static IntPtr CreateSampler(IntPtr device)
    {
        IntPtr sampler = SDL.CreateGPUSampler(
            device,
            new SDL.GPUSamplerCreateInfo
            {
                MinFilter = SDL.GPUFilter.Linear,
                MagFilter = SDL.GPUFilter.Linear,
                MipmapMode = SDL.GPUSamplerMipmapMode.Linear,
                // OMSI scenery UVs wrap: a facade repeats its texture across a wall.
                AddressModeU = SDL.GPUSamplerAddressMode.Repeat,
                AddressModeV = SDL.GPUSamplerAddressMode.Repeat,
                AddressModeW = SDL.GPUSamplerAddressMode.ClampToEdge,
                MinLod = 0.0f,
                MaxLod = 32.0f,
                MaxAnisotropy = 1.0f,
                EnableAnisotropy = false,
                EnableCompare = false,
            });

        if (sampler == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"SDL could not create the scenery sampler: {SDL.GetError()}");
        }

        return sampler;
    }

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
            Pitch = SceneryVertex.SizeInBytes,
            InputRate = SDL.GPUVertexInputRate.Vertex,
        };

        SDL.GPUVertexAttribute[] attributes =
        [
            new()
            {
                Location = 0,
                BufferSlot = 0,
                Format = SDL.GPUVertexElementFormat.Float3,
                Offset = SceneryVertex.PositionOffsetBytes,
            },
            new()
            {
                Location = 1,
                BufferSlot = 0,
                Format = SDL.GPUVertexElementFormat.Float3,
                Offset = SceneryVertex.NormalOffsetBytes,
            },
            new()
            {
                Location = 2,
                BufferSlot = 0,
                Format = SDL.GPUVertexElementFormat.Float2,
                Offset = SceneryVertex.TextureCoordinateOffsetBytes,
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
                    // OMSI meshes are not consistently wound, and the pinned engine's
                    // own model renderer also keeps culling off. The depth test resolves
                    // occlusion; enabling culling needs GPU validation first.
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
                $"SDL could not create the scenery graphics pipeline: {SDL.GetError()}");
        }

        return pipeline;
    }

    private readonly record struct MeshBuffers(IntPtr VertexBuffer, IntPtr IndexBuffer);
}

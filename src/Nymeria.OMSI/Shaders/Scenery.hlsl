// Scenery shaders for SDL3's Direct3D 12 backend.
//
// A Windows build compiles this file to DXIL with the DirectX Shader Compiler and
// embeds the result as Nymeria.OMSI.Shaders.Scenery.vert.dxil and
// ...frag.dxil; see Shaders/DxilShaders.targets. The macOS build embeds
// Shaders/Scenery.metal instead and runs no shader compiler. The two files must stay
// equivalent: the same position/normal/uv vertex layout, the same two uniform
// matrices, the same alpha-tested albedo sample, and the same shading function.
//
// SDL3's Direct3D 12 conventions, from the pinned SDL3 SDL_CreateGPUShader
// documentation:
//
//   * Non-system-value vertex semantics are TEXCOORD, indexed by the
//     SDL_GPUVertexAttribute location. Locations 0, 1, and 2 are TEXCOORD0,
//     TEXCOORD1, and TEXCOORD2.
//   * Vertex uniform buffers are register(b[n], space1), in SDL push-slot order.
//     This shader pushes viewProjection at slot 0 and the model matrix at slot 1.
//   * A pixel shader's textures are register(t[n], space2) and its samplers are
//     register(s[n], space2). This shader declares one texture and one sampler.
//
// System.Numerics stores a matrix row-major and transforms row vectors, so the 64
// bytes pushed for each matrix are row-major. The cbuffer matrices are declared
// column_major explicitly, so the same bytes are read as the transpose and
// `mul(matrix, vector)` matches `vector * matrix` in C#. Do not transpose twice. This
// is the same convention the terrain and Metal shaders rely on.

struct VertexInput
{
    float3 position : TEXCOORD0;
    float3 normal : TEXCOORD1;
    float2 texCoord : TEXCOORD2;
};

struct VertexOutput
{
    float4 position : SV_Position;
    float3 normal : TEXCOORD0;
    float2 texCoord : TEXCOORD1;
};

cbuffer FrameUniforms : register(b0, space1)
{
    column_major float4x4 viewProjection;
};

cbuffer ModelUniforms : register(b1, space1)
{
    column_major float4x4 model;
};

Texture2D albedoTexture : register(t0, space2);
SamplerState albedoSampler : register(s0, space2);

VertexOutput VSMain(VertexInput input)
{
    VertexOutput output;
    float4 world = mul(model, float4(input.position, 1.0));
    output.position = mul(viewProjection, world);
    output.normal = mul((float3x3)model, input.normal);
    output.texCoord = input.texCoord;
    return output;
}

float4 PSMain(VertexOutput input) : SV_Target
{
    float4 albedo = albedoTexture.Sample(albedoSampler, input.texCoord);

    // OMSI scenery uses one-bit transparency for foliage, railings, and windows.
    // This build applies a single 0.5 alpha test to every scenery material; the
    // per-material thresholds OMSI stores are not interpreted yet.
    if (albedo.a < 0.5)
    {
        discard;
    }

    // One fixed sun shared with the terrain shader. This is a diagnostic shading term
    // that makes shape readable; it is not OMSI's lighting model.
    float3 toLight = normalize(float3(-0.45, 0.80, 0.35));
    float diffuse = saturate(dot(normalize(input.normal), toLight));
    float3 shaded = albedo.rgb * (0.45 + (0.55 * diffuse));
    return float4(shaded, 1.0);
}

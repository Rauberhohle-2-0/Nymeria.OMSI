// Terrain shaders for SDL3's Direct3D 12 backend.
//
// A Windows build compiles this file to DXIL with the DirectX Shader Compiler and
// embeds the result as Nymeria.OMSI.Shaders.Terrain.vert.dxil and
// ...frag.dxil; see Shaders/DxilShaders.targets. The macOS build embeds
// Shaders/Terrain.metal instead and runs no shader compiler. The two files must stay
// equivalent: the same three-float3 vertex layout, the same one uniform matrix, and
// the same shading function.
//
// SDL3's Direct3D 12 conventions, from the pinned SDL3 SDL_CreateGPUShader
// documentation:
//
//   * Non-system-value vertex semantics are TEXCOORD, indexed by the
//     SDL_GPUVertexAttribute location. Locations 0, 1, and 2 are TEXCOORD0,
//     TEXCOORD1, and TEXCOORD2.
//   * Vertex uniform buffers are register(b[n], space1), in SDL push-slot order.
//     This shader pushes one vertex uniform block at slot 0, so it is b0.
//   * A pixel shader's uniform buffers are register(b[n], space3); this one declares
//     none.
//
// System.Numerics stores a matrix row-major and transforms row vectors, so the 64
// bytes pushed for viewProjection are a row-major matrix. The cbuffer matrix is
// declared column_major explicitly, so the same bytes are read as the transpose and
// `mul(viewProjection, position)` matches `position * viewProjection` in C#. Do not
// transpose twice. This is the same convention the Metal shader relies on.

struct VertexInput
{
    float3 position : TEXCOORD0;
    float3 normal : TEXCOORD1;
    float3 color : TEXCOORD2;
};

struct VertexOutput
{
    float4 position : SV_Position;
    float3 normal : TEXCOORD0;
    float3 color : TEXCOORD1;
};

cbuffer Uniforms : register(b0, space1)
{
    column_major float4x4 viewProjection;
};

VertexOutput VSMain(VertexInput input)
{
    VertexOutput output;
    output.position = mul(viewProjection, float4(input.position, 1.0));
    output.normal = input.normal;
    output.color = input.color;
    return output;
}

float4 PSMain(VertexOutput input) : SV_Target
{
    // One fixed sun above and behind the initial view. This is a diagnostic shading
    // term that makes relief readable; it is not OMSI's lighting model, which needs
    // terrain holes, light maps, and weather.
    float3 toLight = normalize(float3(-0.45, 0.80, 0.35));
    float diffuse = saturate(dot(normalize(input.normal), toLight));
    float3 shaded = input.color * (0.35 + (0.65 * diffuse));
    return float4(shaded, 1.0);
}

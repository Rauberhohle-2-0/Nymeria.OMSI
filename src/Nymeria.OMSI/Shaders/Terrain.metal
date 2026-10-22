// Terrain shaders for SDL3's Metal backend.
//
// This file is embedded as Nymeria.OMSI.Shaders.Terrain.metal and handed to SDL as
// Metal Shading Language source, which SDL compiles when the shaders are created.
// It must stay equivalent to Shaders/Terrain.hlsl: the same three-float3 vertex
// layout, the same one uniform matrix, and the same shading function.
//
// SDL's Metal backend binds the engine camera's view-projection matrix, pushed with
// SDL_PushGPUVertexUniformData at slot 0, as [[buffer(0)]]. System.Numerics stores the
// matrix row-major and transforms row vectors, while MSL stores float4x4 column-major
// and transforms column vectors. Uploading the row-major bytes unchanged is correct:
// MSL reads them as the transpose, so `viewProjection * position` matches
// `position * viewProjection` in C#. Do not transpose twice.

#include <metal_stdlib>
using namespace metal;

struct VertexIn {
    float3 position [[attribute(0)]];
    float3 normal [[attribute(1)]];
    float3 color [[attribute(2)]];
};

struct VertexOut {
    float4 position [[position]];
    float3 normal;
    float3 color;
};

struct Uniforms {
    float4x4 viewProjection;
};

vertex VertexOut vertex_main(VertexIn in [[stage_in]],
                             constant Uniforms &uniforms [[buffer(0)]]) {
    VertexOut out;
    out.position = uniforms.viewProjection * float4(in.position, 1.0);
    out.normal = in.normal;
    out.color = in.color;
    return out;
}

// One fixed sun above and behind the initial view. This is a diagnostic shading term
// that makes relief readable; it is not OMSI's lighting model.
fragment float4 fragment_main(VertexOut in [[stage_in]]) {
    float3 toLight = normalize(float3(-0.45, 0.80, 0.35));
    float diffuse = saturate(dot(normalize(in.normal), toLight));
    float3 shaded = in.color * (0.35 + (0.65 * diffuse));
    return float4(shaded, 1.0);
}

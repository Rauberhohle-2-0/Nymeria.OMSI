// Scenery shaders for SDL3's Metal backend.
//
// This file is embedded as Nymeria.OMSI.Shaders.Scenery.metal and handed to SDL as
// Metal Shading Language source, which SDL compiles when the shaders are created.
// It must stay equivalent to Shaders/Scenery.hlsl: the same position/normal/uv vertex
// layout, the same two uniform matrices, the same alpha-tested albedo sample, and the
// same shading function.
//
// SDL's Metal backend binds the vertex uniform buffers pushed with
// SDL_PushGPUVertexUniformData at slots 0 and 1 as [[buffer(0)]] and [[buffer(1)]],
// the fragment texture at [[texture(0)]], and the fragment sampler at [[sampler(0)]].
// System.Numerics stores the matrices row-major and transforms row vectors, while MSL
// stores float4x4 column-major and transforms column vectors. Uploading the row-major
// bytes unchanged is correct: MSL reads them as the transpose, so
// `matrix * vector` matches `vector * matrix` in C#. Do not transpose twice.

#include <metal_stdlib>
using namespace metal;

struct VertexIn {
    float3 position [[attribute(0)]];
    float3 normal [[attribute(1)]];
    float2 texCoord [[attribute(2)]];
};

struct VertexOut {
    float4 position [[position]];
    float3 normal;
    float2 texCoord;
};

struct FrameUniforms {
    float4x4 viewProjection;
};

struct ModelUniforms {
    float4x4 model;
};

vertex VertexOut vertex_main(VertexIn in [[stage_in]],
                             constant FrameUniforms &frame [[buffer(0)]],
                             constant ModelUniforms &object [[buffer(1)]]) {
    VertexOut out;
    float4 world = object.model * float4(in.position, 1.0);
    out.position = frame.viewProjection * world;
    out.normal = float3x3(object.model[0].xyz, object.model[1].xyz, object.model[2].xyz) * in.normal;
    out.texCoord = in.texCoord;
    return out;
}

// One-bit transparency for foliage, railings, and windows. This build applies a
// single 0.5 alpha test to every scenery material; the per-material thresholds OMSI
// stores are not interpreted yet.
fragment float4 fragment_main(VertexOut in [[stage_in]],
                              texture2d<float> albedo [[texture(0)]],
                              sampler albedoSampler [[sampler(0)]]) {
    float4 colour = albedo.sample(albedoSampler, in.texCoord);

    if (colour.a < 0.5) {
        discard_fragment();
    }

    // One fixed sun shared with the terrain shader. This is a diagnostic shading term
    // that makes shape readable; it is not OMSI's lighting model.
    float3 toLight = normalize(float3(-0.45, 0.80, 0.35));
    float diffuse = saturate(dot(normalize(in.normal), toLight));
    float3 shaded = colour.rgb * (0.45 + (0.55 * diffuse));
    return float4(shaded, 1.0);
}

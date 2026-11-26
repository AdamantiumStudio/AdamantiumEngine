// Clustered forward shading. AssignLights lists, per cluster, the point and spot lights that reach it; the lit passes
// shade a pixel with its own cluster's list.

#include "Includes/ForwardPlusShading.fxh"

float SliceDepth(uint slice)
{
    return exp(slice / ClusterDepth.z + ClusterDepth.w);
}

[shader("compute")]
[numthreads(64, 1, 1)]
void AssignLightsCS(uint3 tid : SV_DispatchThreadID)
{
    uint cluster = tid.x;
    if (cluster >= ClusterCount)
    {
        return;
    }

    uint x = cluster % ClusterX;
    uint y = (cluster / ClusterX) % ClusterY;
    uint z = cluster / (ClusterX * ClusterY);

    // A flat view also sees behind the camera: its first slice reaches back as far as the last reaches ahead.
    float zNear = z == 0 && ClusterW.y > 0.0f ? -ClusterDepth.y : SliceDepth(z);
    float zFar = SliceDepth(z + 1);
    float wNear = zNear * ClusterW.x + ClusterW.y;
    float wFar = zFar * ClusterW.x + ClusterW.y;
    float2 a = (float2(x, y) / float2(ClusterX, ClusterY) * 2.0f - 1.0f) * InverseScale;
    float2 b = (float2(x + 1, y + 1) / float2(ClusterX, ClusterY) * 2.0f - 1.0f) * InverseScale;
    float3 boxMin = float3(min(min(a * wNear, a * wFar), min(b * wNear, b * wFar)), zNear);
    float3 boxMax = float3(max(max(a * wNear, a * wFar), max(b * wNear, b * wFar)), zFar);

    uint first = cluster * MaxLightsPerCluster;
    uint count = 0;
    for (uint i = DirectionalCount; i < LightCount; i++)
    {
        float4 sphere = Lights[i].positionRange;
        float3 gap = clamp(sphere.xyz, boxMin, boxMax) - sphere.xyz;
        if (dot(gap, gap) <= sphere.w * sphere.w && count < MaxLightsPerCluster)
        {
            ClusterLights[first + count] = i;
            count++;
        }
    }

    ClusterCounts[cluster] = count;
}

[shader("fragment")]
float4 LitColored_PS(LIT_PIXEL input) : SV_TARGET
{
    float3 lit = Shade(input.albedo.rgb, input.viewPosition, input.viewNormal, input.position.xy);
    return float4(SimpleInFrame(lit, EmissionOf(input)), 1.0f);
}

[shader("fragment")]
float4 LitTextured_PS(LIT_PIXEL input) : SV_TARGET
{
    float4 surface = SurfaceOf(input);
    float3 lit = Shade(surface.rgb, input.viewPosition, input.viewNormal, input.position.xy);
    return float4(SimpleInFrame(lit, EmissionOf(input)), 1.0f);
}

[shader("fragment")]
float4 LitMaskedTextured_PS(LIT_PIXEL input) : SV_TARGET
{
    float4 surface = MaskedSurfaceOf(input);
    float3 lit = Shade(surface.rgb, input.viewPosition, input.viewNormal, input.position.xy);
    return float4(SimpleInFrame(lit, EmissionOf(input)), 1.0f);
}

[shader("fragment")]
WEIGHTED_PIXEL LitWeightedColored_PS(LIT_PIXEL input)
{
    float3 lit = Shade(input.albedo.rgb, input.viewPosition, input.viewNormal, input.position.xy);
    return Weighted(SimpleInFrame(lit, EmissionOf(input)), input.albedo.a, input.viewPosition.z);
}

[shader("fragment")]
WEIGHTED_PIXEL LitWeightedTextured_PS(LIT_PIXEL input)
{
    float4 surface = SurfaceOf(input);
    float3 lit = Shade(surface.rgb, input.viewPosition, input.viewNormal, input.position.xy);
    return Weighted(SimpleInFrame(lit, EmissionOf(input)), surface.a * input.albedo.a, input.viewPosition.z);
}

[shader("fragment")]
float4 LitPhysical_PS(LIT_PIXEL input) : SV_TARGET
{
    return float4(PhysicalInFrame(LitPhysical(input, BaseColorOf(input)).rgb), 1.0f);
}

[shader("fragment")]
float4 LitMaskedPhysical_PS(LIT_PIXEL input) : SV_TARGET
{
    float4 base = BaseColorOf(input);
    clip(base.a - input.alphaCutoff);
    return float4(PhysicalInFrame(LitPhysical(input, base).rgb), 1.0f);
}

[shader("fragment")]
WEIGHTED_PIXEL LitWeightedPhysical_PS(LIT_PIXEL input)
{
    float4 lit = LitPhysical(input, BaseColorOf(input));
    return Weighted(PhysicalInFrame(lit.rgb), lit.a, input.viewPosition.z);
}

// --- The sky: behind the scene, and made into the pictures the physical surfaces take their light from.

// From the frame's clip space to the world's turn, for the direction a pixel of the background looks along.
float4x4 SkyFromClip;
// One over the size of the sky picture drawn, and the roughness it mirrors at.
float2 SkyStep;
float SkyRoughness;

struct SKY_PIXEL
{
    float4 position : SV_POSITION;
    float2 clip : TEXCOORD0;
};

// One triangle over the whole frame at the far end of its depth: only where nothing was drawn passes.
[shader("vertex")]
SKY_PIXEL Sky_VS(uint vertexId : SV_VertexID)
{
    float2 corner = float2((vertexId << 1) & 2, vertexId & 2) * 2.0f - 1.0f;
    SKY_PIXEL output;
    output.position = float4(corner, 1.0f, 1.0f);
    output.clip = corner;
    return output;
}

[shader("fragment")]
float4 SkyBackground_PS(SKY_PIXEL input) : SV_TARGET
{
    float4 far = mul(float4(input.clip, 1.0f, 1.0f), SkyFromClip);
    float4 near = mul(float4(input.clip, 0.0f, 1.0f), SkyFromClip);
    float3 direction = normalize(far.xyz / far.w - near.xyz / near.w);
    return float4(PhysicalInFrame(SkyLight(direction, false) * SkyStrength), 1.0f);
}

float2 Hammersley(uint i, uint count)
{
    return float2(i / (float)count, reversebits(i) * 2.3283064365386963e-10f);
}

// A direction given round +Z, turned to stand round the normal.
float3 AroundNormal(float3 local, float3 normal)
{
    float3 up = abs(normal.y) < 0.999f ? float3(0.0f, 1.0f, 0.0f) : float3(1.0f, 0.0f, 0.0f);
    float3 tangent = normalize(cross(up, normal));
    float3 bitangent = cross(normal, tangent);
    return tangent * local.x + bitangent * local.y + normal * local.z;
}

static const uint SkySamples = 256;

// The sky's light round a normal, weighted by how square it falls: what a matt white surface sends back.
[shader("fragment")]
float4 SkyIrradiance_PS(float4 position : SV_POSITION) : SV_TARGET
{
    float3 normal = SkyDirectionOf(position.xy * SkyStep);
    float3 sum = 0.0f;
    for (uint i = 0; i < SkySamples; i++)
    {
        float2 h = Hammersley(i, SkySamples);
        float radius = sqrt(h.y);
        float around = 2.0f * Pi * h.x;
        float3 local = float3(radius * cos(around), radius * sin(around), sqrt(1.0f - h.y));
        sum += SkyLight(AroundNormal(local, normal), true);
    }

    return float4(sum / SkySamples, 1.0f);
}

// The sky as a surface of the roughness mirrors it, looked at straight along its normal: the directions round the
// reflection the GGX distribution favours (Karis, "Real Shading in Unreal Engine 4", 2013).
[shader("fragment")]
float4 SkyReflection_PS(float4 position : SV_POSITION) : SV_TARGET
{
    float3 direction = SkyDirectionOf(position.xy * SkyStep);
    if (SkyRoughness <= 0.0f)
    {
        return float4(SkyLight(direction, false), 1.0f);
    }

    float a = SkyRoughness * SkyRoughness;
    float3 sum = 0.0f;
    float weight = 0.0f;
    for (uint i = 0; i < SkySamples; i++)
    {
        float2 h = Hammersley(i, SkySamples);
        float around = 2.0f * Pi * h.x;
        float cosTheta = sqrt((1.0f - h.y) / (1.0f + (a * a - 1.0f) * h.y));
        float sinTheta = sqrt(1.0f - cosTheta * cosTheta);
        float3 between = AroundNormal(float3(sinTheta * cos(around), sinTheta * sin(around), cosTheta), direction);
        float3 toLight = 2.0f * dot(direction, between) * between - direction;
        float nl = dot(direction, toLight);
        if (nl > 0.0f)
        {
            sum += SkyLight(toLight, true) * nl;
            weight += nl;
        }
    }

    return float4(sum / max(weight, 1e-4f), 1.0f);
}

// A surface's depth from the light, into the shadow map: the lit vertex shaders seen through a cascade's view, no color.
[shader("fragment")]
void ShadowDepth_PS()
{
}

// The see-through layers' textures in meshTextures, resolved: the weighted sums and the coverage.
uint AccumulatedTexture;
uint CoverageTexture;

// One triangle over the whole target.
[shader("vertex")]
float4 Fullscreen_VS(uint vertexId : SV_VertexID) : SV_POSITION
{
    float2 corner = float2((vertexId << 1) & 2, vertexId & 2);
    return float4(corner * 2.0f - 1.0f, 0.0f, 1.0f);
}

// The see-through layers' average color, weighted, laid over the scene as much as they cover it.
[shader("fragment")]
float4 WeightedComposite_PS(float4 position : SV_POSITION) : SV_TARGET
{
    int3 pixel = int3(position.xy, 0);
    float coverage = meshTextures[NonUniformResourceIndex(CoverageTexture)].Load(pixel).r;
    if (coverage <= 0.0f)
    {
        discard;
    }

    float4 accumulated = meshTextures[NonUniformResourceIndex(AccumulatedTexture)].Load(pixel);
    return float4(accumulated.rgb / max(accumulated.a, 1e-5f), saturate(coverage));
}

// --- The linear frame brought to the screen: its glow, a chain of ever smaller copies blurred and added back up (Jimenez,
// "Next Generation Post Processing in Call of Duty: Advanced Warfare", 2014), and the AgX tone curve.

// The texture a glow step reads, in meshTextures.
uint GlowSource;
// xy: one over the size of the target drawn, zw: one over the size of the source read.
float4 GlowStep;

float3 GlowAt(float2 uv)
{
    float2 inside = clamp(uv, GlowStep.zw * 0.5f, 1.0f - GlowStep.zw * 0.5f);
    // A single bright pixel would flicker as it moves across the smaller copies: its light is held to a ceiling.
    return min(meshTextures[NonUniformResourceIndex(GlowSource)].Sample(sampleType, inside).rgb, 64.0f);
}

// A copy half the size, from 13 taps of the one before it, weighted so no pattern of the bigger one shows through.
[shader("fragment")]
float4 GlowDown_PS(float4 position : SV_POSITION) : SV_TARGET
{
    float2 uv = position.xy * GlowStep.xy;
    float2 t = GlowStep.zw;
    float3 a = GlowAt(uv + t * float2(-2.0f, -2.0f));
    float3 b = GlowAt(uv + t * float2(0.0f, -2.0f));
    float3 c = GlowAt(uv + t * float2(2.0f, -2.0f));
    float3 d = GlowAt(uv + t * float2(-2.0f, 0.0f));
    float3 e = GlowAt(uv);
    float3 f = GlowAt(uv + t * float2(2.0f, 0.0f));
    float3 g = GlowAt(uv + t * float2(-2.0f, 2.0f));
    float3 h = GlowAt(uv + t * float2(0.0f, 2.0f));
    float3 i = GlowAt(uv + t * float2(2.0f, 2.0f));
    float3 j = GlowAt(uv + t * float2(-1.0f, -1.0f));
    float3 k = GlowAt(uv + t * float2(1.0f, -1.0f));
    float3 l = GlowAt(uv + t * float2(-1.0f, 1.0f));
    float3 m = GlowAt(uv + t * float2(1.0f, 1.0f));
    float3 down = e * 0.125f + (a + c + g + i) * 0.03125f + (b + d + f + h) * 0.0625f + (j + k + l + m) * 0.125f;
    return float4(down, 1.0f);
}

// The smaller copy, blurred by a 3 x 3 tent, added to the one twice its size.
[shader("fragment")]
float4 GlowUp_PS(float4 position : SV_POSITION) : SV_TARGET
{
    float2 uv = position.xy * GlowStep.xy;
    float2 t = GlowStep.zw;
    float3 corners = GlowAt(uv + t * float2(-1.0f, -1.0f)) + GlowAt(uv + t * float2(1.0f, -1.0f))
                     + GlowAt(uv + t * float2(-1.0f, 1.0f)) + GlowAt(uv + t * float2(1.0f, 1.0f));
    float3 sides = GlowAt(uv + t * float2(0.0f, -1.0f)) + GlowAt(uv + t * float2(-1.0f, 0.0f))
                   + GlowAt(uv + t * float2(1.0f, 0.0f)) + GlowAt(uv + t * float2(0.0f, 1.0f));
    return float4((GlowAt(uv) * 4.0f + sides * 2.0f + corners) / 16.0f, 0.0f);
}

// The linear frame in meshTextures, resolved, and its glow - NoMap for none.
uint SceneLightTexture;
uint GlowTexture;
// x: what the light is multiplied by before the curve, y: the share of the glow, z: what the glow is divided by - the
// copies it sums - w unused.
float4 ToneMapping;
// One over the size of the frame.
float2 FrameStep;

// AgX (Troy Sobotka), as fitted for real time by Benjamin Wrensch: the light into a log space a little narrower than the
// sRGB gamut, a sigmoid over it, and back - bright colors go to white as film does, not to a flat hue.
float3 AgxContrast(float3 x)
{
    float3 x2 = x * x;
    float3 x4 = x2 * x2;
    return 15.5f * x4 * x2 - 40.14f * x4 * x + 31.96f * x4 - 6.868f * x2 * x + 0.4298f * x2 + 0.1191f * x - 0.00232f;
}

float3 Agx(float3 light)
{
    const float3x3 inset = float3x3(
        0.842479062253094f, 0.0423282422610123f, 0.0423756549057051f,
        0.0784335999999992f, 0.878468636469772f, 0.0784336f,
        0.0792237451477643f, 0.0791661274605434f, 0.879142973793104f);
    const float3x3 outset = float3x3(
        1.19687900512017f, -0.0528968517574562f, -0.0529716355144438f,
        -0.0980208811401368f, 1.15190312990417f, -0.0980434501171241f,
        -0.0990297440797205f, -0.0989611768448433f, 1.15107367264116f);
    const float lowest = -12.47393f;
    const float highest = 4.026069f;
    float3 c = mul(max(light, 0.0f), inset);
    c = clamp(log2(max(c, 1e-10f)), lowest, highest);
    c = AgxContrast((c - lowest) / (highest - lowest));
    return pow(max(mul(c, outset), 0.0f), 2.2f);
}

// The frame as the screen shows it, premultiplied by how much the scene covers: where it drew nothing its glow still
// lies over what is behind.
[shader("fragment")]
float4 ToneMap_PS(float4 position : SV_POSITION) : SV_TARGET
{
    float4 scene = meshTextures[NonUniformResourceIndex(SceneLightTexture)].Load(int3(position.xy, 0));
    float3 light = scene.rgb;
    if (GlowTexture != NoMap)
    {
        float3 glow = meshTextures[NonUniformResourceIndex(GlowTexture)].Sample(sampleType, position.xy * FrameStep).rgb;
        light = lerp(light, glow / ToneMapping.z, ToneMapping.y);
    }

    return float4(Srgb(Agx(light * ToneMapping.x)), saturate(scene.a));
}

technique ForwardPlus
{
    pass AssignLights
    {
        ComputeShader = AssignLightsCS;
    }

    pass Colored
    {
        VertexShader = Lit_VS;
        PixelShader = LitColored_PS;
    }

    pass Textured
    {
        VertexShader = Lit_VS;
        PixelShader = LitTextured_PS;
    }

    pass SkinnedColored
    {
        VertexShader = LitSkinned_VS;
        PixelShader = LitColored_PS;
    }

    pass SkinnedTextured
    {
        VertexShader = LitSkinned_VS;
        PixelShader = LitTextured_PS;
    }

    pass MaskedTextured
    {
        VertexShader = Lit_VS;
        PixelShader = LitMaskedTextured_PS;
    }

    pass SkinnedMaskedTextured
    {
        VertexShader = LitSkinned_VS;
        PixelShader = LitMaskedTextured_PS;
    }

    pass WeightedColored
    {
        VertexShader = Lit_VS;
        PixelShader = LitWeightedColored_PS;
    }

    pass WeightedTextured
    {
        VertexShader = Lit_VS;
        PixelShader = LitWeightedTextured_PS;
    }

    pass SkinnedWeightedColored
    {
        VertexShader = LitSkinned_VS;
        PixelShader = LitWeightedColored_PS;
    }

    pass SkinnedWeightedTextured
    {
        VertexShader = LitSkinned_VS;
        PixelShader = LitWeightedTextured_PS;
    }

    pass Physical
    {
        VertexShader = Lit_VS;
        PixelShader = LitPhysical_PS;
    }

    pass SkinnedPhysical
    {
        VertexShader = LitSkinned_VS;
        PixelShader = LitPhysical_PS;
    }

    pass MaskedPhysical
    {
        VertexShader = Lit_VS;
        PixelShader = LitMaskedPhysical_PS;
    }

    pass SkinnedMaskedPhysical
    {
        VertexShader = LitSkinned_VS;
        PixelShader = LitMaskedPhysical_PS;
    }

    pass WeightedPhysical
    {
        VertexShader = Lit_VS;
        PixelShader = LitWeightedPhysical_PS;
    }

    pass SkinnedWeightedPhysical
    {
        VertexShader = LitSkinned_VS;
        PixelShader = LitWeightedPhysical_PS;
    }

    pass WeightedComposite
    {
        VertexShader = Fullscreen_VS;
        PixelShader = WeightedComposite_PS;
    }

    pass SkyBackground
    {
        VertexShader = Sky_VS;
        PixelShader = SkyBackground_PS;
    }

    pass SkyIrradiance
    {
        VertexShader = Fullscreen_VS;
        PixelShader = SkyIrradiance_PS;
    }

    pass SkyReflection
    {
        VertexShader = Fullscreen_VS;
        PixelShader = SkyReflection_PS;
    }

    pass ShadowDepth
    {
        VertexShader = Lit_VS;
        PixelShader = ShadowDepth_PS;
    }

    pass SkinnedShadowDepth
    {
        VertexShader = LitSkinned_VS;
        PixelShader = ShadowDepth_PS;
    }

    pass GlowDown
    {
        VertexShader = Fullscreen_VS;
        PixelShader = GlowDown_PS;
    }

    pass GlowUp
    {
        VertexShader = Fullscreen_VS;
        PixelShader = GlowUp_PS;
    }

    pass ToneMap
    {
        VertexShader = Fullscreen_VS;
        PixelShader = ToneMap_PS;
    }
}

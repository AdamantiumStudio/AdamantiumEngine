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
    return float4(Shade(input.albedo.rgb, input.viewPosition, input.viewNormal, input.position.xy) + EmissionOf(input), 1.0f);
}

[shader("fragment")]
float4 LitTextured_PS(LIT_PIXEL input) : SV_TARGET
{
    float4 surface = SurfaceOf(input);
    return float4(Shade(surface.rgb, input.viewPosition, input.viewNormal, input.position.xy) + EmissionOf(input), 1.0f);
}

[shader("fragment")]
float4 LitMaskedTextured_PS(LIT_PIXEL input) : SV_TARGET
{
    float4 surface = MaskedSurfaceOf(input);
    return float4(Shade(surface.rgb, input.viewPosition, input.viewNormal, input.position.xy) + EmissionOf(input), 1.0f);
}

[shader("fragment")]
WEIGHTED_PIXEL LitWeightedColored_PS(LIT_PIXEL input)
{
    float3 lit = Shade(input.albedo.rgb, input.viewPosition, input.viewNormal, input.position.xy) + EmissionOf(input);
    return Weighted(lit, input.albedo.a, input.viewPosition.z);
}

[shader("fragment")]
WEIGHTED_PIXEL LitWeightedTextured_PS(LIT_PIXEL input)
{
    float4 surface = SurfaceOf(input);
    float3 lit = Shade(surface.rgb, input.viewPosition, input.viewNormal, input.position.xy) + EmissionOf(input);
    return Weighted(lit, surface.a * input.albedo.a, input.viewPosition.z);
}

[shader("fragment")]
float4 LitPhysical_PS(LIT_PIXEL input) : SV_TARGET
{
    return float4(Srgb(LitPhysical(input, BaseColorOf(input)).rgb), 1.0f);
}

[shader("fragment")]
float4 LitMaskedPhysical_PS(LIT_PIXEL input) : SV_TARGET
{
    float4 base = BaseColorOf(input);
    clip(base.a - input.alphaCutoff);
    return float4(Srgb(LitPhysical(input, base).rgb), 1.0f);
}

[shader("fragment")]
WEIGHTED_PIXEL LitWeightedPhysical_PS(LIT_PIXEL input)
{
    float4 lit = LitPhysical(input, BaseColorOf(input));
    return Weighted(Srgb(lit.rgb), lit.a, input.viewPosition.z);
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
}

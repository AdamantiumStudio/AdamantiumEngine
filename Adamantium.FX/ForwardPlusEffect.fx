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

    ClusterLight* lights = (ClusterLight*)LightsAddress;
    uint* list = (uint*)ClusterLightsAddress;
    uint first = cluster * MaxLightsPerCluster;
    uint count = 0;
    for (uint i = DirectionalCount; i < LightCount; i++)
    {
        float4 sphere = lights[i].positionRange;
        float3 gap = clamp(sphere.xyz, boxMin, boxMax) - sphere.xyz;
        if (dot(gap, gap) <= sphere.w * sphere.w && count < MaxLightsPerCluster)
        {
            list[first + count] = i;
            count++;
        }
    }

    uint* counts = (uint*)ClusterCountsAddress;
    counts[cluster] = count;
}

[shader("fragment")]
float4 LitColored_PS(LIT_PIXEL input) : SV_TARGET
{
    return float4(Shade(input.albedo, input.viewPosition, input.viewNormal, input.position.xy), 1.0f);
}

[shader("fragment")]
float4 LitTextured_PS(LIT_PIXEL input) : SV_TARGET
{
    float4 surface = SurfaceOf(input);
    return float4(Shade(surface.rgb, input.viewPosition, input.viewNormal, input.position.xy), surface.a);
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
}

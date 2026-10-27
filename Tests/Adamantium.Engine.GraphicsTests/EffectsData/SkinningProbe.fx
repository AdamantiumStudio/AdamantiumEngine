// Bends a point by the joints each thread names, through the effects' own SkinOf, for a CPU readback to check.

#include "Includes/Skinning.fxh"

float4x4* Palette;
uint JointCount;
float4* Indices;   // one float4 of joint indices per thread
float4* Output;    // one float4 per thread
float3 Point;

[shader("compute")]
[numthreads(1, 1, 1)]
void SkinningProbeCS(uint3 tid : SV_DispatchThreadID)
{
    float4x4 skin = SkinOf(Palette, JointCount, Indices[tid.x], float4(1, 0, 0, 0));
    Output[tid.x] = mul(float4(Point, 1.0f), skin);
}

technique SkinningProbe
{
    pass Run
    {
        ComputeShader = SkinningProbeCS;
    }
}

// Bends a point by the joints each thread names, through the effects' own SkinOf, for a CPU readback to check.

#include "Includes/Skinning.fxh"

uint64_t PaletteAddress;
uint JointCount;
uint64_t IndicesAddress;   // one float4 of joint indices per thread
uint64_t OutputAddress;    // one float4 per thread
float3 Point;

[shader("compute")]
[numthreads(1, 1, 1)]
void SkinningProbeCS(uint3 tid : SV_DispatchThreadID)
{
    float4 indices = ((float4*)IndicesAddress)[tid.x];
    float4x4 skin = SkinOf((float4x4*)PaletteAddress, JointCount, indices, float4(1, 0, 0, 0));
    ((float4*)OutputAddress)[tid.x] = mul(float4(Point, 1.0f), skin);
}

technique SkinningProbe
{
    pass Run
    {
        ComputeShader = SkinningProbeCS;
    }
}

// De-risk the second half of one-pass variable-output GPU geometry: a compute shader writes the draw arguments
// (a VkDrawIndirectCommand: vertexCount, instanceCount, firstVertex, firstInstance) AND the vertices into BDA buffers,
// then a DrawIndirect reads the GPU-produced count and rasterizes them. Combined with AtomicAppend, this is the whole
// "GPU decides how much to draw, in one dispatch" pipeline. Shader body is Slang.

uint64_t IndirectAddress;   // VkDrawIndirectCommand = 4 uints
uint64_t OutputAddress;     // float2[] vertices

[shader("compute")]
[numthreads(1, 1, 1)]
void IndirectDrawCS(uint3 tid : SV_DispatchThreadID)
{
    uint* cmd = (uint*)IndirectAddress;
    cmd[0] = 6u;   // vertexCount: two triangles
    cmd[1] = 1u;   // instanceCount
    cmd[2] = 0u;   // firstVertex
    cmd[3] = 0u;   // firstInstance

    float2* v = (float2*)OutputAddress;
    v[0] = float2(10.0, 10.0);
    v[1] = float2(50.0, 10.0);
    v[2] = float2(10.0, 50.0);
    v[3] = float2(50.0, 10.0);
    v[4] = float2(50.0, 50.0);
    v[5] = float2(10.0, 50.0);
}

technique IndirectDraw
{
    pass Run
    {
        ComputeShader = IndirectDrawCS;
    }
}

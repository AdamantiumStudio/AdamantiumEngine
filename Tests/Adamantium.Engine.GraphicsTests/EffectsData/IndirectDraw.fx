// De-risk the second half of one-pass variable-output GPU geometry: a compute shader writes the draw arguments
// (a VkDrawIndirectCommand: vertexCount, instanceCount, firstVertex, firstInstance) AND the vertices into BDA buffers,
// then a DrawIndirect reads the GPU-produced count and rasterizes them. Combined with AtomicAppend, this is the whole
// "GPU decides how much to draw, in one dispatch" pipeline. Shader body is Slang.

uint* Indirect;     // VkDrawIndirectCommand = 4 uints
float2* Output;     // vertices

[shader("compute")]
[numthreads(1, 1, 1)]
void IndirectDrawCS(uint3 tid : SV_DispatchThreadID)
{
    Indirect[0] = 6u;   // vertexCount: two triangles
    Indirect[1] = 1u;   // instanceCount
    Indirect[2] = 0u;   // firstVertex
    Indirect[3] = 0u;   // firstInstance

    Output[0] = float2(10.0, 10.0);
    Output[1] = float2(50.0, 10.0);
    Output[2] = float2(10.0, 50.0);
    Output[3] = float2(50.0, 10.0);
    Output[4] = float2(50.0, 50.0);
    Output[5] = float2(10.0, 50.0);
}

technique IndirectDraw
{
    pass Run
    {
        ComputeShader = IndirectDrawCS;
    }
}

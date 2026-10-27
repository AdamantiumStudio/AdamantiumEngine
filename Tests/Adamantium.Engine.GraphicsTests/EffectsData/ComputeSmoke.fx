// Compute smoke test: a shader object writes index+1 through a BDA pointer for a CPU readback to verify. The shader body
// is Slang; the technique block uses the engine's FX syntax.

uint* Output;   // GetDeviceAddress() of the output buffer
uint Count;     // number of uints to write

[shader("compute")]
[numthreads(64, 1, 1)]
void ComputeSmokeCS(uint3 tid : SV_DispatchThreadID)
{
    if (tid.x >= Count)
        return;

    Output[tid.x] = tid.x + 1;   // known pattern -> readback expects output[i] == i + 1
}

technique ComputeSmoke
{
    pass Run
    {
        ComputeShader = ComputeSmokeCS;
    }
}

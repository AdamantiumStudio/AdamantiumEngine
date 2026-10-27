// De-risk for one-pass variable-output GPU geometry (dashes / round joins / caps): proves that a compute shader can
// reserve output slots with an atomic counter via a BDA pointer (InterlockedAdd on *(uint*)CounterAddress) and scatter
// to an output buffer. If this works, the expander can emit a variable number of triangles in a single dispatch and a
// DrawIndirect can read the count. Shader body is Slang.

uint64_t CounterAddress;   // single uint, the running append count (must be zero-initialized before dispatch)
uint64_t OutputAddress;    // uint[] output (>= Count elements)
uint Count;

[shader("compute")]
[numthreads(64, 1, 1)]
void AtomicAppendCS(uint3 tid : SV_DispatchThreadID)
{
    if (tid.x >= Count)
        return;

    uint* counter = (uint*)CounterAddress;
    uint* outVals = (uint*)OutputAddress;

    uint slot;
    InterlockedAdd(counter[0], 1u, slot);   // atomically reserve one slot, get the previous count
    outVals[slot] = tid.x + 1u;
}

technique AtomicAppend
{
    pass Run
    {
        ComputeShader = AtomicAppendCS;
    }
}

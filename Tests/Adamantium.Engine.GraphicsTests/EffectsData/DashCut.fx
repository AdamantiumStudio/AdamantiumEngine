// One-pass GPU cutting: a single thread walks the contour by arc length, applies trim and dashes, and emits each visible
// piece as a quad into a triangle list, writing the vertex count into a VkDrawIndirectCommand. Deterministic for readback.

float2* Points;     // contour points
float* Pattern;     // dash pattern (on,off,on,off,...); PatternCount entries
float2* Output;     // output vertices
uint* Indirect;     // VkDrawIndirectCommand (4 uints)
uint PointCount;
uint IsClosed;
uint PatternCount;          // 0 = no dashes (one continuous piece over the trim range)
float DashOffset;
float HalfThickness;
float TrimStart;            // 0..1 fraction of total arc length
float TrimEnd;              // 0..1

[shader("compute")]
[numthreads(1, 1, 1)]
void DashCutCS(uint3 tid : SV_DispatchThreadID)
{
    if (tid.x != 0u) return;

    uint segCount = IsClosed != 0u ? PointCount : PointCount - 1u;

    // Total length + trim window.
    float total = 0.0;
    for (uint s = 0u; s < segCount; ++s)
        total += length(Points[(s + 1u) % PointCount] - Points[s]);
    float tStart = TrimStart * total;
    float tEnd = TrimEnd * total;

    // Dash pattern state: phase advanced by DashOffset; 'on' starts on an even (dash) entry.
    float patTotal = 0.0;
    for (uint k = 0u; k < PatternCount; ++k) patTotal += Pattern[k];

    uint pi = 0u;
    float rem = PatternCount > 0u ? Pattern[0] : 1e30;
    bool on = true;
    if (PatternCount > 0u && patTotal > 0.0)
    {
        float off = DashOffset - floor(DashOffset / patTotal) * patTotal;   // mod into [0,patTotal)
        while (off > 1e-6)
        {
            float take = min(off, rem);
            off -= take; rem -= take;
            if (rem <= 1e-6) { pi = (pi + 1u) % PatternCount; rem = Pattern[pi]; }
        }
        on = (pi % 2u) == 0u;
    }

    uint vCount = 0u;
    float arc = 0.0;   // arc length at the start of the current segment

    for (uint s = 0u; s < segCount; ++s)
    {
        float2 a = Points[s];
        float2 b = Points[(s + 1u) % PointCount];
        float2 d = b - a;
        float segLen = length(d);
        if (segLen < 1e-6) continue;
        float2 dir = d / segLen;

        float pos = 0.0;
        while (pos < segLen - 1e-6)
        {
            float take = PatternCount > 0u ? min(rem, segLen - pos) : (segLen - pos);

            // Intersect this [pos, pos+take] sub-span with the trim window, in absolute arc length.
            float spanA = arc + pos;
            float spanB = arc + pos + take;
            float drawA = max(spanA, tStart);
            float drawB = min(spanB, tEnd);
            if (on && drawB > drawA + 1e-6)
            {
                float2 p0 = a + (drawA - arc) * dir;
                float2 p1 = a + (drawB - arc) * dir;
                float2 nrm = float2(-dir.y, dir.x) * HalfThickness;
                uint o = vCount;
                Output[o + 0] = p0 + nrm;
                Output[o + 1] = p0 - nrm;
                Output[o + 2] = p1 + nrm;
                Output[o + 3] = p1 + nrm;
                Output[o + 4] = p0 - nrm;
                Output[o + 5] = p1 - nrm;
                vCount += 6u;
            }

            pos += take;
            if (PatternCount > 0u)
            {
                rem -= take;
                if (rem <= 1e-6) { pi = (pi + 1u) % PatternCount; rem = Pattern[pi]; on = !on; }
            }
        }
        arc += segLen;
    }

    Indirect[0] = vCount;   // vertexCount
    Indirect[1] = 1u;       // instanceCount
    Indirect[2] = 0u;       // firstVertex
    Indirect[3] = 0u;       // firstInstance
}

technique DashCut
{
    pass Run
    {
        ComputeShader = DashCutCS;
    }
}

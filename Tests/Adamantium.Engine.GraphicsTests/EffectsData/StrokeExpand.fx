// GPU stroke expander with miter joins: one thread per point emits two vertices along the miter normal, drawn as a
// triangle strip [p0+, p0-, p1+, p1-, ...]; the miter length is clamped at sharp corners.

uint64_t PointsAddress;   // float2[] polyline points (PointCount of them)
uint64_t OutputAddress;   // float2[] output vertices (PointCount * 2, triangle strip)
uint PointCount;
float HalfThickness;

[shader("compute")]
[numthreads(64, 1, 1)]
void StrokeExpandCS(uint3 tid : SV_DispatchThreadID)
{
    if (tid.x >= PointCount)
        return;

    float2* points = (float2*)PointsAddress;
    float2* outVerts = (float2*)OutputAddress;
    uint i = tid.x;
    float2 p = points[i];

    float2 miter;
    float miterLen;

    if (i == 0)
    {
        float2 d = normalize(points[1] - points[0]);
        miter = float2(-d.y, d.x);
        miterLen = HalfThickness;
    }
    else if (i + 1 == PointCount)
    {
        float2 d = normalize(points[i] - points[i - 1]);
        miter = float2(-d.y, d.x);
        miterLen = HalfThickness;
    }
    else
    {
        float2 d0 = normalize(p - points[i - 1]);
        float2 d1 = normalize(points[i + 1] - p);
        float2 n0 = float2(-d0.y, d0.x);
        float2 n1 = float2(-d1.y, d1.x);
        miter = normalize(n0 + n1);
        float denom = max(dot(miter, n0), 0.25);   // clamp -> miter length capped at 4*half on sharp corners
        miterLen = HalfThickness / denom;
    }

    uint o = i * 2;
    outVerts[o + 0] = p + miter * miterLen;
    outVerts[o + 1] = p - miter * miterLen;
}

technique StrokeExpand
{
    pass Run
    {
        ComputeShader = StrokeExpandCS;
    }
}

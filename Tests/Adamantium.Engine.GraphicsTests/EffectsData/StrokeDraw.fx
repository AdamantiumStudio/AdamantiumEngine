// Minimal draw of the GPU-expanded stroke geometry. The compute stroke-expander
// (StrokeExpand.fx) writes float2 positions into a vertex buffer; this just transforms them by an ortho projection
// and fills them with a solid color, so a render + pixel check proves the rasterizer consumes the compute output.
// Shader body is Slang.

float4x4 Projection;
float4 StrokeColor;

struct VSInput { float2 Position : POSITION; };
struct PSInput { float4 Position : SV_Position; };

[shader("vertex")]
PSInput StrokeVS(VSInput input)
{
    PSInput o;
    o.Position = mul(float4(input.Position, 0.0, 1.0), Projection);   // row-vector convention (matches engine effects)
    return o;
}

[shader("fragment")]
float4 StrokePS(PSInput input) : SV_Target
{
    return StrokeColor;
}

technique StrokeDraw
{
    pass Run
    {
        VertexShader = StrokeVS;
        PixelShader = StrokePS;
    }
}

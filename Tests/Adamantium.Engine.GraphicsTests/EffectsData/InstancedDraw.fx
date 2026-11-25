// A mesh's corner from one vertex buffer, what differs between its instances from another.

struct VSInput
{
    float2 Position : POSITION;
    float2 Offset : TEXCOORD0;
    float4 Color : COLOR0;
};

struct PSInput
{
    float4 Position : SV_Position;
    float4 Color : COLOR0;
};

[shader("vertex")]
PSInput InstancedVS(VSInput input)
{
    PSInput o;
    o.Position = float4(input.Position + input.Offset, 0.0, 1.0);
    o.Color = input.Color;
    return o;
}

[shader("fragment")]
float4 InstancedPS(PSInput input) : SV_Target
{
    return input.Color;
}

technique InstancedDraw
{
    pass Run
    {
        VertexShader = InstancedVS;
        PixelShader = InstancedPS;
    }
}

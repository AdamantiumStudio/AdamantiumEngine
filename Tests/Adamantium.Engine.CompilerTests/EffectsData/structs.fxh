struct TexturedVertexInputType
{
    float4 position : POSITION;
    float4 color: COLOR;
    float2 texcoord: TEXCOORD;
};

struct TexturedPixelInputType
{
    float4 position : SV_POSITION;
    float2 texcoord: TEXCOORD;
    float4 color: COLOR;
};

struct MESH_VERTEX
{
    float4 position : POSITION;
    float4 color : COLOR;
    float3 normal : NORMAL;
    float2 uv0 : TEXCOORD0;
    float2 uv1 : TEXCOORD1;
    float2 uv2 : TEXCOORD2;
    float2 uv3 : TEXCOORD3;
    float4 tan : TANGENT;
    float3 biTangent : BINORMAL0;
};

struct PS_OUTPUT_BASIC
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
    float4 color : COLOR0;
};
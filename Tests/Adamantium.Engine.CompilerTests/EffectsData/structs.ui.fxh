struct PixelInputType
{
	float4 position : SV_POSITION;
	float4 color: COLOR;
};

struct VertexInputType
{
	float4 position : POSITION;
};

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
};
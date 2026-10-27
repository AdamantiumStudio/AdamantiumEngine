#include "structs.ui.fxh"

matrix wvp;

float4 fillColor;
SamplerState sampleType;
Texture2D shaderTexture;

float zNear;
float zFar;
float transparency = 1;

////////////////////////////////////////////////////////////////////////////////
// Vertex Shader
////////////////////////////////////////////////////////////////////////////////
PixelInputType LightVertexShader(TexturedVertexInputType input)
{
	PixelInputType output;
	output.color = input.color;
	output.position = float4(input.position.xyz, 1);

	return output;
}

TexturedPixelInputType TexturedVertexShader(TexturedVertexInputType input)
{
//	TexturedPixelInputType output;
//	output.position = float4(0, 0, 0, 0);
//
//
//	// Change the position vector to be 4 units for proper matrix calculations.
//	//input.position.w = 1.0f;
//
//	// Calculate the position of the vertex against the world, view, and projection matrices.
//
//	output.position = mul(input.position, wvp);
//
//	output.texcoord = input.texcoord;
//	return output;

    TexturedPixelInputType output;
	output.texcoord = input.texcoord;
	output.position = float4(input.position.xyz, 1);

	return output;
}

float4 LightPixelShader(PixelInputType input) : SV_TARGET
{
	float4 result = fillColor;
   //input.color.a = transparency;
   return result;
}

float4 SolidColorPixelShader(TexturedPixelInputType input) : SV_TARGET
{
   float4 result = fillColor;
   return result;
}

float4 TexturedPixelShader(TexturedPixelInputType input) : SV_TARGET
{
   float4 color = shaderTexture.Sample(sampleType, input.texcoord) * fillColor;
   return color;
	/*float4 color = float4(input.texcoord, 0, 1);
   return color;*/
}


technique Render
{
	pass Debug
	{
		VertexShader = LightVertexShader;
		PixelShader = LightPixelShader;
	}

	pass SolidColor
	{
		VertexShader = TexturedVertexShader;
		PixelShader = SolidColorPixelShader;
	}

	pass Textured
	{
		VertexShader = TexturedVertexShader;
		PixelShader = TexturedPixelShader;
	}
}
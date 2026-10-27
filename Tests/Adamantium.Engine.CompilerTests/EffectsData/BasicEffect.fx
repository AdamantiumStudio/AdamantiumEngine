float4x4 wvp;
float3 meshColor;
float transparency;
SamplerState sampleType;
Texture2D shaderTexture;
float4 foregroundColor;
float gamma;
float4 backgroundColor;
uint atlasSize;

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


TexturedPixelInputType TexturedVertexShader(TexturedVertexInputType input)
{
    TexturedPixelInputType output;
    // Change the position vector to be 4 units for proper matrix calculations.
    output.position = float4(input.position.xyz, 1);
    // Calculate the position of the vertex against the world, view, and projection matrices.
	output.position = mul(output.position, wvp);
	output.texcoord = input.texcoord;
	output.color = input.color;

	return output;
}

float4 TexturedPixelShader(TexturedPixelInputType input) : SV_TARGET
{
   float4 color = shaderTexture.Sample(sampleType, input.texcoord);
   return color;
}


PS_OUTPUT_BASIC Basic_VS(MESH_VERTEX input)
{
    PS_OUTPUT_BASIC output;
   
    input.position.w = 1.0f;
    output.position = mul(input.position, wvp);
    output.uv = input.uv0;
    output.color = input.color;
    return output;
}

float4 BasicColored_PS(PS_OUTPUT_BASIC input) : SV_TARGET
{
    float4 color = float4(meshColor, 1);
    color.a = transparency;
    return color;
}

float4 BasicVertexColored_PS(PS_OUTPUT_BASIC input) : SV_TARGET
{
    return input.color;
}

float median(float a, float b, float c)
{
    return max(min(a,b), min(max(a,b), c));
}

float4 SmallGlyph_PS(PS_OUTPUT_BASIC input) : SV_TARGET
{
    float dist = shaderTexture.Sample(sampleType, input.uv).a;

    float blendedAlpha = dist * foregroundColor.a;

    float4 color = float4(foregroundColor.r, foregroundColor.g, foregroundColor.b, blendedAlpha);
    
    return color;
}

float4 LargeGlyph_PS(PS_OUTPUT_BASIC input) : SV_TARGET
{
    float4 sample = shaderTexture.Sample(sampleType, input.uv).rgba;
    int2 sz;
    shaderTexture.GetDimensions(sz.x, sz.y);
    float dx = ddx( input.uv.x ) * sz.x;
    float dy = ddy( input.uv.y ) * sz.y;
    float toPixels = 5.0 * rsqrt( dx * dx + dy * dy );
    float sigDist = median( sample.r, sample.g, sample.b ) - 0.5;
    float opacity = clamp( sigDist * toPixels + 0.5, 0.0, 1.0 );

    //float4 color = float4(foregroundColor.r, foregroundColor.g, foregroundColor.b, opacity);

    float4 color;
    if (sample.a >= 0.55)
    {
        color = foregroundColor;
    }
    else
    {
        color = float4(foregroundColor.r, foregroundColor.g, foregroundColor.b, opacity);
    }

//float3 sample = shaderTexture.Sample(sampleType, input.uv).rgb;
//    float sigDist = median( sample.r, sample.g, sample.b );
//    
//    float4 color;
//    
//    if (sigDist >= 0.5)
//    {
//        color = foregroundColor;
//    }
//    else
//    {
//        color = float4(0,0,0,0);
//    }
    
    return color;
}

float4 BasicTextured_PS(PS_OUTPUT_BASIC input) : SV_TARGET
{
    float4 color = shaderTexture.Sample(sampleType, input.uv);
    return color;
}

float4 EncodedToBrightness(float4 encoded)
{
    return pow(encoded, gamma);
}
        
float4 BrightnessToEncoded(float4 brightness)
{
    return pow(brightness, 1.0 / gamma);
}

float GetRebalancedSubpixel(float mostLeft, float leastLeft, float current, float leastRight, float mostRight)
{
    return (mostLeft / 9.0) +
           ((leastLeft * 2.0) / 9.0) +
           ((current * 3.0) / 9.0) +
           ((leastRight * 2.0) / 9.0) +
           (mostRight / 9.0);
}

float4 Subpixel_PS(PS_OUTPUT_BASIC input) : SV_TARGET
{
    float pixelStep = 1.0 / atlasSize;
    float2 leftPos = float2(input.uv.x - pixelStep, input.uv.y);
    float2 rightPos = float2(input.uv.x + pixelStep, input.uv.y);

    float4 leftPixel = shaderTexture.Sample(sampleType, leftPos);
    float4 currentPixel = shaderTexture.Sample(sampleType, input.uv);
    float4 rightPixel = shaderTexture.Sample(sampleType, rightPos);

    leftPixel = EncodedToBrightness(leftPixel);
    currentPixel = EncodedToBrightness(currentPixel);
    rightPixel = EncodedToBrightness(rightPixel);    

    float redDist = GetRebalancedSubpixel(leftPixel.g, leftPixel.b, currentPixel.r, currentPixel.g, currentPixel.b);
    float greenDist = GetRebalancedSubpixel(leftPixel.b, currentPixel.r, currentPixel.g, currentPixel.b, rightPixel.r);
    float blueDist = GetRebalancedSubpixel(currentPixel.r, currentPixel.g, currentPixel.b, rightPixel.r, rightPixel.g);

    float4 linearForegroundColor = EncodedToBrightness(foregroundColor);
    float4 linearBackgroundColor = EncodedToBrightness(backgroundColor);    
    
    float blendedRed   = redDist * linearForegroundColor.r + (1.0 - redDist) * linearBackgroundColor.r;
    float blendedGreen = greenDist * linearForegroundColor.g + (1.0 - greenDist) * linearBackgroundColor.g;
    float blendedBlue  = blueDist * linearForegroundColor.b + (1.0 - blueDist) * linearBackgroundColor.b;
    //float blendedAlpha = currentPixel.a * linearForegroundColor.a + (1.0 - currentPixel.a) * linearBackgroundColor.a;
    
    float4 color = BrightnessToEncoded(float4(blendedRed, blendedGreen, blendedBlue, currentPixel.a));
    
    return color;
}

technique Render
{
	pass Textured
	{
		VertexShader = TexturedVertexShader;
		PixelShader = TexturedPixelShader;
	}
}

technique Basic
{
    pass Default
    {
        VertexShader = Basic_VS;
        PixelShader = BasicColored_PS;
    }
    
    pass Textured
    {
        VertexShader = Basic_VS;
        PixelShader = BasicTextured_PS;
    }
        
    pass Colored
    {
        VertexShader = Basic_VS;
        PixelShader = BasicColored_PS;
    }
        
    pass VertexColored
    {
        VertexShader = Basic_VS;
        PixelShader = BasicVertexColored_PS;
    }
    
    pass SmallGlyph
    {
        VertexShader = Basic_VS;
        PixelShader = SmallGlyph_PS;
    }
    
    pass LargeGlyph
    {
        VertexShader = Basic_VS;
        PixelShader = LargeGlyph_PS;
    }
}
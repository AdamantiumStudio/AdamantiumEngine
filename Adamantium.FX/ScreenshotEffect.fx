// A screenshot of a model: its colors or textures in a few directional lights and an even light, nothing more - no
// clusters, no shadows, no instancing, no skinning (a model is pictured in its bind pose).

// A directional light, in the world's axes: where it shines (xyz), and its color times its intensity (rgb).
struct ScreenshotLight
{
    float4 direction;
    float4 color;
};

ScreenshotLight* Lights;
uint LightCount;
float3 Ambient;

// The mesh placed and seen from the camera, and placed alone - rotation and scale, for the normals.
float4x4 wvp;
float4x4 world;
// The surface's color when it has no texture, and its texture in meshTextures when it has.
float3 albedo;
uint textureIndex;

SamplerState sampleType;
[[vk::binding(0, 1)]]
Texture2D meshTextures[];

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

struct SCREENSHOT_PIXEL
{
    float4 position : SV_POSITION;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
};

float3 Lit(float3 surface, float3 normal)
{
    float3 n = dot(normal, normal) > 1e-8f ? normalize(normal) : float3(0, 0, -1);
    float3 lit = surface * Ambient;
    for (uint i = 0; i < LightCount; i++)
    {
        lit += surface * Lights[i].color.rgb * saturate(dot(n, -Lights[i].direction.xyz));
    }

    return lit;
}

[shader("vertex")]
SCREENSHOT_PIXEL Screenshot_VS(MESH_VERTEX input)
{
    SCREENSHOT_PIXEL output;
    output.position = mul(float4(input.position.xyz, 1.0f), wvp);
    output.normal = mul(input.normal, (float3x3)world);
    output.uv = input.uv0;
    return output;
}

[shader("fragment")]
float4 ScreenshotColored_PS(SCREENSHOT_PIXEL input) : SV_TARGET
{
    return float4(Lit(albedo, input.normal), 1.0f);
}

[shader("fragment")]
float4 ScreenshotTextured_PS(SCREENSHOT_PIXEL input) : SV_TARGET
{
    float4 surface = meshTextures[NonUniformResourceIndex(textureIndex)].Sample(sampleType, input.uv);
    return float4(Lit(surface.rgb, input.normal), surface.a);
}

technique Screenshot
{
    pass Colored
    {
        VertexShader = Screenshot_VS;
        PixelShader = ScreenshotColored_PS;
    }

    pass Textured
    {
        VertexShader = Screenshot_VS;
        PixelShader = ScreenshotTextured_PS;
    }
}

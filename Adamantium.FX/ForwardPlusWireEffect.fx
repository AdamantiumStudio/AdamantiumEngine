// Clustered forward shading with the mesh's wireframe drawn over it in the same pass: where a pixel lies in its
// triangle tells how near an edge it is. Needs VK_KHR_fragment_shader_barycentric.

#include "Includes/ForwardPlusShading.fxh"

float3 wireColor;
// The lines' width in pixels.
float wireWidth;

// Each triangle draws its half of a shared edge's line, softened over a pixel.
float3 OverWire(float3 lit, float3 barycentrics)
{
    if (wireWidth <= 0.0f)
    {
        return lit;
    }

    float3 pixel = fwidth(barycentrics);
    float halfWidth = wireWidth * 0.5f;
    float3 inside = smoothstep(pixel * (halfWidth - 0.5f), pixel * (halfWidth + 0.5f), barycentrics);
    return lerp(wireColor, lit, min(min(inside.x, inside.y), inside.z));
}

[shader("fragment")]
float4 LitColoredWire_PS(LIT_PIXEL input, float3 barycentrics : SV_Barycentrics) : SV_TARGET
{
    float3 lit = Shade(input.albedo, input.viewPosition, input.viewNormal, input.position.xy);
    return float4(OverWire(lit, barycentrics), 1.0f);
}

[shader("fragment")]
float4 LitTexturedWire_PS(LIT_PIXEL input, float3 barycentrics : SV_Barycentrics) : SV_TARGET
{
    float4 surface = SurfaceOf(input);
    float3 lit = Shade(surface.rgb, input.viewPosition, input.viewNormal, input.position.xy);
    return float4(OverWire(lit, barycentrics), surface.a);
}

technique ForwardPlusWire
{
    pass Colored
    {
        VertexShader = Lit_VS;
        PixelShader = LitColoredWire_PS;
    }

    pass Textured
    {
        VertexShader = Lit_VS;
        PixelShader = LitTexturedWire_PS;
    }
}

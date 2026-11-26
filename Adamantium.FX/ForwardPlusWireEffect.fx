// Clustered forward shading with the mesh's wireframe drawn over it in the same pass: where a pixel lies in its
// triangle tells how near an edge it is. Needs VK_KHR_fragment_shader_barycentric. Opaque surfaces only: a blended
// one's wire is drawn as lines.

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
    float3 wire = LinearFrame != 0 ? Linear(wireColor) : wireColor;
    return lerp(wire, lit, min(min(inside.x, inside.y), inside.z));
}

[shader("fragment")]
float4 LitColoredWire_PS(LIT_PIXEL input, float3 barycentrics : SV_Barycentrics) : SV_TARGET
{
    float3 lit = Shade(input.albedo.rgb, input.viewPosition, input.viewNormal, input.position.xy);
    return float4(OverWire(SimpleInFrame(lit, EmissionOf(input)), barycentrics), 1.0f);
}

[shader("fragment")]
float4 LitTexturedWire_PS(LIT_PIXEL input, float3 barycentrics : SV_Barycentrics) : SV_TARGET
{
    float4 surface = SurfaceOf(input);
    float3 lit = Shade(surface.rgb, input.viewPosition, input.viewNormal, input.position.xy);
    return float4(OverWire(SimpleInFrame(lit, EmissionOf(input)), barycentrics), 1.0f);
}

[shader("fragment")]
float4 LitMaskedTexturedWire_PS(LIT_PIXEL input, float3 barycentrics : SV_Barycentrics) : SV_TARGET
{
    float4 surface = MaskedSurfaceOf(input);
    float3 lit = Shade(surface.rgb, input.viewPosition, input.viewNormal, input.position.xy);
    return float4(OverWire(SimpleInFrame(lit, EmissionOf(input)), barycentrics), 1.0f);
}

[shader("fragment")]
float4 LitPhysicalWire_PS(LIT_PIXEL input, float3 barycentrics : SV_Barycentrics) : SV_TARGET
{
    return float4(OverWire(PhysicalInFrame(LitPhysical(input, BaseColorOf(input)).rgb), barycentrics), 1.0f);
}

[shader("fragment")]
float4 LitMaskedPhysicalWire_PS(LIT_PIXEL input, float3 barycentrics : SV_Barycentrics) : SV_TARGET
{
    float4 base = BaseColorOf(input);
    clip(base.a - input.alphaCutoff);
    return float4(OverWire(PhysicalInFrame(LitPhysical(input, base).rgb), barycentrics), 1.0f);
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

    pass SkinnedColored
    {
        VertexShader = LitSkinned_VS;
        PixelShader = LitColoredWire_PS;
    }

    pass SkinnedTextured
    {
        VertexShader = LitSkinned_VS;
        PixelShader = LitTexturedWire_PS;
    }

    pass MaskedTextured
    {
        VertexShader = Lit_VS;
        PixelShader = LitMaskedTexturedWire_PS;
    }

    pass SkinnedMaskedTextured
    {
        VertexShader = LitSkinned_VS;
        PixelShader = LitMaskedTexturedWire_PS;
    }

    pass Physical
    {
        VertexShader = Lit_VS;
        PixelShader = LitPhysicalWire_PS;
    }

    pass SkinnedPhysical
    {
        VertexShader = LitSkinned_VS;
        PixelShader = LitPhysicalWire_PS;
    }

    pass MaskedPhysical
    {
        VertexShader = Lit_VS;
        PixelShader = LitMaskedPhysicalWire_PS;
    }

    pass SkinnedMaskedPhysical
    {
        VertexShader = LitSkinned_VS;
        PixelShader = LitMaskedPhysicalWire_PS;
    }
}
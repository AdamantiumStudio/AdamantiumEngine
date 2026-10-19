#include "Includes/Skinning.fxh"

float4x4 wvp;
// Rotation for NORMALS. wvp bakes the projection in, and a normal put through that is no longer a direction - lighting
// has to be done in a space the light is fixed in, and for a head-lit gizmo that space is the view.
float4x4 world;
float3 meshColor;
float transparency;

// One draw, many copies of the SAME mesh: each copy's own world matrix and color, with the projection shared. The
// gizmo's seven balls are one sphere seven times over, its three arms one cylinder, its four arrows one triangle.
// The length is the cap on a single instanced draw - see InstanceCapacity.
float4x4 instanceWorld[64];
float4 instanceColor[64];
float4x4 viewProjection;
// The view's rotation for the instanced normals: identity when the copies are placed on the screen.
float4x4 view;
// The selection outline's width as a clip-space offset per unit of w, along x and along y.
float2 outlineStep;
// A line's width in pixels, one per copy in x (and for a ring, 1 in y when only its front half shows); lines are spread
// on screen, so the viewport's size in pixels too.
float4 instanceLine[64];
float2 viewportSize;
// A ring's normal (xyz) and radius (w), one per copy.
float4 instanceRing[64];
sampler sampleType;
Texture2D shaderTexture;

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
    float3 normal : NORMAL;
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
    output.normal = input.normal;
    return output;
}

PS_OUTPUT_BASIC BasicLit_VS(MESH_VERTEX input)
{
    PS_OUTPUT_BASIC output;

    input.position.w = 1.0f;
    output.position = mul(input.position, wvp);
    output.uv = input.uv0;
    output.color = float4(meshColor, transparency);
    output.normal = mul(input.normal, (float3x3)world);
    return output;
}

// The instanced twin: the placement and the color come from the tables above, everything else is identical. The
// normal rides the copy's OWN matrix, so copies of one mesh may be turned any way and still light correctly, then the
// view's, since the light is fixed in the view.
PS_OUTPUT_BASIC BasicLitInstanced_VS(MESH_VERTEX input, uint instanceId : SV_InstanceID)
{
    PS_OUTPUT_BASIC output;

    float4x4 placement = instanceWorld[instanceId];

    input.position.w = 1.0f;
    output.position = mul(mul(input.position, placement), viewProjection);
    output.uv = input.uv0;
    output.color = instanceColor[instanceId];
    output.normal = mul(mul(input.normal, (float3x3)placement), (float3x3)view);
    return output;
}

// Flat color gives a shape no form at all - every face of it reads as one silhouette. Lit from just off the viewer's
// shoulder, so a gizmo shades the same however the view turns: a diffuse term for the body, a tight specular so a ball
// reads as round, and a rim that keeps a dark one off a dark background without an outline pass.
float4 BasicLit_PS(PS_OUTPUT_BASIC input) : SV_TARGET
{
    float3 normal = normalize(input.normal);
    float3 eye = float3(0, 0, -1);
    float3 key = normalize(float3(-0.35f, -0.55f, -1.0f));

    float lambert = saturate(dot(normal, key));
    float3 halfway = normalize(key + eye);
    float specular = pow(saturate(dot(normal, halfway)), 40.0f) * 0.45f;
    float rim = pow(1.0f - saturate(dot(normal, eye)), 3.0f) * 0.35f;

    float3 shade = input.color.rgb * (0.42f + 0.58f * lambert + rim) + specular;
    return float4(saturate(shade), input.color.a);
}

struct PS_OUTPUT_LINE
{
    float4 position : SV_POSITION;
    float4 color : COLOR0;
    noperspective float across : TEXCOORD0;
    nointerpolation float halfWidth : TEXCOORD1;
};

// A segment of a line mesh turned into a quad (LineRibbon): the corner's own end is the position, the other end rides
// the normal, uv0 is the side and which end. The quad is spread on screen to the copy's width and a pixel more for the
// smoothing, and runs half a width past each end, so the segments of a polyline close over their joints.
PS_OUTPUT_LINE SdfLineInstanced_VS(MESH_VERTEX input, uint instanceId : SV_InstanceID)
{
    PS_OUTPUT_LINE output;

    float4x4 placement = instanceWorld[instanceId];
    float4 own = mul(float4(input.position.xyz, 1.0f), placement);
    float4 ownClip = mul(own, viewProjection);
    float4 otherClip = mul(mul(float4(input.normal, 1.0f), placement), viewProjection);

    float2 halfViewport = viewportSize * 0.5f;
    float2 ownPixel = ownClip.xy / ownClip.w * halfViewport;
    float2 otherPixel = otherClip.xy / otherClip.w * halfViewport;
    bool atStart = input.uv0.y < 0.5f;
    float2 along = atStart ? otherPixel - ownPixel : ownPixel - otherPixel;
    float span = length(along);
    along = span > 1e-4f ? along / span : float2(1.0f, 0.0f);
    float2 across = float2(-along.y, along.x);

    float halfWidth = instanceLine[instanceId].x * 0.5f;
    float reach = halfWidth + 1.0f;
    float2 pixel = ownPixel + across * (input.uv0.x * reach) + along * (atStart ? -halfWidth : halfWidth);

    output.position = float4(pixel / halfViewport * ownClip.w, ownClip.z, ownClip.w);
    output.color = instanceColor[instanceId];
    output.across = input.uv0.x * reach;
    output.halfWidth = halfWidth;
    return output;
}

// Full inside the line's width, fading out over its last pixel on either side.
float4 SdfLine_PS(PS_OUTPUT_LINE input) : SV_TARGET
{
    float coverage = saturate(input.halfWidth + 0.5f - abs(input.across));
    return float4(input.color.rgb, input.color.a * coverage);
}

struct PS_OUTPUT_RING
{
    float4 position : SV_POSITION;
    float4 color : COLOR0;
    float3 world : TEXCOORD0;
    nointerpolation float3 center : TEXCOORD1;
    nointerpolation float4 ring : TEXCOORD2;
    nointerpolation float2 stroke : TEXCOORD3;
};

// A ring drawn as the circle it is. The copy's matrix places a square facing the eye round the ring, its translation is
// the ring's center; instanceRing holds the ring's normal and radius, instanceLine its width and whether only the half
// on the eye's side shows. Everything is in the camera-relative space the eye sits at the origin of.
PS_OUTPUT_RING SdfRingInstanced_VS(MESH_VERTEX input, uint instanceId : SV_InstanceID)
{
    PS_OUTPUT_RING output;

    float4x4 placement = instanceWorld[instanceId];
    float4 world = mul(float4(input.position.xyz, 1.0f), placement);
    output.position = mul(world, viewProjection);
    output.color = instanceColor[instanceId];
    output.world = world.xyz;
    output.center = placement[3].xyz;
    output.ring = instanceRing[instanceId];
    output.stroke = float2(instanceLine[instanceId].x * 0.5f, instanceLine[instanceId].y);
    return output;
}

static const float Pi = 3.14159265f;

float RayDistance(float3 p, float3 dir)
{
    return length(p - dir * dot(p, dir));
}

// The point of the half ring c + r (u cos t + v sin t), t in [0, pi], nearest the ray from the eye along dir: a scan
// finds the valley, then Newton steps, halved until they come closer, settle in it.
float3 NearestOnHalfRing(float3 dir, float3 c, float3 u, float3 v, float r)
{
    float theta = 0.0f;
    float nearest = 1e30f;
    for (int k = 0; k < 24; k++)
    {
        float probe = Pi * k / 23.0f;
        float d = RayDistance(c + (u * cos(probe) + v * sin(probe)) * r, dir);
        if (d < nearest)
        {
            nearest = d;
            theta = probe;
        }
    }

    for (int i = 0; i < 5; i++)
    {
        float s, co;
        sincos(theta, s, co);
        float3 p = c + (u * co + v * s) * r;
        float3 dp = (v * co - u * s) * r;
        float3 ddp = -(u * co + v * s) * r;
        float pd = dot(p, dir);
        float dpd = dot(dp, dir);
        float f1 = dot(p, dp) - pd * dpd;
        float f2 = dot(dp, dp) + dot(p, ddp) - dpd * dpd - pd * dot(ddp, dir);
        float step = f2 > 1e-12f ? f1 / f2 : sign(f1) * 0.1f;
        for (int h = 0; h < 6; h++)
        {
            float next = theta - step;
            float d = RayDistance(c + (u * cos(next) + v * sin(next)) * r, dir);
            if (d < nearest)
            {
                theta = next;
                nearest = d;
                break;
            }

            step *= 0.5f;
        }
    }

    theta = clamp(theta, 0.0f, Pi);
    return c + (u * cos(theta) + v * sin(theta)) * r;
}

// The pixel's distance to the true circle, in pixels, turned into coverage. The half on the eye's side is split across
// the sight to the center, not at the ball's silhouette, which would leave less than half of a tilted ring and nothing
// of one that faces the eye.
float4 SdfRing_PS(PS_OUTPUT_RING input) : SV_TARGET
{
    float footprint = max(length(ddx(input.world)), length(ddy(input.world))) / length(input.world);
    float3 dir = normalize(input.world);
    float3 c = input.center;
    float3 n = input.ring.xyz;
    float r = input.ring.w;

    float3 toEye = -c - n * dot(-c, n);
    float3 v = dot(toEye, toEye) > 1e-12f * r * r
        ? normalize(toEye)
        : normalize(cross(n, abs(n.x) < 0.9f ? float3(1.0f, 0.0f, 0.0f) : float3(0.0f, 1.0f, 0.0f)));
    float3 u = cross(n, v);

    float3 p = NearestOnHalfRing(dir, c, u, v, r);
    if (input.stroke.y < 0.5f)
    {
        float3 back = NearestOnHalfRing(dir, c, u, -v, r);
        if (RayDistance(back, dir) < RayDistance(p, dir))
        {
            p = back;
        }
    }

    float depth = dot(p, dir);
    float distance = RayDistance(p, dir) / (footprint * depth);
    float coverage = depth > 0.0f ? saturate(input.stroke.x + 0.5f - distance) : 0.0f;
    if (coverage <= 0.0f)
    {
        discard;
    }

    return float4(input.color.rgb, input.color.a * coverage);
}

// The selection outline: every copy drawn once per direction, shifted that way by the outline's width on screen, and
// the stencil keeps only what lands outside the selection itself. Copy k's placement and color sit at k / directions.
static const uint outlineDirections = 8;

PS_OUTPUT_BASIC OutlineInstanced_VS(MESH_VERTEX input, uint instanceId : SV_InstanceID)
{
    PS_OUTPUT_BASIC output;

    uint copy = instanceId / outlineDirections;
    float angle = (instanceId % outlineDirections) * (6.2831853f / outlineDirections);
    float4 position = mul(mul(float4(input.position.xyz, 1.0f), instanceWorld[copy]), viewProjection);
    position.xy += float2(cos(angle), sin(angle)) * outlineStep * position.w;

    output.position = position;
    output.uv = input.uv0;
    output.color = instanceColor[copy];
    output.normal = input.normal;
    return output;
}

// The skinned twins: the vertex goes where its joints carry it first, then the copy places it. A skin has one palette,
// so a skinned mesh is drawn one copy at a time.
PS_OUTPUT_BASIC SkinnedLitInstanced_VS(SKINNED_VERTEX input, uint instanceId : SV_InstanceID)
{
    PS_OUTPUT_BASIC output;

    float4x4 placement = mul(SkinOf(input.jointIndices, input.jointWeights), instanceWorld[instanceId]);
    output.position = mul(mul(float4(input.position.xyz, 1.0f), placement), viewProjection);
    output.uv = input.uv0;
    output.color = instanceColor[instanceId];
    output.normal = mul(mul(input.normal, (float3x3)placement), (float3x3)view);
    return output;
}

PS_OUTPUT_BASIC SkinnedOutlineInstanced_VS(SKINNED_VERTEX input, uint instanceId : SV_InstanceID)
{
    PS_OUTPUT_BASIC output;

    uint copy = instanceId / outlineDirections;
    float angle = (instanceId % outlineDirections) * (6.2831853f / outlineDirections);
    float4x4 placement = mul(SkinOf(input.jointIndices, input.jointWeights), instanceWorld[copy]);
    float4 position = mul(mul(float4(input.position.xyz, 1.0f), placement), viewProjection);
    position.xy += float2(cos(angle), sin(angle)) * outlineStep * position.w;

    output.position = position;
    output.uv = input.uv0;
    output.color = instanceColor[copy];
    output.normal = input.normal;
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

float4 BasicTextured_PS(PS_OUTPUT_BASIC input) : SV_TARGET
{
    float4 color = shaderTexture.Sample(sampleType, input.uv);
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

    pass Lit
    {
        VertexShader = BasicLit_VS;
        PixelShader = BasicLit_PS;
    }

    pass LitInstanced
    {
        VertexShader = BasicLitInstanced_VS;
        PixelShader = BasicLit_PS;
    }

    // Lines have no normals to light: flat color, placed the same way.
    pass FlatInstanced
    {
        VertexShader = BasicLitInstanced_VS;
        PixelShader = BasicVertexColored_PS;
    }

    pass OutlineInstanced
    {
        VertexShader = OutlineInstanced_VS;
        PixelShader = BasicVertexColored_PS;
    }

    pass SkinnedLitInstanced
    {
        VertexShader = SkinnedLitInstanced_VS;
        PixelShader = BasicLit_PS;
    }

    pass SkinnedFlatInstanced
    {
        VertexShader = SkinnedLitInstanced_VS;
        PixelShader = BasicVertexColored_PS;
    }

    pass SkinnedOutlineInstanced
    {
        VertexShader = SkinnedOutlineInstanced_VS;
        PixelShader = BasicVertexColored_PS;
    }

    // Lines a width in pixels wide with smooth edges, drawn from a LineRibbon.
    pass SdfLineInstanced
    {
        VertexShader = SdfLineInstanced_VS;
        PixelShader = SdfLine_PS;
    }

    // Rings drawn as circles: each pixel's distance to the true circle, see SdfRing_PS.
    pass SdfRingInstanced
    {
        VertexShader = SdfRingInstanced_VS;
        PixelShader = SdfRing_PS;
    }
}
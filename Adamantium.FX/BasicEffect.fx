#include "Includes/Skinning.fxh"

float4x4 wvp;
float3 meshColor;
float transparency;

// One drawn instance of a gizmo's mesh. The gizmo's seven balls are one sphere seven times over, its three arms one
// cylinder, its four arrows one triangle: one draw each, with the projection shared.
struct GizmoInstance
{
    float4x4 world;
    float4 color;

    float4 Placed(float4 position)
    {
        return mul(position, world);
    }

    float4 Placed(float3 position)
    {
        return Placed(float4(position, 1.0f));
    }

    float3 Turned(float3 normal)
    {
        return mul(normal, (float3x3)world);
    }
};

// The draw's instances.
GizmoInstance* Instances;
// The joints a skinned instance bends with, and how many there are.
float4x4* Joints;
uint JointCount;

GizmoInstance InstanceOf(uint instanceId)
{
    return Instances[instanceId];
}

float4x4 viewProjection;
// The view's rotation for the instanced normals: identity when the instances are placed on the screen.
float4x4 view;
// The selection outline's width as a clip-space offset per unit of w, along x and along y.
float2 outlineStep;
// The viewport's size in pixels, which lines are spread on.
float2 viewportSize;
// How wide lines and rings are drawn, in pixels.
float lineWidth;
// The ring a ring pass draws: its normal (xyz) and radius (w); its center is the instance's.
float4 ring;
// Not zero when only the half of the ring on the eye's side shows.
uint frontHalfOnly;

// A normal of the mesh turned with the instance, then with the view, since the light is fixed in the view.
float3 InView(GizmoInstance instance, float3 normal)
{
    return mul(instance.Turned(normal), (float3x3)view);
}

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

[shader("vertex")]
PS_OUTPUT_BASIC Basic_VS(MESH_VERTEX input)
{
    PS_OUTPUT_BASIC output;
    output.position = mul(float4(input.position.xyz, 1.0f), wvp);
    output.uv = input.uv0;
    output.color = input.color;
    output.normal = input.normal;
    return output;
}

// The normal rides the instance's OWN matrix, so instances of one mesh may be turned any way and still light correctly.
[shader("vertex")]
PS_OUTPUT_BASIC BasicLitInstanced_VS(MESH_VERTEX input, uint instanceId : SV_InstanceID)
{
    GizmoInstance instance = InstanceOf(instanceId);
    PS_OUTPUT_BASIC output;
    output.position = mul(instance.Placed(input.position.xyz), viewProjection);
    output.uv = input.uv0;
    output.color = instance.color;
    output.normal = InView(instance, input.normal);
    return output;
}

// Flat color gives a shape no form at all - every face of it reads as one silhouette. Lit from just off the viewer's
// shoulder, so a gizmo shades the same however the view turns: a diffuse term for the body, a tight specular so a ball
// reads as round, and a rim that keeps a dark one off a dark background without an outline pass.
[shader("fragment")]
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
};

// A segment of a line mesh turned into a quad (LineRibbon): the corner's own end is the position, the other end rides
// the normal, uv0 is the side and which end. The quad is spread on screen to the line's width and a pixel more for the
// smoothing, and runs half a width past each end, so the segments of a polyline close over their joints.
[shader("vertex")]
PS_OUTPUT_LINE SdfLineInstanced_VS(MESH_VERTEX input, uint instanceId : SV_InstanceID)
{
    GizmoInstance instance = InstanceOf(instanceId);
    PS_OUTPUT_LINE output;

    float4 ownClip = mul(instance.Placed(input.position.xyz), viewProjection);
    float4 otherClip = mul(instance.Placed(input.normal), viewProjection);

    float2 halfViewport = viewportSize * 0.5f;
    float2 ownPixel = ownClip.xy / ownClip.w * halfViewport;
    float2 otherPixel = otherClip.xy / otherClip.w * halfViewport;
    bool atStart = input.uv0.y < 0.5f;
    float2 along = atStart ? otherPixel - ownPixel : ownPixel - otherPixel;
    float span = length(along);
    along = span > 1e-4f ? along / span : float2(1.0f, 0.0f);
    float2 across = float2(-along.y, along.x);

    float halfWidth = lineWidth * 0.5f;
    float reach = halfWidth + 1.0f;
    float2 pixel = ownPixel + across * (input.uv0.x * reach) + along * (atStart ? -halfWidth : halfWidth);

    output.position = float4(pixel / halfViewport * ownClip.w, ownClip.z, ownClip.w);
    output.color = instance.color;
    output.across = input.uv0.x * reach;
    return output;
}

// Full inside the line's width, fading out over its last pixel on either side.
[shader("fragment")]
float4 SdfLine_PS(PS_OUTPUT_LINE input) : SV_TARGET
{
    float coverage = saturate(lineWidth * 0.5f + 0.5f - abs(input.across));
    return float4(input.color.rgb, input.color.a * coverage);
}

struct PS_OUTPUT_RING
{
    float4 position : SV_POSITION;
    float4 color : COLOR0;
    float3 world : TEXCOORD0;
    nointerpolation float3 center : TEXCOORD1;
};

// A ring drawn as the circle it is. The instance's matrix places a square facing the eye round the ring, its
// translation is the ring's center. Everything is in the camera-relative space the eye sits at the origin of.
[shader("vertex")]
PS_OUTPUT_RING SdfRingInstanced_VS(MESH_VERTEX input, uint instanceId : SV_InstanceID)
{
    GizmoInstance instance = InstanceOf(instanceId);
    PS_OUTPUT_RING output;

    float4 world = instance.Placed(input.position.xyz);
    output.position = mul(world, viewProjection);
    output.color = instance.color;
    output.world = world.xyz;
    output.center = instance.world[3].xyz;
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
[shader("fragment")]
float4 SdfRing_PS(PS_OUTPUT_RING input) : SV_TARGET
{
    float footprint = max(length(ddx(input.world)), length(ddy(input.world))) / length(input.world);
    float3 dir = normalize(input.world);
    float3 c = input.center;
    float3 n = ring.xyz;
    float r = ring.w;

    float3 toEye = -c - n * dot(-c, n);
    float3 v = dot(toEye, toEye) > 1e-12f * r * r
        ? normalize(toEye)
        : normalize(cross(n, abs(n.x) < 0.9f ? float3(1.0f, 0.0f, 0.0f) : float3(0.0f, 1.0f, 0.0f)));
    float3 u = cross(n, v);

    float3 p = NearestOnHalfRing(dir, c, u, v, r);
    if (frontHalfOnly == 0)
    {
        float3 back = NearestOnHalfRing(dir, c, u, -v, r);
        if (RayDistance(back, dir) < RayDistance(p, dir))
        {
            p = back;
        }
    }

    float depth = dot(p, dir);
    float distance = RayDistance(p, dir) / (footprint * depth);
    float coverage = depth > 0.0f ? saturate(lineWidth * 0.5f + 0.5f - distance) : 0.0f;
    if (coverage <= 0.0f)
    {
        discard;
    }

    return float4(input.color.rgb, input.color.a * coverage);
}

// The selection outline: every instance drawn once per direction, shifted that way by the outline's width on screen,
// and the stencil keeps only what lands outside the selection itself. Instance k is drawn by k * directions and on.
static const uint OutlineDirections = 8;

float4 Shifted(float4 position, uint instanceId)
{
    float angle = (instanceId % OutlineDirections) * (6.2831853f / OutlineDirections);
    position.xy += float2(cos(angle), sin(angle)) * outlineStep * position.w;
    return position;
}

[shader("vertex")]
PS_OUTPUT_BASIC OutlineInstanced_VS(MESH_VERTEX input, uint instanceId : SV_InstanceID)
{
    GizmoInstance instance = InstanceOf(instanceId / OutlineDirections);
    PS_OUTPUT_BASIC output;
    output.position = Shifted(mul(instance.Placed(input.position.xyz), viewProjection), instanceId);
    output.uv = input.uv0;
    output.color = instance.color;
    output.normal = input.normal;
    return output;
}

// The skinned twins: the vertex goes where its joints carry it first, then the instance places it. A skin has one
// palette, so a skinned mesh is drawn one instance at a time.
GizmoInstance SkinnedInstanceOf(uint instanceId, SKINNED_VERTEX input)
{
    GizmoInstance instance = InstanceOf(instanceId);
    instance.world = mul(SkinOf(Joints, JointCount, input.jointIndices, input.jointWeights),
        instance.world);
    return instance;
}

[shader("vertex")]
PS_OUTPUT_BASIC SkinnedLitInstanced_VS(SKINNED_VERTEX input, uint instanceId : SV_InstanceID)
{
    GizmoInstance instance = SkinnedInstanceOf(instanceId, input);
    PS_OUTPUT_BASIC output;
    output.position = mul(instance.Placed(input.position.xyz), viewProjection);
    output.uv = input.uv0;
    output.color = instance.color;
    output.normal = InView(instance, input.normal);
    return output;
}

[shader("vertex")]
PS_OUTPUT_BASIC SkinnedOutlineInstanced_VS(SKINNED_VERTEX input, uint instanceId : SV_InstanceID)
{
    GizmoInstance instance = SkinnedInstanceOf(instanceId / OutlineDirections, input);
    PS_OUTPUT_BASIC output;
    output.position = Shifted(mul(instance.Placed(input.position.xyz), viewProjection), instanceId);
    output.uv = input.uv0;
    output.color = instance.color;
    output.normal = input.normal;
    return output;
}

[shader("fragment")]
float4 BasicColored_PS(PS_OUTPUT_BASIC input) : SV_TARGET
{
    return float4(meshColor, transparency);
}

[shader("fragment")]
float4 BasicVertexColored_PS(PS_OUTPUT_BASIC input) : SV_TARGET
{
    return input.color;
}

technique Basic
{
    pass Colored
    {
        VertexShader = Basic_VS;
        PixelShader = BasicColored_PS;
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

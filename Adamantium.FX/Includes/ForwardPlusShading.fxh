// Clustered forward shading, shared by the effects that light meshes with it. The view is cut into ClusterX x ClusterY
// tiles on screen and ClusterZ slices in depth, spaced logarithmically; a pixel is shaded with its own cluster's list
// of point and spot lights. Directional lights reach everything and are never listed.

static const uint ClusterX = 16;
static const uint ClusterY = 9;
static const uint ClusterZ = 24;
static const uint ClusterCount = ClusterX * ClusterY * ClusterZ;
static const uint MaxLightsPerCluster = 128;

enum class LightKind : uint
{
    Point,
    Spot,
    Directional
};

float3 BlinnPhong(float3 surface, float3 normal, float3 toLight, float3 toEye)
{
    float diffuse = saturate(dot(normal, toLight));
    float specular = diffuse > 0.0f ? pow(saturate(dot(normal, normalize(toLight + toEye))), 32.0f) * 0.25f : 0.0f;
    return surface * diffuse + specular;
}

// A light in view space. Directional lights come first in the list.
struct ClusterLight
{
    float4 positionRange;       // xyz position, w range
    float4 colorCosOuter;       // rgb color times intensity, w cosine of the outer half-angle
    float4 directionCosInner;   // xyz where it shines, w cosine of the inner half-angle
    LightKind kind;
    uint3 reserved;

    // What a directional light gives a surface.
    float3 Shine(float3 surface, float3 normal, float3 toEye)
    {
        return BlinnPhong(surface, normal, -directionCosInner.xyz, toEye) * colorCosOuter.rgb;
    }

    // What a point or spot light gives a surface at a position, fading out towards its range and, for a spot, its cone.
    float3 Light(float3 surface, float3 position, float3 normal, float3 toEye)
    {
        float3 toLight = positionRange.xyz - position;
        float distance = length(toLight);
        float3 l = toLight / max(distance, 1e-4f);
        float reach = saturate(1.0f - (distance * distance) / (positionRange.w * positionRange.w));
        float fade = reach * reach;
        if (kind == LightKind.Spot)
        {
            fade *= smoothstep(colorCosOuter.w, directionCosInner.w, dot(-l, directionCosInner.xyz));
        }

        return BlinnPhong(surface, normal, l, toEye) * colorCosOuter.rgb * fade;
    }
};

ClusterLight* Lights;
// How many lights each cluster has, and for each the lights it has, MaxLightsPerCluster per cluster.
uint* ClusterCounts;
uint* ClusterLights;
uint LightCount;
uint DirectionalCount;
// x near, y far, z slices per unit of log depth, w log(near).
float4 ClusterDepth;
// The inverse of the projection's x and y scales.
float2 InverseScale;
// The projection's w at a depth, as depth * x + y: the depth itself in perspective, 1 in a flat view.
float2 ClusterW;
// The viewport in framebuffer pixels: x, y, width, height.
float4 ViewportRect;
float3 Ambient;

// One drawn copy of a mesh: a draw takes InstanceBase + its instance index.
struct MeshInstance
{
    float4x4 world;
    // rgb: the surface color when it has no texture.
    float4 albedo;
    // x: the first matrix of its joints' palette in Palettes, y: how many joints it has (a skinned copy only);
    // z: its texture in meshTextures (a textured pass only).
    uint4 slots;

    float4 Placed(float4 position)
    {
        return mul(position, world);
    }

    float3 Turned(float3 normal)
    {
        return mul(normal, (float3x3)world);
    }
};

MeshInstance* Instances;
float4x4* Palettes;
uint InstanceBase;

float4x4 view;
float4x4 viewProjection;
SamplerState sampleType;
// Every texture of the resource heap - or of the shared texture set - by its slot there: the copies of one draw each name
// their own. A set of its own, which descriptor sets bind apart from the pass's other resources.
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

struct LIT_PIXEL
{
    float4 position : SV_POSITION;
    float3 viewPosition : TEXCOORD0;
    float3 viewNormal : NORMAL;
    float2 uv : TEXCOORD1;
    nointerpolation float3 albedo : COLOR0;
    nointerpolation uint texture : TEXCOORD2;
};

float4 SurfaceOf(LIT_PIXEL input)
{
    return meshTextures[NonUniformResourceIndex(input.texture)].Sample(sampleType, input.uv);
}

uint ClusterAt(float2 pixel, float depth)
{
    float2 place = saturate((pixel - ViewportRect.xy) / ViewportRect.zw);
    uint x = min((uint)(place.x * ClusterX), ClusterX - 1);
    uint y = min((uint)(place.y * ClusterY), ClusterY - 1);
    float slice = (log(max(depth, ClusterDepth.x)) - ClusterDepth.w) * ClusterDepth.z;
    uint z = min((uint)max(slice, 0.0f), ClusterZ - 1);
    return x + y * ClusterX + z * ClusterX * ClusterY;
}

float3 Shade(float3 surface, float3 position, float3 normal, float2 pixel)
{
    float3 toEye = normalize(-position);
    float3 n = dot(normal, normal) > 1e-8f ? normalize(normal) : toEye;
    if (dot(n, toEye) < 0.0f)
    {
        n = -n;
    }

    float3 lit = surface * Ambient;
    for (uint i = 0; i < DirectionalCount; i++)
    {
        lit += Lights[i].Shine(surface, n, toEye);
    }

    uint cluster = ClusterAt(pixel, position.z);
    uint count = ClusterCounts[cluster];
    for (uint k = 0; k < count; k++)
    {
        lit += Lights[ClusterLights[cluster * MaxLightsPerCluster + k]].Light(surface, position, n, toEye);
    }

    return lit;
}

MeshInstance InstanceOf(uint instanceId)
{
    return Instances[InstanceBase + instanceId];
}

// A vertex of a copy, at a position and with a normal already in the mesh's own space.
LIT_PIXEL LitOf(MeshInstance instance, float4 position, float3 normal, float2 uv)
{
    LIT_PIXEL output;
    float4 placed = instance.Placed(position);
    output.position = mul(placed, viewProjection);
    output.viewPosition = mul(placed, view).xyz;
    output.viewNormal = mul(instance.Turned(normal), (float3x3)view);
    output.uv = uv;
    output.albedo = instance.albedo.rgb;
    output.texture = instance.slots.z;
    return output;
}

[shader("vertex")]
LIT_PIXEL Lit_VS(MESH_VERTEX input, uint instanceId : SV_InstanceID)
{
    return LitOf(InstanceOf(instanceId), float4(input.position.xyz, 1.0f), input.normal, input.uv0);
}

#include "Skinning.fxh"

// The vertex goes where its joints carry it first, then the copy places it.
[shader("vertex")]
LIT_PIXEL LitSkinned_VS(SKINNED_VERTEX input, uint instanceId : SV_InstanceID)
{
    MeshInstance instance = InstanceOf(instanceId);
    float4x4 skin = SkinOf(Palettes + instance.slots.x, instance.slots.y, input.jointIndices,
        input.jointWeights);
    return LitOf(instance, mul(float4(input.position.xyz, 1.0f), skin), mul(input.normal, (float3x3)skin),
        input.uv0);
}

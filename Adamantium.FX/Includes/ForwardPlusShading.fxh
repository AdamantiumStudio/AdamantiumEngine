// Clustered forward shading, shared by the effects that light meshes with it. The view is cut into ClusterX x ClusterY
// tiles on screen and ClusterZ slices in depth, spaced logarithmically; a pixel is shaded with its own cluster's list
// of point and spot lights. Directional lights reach everything and are never listed.

static const uint ClusterX = 16;
static const uint ClusterY = 9;
static const uint ClusterZ = 24;
static const uint ClusterCount = ClusterX * ClusterY * ClusterZ;
static const uint MaxLightsPerCluster = 128;

static const uint SpotLight = 1;

// A light in view space. Directional lights come first in the list.
struct ClusterLight
{
    float4 positionRange;       // xyz position, w range
    float4 colorCosOuter;       // rgb color times intensity, w cosine of the outer half-angle
    float4 directionCosInner;   // xyz where it shines, w cosine of the inner half-angle
    uint4 kind;                 // x: 0 point, 1 spot, 2 directional
};

uint64_t LightsAddress;
uint64_t ClusterCountsAddress;
uint64_t ClusterLightsAddress;
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

float4x4 world;
float4x4 view;
float4x4 viewProjection;
float3 albedo;
sampler sampleType;
Texture2D shaderTexture;

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
};

uint ClusterAt(float2 pixel, float depth)
{
    float2 place = saturate((pixel - ViewportRect.xy) / ViewportRect.zw);
    uint x = min((uint)(place.x * ClusterX), ClusterX - 1);
    uint y = min((uint)(place.y * ClusterY), ClusterY - 1);
    float slice = (log(max(depth, ClusterDepth.x)) - ClusterDepth.w) * ClusterDepth.z;
    uint z = min((uint)max(slice, 0.0f), ClusterZ - 1);
    return x + y * ClusterX + z * ClusterX * ClusterY;
}

float3 BlinnPhong(float3 surface, float3 normal, float3 toLight, float3 toEye)
{
    float diffuse = saturate(dot(normal, toLight));
    float specular = diffuse > 0.0f ? pow(saturate(dot(normal, normalize(toLight + toEye))), 32.0f) * 0.25f : 0.0f;
    return surface * diffuse + specular;
}

float3 Shade(float3 surface, float3 position, float3 normal, float2 pixel)
{
    float3 toEye = normalize(-position);
    float3 n = dot(normal, normal) > 1e-8f ? normalize(normal) : toEye;
    if (dot(n, toEye) < 0.0f)
    {
        n = -n;
    }

    ClusterLight* lights = (ClusterLight*)LightsAddress;
    float3 lit = surface * Ambient;
    for (uint i = 0; i < DirectionalCount; i++)
    {
        lit += BlinnPhong(surface, n, -lights[i].directionCosInner.xyz, toEye) * lights[i].colorCosOuter.rgb;
    }

    uint cluster = ClusterAt(pixel, position.z);
    uint* counts = (uint*)ClusterCountsAddress;
    uint* list = (uint*)ClusterLightsAddress;
    uint count = counts[cluster];
    for (uint k = 0; k < count; k++)
    {
        ClusterLight light = lights[list[cluster * MaxLightsPerCluster + k]];
        float3 toLight = light.positionRange.xyz - position;
        float distance = length(toLight);
        float3 l = toLight / max(distance, 1e-4f);
        float reach = saturate(1.0f - (distance * distance) / (light.positionRange.w * light.positionRange.w));
        float fade = reach * reach;
        if (light.kind.x == SpotLight)
        {
            fade *= smoothstep(light.colorCosOuter.w, light.directionCosInner.w, dot(-l, light.directionCosInner.xyz));
        }

        lit += BlinnPhong(surface, n, l, toEye) * light.colorCosOuter.rgb * fade;
    }

    return lit;
}

LIT_PIXEL Lit_VS(MESH_VERTEX input)
{
    LIT_PIXEL output;
    float4 placed = mul(float4(input.position.xyz, 1.0f), world);
    output.position = mul(placed, viewProjection);
    output.viewPosition = mul(placed, view).xyz;
    output.viewNormal = mul(mul(input.normal, (float3x3)world), (float3x3)view);
    output.uv = input.uv0;
    return output;
}

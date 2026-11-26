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
    // rgb: the surface color when it has no texture; a: its alpha, which a texture's alpha multiplies.
    float4 albedo;
    // x: the first matrix of its joints' palette in Palettes, y: how many joints it has (a skinned copy only);
    // z: its texture in meshTextures (a textured pass only).
    uint3 slots;
    // Below this alpha an opaque pass leaves the pixel out.
    float alphaCutoff;
    // rgb: the light the surface gives off, its strength in; a: unused.
    float4 emission;
    // x metalness, y roughness, z how much the normal map bends, w how much the occlusion map darkens (physical only).
    float4 surface;
    // Its maps in meshTextures, NoMap where it has none: x metalness and roughness, y normals, z occlusion, w emission.
    uint4 maps;

    float4 Placed(float4 position)
    {
        return mul(position, world);
    }

    float3 Turned(float3 normal)
    {
        return mul(normal, (float3x3)world);
    }
};

// A map slot or texture index that names no texture.
static const uint NoMap = 0xFFFFFFFFu;

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
    nointerpolation float4 albedo : COLOR0;
    nointerpolation uint texture : TEXCOORD2;
    nointerpolation float alphaCutoff : TEXCOORD3;
    // The copy's place in Instances, for what only some pixels read: its emission and its maps.
    nointerpolation uint instance : TEXCOORD4;
};

float4 SurfaceOf(LIT_PIXEL input)
{
    return meshTextures[NonUniformResourceIndex(input.texture)].Sample(sampleType, input.uv);
}

float4 MapOf(uint map, float2 uv, float4 none)
{
    return map == NoMap ? none : meshTextures[NonUniformResourceIndex(map)].Sample(sampleType, uv);
}

float3 Linear(float3 srgb)
{
    return srgb <= 0.04045f ? srgb / 12.92f : pow((srgb + 0.055f) / 1.055f, 2.4f);
}

// The light the surface gives off at the pixel, in linear light: its map, decoded from sRGB, times its color and
// strength.
float3 EmissionOf(LIT_PIXEL input)
{
    MeshInstance copy = Instances[input.instance];
    return copy.emission.rgb * Linear(MapOf(copy.maps.w, input.uv, float4(1.0f, 1.0f, 1.0f, 1.0f)).rgb);
}

// The surface's own color, from its texture if it has one, times its color.
float4 BaseColorOf(LIT_PIXEL input)
{
    return input.texture == NoMap ? input.albedo : SurfaceOf(input) * float4(1.0f, 1.0f, 1.0f, input.albedo.a);
}

// A see-through pixel, in any order with the others over it (weighted blended order-independent transparency, McGuire
// and Bavoil 2013): its color and alpha, weighted, summed into the first target; its alpha laid over the coverage in the
// second, 1 - the product of (1 - alpha). The weight, their equation (7), favours the near layers at the scale of metres;
// it stops at 300 and a layer's light at 16, so a dozen bright layers at the eye still fit a half float.
struct WEIGHTED_PIXEL
{
    float4 accumulated : SV_TARGET0;
    float4 coverage : SV_TARGET1;
};

WEIGHTED_PIXEL Weighted(float3 lit, float alpha, float viewDepth)
{
    float depth = abs(viewDepth);
    float weight = alpha * clamp(10.0f / (1e-5f + pow(depth / 5.0f, 2.0f) + pow(depth / 200.0f, 6.0f)), 1e-2f, 3e2f);
    WEIGHTED_PIXEL output;
    output.accumulated = float4(min(lit, 16.0f) * alpha, alpha) * weight;
    output.coverage = float4(alpha, 0.0f, 0.0f, alpha);
    return output;
}

// A masked surface's texel, the pixel left out below the cutoff. Only masked passes call it: a discard anywhere in a
// shader turns off the early depth test.
float4 MaskedSurfaceOf(LIT_PIXEL input)
{
    float4 surface = SurfaceOf(input);
    clip(surface.a * input.albedo.a - input.alphaCutoff);
    return surface;
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

// --- The shadow of one directional light, its four cascades side by side in one depth map, each a quarter of it: the map
// in meshTextures, NoMap for none, and the light's place in Lights.
uint ShadowMap;
uint ShadowLight;
// From view space into each cascade's quarter of the map: x and y where on the map, z the depth from the light.
float4x4 ShadowCascade0;
float4x4 ShadowCascade1;
float4x4 ShadowCascade2;
float4x4 ShadowCascade3;
// Where each cascade ends, in view depth.
float4 ShadowSplits;
// How far apart two texels of each cascade lie, in the world.
float4 ShadowTexels;
// One over the map's side.
float ShadowStep;

// How much of the shadowing light reaches a point seen in view space, 0 to 1: nine bilinear comparisons around it, so the
// edge is soft over a few texels. The point is moved off its surface along its normal by a texel and a half, so a surface
// does not shadow itself.
float ShadowAt(float3 position, float3 n)
{
    if (ShadowMap == NoMap || position.z > ShadowSplits.w)
    {
        return 1.0f;
    }

    uint cascade = position.z > ShadowSplits.x ? (position.z > ShadowSplits.y ? (position.z > ShadowSplits.z ? 3 : 2) : 1) : 0;
    float4x4 toMap = cascade == 0 ? ShadowCascade0 : cascade == 1 ? ShadowCascade1 : cascade == 2 ? ShadowCascade2
                                                                                                 : ShadowCascade3;
    float4 at = mul(float4(position + n * ShadowTexels[cascade] * 1.5f, 1.0f), toMap);
    float2 corner = float2(cascade & 1, cascade >> 1) * 0.5f;
    if (any(at.xy < corner) || any(at.xy > corner + 0.5f) || at.z >= 1.0f)
    {
        return 1.0f;
    }

    // Kept two texels inside its quarter, so no tap reads the next cascade.
    float2 uv = clamp(at.xy, corner + ShadowStep * 2.0f, corner + 0.5f - ShadowStep * 2.0f);
    float2 texel = uv / ShadowStep - 0.5f;
    float2 f = frac(texel);
    float2 center = (floor(texel) + 1.0f) * ShadowStep;
    float lit = 0.0f;
    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            // The four texels around the tap: x (0, 1), y (1, 1), z (1, 0), w (0, 0).
            float4 depths = meshTextures[NonUniformResourceIndex(ShadowMap)].Gather(sampleType,
                center + float2(x, y) * ShadowStep);
            float4 reached = step(at.z, depths);
            lit += lerp(lerp(reached.w, reached.z, f.x), lerp(reached.x, reached.y, f.x), f.y);
        }
    }

    return lit / 9.0f;
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
    float shadow = ShadowAt(position, n);
    for (uint i = 0; i < DirectionalCount; i++)
    {
        lit += Lights[i].Shine(surface, n, toEye) * (i == ShadowLight ? shadow : 1.0f);
    }

    uint cluster = ClusterAt(pixel, position.z);
    uint count = ClusterCounts[cluster];
    for (uint k = 0; k < count; k++)
    {
        lit += Lights[ClusterLights[cluster * MaxLightsPerCluster + k]].Light(surface, position, n, toEye);
    }

    return lit;
}

// --- The physical model: metalness and roughness, Cook-Torrance with the GGX distribution, Smith-Schlick geometry and
// Schlick's Fresnel. It works in linear light, the colors it is given decoded from sRGB.

static const float Pi = 3.14159265f;

float3 Srgb(float3 linear)
{
    float3 c = saturate(linear);
    return c <= 0.0031308f ? c * 12.92f : 1.055f * pow(c, 1.0f / 2.4f) - 0.055f;
}

// 1 when the frame holds linear light without a ceiling, for the tone curve to bring to the screen; 0 when it holds
// what the screen shows.
uint LinearFrame;

// A simple surface's light as the frame takes it: its sRGB as it is, or decoded for a linear frame, its glow added.
float3 SimpleInFrame(float3 lit, float3 glow)
{
    return LinearFrame != 0 ? Linear(saturate(lit)) + glow : lit + glow;
}

// A physical surface's linear light as the frame takes it.
float3 PhysicalInFrame(float3 lit)
{
    return LinearFrame != 0 ? lit : Srgb(lit);
}

// --- The sky round the scene, made here rather than read from a picture: its kind (0 none, 1 studio, 2 stars, 3 even)
// and strength. Up is -Y.
uint Sky;
float SkyStrength;
// From view space to the world's turn, for looking the sky up along a normal or a reflection.
float4x4 WorldFromView;
// The sky's light round a normal, in meshTextures: what a matt surface gets from it.
uint SkyIrradiance;
// The sky mirrored by ever rougher surfaces, roughness 0, 0.2 ... 1, in meshTextures.
uint SkyReflection0;
uint SkyReflection1;
uint SkyReflection2;
uint SkyReflection3;
uint SkyReflection4;
uint SkyReflection5;

static const uint SkyReflectionLevels = 6;

// Where a direction falls on a picture of the whole sky: across, round the vertical; down, from straight up.
float2 SkyPlaceOf(float3 direction)
{
    return float2(atan2(direction.z, direction.x) / (2.0f * Pi) + 0.5f, acos(clamp(-direction.y, -1.0f, 1.0f)) / Pi);
}

float3 SkyDirectionOf(float2 place)
{
    float around = (place.x - 0.5f) * 2.0f * Pi;
    float down = place.y * Pi;
    return float3(cos(around) * sin(down), -cos(down), sin(around) * sin(down));
}

// A number 0 to 1 of a cell and a salt, the same on every GPU: integer mixing, as a sine of a large number loses its
// digits and draws patterns.
float SkyHash(int3 cell, uint salt)
{
    uint3 v = asuint(cell) * uint3(1597334673u, 3812015801u, 2798796415u);
    uint h = (v.x ^ v.y ^ v.z ^ salt) * 2654435761u;
    h ^= h >> 15;
    h *= 739982445u;
    h ^= h >> 12;
    return h / 4294967295.0f;
}

// The stars of one layer: the sphere cut into cells, a few holding one star somewhere inside.
float StarsOf(float3 direction, float cells, float share, float size)
{
    int3 cell = int3(floor(direction * cells));
    if (SkyHash(cell, 0u) > share)
    {
        return 0.0f;
    }

    float3 star = cell + 0.25f + 0.5f * float3(SkyHash(cell, 1u), SkyHash(cell, 2u), SkyHash(cell, 3u));
    float apart = acos(saturate(dot(direction, normalize(star))));
    float brightness = pow(SkyHash(cell, 4u), 3.0f);
    return (0.15f + 0.85f * brightness) * exp(-apart * apart / (size * size));
}

// The sky's light along a direction, before its strength. Smooth leaves the stars out but keeps their sum, for what
// gathers light from many directions at once.
float3 SkyLight(float3 direction, bool smooth)
{
    float up = -direction.y;
    if (Sky == 1)
    {
        float3 zenith = float3(0.85f, 0.88f, 0.95f);
        float3 horizon = float3(0.62f, 0.62f, 0.64f);
        float3 ground = float3(0.18f, 0.17f, 0.16f);
        float3 light = up >= 0.0f ? lerp(horizon, zenith, pow(up, 0.6f)) : lerp(horizon, ground, pow(-up, 0.4f));
        // A broad panel high on one side, as a studio's key light.
        return light + float3(1.6f, 1.55f, 1.5f) * pow(saturate(dot(direction, normalize(float3(0.4f, -0.8f, 0.3f)))), 24.0f);
    }

    if (Sky == 2)
    {
        // A faint band of the galaxy along a great circle, a little blue.
        float band = exp(-pow(dot(direction, normalize(float3(0.3f, 0.25f, 0.92f))) / 0.18f, 2.0f));
        float3 light = float3(0.010f, 0.011f, 0.016f) * band + 0.0008f;
        if (smooth)
        {
            return light + 0.002f;
        }

        // A few thousand bright stars and more faint ones, as a night sky away from towns.
        float stars = StarsOf(direction, 160.0f, 0.012f, 0.0006f) * 4.0f + StarsOf(direction, 420.0f, 0.004f, 0.0004f);
        return light + stars * float3(0.95f, 0.97f, 1.0f);
    }

    return Sky == 3 ? 1.0f : 0.0f;
}

uint SkyReflectionAt(uint level)
{
    switch (level)
    {
        case 0: return SkyReflection0;
        case 1: return SkyReflection1;
        case 2: return SkyReflection2;
        case 3: return SkyReflection3;
        case 4: return SkyReflection4;
        default: return SkyReflection5;
    }
}

// The sky's light reflected at a roughness, from the two pictures of the nearest roughnesses.
float3 SkyReflected(float3 direction, float rough)
{
    float level = saturate(rough) * (SkyReflectionLevels - 1);
    uint low = (uint)floor(level);
    uint high = min(low + 1, SkyReflectionLevels - 1);
    float2 place = SkyPlaceOf(direction);
    uint lowMap = SkyReflectionAt(low);
    uint highMap = SkyReflectionAt(high);
    float3 a = meshTextures[NonUniformResourceIndex(lowMap)].SampleLevel(sampleType, place, 0).rgb;
    float3 b = meshTextures[NonUniformResourceIndex(highMap)].SampleLevel(sampleType, place, 0).rgb;
    return lerp(a, b, level - low);
}

// The share of the light a surface mirrors from all round at a roughness and a view, as fitted by Karis ("Physically
// Based Shading on Mobile", 2014): x times its Fresnel at head-on, plus y.
float2 SkyMirrored(float rough, float nv)
{
    const float4 c0 = float4(-1.0f, -0.0275f, -0.572f, 0.022f);
    const float4 c1 = float4(1.0f, 0.0425f, 1.04f, -0.04f);
    float4 r = rough * c0 + c1;
    float a004 = min(r.x * r.x, exp2(-9.28f * nv)) * r.x + r.y;
    return float2(-1.04f, 1.04f) * a004 + r.zw;
}

// What one light gives a physical surface, its direction and strength at the point already found.
float3 Reflected(float3 base, float metal, float rough, float3 n, float3 toEye, float3 toLight, float3 radiance)
{
    float nl = saturate(dot(n, toLight));
    if (nl <= 0.0f)
    {
        return 0.0f;
    }

    float3 h = normalize(toLight + toEye);
    float nh = saturate(dot(n, h));
    float nv = max(dot(n, toEye), 1e-4f);
    float vh = saturate(dot(toEye, h));
    float a = max(rough * rough, 2e-3f);
    float a2 = a * a;
    float d = nh * nh * (a2 - 1.0f) + 1.0f;
    float distribution = a2 / (Pi * d * d);
    float k = (rough + 1.0f) * (rough + 1.0f) / 8.0f;
    float geometry = nv / (nv * (1.0f - k) + k) * (nl / (nl * (1.0f - k) + k));
    float3 f0 = lerp(0.04f, base, metal);
    float3 fresnel = f0 + (1.0f - f0) * pow(1.0f - vh, 5.0f);
    float3 specular = distribution * geometry * fresnel / (4.0f * nv * nl + 1e-4f);
    float3 diffuse = (1.0f - fresnel) * (1.0f - metal) * base / Pi;
    // Scaled by pi so a light of strength 1 on a white wall gives what the simple model gives.
    return (diffuse + specular) * Pi * radiance * nl;
}

float3 ShadePhysical(float3 base, float metal, float rough, float occlusion, float3 n, float3 position, float2 pixel)
{
    float3 toEye = normalize(-position);
    float nv = max(dot(n, toEye), 1e-4f);
    float3 f0 = lerp(0.04f, base, metal);
    // The ambient light comes from all round: the diffuse part, and the share a rough or a grazing surface mirrors.
    float3 mirrored = f0 + (max(1.0f - rough, f0) - f0) * pow(1.0f - nv, 5.0f);
    float3 lit;
    if (Sky != 0)
    {
        // From the sky: what a matt surface gathers round its normal, and what it mirrors along its reflection.
        float3 normal = mul(float4(n, 0.0f), WorldFromView).xyz;
        float3 reflection = mul(float4(reflect(-toEye, n), 0.0f), WorldFromView).xyz;
        float3 gathered = meshTextures[NonUniformResourceIndex(SkyIrradiance)].SampleLevel(sampleType,
            SkyPlaceOf(normalize(normal)), 0).rgb;
        // What one bounce mirrors, and what the rough surface's further bounces give back of what it misses (Fdez-Aguera,
        // "A Multiple-Scattering Microfacet Model for Real-Time Image-based Lighting", 2019): a white surface sends
        // back all it gets, the rest of the light left to the diffuse part.
        float2 share = SkyMirrored(rough, nv);
        float3 once = f0 * share.x + share.y;
        float missed = 1.0f - (share.x + share.y);
        float3 average = f0 + (1.0f - f0) / 21.0f;
        float3 mirroredAll = once + once * average / (1.0f - missed * average) * missed;
        float3 specular = SkyReflected(normalize(reflection), rough) * mirroredAll;
        lit = (base * (1.0f - metal) * (1.0f - mirroredAll) * gathered + specular) * SkyStrength * occlusion;
    }
    else
    {
        lit = (base * (1.0f - metal) * (1.0f - mirrored) + mirrored) * Ambient * occlusion;
    }
    float shadow = ShadowAt(position, n);
    for (uint i = 0; i < DirectionalCount; i++)
    {
        lit += Reflected(base, metal, rough, n, toEye, -Lights[i].directionCosInner.xyz, Lights[i].colorCosOuter.rgb)
               * (i == ShadowLight ? shadow : 1.0f);
    }

    uint cluster = ClusterAt(pixel, position.z);
    uint count = ClusterCounts[cluster];
    for (uint k = 0; k < count; k++)
    {
        ClusterLight light = Lights[ClusterLights[cluster * MaxLightsPerCluster + k]];
        float3 toLight = light.positionRange.xyz - position;
        float distance = length(toLight);
        float3 l = toLight / max(distance, 1e-4f);
        float reach = saturate(1.0f - (distance * distance) / (light.positionRange.w * light.positionRange.w));
        float fade = reach * reach;
        if (light.kind == LightKind.Spot)
        {
            fade *= smoothstep(light.colorCosOuter.w, light.directionCosInner.w, dot(-l, light.directionCosInner.xyz));
        }

        lit += Reflected(base, metal, rough, n, toEye, l, light.colorCosOuter.rgb * fade);
    }

    return lit;
}

// The normal facing the eye, bent by the normal map when there is one: the map's frame is found from how the position
// and the texture coordinates change across the pixel, so a mesh needs no tangents of its own and a mirrored map still
// bends the right way.
float3 NormalAt(LIT_PIXEL input, MeshInstance copy)
{
    float3 toEye = normalize(-input.viewPosition);
    float3 n = dot(input.viewNormal, input.viewNormal) > 1e-8f ? normalize(input.viewNormal) : toEye;
    if (dot(n, toEye) < 0.0f)
    {
        n = -n;
    }

    if (copy.maps.y == NoMap)
    {
        return n;
    }

    float3 bent = MapOf(copy.maps.y, input.uv, float4(0.5f, 0.5f, 1.0f, 1.0f)).xyz * 2.0f - 1.0f;
    bent.xy *= copy.surface.z;
    float3 dp1 = ddx(input.viewPosition);
    float3 dp2 = ddy(input.viewPosition);
    float2 duv1 = ddx(input.uv);
    float2 duv2 = ddy(input.uv);
    float3 dp2perp = cross(dp2, n);
    float3 dp1perp = cross(n, dp1);
    float3 t = dp2perp * duv1.x + dp1perp * duv2.x;
    float3 b = dp2perp * duv1.y + dp1perp * duv2.y;
    float scale = rsqrt(max(max(dot(t, t), dot(b, b)), 1e-20f));
    return normalize(t * scale * bent.x - b * scale * bent.y + n * bent.z);
}

// A physical pixel lit, in linear light, its alpha with it.
float4 LitPhysical(LIT_PIXEL input, float4 base)
{
    MeshInstance copy = Instances[input.instance];
    float4 metalRough = MapOf(copy.maps.x, input.uv, float4(1.0f, 1.0f, 1.0f, 1.0f));
    float occlusion = lerp(1.0f, MapOf(copy.maps.z, input.uv, float4(1.0f, 1.0f, 1.0f, 1.0f)).r, copy.surface.w);
    float3 lit = ShadePhysical(Linear(base.rgb), saturate(copy.surface.x * metalRough.b),
        saturate(copy.surface.y * metalRough.g), occlusion, NormalAt(input, copy), input.viewPosition, input.position.xy);
    return float4(lit + EmissionOf(input), base.a);
}

MeshInstance InstanceOf(uint instanceId)
{
    return Instances[InstanceBase + instanceId];
}

// A vertex of a copy, at a position and with a normal already in the mesh's own space.
LIT_PIXEL LitOf(MeshInstance instance, uint at, float4 position, float3 normal, float2 uv)
{
    LIT_PIXEL output;
    float4 placed = instance.Placed(position);
    output.position = mul(placed, viewProjection);
    output.viewPosition = mul(placed, view).xyz;
    output.viewNormal = mul(instance.Turned(normal), (float3x3)view);
    output.uv = uv;
    output.albedo = instance.albedo;
    output.texture = instance.slots.z;
    output.alphaCutoff = instance.alphaCutoff;
    output.instance = at;
    return output;
}

[shader("vertex")]
LIT_PIXEL Lit_VS(MESH_VERTEX input, uint instanceId : SV_InstanceID)
{
    return LitOf(InstanceOf(instanceId), InstanceBase + instanceId, float4(input.position.xyz, 1.0f), input.normal,
        input.uv0);
}

#include "Skinning.fxh"

// The vertex goes where its joints carry it first, then the copy places it.
[shader("vertex")]
LIT_PIXEL LitSkinned_VS(SKINNED_VERTEX input, uint instanceId : SV_InstanceID)
{
    MeshInstance instance = InstanceOf(instanceId);
    float4x4 skin = SkinOf(Palettes + instance.slots.x, instance.slots.y, input.jointIndices,
        input.jointWeights);
    return LitOf(instance, InstanceBase + instanceId, mul(float4(input.position.xyz, 1.0f), skin),
        mul(input.normal, (float3x3)skin), input.uv0);
}

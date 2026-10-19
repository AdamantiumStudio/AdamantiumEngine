// Skinning shared by the effects that draw a skinned mesh: the skin's joints and how a vertex follows them.

// The most joints one skinned draw takes; a vertex naming one past them is held at the last.
#define MAX_JOINTS 128

// Each joint of the skin being drawn, as it carries the mesh in the mesh's own space: inverse bind, joint, back. A buffer
// of its own, so only skinned passes upload it.
cbuffer Skinning
{
    float4x4 joints[MAX_JOINTS];
};

struct SKINNED_VERTEX
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
    float4 jointIndices : BLENDINDICES;
    float4 jointWeights : BLENDWEIGHT;
};

// The joints' matrices blended by the vertex's weights; a vertex weighed to none stays where it is.
float4x4 SkinOf(float4 indices, float4 weights)
{
    float total = weights.x + weights.y + weights.z + weights.w;
    if (total < 1e-6f)
    {
        return float4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
    }

    uint4 at = min((uint4)indices, (uint4)(MAX_JOINTS - 1));
    return (joints[at.x] * weights.x + joints[at.y] * weights.y + joints[at.z] * weights.z + joints[at.w] * weights.w) / total;
}

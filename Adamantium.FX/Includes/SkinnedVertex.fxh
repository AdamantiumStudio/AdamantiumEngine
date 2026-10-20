// A skinned mesh's vertex and how the joints it names are blended, shared by the effects that draw one.

// The most joints one skinned draw takes; a vertex naming one past them is held at the last.
#define MAX_JOINTS 128

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

// Which joints a vertex names, held within MAX_JOINTS.
uint4 JointSlots(float4 indices)
{
    return (uint4)clamp(indices, 0, MAX_JOINTS - 1);
}

// The four joints' matrices blended by the vertex's weights, a weight below zero taken as none; a vertex weighed to none
// stays where it is.
float4x4 BlendJoints(float4x4 a, float4x4 b, float4x4 c, float4x4 d, float4 weights)
{
    weights = max(weights, 0);
    float total = weights.x + weights.y + weights.z + weights.w;
    if (total < 1e-6f)
    {
        return float4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
    }

    return (a * weights.x + b * weights.y + c * weights.z + d * weights.w) / total;
}

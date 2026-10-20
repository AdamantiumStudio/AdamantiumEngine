// Skinning for the effects that draw one skinned mesh at a time: the skin's joints in a buffer of their own.

#include "SkinnedVertex.fxh"

// Each joint of the skin being drawn, as it carries the mesh in the mesh's own space: inverse bind, joint, back. A buffer
// of its own, so only skinned passes upload it.
cbuffer Skinning
{
    float4x4 joints[MAX_JOINTS];
};

// The joints' matrices blended by the vertex's weights (see BlendJoints).
float4x4 SkinOf(float4 indices, float4 weights)
{
    uint4 at = JointSlots(indices);
    return BlendJoints(joints[at.x], joints[at.y], joints[at.z], joints[at.w], weights);
}

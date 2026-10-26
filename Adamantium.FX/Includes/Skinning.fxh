// How the joints a skinned vertex names bend it, shared by the effects that draw one; a skin's palette of joints is
// read through a pointer.

#include "SkinnedVertex.fxh"

static const float4x4 Unbent = float4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);

// A joint of a palette of `count`; one past them stays where it is.
float4x4 JointOf(float4x4* palette, uint count, uint at)
{
    return at < count ? palette[at] : Unbent;
}

// The four joints a vertex names, from a palette of `count`, blended by its weights, a weight below zero taken as none;
// a vertex weighed to none stays where it is.
float4x4 SkinOf(float4x4* palette, uint count, float4 indices, float4 weights)
{
    uint4 at = (uint4)max(indices, 0);
    weights = max(weights, 0);
    float total = weights.x + weights.y + weights.z + weights.w;
    if (total < 1e-6f)
    {
        return Unbent;
    }

    return (JointOf(palette, count, at.x) * weights.x + JointOf(palette, count, at.y) * weights.y
        + JointOf(palette, count, at.z) * weights.z + JointOf(palette, count, at.w) * weights.w) / total;
}

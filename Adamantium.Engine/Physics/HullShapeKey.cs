using Adamantium.Mathematics;

namespace Adamantium.Engine.Physics;

/// <summary>A hull, how it is carried, rounded, and how round its edges are: the bodies of a game that share these share
/// one physics shape.</summary>
internal readonly record struct HullShapeKey(ConvexHull Hull, Matrix4x4F Carry, float ConvexRadius);

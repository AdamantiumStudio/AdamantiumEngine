using Adamantium.Graphics.Core;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Physics;

/// <summary>A hull or a mesh at a version and topology of its own, how it is carried, rounded, and how round its edges
/// are: the bodies of a game that share these share one physics shape.</summary>
internal readonly record struct CarriedShapeKey(object Source, int Version, PrimitiveType Topology, Matrix4x4F Carry,
    float ConvexRadius);

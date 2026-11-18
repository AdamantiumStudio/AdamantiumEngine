using Adamantium.Mathematics;

namespace Adamantium.Engine.Physics;

/// <summary>A collider's hull at a scale: the bodies of a game that share both share one physics shape.</summary>
internal readonly record struct HullShapeKey(ConvexHull Hull, Vector3F Scale);

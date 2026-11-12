namespace Adamantium.Mathematics.Triangulation;

/// <summary>Which points of two areas <see cref="GeometryOverlay.Combine"/> keeps.</summary>
public enum AreaCombineMode
{
    /// <summary>What is in either area.</summary>
    Union = 0,

    /// <summary>What is in one area and not the other.</summary>
    Xor = 1,

    /// <summary>What is in both areas.</summary>
    Intersect = 2,

    /// <summary>What is in the first area and not the second.</summary>
    Exclude = 3
}

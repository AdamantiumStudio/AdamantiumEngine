namespace Adamantium.ECS;

/// <summary>A sky a scene can have, made by the engine itself.</summary>
public enum SkyKind
{
    /// <summary>No sky: the frame's clear color behind the scene, its ambient light on it.</summary>
    None,

    /// <summary>A soft studio: light from above, a pale horizon, a darker floor below.</summary>
    Studio,

    /// <summary>Space: black, with the galaxy across it and its stars scattered round.</summary>
    Stars,

    /// <summary>The same light from every side, as under an overcast.</summary>
    Even
}

namespace Adamantium.ECS.Components;

/// <summary>How a material's surface is lit.</summary>
public enum LightingModel
{
    /// <summary>Diffuse light and a highlight from the surface's color alone: cheap, and needs nothing prepared.</summary>
    Simple,

    /// <summary>Metalness and roughness, lit by how much light the surface really sends back; reads the normal, occlusion
    /// and metal-and-roughness maps.</summary>
    Physical
}
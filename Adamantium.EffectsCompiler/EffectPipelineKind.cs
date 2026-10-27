namespace Adamantium.EffectsCompiler;

/// <summary>The kind of pipeline a pass runs, told by its stages.</summary>
internal enum EffectPipelineKind
{
    /// <summary>Vertex, hull and domain, pixel shaders.</summary>
    Graphics,

    /// <summary>Task, mesh and pixel shaders.</summary>
    Mesh,

    /// <summary>A compute shader.</summary>
    Compute,

    /// <summary>Ray generation, miss and callable shaders, and hit groups.</summary>
    RayTracing
}

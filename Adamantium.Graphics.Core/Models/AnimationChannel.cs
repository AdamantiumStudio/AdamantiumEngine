namespace Adamantium.Graphics.Core.Models;

/// <summary>One channel of a <see cref="SceneData.AnimationSource"/>: its target, and its curve's times and values as
/// the file has them - a cubic spline's values carry an in-tangent, a value and an out-tangent for each key.</summary>
public class AnimationChannel
{
    /// <summary>The ID of the node it moves; null for a channel aimed by <see cref="Pointer"/> alone.</summary>
    public string Node { get; set; }

    /// <summary>What of the node it moves: translation, rotation, scale, weights - or pointer.</summary>
    public string Path { get; set; }

    /// <summary>The JSON pointer to the property it moves (KHR_animation_pointer); null otherwise.</summary>
    public string Pointer { get; set; }

    /// <summary>LINEAR, STEP or CUBICSPLINE.</summary>
    public string Interpolation { get; set; }

    public float[] Times { get; set; } = [];

    public float[] Values { get; set; } = [];

    /// <summary>How many numbers make one value: 3 for a translation, 4 for a rotation, a node's morph target count
    /// for weights.</summary>
    public int Components { get; set; }

    public ExtraData Extra { get; set; }

    public ExtraData TargetExtra { get; set; }

    public ExtraData SamplerExtra { get; set; }
}

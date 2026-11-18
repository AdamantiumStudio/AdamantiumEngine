using Adamantium.DarkMatter;

namespace Adamantium.Engine.Physics;

/// <summary>A body for the physics thread to add, or to put in place of the one under the same key.</summary>
internal sealed class BodyRequest
{
    public BodyRequest(BodyDescription description, int generation, string name, bool keepsPose)
    {
        Description = description;
        Generation = generation;
        Name = name;
        KeepsPose = keepsPose;
    }

    public BodyDescription Description { get; }

    /// <summary>Which body under its key this is, so motions of the one before it are not taken for its own.</summary>
    public int Generation { get; }

    /// <summary>The entity's name, for the physics thread's log.</summary>
    public string Name { get; }

    /// <summary>Whether a body put in place of another stands where that one stood, not where the description says.</summary>
    public bool KeepsPose { get; }
}

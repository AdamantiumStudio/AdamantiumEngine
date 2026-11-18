using Adamantium.DarkMatter;

namespace Adamantium.Engine.Physics;

/// <summary>A body for the physics thread to add, to put in place of the one under the same key, or to change.</summary>
internal sealed class BodyRequest
{
    public BodyRequest(BodyDescription description, int generation, int addedAs, int number, string name)
    {
        Description = description;
        Generation = generation;
        AddedAs = addedAs;
        Number = number;
        Name = name;
    }

    /// <summary>Its number among the requests sent for the body, which a refusal of it names.</summary>
    public int Number { get; }

    public BodyDescription Description { get; }

    /// <summary>Which body under its key this is, so motions sent before it are not taken for its own.</summary>
    public int Generation { get; }

    /// <summary>The generation the body was added with, which a refusal of it names.</summary>
    public int AddedAs { get; }

    /// <summary>The entity's name, for the physics thread's log.</summary>
    public string Name { get; }

    /// <summary>Whether a body put in place of another stands where that one stood, not where the description says.
    /// </summary>
    public bool KeepsPose { get; init; }

    /// <summary>Whether a body changed takes the description's kind and settings.</summary>
    public bool ChangesSettings { get; init; }

    /// <summary>Whether a body changed takes the description's shape.</summary>
    public bool ChangesShape { get; init; }

    /// <summary>Whether a body changed is put where the description says.</summary>
    public bool Moves { get; init; }
}

namespace Adamantium.DarkMatter;

/// <summary>What became of a touch of two parts.</summary>
public enum ContactChange
{
    /// <summary>They began to touch: a trigger was entered.</summary>
    Began,

    /// <summary>They stopped touching - parted, or one was taken out or changed: a trigger was left.</summary>
    Ended
}

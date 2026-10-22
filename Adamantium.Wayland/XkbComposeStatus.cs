namespace Adamantium.Wayland;

/// <summary>Where a compose sequence (a dead key, the Compose key) stands after a key.</summary>
public enum XkbComposeStatus
{
    /// <summary>No sequence: the key types its own text.</summary>
    None,

    /// <summary>A sequence goes on: the key types nothing yet.</summary>
    Composing,

    /// <summary>The sequence is done and gives its text.</summary>
    Composed,

    /// <summary>The key did not continue the sequence, which is dropped with the key's text.</summary>
    Cancelled,
}

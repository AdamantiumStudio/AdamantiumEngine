namespace Adamantium.Multiverse.Remote;

/// <summary>What a message on a <see cref="RemoteLink"/> is.</summary>
internal enum RemoteMessageKind : byte
{
    /// <summary>A method called, its answer waited for.</summary>
    Call,

    /// <summary>A method called, nothing waited for.</summary>
    Notice,

    /// <summary>A call's answer: what it returned, if anything.</summary>
    Result,

    /// <summary>A call failed: why.</summary>
    Failure,

    /// <summary>A call was called off before it was done.</summary>
    Canceled,

    /// <summary>The caller calls a call off.</summary>
    CallOff
}

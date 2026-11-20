namespace Adamantium.Multiverse.Remote;

/// <summary>How a universe host shares its frames with its viewer.</summary>
public enum FrameSharing
{
    /// <summary>GPU memory through handles where the viewer is on the same GPU and the host's device exports them - on
    /// Windows, for now - else memory of the process.</summary>
    Auto,

    /// <summary>GPU memory, its handles given to the viewer's process: the fastest, on the same GPU only.</summary>
    GpuHandles,

    /// <summary>Memory shared through a file, which the host's GPU writes the frames into: for another GPU, or a driver
    /// that shares no GPU memory between processes.</summary>
    HostMemory
}

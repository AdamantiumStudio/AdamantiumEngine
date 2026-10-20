using System;

namespace Adamantium.Graphics.Core;

/// <summary>Draw commands recorded once into a secondary command buffer and executed inside the device's render pass on
/// later frames, instead of being issued again. Owned by whoever recorded it; disposing it frees it once the frames that
/// may execute it are done.</summary>
public interface IRecordedCommands : IDisposable
{
    /// <summary>The frame slot it was recorded for: what it reads per frame in flight is that slot's.</summary>
    uint Frame { get; }

    /// <summary>Whether the device can still execute it as recorded: its shaders have not been reloaded and its target
    /// formats are the ones it was recorded against. Whether the recorder would issue the same draws is the recorder's to
    /// say.</summary>
    bool IsReusable { get; }
}

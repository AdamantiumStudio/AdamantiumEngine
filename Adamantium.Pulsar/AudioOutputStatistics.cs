namespace Adamantium.Pulsar;

/// <summary>What happened to an output since it was started.</summary>
/// <param name="FramesRendered">The frames asked of the renderer.</param>
/// <param name="Gaps">How many times the device had played everything before more came: each is heard.</param>
/// <param name="LongestBetweenFills">The longest the device went without being given more.</param>
/// <param name="LongestRender">The longest the renderer took.</param>
public readonly record struct AudioOutputStatistics(long FramesRendered, long Gaps, TimeSpan LongestBetweenFills,
    TimeSpan LongestRender);

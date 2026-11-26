using System;

namespace Adamantium.EngineTests;

/// <summary>A clock that stands still until the test moves it.</summary>
public class ManualClock : TimeProvider
{
    private long ticks = 1;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        return ticks;
    }

    public void Advance(TimeSpan time)
    {
        ticks += time.Ticks;
    }
}

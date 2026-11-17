using System;

namespace Adamantium.DarkMatter;

/// <summary>Turns passing time into fixed steps, and the rest into <see cref="Blend"/> for drawing between states.
/// Falling behind, it gives at most <see cref="MaxStepsPerAdvance"/> and drops the rest, so the world slows down.</summary>
public sealed class StepClock
{
    private double _accumulated;

    public StepClock(int stepRate, int maxStepsPerAdvance = 8)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stepRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxStepsPerAdvance);
        StepSeconds = 1.0 / stepRate;
        MaxStepsPerAdvance = maxStepsPerAdvance;
    }

    /// <summary>The length of one step, in seconds.</summary>
    public double StepSeconds { get; }

    /// <summary>The most steps one <see cref="Advance"/> gives.</summary>
    public int MaxStepsPerAdvance { get; }

    /// <summary>How far time has gone past the last step, as a share of a step: 0 at the last step, nearly 1 just
    /// before the next.</summary>
    public float Blend => (float)(_accumulated / StepSeconds);

    /// <summary>How much time the last <see cref="Advance"/> dropped for falling behind, in seconds; zero mostly.
    /// </summary>
    public double DroppedSeconds { get; private set; }

    /// <summary>Adds <paramref name="elapsedSeconds"/> and gives how many steps are due.</summary>
    public int Advance(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds), elapsedSeconds,
                "The time passed must be a finite number of seconds, not negative.");

        _accumulated += elapsedSeconds;
        DroppedSeconds = 0;
        var steps = (int)Math.Min(Math.Floor(_accumulated / StepSeconds), int.MaxValue);
        if (steps > MaxStepsPerAdvance)
        {
            DroppedSeconds = _accumulated - MaxStepsPerAdvance * StepSeconds;
            _accumulated = 0;
            return MaxStepsPerAdvance;
        }

        _accumulated = Math.Max(0, _accumulated - steps * StepSeconds);
        return steps;
    }

    /// <summary>Forgets the time not yet stepped.</summary>
    public void Reset()
    {
        _accumulated = 0;
        DroppedSeconds = 0;
    }
}

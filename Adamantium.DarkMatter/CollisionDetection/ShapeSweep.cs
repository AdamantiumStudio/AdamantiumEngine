using Adamantium.DarkMatter.Shapes;

namespace Adamantium.DarkMatter.CollisionDetection;

/// <summary>Casts one convex shape at another by conservative advancement on the GJK distance (Box2D v3, Jolt): it
/// meets the other within <see cref="MeetingReach"/> and stops where it first came within <see cref="StoppingGap"/>.
/// </summary>
internal static class ShapeSweep
{
    /// <summary>The gap, in meters, a moving shape stops within.</summary>
    public const float StoppingGap = 0.001f;

    /// <summary>The gap, in meters, the steps aim at to meet the other.</summary>
    public const float MeetingGap = 0.0005f;

    /// <summary>The gap, in meters, a shape must come within to meet another.</summary>
    public const float MeetingReach = MeetingGap + 0.00002f;

    /// <summary>How much earlier, in meters along the way, a meeting may be than one with the same body and be passed by.
    /// </summary>
    public const float MeetTolerance = (StoppingGap - MeetingGap) / 2;

    // Just inside the stopping gap, so a stopped shape keeps well clear of the meeting gap.
    private const float StopAimGap = StoppingGap - MeetTolerance / 2;

    // The most steps toward the stop, and again from it toward the meeting.
    private const int MaxIterations = 32;

    // Above rounding in the way toward the other: the least closing, as a share of the way's length, a shape starting
    // within the meeting gap must have to meet at once.
    private const float ClosingNoise = 0.000001f;

    /// <summary>Whether <paramref name="first"/>, moved by <paramref name="translation"/>, meets
    /// <paramref name="second"/> within <paramref name="limit"/> of it, and where it stops and meets.</summary>
    public static bool TryCast(CollisionShape first, in LocalPose firstPose, Vector3F translation, CollisionShape second,
        in LocalPose secondPose, float limit, ManifoldBuffer buffer, out ShapeMeeting meeting)
    {
        meeting = default;
        var phase = SweepPhase.Approaching;
        var length = translation.Length();
        var secondRadius = second.CoreRadius;
        var radii = first.CoreRadius + secondRadius;
        var secondCore = new PlacedCore(second, secondPose);
        var firstCore = new PlacedCore(first, firstPose);
        // Each step's walk starts from the last step's way: the shape barely moved against the other.
        var guess = Vector3F.Zero;
        var at = 0f;
        var steps = 0;
        while (true)
        {
            var position = firstPose.Position + translation * at;
            var reach = (limit - at) * length + radii + StoppingGap;
            if (!GjkClosestPoints.TryFind(firstCore.MovedTo(position), secondCore, guess, reach, buffer.Simplex,
                    out var distance, out var towardSecond, out _, out var onSecond)
                || distance - radii < 0)
            {
                meeting.Meet = at;
                if (phase != SweepPhase.Approaching)
                    return true;

                meeting.Stop = at;
                return Overlap(first, new LocalPose(position, firstPose.Rotation), second, secondPose, translation,
                    -towardSecond, onSecond, buffer, ref meeting);
            }

            if (distance > reach)
                return false;

            var gap = distance - radii;
            var closing = Vector3F.Dot(translation, towardSecond);
            if (phase != SweepPhase.Stopped && gap <= StoppingGap
                || phase == SweepPhase.Approaching && steps == MaxIterations)
            {
                // Out of steps short of the stopping gap, it stops where it stands rather than pass through.
                phase = gap <= StoppingGap ? SweepPhase.Stopped : SweepPhase.StoppedShort;
                steps = 0;
                meeting.Stop = at;
                meeting.Normal = -towardSecond;
                meeting.Point = onSecond + meeting.Normal * secondRadius;
            }

            // Within the meeting gap it meets - at the start only when not moving, or moving toward it.
            if (gap <= MeetingReach)
            {
                meeting.Meet = at;
                return at > 0 || length == 0 || closing > ClosingNoise * length;
            }

            // Out of steps once stopped, it meets where it stands, never passed through.
            if (steps == MaxIterations)
            {
                meeting.Meet = at;
                return true;
            }

            // The distance along a straight way only grows once it stops shrinking.
            if (!(closing > 0))
                return false;

            var aimsToMeet = phase == SweepPhase.Stopped;
            var next = at + (gap - (aimsToMeet ? MeetingGap : StopAimGap)) / closing;
            if (next > limit)
            {
                // The way ends short of the meeting it aims at: met only if it ends within the meeting gap.
                if (!aimsToMeet || at >= limit)
                    return false;

                next = limit;
            }

            at = next;
            steps++;
            guess = towardSecond;
        }
    }

    // Already overlapping: the normal and the second's deepest point as a contact tells them, else GJK's last way.
    private static bool Overlap(CollisionShape first, in LocalPose firstPose, CollisionShape second, in LocalPose secondPose,
        Vector3F translation, Vector3F walkedNormal, Vector3F nearestOfSecond, ManifoldBuffer buffer,
        ref ShapeMeeting meeting)
    {
        buffer.Clear();
        if (ConvexCollisions.Collide(first, firstPose, second, secondPose, 0, buffer) && buffer.Count > 0)
        {
            // A contact point lies halfway between the surfaces; the second's is half the depth along the normal.
            var deepest = buffer.FindDeepest();
            meeting.Normal = -buffer.Normal;
            meeting.Point = buffer.Positions[deepest] + buffer.Normal * (buffer.Separations[deepest] * 0.5f);
            return true;
        }

        meeting.Normal = walkedNormal != Vector3F.Zero ? walkedNormal
            : translation.LengthSquared() > 0 ? -Vector3F.Normalize(translation) : Vector3F.UnitY;
        meeting.Point = nearestOfSecond + meeting.Normal * second.CoreRadius;
        return true;
    }
}

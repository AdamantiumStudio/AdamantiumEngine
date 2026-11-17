namespace Adamantium.DarkMatter;

/// <summary>What the last step of a world did, for instruments.</summary>
public readonly struct StepStatistics
{
    public StepStatistics(int awakeBodies, int pairs, int touchingPairs, int contactPoints, double milliseconds)
    {
        AwakeBodies = awakeBodies;
        Pairs = pairs;
        TouchingPairs = touchingPairs;
        ContactPoints = contactPoints;
        Milliseconds = milliseconds;
    }

    /// <summary>How many dynamic and kinematic bodies were awake.</summary>
    public int AwakeBodies { get; }

    /// <summary>How many pairs of bodies stood close enough to be tested.</summary>
    public int Pairs { get; }

    /// <summary>How many of them held contact points: touching, or about to.</summary>
    public int TouchingPairs { get; }

    /// <summary>How many contact points all of them held.</summary>
    public int ContactPoints { get; }

    /// <summary>How long the step took.</summary>
    public double Milliseconds { get; }
}

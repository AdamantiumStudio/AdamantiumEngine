namespace Adamantium.DarkMatter.Dynamics;

/// <summary>Which pass over the joints a substep makes.</summary>
internal enum JointPass
{
    /// <summary>The impulses of the last substep given again.</summary>
    WarmStart,

    /// <summary>The soft spring closes the joints' errors.</summary>
    Push,

    /// <summary>Only the speeds along the rows are taken out, the spring's included.</summary>
    Relax
}

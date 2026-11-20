namespace Adamantium.DarkMatter;

/// <summary>A surface's triangle a cast may meet, in the surface's own axes, and the least share of the cast's
/// translation at which it could meet it.</summary>
internal struct CastTriangle
{
    public Vector3F First;
    public Vector3F Second;
    public Vector3F Third;
    public Vector3F Normal;
    public float Reach;
}

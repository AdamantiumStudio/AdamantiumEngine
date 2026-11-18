using System.Collections.Generic;

namespace Adamantium.DarkMatter.Shapes;

/// <summary>A flat face of a hull while it is built: its corners in order round it and its outward normal.</summary>
internal readonly struct HullFace
{
    public HullFace(List<int> corners, Vector3F normal)
    {
        Corners = corners;
        Normal = normal;
    }

    public List<int> Corners { get; }

    public Vector3F Normal { get; }
}

using System.Collections.Generic;

namespace Adamantium.DarkMatter.Shapes;

/// <summary>A flat face of a hull while it is built: its corners in order round it, every corner its triangles touch -
/// those on a straight run of its outline too - and its outward normal.</summary>
internal readonly struct HullFace
{
    public HullFace(List<int> corners, List<int> touching, Vector3F normal)
    {
        Corners = corners;
        Touching = touching;
        Normal = normal;
    }

    public List<int> Corners { get; }

    public List<int> Touching { get; }

    public Vector3F Normal { get; }
}

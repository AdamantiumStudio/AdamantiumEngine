using Adamantium.Mathematics;

namespace Adamantium.ECS.Components;

public class TransformMetaData
{
    public TransformMetaData()
    {
        Enabled = true;
        Scale = Vector3F.One;
        AbsoluteWorld = Matrix4x4F.Identity;
        WorldMatrixF = Matrix4x4F.Identity;
        WorldMatrix = Matrix4x4<double>.Identity;
    }

    public CameraBase Camera { get; set; }

    // The inputs the cached world matrix below was last computed from. TransformService recomputes only when one of
    // these (or the owner transform's IsWorldDirty) changed, so a static entity under a rotating camera is skipped
    // (the world matrix depends on camera POSITION, not its rotation - rotation lives in the per-frame view matrix).
    public bool Computed { get; set; }
    public Vector3<double> LastCameraPosition { get; set; }

    public Vector3F RelativePosition { get; set; }

    // The camera-INDEPENDENT world (local * parent), used to compose this node's children. WorldMatrixF below is this
    // shifted by the camera position for rendering (a no-op while the camera sits at the origin).
    public Matrix4x4F AbsoluteWorld { get; set; }

    public Matrix4x4F WorldMatrixF { get; set; }
        
    public Matrix4x4<double> WorldMatrix { get; set; }

    public QuaternionF Rotation { get; set; }

    public Vector3F Scale { get; set; }

    public Vector3F Pivot { get; set; }

    public bool Enabled { get; set; }

    public bool IsSelected { get; set; }

    public static TransformMetaData New()
    {
        return new TransformMetaData();
    }
}
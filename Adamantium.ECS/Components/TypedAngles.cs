using Adamantium.Mathematics;

namespace Adamantium.ECS.Components;

/// <summary>A turn in degrees about X, Y and Z, given back as it was typed (200 stays 200) until the turn changes.
/// Kept in a field that is not readonly: <see cref="Read"/> remembers in place.</summary>
internal struct TypedAngles
{
    private Vector3F angles;
    private QuaternionF anglesTurn;

    public Vector3F Read(QuaternionF turn)
    {
        if (turn != anglesTurn)
        {
            turn.ToYawPitchRoll(out var yaw, out var pitch, out var roll);
            angles = new Vector3F(float.RadiansToDegrees(pitch), float.RadiansToDegrees(yaw),
                float.RadiansToDegrees(roll));
            anglesTurn = turn;
        }

        return angles;
    }

    public QuaternionF Take(Vector3F value)
    {
        var turn = Quaternion.CreateFromYawPitchRoll<float>(float.DegreesToRadians(value.Y),
            float.DegreesToRadians(value.X), float.DegreesToRadians(value.Z));
        angles = value;
        anglesTurn = turn;
        return turn;
    }
}

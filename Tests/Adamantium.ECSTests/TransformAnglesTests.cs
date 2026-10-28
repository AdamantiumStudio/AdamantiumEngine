using System;
using System.Collections.Generic;
using Adamantium.ECS.Components;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.ECSTests;

/// <summary>A turn typed in degrees: the inspector edits <see cref="Transform.Angles"/>, not the quaternion.</summary>
[TestFixture]
public class TransformAnglesTests
{
    [Test]
    public void AnyTurn_ComesBackAsTheAnglesItWasMadeFrom()
    {
        var random = new Random(7);
        for (var i = 0; i < 500; i++)
        {
            var yaw = (float)(random.NextDouble() * 2 - 1) * MathF.PI * 0.99f;
            var pitch = (float)(random.NextDouble() * 2 - 1) * MathF.PI / 2 * 0.99f;
            var roll = (float)(random.NextDouble() * 2 - 1) * MathF.PI * 0.99f;

            Quaternion.CreateFromYawPitchRoll<float>(yaw, pitch, roll).ToYawPitchRoll(out var y, out var p, out var r);

            Assert.That(new[] { y, p, r }, Is.EqualTo(new[] { yaw, pitch, roll }).Within(1e-3f), $"turn {i}");
        }
    }

    [TestCase(1f)]
    [TestCase(-1f)]
    public void LookingStraightUpOrDown_GivesATurnThatIsTheSame(float side)
    {
        var turn = Quaternion.CreateFromYawPitchRoll<float>(0.7f, side * MathF.PI / 2, 0.3f);

        turn.ToYawPitchRoll(out var yaw, out var pitch, out var roll);
        var again = Quaternion.CreateFromYawPitchRoll<float>(yaw, pitch, roll);

        Assert.That(Math.Abs(QuaternionF.Dot(turn, again)), Is.EqualTo(1f).Within(1e-4f));
    }

    [TestCase(89.8f)]
    [TestCase(-89.9f)]
    [TestCase(89.99f)]
    public void NearlyStraightUpOrDown_TheAnglesStillMakeTheSameTurn(float pitchDegrees)
    {
        var turn = Quaternion.CreateFromYawPitchRoll<float>(float.DegreesToRadians(30f), float.DegreesToRadians(pitchDegrees),
            float.DegreesToRadians(20f));

        turn.ToYawPitchRoll(out var yaw, out var pitch, out var roll);
        var again = Quaternion.CreateFromYawPitchRoll<float>(yaw, pitch, roll);

        Assert.That(Math.Abs(QuaternionF.Dot(turn, again)), Is.EqualTo(1f).Within(1e-6f));
    }

    [Test]
    public void AnglesTyped_AreGivenBackAsTyped_AndTurnTheEntity()
    {
        var transform = new Transform();

        transform.Angles = new Vector3F(200, 0, 0);

        var expected = Quaternion.CreateFromYawPitchRoll<float>(0, float.DegreesToRadians(200f), 0);
        Assert.Multiple(() =>
        {
            Assert.That(transform.Angles, Is.EqualTo(new Vector3F(200, 0, 0)));
            Assert.That(transform.Rotation, Is.EqualTo(expected));
        });
    }

    [Test]
    public void TurnedByAnythingElse_TheAnglesFollow_AndSaySo()
    {
        var transform = new Transform { Angles = new Vector3F(200, 0, 0) };
        var changed = new List<string>();
        transform.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        transform.Rotation = Quaternion.CreateFromYawPitchRoll<float>(float.DegreesToRadians(30f), 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(transform.Angles.X, Is.EqualTo(0).Within(1e-3f));
            Assert.That(transform.Angles.Y, Is.EqualTo(30).Within(1e-3f));
            Assert.That(transform.Angles.Z, Is.EqualTo(0).Within(1e-3f));
            Assert.That(changed, Does.Contain(nameof(Transform.Angles)));
        });
    }
}

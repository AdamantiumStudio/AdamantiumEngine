using System;
using Adamantium.Mathematics;
using NUnit.Framework;
using QuaternionD = Adamantium.Mathematics.Quaternion<double>;
using Vector3D = Adamantium.Mathematics.Vector3<double>;

namespace Adamantium.MathTests;

[TestFixture]
public class TypedQuaternionTests
{
    [Test]
    public void YawPitchRoll_AtStraightDown_RebuildsTheRotation()
    {
        var turn = new QuaternionF(BitConverter.Int32BitsToSingle(unchecked((int)0xBE1B9709)),
            BitConverter.Int32BitsToSingle(0x3F30CA71), BitConverter.Int32BitsToSingle(0x3F30CA71),
            BitConverter.Int32BitsToSingle(0x3E1B9709));

        turn.ToYawPitchRoll(out var yaw, out var pitch, out var roll);

        Assert.That(pitch, Is.EqualTo(-MathF.PI / 2).Within(1e-6f));
        Assert.That(roll, Is.EqualTo(0f));
        Assert.That(Math.Abs(QuaternionF.Dot(Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll), turn)), Is.GreaterThan(1 - 1e-6));
    }

    [TestCase(0.0)]
    [TestCase(1e-7)]
    [TestCase(1e-5)]
    [TestCase(1e-4)]
    [TestCase(1e-3)]
    [TestCase(0.3)]
    public void YawPitchRoll_NearThePoles_RebuildsTheRotation(double fromPole)
    {
        var random = new Random(5);
        for (var i = 0; i < 2000; i++)
        {
            var pitch = (i % 2 == 0 ? 1 : -1) * (Math.PI / 2 - fromPole);
            var (yaw, roll) = ((random.NextDouble() * 2 - 1) * Math.PI, (random.NextDouble() * 2 - 1) * Math.PI);
            var single = Quaternion.CreateFromYawPitchRoll((float)yaw, (float)pitch, (float)roll);
            var precise = Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll);

            single.ToYawPitchRoll(out var y, out var p, out var r);
            precise.ToYawPitchRoll(out var yd, out var pd, out var rd);

            Assert.That(Math.Abs(QuaternionF.Dot(Quaternion.CreateFromYawPitchRoll(y, p, r), single)), Is.GreaterThan(1 - 1e-6),
                $"float {yaw}, {pitch}, {roll}");
            Assert.That(Math.Abs(QuaternionD.Dot(Quaternion.CreateFromYawPitchRoll(yd, pd, rd), precise)), Is.GreaterThan(1 - 1e-14),
                $"double {yaw}, {pitch}, {roll}");
        }
    }

    [Test]
    public void YawPitchRoll_OfExtremeLengths_IsTheSameAsOfUnitOnes()
    {
        new QuaternionD(0, 0, 0, 1e200).ToYawPitchRoll(out var yaw, out var pitch, out var roll);
        Assert.That((yaw, pitch, roll), Is.EqualTo((0.0, 0.0, 0.0)));

        new QuaternionD(1e-200, 0, 0, 1e-200).ToYawPitchRoll(out _, out pitch, out _);
        Assert.That(pitch, Is.EqualTo(Math.PI / 2).Within(1e-15));
    }

    [Test]
    public void Logarithm_OfAFloatTurnRoundedToMinusOne_KeepsTheAxis()
    {
        var turn = new QuaternionF(2e-4f, 0, 0, -1);

        var back = QuaternionF.Exponential(QuaternionF.Logarithm(turn));

        Assert.That(Math.Abs(QuaternionF.Dot(back, turn)), Is.GreaterThan(1 - 1e-6));
        Assert.That(back.X, Is.EqualTo(turn.X).Within(1e-6f));
    }

    [Test]
    public void SquadSetup_ChoosesSignsAlongTheChain()
    {
        static QuaternionD Turn(double angle) => Quaternion.CreateFromAxisAngle(Vector3D.UnitY, angle);

        var canonical = QuaternionD.SquadSetup(Turn(-0.5), Turn(0), Turn(0.5), Turn(1.0));
        var flipped = QuaternionD.SquadSetup(Turn(-0.5), Turn(0), -Turn(0.5), -Turn(1.0));

        Assert.That(Math.Abs(QuaternionD.Dot(flipped.End, canonical.End)), Is.GreaterThan(1 - 1e-12));
    }

    [Test]
    public void Exponential_OfHalfATurn_IsMinusOne()
    {
        var result = QuaternionD.Exponential(new QuaternionD(0, 0, Math.PI, 0));

        Assert.That(QuaternionD.NearEqual(result, new QuaternionD(0, 0, 0, -1)), Is.True);
    }

    [TestCase(0.0, 0.0)]
    [TestCase(0.5, -0.5)]
    public void Barycentric_OfWeightsAddingToZero_IsTheFirstRotation(double amount1, double amount2)
    {
        var first = Quaternion.CreateFromAxisAngle(Vector3D.UnitY, 0.4);

        var result = QuaternionD.Barycentric(first, QuaternionD.Identity, Quaternion.CreateFromAxisAngle(Vector3D.UnitX, 1.0),
            amount1, amount2);

        Assert.That(QuaternionD.NearEqual(result, first), Is.True);
    }

    [TestCase(2.5e-4f)]
    [TestCase(4e-4f)]
    [TestCase(1e-3f)]
    public void Logarithm_NextToMinusOne_KeepsTheAngle(float x)
    {
        var turn = QuaternionF.Normalize(new QuaternionF(x, 0, 0, -1));

        var back = QuaternionF.Exponential(QuaternionF.Logarithm(turn));

        Assert.That(Math.Abs(QuaternionF.Dot(back, turn)), Is.GreaterThan(1 - 1e-6));
    }

    [Test]
    public void SquadSetup_OfEqualKeys_KeepsThem()
    {
        var turn = Quaternion.CreateFromAxisAngle(Vector3.Create(0.3, 0.5, 0.8), 1.2);

        var (start, end, to) = QuaternionD.SquadSetup(turn, turn, turn, turn);

        Assert.That(QuaternionD.NearEqual(start, turn) && QuaternionD.NearEqual(end, turn) && QuaternionD.NearEqual(to, turn), Is.True);
        Assert.That(QuaternionD.NearEqual(QuaternionD.Squad(turn, start, end, to, 0.5), turn), Is.True);
    }

    [Test]
    public void Squad_OfEvenTurnsAroundOneAxis_PassesTheMiddleHalfway()
    {
        var keys = new QuaternionD[4];
        for (var i = 0; i < 4; i++)
        {
            keys[i] = Quaternion.CreateFromAxisAngle(Vector3D.UnitZ, 0.5 * i);
        }

        var (start, end, to) = QuaternionD.SquadSetup(keys[0], keys[1], keys[2], keys[3]);
        var middle = QuaternionD.Squad(keys[1], start, end, to, 0.5);

        Assert.That(QuaternionD.NearEqual(middle, Quaternion.CreateFromAxisAngle(Vector3D.UnitZ, 0.75)), Is.True);
    }

    [Test]
    public void Facing_LeavesTheTargetsHeightOut()
    {
        var facing = Quaternion.CreateFacing(Vector3D.Zero, Vector3.Create(0.0, 1.0, -1.0), Vector3D.Up);

        Assert.That(Vector3D.NearEqual(Vector3D.Transform(Vector3D.ForwardRH, facing), Vector3D.ForwardRH), Is.True);
    }
}

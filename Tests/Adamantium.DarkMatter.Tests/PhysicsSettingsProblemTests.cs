using System;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.DarkMatter.Tests;

/// <summary>Settings no simulation takes say which setting as a value, each its own, with an English sentence for a log;
/// a simulation refuses them with that sentence.</summary>
[TestFixture]
public class PhysicsSettingsProblemTests
{
    private static readonly object[] Refused =
    [
        new object[] { new PhysicsSettings { StepRate = 0 }, PhysicsSettingsProblem.NoSteps },
        new object[] { new PhysicsSettings { Substeps = 0 }, PhysicsSettingsProblem.NoSubsteps },
        new object[] { new PhysicsSettings { MaxContactPointsPerPair = 99 }, PhysicsSettingsProblem.ContactPointsOutOfRange },
        new object[] { new PhysicsSettings { MaxStepsPerAdvance = 0 }, PhysicsSettingsProblem.NoStepsPerAdvance },
        new object[]
        {
            new PhysicsSettings { Gravity = new Vector3F(0, float.NaN, 0) }, PhysicsSettingsProblem.GravityNotFinite
        },
        new object[] { new PhysicsSettings { MaxLinearSpeed = 0 }, PhysicsSettingsProblem.SpeedLimitOutOfRange },
        new object[] { new PhysicsSettings { MaxLinearSpeed = 1e30f }, PhysicsSettingsProblem.SpeedLimitOutOfRange },
        new object[] { new PhysicsSettings { ContactHertz = -1 }, PhysicsSettingsProblem.ContactOrSleepOutOfRange },
        new object[] { new PhysicsSettings { SleepSpeed = float.NaN }, PhysicsSettingsProblem.ContactOrSleepOutOfRange },
        new object[] { new PhysicsSettings { TimeToSleep = -1 }, PhysicsSettingsProblem.TimeToSleepOutOfRange },
        new object[] { new PhysicsSettings { TimeToSleep = float.NaN }, PhysicsSettingsProblem.TimeToSleepOutOfRange },
    ];

    [TestCaseSource(nameof(Refused))]
    public void SettingsNoSimulationTakes_SayWhich_AndASimulationRefusesThemSayingSo(PhysicsSettings settings,
        PhysicsSettingsProblem problem)
    {
        var refusal = Assert.Throws<ArgumentOutOfRangeException>(() => new PhysicsSimulation(settings).Dispose());

        Assert.Multiple(() =>
        {
            Assert.That(settings.FindProblem(), Is.EqualTo(problem));
            Assert.That(problem.Explained(), Is.Not.Null.And.Not.Empty);
            Assert.That(refusal.Message, Does.StartWith(problem.Explained()));
        });
    }

    [Test]
    public void SettingsASimulationTakes_HaveNoProblem()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new PhysicsSettings().FindProblem(), Is.EqualTo(PhysicsSettingsProblem.None));
            Assert.That(new PhysicsSettings { TimeToSleep = float.PositiveInfinity }.FindProblem(),
                Is.EqualTo(PhysicsSettingsProblem.None), "bodies that never sleep");
            Assert.That(PhysicsSettingsProblem.None.Explained(), Is.Null);
        });
    }
}

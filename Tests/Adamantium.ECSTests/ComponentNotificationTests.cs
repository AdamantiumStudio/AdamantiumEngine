using System;
using System.Collections.Generic;
using System.ComponentModel;
using Adamantium.ECS.Components;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.ECSTests;

/// <summary>What an inspector shows of a component says when it changes, so the inspector follows whoever changes it.</summary>
[TestFixture]
public class ComponentNotificationTests
{
    private static IEnumerable<TestCaseData> Changes()
    {
        yield return Case(new Material(), m => m.DiffuseColor = new Vector4F(1, 0, 0, 1), nameof(Material.DiffuseColor));
        yield return Case(new Material(), m => m.Shininess = 12, nameof(Material.Shininess));
        yield return Case(new Material(), m => m.Reflectivity = 0.5f, nameof(Material.Reflectivity));
        yield return Case(new Material(), m => m.TexturePath = "a.png", nameof(Material.TexturePath));
        yield return Case(new AnimationComponent(), a => a.AnimationSpeed = 2, nameof(AnimationComponent.AnimationSpeed));
        yield return Case(new AnimationComponent(), a => a.IsPaused = true, nameof(AnimationComponent.IsPaused));
        yield return Case(new AnimationComponent(), a => a.StartingKeyFrame = 3, nameof(AnimationComponent.StartingKeyFrame));
        yield return Case(new AnimationComponent(), a => a.EndingKeyFrame = 9, nameof(AnimationComponent.EndingKeyFrame));
        yield return Case(new AnimationController(), c => c.ControllerId = "rig", nameof(AnimationController.ControllerId));
        yield return Case(new Camera(60, 800, 600, 0.1f, 1000f), c => c.Velocity = 7, nameof(CameraBase.Velocity));
    }

    private static TestCaseData Case<T>(T component, Action<T> change, string property) where T : INotifyPropertyChanged =>
        new TestCaseData((INotifyPropertyChanged)component, (Action<INotifyPropertyChanged>)(c => change((T)c)), property)
            .SetName($"{typeof(T).Name}.{property}");

    [TestCaseSource(nameof(Changes))]
    public void AnInspectedProperty_SaysWhenItChanges(INotifyPropertyChanged component, Action<INotifyPropertyChanged> change,
        string property)
    {
        var said = new List<string>();
        component.PropertyChanged += (_, e) => said.Add(e.PropertyName);

        change(component);

        Assert.That(said, Does.Contain(property));
    }

    [Test]
    public void AMaterial_StillStartsWithAWhiteBoundingBox()
    {
        Assert.That(new Material().BoundingBoxColor, Is.EqualTo(Colors.White.ToVector4()));
    }
}

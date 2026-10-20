using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace Adamantium.MVVM.Tests;

// Bindings reach a view model's properties through what the generator wrote - the [Bindable] properties, the commands
// and the hand-written properties - and what no description holds through reflection, said so once per type. A
// reflected accessor's delegates are bound to a PropertyInfo or a FieldInfo; a generated one's are not.
[TestFixture]
public class PropertyAccessorsTests
{
    [Test]
    public void AViewModel_IsReached_ThroughItsGeneratedDescription()
    {
        var person = new PersonViewModel();
        var firstName = PropertyAccessors.Find(typeof(PersonViewModel), nameof(PersonViewModel.FirstName));
        var fullName = PropertyAccessors.Find(typeof(PersonViewModel), nameof(PersonViewModel.FullName));
        var save = PropertyAccessors.Find(typeof(PersonViewModel), nameof(PersonViewModel.SaveCommand));
        var title = PropertyAccessors.Find(typeof(PartialPropertyViewModel), nameof(PartialPropertyViewModel.Title));

        firstName.Set(person, "Ada");
        person.LastName = "Lovelace";

        Assert.Multiple(() =>
        {
            Assert.That(firstName.ValueType, Is.EqualTo(typeof(string)));
            Assert.That(firstName.Get(person), Is.EqualTo("Ada"));
            Assert.That(person.LastChangedProperty, Is.EqualTo(nameof(PersonViewModel.FirstName)));
            Assert.That(fullName.Get(person), Is.EqualTo("Ada Lovelace"));
            Assert.That(fullName.CanWrite, Is.False);
            Assert.That(save.Get(person), Is.SameAs(person.SaveCommand));
            Assert.That(save.CanWrite, Is.False);
            Assert.That(IsGenerated(firstName), Is.True);
            Assert.That(IsGenerated(fullName), Is.True);
            Assert.That(IsGenerated(save), Is.True);
            Assert.That(IsGenerated(title), Is.True);
        });
    }

    [Test]
    public void AHandWrittenNotifyingClass_IsDescribed_KeywordsAndWriteOnlyIncluded()
    {
        var count = PropertyAccessors.Find(typeof(HandWritten), nameof(HandWritten.Count));
        var created = PropertyAccessors.Find(typeof(HandWritten), nameof(HandWritten.Created));
        var keyword = PropertyAccessors.Find(typeof(HandWritten), "class");
        var writeOnly = PropertyAccessors.Find(typeof(HandWritten), nameof(HandWritten.WriteOnly));
        var item = new HandWritten { Count = 3 };
        count.Set(item, null);
        keyword.Set(item, 7);
        writeOnly.Set(item, "set");
        created.Set(item, "later");

        Assert.Multiple(() =>
        {
            Assert.That(item.Count, Is.Zero, "a value type takes nothing as its default");
            Assert.That(item.Created, Is.EqualTo("later"), "an init setter is written, as reflection writes it");
            Assert.That(IsGenerated(created), Is.True);
            Assert.That(item.@class, Is.EqualTo(7));
            Assert.That(writeOnly.Get, Is.Null);
            Assert.That(item.Written, Is.EqualTo("set"));
            Assert.That(IsGenerated(count), Is.True);
            Assert.That(IsGenerated(keyword), Is.True);
            Assert.That(IsGenerated(writeOnly), Is.True);
        });
    }

    [Test]
    public void ABaseThatAsksForIt_IsDescribed_WithEveryDescendant()
    {
        var button = new DescribedButton { Width = 12, Label = "OK" };
        var label = PropertyAccessors.Find(typeof(DescribedButton), nameof(DescribedButton.Label));
        var width = PropertyAccessors.Find(typeof(DescribedButton), nameof(DescribedButton.Width));

        Assert.Multiple(() =>
        {
            Assert.That(label.Get(button), Is.EqualTo("OK"));
            Assert.That(width.Get(button), Is.EqualTo(12d), "found on the base");
            Assert.That(IsGenerated(label), Is.True);
            Assert.That(IsGenerated(width), Is.True);
        });
    }

    [Test]
    public void AClassDescribedOnlyForItsCommand_StillHasItsOtherProperties()
    {
        var host = new CommandOnlyHost { Name = "x" };
        var run = PropertyAccessors.Find(typeof(CommandOnlyHost), nameof(CommandOnlyHost.RunCommand));
        var name = PropertyAccessors.Find(typeof(CommandOnlyHost), nameof(CommandOnlyHost.Name));

        Assert.Multiple(() =>
        {
            Assert.That(IsGenerated(run), Is.True);
            Assert.That(name?.Get(host), Is.EqualTo("x"), "found through reflection on the described level");
        });
    }

    [Test]
    public void ATypeNothingDescribes_IsReachedThroughReflection_AndSaysSoOnce()
    {
        List<Type> reflected = [];
        Action<Type> onReflected = reflected.Add;
        PropertyAccessors.ReachedThroughReflection += onReflected;
        try
        {
            var size = PropertyAccessors.Find(typeof(Plain), nameof(Plain.Size));
            var again = PropertyAccessors.Find(typeof(Plain), nameof(Plain.Size));
            var stamp = PropertyAccessors.Find(typeof(Plain), nameof(Plain.Stamp));
            var plain = new Plain();
            size.Set(plain, 4);

            Assert.Multiple(() =>
            {
                Assert.That(again, Is.SameAs(size));
                Assert.That(plain.Size, Is.EqualTo(4));
                Assert.That(stamp.CanWrite, Is.True, "an init setter is written through reflection too");
                Assert.That(PropertyAccessors.Find(typeof(Plain), "Missing"), Is.Null);
                Assert.That(IsGenerated(size), Is.False);
                Assert.That(reflected, Is.EqualTo((Type[])[typeof(Plain)]));
            });
        }
        finally
        {
            PropertyAccessors.ReachedThroughReflection -= onReflected;
        }
    }

    [Test]
    public void ASubscriberThatThrows_DoesNotBreakTheLookup()
    {
        Action<Type> throwing = _ => throw new InvalidOperationException();
        PropertyAccessors.ReachedThroughReflection += throwing;
        try
        {
            Assert.That(PropertyAccessors.Find(typeof(Lone), nameof(Lone.Value)), Is.Not.Null);
        }
        finally
        {
            PropertyAccessors.ReachedThroughReflection -= throwing;
        }
    }

    public sealed class Plain
    {
        public int Size { get; set; }

        public string Stamp { get; init; }
    }

    public sealed class Lone
    {
        public int Value { get; set; }
    }

    private static bool IsGenerated(PropertyAccessor accessor) =>
        accessor.Get?.Target is not MemberInfo && accessor.Set?.Target is not MemberInfo;
}

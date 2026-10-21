using System.Runtime.InteropServices;
using Adamantium.Wayland;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>
/// The generated Wayland bindings without a compositor: the structures libwayland reads have its layout, and the
/// tables carry what wayland-scanner would have written - libwayland trusts both, and a wrong one is a crash or a
/// protocol error, not an exception.
/// </summary>
[TestFixture]
public unsafe class WaylandTablesTests
{
    [Test]
    public void Structures_HaveTheLayoutOfLibwayland()
    {
        Assert.That(sizeof(WlInterface), Is.EqualTo(40));
        Assert.That(sizeof(WlMessage), Is.EqualTo(24));
        Assert.That(sizeof(WlArgument), Is.EqualTo(8));
        Assert.That(sizeof(WlArray), Is.EqualTo(24));
    }

    [Test]
    public void UntypedNewId_TravelsAsNameVersionAndId()
    {
        var bind = Request(WaylandInterfaces.WlRegistry, "bind");

        Assert.That(Signature(bind), Is.EqualTo("usun"));
        for (var i = 0; i < 4; i++)
        {
            Assert.That((nint)bind->types[i], Is.EqualTo((nint)0), $"type slot {i}");
        }
    }

    [Test]
    public void Signatures_CarryTheVersionAndNullability()
    {
        Assert.That(Signature(Request(WaylandInterfaces.WlSurface, "attach")), Is.EqualTo("?oii"));
        Assert.That(Signature(Request(WaylandInterfaces.WlSurface, "set_buffer_transform")), Is.EqualTo("2i"));
        Assert.That(Signature(Request(WaylandInterfaces.WlSurface, "damage_buffer")), Is.EqualTo("4iiii"));
    }

    [Test]
    public void ObjectArguments_PointAtTheirInterfaces_AcrossProtocols()
    {
        var get = Request(WaylandInterfaces.XdgWmBase, "get_xdg_surface");

        Assert.That((nint)get->types[0], Is.EqualTo((nint)WaylandInterfaces.XdgSurface));
        Assert.That((nint)get->types[1], Is.EqualTo((nint)WaylandInterfaces.WlSurface));
    }

    [Test]
    public void Tables_NameTheInterfaceAndItsVersion()
    {
        Assert.That(Marshal.PtrToStringUTF8((nint)WaylandInterfaces.XdgToplevel->name), Is.EqualTo(XdgToplevel.InterfaceName));
        Assert.That(WaylandInterfaces.XdgToplevel->version, Is.EqualTo((int)XdgToplevel.InterfaceVersion));
        Assert.That(XdgToplevel.Interface == WaylandInterfaces.XdgToplevel, Is.True);
    }

    [Test]
    public void Names_ThatWouldStartWithADigit_TakeTheEnumsName()
    {
        Assert.That((uint)WlOutput.Transform.Transform90, Is.EqualTo(1));
        Assert.That((uint)WlOutput.Transform.Flipped90, Is.EqualTo(5));
    }

    [Test]
    public void AnEventBesideAnEnumOfItsName_TakesASuffix()
    {
        Assert.That(typeof(WlDisplay).GetEvent("ErrorEvent"), Is.Not.Null);
        Assert.That(typeof(WlDisplay).GetNestedType("Error"), Is.Not.Null);
    }

    [Test]
    public void Fixed_IsTwentyFourEight()
    {
        Assert.That(WlFixed.FromDouble(1.5), Is.EqualTo(384));
        Assert.That(WlFixed.FromDouble(-2.25), Is.EqualTo(-576));
        Assert.That(WlFixed.ToDouble(384), Is.EqualTo(1.5));
    }

    private static WlMessage* Request(WlInterface* @interface, string name)
    {
        for (var i = 0; i < @interface->method_count; i++)
        {
            if (Marshal.PtrToStringUTF8((nint)@interface->methods[i].name) == name)
            {
                return &@interface->methods[i];
            }
        }

        throw new AssertionException($"{name} is not a request of {Marshal.PtrToStringUTF8((nint)@interface->name)}");
    }

    private static string? Signature(WlMessage* message) => Marshal.PtrToStringUTF8((nint)message->signature);
}

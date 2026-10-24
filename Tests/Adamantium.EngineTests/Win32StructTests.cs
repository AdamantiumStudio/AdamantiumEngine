using System.Runtime.InteropServices;
using Adamantium.Win32;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>Structs handed to Windows by size: a wrong size makes the call fail or write past the struct.</summary>
[TestFixture]
public class Win32StructTests
{
    [Test]
    public void TheDisplayConfigurationStructs_AreTheSizesWindowsExpects()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Marshal.SizeOf<DISPLAYCONFIG_PATH_INFO>(), Is.EqualTo(72));
            Assert.That(Marshal.OffsetOf<DISPLAYCONFIG_PATH_INFO>(nameof(DISPLAYCONFIG_PATH_INFO.RefreshRateNumerator)).ToInt32(),
                Is.EqualTo(48));
            Assert.That(Marshal.SizeOf<DISPLAYCONFIG_MODE_INFO>(), Is.EqualTo(64));
            Assert.That(Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(), Is.EqualTo(84));
        });
    }

    [Test]
    public void AMonitorsInfo_IsTheSizeWindowsExpects()
    {
        Assert.That(Marshal.SizeOf<MONITORINFOEX>(), Is.EqualTo(104));
    }
}

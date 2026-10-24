using System;
using System.Collections.Generic;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests
{
    /// <summary>One Vulkan instance and device for the whole assembly, since repeated instances made the loader reject new
    /// ones mid-run. Each test still gets its own render device.</summary>
    [SetUpFixture]
    public class GpuFixture
    {
        public static MainGraphicsDevice Main { get; private set; }

        private static readonly List<IGraphicsDevice> PerTest = new();

        [OneTimeSetUp]
        public void CreateSharedDevice()
        {
            Main = MainGraphicsDevice.Create(new GraphicsDeviceFactory(), Settings(), "TestApp", true);
        }

        /// <summary>The default settings with the run parameter <c>DescriptorModel</c>, if given.</summary>
        public static GraphicsSettings Settings()
        {
            var settings = GraphicsSettings.CreateDefault();
            if (Enum.TryParse<DescriptorModel>(TestContext.Parameters.Get("DescriptorModel", ""), out var model))
            {
                settings.DescriptorModel = model;
            }

            return settings;
        }

        [OneTimeTearDown]
        public void DisposeSharedDevice()
        {
            ReleaseRenderDevices();
            Main?.Dispose();
            Main = null;
        }

        /// <summary>A render device for the current test, tracked so the fixture can release it however the test ends -
        /// including on a failed assertion, which throws before any cleanup line the test itself might carry.</summary>
        public static IGraphicsDevice CreateRenderDevice()
        {
            var device = Main.CreateRenderDevice();
            PerTest.Add(device);
            return device;
        }

        /// <summary>Releases one render device before the test ends - a window that closes while others stay.</summary>
        public static void ReleaseRenderDevice(IGraphicsDevice device)
        {
            Main.DeviceWaitIdle();
            Main.RemoveDevice(device);
            PerTest.Remove(device);
        }

        /// <summary>Called from each fixture's TearDown. RemoveDevice both unregisters and disposes, so the shared main
        /// device is not left holding a device the next test knows nothing about.</summary>
        public static void ReleaseRenderDevices()
        {
            if (Main == null) { PerTest.Clear(); return; }

            Main.DeviceWaitIdle();
            foreach (var device in PerTest) Main.RemoveDevice(device);
            PerTest.Clear();
        }
    }
}

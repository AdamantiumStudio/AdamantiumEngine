using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using Adamantium.Imaging;

namespace Adamantium.Engine.GraphicsTests
{
    [TestFixture]
    public class ImageTests
    {
        /// <summary>Where the sample images live, found by walking up from the test binary. Every path here used to be
        /// absolute - rooted on a drive letter from the machine they were written on - so the whole fixture failed with
        /// DirectoryNotFoundException anywhere else, including here.</summary>
        private static readonly string Assets = FindAssets();

        private static string FindAssets()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "Tests", "TestAssets");
                if (Directory.Exists(candidate)) return candidate + Path.DirectorySeparatorChar;
            }

            throw new DirectoryNotFoundException("Tests/TestAssets not found above " + AppContext.BaseDirectory);
        }

        [Test]
        public void DDSImageTest()
        {
            var img = Image.Load(Assets + @"balls.dds");
            img.Save("loaded.dds", ImageFileType.Dds);
            img.Dispose();

            img = Image.Load(Assets + @"TextureCube.dds");
            img.Save("TextureCube.dds", ImageFileType.Dds);
            img.Dispose();
        }

        [Test]
        public void TGAImageTest()
        {
            var img = Image.Load(Assets + @"2RLEExpand.tga");
            img.Save("2RLEExpand_reconstructed.tga", ImageFileType.Tga);
            img.Dispose();

            img = Image.Load(Assets + @"luxfon.tga");
            img.Save("luxfon_reconstructed.tga", ImageFileType.Tga);
            img.Dispose();
        }

        /// <summary>A 24-bit JPEG round trip returns the same picture within tolerance, corners included, where a shifted
        /// extraction shows first.</summary>
        [Test]
        public void Jpeg24BitRoundTrip_KeepsThePicture()
        {
            using var source = Image.Load(Assets + @"AplhaTestBitmap_24.bmp");
            source.Save("roundtrip24.jpg", ImageFileType.Jpg);

            using var decoded = Image.Load("roundtrip24.jpg");
            var a = source.PixelBuffer[0];
            var b = decoded.PixelBuffer[0];

            Assert.That(b.Width, Is.EqualTo(a.Width), "width survived the encode");
            Assert.That(b.Height, Is.EqualTo(a.Height), "height survived the encode");

            var w = (int)a.Width;
            var h = (int)a.Height;
            (int X, int Y)[] probes =
            [
                (0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1), (w / 2, h / 2), (w / 4, h / 3)
            ];

            foreach (var (x, y) in probes)
            {
                var expected = a.GetPixel<Adamantium.Mathematics.Color>(x, y);
                var actual = b.GetPixel<Adamantium.Mathematics.Color>(x, y);
                Assert.That(actual.R, Is.EqualTo(expected.R).Within(12), $"R at {x},{y}");
                Assert.That(actual.G, Is.EqualTo(expected.G).Within(12), $"G at {x},{y}");
                Assert.That(actual.B, Is.EqualTo(expected.B).Within(12), $"B at {x},{y}");
            }
        }

        [Test]
        public void BitmapImageTest()
        {
            //var img = Image.Load(Assets + @"AplhaTestBitmap.bmp");
            var img = Image.Load(Assets + @"AplhaTestBitmap_24.bmp");
            //var img = Image.Load(Assets + @"Shapes.bmp");
            //var img = Image.Load(Assets + @"Small_24.bmp");
            //var img = Image.Load(Assets + @"t2_24.bmp");
            img.Save(@"BaseAlbedoTexture_Text.jpg", ImageFileType.Jpg);
            img.Save(@"BaseAlbedoTexture_Text.bmp", ImageFileType.Bmp);
            img?.Dispose();
        }

        [Test]
        public void IcoImageTest()
        {
            //var img = Image.Load(Assets + @"Icons\SharpGen.ico");
            //var img = Image.Load(Assets + @"Icons\Testicon24.ico");
            var img = Image.Load(Path.Combine(Assets, "Icons", "testicon32.ico"));
            //var img = Image.Load(Assets + @"Icons\NewIcon.ico");

            img.Save(@"RestoredBitmap.bmp", ImageFileType.Bmp);
            img.Save(@"RestoredBitmap.jpg", ImageFileType.Jpg);
            img.Save(@"RestoredBitmap.dds", ImageFileType.Dds);
            img?.Dispose();
        }

        [Test]
        public void JpegImageTest()
        {
            //var img = Image.Load(Assets + @"luxfon.jpg");
            var img = Image.Load(Assets + @"testpng1.jpg");
            img?.Save("luxfon.bmp", ImageFileType.Bmp);
            img?.Save("luxfon.tga", ImageFileType.Tga);
            img?.Save("luxfon.jpg", ImageFileType.Jpg);

            //var img = Image.Load(Assets + @"Icons\SharpGen.ico");
            //var img = Image.Load(Assets + @"1.jpg");
            //var img = Image.Load(Assets + @"Icons\SharpGen.ico");
            //var img = Image.Load(Assets + @"Icons\NewIcon.ico");

            //img.Save(@"RestoredBitmap.bmp", ImageFileType.Bmp);
            //img.Save(@"RestoredBitmap.tga", ImageFileType.Tga);
            img?.Dispose();
        }

        [Test]
        public void PngImageTest()
        {
            var img = Image.Load(Path.Combine(Assets, "APNG", "APNG-cube.png"));
            //var img = Image.Load(Assets + @"BaseAlbedoTexture_Text.png");
            //var img = Image.Load(Assets + @"APNG\elephant.png");
            //var img = Image.Load(Assets + @"testpng6.png");

            //var bytes = File.ReadAllBytes(Assets + @"converted2.png");
            //var img = Image.New2D(1920, 1080, 1, SurfaceFormat.R8G8B8A8.UNorm);
            //var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            //Utilities.CopyMemory(img.DataPointer, handle.AddrOfPinnedObject(), bytes.Length);
            //handle.Free();
            var timer = Stopwatch.StartNew();
            img?.Save("test.bmp", ImageFileType.Bmp);
            img?.Save("test.png", ImageFileType.Png);
            timer.Stop();
            //img?.Save("1.tga", ImageFileType.Tga);
            //img?.Save("1.dds", ImageFileType.Dds);
            //img?.Save("1.jpg", ImageFileType.Jpg);


            img?.Dispose();
        }

        [Test]
        public void GIFImageTest()
        {
            //var img = Image.Load(Assets + @"gif\cube.gif");
            //var img = Image.Load(Assets + @"gif\Rotating_earth.gif");
            var img = Image.Load(Path.Combine(Assets, "gif", "infinity.gif"));
            //var img = Image.Load(Assets + @"gif\RotatingEarth2.gif");
            //var img = Image.Load(Assets + @"gif\interlaced.gif");
            //var img = Image.Load(Assets + @"coloredImage.jpg");
            //var img = Image.Load(Assets + @"gif\coloredImage.gif");
            //var img = Image.Load(Assets + @"gif\mygif.gif");
            //var img = Image.Load(Assets + @"testpng1.png");
            //img?.Save("1.bmp", ImageFileType.Bmp);
            //img?.Save("1.tga", ImageFileType.Tga);
            //img?.Save("1.jpg", ImageFileType.Jpg);
            img?.Save("mygif2.gif", ImageFileType.Gif);
            //img?.Save("cube.png", ImageFileType.Png);
            img?.Dispose();
        }
    }
}

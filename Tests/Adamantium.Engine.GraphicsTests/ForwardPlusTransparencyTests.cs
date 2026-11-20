extern alias engine;
using System;
using System.IO;
using Adamantium.Core;
using Adamantium.Core.DependencyInjection;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.Multiverse;
using Adamantium.Vulkan.Core;
using Image = Adamantium.Imaging.Image;
using TextureDimension = Adamantium.Imaging.TextureDimension;
using NUnit.Framework;
using engine::Adamantium.Engine.EntityServices;
using engine::Adamantium.Engine.Templates;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>Materials whose alpha counts, drawn by the Forward+ pass: see-through in any order over what is behind them
/// (weighted blended), masked by their cutoff, or opaque whatever their alpha. Lit by a white ambient light alone, a surface shows its own
/// color, and each cube is seen through both its faces.</summary>
[TestFixture]
public class ForwardPlusTransparencyTests
{
    private const uint Size = 64;
    private const int Frames = 4;
    // How far a channel may be from the one blending gives, in 8-bit levels.
    private const int Within = 6;
    private const uint TexelsSide = 4;

    private Universe _universe;
    private OffscreenOutput _output;
    private RenderingService _rendering;
    private Texture _texture;

    [SetUp]
    public void MakeUniverse()
    {
        var container = new AdamantiumDependencyContainer();
        var registry = (IContainerRegistry)container;
        var host = new HeadlessPlatform();
        registry.RegisterInstance<IOutputFactory>(host);
        registry.RegisterInstance<IWindowingPlatform>(host);
        _universe = new Universe(UniverseMode.Slave, false, container, new SharedDeviceService());
        _universe.InitializeUniverse();
        _universe.FrameMode = FrameMode.Continuous;
        _universe.EntityWorld.CreateService<TransformService>(_universe.EntityWorld);
        _output = new OffscreenOutput(Size, Size);
        _universe.AddOutput(_output);
        RunFrame();
        _rendering = _universe.CreateRenderService<RenderingService>(_output);
        _rendering.AttachProcessor(new ForwardPlusRenderingProcessor { Ambient = Vector3F.One });
        var eye = _output.Camera;
        eye.Owner.Transform.Position = new WorldPosition(0, 0, -6);
        eye.SetFreeLookAt(WorldPosition.Zero);
    }

    [TearDown]
    public void ShutDown()
    {
        _universe.ShutDown();
        _texture?.Dispose();
        GpuFixture.ReleaseRenderDevices();
    }

    // Half of the red goes over the blue at each of the cube's two faces: a quarter of the blue is left.
    [Test]
    public void AHalfTransparentCube_OverAnOpaqueOne_ShowsWhatIsBehind()
    {
        AddCube("Behind", new WorldPosition(0, 0, 2), new Vector4F(0, 0, 1, 1), AlphaMode.Opaque);
        AddCube("Glass", WorldPosition.Zero, new Vector4F(1, 0, 0, 0.5f), AlphaMode.Blend);

        var center = CenterPixel("forward-plus-glass.png");

        Assert.Multiple(() =>
        {
            Assert.That(center.Red, Is.EqualTo(191).Within(Within), "three quarters of the red");
            Assert.That(center.Green, Is.LessThanOrEqualTo(Within));
            Assert.That(center.Blue, Is.EqualTo(64).Within(Within), "a quarter of the blue behind");
        });
    }

    // A texel's alpha times the diffuse color's: half of a whole-alpha texel, as the untextured glass.
    [Test]
    public void ATexturedTransparentCube_TakesItsTexelsAlphaTimesItsColors()
    {
        AddCube("Behind", new WorldPosition(0, 0, 2), new Vector4F(0, 0, 1, 1), AlphaMode.Opaque);
        var glass = AddCube("Glass", WorldPosition.Zero, new Vector4F(1, 1, 1, 0.5f), AlphaMode.Blend);
        _texture = Texture.CreateFrom(_rendering.GraphicsDevice, TexelsDescription(), RedTexels(255));
        glass.GetComponent<Material>().Texture = _texture;

        var center = CenterPixel("forward-plus-textured-glass.png");

        Assert.Multiple(() =>
        {
            Assert.That(center.Red, Is.EqualTo(191).Within(Within), "three quarters of the red");
            Assert.That(center.Blue, Is.EqualTo(64).Within(Within), "a quarter of the blue behind");
        });
    }

    // Met in either order, two see-through cubes give one picture - nothing is sorted - the near red weighing more than
    // the far green, and the four faces covering all but a sixteenth of the black behind.
    [Test]
    public void TwoTransparentCubes_GiveOnePicture_InEitherOrder_TheNearOneWeighingMore()
    {
        var near = AddCube("Near", new WorldPosition(0, 0, -1.5), new Vector4F(1, 0, 0, 0.5f), AlphaMode.Blend);
        var far = AddCube("Far", new WorldPosition(0, 0, 1.5), new Vector4F(0, 1, 0, 0.5f), AlphaMode.Blend);
        var nearFirst = CenterPixel("forward-plus-near-first.png");

        _universe.EntityWorld.EntityManager.RemoveEntity(near);
        _universe.EntityWorld.EntityManager.RemoveEntity(far);
        AddCube("Far", new WorldPosition(0, 0, 1.5), new Vector4F(0, 1, 0, 0.5f), AlphaMode.Blend);
        AddCube("Near", new WorldPosition(0, 0, -1.5), new Vector4F(1, 0, 0, 0.5f), AlphaMode.Blend);
        var farFirst = CenterPixel("forward-plus-far-first.png");

        Assert.Multiple(() =>
        {
            Assert.That(farFirst.Red, Is.EqualTo(nearFirst.Red).Within(2), "red, whichever came first");
            Assert.That(farFirst.Green, Is.EqualTo(nearFirst.Green).Within(2), "green, whichever came first");
            Assert.That(nearFirst.Red, Is.GreaterThan(nearFirst.Green * 2), "the near red weighs more");
            Assert.That(nearFirst.Red + nearFirst.Green, Is.EqualTo(239).Within(Within), "15/16 covered");
        });
    }

    // A masked surface keeps what reaches its cutoff, whole, and leaves out the rest.
    [TestCase(0.7f, 255)]
    [TestCase(0.3f, 0)]
    public void AMaskedCube_IsWholeAboveItsCutoffAndLeftOutBelow(float alpha, int red)
    {
        AddCube("Masked", WorldPosition.Zero, new Vector4F(1, 0, 0, alpha), AlphaMode.Mask);

        var center = CenterPixel("forward-plus-masked.png");

        Assert.That(center.Red, Is.EqualTo(red).Within(Within));
    }

    // A texture's alpha changes across a surface, so its pixels are left out in the shader, by the alpha of the texel.
    [TestCase((byte)180, 255)]
    [TestCase((byte)70, 0)]
    public void AMaskedTexturedCube_IsLeftOutWhereItsTextureIsBelowTheCutoff(byte texelAlpha, int red)
    {
        var cube = AddCube("Masked", WorldPosition.Zero, Vector4F.One, AlphaMode.Mask);
        _texture = Texture.CreateFrom(_rendering.GraphicsDevice, TexelsDescription(), RedTexels(texelAlpha));
        cube.GetComponent<Material>().Texture = _texture;

        var center = CenterPixel("forward-plus-masked-texture.png");

        Assert.That(center.Red, Is.EqualTo(red).Within(Within));
    }

    // A cube round the eye shows its inside faces only when its material shows both; from outside, its front faces show,
    // mirrored or not.
    [TestCase(MaterialFaces.Front, false, false, 0)]
    [TestCase(MaterialFaces.Both, false, false, 255)]
    [TestCase(MaterialFaces.Front, true, false, 255)]
    [TestCase(MaterialFaces.Front, false, true, 0)]
    public void ACubesFaces_AreWhatItsMaterialShows(MaterialFaces faces, bool fromOutside, bool mirrored, int red)
    {
        var cube = AddCube("Box", fromOutside ? WorldPosition.Zero : new WorldPosition(0, 0, -6),
            new Vector4F(1, 0, 0, 1), AlphaMode.Opaque);
        cube.GetComponent<Material>().Faces = faces;
        cube.Transform.ScaleFactor = new Vector3F(mirrored ? -1 : 1, 1, 1) * (fromOutside ? 1 : 4);

        var center = CenterPixel("forward-plus-faces.png");

        Assert.That(center.Red, Is.EqualTo(red).Within(Within));
    }

    [Test]
    public void AnOpaqueCube_IgnoresItsAlpha()
    {
        AddCube("Opaque", WorldPosition.Zero, new Vector4F(1, 0, 0, 0.2f), AlphaMode.Opaque);

        var center = CenterPixel("forward-plus-opaque.png");

        Assert.That(center.Red, Is.EqualTo(255).Within(Within));
    }

    private Entity AddCube(string name, WorldPosition position, Vector4F color, AlphaMode alphaMode)
    {
        var cube = new ShapeTemplate(new CubeShape()).Build(new Entity(null, name));
        cube.Transform.Position = position;
        cube.AddComponent(new Material { DiffuseColor = color, AlphaMode = alphaMode });
        _universe.EntityWorld.EntityManager.AddEntity(cube, _universe.EntityWorld.MainScene);
        return cube;
    }

    private static TextureDescription TexelsDescription() => new()
    {
        Width = TexelsSide,
        Height = TexelsSide,
        Depth = 1,
        ArrayLayers = 1,
        MipLevels = 1,
        Samples = MSAALevel.None,
        Format = Format.R8G8B8A8_UNORM,
        InitialLayout = ImageLayout.Undefined,
        DesiredImageLayout = ImageLayout.ShaderReadOnlyOptimal,
        ImageType = ImageType._2d,
        ImageAspect = ImageAspectFlagBits.ColorBit,
        ImageTiling = ImageTiling.Optimal,
        Usage = ImageUsageFlagBits.SampledBit | ImageUsageFlagBits.TransferDstBit,
        Dimension = TextureDimension.Texture2D
    };

    private static byte[] RedTexels(byte alpha)
    {
        var texels = new byte[TexelsSide * TexelsSide * 4];
        for (var start = 0; start < texels.Length; start += 4)
        {
            texels[start] = 255;
            texels[start + 3] = alpha;
        }

        return texels;
    }

    private ColorAt CenterPixel(string savedAs)
    {
        for (var frame = 0; frame < Frames; frame++)
        {
            RunFrame();
        }

        var pixels = Pixels(_output.ReadFrame(), savedAs);
        var start = ((int)Size / 2 * (int)Size + (int)Size / 2) * 4;
        return new ColorAt(pixels[start], pixels[start + 1], pixels[start + 2]);
    }

    // The input service updates the editor's cameras; this universe has none.
    private void RunFrame()
    {
        _output.Camera?.Update(default);
        _universe.RunOnce(default(AppTime));
        _universe.Submit();
    }

    private static byte[] Pixels(Image picture, string savedAs)
    {
        Assert.That(picture, Is.Not.Null, "the output drew no frame");
        using var stream = new MemoryStream();
        picture.Save(stream, ImageFileType.Png);
        File.WriteAllBytes(Path.Combine(TestContext.CurrentContext.WorkDirectory, savedAs), stream.ToArray());
        stream.Position = 0;
        return BitmapLoader.Load(stream).GetRawPixels(0);
    }

    private readonly record struct ColorAt(byte Red, byte Green, byte Blue);
}

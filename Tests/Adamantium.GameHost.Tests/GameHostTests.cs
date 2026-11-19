using System.Diagnostics;
using Adamantium.Engine.Scenes;
using Adamantium.Imaging;
using NUnit.Framework;

namespace Adamantium.GameHost.Tests;

/// <summary>The game host as the editor starts it: a process of its own, with no window, playing a scene with the game's
/// code loaded by path.</summary>
[TestFixture]
public class GameHostTests
{
    private const string Scene = """
        <?xml version="1.0" encoding="utf-8"?>
        <Scene Version="1" Uid="70000000000000000000000000000000">
          <Lights>
            <Light Uid="70000000000000000000000000000001" Name="Sun" Position="0, -8, -6" Rotation="-0.9659259, 0, 0, 0.25881895" Type="Directional" Color="1, 1, 1" Intensity="1.5" Range="1" InnerSpotAngle="0" OuterSpotAngle="45" Shadows="false" ShadowMapResolution="0" />
          </Lights>
          <Cameras>
            <Camera Uid="70000000000000000000000000000002" Name="Eye" Position="0, 0, -46" />
          </Cameras>
          <Shapes>
            <Shape Uid="70000000000000000000000000000003" Name="Mover" Position="0, 0, -40" Type="Adamantium.ECS.Components.CubeShape" Color="1, 1, 1, 1" Size="6, 6, 6" Tessellation="1" />
          </Shapes>
        </Scene>
        """;

    private string _folder;

    [SetUp]
    public void MakeFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), "adamantium-game-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "Drift.ascene"), Scene.Replace("        ", ""));
    }

    [TearDown]
    public void RemoveFolder() => Directory.Delete(_folder, true);

    [Test]
    public void TheOptions_AreReadAsWritten()
    {
        var options = GameHostOptions.Parse(["--scene", "a.ascene", "--content", "c1", "--content", "c2", "--code", "g.dll",
            "--size", "640x360", "--frames", "5", "--capture", "out.ascene", "--image", "out.png"]);

        Assert.Multiple(() =>
        {
            Assert.That(options.ScenePath, Is.EqualTo(Path.GetFullPath("a.ascene")));
            Assert.That(options.ContentFolders, Is.EqualTo((string[])[Path.GetFullPath("c1"), Path.GetFullPath("c2")]));
            Assert.That(options.CodeAssemblies, Is.EqualTo((string[])[Path.GetFullPath("g.dll")]));
            Assert.That(options.Width, Is.EqualTo(640));
            Assert.That(options.Height, Is.EqualTo(360));
            Assert.That(options.Frames, Is.EqualTo(5));
            Assert.That(options.CapturePath, Is.EqualTo(Path.GetFullPath("out.ascene")));
            Assert.That(options.ImagePath, Is.EqualTo(Path.GetFullPath("out.png")));
        });
    }

    [Test]
    public void OptionsWithoutAScene_OrWithABadSizeOrFrameCount_AreRefused()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => GameHostOptions.Parse(["--frames", "5"]), Throws.ArgumentException);
            Assert.That(() => GameHostOptions.Parse(["--scene", "a.ascene", "--size", "640"]), Throws.ArgumentException);
            Assert.That(() => GameHostOptions.Parse(["--scene", "a.ascene", "--frames", "-1"]), Throws.ArgumentException);
            Assert.That(() => GameHostOptions.Parse(["--scene", "a.ascene", "--frames", "99999999999"]), Throws.ArgumentException);
            Assert.That(() => GameHostOptions.Parse(["--scene", "a.ascene", "--what", "x"]), Throws.ArgumentException);
        });
    }

    // Played for a while, the game's service moved the mover, and the scene's camera - not the universe's own, which looks
    // the other way - drew it in the middle of the frame.
    [Test]
    public void AGamePlayed_MovesWhatItsCodeMoves_AndIsDrawn()
    {
        var capture = Path.Combine(_folder, "after.ascene");
        var image = Path.Combine(_folder, "frame.png");

        var run = RunHost("--scene", Path.Combine(_folder, "Drift.ascene"),
            "--code", Path.Combine(AppContext.BaseDirectory, "Adamantium.GameHost.TestGame.dll"),
            "--size", "160x90", "--frames", "60", "--capture", capture, "--image", image);

        Assert.That(run.ExitCode, Is.EqualTo(0), run.Errors);
        var mover = SceneFile.Read(capture).Shapes.Single(shape => shape.Name == "Mover");
        using var frame = Image.Load(image);
        var pixels = frame.GetPixelBuffer(0, 0).GetPixels<byte>();
        var center = (frame.Description.Height / 2 * frame.Description.Width + frame.Description.Width / 2) * 4;
        Assert.Multiple(() =>
        {
            Assert.That(mover.Position.X, Is.GreaterThan(0), "the game's service moved it");
            Assert.That(pixels[center] + pixels[center + 1] + pixels[center + 2], Is.GreaterThan(60), "the lit cube is drawn there");
        });
    }

    // A scene or code the host cannot load stops the universe it started; the host says why and exits with 1.
    [Test]
    public void AGameThatCannotBeLoaded_StopsTheHost_WithTheReason()
    {
        var noScene = RunHost("--scene", Path.Combine(_folder, "Missing.ascene"), "--frames", "60");
        var noCode = RunHost("--scene", Path.Combine(_folder, "Drift.ascene"), "--code",
            Path.Combine(_folder, "Missing.dll"), "--frames", "60");

        Assert.Multiple(() =>
        {
            Assert.That(noScene.ExitCode, Is.EqualTo(1));
            Assert.That(noScene.Errors, Does.Contain("Missing.ascene"));
            Assert.That(noCode.ExitCode, Is.EqualTo(1));
            Assert.That(noCode.Errors, Does.Contain("Missing.dll"));
        });
    }

    private static HostRun RunHost(params string[] arguments)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Adamantium.GameHost.dll"));
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var host = Process.Start(start)!;
        var output = host.StandardOutput.ReadToEndAsync();
        var errors = host.StandardError.ReadToEndAsync();
        if (!host.WaitForExit(TimeSpan.FromSeconds(60)))
        {
            host.Kill();
            Assert.Fail("The host did not stop by itself.");
        }

        _ = output.Result;
        return new HostRun(host.ExitCode, errors.Result);
    }
}

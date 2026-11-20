using System.Diagnostics;
using Adamantium.Engine.Scenes;
using Adamantium.Imaging;
using Adamantium.Multiverse.Remote;
using NUnit.Framework;
using StreamJsonRpc;

namespace Adamantium.UniverseHost.Tests;

/// <summary>The universe host as the editor starts it: a process of its own, with no window, serving its viewer - the
/// test - which launches a scene with the game's code loaded by path.</summary>
[TestFixture]
public class UniverseHostTests
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

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private string _folder;
    private Process _host;
    private Task<string> _errors;
    private WatchingViewer _viewer;
    private UniverseHostConnection _connection;

    [SetUp]
    public void StartHost()
    {
        _folder = Path.Combine(Path.GetTempPath(), "adamantium-universe-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "Drift.ascene"), Scene.Replace("        ", ""));

        var pipeName = "adamantium-universe-host-" + Guid.NewGuid().ToString("N");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Adamantium.UniverseHost.dll"));
        start.ArgumentList.Add(pipeName);
        _host = Process.Start(start)!;
        _errors = _host.StandardError.ReadToEndAsync();
        _ = _host.StandardOutput.ReadToEndAsync();
        _viewer = new WatchingViewer();
        using var connecting = new CancellationTokenSource(Patience);
        _connection = UniverseHostConnection.ConnectAsync(pipeName, _viewer, connecting.Token).GetAwaiter().GetResult();
    }

    [TearDown]
    public void StopHost()
    {
        _connection?.Dispose();
        if (_host is { HasExited: false })
        {
            _host.WaitForExit(Patience);
        }

        if (_host is { HasExited: false })
        {
            _host.Kill();
        }

        _host?.Dispose();
        Directory.Delete(_folder, true);
    }

    // Played for a while, the game's service moved the mover, the scene's camera - not the universe's own, which looks
    // the other way - drew it in the middle of the frame, and the host stops when told.
    [Test]
    public async Task AGameLaunched_MovesWhatItsCodeMoves_IsDrawn_AndStopsWhenTold()
    {
        await Launch("Drift.ascene", 160, 90);
        Assert.That(_viewer.Hellos.TryTake(out var hello, Patience), Is.True, "greeted");
        var moved = await WaitUntilMoverMoved();

        using var frame = Image.Load(new MemoryStream(await _connection.Host.CaptureFrameAsync(CancellationToken.None)));
        var pixels = frame.GetPixelBuffer(0, 0).GetPixels<byte>();
        var center = (frame.Description.Height / 2 * frame.Description.Width + frame.Description.Width / 2) * 4;
        await _connection.Host.StopAsync(CancellationToken.None);
        var told = _viewer.Ended.Wait(Patience);
        _connection.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(hello.ProcessId, Is.EqualTo(_host.Id));
            Assert.That(hello.DeviceUuid, Is.Not.EqualTo(Guid.Empty), "the GPU it draws on");
            Assert.That(moved, Is.GreaterThan(0), "the game's service moved it");
            Assert.That(pixels[center] + pixels[center + 1] + pixels[center + 2], Is.GreaterThan(60), "the lit cube is drawn there");
            Assert.That(told, Is.True, "told the universe stopped");
            Assert.That(_host.WaitForExit(Patience), Is.True, "the host exits once the viewer let go");
            Assert.That(_host.ExitCode, Is.EqualTo(0), _errors.Result);
        });
    }

    // The viewer is handed the handles of surfaces of the size it shows, and of a new size when it asks.
    [Test]
    public async Task TheViewer_IsGivenSurfacesOfItsSize_AndNewOnesWhenItResizes()
    {
        await Launch("Drift.ascene", 160, 90);
        Assert.That(_viewer.Surfaces.TryTake(out var first, Patience), Is.True, "given a surface");
        Assert.Multiple(() =>
        {
            Assert.That(first.Width, Is.EqualTo(160u));
            Assert.That(first.Height, Is.EqualTo(90u));
            Assert.That(first.Buffers, Has.Length.EqualTo(3));
            Assert.That(first.Buffers.All(buffer => buffer.MemoryHandle != 0 && buffer.ProduceSemaphoreHandle != 0
                                                  && buffer.ConsumeSemaphoreHandle != 0), Is.True, "handles of this process");
        });
        first.CloseHandles();

        _connection.Host.Resize(320, 180, 1);
        _connection.Host.AskFrame(_viewer.LastFrame + 1);
        Assert.That(_viewer.Surfaces.TryTake(out var resized, Patience), Is.True, "given a surface of the new size");
        Assert.That(resized.Width, Is.EqualTo(320u));
        resized.CloseHandles();
        await _connection.Host.StopAsync(CancellationToken.None);
    }

    // A scene or code the host cannot load fails the launch with the reason, and the host exits with 1.
    [Test]
    public void ALaunchThatCannotLoad_FailsWithTheReason()
    {
        var failure = Assert.ThrowsAsync<RemoteInvocationException>(() => Launch("Missing.ascene", 160, 90));
        _connection.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain("Missing.ascene"));
            Assert.That(_host.WaitForExit(Patience), Is.True, "the host exits");
            Assert.That(_host.ExitCode, Is.EqualTo(1));
        });
    }

    private Task Launch(string scene, uint width, uint height) =>
        _connection.Host.LaunchAsync(new UniverseLaunch
        {
            ScenePath = Path.Combine(_folder, scene),
            CodeAssemblies = [Path.Combine(AppContext.BaseDirectory, "Adamantium.UniverseHost.TestGame.dll")],
            Width = width,
            Height = height
        }, new CancellationTokenSource(Patience).Token);

    private async Task<double> WaitUntilMoverMoved()
    {
        var waited = Stopwatch.StartNew();
        while (true)
        {
            var text = await _connection.Host.CaptureSceneAsync(CancellationToken.None);
            var scene = SceneFile.Read(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)));
            var x = scene.Shapes.Single(shape => shape.Name == "Mover").Position.X;
            if (x > 0.5 || waited.Elapsed > Patience)
            {
                return x;
            }

            await Task.Delay(50);
        }
    }
}

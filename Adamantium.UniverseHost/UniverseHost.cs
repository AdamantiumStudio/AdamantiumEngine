using System.Reflection;
using System.Runtime.Loader;
using Adamantium.Core.DependencyInjection;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Engine.Scenes;
using Adamantium.Graphics.Core.Assets;
using Adamantium.Imaging;
using Adamantium.Multiverse;
using Adamantium.Multiverse.Input;
using Adamantium.Multiverse.Remote;

namespace Adamantium.UniverseHost;

/// <summary>Plays a game with no window, for a viewer in another process: serves it <see cref="IUniverseHostService"/>,
/// and on its launch loads the game's code by path - the PDB beside it, for a debugger to see - builds its scene from its
/// content, starts its services and draws it by the scene's camera into surfaces the viewer shows.</summary>
public sealed class UniverseHost : IUniverseHostService, IDisposable
{
    private static readonly TimeSpan ViewerConnectionTimeout = TimeSpan.FromSeconds(30);

    private readonly TaskCompletionSource<UniverseLaunch> _launchAsked = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _launched = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _stopping = new();
    private readonly List<string> _codeFolders = [];
    private UniverseViewerConnection _connection;
    private GameUniverse _universe;
    private RemoteUniverseOutput _output;
    private UniverseLaunch _launch;
    private GameRun _run;
    private volatile bool _stopAsked;

    /// <summary>The exit code: 0 once the game played until stopped, else 1 - the reason written to the error output.
    /// </summary>
    public int ExitCode { get; private set; }

    /// <summary>Serves on <paramref name="pipeName"/>, waits for the viewer to launch the game and plays it until it is
    /// stopped or the viewer goes; returns once the universe stopped and the viewer let go.</summary>
    public void Serve(string pipeName)
    {
        using var connecting = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
        connecting.CancelAfter(ViewerConnectionTimeout);
        try
        {
            _connection = UniverseViewerConnection.AcceptAsync(pipeName, this, connecting.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (_stopAsked)
        {
            return;
        }

        _ = _connection.Completion.ContinueWith(_ => Stop(), TaskScheduler.Default);
        Task.WhenAny(_launchAsked.Task, _connection.Completion).GetAwaiter().GetResult();
        if (!_launchAsked.Task.IsCompletedSuccessfully)
        {
            _stopped.TrySetResult();
            WaitForViewerToLetGo();
            return;
        }

        _launch = _launchAsked.Task.Result;
        AssemblyLoadContext.Default.Resolving += ResolveFromCodeFolders;
        try
        {
            Play();
        }
        catch (Exception exception)
        {
            Fail("the universe could not start: " + exception);
        }
        finally
        {
            AssemblyLoadContext.Default.Resolving -= ResolveFromCodeFolders;
            _stopped.TrySetResult();
        }

        WaitForViewerToLetGo();
    }

    /// <summary>Stops the game from any thread; <see cref="Serve"/> returns once it stopped.</summary>
    public void Stop()
    {
        _stopAsked = true;
        _stopping.Cancel();
        _launchAsked.TrySetCanceled();
        _launched.TrySetCanceled();
        _universe?.ShutDown();
    }

    public void Dispose()
    {
        if (_universe != null)
        {
            _universe.Started -= OnStarted;
            _universe.ShuttingDown -= OnShuttingDown;
            _universe.Stopped -= OnStopped;
        }

        _output?.Dispose();
        _connection?.Dispose();
        _stopping.Dispose();
    }

    public Task LaunchAsync(UniverseLaunch launch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(launch);
        if (!_launchAsked.TrySetResult(launch))
        {
            throw new InvalidOperationException("The game is launched once.");
        }

        return _launched.Task.WaitAsync(cancellationToken);
    }

    public void Resize(uint width, uint height, float pixelsPerPoint) => _output?.ResizeTo(width, height, pixelsPerPoint);

    public void AskFrame(ulong frame) => _output?.TakeFrameRequest();

    public void AskNewSurface() => _output?.AskNewSurface();

    public void MovePointer(float x, float y, bool isOver) => _output?.MovePointer(new Vector2F(x, y), isOver);

    public void Focus(bool hasKeyboard) => _output?.Focus(hasKeyboard);

    public void TakeKeyboardInput(KeyboardInput input) => _output?.TakeKeyboardInput(input);

    public void TakeMouseInput(MouseInput input) => _output?.TakeMouseInput(input);

    public void TakeGestureInput(GestureInput input) => _output?.TakeGestureInput(input);

    public void TakeText(string text) => _output?.TakeText(text);

    public Task<string> CaptureSceneAsync(CancellationToken cancellationToken) => OnUniverseThread(() =>
    {
        using var stream = new MemoryStream();
        SceneCapture.Capture(_universe.EntityWorld.MainScene).Write(stream);
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }, cancellationToken);

    public Task<byte[]> CaptureFrameAsync(CancellationToken cancellationToken) => OnUniverseThread(() =>
    {
        using var frame = _output.ReadFrame() ?? throw new InvalidOperationException("No frame was drawn.");
        using var stream = new MemoryStream();
        frame.Save(stream, ImageFileType.Png);
        return stream.ToArray();
    }, cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Stop();
        return _stopped.Task.WaitAsync(cancellationToken);
    }

    private void Play()
    {
        var container = new AdamantiumDependencyContainer();
        var registry = (IContainerRegistry)container;
        var platform = new HeadlessPlatform();
        registry.RegisterInstance<IOutputFactory>(platform);
        registry.RegisterInstance<IWindowingPlatform>(platform);
        _universe = new GameUniverse(container);
        _output = new RemoteUniverseOutput(_universe.Satellites.Get<IUniverseEventAggregator>(), _connection.Viewer,
            _connection.ViewerProcessId, _launch);
        List<IAssetResolver> folders = [];
        foreach (var folder in _launch.ContentFolders)
            folders.Add(new FileSystemAssetResolver(folder));

        _universe.Assets.Resolvers.InsertRange(0, folders);
        foreach (var path in _launch.CodeAssemblies)
            _codeFolders.Add(Path.GetDirectoryName(path)!);

        _universe.Started += OnStarted;
        _universe.ShuttingDown += OnShuttingDown;
        _universe.Stopped += OnStopped;
        _universe.Run(_output);
        if (!_stopAsked)
            Fail("the universe stopped by itself; the reason is above");
    }

    private async Task<T> OnUniverseThread<T>(Func<T> read, CancellationToken cancellationToken)
    {
        if (_universe == null || !_launched.Task.IsCompletedSuccessfully || _stopped.Task.IsCompleted)
        {
            throw new InvalidOperationException("The game is not playing.");
        }

        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _universe.Post(() =>
        {
            try
            {
                result.TrySetResult(read());
            }
            catch (Exception exception)
            {
                result.TrySetException(exception);
            }
        });
        if (await Task.WhenAny(result.Task, _stopped.Task).WaitAsync(cancellationToken) != result.Task)
        {
            throw new InvalidOperationException("The game stopped before it was read.");
        }

        return await result.Task;
    }

    private void WaitForViewerToLetGo() =>
        _connection.Completion.ContinueWith(_ => { }, TaskScheduler.Default).Wait();

    private void OnStarted(object sender, EventArgs e)
    {
        if (_stopAsked)
        {
            _universe.ShutDown();
            return;
        }

        var adapter = _universe.GraphicsDeviceService.MainGraphicsDevice.GraphicsAdapter;
        _connection.Viewer.Greet(new RemoteHello(Environment.ProcessId, adapter.DeviceUuid));
        _ = PrepareAsync();
    }

    private async Task PrepareAsync()
    {
        try
        {
            var scene = SceneFile.Read(_launch.ScenePath);
            var assemblies = LoadCode();
            var builder = new SceneBuilder(_universe.EntityWorld.MainScene, _universe.Assets);
            var models = await builder.LoadModelsAsync(scene);
            _universe.Post(() =>
            {
                try
                {
                    Start(scene, assemblies, builder, models);
                }
                catch (Exception exception)
                {
                    Fail(exception.ToString());
                }
            });
        }
        catch (Exception exception)
        {
            Fail(exception.ToString());
        }
    }

    private void Start(SceneFile file, IReadOnlyList<Assembly> assemblies, SceneBuilder builder,
        IReadOnlyDictionary<string, Adamantium.Graphics.Core.Models.SceneData> models)
    {
        var world = _universe.EntityWorld;
        foreach (var problem in SceneCode.Replace(world, [], assemblies))
            Console.Error.WriteLine("Game code: " + problem);

        builder.Build(file, models);
        foreach (var problem in builder.Problems)
            Console.Error.WriteLine("Scene: " + problem);

        if (_output.SharingFailure is { } failure)
            throw new NotSupportedException(failure);

        world.ForceUpdate();
        if (FindSceneCamera(world.MainScene, _output.OwnCamera) is { } camera)
            _universe.LookThrough(camera);

        _run = GameRun.Start(world.MainScene);
        _launched.TrySetResult();
    }

    private List<Assembly> LoadCode()
    {
        List<Assembly> assemblies = [];
        foreach (var path in _launch.CodeAssemblies)
            assemblies.Add(AssemblyLoadContext.Default.LoadFromAssemblyPath(path));

        return assemblies;
    }

    private Assembly ResolveFromCodeFolders(AssemblyLoadContext context, AssemblyName name)
    {
        foreach (var folder in _codeFolders)
        {
            var path = Path.Combine(folder, name.Name + ".dll");
            if (File.Exists(path))
                return context.LoadFromAssemblyPath(path);
        }

        return null;
    }

    private static Camera FindSceneCamera(Scene scene, Camera outputCamera)
    {
        var pending = new Stack<Entity>();
        PushReversed(pending, scene.RootEntities);
        while (pending.TryPop(out var entity))
        {
            if (entity.GetComponent<Camera>() is { } camera && camera != outputCamera)
                return camera;

            PushReversed(pending, entity.Dependencies);
        }

        return null;
    }

    private static void PushReversed(Stack<Entity> pending, IReadOnlyList<Entity> entities)
    {
        for (var index = entities.Count - 1; index >= 0; index--)
            pending.Push(entities[index]);
    }

    private void OnShuttingDown(object sender, EventArgs e)
    {
        _run?.Stop();
        _run = null;
        _connection.Viewer.EndUniverse();
    }

    private void OnStopped(object sender, EventArgs e) => _stopped.TrySetResult();

    private void Fail(string reason)
    {
        Console.Error.WriteLine("The game could not play: " + reason);
        ExitCode = 1;
        _launched.TrySetException(new InvalidOperationException(reason));
        Stop();
    }
}

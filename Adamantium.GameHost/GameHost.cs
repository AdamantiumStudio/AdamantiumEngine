using System.Reflection;
using System.Runtime.Loader;
using Adamantium.Core.DependencyInjection;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Engine.Scenes;
using Adamantium.Graphics.Core.Assets;
using Adamantium.Imaging;
using Adamantium.Multiverse;

namespace Adamantium.GameHost;

/// <summary>Plays a game with no window: its code loaded by path - the PDB beside it, for a debugger to see - its scene
/// built from its content, its services started, drawn by the scene's camera into an offscreen output.</summary>
public sealed class GameHost : IDisposable
{
    private readonly GameHostOptions _options;
    private readonly GameUniverse _universe;
    private readonly OffscreenOutput _output;
    private readonly List<string> _codeFolders = [];
    private GameRun _run;
    private int _framesPlayed;
    private volatile bool _stopAsked;

    public GameHost(GameHostOptions options)
    {
        _options = options;
        var container = new AdamantiumDependencyContainer();
        var registry = (IContainerRegistry)container;
        var platform = new HeadlessPlatform();
        registry.RegisterInstance<IOutputFactory>(platform);
        registry.RegisterInstance<IWindowingPlatform>(platform);
        _universe = new GameUniverse(container);
        _output = new OffscreenOutput(options.Width, options.Height);
        List<IAssetResolver> folders = [];
        foreach (var folder in options.ContentFolders)
            folders.Add(new FileSystemAssetResolver(folder));

        _universe.Assets.Resolvers.InsertRange(0, folders);
        foreach (var path in options.CodeAssemblies)
            _codeFolders.Add(Path.GetDirectoryName(path)!);

        _universe.Started += OnStarted;
        _universe.ShuttingDown += OnShuttingDown;
        _universe.FrameFinished += OnFrameFinished;
    }

    /// <summary>The exit code: 0 once the game played as asked, else 1 - the reason written to the error output.</summary>
    public int ExitCode { get; private set; }

    /// <summary>Plays the game until it is stopped, or for <see cref="GameHostOptions.Frames"/> frames; returns once the
    /// universe stopped.</summary>
    public void Run()
    {
        AssemblyLoadContext.Default.Resolving += ResolveFromCodeFolders;
        try
        {
            _universe.Run(_output);
            if (!_stopAsked)
                Fail("the universe stopped by itself; the reason is above");
        }
        catch (Exception exception)
        {
            Fail("the universe could not start: " + exception);
        }
        finally
        {
            AssemblyLoadContext.Default.Resolving -= ResolveFromCodeFolders;
        }
    }

    /// <summary>Stops the game from any thread; <see cref="Run"/> returns once it stopped.</summary>
    public void Stop()
    {
        _stopAsked = true;
        _universe.ShutDown();
    }

    public void Dispose()
    {
        _universe.Started -= OnStarted;
        _universe.ShuttingDown -= OnShuttingDown;
        _universe.FrameFinished -= OnFrameFinished;
        _output.Dispose();
    }

    private void OnStarted(object sender, EventArgs e)
    {
        if (_stopAsked)
        {
            _universe.ShutDown();
            return;
        }

        _ = PrepareAsync();
    }

    private async Task PrepareAsync()
    {
        try
        {
            var scene = SceneFile.Read(_options.ScenePath);
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

        world.ForceUpdate();
        if (FindSceneCamera(world.MainScene, _output.OwnCamera) is { } camera)
            _universe.LookThrough(camera);

        _run = GameRun.Start(world.MainScene);
    }

    private List<Assembly> LoadCode()
    {
        List<Assembly> assemblies = [];
        foreach (var path in _options.CodeAssemblies)
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

    private void OnFrameFinished(object sender, EventArgs e)
    {
        if (_run == null || _options.Frames <= 0 || ++_framesPlayed < _options.Frames)
            return;

        try
        {
            if (_options.CapturePath != null)
                SceneCapture.Capture(_universe.EntityWorld.MainScene).Write(_options.CapturePath);

            if (_options.ImagePath != null)
            {
                using var frame = _output.ReadFrame() ?? throw new InvalidOperationException("No frame was drawn.");
                frame.Save(_options.ImagePath, ImageFileType.Png);
            }
        }
        catch (Exception exception)
        {
            Fail(exception.ToString());
            return;
        }

        Stop();
    }

    private void OnShuttingDown(object sender, EventArgs e)
    {
        _run?.Stop();
        _run = null;
    }

    private void Fail(string reason)
    {
        Console.Error.WriteLine("The game could not play: " + reason);
        ExitCode = 1;
        Stop();
    }
}

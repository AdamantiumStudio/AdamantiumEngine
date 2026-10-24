using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using Adamantium.Core;
using Adamantium.Core.DependencyInjection;
using Adamantium.Engine.Compiler.Models;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Multiverse.Events;
using Adamantium.Multiverse.Input;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Content;
using Adamantium.Graphics.Core.Models;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Serilog;

namespace Adamantium.Multiverse;

public class Universe : IUniverse
{
    private readonly Dictionary<UniverseOutput, EntityService> drawSystems = [];
    private volatile bool deviceResourcesLost;
    private bool isStopped;

    private readonly DisposeCollector unloadContentCollector;
        
    private readonly UniversePlatform platform;
    private CancellationTokenSource cancellationTokenSource;
    private readonly Thread loopThread;

    private double accumulatedFrameTime;
    private AppTime appTime;

    private TimeSpan totalTime;
    private volatile bool simulationPauseRequested;
    private bool isSimulationPaused;

    private readonly System.Diagnostics.Stopwatch renderTimer = new();
    private double renderSeconds, renderWindow, gpuMilliseconds;
    private int renderedFrames;

    private readonly PreciseTimer loopTimer;

    private Double fpsTime;
    private Int32 fpsCounter;

    private readonly Dictionary<Object, OutputContext> contextsMapping;

    private static readonly TimeSpan LongestWaitForShown = TimeSpan.FromMilliseconds(100);
    private const int IdleHostedFrameMs = 16;
    private const int FailedFrameBackoffMs = 100;

    private Thread hostedLoop;
    private readonly object hostedFrameGate = new();
    private int hostedLoopHolds;
    private volatile FrameMode frameMode;
    private volatile bool isPaused;
    private readonly Channel<UniverseMessage> messages = Channel.CreateUnbounded<UniverseMessage>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = true });
    private readonly HashSet<UniverseOutput> outputsAsked = [];
    private bool everyOutputAsked = true;
    private string lastFailedFrame;
    private int hostedFramesDrawn;
    private long hostedFpsSince;
    private long hostedDrawnAt;
    private Single fps;

    /// <summary>
    /// <paramref name="container"/> is the process's: the host has registered its surfaces and windows there.
    /// </summary>
    public Universe(
        UniverseMode mode,
        bool enableDebug,
        IDependencyContainer container,
        IGraphicsDeviceService graphicsDeviceService = null)
    {
        Mode = mode;

        Container = container ?? throw new ArgumentNullException(nameof(container));
        UniverseBuilder.Build(Container);
            
        appTime = new AppTime();
        loopTimer = new PreciseTimer();
        contextsMapping = new Dictionary<Object, OutputContext>();
        IsFixedTimeStep = false;
        DesiredFPS = 60;
        Satellites = new Satellites();
        Content = new ContentManager(Container, Satellites);
        // Cooked artifacts win over raw source.
        Content.Resolvers.Add(new CookedContentResolver());
        Content.Resolvers.Add(new FileSystemContentResolver());
        Content.Resolvers.Add(new EffectContentResolver());
        // The texture reader takes its device from the Satellites at load time, when it exists.
        Content.Readers.Add(typeof(SceneData), new SceneDataContentReader());
        Content.Readers.Add(typeof(Texture), new TextureContentReader());
            
        ModelConverter = new ModelConverter();
        unloadContentCollector = new DisposeCollector();
        ShutDownMode = ShutDownMode.OnMainWindowClosed;
            
        EventAggregator = new UniverseEventAggregator();
        Satellites.Add<IUniverseEventAggregator>(EventAggregator);
        EventAggregator.GetEvent<UniverseOutputRemovedEvent>().Subscribe(OnOutputRemoved);
        EventAggregator.GetEvent<UniverseOutputCreatedEvent>().Subscribe(OnOutputCreated);
        var factory = Container.Resolve<IGraphicsDeviceFactory>();

        if (mode == UniverseMode.Standalone)
        {
            // The host's graphics settings where it registered them, the engine's own otherwise.
            var settings = Container.IsRegistered<GraphicsSettings>()
                ? Container.Resolve<GraphicsSettings>()
                : GraphicsSettings.CreateDefault();
            GraphicsDeviceService = new GraphicsDeviceService(factory, enableDebug, settings);
            //GraphicsDeviceService.CreateMainDevice("Universe", enableDynamicRendering);
        }
        else
        {
            //GraphicsDeviceService = Container.Resolve<IGraphicsDeviceService>();
            GraphicsDeviceService = graphicsDeviceService;
        }
            
        platform = new UniversePlatform(this);
        EntityWorld = new EntityWorld(Container, Satellites);

        Satellites.Add<IUniverse>(this);
        Satellites.Add<IAdamantiumApplication>(this);
        Satellites.Add<IUniversePlatform>(platform);
        Satellites.Add<IContentManager>(Content);
        Satellites.Add(ModelConverter);
        Satellites.Add<IGraphicsDeviceService>(GraphicsDeviceService);
        Satellites.Add(InputActions);

        Stopped += ResetWorld;
        loopThread = new Thread(RunLoop);
    }
        
    protected IUniverseEventAggregator EventAggregator { get; }
        
    public EntityWorld EntityWorld { get; }

    /// <summary>
    /// What this universe has its own of; its services reach them through <see cref="Adamantium.ECS.EntityWorld.Satellites"/>.
    /// </summary>
    public Satellites Satellites { get; }

    public IGraphicsDeviceService GraphicsDeviceService { get; set; }

    /// <summary>
    /// The actions of this universe, read each frame right after the input, through the <see cref="IInputRouting"/> in
    /// <see cref="Satellites"/>; without one they stay idle.
    /// </summary>
    public InputActions InputActions { get; } = new();

    public bool IsInitialized { get; private set; }
    public bool IsPaused
    {
        get => isPaused;
        private set => isPaused = value;
    }
        
    /// <summary>
    /// Calling this method will pause running service
    /// </summary>
    public void Pause()
    {
        if (!IsPaused)
        {
            IsPaused = true;
            Paused?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Calling this method will resume running service
    /// </summary>
    public void Resume()
    {
        if (IsPaused)
        {
            IsPaused = false;
            Resumed?.Invoke(this, EventArgs.Empty);
            messages.Writer.TryWrite(new UniverseMessage(null, null));
        }
    }

    /// <summary>
    /// Stops the game while drawing goes on: no time passes for the services and no input reaches them. Set from any
    /// thread; the change takes effect with the next frame.
    /// </summary>
    public bool IsSimulationPaused
    {
        get => isSimulationPaused;
        set
        {
            simulationPauseRequested = value;
            Invalidate();
        }
    }

    /// <summary>
    /// Read only collection of <see cref="UniverseOutput"/>s
    /// </summary>
    public IReadOnlyList<UniverseOutput> Outputs => platform.Outputs;


    /// <summary>
    /// Main <see cref="UniverseOutput"/>
    /// </summary>
    public UniverseOutput MainOutput => platform.MainWindow;

    public bool IsFixedTimeStep { get; set; }
    public double TimeStep => 1.0d / DesiredFPS;
        
    public UInt32 DesiredFPS { get; set; }

    /// <summary>CPU cost of one drawn frame (recording and submitting), averaged over the last second.</summary>
    public Double DrawTimeMs { get; private set; }

    /// <summary>Frames per second the universe's rendering could sustain, from <see cref="DrawTimeMs"/> - not the rate its
    /// host ticks it at.</summary>
    public Single RenderFps { get; private set; }

    /// <summary>GPU time of one drawn frame, averaged over the last second; other queues' work running meanwhile counts
    /// too.</summary>
    public Double GpuTimeMs { get; private set; }

    /// <summary>Frames the universe has actually run a second, over the last second: what the eye sees.</summary>
    public Single Fps
    {
        get => hostedLoop != null && Stopwatch.GetElapsedTime(Volatile.Read(ref hostedDrawnAt)).TotalSeconds > 1 ? 0 : fps;
        private set => fps = value;
    }

    /// <summary>
    /// Condition on which the loop will be exited
    /// </summary>
    public ShutDownMode ShutDownMode { get; set; }
        
    public UniverseMode Mode { get; }
        
    public string Title { get; set; }
        
    public bool IsRunning => cancellationTokenSource != null && cancellationTokenSource.IsCancellationRequested != true;

    public void InitializeUniverse()
    {
        InitializeBeforeRun();
    }

    /// <summary>
    /// Services which could be added to the universe
    /// </summary>
    public IDependencyContainer Container { get; }
        
    /// <summary>
    /// Represents a Content Manager, which can load all needed resources as Textures, Effects, Entity
    /// </summary>
    public ContentManager Content { get; set; }

    /// <summary>
    /// Built-in model converter to import model directly in engine
    /// </summary>
    public ModelConverter ModelConverter { get; set; }

    protected virtual void Initialize()
    {
    }
        
    private void ResetWorld(object sender, EventArgs e)
    {
        EntityWorld.Reset();
    }

    private void RemoveRenderProcessor(UniverseOutput window)
    {
        lock (drawSystems)
        {
            if (drawSystems.Remove(window, out var system))
            {
                EntityWorld.RemoveService(system);
            }
        }
    }

    /// <summary>
    /// Creates the service that draws <paramref name="window"/>; it is removed together with the output.
    /// </summary>
    public T CreateRenderService<T>(UniverseOutput window) where T : EntityService
    {
        var system = EntityWorld.CreateService<T>(EntityWorld, window);
        lock (drawSystems)
        {
            drawSystems.Add(window, system);
        }

        return system;
    }

    /// <summary>
    /// Run the loop on default control
    /// </summary>
    public void Run()
    {
        if (IsRunning) return;

        RunInternal();
    }

    /// <summary>
    /// Run the loop on the selected control
    /// </summary>
    /// <param name="context">Control which will be used for creating corresponding <see cref="UniverseOutput"/> and further rendering</param>
    public void Run(object context)
    {
        if (IsRunning) return;

        var window = CreateOutputFromContext(context);
        Run(window);
    }

    /// <summary>
    /// Runs one frame on the host's tick. While paused it only frees the outputs taken out meanwhile, and its own clock
    /// resumes where it stopped.
    /// </summary>
    public void RunOnce(AppTime time)
    {
        if (IsPaused)
        {
            platform.RemoveWindowsInternal();
            hostedFrameAt = 0;
            Fps = 0;
            return;
        }

        UpdateAppTime(HostedFrameTime());
        StartFrame(true);
        MakePreparations();
        Update(SimulationTime());
        ExecuteDrawSequence2(appTime);
        FrameFinished?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Runs a universe hosted by another platform on a thread of its own, at its own pace: each frame waits until its
    /// outputs' previous frames were shown, so it draws no more often than it is seen, and its host no longer drives it
    /// with <see cref="RunOnce"/>. Stops with <see cref="ShutDown"/>.
    /// </summary>
    public void StartHostedLoop()
    {
        if (Mode == UniverseMode.Standalone || hostedLoop != null) return;

        InitializeBeforeRun();
        hostedLoop = new Thread(RunHostedLoop) { IsBackground = true, Name = $"Universe {Title}" };
        hostedLoop.Start();
    }

    /// <summary>Whether this universe runs its own loop (see <see cref="StartHostedLoop"/>).</summary>
    public bool RunsOwnLoop => hostedLoop != null;

    /// <summary>
    /// Holds the loop of <see cref="StartHostedLoop"/> between two frames until <see cref="ResumeHostedLoop"/>, waiting
    /// for the frame in progress to finish: for whoever remakes the devices it draws with.
    /// </summary>
    public void SuspendHostedLoop()
    {
        Interlocked.Increment(ref hostedLoopHolds);
        lock (hostedFrameGate)
        {
        }
    }

    /// <summary>Lets the loop held by <see cref="SuspendHostedLoop"/> go on.</summary>
    public void ResumeHostedLoop()
    {
        Interlocked.Decrement(ref hostedLoopHolds);
    }

    /// <summary>How the own loop paces its frames; set from any thread.</summary>
    public FrameMode FrameMode
    {
        get => frameMode;
        set
        {
            frameMode = value;
            messages.Writer.TryWrite(new UniverseMessage(null, null));
        }
    }

    /// <summary>Asks for a frame of every output: what the world shows has changed. From any thread; under
    /// <see cref="Multiverse.FrameMode.OnDemand"/> the loop sleeps until something asks, under
    /// <see cref="Multiverse.FrameMode.Continuous"/> every frame draws every output and nothing is asked.</summary>
    public void Invalidate()
    {
        if (frameMode == FrameMode.OnDemand || IsPaused)
        {
            messages.Writer.TryWrite(new UniverseMessage(null, null));
        }
    }

    /// <summary>Asks for a frame of <paramref name="output"/> alone: what changed is its own - its input, its size, its
    /// camera. From any thread.</summary>
    public void Invalidate(UniverseOutput output)
    {
        if (frameMode == FrameMode.OnDemand || IsPaused)
        {
            messages.Writer.TryWrite(new UniverseMessage(output, null));
        }
    }

    /// <summary>Runs <paramref name="action"/> on the universe's thread before its next frame, then draws every output:
    /// how anything outside the loop changes the world, with no race against a frame. From any thread.</summary>
    public void Post(Action action)
    {
        messages.Writer.TryWrite(new UniverseMessage(null, action));
    }

    private void RunHostedLoop()
    {
        try
        {
            OnStarted();
            while (IsRunning)
            {
                if (Volatile.Read(ref hostedLoopHolds) > 0)
                {
                    Thread.Sleep(1);
                    continue;
                }

                if (frameMode == FrameMode.OnDemand && !FramesAsked())
                {
                    WaitForMessage();
                    continue;
                }

                bool drawn;
                bool paused;
                try
                {
                    lock (hostedFrameGate)
                    {
                        if (Volatile.Read(ref hostedLoopHolds) > 0 || !IsRunning) continue;

                        paused = IsPaused;
                        drawn = paused ? RunPausedFrame() : RunHostedFrame();
                    }
                }
                catch (Exception exception)
                {
                    ReportFailedFrame(exception);
                    everyOutputAsked = true;
                    Thread.Sleep(FailedFrameBackoffMs);
                    continue;
                }

                if (paused)
                {
                    if (IsPaused)
                    {
                        WaitForMessage();
                    }

                    continue;
                }

                CountHostedFrame(drawn);
                if (!drawn)
                {
                    Thread.Sleep(IdleHostedFrameMs);
                }
            }
        }
        catch (Exception exception)
        {
            Log.Logger.Fatal(exception, "Universe loop stopped");
        }
        finally
        {
            OnStopped();
        }
    }

    private void ReportFailedFrame(Exception exception)
    {
        var what = exception.GetType().FullName + ": " + exception.Message;
        if (what == lastFailedFrame)
        {
            return;
        }

        lastFailedFrame = what;
        Log.Logger.Error(exception, "A frame of the universe failed; the loop goes on");
    }

    private bool RunPausedFrame()
    {
        platform.RemoveWindowsInternal();
        hostedFrameAt = 0;
        Fps = 0;
        TakeMessages();
        return false;
    }

    private bool RunHostedFrame()
    {
        StartFrame(false);
        WaitUntilShown();
        UpdateAppTime(HostedFrameTime());
        MakePreparations();
        Update(SimulationTime());
        var drawn = ExecuteDrawSequence2(appTime);
        EndScene();
        FrameFinished?.Invoke(this, EventArgs.Empty);
        return drawn;
    }

    private bool FramesAsked() => everyOutputAsked || outputsAsked.Count > 0 || messages.Reader.TryPeek(out _);

    private void WaitForMessage()
    {
        try
        {
            messages.Reader.WaitToReadAsync(cancellationTokenSource.Token).AsTask().Wait();
        }
        catch (AggregateException) when (cancellationTokenSource.IsCancellationRequested)
        {
        }
    }

    private void TakeMessages()
    {
        everyOutputAsked |= frameMode == FrameMode.Continuous;
        while (messages.Reader.TryRead(out var message))
        {
            if (message.Action != null)
            {
                RunPosted(message.Action);
                everyOutputAsked = true;
            }
            else if (message.Output == null)
            {
                everyOutputAsked = true;
            }
            else
            {
                outputsAsked.Add(message.Output);
            }
        }
    }

    private static void RunPosted(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            Log.Logger.Error(exception, "An action posted to the universe failed");
        }
    }

    private void StartFrame(bool everyOutput)
    {
        TakeMessages();
        everyOutputAsked |= everyOutput;
        MarkOutputsDrawn();
    }

    private void MarkOutputsDrawn()
    {
        foreach (var output in platform.Outputs)
        {
            output.IsDrawnThisFrame = everyOutputAsked || outputsAsked.Contains(output);
        }

        everyOutputAsked = false;
        outputsAsked.Clear();
    }

    private void WaitUntilShown()
    {
        foreach (var output in platform.Outputs)
        {
            if (output.IsDrawnThisFrame && output.IsVisible)
            {
                output.WaitUntilShown(LongestWaitForShown);
            }
        }
    }

    // A hosted universe may run several frames to one of its host's, or one to several: its clock is its own frames.
    // A gap without frames - a minimized window, a stalled render thread - moves it by one long frame at most.
    private double HostedFrameTime()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = hostedFrameAt == 0 ? 0 : Stopwatch.GetElapsedTime(hostedFrameAt, now).TotalSeconds;
        hostedFrameAt = now;
        return Math.Min(elapsed, LongestHostedFrame);
    }

    private const double LongestHostedFrame = 0.25;

    private long hostedFrameAt;

    /// <summary>
    /// Run the loop on the selected control
    /// </summary>
    /// <param name="window"><see cref="UniverseOutput"/> which will be used for rendering</param>
    public void Run(UniverseOutput window)
    {
        if (IsRunning) return;
            
        platform.AddOutput(window);
        RunInternal();
    }

    private void RunInternal()
    {
        InitializeBeforeRun();
            
        //RunLoop();
        loopThread.Start();

        if (Mode == UniverseMode.Standalone)
        {
            platform.Run(cancellationTokenSource.Token);
        }
    }

    public void Submit()
    {
        if (IsPaused)
        {
            return;
        }

        EndScene();
    }

    private void OnInitialized()
    {
        IsInitialized = true;
        Initialized?.Invoke(this, EventArgs.Empty);
    }

    private void OnStarted()
    {
        Started?.Invoke(this, EventArgs.Empty);
    }

    public void AddOutput(UniverseOutput output)
    {
        platform.AddOutput(output);
    }

    /// <summary>
    /// Create new output and add it to the list of outputs
    /// </summary>
    /// <param name="width">Initial window width</param>
    /// <param name="height">Initial window height</param>
    public UniverseOutput CreateOutput(uint width = 1280, uint height = 720)
    {
        return platform.CreateOutput(width, height);
    }

    /// <summary>
    /// Create new output from context and add it to the list of outputs
    /// </summary>
    /// <param name="context">Window, in which Vulkan content will be rendered</param>
    public UniverseOutput CreateOutputFromContext(object context)
    {
        if (!contextsMapping.ContainsKey(context))
        {
            var outputContext = new OutputContext(context);
            var output = platform.CreateOutput(outputContext);
            contextsMapping.Add(context, outputContext);
            return output;
        }
        throw new ArgumentException("There is already an output created on the current context");
    }

    /// <summary>
    /// Create new output from context and add it to the list of outputs
    /// </summary>
    /// <param name="context">Window, in which Vulkan content will be rendered</param>
    /// <param name="surfaceFormat">Surface format</param>
    /// <param name="depthFormat">Depth buffer format</param>
    /// <param name="msaaLevel">MSAA level</param>
    public UniverseOutput CreateOutputFromContext(
        object context, 
        SurfaceFormat surfaceFormat, 
        DepthFormat depthFormat = DepthFormat.Depth32Stencil8X24, 
        MSAALevel msaaLevel = MSAALevel.None)
    {
        if (!contextsMapping.ContainsKey(context))
        {
            var window = platform.CreateOutput(context, surfaceFormat, depthFormat, msaaLevel);
            contextsMapping.Add(context, window.OutputContext);
            return window;
        }
        throw new ArgumentException("There is already an output created on the current context");
    }

    /// <summary>
    /// Remove an output from the list by its context
    /// </summary>
    /// <param name="context">Context of the output to remove</param>
    public void RemoveWindowByContext(object context)
    {
        platform.RemoveOutput(context);
        contextsMapping.Remove(context);
    }

    /// <summary>
    /// Switching presentation from one control to another
    /// </summary>
    /// <param name="oldContext">previous output context</param>
    /// <param name="newContext">new output context</param>
    public void SwitchContext(object oldContext, object newContext)
    {
        if (oldContext == null)
        {
            throw new ArgumentNullException(nameof(oldContext));
        }

        if (newContext == null)
        {
            throw new ArgumentNullException(nameof(newContext));
        }

        if (!contextsMapping.Remove(oldContext, out var oldOutputContext))
        {
            if (!contextsMapping.ContainsKey(newContext))
            {
                CreateOutputFromContext(newContext);
            }
            return;
        }
        var context = new OutputContext(newContext);
        platform.SwitchContext(oldOutputContext, context);
        contextsMapping[newContext] = context;
    }
        
    /// <summary>
    /// Updates the time for each frame
    /// </summary>
    /// <param name="elapsed">elapsed time from the last frame</param>
    protected void UpdateAppTime(double elapsed)
    {
        TimeSpan frameTimeSpan = TimeSpan.FromSeconds(appTime.FrameTime);
        if (!IsPaused && !isSimulationPaused)
        {
            totalTime += frameTimeSpan;
        }
        if (appTime.FramesCount > 0 && appTime.FramesCount - 1 < UInt64.MaxValue)
        {
            appTime.FramesCount++;
        }
        else
        {
            appTime.FramesCount = 1;
        }

        appTime.FrameTime = elapsed;
        appTime.TotalTime = totalTime;
        CalculateFps(ref appTime);

    }

    /// <summary>
    /// Calculates FPS count
    /// </summary>
    private void CalculateFps(ref AppTime appTime)
    {
        fpsCounter++;
        fpsTime += appTime.FrameTime;
        if (fpsTime >= 1.0d)
        {
            appTime.Fps = (fpsCounter) / (Single)fpsTime;
            if (hostedLoop == null) Fps = appTime.Fps;
            fpsCounter = 0;
            fpsTime = 0;
        }
    }

    private void CountHostedFrame(bool drawn)
    {
        var now = Stopwatch.GetTimestamp();
        if (drawn)
        {
            hostedFramesDrawn++;
            Volatile.Write(ref hostedDrawnAt, now);
        }

        if (hostedFpsSince == 0)
        {
            hostedFpsSince = now;
            return;
        }

        var seconds = Stopwatch.GetElapsedTime(hostedFpsSince, now).TotalSeconds;
        if (seconds < 1) return;

        Fps = (Single)(hostedFramesDrawn / seconds);
        hostedFramesDrawn = 0;
        hostedFpsSince = now;
    }

    private AppTime SimulationTime()
    {
        return isSimulationPaused ? appTime with { FrameTime = 0 } : appTime;
    }

    /// <summary>
    /// Runs the universe's own loop
    /// </summary>
    private void RunLoop()
    {
        try
        {
            OnStarted();

            while (IsRunning)
            {
                if (IsFixedTimeStep)
                {
                    accumulatedFrameTime += loopTimer.GetElapsedTime();

                    if (accumulatedFrameTime >= TimeStep)
                    {
                        StartFrame(true);
                        MakePreparations();
                        Update(SimulationTime());
                        ExecuteDrawSequence(appTime);

                        UpdateAppTime(accumulatedFrameTime);
                        accumulatedFrameTime = 0;
                    }
                }
                else
                {
                    var frameTime = loopTimer.GetElapsedTime();

                    StartFrame(true);
                    MakePreparations();
                    Update(SimulationTime());
                    ExecuteDrawSequence(appTime);

                    UpdateAppTime(frameTime);
                }
                FrameFinished?.Invoke(this, EventArgs.Empty);
            }

            OnStopped();
        }
        catch (Exception exception)
        {
            // The loop thread's outermost frame: the only record that the loop stopped, and why.
            Log.Logger.Fatal(exception, "Universe loop stopped");
        }
    }
        
    private void ExecuteDrawSequence(AppTime time)
    {
        if (!platform.HasOutputs) return;
                
        if (BeginScene())
        {
            Draw(time);
            EndScene();
        }
    }

    private bool ExecuteDrawSequence2(AppTime time)
    {
        if (!platform.HasOutputs) return false;

        var drawn = false;
        if (BeginScene())
        {
            renderTimer.Restart();
            if (Draw(time))
            {
                drawn = true;
                renderSeconds += renderTimer.Elapsed.TotalSeconds;
                renderedFrames++;
                foreach (var output in platform.Outputs)
                {
                    if (output.IsDrawnThisFrame && output.IsVisible)
                    {
                        gpuMilliseconds += output.GraphicsDevice?.LastGpuFrameMs ?? 0;
                    }
                }
            }
        }

        // Averaged over a second: one frame is noise.
        renderWindow += time.FrameTime;
        if (renderWindow < 1.0) return drawn;

        DrawTimeMs = renderedFrames > 0 ? renderSeconds * 1000.0 / renderedFrames : 0;
        RenderFps = renderSeconds > 0 ? (Single)(renderedFrames / renderSeconds) : 0;
        GpuTimeMs = renderedFrames > 0 ? gpuMilliseconds / renderedFrames : 0;

        renderWindow = 0;
        renderSeconds = 0;
        renderedFrames = 0;
        gpuMilliseconds = 0;
        return drawn;
    }

    private void InitializeBeforeRun()
    {
        if (IsInitialized) return;
            
        if (Mode == UniverseMode.Standalone)
        {
            GraphicsDeviceService.CreateMainDevice("");
        }

        cancellationTokenSource = new CancellationTokenSource();
            
        EntityWorld.Initialize();

        InitializeCore();
        LoadContentCore();
        OnInitialized();
    }

    private void InitializeCore()
    {
        GraphicsDeviceService.DeviceCreated += GraphicsDeviceCreated;
        GraphicsDeviceService.DeviceDisposing += GraphicsDeviceDisposing;
        GraphicsDeviceService.DeviceChangeEnd += GraphicsDeviceChanged;
        Initialize();
    }

    private void GraphicsDeviceChanged(object sender, EventArgs e)
    {
        deviceResourcesLost = true;
    }

    // Textures died with the old device and are loaded again; the scene itself stays.
    private void ReloadMaterialTextures()
    {
        var options = new ContentLoadOptions
        {
            AllowDuplication = false,
            IgnoreRootDirectory = true
        };

        var roots = EntityWorld.RootEntities;
        for (int i = 0; i < roots.Count; i++)
        {
            roots[i].TraverseByLayer(entity =>
            {
                var material = entity.GetComponent<Material>();
                if (material?.Texture is { IsDisposed: true } && !string.IsNullOrEmpty(material.TexturePath))
                {
                    material.Texture = Content.Load<Texture>(material.TexturePath, options);
                }
            });
        }
    }

    private void LoadContentCore()
    {
        ContentLoading?.Invoke(this, EventArgs.Empty);
        LoadContent();
    }

    private void GraphicsDeviceCreated(object sender, EventArgs e)
    {
        LoadContentCore();
    }

    private void GraphicsDeviceDisposing(object sender, EventArgs e)
    {
        // Unsubscribed meanwhile, so the unload cannot recurse into it.
        GraphicsDeviceService.DeviceDisposing -= GraphicsDeviceDisposing;
        unloadContentCollector.DisposeAndClear();
        ContentUnloading?.Invoke(this, e);
        UnloadContent();
        GraphicsDeviceService.DeviceDisposing += GraphicsDeviceDisposing;
    }

    /// <summary>
    /// Method for updating the universe's logic
    /// </summary>
    /// <param name="time">AppTime contains elapsed time, total time and FPS</param>
    protected virtual void Update(AppTime time)
    {
        platform.UpdateInput(time);
        if (Satellites.TryGet<IInputRouting>(out var routing))
        {
            InputActions.Update(routing.KeyboardInput, routing.PointerInput);
        }

        EntityWorld.ServiceManager.Update(time);
    }

    /// <summary>
    /// Method for preparation before drawing
    /// </summary>
    protected virtual bool BeginScene()
    {
        return GraphicsDeviceService.IsReady;
    }

    /// <summary>
    /// Method for drawing operation
    /// </summary>
    /// <param name="time">AppTime contains elapsed time from the last frame, total time and current FPS</param>
    /// <returns>True when anything was drawn: an output that is hidden or has no area yet draws nothing.</returns>
    protected virtual bool Draw(AppTime time)
    {
        return EntityWorld.ServiceManager.Draw(time);
    }

    /// <summary>
    /// Method for after drawing measures
    /// </summary>
    protected virtual void EndScene()
    {
        //Parallel.ForEach(platform.Windows, window => window.DisplayContent());
        foreach (var output in platform.Outputs)
        {
            if (output.IsDrawnThisFrame)
            {
                output.DisplayContent();
            }
        }

        //EntityWorld.ServiceManager.Submit();
        //EntityWorld.ServiceManager.Present();
        EntityWorld.ServiceManager.OnFrameEnded();
    }

    /// <summary>
    /// Called at the end of the universe to free all its resources
    /// </summary>
    protected virtual void UnloadContent()
    {

    }

    /// <summary>
    /// Applies the output and device changes at the start of the frame.
    /// </summary>
    protected virtual void MakePreparations()
    {
        isSimulationPaused = simulationPauseRequested;
        platform.MakePreparationsForNextFrame();
        if (deviceResourcesLost)
        {
            deviceResourcesLost = false;
            ReloadMaterialTextures();
        }
        OutputsSettled?.Invoke(this, EventArgs.Empty);
    }

    private void DisposeGraphicsDeviceEvents()
    {
        if (GraphicsDeviceService != null)
        {
            GraphicsDeviceService.DeviceCreated -= GraphicsDeviceCreated;
            GraphicsDeviceService.DeviceDisposing -= GraphicsDeviceDisposing;
            GraphicsDeviceService.DeviceChangeEnd -= GraphicsDeviceChanged;
        }
    }

    private void FreeResources()
    {
        lock (this)
        {
            // Nothing of this universe may still be in flight on the GPU when its resources go.
            GraphicsDeviceService?.MainGraphicsDevice?.DeviceWaitIdle();
            contextsMapping.Clear();
            platform?.Dispose();
            Content.Unload();
            DisposeGraphicsDeviceEvents();

            // Only a service this universe made is its to dispose; a hosted universe shares its host's.
            if (Mode == UniverseMode.Standalone)
            {
                (GraphicsDeviceService as IDisposable)?.Dispose();
            }
        }
    }
        
    /// <summary>
    /// Add <see cref="IDisposable"/> instance to dispose list, which will be disposed and cleared when UnloadContent will be called
    /// </summary>
    /// <param name="disposeArg">Instance which implements <see cref="IDisposable"/></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    protected T ToDisposeContent<T>(T disposeArg) where T : IDisposable
    {
        return unloadContentCollector.Collect(disposeArg);
    }

    private void OnOutputCreated(UniverseOutput output)
    {
        var entity = new CameraTemplate().BuildEntity(
            null, $"Main camera for {output.Name}", Vector3.Zero, Vector3.ForwardLH, -Vector3.Up,
            output.Width, output.Height, 0.1f, 1000000.0f);
        entity.Transform.Position = new Vector3(0, -3, -20);
        var camera = entity.GetComponent<Camera>();
        camera.SetFreeLookAt(Vector3.Zero);
        EntityWorld.EntityManager.AddEntity(entity);
        output.OwnCamera = camera;
        output.Camera = camera;
    }

    private void OnOutputRemoved(UniverseOutput output)
    {
        RemoveRenderProcessor(output);
        if (output.OwnCamera?.Owner is { } ownCamera)
        {
            EntityWorld.EntityManager.RemoveEntity(ownCamera);
        }

        if (platform.Outputs.Count == 0 && ShutDownMode == ShutDownMode.OnLastWindowClosed)
        {
            ShutDown();
        }
    }
        
    /// <summary>
    /// Finish the loop and shut the universe down
    /// </summary>
    public virtual void ShutDown()
    {
        cancellationTokenSource?.Cancel();

        if (hostedLoop != null)
        {
            if (Thread.CurrentThread != hostedLoop)
            {
                hostedLoop.Join();
            }

            return;
        }

        // A universe driven by its host has no loop of its own to finish in: it stops here.
        if (Mode != UniverseMode.Standalone)
        {
            OnStopped();
        }
    }

    private void OnStopped()
    {
        if (isStopped)
        {
            return;
        }

        isStopped = true;
        messages.Writer.TryComplete();
        ShuttingDown?.Invoke(this, EventArgs.Empty);
        FreeResources();
        Stopped?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Load content at startup after resources initialization
    /// </summary>
    protected virtual void LoadContent() { }

    public event EventHandler Initialized;
    public event EventHandler FrameFinished;
    public event EventHandler OutputsSettled;
    public event EventHandler<EventArgs> Started;
    public event EventHandler<EventArgs> ShuttingDown;
    public event EventHandler<EventArgs> Stopped;
    public event EventHandler Paused;
    public event EventHandler Resumed;
    public event EventHandler<EventArgs> ContentLoading;
    public event EventHandler<EventArgs> ContentUnloading;
        
}
using System;
using System.Collections.Generic;
using Adamantium.Core;
using Adamantium.Core.DependencyInjection;
using Adamantium.ECS;
using Adamantium.Multiverse;

namespace Adamantium.EngineTests;

/// <summary>A universe that runs no frames: the test adds the outputs and says when they are settled.</summary>
public class TestUniverse : IUniverse
{
    private readonly List<UniverseOutput> outputs = [];

    public TestUniverse()
    {
        Satellites = new Satellites();
        Container = new AdamantiumDependencyContainer();
        EntityWorld = new EntityWorld(Container, Satellites);
    }

    public EntityWorld EntityWorld { get; }

    public Satellites Satellites { get; }

    public IReadOnlyList<UniverseOutput> Outputs => outputs;

    public UniverseOutput MainOutput => outputs.Count > 0 ? outputs[0] : null;

    public IDependencyContainer Container { get; }

    public bool IsSimulationPaused { get; set; }

    public bool IsFixedTimeStep { get; set; }

    public double TimeStep => 0;

    public uint DesiredFPS { get; set; }

    public ShutDownMode ShutDownMode { get; set; }

    public UniverseMode Mode => UniverseMode.Slave;

    public string Title { get; set; }

    public bool IsRunning => false;

    public bool IsInitialized => true;

    public bool IsPaused => false;

    public void Add(UniverseOutput output)
    {
        outputs.Add(output);
    }

    public void Settle()
    {
        OutputsSettled?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Says the universe is initialized, as a real one does once its services exist.</summary>
    public void RaiseInitialized()
    {
        Initialized?.Invoke(this, EventArgs.Empty);
    }

    public void InitializeUniverse()
    {
    }

    public void Submit()
    {
    }

    public void StartHostedLoop()
    {
    }

    public bool RunsOwnLoop => false;

    public void SuspendHostedLoop()
    {
    }

    public void ResumeHostedLoop()
    {
    }

    public FrameMode FrameMode { get; set; }

    public void Invalidate()
    {
    }

    public void Invalidate(UniverseOutput output)
    {
    }

    public void Post(Action action)
    {
        action();
    }

    public void Run()
    {
    }

    public void Run(object context)
    {
    }

    public void RunOnce(AppTime time)
    {
    }

    public void ShutDown()
    {
    }

    public void Pause()
    {
    }

    public void Resume()
    {
    }

    public event EventHandler OutputsSettled;

    public event EventHandler Initialized;

    public event EventHandler FrameFinished { add { } remove { } }

    public event EventHandler<EventArgs> Started { add { } remove { } }

    public event EventHandler<EventArgs> ShuttingDown { add { } remove { } }

    public event EventHandler<EventArgs> Stopped { add { } remove { } }

    public event EventHandler Paused { add { } remove { } }

    public event EventHandler Resumed { add { } remove { } }

    public event EventHandler<EventArgs> ContentLoading { add { } remove { } }

    public event EventHandler<EventArgs> ContentUnloading { add { } remove { } }
}

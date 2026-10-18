using System;
using System.Collections.Generic;
using Adamantium.Engine.Managers;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Multiverse;
using Adamantium.Multiverse.Input;

namespace Adamantium.Engine;

/// <summary>
/// The editor's view of the universe: its outputs, the ones taking keyboard and pointer, and the cameras seen through
/// them. An immutable snapshot taken once per frame after the outputs settle, readable from any thread.
/// </summary>
public class Observatory : IInputRouting
{
    private static readonly int StateCount = Enum.GetValues<OutputState>().Length;

    private readonly IUniverse universe;
    private readonly List<UniverseOutput> scratchOutputs = [];
    private readonly List<UniverseOutput>[] scratchByState = new List<UniverseOutput>[StateCount];
    private readonly List<Camera> scratchCurrentCameras = [];
    private readonly List<Camera> scratchAllCameras = [];
    private volatile Snapshot snapshot;

    public Observatory(IUniverse universe, EntityWorld entityWorld)
    {
        this.universe = universe;
        for (int i = 0; i < scratchByState.Length; i++)
        {
            scratchByState[i] = [];
        }
        CameraGizmo = new CameraGizmo(entityWorld);
        TakeSnapshot();
        universe.OutputsSettled += OnOutputsSettled;
        universe.Satellites.Add<IInputRouting>(this);
    }

    public CameraGizmo CameraGizmo { get; }

    /// <summary>
    /// The one camera the user's mouse and camera keys move, as an editor's own camera is; any other camera an output
    /// shows is only looked through. Null - the default - lets them move whichever camera the output shows.
    /// </summary>
    public Camera ControlledCamera
    {
        get => controlledCamera;
        set => controlledCamera = value;
    }

    private volatile Camera controlledCamera;

    /// <summary>Whether the user's input moves <paramref name="camera"/>: see <see cref="ControlledCamera"/>.</summary>
    public bool Controls(Camera camera) => controlledCamera == null || camera == controlledCamera;

    public IReadOnlyList<UniverseOutput> Outputs => snapshot.Outputs;

    /// <summary>
    /// The output taking the keyboard - keyboard focus in the active OS window, keyboard enabled; null if none or while
    /// the simulation is paused.
    /// </summary>
    public UniverseOutput KeyboardOutput => snapshot.KeyboardOutput;

    /// <summary>
    /// The output under the pointer, or holding it during a drag, mouse enabled; null if none or while the simulation is
    /// paused. May differ from <see cref="KeyboardOutput"/>.
    /// </summary>
    public UniverseOutput PointerOutput => snapshot.PointerOutput;

    public InputWormhole KeyboardInput => KeyboardOutput?.Input;

    public InputWormhole PointerInput => PointerOutput?.Input;

    public IReadOnlyList<UniverseOutput> VisibleOutputs => GetOutputs(OutputState.Shown);

    /// <summary>
    /// The camera each visible output looks through, each once - to process them all at once.
    /// </summary>
    public IReadOnlyList<Camera> CurrentCameras => snapshot.CurrentCameras;

    /// <summary>
    /// Every viewpoint of every output, current or not, each once.
    /// </summary>
    public IReadOnlyList<Camera> AllCameras => snapshot.AllCameras;

    public IReadOnlyList<UniverseOutput> GetOutputs(OutputState state)
    {
        return snapshot.ByState[(int)state];
    }

    private void OnOutputsSettled(object sender, EventArgs e)
    {
        TakeSnapshot();
    }

    private void TakeSnapshot()
    {
        UniverseOutput keyboardOutput = null;
        UniverseOutput pointerOutput = null;
        var routesInput = !universe.IsSimulationPaused;

        scratchOutputs.Clear();
        for (int i = 0; i < scratchByState.Length; i++)
        {
            scratchByState[i].Clear();
        }
        scratchAllCameras.Clear();

        var outputs = universe.Outputs;
        for (int i = 0; i < outputs.Count; i++)
        {
            var output = outputs[i];
            scratchOutputs.Add(output);
            scratchByState[(int)output.State].Add(output);

            if (routesInput && keyboardOutput == null && output.IsKeyboardFocused
                && output.Input is { IsKeyboardEnabled: true })
            {
                keyboardOutput = output;
            }

            if (routesInput && pointerOutput == null && output.IsPointerOver && output.Input is { IsMouseEnabled: true })
            {
                pointerOutput = output;
            }

            var cameras = output.Cameras;
            for (int j = 0; j < cameras.Count; j++)
            {
                if (!scratchAllCameras.Contains(cameras[j]))
                {
                    scratchAllCameras.Add(cameras[j]);
                }
            }
        }
        universe.CollectCurrentCameras(scratchCurrentCameras);

        var current = snapshot;
        if (current != null
            && ReferenceEquals(current.KeyboardOutput, keyboardOutput)
            && ReferenceEquals(current.PointerOutput, pointerOutput)
            && SameItems(current.Outputs, scratchOutputs)
            && SameStates(current.ByState)
            && SameItems(current.CurrentCameras, scratchCurrentCameras)
            && SameItems(current.AllCameras, scratchAllCameras))
        {
            return;
        }

        var byState = new UniverseOutput[StateCount][];
        for (int i = 0; i < byState.Length; i++)
        {
            byState[i] = scratchByState[i].ToArray();
        }

        snapshot = new Snapshot(
            scratchOutputs.ToArray(),
            byState,
            keyboardOutput,
            pointerOutput,
            scratchCurrentCameras.ToArray(),
            scratchAllCameras.ToArray());
    }

    private bool SameStates(UniverseOutput[][] published)
    {
        for (int i = 0; i < published.Length; i++)
        {
            if (!SameItems(published[i], scratchByState[i]))
            {
                return false;
            }
        }
        return true;
    }

    private static bool SameItems<T>(T[] published, List<T> taken) where T : class
    {
        if (published.Length != taken.Count)
        {
            return false;
        }

        for (int i = 0; i < published.Length; i++)
        {
            if (!ReferenceEquals(published[i], taken[i]))
            {
                return false;
            }
        }
        return true;
    }

    private sealed class Snapshot
    {
        public Snapshot(
            UniverseOutput[] outputs,
            UniverseOutput[][] byState,
            UniverseOutput keyboardOutput,
            UniverseOutput pointerOutput,
            Camera[] currentCameras,
            Camera[] allCameras)
        {
            Outputs = outputs;
            ByState = byState;
            KeyboardOutput = keyboardOutput;
            PointerOutput = pointerOutput;
            CurrentCameras = currentCameras;
            AllCameras = allCameras;
        }

        public UniverseOutput[] Outputs { get; }

        public UniverseOutput[][] ByState { get; }

        public UniverseOutput KeyboardOutput { get; }

        public UniverseOutput PointerOutput { get; }

        public Camera[] CurrentCameras { get; }

        public Camera[] AllCameras { get; }
    }
}

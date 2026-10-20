using Adamantium.Core;
using Adamantium.Core.DependencyInjection;
using Adamantium.ECS;

namespace Adamantium.Multiverse
{
    public interface IUniverse : IAdamantiumApplication
    {
        EntityWorld EntityWorld { get; }

        /// <summary>
        /// What this universe has its own of, as opposed to what the whole process shares through <see cref="Container"/>.
        /// </summary>
        Satellites Satellites { get; }

        IReadOnlyList<UniverseOutput> Outputs { get; }
        
        UniverseOutput MainOutput { get; }

        public void InitializeUniverse();

        public void Submit();

        /// <summary>
        /// Runs a universe hosted by another platform on a thread of its own, at the pace its outputs are shown, instead of
        /// being driven by its host with <see cref="IAdamantiumApplication.RunOnce"/>.
        /// </summary>
        public void StartHostedLoop();

        /// <summary>Whether this universe runs its own loop (see <see cref="StartHostedLoop"/>).</summary>
        public bool RunsOwnLoop { get; }

        /// <summary>Holds the own loop between two frames, once the frame in progress is done, until
        /// <see cref="ResumeHostedLoop"/>.</summary>
        public void SuspendHostedLoop();

        /// <summary>Lets the loop held by <see cref="SuspendHostedLoop"/> go on.</summary>
        public void ResumeHostedLoop();

        /// <summary>How the own loop paces its frames; set from any thread.</summary>
        public FrameMode FrameMode { get; set; }

        /// <summary>Asks for a frame of every output: what the world shows has changed. From any thread; under
        /// <see cref="Multiverse.FrameMode.OnDemand"/> the loop sleeps until something asks.</summary>
        public void Invalidate();

        /// <summary>Asks for a frame of <paramref name="output"/> alone: what changed is its own - its input, its size, its
        /// camera. From any thread.</summary>
        public void Invalidate(UniverseOutput output);

        /// <summary>Runs <paramref name="action"/> on the universe's thread before its next frame, then draws every output:
        /// how anything outside the loop changes the world, with no race against a frame. From any thread.</summary>
        public void Post(Action action);

        /// <summary>
        /// Stops the game while drawing goes on: no time passes for the services and no input reaches them. Set from
        /// any thread; the change takes effect with the next frame.
        /// </summary>
        public bool IsSimulationPaused { get; set; }

        /// <summary>
        /// The container the whole process shares.
        /// </summary>
        public IDependencyContainer Container { get; }

        /// <summary>
        /// Enables or disables fixed framerate
        /// </summary>
        public Boolean IsFixedTimeStep { get; set; }

        /// <summary>
        /// Seconds per frame at <see cref="DesiredFPS"/>.
        /// </summary>
        public Double TimeStep { get; }
        
        /// <summary>
        /// Desired number of frames per second
        /// </summary>
        UInt32 DesiredFPS { get; set; }
        
        /// <summary>
        /// Condition on which the loop will be exited
        /// </summary>
        ShutDownMode ShutDownMode { get; set; }
        
        UniverseMode Mode { get; }
        
        /// <summary>
        /// Title of the universe to show in the window title bar
        /// </summary>
        String Title { get; set; }

        public event EventHandler Initialized;

        public event EventHandler FrameFinished;

        /// <summary>
        /// Raised once per frame, before the update, when the frame's outputs are settled: added, removed and resized.
        /// </summary>
        public event EventHandler OutputsSettled;
    }
}
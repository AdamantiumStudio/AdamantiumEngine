using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;

namespace Adamantium.Multiverse
{
    public interface IUniversePlatform
    {
        String DefaultAppDirectory { get; }

        UniverseOutput MainWindow { get; }

        IReadOnlyList<UniverseOutput> Outputs { get; }

        bool HasOutputs { get; }

        /// <summary>The gamepads of the machine, polled once a frame.</summary>
        Input.GamepadHub Gamepads { get; }

        void Run(CancellationToken token);

        /// <summary>
        /// Creates an output in a window of its own.
        /// </summary>
        UniverseOutput CreateOutput(uint width = 1280, uint height = 720);

        /// <summary>
        /// Creates an output on the control in <paramref name="context"/>.
        /// </summary>
        UniverseOutput CreateOutput(OutputContext context);

        /// <summary>
        /// Creates an output on <paramref name="context"/>, a control.
        /// </summary>
        UniverseOutput CreateOutput(Object context);

        /// <summary>
        /// Creates an output on <paramref name="context"/> with the given formats, or returns the one it already has.
        /// </summary>
        UniverseOutput CreateOutput(Object context, SurfaceFormat surfaceFormat, DepthFormat depthFormat = DepthFormat.Depth32Stencil8X24, MSAALevel msaaLevel = MSAALevel.None);

        /// <summary>
        /// Moves drawing from the old control to the new one; the old control can then be removed.
        /// </summary>
        void SwitchContext(OutputContext oldContext, OutputContext newContext);

        /// <summary>
        /// Adds an output; it joins at the start of the next frame.
        /// </summary>
        void AddOutput(UniverseOutput window);

        /// <summary>
        /// Removes the output of <paramref name="context"/>.
        /// </summary>
        void RemoveOutput(OutputContext context);

        /// <summary>
        /// Removes the output of the control <paramref name="context"/>.
        /// </summary>
        void RemoveOutput(Object context);
    }
}

using Adamantium.Mathematics;

namespace Adamantium.Multiverse.Payloads
{
    public class UniverseOutputSizeChangedPayload
    {
        public Size<double> Size { get; }

        public UniverseOutput Output { get; }

        public UniverseOutputSizeChangedPayload(UniverseOutput output, Size<double> size)
        {
            Output = output;
            Size = size;
        }
    }
}
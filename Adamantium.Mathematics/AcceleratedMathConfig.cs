using System.Runtime.Intrinsics.X86;

namespace Adamantium.Mathematics
{
    public static class AcceleratedMathConfig
    {
        public static bool IsAvx2Supported { get; }
        
        public static bool IsAvxSupported { get; }
        
        public static bool IsSse42Supported { get; }
        
        public static bool IsSse41Supported { get; }
        
        public static bool IsSse3Supported { get; }
        
        public static bool IsSsse3Supported { get; }

        static AcceleratedMathConfig()
        {
            IsAvx2Supported = Avx2.IsSupported;
            IsAvxSupported = Avx.IsSupported;
            IsSse42Supported = Sse42.IsSupported;
            IsSse41Supported = Sse41.IsSupported;
            IsSsse3Supported = Ssse3.IsSupported;
            IsSse3Supported = Sse3.IsSupported;
        }
    }
}

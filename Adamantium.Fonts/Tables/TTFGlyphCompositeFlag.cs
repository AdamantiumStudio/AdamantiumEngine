namespace Adamantium.Fonts.Tables
{
    internal class TTFGlyphCompositeFlag
    {
        public bool Arg1And2AreWords { get; set; }
        public bool ArgsAreXYValues { get; set; }
        public bool WeHaveAScale { get; set; }
        public bool MoreComponents { get; set; }
        public bool WeHaveAnXAndYScale { get; set; }
        public bool WeHaveATwoByTwo { get; set; }
        public bool WeHaveInstructions { get; set; }
        public bool UseMyMetrics { get; set; }
        public bool ScaledComponentOffset { get; set; }
    };
}

using System;
using Adamantium.Mathematics;

namespace Adamantium.Fonts.Common
{
    public class CompositeGlyphComponent
    {
        public UInt16 SimpleGlyphIndex { get; set; } // index of simple glyph which is a part of composite glyph
        public Matrix3x2<double> TransformMatrix { get; set; } = Matrix3x2<double>.Identity;

        internal bool IsAnchored { get; set; }

        internal bool UseMyMetrics { get; set; }

        internal int ParentPoint { get; set; }

        internal int ChildPoint { get; set; }

        internal CompositeGlyphComponent() {}
    }
}

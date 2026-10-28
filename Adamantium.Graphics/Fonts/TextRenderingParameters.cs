using System;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Fonts
{
    public class TextRenderingParameters : IEquatable<TextRenderingParameters>
    {
        /// <summary>
        /// Defines text area inside render target
        /// </summary>
        public Rectangle<int> TextArea { get; set; }

        public TextWrapping TextWrapping { get; set; }

        public HorizontalTextAlignment HorizontalTextAlignment { get; set; }
        
        public VerticalTextAlignment VerticalTextAlignment { get; set; }

        /// <summary>
        /// When <see cref="HorizontalTextAlignment.Justify"/> is used, the last line of a block
        /// (and a single, only line) stays ragged by default, per typography convention. Set this
        /// to <c>true</c> to stretch the last/only line to the full width as well (text-align-last).
        /// </summary>
        public bool JustifyLastLine { get; set; }

        public TextTrimming TextTrimming { get; set; }
        
        public Color Color { get; set; }

        public bool Equals(TextRenderingParameters other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return TextArea.Equals(other.TextArea) && 
                   TextWrapping == other.TextWrapping &&
                   HorizontalTextAlignment == other.HorizontalTextAlignment &&
                   VerticalTextAlignment == other.VerticalTextAlignment &&
                   JustifyLastLine == other.JustifyLastLine &&
                   TextTrimming == other.TextTrimming &&
                   Color.Equals(other.Color);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((TextRenderingParameters)obj);
        }
        
        public override int GetHashCode()
        {
            return HashCode.Combine(TextArea, (int)TextWrapping, (int)HorizontalTextAlignment, (int)VerticalTextAlignment, JustifyLastLine, (int)TextTrimming, Color);
        }
        
        public static bool operator ==(TextRenderingParameters @paramA, TextRenderingParameters @paramB)
        {
            return paramA.Equals(@paramB);
        }

        public static bool operator !=(TextRenderingParameters paramA, TextRenderingParameters paramB)
        {
            return !@paramA.Equals(@paramB);
        }
    }
}

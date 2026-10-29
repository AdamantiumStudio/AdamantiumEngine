using System;

namespace Adamantium.Graphics
{
   /// <summary>How a sprite's texture is mirrored inside its rectangle; the two flips combine.</summary>
   [Flags]
   public enum SpriteEffects
   {
      None = 0,

      /// <summary>Left and right swapped.</summary>
      FlipHorizontally = 1,

      /// <summary>Top and bottom swapped.</summary>
      FlipVertically = 2,

      FlipBoth = FlipHorizontally | FlipVertically
   }
}

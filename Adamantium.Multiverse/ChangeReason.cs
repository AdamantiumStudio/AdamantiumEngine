namespace Adamantium.Multiverse
{
   /// <summary>
   /// Why the parameters of a <see cref="Graphics.Core.GraphicsPresenter"/> changed.
   /// </summary>
   public enum ChangeReason
   {
      /// <summary>
      /// The surface or the sample count changed: the output's device and <see cref="Graphics.Core.GraphicsPresenter"/> are made
      /// anew.
      /// </summary>
      FullUpdate,

      /// <summary>
      /// The size or a buffer format changed: the <see cref="Graphics.Core.GraphicsPresenter"/> rebuilds its buffers.
      /// </summary>
      Resize,
   }
}

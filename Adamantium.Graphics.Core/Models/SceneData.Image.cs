using System;
using MessagePack;

namespace Adamantium.Graphics.Core.Models;

public partial class SceneData
{
   public class Image
   {
      public Image()
      {

      }

      public Image(String imageName, String filePath)
      {
         ImageName = imageName;
         FilePath = filePath;
      }

      public String ID { get; set; }

      /// <summary>The image reference exactly as the model file wrote it - relative and portable.</summary>
      public String ImageName { get; set; }

      /// <summary>Where the image sits ON THIS MACHINE. Never written to the artifact: an absolute path of one
      /// machine inside a portable file points at nothing after the first move, and the reader looks the image up
      /// beside the model by <see cref="ImageName"/> anyway.</summary>
      [IgnoreMember]
      public String FilePath { get; set; }

      /// <summary>The image's encoded bytes - a PNG or a JPEG - when the model carries it inside, as a <c>.glb</c>
      /// does; null for one in a file of its own. Written to the artifact with the model.</summary>
      public byte[] Data { get; set; }

      /// <summary>What <see cref="Data"/> is encoded as, <c>image/png</c> say; null if the model did not say.</summary>
      public String MimeType { get; set; }
   }
}
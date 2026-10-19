namespace Adamantium.Graphics.Core.Models;

public enum UpAxis
{
   X_UP = 0,
   Y_UP_RH = 1,
   Z_UP = 2,
   Y_UP_LH = 3,
   Y_DOWN_RH = 4,

   /// <summary>Y up, right-handed, with the front of a model facing +Z, as glTF has it.</summary>
   Y_UP_RH_FRONT_Z = 5
}
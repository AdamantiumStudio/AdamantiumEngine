using System;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.ECS.Components
{
   public static class AnimationHelper
   {
      public static Int32 FindKeyIndex(Int32 startFrame, Int32 endFrame, Double seconds, SceneData.FrameCollection keyFrames, CalculateKeyFrameOptions option = CalculateKeyFrameOptions.Forward)
      {
         Int32 keyIndex = 0;
         if (option == CalculateKeyFrameOptions.Forward)
         {
            if (seconds < keyFrames[endFrame].TimeStamp)
            {
               for (int i = startFrame; i < endFrame-1; i++)
               {
                  if (seconds > keyFrames[endFrame].TimeStamp)
                  {
                     keyIndex = endFrame-1;
                     break;
                  }

                  if (seconds >= keyFrames[i].TimeStamp && seconds < keyFrames[i+1].TimeStamp)
                  {
                     keyIndex = i;
                     break;
                  }
               }
            }
            else
            {
               keyIndex = endFrame-1;
            }
         }
         else if (option == CalculateKeyFrameOptions.Backward)
         {
            if (seconds > keyFrames[endFrame].TimeStamp)
            {
               for (int i = startFrame; i >= endFrame+1; i--)
               {
                  if (seconds < keyFrames[endFrame].TimeStamp)
                  {
                     keyIndex = endFrame;
                     break;
                  }

                  if (seconds <= keyFrames[i].TimeStamp && seconds>keyFrames[i-1].TimeStamp)
                  {
                     keyIndex = i;
                     break;
                  }
               }
            }
            else
            {
               keyIndex = endFrame+1;
            }
         }
         

         return keyIndex;
      }

      public static double CalculateCurrentFrameTime(double passedAnimationTime, double framestartTime, double frameEndTime)
      {
         return (passedAnimationTime - framestartTime) / (frameEndTime - framestartTime);
      }

      public static Matrix4x4F InterpolateFrames(SceneData.KeyFrame firstKeyFrame, SceneData.KeyFrame secondKeyFrame, double time)
      {
         Vector3F interpolatedScale = Vector3F.Lerp(firstKeyFrame.Scale, secondKeyFrame.Scale, (float)time);
         Vector3F interpolatedTranslation = Vector3F.Lerp(firstKeyFrame.Position, secondKeyFrame.Position, (float)time);
         QuaternionF interpolatedQuaternion = QuaternionF.Slerp(firstKeyFrame.Rotation, secondKeyFrame.Rotation, (float)time);
         return Matrix4x4.CreateScale<float>(interpolatedScale) * Matrix4x4.CreateFromQuaternion<float>(interpolatedQuaternion) *
                Matrix4x4.CreateTranslation<float>(interpolatedTranslation);
      }
   }
}

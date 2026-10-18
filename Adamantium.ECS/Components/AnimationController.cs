using System;
using System.Collections.Generic;
using Adamantium.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using Serilog;

namespace Adamantium.ECS.Components
{
   [ViewModel]
   public partial class AnimationController : ActivatableComponent
   {
      public AnimationController()
      {
         BindShapeMatrix = Matrix4x4F.Identity;
         ControllerId = String.Empty;
         JointDictionary = new Dictionary<string, Matrix4x4F>();
         FinalMatrices = new Dictionary<string, Matrix4x4F>();
      }

      public Matrix4x4F BindShapeMatrix { get; set; }

      [Bindable]
      public partial String ControllerId { get; set; }

      /// <summary>
      /// Contains a set of joints with its IBM`s (inverse bind matrices)
      /// </summary>
      public Dictionary<String, Matrix4x4F> JointDictionary { get; set; }

      public IEnumerable<String> JointNames { get { return JointDictionary.Keys; } }

      public IEnumerable<Matrix4x4F> JointMatrices { get { return JointDictionary.Values; } }

      public Dictionary<String, Matrix4x4F> FinalMatrices { get; }

      public void Update(AppTime appTime)
      {
         var animation = GetComponentInParents<AnimationComponent>();
         if (animation != null && animation.IsAnimationStarted && IsEnabled)
         {
            try
            {
               if (animation.InterpolatedMatrices.Count > 0)
               {
                  FinalMatrices.Clear();
                  Queue<SceneData.Joint> jointQueue = new Queue<SceneData.Joint>();
                  foreach (var matrix in JointDictionary)
                  {
                     var key = ControllerId + matrix.Key;
                     if (animation.InterpolatedMatrices.ContainsKey(key))
                     {
                        FinalMatrices.Add(matrix.Key, animation.InterpolatedMatrices[key]);
                     }
                  }

                  List<SceneData.Joint> rootJoints = null;
                  if (animation.Skeletons.ContainsKey(ControllerId))
                  {
                     rootJoints = animation.Skeletons[ControllerId];
                  }

                  //Etalon
                  foreach (var joint in rootJoints)
                  {
                     jointQueue.Enqueue(joint);
                     while (jointQueue.Count > 0)
                     {
                        SceneData.Joint currentJoint = jointQueue.Dequeue();
                        if (FinalMatrices.ContainsKey(currentJoint.JointSid))
                        {
                           FinalMatrices[currentJoint.JointSid] =
                              JointDictionary[currentJoint.JointSid] *
                              FinalMatrices[currentJoint.JointSid];
                           var parentJoint = currentJoint.ParentJoint;
                           while (parentJoint != null)
                           {
                              FinalMatrices[currentJoint.JointSid] *=
                                 animation.InterpolatedMatrices[ControllerId + parentJoint.JointSid];
                              parentJoint = parentJoint.ParentJoint;
                           }
                        }
                        foreach (var child in currentJoint.Children)
                        {
                           jointQueue.Enqueue(child);
                        }
                     }
                  }
               }
            }
            catch (Exception exception)
            {
               Log.Logger.Error(exception, "Joint matrix composition failed for controller {Controller}", ControllerId);
            }
         }
      }
   }

   
}

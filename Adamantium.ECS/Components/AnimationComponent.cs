using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using Serilog;

namespace Adamantium.ECS.Components
{

    [ViewModel]
    public partial class AnimationComponent : ActivatableComponent
    {
        public SceneData.AnimationCollection Animations { get; set; }

        public SceneData.SkeletonCollection Skeletons { get; set; }

        [Bindable]
        public partial Int32 StartingKeyFrame { get; set; }

        [Bindable]
        public partial Int32 EndingKeyFrame { get; set; }

        public TimeSpan AnimationStartTime { get; set; }

        public Boolean IsAnimationStarted { get; set; }

        [Bindable]
        public partial Double AnimationSpeed { get; set; }

        [Bindable]
        public partial Boolean IsPaused { get; set; }

        [Bindable]
        public partial CalculateOptions Option { get; set; }

        public AnimationComponent()
        {
            Animations = new SceneData.AnimationCollection();
            Skeletons = new SceneData.SkeletonCollection();
            AnimationSpeed = 1.0;

            InterpolatedMatrices = new Dictionary<string, Matrix4x4F>();
        }

        public AnimationComponent(AnimationComponent copy)
        {
            Animations = new SceneData.AnimationCollection(copy.Animations);
            Skeletons = new SceneData.SkeletonCollection(copy.Skeletons);
            StartingKeyFrame = copy.StartingKeyFrame;
            EndingKeyFrame = copy.EndingKeyFrame;
            Option = CalculateOptions.CyclicForward;
            InterpolatedMatrices = new Dictionary<string, Matrix4x4F>();
        }

        public Dictionary<string, Matrix4x4F> InterpolatedMatrices { get; private set; }

        public void Update(AppTime appTime)
        {
            if (!IsPaused)
            {
                try
                {
                    var currentTime = appTime.TotalTime;
                    InterpolatedMatrices.Clear();


                    if (IsAnimationStarted == false)
                    {
                        IsAnimationStarted = true;
                        AnimationStartTime = currentTime;
                    }

                    Double duration = currentTime.TotalSeconds - AnimationStartTime.TotalSeconds;
                    Double maxDuration = Animations.Values.ToArray()[0].Last().TimeStamp;
                    Double elapsed = 0;
                    switch (Option)
                    {
                        case CalculateOptions.CyclicForward:
                            elapsed = duration;
                            if (duration > maxDuration)
                            {
                                elapsed = 0;
                                AnimationStartTime = currentTime;
                            }

                            break;
                        case CalculateOptions.CyclicBackward:
                            elapsed = maxDuration - duration;
                            if (elapsed < 0)
                            {
                                elapsed = maxDuration;
                                AnimationStartTime = currentTime;
                            }
                            break;
                        case CalculateOptions.OneTimeBackward:
                            elapsed = maxDuration - duration;

                            if (elapsed > 0)
                            {
                                foreach (var animationData in Animations)
                                {
                                    StartingKeyFrame = animationData.Value.Count - 1;
                                    EndingKeyFrame = 1;
                                    var keyFrameIndex = AnimationHelper.FindKeyIndex(StartingKeyFrame,
                                       EndingKeyFrame,
                                       elapsed * AnimationSpeed,
                                       Animations[animationData.Key], CalculateKeyFrameOptions.Backward);
                                    //if (keyFrameIndex > animationData.Value.KeyFrames[EndingKeyFrame])
                                    {
                                        var time = AnimationHelper.CalculateCurrentFrameTime(elapsed * AnimationSpeed,
                                           Animations[animationData.Key][keyFrameIndex].TimeStamp,
                                           Animations[animationData.Key][keyFrameIndex - 1].TimeStamp);
                                        Matrix4x4F interpolated =
                                           AnimationHelper.InterpolateFrames(
                                              Animations[animationData.Key][keyFrameIndex],
                                              Animations[animationData.Key][keyFrameIndex - 1], time);
                                        InterpolatedMatrices.Add(animationData.Key, interpolated);
                                    }
                                    //else
                                    //{
                                    //   InterpolatedMatrices.Add(animationData.Key,
                                    //      Animations[animationData.Key].Frames[keyFrameIndex].ComposeMatrix());
                                    //}
                                }
                            }
                            break;

                        case CalculateOptions.OneTimeForward:
                            elapsed = duration;
                            if (elapsed < maxDuration)
                            {
                                foreach (var animationData in Animations)
                                {
                                    StartingKeyFrame = 1;
                                    EndingKeyFrame = animationData.Value.Count - 1;
                                    var keyFrameIndex = AnimationHelper.FindKeyIndex(StartingKeyFrame, EndingKeyFrame,
                                       elapsed * AnimationSpeed,
                                       Animations[animationData.Key]);
                                    if (keyFrameIndex != -1)
                                    {
                                        var time = AnimationHelper.CalculateCurrentFrameTime(elapsed * AnimationSpeed,
                                           Animations[animationData.Key][keyFrameIndex].TimeStamp,
                                           Animations[animationData.Key][keyFrameIndex + 1].TimeStamp);
                                        Matrix4x4F interpolated =
                                           AnimationHelper.InterpolateFrames(
                                              Animations[animationData.Key][keyFrameIndex],
                                              Animations[animationData.Key][keyFrameIndex + 1], time);
                                        InterpolatedMatrices.Add(animationData.Key, interpolated);
                                    }
                                }
                            }
                            break;

                        case CalculateOptions.CyclicForwardBackward:

                            break;
                    }


                    if (Option == CalculateOptions.CyclicForward)
                    {
                        foreach (var animationData in Animations)
                        {
                            StartingKeyFrame = 1;
                            EndingKeyFrame = animationData.Value.Count - 1;
                            var keyFrameIndex = AnimationHelper.FindKeyIndex(StartingKeyFrame, EndingKeyFrame,
                               elapsed * AnimationSpeed,
                               Animations[animationData.Key], CalculateKeyFrameOptions.Forward);
                            //if (keyFrameIndex < animationData.Value.KeyFrames.Count - 1)
                            {
                                var time = AnimationHelper.CalculateCurrentFrameTime(elapsed * AnimationSpeed,
                                   Animations[animationData.Key][keyFrameIndex].TimeStamp,
                                   Animations[animationData.Key][keyFrameIndex + 1].TimeStamp);
                                Matrix4x4F interpolated =
                                   AnimationHelper.InterpolateFrames(
                                      Animations[animationData.Key][keyFrameIndex],
                                      Animations[animationData.Key][keyFrameIndex + 1], time);
                                InterpolatedMatrices.Add(animationData.Key, interpolated);
                            }
                            //else
                            //{
                            //   InterpolatedMatrices.Add(animationData.Key,
                            //      Animations[animationData.Key].Frames[keyFrameIndex].ComposeMatrix());
                            //   AnimationStartTime = currentTime;
                            //}
                        }
                    }
                    else if (Option == CalculateOptions.CyclicBackward)
                    {
                        foreach (var animationData in Animations)
                        {
                            StartingKeyFrame = animationData.Value.Count - 1;
                            EndingKeyFrame = 1;
                            var keyFrameIndex = AnimationHelper.FindKeyIndex(StartingKeyFrame, EndingKeyFrame,
                               elapsed * AnimationSpeed,
                               Animations[animationData.Key], CalculateKeyFrameOptions.Backward);
                            //if (keyFrameIndex > animationData.Value.KeyFrames[EndingKeyFrame])
                            {
                                var time = AnimationHelper.CalculateCurrentFrameTime(elapsed * AnimationSpeed,
                                   Animations[animationData.Key][keyFrameIndex].TimeStamp,
                                   Animations[animationData.Key][keyFrameIndex - 1].TimeStamp);
                                Matrix4x4F interpolated =
                                   AnimationHelper.InterpolateFrames(
                                      Animations[animationData.Key][keyFrameIndex],
                                      Animations[animationData.Key][keyFrameIndex - 1], time);
                                InterpolatedMatrices.Add(animationData.Key, interpolated);
                            }
                            //else
                            //{
                            //   InterpolatedMatrices.Add(animationData.Key,
                            //      Animations[animationData.Key].Frames[keyFrameIndex].ComposeMatrix());
                            //   AnimationStartTime = currentTime;
                            //}
                        }
                    }
                }
                catch (Exception exception)
                {
                    Log.Logger.Error(exception, "Animation frame interpolation failed");
                }
            }
        }
    }
}

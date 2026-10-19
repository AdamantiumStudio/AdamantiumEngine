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
        /// <summary>The frames of the clip playing - <see cref="CurrentClip"/>'s - by what they move.</summary>
        public SceneData.AnimationCollection Animations { get; set; }

        public SceneData.SkeletonCollection Skeletons { get; set; }

        /// <summary>The clips the model brings - walking, running, jumping - by name.</summary>
        public SceneData.AnimationSetCollection Clips { get; set; }

        /// <summary>The clip playing, by its name in <see cref="Clips"/>; choosing another starts it from its beginning.</summary>
        [Bindable]
        public partial String CurrentClip { get; set; }

        /// <summary>The names of <see cref="Clips"/>, as a list to choose from.</summary>
        public IEnumerable<String> ClipNames => Clips.Keys;

        /// <summary>The entities the clips pose, by the key their frames go by - a node of the model. A clip with any
        /// of them poses those nodes; one with none plays as joints of a skeleton (<see cref="InterpolatedMatrices"/>).</summary>
        public Dictionary<String, Entity> Targets { get; } = new();

        /// <summary>How each of <see cref="Targets"/> stands with no clip on it, by the same key: what a node takes back
        /// when the clip moving it stops, so no pose of one clip is left behind in another.</summary>
        public Dictionary<String, SceneData.KeyFrame> RestPoses { get; } = new();

        // Chosen on the UI thread, played on the loop's: the targets are stood at rest where they are posed.
        partial void OnCurrentClipChanged(String value)
        {
            Animations = value != null && Clips.TryGetValue(value, out var clip) ? clip : new SceneData.AnimationCollection();
            IsAnimationStarted = false;
            restPending = true;
        }

        private volatile bool restPending;

        private void RestIfClipChanged()
        {
            if (restPending)
            {
                restPending = false;
                Rest();
            }
        }

        /// <summary>Stands every target in its <see cref="RestPoses">rest pose</see>.</summary>
        public void Rest()
        {
            foreach (var (key, rest) in RestPoses)
            {
                if (Targets.TryGetValue(key, out var target))
                {
                    target.Transform.SetPose((Vector3)rest.Position, rest.Rotation, rest.Scale);
                }
            }
        }

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
            Clips = new SceneData.AnimationSetCollection();
            AnimationSpeed = 1.0;

            InterpolatedMatrices = new Dictionary<string, Matrix4x4F>();
        }

        public AnimationComponent(AnimationComponent copy)
        {
            Animations = new SceneData.AnimationCollection(copy.Animations);
            Skeletons = new SceneData.SkeletonCollection(copy.Skeletons);
            Clips = new SceneData.AnimationSetCollection();
            foreach (var clip in copy.Clips)
            {
                Clips[clip.Key] = clip.Value;
            }

            CurrentClip = copy.CurrentClip;
            StartingKeyFrame = copy.StartingKeyFrame;
            EndingKeyFrame = copy.EndingKeyFrame;
            Option = CalculateOptions.CyclicForward;
            InterpolatedMatrices = new Dictionary<string, Matrix4x4F>();
        }

        public Dictionary<string, Matrix4x4F> InterpolatedMatrices { get; private set; }

        public void Update(AppTime appTime)
        {
            RestIfClipChanged();
            if (Animations.Count == 0)
            {
                return;
            }

            if (!IsPaused && Targets.Count > 0)
            {
                PoseTargets(appTime);
                return;
            }

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

        /// <summary>How long the clip playing runs, in seconds: to its latest key.</summary>
        public Double Duration
        {
            get
            {
                var duration = 0.0;
                foreach (var frames in Animations.Values)
                {
                    if (frames.Count > 0) duration = Math.Max(duration, frames[^1].TimeStamp);
                }

                return duration;
            }
        }

        private void PoseTargets(AppTime appTime)
        {
            if (!IsAnimationStarted)
            {
                IsAnimationStarted = true;
                AnimationStartTime = appTime.TotalTime;
            }

            var time = TimeAt((appTime.TotalTime - AnimationStartTime).TotalSeconds * AnimationSpeed, Duration, Option);
            if (time is { } at)
            {
                Pose(at);
            }
        }

        /// <summary>The time into a clip of <paramref name="duration"/> seconds that <paramref name="elapsed"/> seconds of
        /// play reach, played as <paramref name="option"/> says; null for <see cref="CalculateOptions.NoAnimation"/>.</summary>
        public static Double? TimeAt(Double elapsed, Double duration, CalculateOptions option)
        {
            if (duration <= 0)
            {
                return option == CalculateOptions.NoAnimation ? null : 0;
            }

            var there = elapsed % (2 * duration);
            var bounced = there <= duration ? there : 2 * duration - there;
            return option switch
            {
                CalculateOptions.NoAnimation => null,
                CalculateOptions.OneTimeForward => Math.Min(elapsed, duration),
                CalculateOptions.OneTimeBackward => Math.Max(duration - elapsed, 0),
                CalculateOptions.OneTimeForwardBackward => elapsed >= 2 * duration ? 0 : bounced,
                CalculateOptions.CyclicBackward => duration - elapsed % duration,
                CalculateOptions.CyclicForwardBackward => bounced,
                CalculateOptions.CyclicBackwardForward => duration - bounced,
                _ => elapsed % duration
            };
        }

        /// <summary>Stands every target as the clip playing has it at <paramref name="time"/> seconds into it; after
        /// another clip was chosen, those it does not move stand at rest.</summary>
        public void Pose(Double time)
        {
            RestIfClipChanged();
            foreach (var (key, frames) in Animations)
            {
                if (frames.Count == 0 || !Targets.TryGetValue(key, out var target)) continue;

                var pose = Sample(frames, time);
                target.Transform.SetPose((Vector3)pose.Position, pose.Rotation, pose.Scale);
            }
        }

        /// <summary>The pose <paramref name="frames"/> give at <paramref name="time"/>: held before the first key and after
        /// the last, stepped or blended between two as the earlier one says.</summary>
        public static SceneData.KeyFrame Sample(SceneData.FrameCollection frames, Double time)
        {
            if (time <= frames[0].TimeStamp) return frames[0];
            if (time >= frames[^1].TimeStamp) return frames[^1];

            int low = 0, high = frames.Count - 1;
            while (high - low > 1)
            {
                var middle = (low + high) / 2;
                if (frames[middle].TimeStamp <= time) low = middle;
                else high = middle;
            }

            var from = frames[low];
            var to = frames[high];
            if (from.Interpolation == InterpolationType.Step) return from;

            var span = to.TimeStamp - from.TimeStamp;
            var amount = span > 0 ? (float)((time - from.TimeStamp) / span) : 0;
            return new SceneData.KeyFrame(
                Vector3F.Lerp(from.Scale, to.Scale, amount),
                Vector3F.Lerp(from.Position, to.Position, amount),
                QuaternionF.Slerp(from.Rotation, to.Rotation, amount)) { TimeStamp = time };
        }
    }
}

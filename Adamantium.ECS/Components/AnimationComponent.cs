using System;
using System.Collections.Generic;
using Adamantium.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.MVVM;

namespace Adamantium.ECS.Components
{

    [ViewModel]
    public partial class AnimationComponent : ActivatableComponent
    {
        private volatile bool restPending;
        private bool paused;
        private bool switchedOff;
        private TimeSpan lastPlayed;
        private double played;

        /// <summary>The frames of the clip playing - <see cref="CurrentClip"/>'s - by what they move.</summary>
        public SceneData.AnimationCollection Animations { get; set; }

        /// <summary>The clips the model brings - walking, running, jumping - by name.</summary>
        public SceneData.AnimationSetCollection Clips { get; set; }

        /// <summary>The clip playing, by its name in <see cref="Clips"/>; choosing another starts it from its beginning.</summary>
        [Bindable]
        public partial String CurrentClip { get; set; }

        /// <summary>The names of <see cref="Clips"/>, as a list to choose from.</summary>
        public IEnumerable<String> ClipNames => Clips.Keys;

        /// <summary>The entities the clips pose, by the key their frames go by - a node of the model, a skeleton's
        /// joint among them.</summary>
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
                    target.Transform.SetPose(rest.Position.As<double>(), rest.Rotation, rest.Scale);
                }
            }
        }

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
            Clips = new SceneData.AnimationSetCollection();
            AnimationSpeed = 1.0;
        }

        public AnimationComponent(AnimationComponent copy)
        {
            Animations = new SceneData.AnimationCollection(copy.Animations);
            Clips = CopyClips(copy.Clips);
            CurrentClip = copy.CurrentClip;
            Option = CalculateOptions.CyclicForward;
        }

        public override void CloneValues(IComponent clone)
        {
            base.CloneValues(clone);
            var animation = (AnimationComponent)clone;
            animation.Clips = CopyClips(Clips);
            animation.Animations = Animations;
            animation.IsAnimationStarted = IsAnimationStarted;
            animation.AnimationStartTime = AnimationStartTime;
            animation.restPending = restPending;
            animation.paused = paused;
            animation.switchedOff = switchedOff;
            animation.lastPlayed = lastPlayed;
            animation.played = played;
            foreach (var (key, rest) in RestPoses)
            {
                animation.RestPoses[key] = rest;
            }
        }

        public override void CloneReferences(IComponent clone, IReadOnlyDictionary<Entity, Entity> copies)
        {
            var animation = (AnimationComponent)clone;
            foreach (var (key, target) in Targets)
            {
                animation.Targets[key] = CopyOf(target, copies);
            }
        }

        private static SceneData.AnimationSetCollection CopyClips(SceneData.AnimationSetCollection clips)
        {
            var copy = new SceneData.AnimationSetCollection();
            foreach (var (name, clip) in clips)
            {
                copy[name] = clip;
            }

            return copy;
        }

        /// <summary>Plays the clip on to <paramref name="appTime"/>. Paused, its clock stands still; switched off, its
        /// targets stand at rest once and it plays from the start when switched on again.</summary>
        public void Update(AppTime appTime)
        {
            RestIfClipChanged();
            if (!IsEnabled)
            {
                SwitchOff();
                return;
            }

            switchedOff = false;
            if (IsPaused)
            {
                paused = true;
                return;
            }

            var sincePlayed = appTime.TotalTime - lastPlayed;
            if (paused)
            {
                paused = false;
                AnimationStartTime += sincePlayed;
                sincePlayed = TimeSpan.Zero;
            }

            lastPlayed = appTime.TotalTime;
            if (Animations.Count == 0)
            {
                return;
            }

            PoseTargets(appTime, sincePlayed);
        }

        private void SwitchOff()
        {
            if (switchedOff)
            {
                return;
            }

            switchedOff = true;
            paused = false;
            IsAnimationStarted = false;
            Rest();
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

        private void PoseTargets(AppTime appTime, TimeSpan sincePlayed)
        {
            if (!IsAnimationStarted)
            {
                IsAnimationStarted = true;
                AnimationStartTime = appTime.TotalTime;
                played = 0;
            }
            else
            {
                played += sincePlayed.TotalSeconds * AnimationSpeed;
            }

            var time = TimeAt(played, Duration, Option);
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
                target.Transform.SetPose(pose.Position.As<double>(), pose.Rotation, pose.Scale);
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

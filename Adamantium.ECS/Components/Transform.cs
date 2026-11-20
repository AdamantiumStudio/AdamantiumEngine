using System;
using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.MVVM;

namespace Adamantium.ECS.Components
{
    [ViewModel]
    public sealed partial class Transform : ActivatableComponent
    {
        public Transform()
        {
            InitialPosition = WorldPosition.Zero;
            Position = WorldPosition.Zero;
            Rotation = QuaternionF.Identity;
            PivotRotation = QuaternionF.Identity;
            baseScale = Vector3F.One;
            scaleFactor = Vector3F.One;
            TransformData = new Dictionary<CameraBase, TransformMetaData>();
        }

        [Bindable] private WorldPosition initialPosition;
        [Bindable] private WorldPosition position;
        [Bindable, Affects(nameof(Angles))] private QuaternionF rotation;
        [Bindable, Affects(nameof(Scale))] private Vector3F baseScale;
        [Bindable, Affects(nameof(Scale))] private Vector3F scaleFactor;
        private Vector3<double> pivot;
        [Bindable] private QuaternionF pivotRotation;

        private readonly Dictionary<CameraBase, TransformMetaData> TransformData;

        public Vector3F GetRelativePosition(WorldPosition origin) => WorldPosition.OffsetFrom(origin);

        /// <summary>Where this entity is IN THE WORLD: its own position composed through its parents. <see cref="Position"/>
        /// is relative to the parent, so for anything below a root the two differ. Camera-independent, unlike
        /// <see cref="TransformMetaData.AbsoluteWorld"/>, which the render pass records per camera. In double through every
        /// parent, so far from the origin it keeps what float would round away.</summary>
        public WorldPosition WorldPosition => new(GetWorldMatrix().TranslationVector);

        public void RemoveMetadata(CameraBase camera)
        {
            if (TransformData.ContainsKey(camera))
            {
                TransformData.Remove(camera);
            }
        }

        /// <summary>Marks the world matrix kept for every camera as no longer computed, so a camera not looked through
        /// when the entity moved computes it again once it is.</summary>
        public void ForgetComputedMetadata()
        {
            foreach (var metadata in TransformData.Values)
            {
                metadata.Computed = false;
            }
        }

        public TransformMetaData GetMetadata(CameraBase camera)
        {
            TransformMetaData metaData;
            TransformData.TryGetValue(camera, out metaData);
            if (metaData == null)
            {
                metaData = TransformMetaData.New();
                metaData.Camera = camera;
                TransformData.Add(camera, metaData);
            }
            return metaData;
        }

        public void SetMetadata(CameraBase camera, TransformMetaData metadata)
        {
            //Make sure metadata contains correct camera instance
            metadata.Camera = camera;
            if (TransformData.ContainsKey(camera))
            {
                TransformData[camera] = metadata;
            }
            else
            {
                TransformData.Add(camera, metadata);
            }
        }

        public void SetEnableForCamera(CameraBase camera, bool enabled)
        {
            GetMetadata(camera).Enabled = enabled;
        }

        /// <summary>The point this entity turns and scales about, in its parent's space. Moving it leaves the entity in place.</summary>
        public WorldPosition Pivot
        {
            get => Position + pivot;
            set
            {
                var shift = (value - Pivot).As<float>();
                var inverse = Matrix4x4F.Invert(GetLocalMatrixF());
                ShiftPivot(inverse == Matrix4x4F.Zero ? shift : Vector3F.TransformNormal(shift, inverse));
            }
        }

        /// <summary>How it places its entity, as one value. Set, it puts back exactly what was taken, the pivot with it -
        /// unlike setting <see cref="Pivot"/>, which keeps the entity where it stands.</summary>
        public TransformState State
        {
            get => new(Position, Rotation, ScaleFactor, pivot, PivotRotation);
            set
            {
                pivot = value.PivotOffset;
                Position = value.Position;
                Rotation = value.Rotation;
                ScaleFactor = value.ScaleFactor;
                PivotRotation = value.PivotRotation;
                IsWorldDirty = true;
                RaisePropertyChanged(nameof(Pivot));
            }
        }

        private void ShiftPivot(Vector3F offset)
        {
            if (offset == Vector3F.Zero)
            {
                return;
            }

            var drift = Vector3F.TransformNormal(offset, GetLocalMatrixF()) - offset;
            pivot += offset.As<double>();
            Position += drift.As<double>();
            IsWorldDirty = true;
            RaisePropertyChanged(nameof(Pivot));
        }

        // Every input of the world matrix marks it to be computed again.
        partial void OnPivotRotationChanged(QuaternionF value) => Turn(Owner);

        partial void OnPositionChanged(WorldPosition value) => IsWorldDirty = true;

        partial void OnRotationChanged(QuaternionF value) => Turn(Owner?.Owner);

        partial void OnBaseScaleChanged(Vector3F value) => Rescale();

        partial void OnScaleFactorChanged(Vector3F value) => Rescale();

        // A scale changes the size of all under the entity: its world, and the weight of its bodies.
        private void Rescale()
        {
            IsWorldDirty = true;
            RigidBody.ReweighAround(Owner);
        }

        // A turn changes the sizes along their axes only of what a scale after it stretches unevenly: a rotation comes
        // after the entity's own scale, so only those above count; a pivot's turn comes before it too. A body the physics
        // turns every frame stands under an even scale, and weighs nothing anew.
        private void Turn(Entity firstScaledAfter)
        {
            IsWorldDirty = true;
            for (var at = firstScaledAfter; at != null; at = at.Owner)
            {
                if (at.Transform is { } transform && !Vector3.IsEven(transform.Scale))
                {
                    RigidBody.ReweighAround(Owner);
                    return;
                }
            }
        }

        public Vector3F Scale => baseScale * scaleFactor;

        private TypedAngles angles;

        /// <summary><see cref="Rotation"/> in degrees about X, Y and Z (pitch, yaw, roll), as people type a turn. The angles
        /// set are given back as they were, 200 staying 200, until something else turns the entity.</summary>
        public Vector3F Angles
        {
            get => angles.Read(rotation);
            set => Rotation = angles.Take(value);
        }

        /// <summary>Set by the world-matrix input setters (position/rotation/scale/pivot). TransformService recomputes the
        /// cached world matrix only for entities whose flag is set (or whose camera/parent moved), so a static subtree
        /// costs nothing. Starts true so the first frame computes.</summary>
        public bool IsWorldDirty { get; set; } = true;

        public void Move(Vector3<double> direction, Double distance)
        {
            lock (this)
            {
                var distanceVector = Vector3<double>.Multiply(direction, distance);
                if (IsEnabled)
                {
                    Position = new WorldPosition(distanceVector);
                }
            }
        }

        public void Move(Vector3<double> direction, Vector3<double> distance)
        {
            lock (this)
            {
                var distanceVector = Vector3<double>.Multiply(direction, distance);
                if (IsEnabled)
                {
                    Position = new WorldPosition(distanceVector);
                }
            }
        }

        public void Translate(Vector3<double> direction, Double distance)
        {
            lock (this)
            {
                var distanceVector = Vector3<double>.Multiply(direction, distance);
                if (IsEnabled)
                {
                    Position += distanceVector;
                }
            }
        }

        public void TranslateRight(Double distance)
        {
            lock (this)
            {
                var distanceVector = Vector3<double>.Multiply(GetRotationMatrixF().Right.As<double>(), distance);
                if (IsEnabled)
                {
                    Position += distanceVector;
                }
            }
        }

        public void TranslateUp(Double distance)
        {
            lock (this)
            {
                var distanceVector = Vector3<double>.Multiply(GetRotationMatrixF().Up.As<double>(), distance);
                if (IsEnabled)
                {
                    Position += distanceVector;
                }
            }
        }

        public void TranslateForward(Double distance)
        {
            lock (this)
            {
                var distanceVector = Vector3<double>.Multiply(GetRotationMatrixF().Forward.As<double>(), distance);
                if (IsEnabled)
                {
                    Position += distanceVector;
                }
            }
        }

        public void Translate(Vector3<double> direction, Vector3<double> distance)
        {
            lock (this)
            {
                var distanceVector = Vector3<double>.Multiply(direction, distance);
                if (IsEnabled)
                {
                    Position += distanceVector;
                }
            }
        }

        public void TranslatePivot(Vector3<double> direction, Double distance)
        {
            lock (this)
            {
                var distanceVector = Vector3<double>.Multiply(direction, distance);
                if (IsEnabled)
                {
                    Pivot += distanceVector;
                }
            }
        }

        public void TranslatePivot(Vector3<double> direction, Vector3<double> distance)
        {
            lock (this)
            {
                var distanceVector = Vector3<double>.Multiply(direction, distance);
                if (IsEnabled)
                {
                    Pivot += distanceVector;
                }
            }
        }

        private float EnsureAngle(float angle, RotationUnits units)
        {
            if (units == RotationUnits.Degrees)
            {
                angle = float.DegreesToRadians(angle);
            }
            return angle;
        }

        public Matrix4x4F GetRotationMatrixF()
        {
            return Matrix4x4.CreateFromQuaternion<float>(Rotation);
        }

        public Matrix4x4F GetPivotRotationMatrixF()
        {
            return Matrix4x4.CreateFromQuaternion<float>(PivotRotation);
        }
        
        public Matrix4x4<double> GetRotationMatrix()
        {
            return Matrix4x4.CreateFromQuaternion<double>(Rotation.As<double>());
        }

        public Matrix4x4<double> GetPivotRotationMatrix()
        {
            return Matrix4x4.CreateFromQuaternion<double>(PivotRotation.As<double>());
        }

        public void ResetPosition()
        {
            if (IsEnabled)
            {
                Position = InitialPosition;
            }
        }

        public void ResetScale()
        {
            if (IsEnabled)
            {
                ScaleFactor = Vector3F.One;
            }
        }

        public void ResetRotation()
        {
            if (IsEnabled)
            {
                Rotation = QuaternionF.Identity;
            }
        }

        public void ResetPivotPosition()
        {
            if (IsEnabled)
            {
                ShiftPivot(-pivot.As<float>());
            }
        }

        public void ResetPivotRotation()
        {
            if (IsEnabled)
            {
                PivotRotation = QuaternionF.Identity;
            }
        }

        public void Rotate(Vector3F axis, float angle, RotationUnits units = RotationUnits.Radians)
        {
            angle = EnsureAngle(angle, units);
            if (IsEnabled)
            {
                Rotation = QuaternionF.Multiply(Quaternion.CreateFromAxisAngle<float>(axis, angle), Rotation);
            }
        }

        public void RotateRight(float angle, RotationUnits units = RotationUnits.Radians)
        {
            angle = EnsureAngle(angle, units);
            if (IsEnabled)
            {
                Rotation = QuaternionF.Multiply(Quaternion.CreateFromAxisAngle<float>(GetRotationMatrixF().Right, angle), Rotation);
            }
        }

        public void RotateUp(float angle, RotationUnits units = RotationUnits.Radians)
        {
            angle = EnsureAngle(angle, units);
            if (IsEnabled)
            {
                Rotation = QuaternionF.Multiply(Quaternion.CreateFromAxisAngle<float>(GetRotationMatrixF().Up, angle), Rotation);
            }
        }

        public void RotateForward(float angle, RotationUnits units = RotationUnits.Radians)
        {
            angle = EnsureAngle(angle, units);
            if (IsEnabled)
            {
                Rotation = QuaternionF.Multiply(Quaternion.CreateFromAxisAngle<float>(GetRotationMatrixF().Forward, angle), Rotation);
            }
        }

        public void RotatePivot(Vector3F axis, float angle, RotationUnits units = RotationUnits.Radians)
        {
            angle = EnsureAngle(angle, units);
            if (IsEnabled)
            {
                PivotRotation = QuaternionF.Multiply(Quaternion.CreateFromAxisAngle<float>(axis, angle), PivotRotation);
            }
        }

        public void RotatePivotRight(float angle, RotationUnits units = RotationUnits.Radians)
        {
            angle = EnsureAngle(angle, units);
            if (IsEnabled)
            {
                PivotRotation = QuaternionF.Multiply(Quaternion.CreateFromAxisAngle<float>(GetPivotRotationMatrixF().Right, angle), PivotRotation);
            }
        }

        public void RotatePivotUp(float angle, RotationUnits units = RotationUnits.Radians)
        {
            angle = EnsureAngle(angle, units);
            if (IsEnabled)
            {
                PivotRotation = QuaternionF.Multiply(Quaternion.CreateFromAxisAngle<float>(GetPivotRotationMatrixF().Up, angle), PivotRotation);
            }
        }

        public void RotatePivotForward(float angle, RotationUnits units = RotationUnits.Radians)
        {
            angle = EnsureAngle(angle, units);
            if (IsEnabled)
            {
                PivotRotation = QuaternionF.Multiply(Quaternion.CreateFromAxisAngle<float>(GetPivotRotationMatrixF().Forward, angle), PivotRotation);
            }
        }

        ///<summary>
        ///Sync entity orientation with camera forward axis for left handed coordinate system
        ///</summary>
        public void SyncOrientationWithCameraForwardLH(CameraBase camera)
        {
            var rotMatr = camera.RotationMatrix;
            var quat = Quaternion.CreateLookToLH<float>(rotMatr.Forward, rotMatr.Up);
            Owner.Transform.Rotation = quat;
        }

        ///<summary>
        ///Sync entity orientation with camera backward axis for left handed coordinate system
        ///</summary>
        public void SyncOrientationWithCameraBackwardLH(CameraBase camera)
        {
            var rotMatr = camera.RotationMatrix;
            var quat = Quaternion.CreateLookToLH<float>(rotMatr.Backward, rotMatr.Up);
            Owner.Transform.Rotation = quat;
        }

        public void SetScaleFactor(float factor)
        {
            SetScaleFactor(new Vector3F(factor));
        }

        public void SetScaleFactor(Vector3F factor)
        {
            if (IsEnabled)
            {
                ScaleFactor = factor;
            }
        }

        public void SetBaseScale(Vector3F scale)
        {
            if (IsEnabled)
            {
                BaseScale = scale;
            }
        }

        public void SetBaseScale(float scale)
        {
            SetBaseScale(new Vector3F(scale));
        }

        public void DivideScale(float scale)
        {
            DivideScale(new Vector3F(scale));
        }


        public void DivideScale(Vector3F scale)
        {
            if (IsEnabled)
            {
                ScaleFactor /= scale;
            }
        }

        public void MultiplyScale(float scale)
        {
            MultiplyScale(new Vector3F(scale));
        }

        public void MultiplyScale(Vector3F scale)
        {
            if (IsEnabled)
            {
                ScaleFactor *= scale;
            }
        }

        /// <summary>Stands the entity in its parent as a model's node is posed - an animation's key, say: scaled by
        /// <paramref name="baseScale"/> (the user's <see cref="ScaleFactor"/> on top) and turned about its own origin, then
        /// moved to <paramref name="position"/>. Its pivot stays where it is on it, and only moves the position kept.</summary>
        public void SetPose(WorldPosition position, QuaternionF rotation, Vector3F baseScale)
        {
            Rotation = rotation;
            BaseScale = baseScale;
            var scaling = Scale;
            var center = pivot.As<float>();
            var unmoved = Vector3F.Zero;
            var aboutPivot = Matrix4x4.CreateTransformation(center, pivotRotation, scaling, center, this.rotation, unmoved);
            Position = position - aboutPivot.TranslationVector.As<double>();
        }

        /// <summary>Where this entity stands in its parent: scaled and turned about its pivot, then moved to its position.</summary>
        public Matrix4x4F GetLocalMatrixF() => ComposeLocalMatrix<float>();

        /// <summary>As <see cref="GetLocalMatrixF"/>, in double: the position as it is kept, not rounded to float.</summary>
        public Matrix4x4<double> GetLocalMatrix() => ComposeLocalMatrix<double>();

        // Scaled and turned about the pivot, then moved to the position: one formula for either precision.
        private Matrix4x4<T> ComposeLocalMatrix<T>() where T : unmanaged, System.Numerics.IFloatingPointIeee754<T>
        {
            var center = pivot.As<T>();
            return Matrix4x4.CreateTransformation(center, pivotRotation.As<T>(), Scale.As<T>(), center, rotation.As<T>(),
                Position.ToVector().As<T>());
        }

        /// <summary>
        /// Rewrites this entity's position, rotation and scale for <paramref name="newOwner"/> - null for none - so that
        /// it stays where it stands in the world once it hangs there; called before the owner changes. The turn and even
        /// scale between the two owners join the entity's own, its shape - pivot, uneven or mirrored scale - kept; an
        /// uneven scale between them is taken as its mean. False, and the transform untouched, when no transform could
        /// keep it: between owners one of which is flattened or mirrored against the other.
        /// </summary>
        public bool StayInWorldUnder(Entity newOwner)
        {
            // In double: between owners far from the origin a float inverse would lose the entity's own small place.
            var oldParent = Owner?.Owner?.Transform.GetWorldMatrix() ?? Matrix4x4<double>.Identity;
            var newParent = newOwner?.Transform.GetWorldMatrix() ?? Matrix4x4<double>.Identity;
            if (oldParent == newParent)
            {
                return true;
            }

            var between = oldParent * Matrix4x4<double>.Invert(newParent);
            if (between.Determinant() <= 1e-12 || !between.Decompose(out var scale, out var turn, out _))
            {
                return false;
            }

            var target = (GetLocalMatrix() * between).TranslationVector;
            Rotation = QuaternionF.Multiply(turn.As<float>(), Rotation);
            ScaleFactor *= (float)((scale.X + scale.Y + scale.Z) / 3);
            Position = WorldPosition.Zero;
            Position = new WorldPosition(target - GetLocalMatrix().TranslationVector);
            return true;
        }

        /// <summary>Where this entity stands in the world against <paramref name="origin"/>, made float once the way
        /// from it is taken in double: the frame a test between things near each other wants.</summary>
        public Matrix4x4F GetWorldMatrixF(WorldPosition origin)
        {
            var world = GetWorldMatrix();
            world.TranslationVector -= origin.ToVector();
            return world.As<float>();
        }

        /// <summary>Where this entity stands in the world as the transforms were worked out for <paramref name="camera"/>
        /// this frame - a lookup, where <see cref="GetWorldMatrix()"/> composes every parent afresh; worked out afresh
        /// while there is nothing for the camera yet, or for the camera's own entity, which its walk passes by.</summary>
        public Matrix4x4<double> GetWorldMatrix(CameraBase camera) =>
            camera != null && TransformData.TryGetValue(camera, out var metadata) && metadata.Computed
                ? metadata.AbsoluteWorld
                : GetWorldMatrix();

        /// <summary>Where this entity stands in the world, through its parents; the same matrix it is drawn with, made
        /// float once at the end.</summary>
        public Matrix4x4F GetWorldMatrixF() => GetWorldMatrix().As<float>();

        /// <summary>As <see cref="GetWorldMatrixF()"/>, in double through every parent.</summary>
        public Matrix4x4<double> GetWorldMatrix()
        {
            var world = GetLocalMatrix();
            for (var at = Owner?.Owner; at != null; at = at.Owner)
            {
                world *= at.Transform.GetLocalMatrix();
            }

            return world;
        }

        public Matrix4x4F CalculateFinalTransform(CameraBase camera, in Matrix4x4<double> parentWorld) =>
            CalculateFinalTransform(camera, parentWorld, camera.WorldPosition);

        /// <summary>As <see cref="CalculateFinalTransform(CameraBase, in Matrix4x4{double})"/>, with the camera's world
        /// position worked out once for the frame's walk.</summary>
        public Matrix4x4F CalculateFinalTransform(CameraBase camera, in Matrix4x4<double> parentWorld,
            WorldPosition cameraWorld)
        {
            // Through the parent (row vectors: local * parent), whose world this frame's top-down walk worked out
            // already; identity for a root. In double: far from the origin float would move the entity by its rounding.
            var absoluteWorld = GetLocalMatrix() * parentWorld;

            // Drawn against the camera, the view turning only: the camera's world position is taken away in double, and
            // only the small way left goes to float - so the entity stands as exactly far out as near the origin.
            var renderWorld = absoluteWorld;
            renderWorld.TranslationVector = absoluteWorld.TranslationVector - cameraWorld.ToVector();

            var metadata = GetMetadata(camera);
            metadata.AbsoluteWorld = absoluteWorld;
            metadata.RelativePosition = renderWorld.TranslationVector.As<float>();
            metadata.Pivot = pivot.As<float>();
            metadata.WorldMatrixF = renderWorld.As<float>();
            metadata.Rotation = Rotation;
            metadata.Scale = Scale;
            // Record the inputs so TransformService can skip this (camera, node) next frame if none of them changed.
            metadata.LastCameraPosition = cameraWorld;
            metadata.Computed = true;
            return metadata.WorldMatrixF;
        }

        public override void CloneValues(IComponent component)
        {
            if (component is Transform transform)
            {
                transform.angles = angles;
                transform.Rotation = Rotation;
                transform.BaseScale = BaseScale;
                transform.ScaleFactor = ScaleFactor;
                transform.InitialPosition = InitialPosition;
                transform.Position = Position;
                transform.pivot = pivot;
                transform.IsWorldDirty = true;
                transform.PivotRotation = PivotRotation;
                transform.IsEnabled = IsEnabled;
            }
        }
    }
}

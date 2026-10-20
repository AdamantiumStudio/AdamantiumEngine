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
            InitialPosition = Vector3.Zero;
            Position = Vector3.Zero;
            Rotation = QuaternionF.Identity;
            PivotRotation = QuaternionF.Identity;
            baseScale = Vector3F.One;
            scaleFactor = Vector3F.One;
            TransformData = new Dictionary<CameraBase, TransformMetaData>();
        }

        [Bindable] private Vector3 initialPosition;
        [Bindable] private Vector3 position;
        [Bindable, Affects(nameof(Angles))] private QuaternionF rotation;
        [Bindable, Affects(nameof(Scale))] private Vector3F baseScale;
        [Bindable, Affects(nameof(Scale))] private Vector3F scaleFactor;
        private Vector3 pivot;
        [Bindable] private QuaternionF pivotRotation;

        private readonly Dictionary<CameraBase, TransformMetaData> TransformData;

        public Vector3F GetRelativePosition(Vector3 offset)
        {
            return WorldPosition - offset;
        }

        /// <summary>Where this entity is IN THE WORLD: its own position composed through its parents. <see cref="Position"/>
        /// is relative to the parent, so for anything below a root the two differ. Camera-independent, unlike
        /// <see cref="TransformMetaData.AbsoluteWorld"/>, which the render pass records per camera.</summary>
        public Vector3 WorldPosition => (Vector3)GetWorldMatrixF().TranslationVector;

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
        public Vector3 Pivot
        {
            get => pivot + Position;
            set
            {
                var shift = (Vector3F)(value - Pivot);
                var local = GetLocalMatrixF();
                Matrix4x4F.Invert(ref local, out var inverse);
                ShiftPivot(inverse == Matrix4x4F.Zero ? shift : Vector3F.TransformNormal(shift, inverse));
            }
        }

        private void ShiftPivot(Vector3F offset)
        {
            if (offset == Vector3F.Zero)
            {
                return;
            }

            var drift = Vector3F.TransformNormal(offset, GetLocalMatrixF()) - offset;
            pivot += (Vector3)offset;
            Position += (Vector3)drift;
            IsWorldDirty = true;
            RaisePropertyChanged(nameof(Pivot));
        }

        // Every input of the world matrix marks it to be computed again.
        partial void OnPivotRotationChanged(QuaternionF value) => IsWorldDirty = true;

        partial void OnPositionChanged(Vector3 value) => IsWorldDirty = true;

        partial void OnRotationChanged(QuaternionF value) => IsWorldDirty = true;

        partial void OnBaseScaleChanged(Vector3F value) => IsWorldDirty = true;

        partial void OnScaleFactorChanged(Vector3F value) => IsWorldDirty = true;

        public Vector3F Scale => baseScale * scaleFactor;

        private Vector3F angles;
        private QuaternionF anglesRotation = QuaternionF.Identity;

        /// <summary><see cref="Rotation"/> in degrees about X, Y and Z (pitch, yaw, roll), as people type a turn. The angles
        /// set are given back as they were, 200 staying 200, until something else turns the entity.</summary>
        public Vector3F Angles
        {
            get
            {
                var turn = rotation;
                if (turn != anglesRotation)
                {
                    turn.ToYawPitchRoll(out var yaw, out var pitch, out var roll);
                    angles = new Vector3F(MathHelper.RadiansToDegrees(pitch), MathHelper.RadiansToDegrees(yaw),
                        MathHelper.RadiansToDegrees(roll));
                    anglesRotation = turn;
                }

                return angles;
            }
            set
            {
                var turn = QuaternionF.RotationYawPitchRoll(MathHelper.DegreesToRadians(value.Y),
                    MathHelper.DegreesToRadians(value.X), MathHelper.DegreesToRadians(value.Z));
                angles = value;
                anglesRotation = turn;
                Rotation = turn;
            }
        }

        /// <summary>Set by the world-matrix input setters (position/rotation/scale/pivot). TransformService recomputes the
        /// cached world matrix only for entities whose flag is set (or whose camera/parent moved), so a static subtree
        /// costs nothing. Starts true so the first frame computes.</summary>
        public bool IsWorldDirty { get; set; } = true;

        public void Move(Vector3 direction, Double distance)
        {
            lock (this)
            {
                var distanceVector = Vector3.Multiply(direction, distance);
                if (IsEnabled)
                {
                    Position = distanceVector;
                }
            }
        }

        public void Move(Vector3 direction, Vector3 distance)
        {
            lock (this)
            {
                var distanceVector = Vector3.Multiply(direction, distance);
                if (IsEnabled)
                {
                    Position = distanceVector;
                }
            }
        }

        public void Translate(Vector3 direction, Double distance)
        {
            lock (this)
            {
                var distanceVector = Vector3.Multiply(direction, distance);
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
                var distanceVector = Vector3.Multiply((Vector3)GetRotationMatrixF().Right, distance);
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
                var distanceVector = Vector3.Multiply((Vector3)GetRotationMatrixF().Up, distance);
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
                var distanceVector = Vector3.Multiply((Vector3)GetRotationMatrixF().Forward, distance);
                if (IsEnabled)
                {
                    Position += distanceVector;
                }
            }
        }

        public void Translate(Vector3 direction, Vector3 distance)
        {
            lock (this)
            {
                var distanceVector = Vector3.Multiply(direction, distance);
                if (IsEnabled)
                {
                    Position += distanceVector;
                }
            }
        }

        public void TranslatePivot(Vector3 direction, Double distance)
        {
            lock (this)
            {
                var distanceVector = Vector3.Multiply(direction, distance);
                if (IsEnabled)
                {
                    Pivot += distanceVector;
                }
            }
        }

        public void TranslatePivot(Vector3 direction, Vector3 distance)
        {
            lock (this)
            {
                var distanceVector = Vector3.Multiply(direction, distance);
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
                angle = MathHelper.DegreesToRadians(angle);
            }
            return angle;
        }

        public Matrix4x4F GetRotationMatrixF()
        {
            return Matrix4x4F.RotationQuaternion(Rotation);
        }

        public Matrix4x4F GetPivotRotationMatrixF()
        {
            return Matrix4x4F.RotationQuaternion(PivotRotation);
        }
        
        public Matrix4x4 GetRotationMatrix()
        {
            return Matrix4x4.RotationQuaternion(Rotation);
        }

        public Matrix4x4 GetPivotRotationMatrix()
        {
            return Matrix4x4.RotationQuaternion(PivotRotation);
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
                ShiftPivot(-(Vector3F)pivot);
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
                Rotation = QuaternionF.Multiply(QuaternionF.RotationAxis(axis, angle), Rotation);
            }
        }

        public void RotateRight(float angle, RotationUnits units = RotationUnits.Radians)
        {
            angle = EnsureAngle(angle, units);
            if (IsEnabled)
            {
                Rotation = QuaternionF.Multiply(QuaternionF.RotationAxis(GetRotationMatrixF().Right, angle), Rotation);
            }
        }

        public void RotateUp(float angle, RotationUnits units = RotationUnits.Radians)
        {
            angle = EnsureAngle(angle, units);
            if (IsEnabled)
            {
                Rotation = QuaternionF.Multiply(QuaternionF.RotationAxis(GetRotationMatrixF().Up, angle), Rotation);
            }
        }

        public void RotateForward(float angle, RotationUnits units = RotationUnits.Radians)
        {
            angle = EnsureAngle(angle, units);
            if (IsEnabled)
            {
                Rotation = QuaternionF.Multiply(QuaternionF.RotationAxis(GetRotationMatrixF().Forward, angle), Rotation);
            }
        }

        public void RotatePivot(Vector3F axis, float angle, RotationUnits units = RotationUnits.Radians)
        {
            angle = EnsureAngle(angle, units);
            if (IsEnabled)
            {
                PivotRotation = QuaternionF.Multiply(QuaternionF.RotationAxis(axis, angle), PivotRotation);
            }
        }

        public void RotatePivotRight(float angle, RotationUnits units = RotationUnits.Radians)
        {
            angle = EnsureAngle(angle, units);
            if (IsEnabled)
            {
                PivotRotation = QuaternionF.Multiply(QuaternionF.RotationAxis(GetPivotRotationMatrixF().Right, angle), PivotRotation);
            }
        }

        public void RotatePivotUp(float angle, RotationUnits units = RotationUnits.Radians)
        {
            angle = EnsureAngle(angle, units);
            if (IsEnabled)
            {
                PivotRotation = QuaternionF.Multiply(QuaternionF.RotationAxis(GetPivotRotationMatrixF().Up, angle), PivotRotation);
            }
        }

        public void RotatePivotForward(float angle, RotationUnits units = RotationUnits.Radians)
        {
            angle = EnsureAngle(angle, units);
            if (IsEnabled)
            {
                PivotRotation = QuaternionF.Multiply(QuaternionF.RotationAxis(GetPivotRotationMatrixF().Forward, angle), PivotRotation);
            }
        }

        ///<summary>
        ///Sync entity orientation with camera forward axis for left handed coordinate system
        ///</summary>
        public void SyncOrientationWithCameraForwardLH(CameraBase camera)
        {
            var rotMatr = camera.RotationMatrix;
            var quat = QuaternionF.RotationLookAtLH(rotMatr.Forward, rotMatr.Up);
            Owner.Transform.Rotation = quat;
        }

        ///<summary>
        ///Sync entity orientation with camera backward axis for left handed coordinate system
        ///</summary>
        public void SyncOrientationWithCameraBackwardLH(CameraBase camera)
        {
            var rotMatr = camera.RotationMatrix;
            var quat = QuaternionF.RotationLookAtLH(rotMatr.Backward, rotMatr.Up);
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
        public void SetPose(Vector3 position, QuaternionF rotation, Vector3F baseScale)
        {
            Rotation = rotation;
            BaseScale = baseScale;
            var scaling = Scale;
            var center = (Vector3F)pivot;
            var unmoved = Vector3F.Zero;
            Matrix4x4F.Transformation(ref center, ref pivotRotation, ref scaling, ref center, ref this.rotation, ref unmoved,
                out var aboutPivot);
            Position = position - (Vector3)aboutPivot.TranslationVector;
        }

        /// <summary>Where this entity stands in its parent: scaled and turned about its pivot, then moved to its position.</summary>
        public Matrix4x4F GetLocalMatrixF()
        {
            var scaling = Scale;
            var localPosition = (Vector3F)Position;
            var scalingCenter = (Vector3F)pivot;
            var rotationCenter = scalingCenter;
            Matrix4x4F.Transformation(ref scalingCenter, ref pivotRotation, ref scaling, ref rotationCenter, ref rotation, ref localPosition, out var localMatrix);
            return localMatrix;
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
            var oldParent = Owner?.Owner?.Transform.GetWorldMatrixF() ?? Matrix4x4F.Identity;
            var newParent = newOwner?.Transform.GetWorldMatrixF() ?? Matrix4x4F.Identity;
            if (oldParent == newParent)
            {
                return true;
            }

            Matrix4x4F.Invert(ref newParent, out var inverse);
            var between = oldParent * inverse;
            if (between.Determinant() <= 1e-12f || !between.Decompose(out var scale, out var turn, out _))
            {
                return false;
            }

            var target = (GetLocalMatrixF() * between).TranslationVector;
            Rotation = QuaternionF.Multiply(turn, Rotation);
            ScaleFactor *= (scale.X + scale.Y + scale.Z) / 3;
            Position = Vector3.Zero;
            Position = (Vector3)(target - GetLocalMatrixF().TranslationVector);
            return true;
        }

        /// <summary>Where this entity stands in the world as the transforms were worked out for <paramref name="camera"/>
        /// this frame - a lookup, where <see cref="GetWorldMatrixF()"/> composes every parent afresh; worked out afresh
        /// while there is nothing for the camera yet.</summary>
        public Matrix4x4F GetWorldMatrixF(CameraBase camera) =>
            camera != null && TransformData.TryGetValue(camera, out var metadata) && metadata.Computed
                ? metadata.AbsoluteWorld
                : GetWorldMatrixF();

        /// <summary>Where this entity stands in the world, through its parents; the same matrix it is drawn with.</summary>
        public Matrix4x4F GetWorldMatrixF()
        {
            var world = GetLocalMatrixF();
            for (var at = Owner?.Owner; at != null; at = at.Owner)
            {
                world *= at.Transform.GetLocalMatrixF();
            }

            return world;
        }

        public Matrix4x4F CalculateFinalTransform(CameraBase camera, Matrix4x4F parentWorld) =>
            CalculateFinalTransform(camera, parentWorld, camera.WorldPosition);

        /// <summary>As <see cref="CalculateFinalTransform(CameraBase, Matrix4x4F)"/>, with the camera's world position
        /// worked out once for the frame's walk.</summary>
        public Matrix4x4F CalculateFinalTransform(CameraBase camera, Matrix4x4F parentWorld, Vector3 cameraWorld)
        {
            var localMatrix = GetLocalMatrixF();

            // THE hierarchical fix: compose through the parent (row-vector convention -> local * parent). The parent's
            // absolute world was computed earlier this frame (TransformService walks the tree top-down), so a parent
            // transform now flows into its children. For a root, parentWorld is identity and this leaves the matrix as-is.
            var absoluteWorld = localMatrix * parentWorld;

            // Camera-relative render matrix: the view is rotation-only, so shift the world by the camera's WORLD
            // position - Transform.Position is relative to a parent, which a third-person camera has.
            var cameraPosition = (Vector3F)cameraWorld;
            var renderWorld = absoluteWorld * Matrix4x4F.Translation(-cameraPosition);

            var metadata = GetMetadata(camera);
            metadata.AbsoluteWorld = absoluteWorld;
            metadata.RelativePosition = (Vector3)absoluteWorld.TranslationVector - cameraWorld;
            metadata.Pivot = (Vector3F)pivot;
            metadata.WorldMatrixF = renderWorld;
            metadata.WorldMatrix = (Matrix4x4)renderWorld;
            metadata.Rotation = Rotation;
            metadata.Scale = Scale;
            // Record the inputs so TransformService can skip this (camera, node) next frame if none of them changed.
            metadata.LastCameraPosition = cameraWorld;
            metadata.Computed = true;
            return renderWorld;
        }

        public override void CloneValues(IComponent component)
        {
            if (component is Transform transform)
            {
                transform.angles = angles;
                transform.anglesRotation = anglesRotation;
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

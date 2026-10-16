using Adamantium.Mathematics;
using System;
using Adamantium.Core;
using Adamantium.Graphics.Core;
using Adamantium.MVVM;

namespace Adamantium.ECS.Components
{
    [ViewModel]
    public abstract partial class CameraBase : ActivatableComponent
    {
        [Bindable] private QuaternionF rotation = QuaternionF.Identity;
        [Bindable] private uint width;
        [Bindable] private uint height;
        [Bindable] private Single fov;
        [Bindable] private Single fovY;
        private Single tanFov;
        [Bindable] private Double radius;
        [Bindable] private Single zNear;
        [Bindable] private Single zFar;
        [Bindable] private Single aspectRatio;
        [Bindable] private Entity lookAtObject;


        public Matrix4x4F ViewMatrix { get; set; } = Matrix4x4F.Identity;

        public Matrix4x4F PerspectiveProjection { get; set; }

        public Matrix4x4F UiProjection { get; set; }

        public Matrix4x4F OrthoProjection { get; set; } = Matrix4x4F.Identity;

        public Matrix4x4F IsometricProjection { get; set; }

        public Matrix4x4F RotationMatrix => Matrix4x4F.RotationQuaternion(Rotation);

        public BoundingFrustum Frustum { get; set; }

        public Vector3? LookAt { get; set; }

        public Double Velocity { get; set; }

        public Double DragVelocity { get; set; }

        public Double CurrentVelocity { get; set; }

        /// <summary>How far one wheel notch moves a free camera, in seconds of travel at its <see cref="Velocity"/>.</summary>
        public Double WheelVelocity { get; set; }

        /// <summary>How much one wheel notch closes the distance to the subject a camera orbits: it is divided by this.</summary>
        public Double ZoomStep { get; set; } = 1.15;

        public Single RotationSpeed { get; set; }

        public Single MouseSensitivity { get; set; }

        public Boolean IsUserControlled { get; set; }

        /// <summary>
        /// Gets or gets value does this camera is currently active (bind to window and displaying content)
        /// </summary>
        public Boolean IsActive { get; set; }

        /// <summary>
        /// Camera relative X axis from view matrix
        /// </summary>
        public Vector3 Right { get; protected set; }

        /// <summary>
        /// Camera relative Y axis from view matrix
        /// </summary>
        public Vector3 Up { get; protected set; }

        /// <summary>
        /// Camera relative Z axis from view matrix
        /// </summary>
        public Vector3 Forward { get; protected set; }

        public Vector3 Backward => -Forward;

        /// <summary>The object a third-person camera FOLLOWS. It IS the parent of the camera's entity: transforms are
        /// relative to the parent, so the follow link and the transform link are one thing.</summary>
        public Entity Subject => Owner?.Owner;

        /// <summary>Air a third-person camera keeps around its subject, as a multiple of the edge-to-edge framing
        /// distance. Ignored when an explicit distance is passed to <c>SetThirdPersonCamera</c>.</summary>
        public Double FramingMargin { get; set; } = 2;

        /// <summary>Where the camera is IN THE WORLD. Its own <c>Transform.Position</c> is relative to its parent, and
        /// a third-person camera hangs off the object it follows - the scene is shifted by THIS one.</summary>
        public Vector3 WorldPosition => Owner?.Transform.WorldPosition ?? Vector3.Zero;

        /// <summary>
        /// Gets or Sets value indicating does ZNear and ZFar planes should be reversed for inverted depth buffer
        /// </summary>
        [Bindable]
        public partial bool IsDepthInversed { get; set; }

        /// <summary>
        /// Pixels per point on the output the camera draws into, as its host's scale gives it: <see cref="Width"/> and
        /// <see cref="Height"/> are pixels, sizes meant to look the same at any scale are points.
        /// </summary>
        public float PixelsPerPoint { get; set; } = 1;

        public float TanFov
        {
            get => tanFov;
            protected set => SetProperty(ref tanFov, value);
        }

        // ------- 3rd person related
        public Vector3F HostUpVector { get; set; }

        public bool IsLookingBackwards { get; set; }

        public QuaternionF FormerRotation { get; set; }
        // ------- 3rd person related END

        [Bindable]
        public partial CameraType Type { get; set; }

        public CameraProjectionType ProjectionType { get; set; }

        public virtual Matrix4x4F ProjectionMatrix
        {
            get
            {
                switch (ProjectionType)
                {
                    case CameraProjectionType.Perspective:
                        return PerspectiveProjection;
                    case CameraProjectionType.Ortho:
                        return OrthoProjection;
                    case CameraProjectionType.UI:
                        return UiProjection;
                    case CameraProjectionType.Isometric:
                        return IsometricProjection;
                    default:
                        return PerspectiveProjection;
                }
            }
        }

        ///<summary>
        ///Returns rotation matrix for camera host object all third person related properties and set the camera type to Free.
        ///</summary>
        public Matrix4x4F EntityRotationMatrix
        {
            get
            {
                if (Subject == null)
                {
                    return Matrix4x4F.Identity;
                }

                // The SUBJECT's rotation. The guard used to ask about the host and the answer described the camera
                // (Owner.Transform is the camera's own), so every caller got the camera's rotation handed back.
                return Matrix4x4F.RotationQuaternion(Subject.Transform.Rotation);
            }
        }

        public abstract void Update(AppTime appTime);

        ///<summary>
        ///Rotate camera absolutely around base X, Y and Z axis.
        ///</summary>
        ///<remarks>
        ///Angles must be in degrees.
        ///</remarks>
        public abstract void SetAbsoluteRotation(float angleX, float angleY, float angleZ);

        ///<summary>
        ///Move camera along X axis relative to the current position and view direction.
        ///</summary>
        ///<remarks>
        ///relativeX: value defines distance on which camera will be moved along X axis.
        ///</remarks>
        public abstract void TranslateRight(Double relativeX);

        ///<summary>
        ///Move camera along Y axis relative to the current position and view direction.
        ///</summary>
        ///<remarks>
        ///relativeY: value defines distance on which camera will be moved along Y axis.
        ///</remarks>
        public abstract void TranslateUp(Double relativeY);

        ///<summary>
        ///Move camera along Z axis relative to the current position and view direction.
        ///</summary>
        ///<remarks>
        ///relativeZ: value defines distance on which camera will be moved along Z axis.
        ///</remarks>
        public abstract void TranslateForward(Double relativeZ);

        ///<summary>
        ///Rotate camera around X axis.
        ///</summary>
        ///<remarks>
        ///Angle must be in degrees.
        ///</remarks>
        public abstract void RotateRight(float angle);

        ///<summary>
        ///Rotate camera around Y axis.
        ///</summary>
        ///<remarks>
        ///Angle must be in degrees.
        ///</remarks>
        public abstract void RotateUp(float angle);

        ///<summary>
        ///Rotate camera around Z axis.
        ///</summary>
        ///<remarks>
        ///Angle must be in degrees.
        ///</remarks>
        public abstract void RotateForward(float angle);

        ///<summary>
        ///Rotate camera around X and Y axis.
        ///</summary>
        ///<remarks>
        ///Angles must be in degrees.
        ///</remarks>
        public abstract void RotateRelativeXY(float angleX, float angleY);

        ///<summary>
        ///Sets camera as Free with specific parameters.
        ///</summary>
        public abstract void SetFreeCamera(Vector3 position, Vector3 lookAt, Vector3 up);

        ///<summary>
        ///Sets camera as Free where it stands, looking where it looks.
        ///</summary>
        public abstract void SetFreeCamera();

        ///<summary>
        ///Changes the Position (Offset) of Free camera.
        ///</summary>
        public abstract void SetFreePosition(Vector3 position);

        ///<summary>
        ///Changes the LookAt of Free camera.
        ///</summary>
        public abstract void SetFreeLookAt(Vector3 lookAt);

        ///<summary>
        ///Sets camera as First Person with specific parameters.
        ///</summary>
        ///<remarks>
        ///objectRotation - rotation of the camera host (e.g. position of human body (and face) in space).
        ///</remarks>
        public abstract void SetFirstPersonCamera(Vector3 position, QuaternionF objectRotation, Double faceDistance);

        ///<summary>
        ///Changes the Position (Offset) and Rotation of First Person camera.
        ///</summary>
        public abstract void SetFirstPersonPositionRotation(Vector3 position, QuaternionF objectRotation);

        ///<summary>
        ///Sets camera as Third Person with specific parameters.
        ///</summary>
        ///<remarks>
        ///<param name="hostObject">the host object of the camera. Camera spins around host object's center. The camera's
        ///entity is hung under it first, through <see cref="EntityManager.SetOwner"/>: its transforms are relative to
        ///the host.</param>
        ///<param name="initialRelRotation">initial rotation relative to host object's rotation.</param>
        ///<param name="lookAt">initial point at which camera is looking at creation (not always the same as center!).</param>
        ///<param name="distanceToObject">initial distance from host object's center</param>
        ///<param name="desiredType">is camera mode alternative (spinning around self Y axis (alt = false) or host object Y axis (alt = true)) - ignored, if isLocked is true.</param>
        ///</remarks>
        public abstract void SetThirdPersonCamera(
            Entity hostObject,
            Vector3F initialRelRotation,
            CameraType desiredType,
            Vector3? lookAt = null,
            Double? distanceToObject = null);

        public abstract void SetSpecialCamera(Vector3 lookAt);

        ///<summary>
        ///Sets the Radius of spinning for 1st and 3rd person camera.
        ///</summary>
        public abstract void SetRadius(Double radius);

        ///<summary>
        ///Sets the LookAt of Third Person camera.
        ///</summary>
        public abstract void SetThirdPersonLookAt(Vector3 lookAt);

        ///<summary>
        ///Sets the camera's rear view.
        ///</summary>
        public abstract void SetThirdPersonLookBackwards(bool lookBackwards);

        ///<summary>
        ///Resets all third person related properties and set the camera type to Free; the view stays as it was. The
        ///camera's entity stays under its host until taken off through <see cref="EntityManager.RemoveOwner"/>.
        ///</summary>
        public abstract void DeleteThirdPersonConfig();

        /// <summary>
        /// Make camera jump to a certain object in the specified time
        /// </summary>
        /// <param name="lookAtObject"></param>
        /// <param name="time"></param>
        public abstract void JumpToObject(Entity lookAtObject, int time);

        /// <summary>Travels to a point over <paramref name="time"/> milliseconds. The same machinery
        /// <see cref="JumpToObject"/> rides - offered because a place can be worth flying to without an object there
        /// to aim at.</summary>
        public abstract void MoveTo(Vector3 position, int time);

        ///<summary>
        ///Returns quaternion, which has the same angle as the forward vector of the hosted object in left handed coordinate system
        ///</summary>
        public QuaternionF SyncRotationWithEntityForwardLH()
        {
            return QuaternionF.RotationLookAtLH(SubjectForward, EntityRotationMatrix.Up);
        }

        ///<summary>
        ///Returns quaternion, which has the same angle as the backward vector of the hosted object in left handed coordinate system
        ///</summary>
        public QuaternionF SyncRotationWithEntityBackwardLH()
        {
            return QuaternionF.RotationLookAtLH(-SubjectForward, EntityRotationMatrix.Up);
        }

        /// <summary>Which way the <see cref="Subject"/> FACES: -Z, as <c>Matrix4x4F.Forward</c> states and the models
        /// are authored. Not <see cref="Vector3.ForwardLH"/> (+Z), which is about VIEW space depth.</summary>
        public Vector3F SubjectForward => EntityRotationMatrix.Forward;
    }
}

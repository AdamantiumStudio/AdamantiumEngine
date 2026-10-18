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

        partial void OnFovChanged(Single value) => ProjectionStale = true;

        partial void OnZNearChanged(Single value) => ProjectionStale = true;

        partial void OnZFarChanged(Single value) => ProjectionStale = true;

        partial void OnWidthChanged(uint value) => ProjectionStale = true;

        partial void OnHeightChanged(uint value) => ProjectionStale = true;

        [Bindable] private CameraProjectionType projectionType;

        /// <summary>Whether the camera looks down a cube's diagonal, from above: a free camera turns to that angle about the
        /// point it looks at, smoothly - one on a host once it is free - and back to where it looked when this is let
        /// go; going round the point meanwhile is part of it. Turned away by anything else, a face of the orientation
        /// cube or a reset, it is no longer isometric and this clears. Either projection: orthographic, it is the
        /// isometric view of drawings; in perspective, the same angle with depth.</summary>
        [Bindable] private bool isIsometric;

        /// <summary>Whether the user's mouse and keys leave the camera where it stands, even looked through; it still
        /// moves with what it rides on.</summary>
        [Bindable] private bool isLocked;

        /// <summary>How much an orthographic view is zoomed: its projection scaled along X and Y, never Z, so
        /// nothing is clipped away however close it comes. 1 is a world unit to a pixel; the wheel multiplies it.</summary>
        [Bindable] private Vector2F orthoScale = Vector2F.One;

        partial void OnOrthoScaleChanged(Vector2F value) => ProjectionStale = true;

        /// <summary>The near plane of an orthographic view: behind the camera, as a flat view shows the same
        /// wherever along its view the camera stands, so nothing behind it is cut away.</summary>
        [Bindable] private float orthoNear = -10000;

        /// <summary>The far plane of an orthographic view.</summary>
        [Bindable] private float orthoFar = 10000;

        partial void OnOrthoNearChanged(float value) => ProjectionStale = true;

        partial void OnOrthoFarChanged(float value) => ProjectionStale = true;

        /// <summary>How long a change of <see cref="ProjectionType"/> takes on screen, in milliseconds: the projection
        /// flows from one to the other keeping the plane at <see cref="FocusDistance"/> the same size. 0 changes it at
        /// once.</summary>
        public int ProjectionTransitionTime { get; set; } = 300;

        /// <summary>How far ahead lies the plane the camera looks at: a change of projection keeps it the same size on
        /// screen, and turning to <see cref="IsIsometric"/> goes about the point on it. A camera orbiting a subject uses its distance to
        /// it.</summary>
        public double FocusDistance { get; set; } = 10;

        /// <summary>A value the projections are built from changed - the field of view, a clipping plane or the size - and
        /// the next <see cref="Update"/> builds them again, on the world's thread.</summary>
        protected bool ProjectionStale { get; set; }


        public Matrix4x4F ViewMatrix { get; set; } = Matrix4x4F.Identity;

        public Matrix4x4F PerspectiveProjection { get; set; }

        public Matrix4x4F UiProjection { get; set; }

        /// <summary>The orthographic projection, scaled by <see cref="OrthoScale"/>, from <see cref="OrthoNear"/> to
        /// <see cref="OrthoFar"/>.</summary>
        public Matrix4x4F OrthoProjection { get; set; } = Matrix4x4F.Identity;

        public Matrix4x4F RotationMatrix => Matrix4x4F.RotationQuaternion(Rotation);

        public BoundingFrustum Frustum { get; set; }

        public Vector3? LookAt { get; set; }

        [Bindable]
        public partial Double Velocity { get; set; }

        public Double DragVelocity { get; set; }

        public Double CurrentVelocity { get; set; }

        /// <summary>How many times larger one wheel notch makes what a camera looks at, near or far, in either projection.</summary>
        public Double ZoomStep { get; set; } = 1.08;

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
        /// a third-person camera hangs off the object it follows - the scene is shifted by THIS one. Hung on another
        /// entity, it is still on its way there for <see cref="HostChangeTime"/>.</summary>
        public Vector3 WorldPosition => (Owner?.Transform.WorldPosition ?? Vector3.Zero) + TravelOffset;

        /// <summary>How long, in milliseconds, a camera hung on another entity or taken off one takes to get from where it
        /// stood and looked to its new place and view; 0 jumps there.</summary>
        public int HostChangeTime { get; set; } = 250;

        /// <summary>How far the camera still is from its place while it travels to a new host.</summary>
        protected Vector3 TravelOffset { get; set; }

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

        public virtual Matrix4x4F ProjectionMatrix => ProjectionOf(ProjectionType);

        /// <summary>The projection a camera draws with for the kind.</summary>
        public Matrix4x4F ProjectionOf(CameraProjectionType type)
        {
            switch (type)
            {
                case CameraProjectionType.Ortho:
                    return OrthoProjection;
                case CameraProjectionType.UI:
                    return UiProjection;
                default:
                    return PerspectiveProjection;
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

                // The SUBJECT's rotation, in the world: a part of a model turns with what it hangs on. The guard used to
                // ask about the host and the answer described the camera (Owner.Transform is the camera's own).
                var world = Subject.Transform.GetWorldMatrixF();
                var turn = Matrix4x4F.Identity;
                turn.Right = Vector3F.Normalize(world.Right);
                turn.Up = Vector3F.Normalize(world.Up);
                turn.Forward = Vector3F.Normalize(world.Forward);
                return turn;
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
        ///Angles must be in degrees. A free camera turns as an editor's does: about the world's vertical, and up or down
        ///about its own right short of straight up or down, so its horizon stays level (so do <see cref="RotateRight"/>
        ///and <see cref="RotateUp"/>). <see cref="IsIsometric"/>, it only goes round the point it looks at, its angle
        ///down kept, and <see cref="RotateForward"/> leaves it.
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
        /// Flies, keeping the way it looks, over <paramref name="time"/> milliseconds to where
        /// <paramref name="lookAtObject"/> fills the view with <see cref="FramingMargin"/> to spare; an object with no
        /// bounds is looked at from a few units away. That distance becomes the <see cref="FocusDistance"/>, and a flat
        /// view is scaled to show the object as large.
        /// </summary>
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

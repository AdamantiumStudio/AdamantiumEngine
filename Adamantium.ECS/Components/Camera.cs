using System;
using Adamantium.Core;
using Adamantium.ECS.Components.Extensions;
using Adamantium.ECS.Components;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;

namespace Adamantium.ECS.Components
{
    /// <summary>
    /// Component represents camera
    /// </summary>
    public class Camera : CameraBase
    {
        private QuaternionF rotationToSync;
        private int rotationTime;
        private bool rotationDone = true;
        private double rotationDuration;
        private QuaternionF startingRotation;
        private int moveTime;
        private bool moveToObjectDone = true;
        private double moveToDuration = 0;
        private Vector3 startingOffset;
        private Vector3 endingPosition;
        private Vector3 distance;
        private Vector3 diameter;
        private Vector3 center;

        // How far a jump stands from an object with no bounds to frame: an empty entity, a group.
        private const double UnframedDistance = 5;

        private const double FocusFloor = 1;
        private const float SmallestOrthoScale = 1e-6f;
        private const float LargestOrthoScale = 1e6f;

        private void ScaleOrtho(double factor)
        {
            var scaled = OrthoScale * (float)factor;
            if (scaled.X is >= SmallestOrthoScale and <= LargestOrthoScale && scaled.Y is >= SmallestOrthoScale and <= LargestOrthoScale)
            {
                OrthoScale = scaled;
            }
        }

        private static readonly float IsometricAxis = 1 / MathF.Sqrt(3);

        private CameraProjectionType shownProjection;
        private bool projectionShown;
        private bool blending;
        private bool turning;
        private Matrix4x4F blendFrom;
        private Matrix4x4F blendedProjection;
        private double changeElapsed;
        private double changeTime;
        private QuaternionF turnFrom;
        private QuaternionF turnTo;
        private Vector3 turnPivot;
        private double turnDistance;
        private double turnElapsed;
        private double turnTime;
        private bool isometricShown;
        private bool isometricTurned;
        private QuaternionF rotationBeforeIsometric;
        private QuaternionF isometricRotation;
        private Entity shownSubject;
        private bool subjectShown;
        private bool travelling;
        private double travelElapsed;
        private Vector3 travelFrom;
        private QuaternionF travelViewFrom;
        private Vector3 lastStanding;
        private bool entityTurnKnown;
        private QuaternionF knownEntityTurn;
        private QuaternionF lastView = QuaternionF.Identity;

        public Camera()
        {
        }

        /// <summary>
        /// Initializes camera instance from field of view , window width, window height, near and far planes and initial depth state
        /// </summary>
        /// <param name="fov">Field of view</param>
        /// <param name="width">Window width</param>
        /// <param name="height">Window height</param>
        /// <param name="znear">Distance to near plane</param>
        /// <param name="zfar">Distance to far plane</param>
        /// <param name="inverseDepth">Is depth should be inverted</param>
        /// <remarks>If <paramref name="inverseDepth"/> is true, then znear and zfar will be swap when creating projection matrix</remarks>
        public Camera(Single fov, UInt32 width, UInt32 height, Single znear, Single zfar, bool inverseDepth = true)
        {
            IsDepthInversed = !inverseDepth;
            Rotation = QuaternionF.Identity;

            LookAt = Vector3.ForwardLH;
            Up = -Vector3.UnitY;
            Velocity = 1;
            CurrentVelocity = 0;
            DragVelocity = 200;
            Width = width;
            Height = height;

            Fov = fov;
            AspectRatio = (float)width / height;
            ZNear = znear;
            ZFar = zfar;

            RotationSpeed = 35f;
            MouseSensitivity = 0.8f;
            Type = CameraType.Free;

            IsLookingBackwards = false;

            Initialize();
        }

        /// <summary>
        /// Initializes camera instance from position, lookAt vector, Up vector, field of view , window width, window height, near and far planes and initial depth state
        /// </summary>
        /// <param name="lookAt">Look at vector defining forward direction</param>
        /// <param name="up">Up vector defining initial up direction</param>
        /// <param name="fov">Field of view</param>
        /// <param name="width">Window width</param>
        /// <param name="height">Window height</param>
        /// <param name="znear">Distance to near plane</param>
        /// <param name="zfar">Distance to far plane</param>
        /// <param name="inverseDepth">Is depth should be inverted</param>
        /// <remarks>If <paramref name="inverseDepth"/> is true, then znear and zfar will be swap when creating projection matrix</remarks>
        public Camera(Vector3 lookAt, Vector3 up, Single fov, UInt32 width, UInt32 height,
           Single znear, Single zfar, bool inverseDepth = true)
        {
            IsDepthInversed = inverseDepth;
            Rotation = QuaternionF.Identity;
            LookAt = lookAt;
            Up = up;
            Velocity = 1;
            CurrentVelocity = 0;
            DragVelocity = 200;
            Width = width;
            Height = height;

            Fov = fov;
            AspectRatio = (float)width / height;
            ZNear = znear;
            ZFar = zfar;

            RotationSpeed = 35f;
            MouseSensitivity = 5.0f;
            Type = CameraType.Free;

            IsLookingBackwards = false;
            Initialize();
        }

        /// <summary>
        /// Initialize view/projection matrices for camera
        /// </summary>
        public sealed override void Initialize()
        {
            GetAxisFromViewMatrix();
            BuildPerspectiveFoV();
            BuildOrthoProjection();
            BuildUiProjection();
            ProjectionStale = false;
            Frustum = new BoundingFrustum(ViewMatrix * ProjectionMatrix);
            base.Initialize();
        }

        /// <summary>
        /// Updates camera`s frustum
        /// </summary>
        public void UpdateFrustum()
        {
            Frustum.ViewProjection = ViewMatrix * ProjectionMatrix;
        }

        private Matrix4x4F _viewProjectionMatrix;

        public Matrix4x4F ViewProjectionMatrix => _viewProjectionMatrix;

        ///<summary>
        ///Decides which algorithm will be used for building the field of view.
        ///</summary>
        ///<remarks>
        ///Uses Camera Component as input.
        ///</remarks>
        public void BuildPerspectiveFoV()
        {
            float znear = ZNear;
            float zfar = ZFar;
            // if (IsDepthInversed)
            // {
            //     znear = ZFar;
            //     zfar = ZNear;
            // }

            if (Width > Height)
            {
                BuildPerspectiveFovX(znear, zfar);
            }
            else
            {
                BuildPerspectiveFovY(znear, zfar);
            }
        }

        private void BuildOrthoProjection()
        {
            var far = Math.Max(OrthoFar, OrthoNear + 1);
            OrthoProjection = Matrix4x4F.OrthoLH(Width, Height, OrthoNear, far) * Matrix4x4F.Scaling(OrthoScale.X, OrthoScale.Y, 1);
        }

        /// <summary>The projection drawn now: while <see cref="CameraBase.ProjectionType"/> changes, one flowing from the
        /// old into the new.</summary>
        public override Matrix4x4F ProjectionMatrix =>
            blending ? blendedProjection : ProjectionOf(projectionShown ? shownProjection : ProjectionType);

        private void FollowProjection(AppTime appTime)
        {
            var wanted = ProjectionType;
            var isometric = IsIsometric;
            if (isometricTurned && !turning && Math.Abs(QuaternionF.Dot(Rotation, isometricRotation)) < 0.9999f)
            {
                isometricTurned = false;
                isometricShown = false;
                IsIsometric = isometric = false;
            }

            if (!projectionShown)
            {
                shownProjection = wanted;
                isometricShown = false;
                projectionShown = true;
            }
            else if (wanted != shownProjection)
            {
                StartProjectionChange(shownProjection, wanted);
                shownProjection = wanted;
            }

            if (isometric != isometricShown || (isometric && !isometricTurned && !turning && TurnsToIsometric()))
            {
                StartIsometricTurn(isometric);
                isometricShown = isometric;
            }

            var step = appTime.FrameTime * 1000;
            if (blending)
            {
                changeElapsed += step;
                var done = Done(changeElapsed, changeTime);
                blendedProjection = Matrix4x4F.Lerp(blendFrom, BlendableProjection(wanted), Eased(done));
                blending = done < 1;
            }

            if (turning)
            {
                turnElapsed += step;
                var done = Done(turnElapsed, turnTime);
                TurnAbout(QuaternionF.Slerp(turnFrom, turnTo, Eased(done)));
                turning = done < 1;
            }
        }

        private static double Done(double elapsed, double time) => time > 0 ? Math.Min(elapsed / time, 1) : 1;

        private static float Eased(double done) => (float)(done * done * (3 - 2 * done));

        private void StartProjectionChange(CameraProjectionType from, CameraProjectionType to)
        {
            var shown = blending ? blendedProjection : BlendableProjection(from);
            var depth = FocusDepth();
            var wasFlat = from != CameraProjectionType.Perspective;
            var flat = to != CameraProjectionType.Perspective;
            if (!wasFlat && flat)
            {
                OrthoScale = OrthoScaleAt(depth);
                BuildOrthoProjection();
            }
            else if (wasFlat && !flat)
            {
                var matching = Math.Clamp(Width * PerspectiveProjection.M11 / (2 * OrthoScale.X), ZNear * 2, ZFar / 2);
                StepToFocus(depth, matching);
                if (turning)
                {
                    turnDistance = matching;
                }
            }

            blendFrom = shown;
            blending = blending || wasFlat != flat;
            changeElapsed = 0;
            changeTime = ProjectionTransitionTime;
        }

        private void StartIsometricTurn(bool toIsometric)
        {
            var wasTurned = isometricTurned;
            isometricTurned = toIsometric && TurnsToIsometric();
            QuaternionF target;
            if (isometricTurned)
            {
                if (!wasTurned && !turning)
                {
                    rotationBeforeIsometric = Rotation;
                }

                isometricRotation = IsometricRotation();
                target = isometricRotation;
            }
            else if (!toIsometric && wasTurned && TurnsToIsometric())
            {
                target = rotationBeforeIsometric;
            }
            else
            {
                return;
            }

            var depth = FocusDepth();
            turning = true;
            turnFrom = Rotation;
            turnTo = target;
            turnPivot = Owner.Transform.Position + ForwardOf(Rotation) * depth;
            turnDistance = depth;
            turnElapsed = 0;
            turnTime = ProjectionTransitionTime;
        }

        private double FocusDepth()
        {
            var depth = Type.IsThirdPerson() && Subject != null
                ? (Subject.GetCenterAbsolute() - Owner.Transform.WorldPosition).Length()
                : FocusDistance;
            return Math.Max(depth, ZNear * 2);
        }

        private Vector2F OrthoScaleAt(double depth) => new(
            (float)(Width * PerspectiveProjection.M11 / (2 * depth)),
            (float)(Height * PerspectiveProjection.M22 / (2 * depth)));

        private bool TurnsToIsometric() => Type == CameraType.Free && Owner.Owner == null;

        private Matrix4x4F BlendableProjection(CameraProjectionType type)
        {
            if (type == CameraProjectionType.Perspective)
            {
                return PerspectiveProjection;
            }

            return Matrix4x4F.OrthoLH(Width, Height, ZNear, OrthoFar) * Matrix4x4F.Scaling(OrthoScale.X, OrthoScale.Y, 1);
        }

        private void StepToFocus(double depth, double matching)
        {
            if (Type == CameraType.Free)
            {
                Owner.Transform.Position += Forward * (depth - matching);
                FocusDistance = matching;
            }
            else if (Type.IsThirdPerson())
            {
                Radius *= matching / depth;
            }
        }

        private QuaternionF IsometricRotation()
        {
            var forward = Forward;
            var diagonal = new Vector3F(forward.X < 0 ? -1 : 1, 1, forward.Z < 0 ? -1 : 1) * IsometricAxis;
            return QuaternionF.RotationLookAtLH(diagonal, Vector3F.Up);
        }

        private void TurnAbout(QuaternionF rotation)
        {
            Rotation = rotation;
            if (Type == CameraType.Free)
            {
                var view = Matrix4x4F.RotationQuaternion(rotation);
                Owner.Transform.Position = turnPivot - new Vector3(view.M13, view.M23, view.M33) * turnDistance;
            }
        }
        
        private void BuildUiProjection()
        {
            float znear = ZNear;
            float zfar = ZFar;
            // if (IsDepthInversed)
            // {
            //     znear = ZFar;
            //     zfar = ZNear;
            // }
            
            UiProjection = Matrix4x4F.OrthoOffCenter(0, Width, 0, Height, znear, zfar);
        }

        private void BuildPerspectiveFovX(float zNear, float zFar)
        {
            PerspectiveProjection = Matrix4x4F.PerspectiveFovX(Fov, (float)Width / Height, zNear, zFar);
        }

        private void BuildPerspectiveFovY(float zNear, float zFar)
        {
            PerspectiveProjection = Matrix4x4F.PerspectiveFovY(MathHelper.DegreesToRadians(Fov), (float)Width / Height, zNear, zFar);
        }

        private void GetAxisFromViewMatrix()
        {
            Right = new Vector3(ViewMatrix.M11, ViewMatrix.M21, ViewMatrix.M31);
            Up = new Vector3(ViewMatrix.M12, ViewMatrix.M22, ViewMatrix.M32);
            Forward = new Vector3(ViewMatrix.M13, ViewMatrix.M23, ViewMatrix.M33);
        }

        /// <inheritdoc />
        public void RotateAroundSelectedObject(QuaternionF newRotation, int time)
        {
            rotationDuration = 0;
            rotationToSync = newRotation;
            rotationTime = time;
            rotationDone = false;
            startingRotation = Rotation;
            startingOffset = Owner.Transform.Position;
            if (LookAtObject != null)
            {
                diameter = (Vector3)Forward * LookAtObject.GetDiameter() * Fov;
                center = LookAtObject.GetCenterAbsolute() + diameter + Owner.Transform.Position - LookAtObject.GetLocalCenter();
            }
            Type = CameraType.Special;
        }

        /// <summary>Drops a turn or a flight still in progress, leaving the camera exactly where it got to. What a drag
        /// needs before it takes over: otherwise the animation keeps writing the rotation the drag is changing.</summary>
        public void CancelTravel()
        {
            if (rotationDone && moveToObjectDone) return;

            rotationDone = true;
            moveToObjectDone = true;
            rotationDuration = 0;
            moveToDuration = 0;
            SetFreeCamera();
        }

        private void ContiniousRotation(AppTime appTime)
        {
            if (!rotationDone)
            {
                rotationDuration += appTime.FrameTime * 1000;
                var weight = (float) rotationDuration / rotationTime;
                Rotation = QuaternionF.Lerp(startingRotation, rotationToSync, weight);
                if (rotationDuration >= rotationTime)
                {
                    Rotation = rotationToSync;
                    rotationDone = true;
                    rotationDuration = 0;
                    SetFreeCamera();
                }
            }
        }

        /// <inheritdoc />
        /// <inheritdoc />
        public override void MoveTo(Vector3 position, int time)
        {
            moveTime = time;
            moveToObjectDone = false;
            moveToDuration = 0;
            startingOffset = Owner.Transform.Position;
            endingPosition = position;
        }

        /// <inheritdoc />
        public override void JumpToObject(Entity lookAtObject, int time)
        {
            LookAtObject = lookAtObject;
            moveTime = time;
            moveToDuration = 0;
            startingOffset = Owner.Transform.Position;
            var world = lookAtObject.Transform.GetWorldMatrixF();
            var size = lookAtObject.GetDiameter() * Math.Max(world.Right.Length(), Math.Max(world.Up.Length(), world.Backward.Length()));
            var away = size > 0 ? FramingDistance(size) : UnframedDistance;
            endingPosition = lookAtObject.GetCenterAbsolute() - (Vector3)Forward * away;
            Type = CameraType.Free;
            FocusDistance = away;
            if (ProjectionType == CameraProjectionType.Ortho)
            {
                OrthoScale = OrthoScaleAt(away);
            }

            // Last: the flight starts once where it goes is known.
            moveToObjectDone = false;
        }

        // From the center: out of the body, then far enough back for a diameter in the world to fill the field of view
        // (Fov is in degrees), with the margin.
        private double FramingDistance(double size) =>
            (size / 2 + size / (2 * Math.Tan(MathHelper.DegreesToRadians(Fov / 2)))) * FramingMargin;

        private void MoveToPoint(AppTime appTime)
        {
            if (!moveToObjectDone)
            {
                moveToDuration += appTime.FrameTime * 1000;
                var weight = (float)moveToDuration / moveTime;
                Owner.Transform.Position = Vector3.Lerp(startingOffset, endingPosition, weight);
                if (moveToDuration >= moveTime)
                {
                    Owner.Transform.Position = endingPosition;
                    moveToObjectDone = true;
                    moveToDuration = 0;
                    SetFreeCamera();
                }
            }
        }

        public override void Update(AppTime appTime)
        {
            // Built in Initialize only, the projections kept the field of view and planes a camera was made with, while
            // whatever reads ZNear or ZFar directly moved on with an edit.
            if (ProjectionStale && Frustum != null && Width > 0 && Height > 0)
            {
                ProjectionStale = false;
                BuildPerspectiveFoV();
                BuildOrthoProjection();
                BuildUiProjection();
            }

            FollowEntityTurn();
            FollowProjection(appTime);

            // Rotation is a STRUCT behind a property: Rotation.Normalize() normalized a copy and dropped it, so the
            // quaternion drifted from unit length as mouse-look multiplied into it - and a non-unit quaternion scales
            // the rotation matrix, shrinking the third-person offset until the camera sat inside its subject.
            var rotation = Rotation;
            rotation.Normalize();
            Rotation = rotation;

            MoveToPoint(appTime);

            // Whatever the camera type is. It used to tick only inside the Special branch, so the orientation gizmo
            // armed a turn the free camera never performed - the click registered and nothing moved.
            ContiniousRotation(appTime);
            if (Type == CameraType.Free)
            {
                ViewMatrix = Matrix4x4F.RotationQuaternion(Rotation);
                //ViewMatrix =  Matrix4x4F.LookAtRH(new Vector3F(0, 0, 0), Vector3F.Zero, Vector3F.Up);
                //ViewMatrix.Transpose();
            }
            else if (Type == CameraType.FirstPerson)
            {
                if (Subject == null)
                {
                    SetFreeCamera();
                    ViewMatrix = Matrix4x4F.RotationQuaternion(Rotation);
                }
                else
                {
                    ViewMatrix = Matrix4x4F.RotationQuaternion(QuaternionF.Multiply(Rotation, SyncRotationWithEntityForwardLH()));
                }
            }
            else if (Type == CameraType.Special)
            {
                if (LookAtObject == null)
                {
                    ViewMatrix = Matrix4x4F.RotationQuaternion(Rotation);
                }
                else
                {
                    Matrix4x4F rotMatrix = Matrix4x4F.RotationQuaternion(Rotation);
                    distance = center - Owner.Transform.Position;
                    Owner.Transform.Position = (center) - new Vector3(rotMatrix.M13, rotMatrix.M23, rotMatrix.M33) * distance.Length();
                    ViewMatrix = Matrix4x4F.LookToLH(Vector3F.Zero, -new Vector3F(rotMatrix.M13, rotMatrix.M23, rotMatrix.M33), new Vector3F(rotMatrix.M12, rotMatrix.M22, rotMatrix.M32));
                }
            }
            else
            {
                // A subject that has gone away leaves an orbit around nowhere.
                if (Subject == null)
                {
                    SetFreeCamera();
                    ViewMatrix = Matrix4x4F.RotationQuaternion(Rotation);
                    return;
                }

                QuaternionF tmpRotation;

                if (Type == CameraType.ThirdPersonLocked)
                {
                    tmpRotation = SyncRotationWithEntityForwardLH();
                    tmpRotation = QuaternionF.Multiply(Rotation, tmpRotation);
                }
                else
                {
                    tmpRotation = Rotation;
                }

                Matrix4x4F rotMatrix = Matrix4x4F.RotationQuaternion(tmpRotation);

                var back = Vector3.Multiply(new Vector3(rotMatrix.M13, rotMatrix.M23, rotMatrix.M33), Radius);

                // In the subject's space, from its CENTER: an offset from the origin orbits whatever point the model
                // was authored around, not the thing on screen.
                var center = (Vector3)Subject.GetLocalCenter();
                Owner.Transform.Position = center - back;

                ViewMatrix = Matrix4x4F.LookAtLH(
                    Vector3F.Zero,
                    LookAt != null ? (Vector3)LookAt - Owner.Transform.Position : back,
                    new Vector3F(rotMatrix.M12, rotMatrix.M22, rotMatrix.M32));

            }

            FollowHost(appTime);
            GetAxisFromViewMatrix();
            UpdateFrustum();
            _viewProjectionMatrix = ViewMatrix * ProjectionMatrix;
        }

        private void FollowEntityTurn()
        {
            if (Type == CameraType.Special && Owner.Owner == null && entityTurnKnown)
            {
                Owner.Transform.Rotation = QuaternionF.Conjugate(Rotation);
                knownEntityTurn = Owner.Transform.Rotation;
                return;
            }

            if (Type != CameraType.Free || Owner.Owner != null)
            {
                entityTurnKnown = false;
                return;
            }

            var turn = Owner.Transform.Rotation;
            var turnedByOthers = entityTurnKnown ? turn != knownEntityTurn : !Same(turn, QuaternionF.Identity);
            if (turnedByOthers)
            {
                Rotation = QuaternionF.Conjugate(turn);
            }
            else if (turn != QuaternionF.Conjugate(Rotation))
            {
                Owner.Transform.Rotation = QuaternionF.Conjugate(Rotation);
            }

            knownEntityTurn = Owner.Transform.Rotation;
            entityTurnKnown = true;
        }

        private static bool Same(QuaternionF a, QuaternionF b) =>
            Math.Abs(QuaternionF.Dot(a, b)) > 0.99999f * a.Length() * b.Length();

        private void FollowHost(AppTime appTime)
        {
            var subject = Subject;
            var standing = Owner.Transform.WorldPosition;
            ViewMatrix.Decompose(out _, out QuaternionF view, out _);
            if (!subjectShown)
            {
                shownSubject = subject;
                subjectShown = true;
            }
            else if (subject != shownSubject)
            {
                shownSubject = subject;
                travelFrom = lastStanding;
                travelViewFrom = lastView;
                travelElapsed = 0;
                travelling = true;
            }

            if (travelling)
            {
                travelElapsed += appTime.FrameTime * 1000;
                var done = HostChangeTime > 0 ? Math.Min(travelElapsed / HostChangeTime, 1) : 1;
                var eased = done * done * (3 - 2 * done);
                TravelOffset = (travelFrom - standing) * (1 - eased);
                view = QuaternionF.Slerp(travelViewFrom, view, (float)eased);
                ViewMatrix = Matrix4x4F.RotationQuaternion(view);
                if (done >= 1)
                {
                    travelling = false;
                    TravelOffset = Vector3.Zero;
                }
            }

            lastStanding = standing + TravelOffset;
            lastView = view;
        }

        /// <inheritdoc />
        public override void SetAbsoluteRotation(float angleX, float angleY, float angleZ)
        {
            if (Type != CameraType.ThirdPersonLocked)
            {
                Rotation = QuaternionF.RotationYawPitchRoll(angleY, angleX, angleZ);
            }
            else
            {
                Rotation = QuaternionF.RotationYawPitchRoll(0, angleX, 0);
            }
        }

        /// <inheritdoc />
        public override void TranslateRight(Double relativeX)
        {
            if (Type == CameraType.Free)
            {
                Owner.Transform.Position += Vector3.Multiply(Right, relativeX);
            }
        }

        /// <inheritdoc />
        public override void TranslateUp(Double relativeY)
        {
            if (Type == CameraType.Free)
            {
                Owner.Transform.Position += Vector3.Multiply(Up, relativeY);
            }
        }

        /// <inheritdoc />
        public override void TranslateForward(Double relativeZ)
        {
            if (Type == CameraType.Free && ProjectionType == CameraProjectionType.Ortho)
            {
                ScaleOrtho(Math.Exp(relativeZ / FocusDepth()));
            }
            else if (Type == CameraType.Free)
            {
                Owner.Transform.Position += Vector3.Multiply(Forward, relativeZ);
                var focus = FocusDistance - relativeZ;
                if (focus >= Math.Max(FocusFloor, ZNear * 2) || focus > FocusDistance)
                {
                    FocusDistance = Math.Min(focus, ZFar / 2);
                }
            }
            else if (Type == CameraType.FirstPerson)
            {
                //@TODO implement
            }
            else
            {
                Radius -= relativeZ;
            }
        }

        /// <summary>
        /// Wheel notches, forward positive: what the camera looks at grows by <see cref="CameraBase.ZoomStep"/> a notch,
        /// evenly however near or far. A flat view scales <see cref="CameraBase.OrthoScale"/>, without limit; a free camera
        /// in perspective comes that much nearer the point at its focus distance, and past a metre short of it pushes the
        /// point on ahead; a camera orbiting a subject comes nearer it.
        /// </summary>
        public void Zoom(double notches)
        {
            if (ProjectionType == CameraProjectionType.Ortho)
            {
                ScaleOrtho(Math.Pow(ZoomStep, notches));
            }
            else if (Type == CameraType.Free)
            {
                TranslateForward(FocusDistance * (1 - Math.Pow(ZoomStep, -notches)));
            }
            else if (Type.IsThirdPerson())
            {
                Radius = Math.Max(Radius * Math.Pow(ZoomStep, -notches), ZNear * 2);
            }
        }

        /// <inheritdoc />
        public override void RotateRelativeXY(float angleX, float angleY)
        {
            if ((Type == CameraType.ThirdPersonFree) || (Type == CameraType.ThirdPersonFreeAlt) ||
                (Type == CameraType.ThirdPersonLocked))
            {
                angleX = -angleX;
                angleY = -angleY;
            }

            if (Type == CameraType.FirstPerson)
            {
                //@TODO implement
            }
            else if (Type == CameraType.ThirdPersonLocked)
            {
                Rotation =
                   QuaternionF.Multiply(QuaternionF.RotationAxis(Vector3F.UnitX, MathHelper.DegreesToRadians(angleX)),
                      Rotation);
            }
            else if (Type == CameraType.ThirdPersonFreeAlt)
            {
                Rotation =
                   QuaternionF.Multiply(QuaternionF.RotationAxis(Vector3F.UnitX, MathHelper.DegreesToRadians(angleX)),
                      Rotation);
                Rotation = QuaternionF.Multiply(Rotation, QuaternionF.RotationAxis(HostUpVector, MathHelper.DegreesToRadians(angleY)));
            }
            else if (Type == CameraType.Free)
            {
                TurnLevel(angleX, angleY);
            }
            else
            {
                Rotation =
                   QuaternionF.Multiply(QuaternionF.RotationAxis(Vector3F.UnitX, MathHelper.DegreesToRadians(angleX)), Rotation);
                Rotation =
                   QuaternionF.Multiply(QuaternionF.RotationAxis(Vector3F.UnitY, MathHelper.DegreesToRadians(angleY)), Rotation);
            }
        }

        private void TurnLevel(float pitchDegrees, float yawDegrees)
        {
            var yaw = QuaternionF.RotationAxis(Vector3F.UnitY, MathHelper.DegreesToRadians(yawDegrees));
            if (HoldsIsometric())
            {
                if (yawDegrees == 0)
                {
                    return;
                }

                var depth = FocusDepth();
                var pivot = Owner.Transform.Position + ForwardOf(Rotation) * depth;
                Rotation = QuaternionF.Multiply(Rotation, yaw);
                Owner.Transform.Position = pivot - ForwardOf(Rotation) * depth;
                isometricRotation = Rotation;
                return;
            }

            var turned = QuaternionF.Multiply(Rotation, yaw);
            Rotation = turned;
            if (pitchDegrees == 0)
            {
                return;
            }

            var before = Matrix4x4F.RotationQuaternion(turned);
            var upright = before.M22 >= 0;
            var share = 1f;
            for (var i = 0; i < 12; i++)
            {
                var pitched = Pitched(turned, pitchDegrees * share);
                var view = Matrix4x4F.RotationQuaternion(pitched);
                var steepEnough = Math.Abs(view.M23) < MaxLevelPitchSine || Math.Abs(view.M23) < Math.Abs(before.M23);
                if (steepEnough && (!upright || view.M22 >= 0))
                {
                    Rotation = pitched;
                    return;
                }

                share /= 2;
            }
        }

        private static QuaternionF Pitched(QuaternionF rotation, float degrees) =>
            QuaternionF.Multiply(QuaternionF.RotationAxis(Vector3F.UnitX, MathHelper.DegreesToRadians(degrees)), rotation);

        private static Vector3 ForwardOf(QuaternionF rotation)
        {
            var view = Matrix4x4F.RotationQuaternion(rotation);
            return new Vector3(view.M13, view.M23, view.M33);
        }

        private bool HoldsIsometric() => isometricTurned && IsIsometric && TurnsToIsometric() && !turning;

        private static readonly float MaxLevelPitchSine = MathF.Sin(MathHelper.DegreesToRadians(89f));

        /// <inheritdoc />
        public override void RotateRight(float angle)
        {
            if ((Type == CameraType.ThirdPersonFree) || (Type == CameraType.ThirdPersonFreeAlt) ||
                (Type == CameraType.ThirdPersonLocked))
            {
                angle = -angle;
            }

            if (Type == CameraType.Free)
            {
                TurnLevel(angle, 0);
                return;
            }

            Rotation = QuaternionF.Multiply(QuaternionF.RotationAxis(Vector3F.UnitX, MathHelper.DegreesToRadians(angle)), Rotation);
        }

        /// <inheritdoc />
        public override void RotateUp(float angle)
        {
            if (Type != CameraType.ThirdPersonLocked)
            {
                if ((Type == CameraType.ThirdPersonFree) ||
                    (Type == CameraType.ThirdPersonFreeAlt))
                {
                    angle = -angle;
                }

                if (Type == CameraType.ThirdPersonFreeAlt)
                {
                    Rotation = QuaternionF.Multiply(Rotation, QuaternionF.RotationAxis(HostUpVector, MathHelper.DegreesToRadians(angle)));
                }
                else if (Type == CameraType.Free)
                {
                    TurnLevel(0, angle);
                }
                else
                {
                    Rotation =
                       QuaternionF.Multiply(QuaternionF.RotationAxis(Vector3F.UnitY, MathHelper.DegreesToRadians(angle)),
                          Rotation);
                }
            }
        }

        /// <inheritdoc />
        public override void RotateForward(float angle)
        {
            if (Type != CameraType.ThirdPersonLocked && !HoldsIsometric())
            {
                Rotation =
                   QuaternionF.Multiply(QuaternionF.RotationAxis(Vector3F.UnitZ, MathHelper.DegreesToRadians(angle)),
                      Rotation);
            }
        }

        /// <inheritdoc />
        public override void SetFreeCamera(Vector3 position, Vector3 lookAt, Vector3 up)
        {
            DeleteThirdPersonConfig(); // sets camera type to Free here (inside method)

            LookAt = lookAt;
            Up = up;
            ViewMatrix = Matrix4x4F.LookAtLH(Vector3F.Zero, lookAt, up);

            // stub variables for Decompose function
            Vector3F scale, translation;
            QuaternionF decomposedRotation;
            ViewMatrix.Decompose(out scale, out decomposedRotation, out translation);
            Rotation = decomposedRotation;
            Type = CameraType.Free;
        }

        /// <inheritdoc />
        public override void SetFreeCamera()
        {
            DeleteThirdPersonConfig(); // sets camera type to Free here (inside method)
            Type = CameraType.Free;
        }

        /// <inheritdoc />
        public override void SetFreePosition(Vector3 position)
        {
            if (Type == CameraType.Free)
            {
                Owner.Transform.Position = position;
            }
        }

        /// <inheritdoc />
        public override void SetFreeLookAt(Vector3 lookAt)
        {
            if (Type == CameraType.Free)
            {
                // convert to offset world presentation (float)
                lookAt -= Owner.Transform.Position;
                Vector3F up = new Vector3F(ViewMatrix.M12, ViewMatrix.M22, ViewMatrix.M32);

                ViewMatrix = Matrix4x4F.LookAtLH(Vector3F.Zero, lookAt, up);

                // stub variables for Decompose function
                Vector3F scale, translation;
                QuaternionF decomposedRotation;
                ViewMatrix.Decompose(out scale, out decomposedRotation, out translation);
                Rotation = decomposedRotation;
            }
        }

        /// <inheritdoc />
        public override void SetFirstPersonCamera(Vector3 position, QuaternionF objectRotation, Double faceDistance)
        {
            Type = CameraType.FirstPerson;

            Owner.Transform.Position = position;
            Radius = faceDistance;
            Rotation = objectRotation;
        }

        /// <inheritdoc />
        public override void SetFirstPersonPositionRotation(Vector3 position, QuaternionF objectRotation)
        {
            if (Type == CameraType.FirstPerson)
            {
                Owner.Transform.Position = position;
                Rotation = objectRotation;
            }
        }

        /// <inheritdoc />
        public override void SetThirdPersonCamera(Entity hostObject, Vector3F initialRelRotation, CameraType desiredType,
           Vector3? lookAt = null, Double? distanceToObject = null)
        {
            if (!desiredType.IsThirdPerson() || hostObject == Owner)
            {
                return;
            }
            if (hostObject != null)
            {
                HostUpVector = EntityRotationMatrix.Up;

                LookAt = lookAt;
                if (desiredType.IsThirdPerson())
                {
                    if (distanceToObject != null)
                    {
                        Radius = (Double)distanceToObject;
                    }
                    else
                    {
                        // From the CENTER: get out of the body, then stand back far enough for the diameter to
                        // subtend the field of view. Fov is in DEGREES.
                        var diameter = hostObject.GetDiameter();

                        if (diameter > 0)
                        {
                            Radius = FramingDistance(diameter);
                        }
                        else if (Radius <= 0)
                        {
                            // Nothing measurable to frame - a subject whose bounds have not been built yet. Keep the
                            // distance we already stand at rather than collapsing the orbit onto its center.
                            Radius = (WorldPosition - hostObject.GetCenterAbsolute()).Length();
                        }
                    }
                }

                if (desiredType != CameraType.ThirdPersonLocked)
                {
                    Rotation = SyncRotationWithEntityForwardLH();
                    Rotation *= QuaternionF.RotationYawPitchRoll(MathHelper.DegreesToRadians(initialRelRotation.Y), MathHelper.DegreesToRadians(initialRelRotation.X), MathHelper.DegreesToRadians(initialRelRotation.Z));
                    //Rotation = QuaternionF.Multiply(QuaternionF.RotationAxis(Vector3F.UnitX, MathHelper.DegreesToRadians(initialRelRotation.X)), Rotation);
                    //Rotation = QuaternionF.Multiply(Rotation, QuaternionF.RotationAxis(HostUpVector, MathHelper.DegreesToRadians(initialRelRotation.Y)));
                    //Rotation = QuaternionF.Multiply(QuaternionF.RotationAxis(Vector3F.UnitZ, MathHelper.DegreesToRadians(initialRelRotation.Z)), Rotation);
                }
                else
                {
                    Rotation = QuaternionF.RotationAxis(Vector3F.UnitX, MathHelper.DegreesToRadians(initialRelRotation.X));
                }
                Type = desiredType;
            }
        }

        /// <inheritdoc />
        public override void SetSpecialCamera(Vector3 lookAt)
        {
            Type = CameraType.Special;

            LookAt = lookAt;
        }

        /// <inheritdoc />
        public override void SetRadius(Double radius)
        {
            if (Type != CameraType.Free)
            {
                Radius = radius;
            }
        }

        /// <inheritdoc />
        public override void SetThirdPersonLookAt(Vector3 lookAt)
        {
            if ((Type == CameraType.ThirdPersonFree) || (Type == CameraType.ThirdPersonFreeAlt))
            {
                LookAt = lookAt;
            }
        }

        /// <inheritdoc />
        public override void SetThirdPersonLookBackwards(bool lookBackwards)
        {
            if (lookBackwards != IsLookingBackwards)
            {
                if ((Type == CameraType.ThirdPersonFree) ||
                    (Type == CameraType.ThirdPersonFreeAlt))
                {
                    if (Owner != null)
                    {
                        IsLookingBackwards = lookBackwards;

                        if (lookBackwards == true)
                        {
                            FormerRotation = Rotation;

                            Rotation = SyncRotationWithEntityBackwardLH();
                        }
                        else
                        {
                            Rotation = FormerRotation;
                        }
                    }
                    else
                    {
                        IsLookingBackwards = false; // for robustness
                    }
                }
            }
        }

        /// <inheritdoc />
        public override void DeleteThirdPersonConfig()
        {
            if (Type.IsThirdPerson() || Type == CameraType.FirstPerson)
            {
                ViewMatrix.Decompose(out _, out var looking, out _);
                Rotation = looking;
            }

            Type = CameraType.Free;
        }
    }
}

using System;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.MVVM;

namespace Adamantium.ECS.Components
{
    /// <summary>
    /// A shape round an entity for the physics and picking: its own sizes, placed on the entity by <see cref="Position"/>,
    /// <see cref="Rotation"/> and <see cref="Scale"/>. While <see cref="IsFitted"/>, the sizes and the placement follow the
    /// entity's mesh.
    /// </summary>
    [ViewModel]
    public abstract partial class Collider : ActivatableComponent
    {
        private bool outlineIsStale;
        private int fitDepth;
        private bool isShaped;
        private TypedAngles angles;

        // A collider's own shape is not built here: it is not made yet. Its constructor sets its defaults between
        // BeginFit and EndFit.
        protected Collider()
        {
            fitDepth++;
            Rotation = QuaternionF.Identity;
            Scale = Vector3F.One;
            IsFitted = true;
            fitDepth--;
        }

        /// <summary>Whether the collider fits its entity's mesh, and fits it again when the mesh changes. A size or the
        /// position or rotation set by hand makes it false; set true again, the collider fits the mesh at once.</summary>
        [Bindable]
        public partial bool IsFitted { get; set; }

        /// <summary>Where the collider's center stands in its entity's own space.</summary>
        [Bindable]
        public partial Vector3F Position { get; set; }

        /// <summary>How the collider is turned against its entity.</summary>
        [Bindable, Affects(nameof(Angles))]
        public partial QuaternionF Rotation { get; set; }

        /// <summary><see cref="Rotation"/> in degrees about X, Y and Z, as people type a turn; 200 stays 200 until the
        /// collider is turned otherwise. Set by hand, as <see cref="Rotation"/> is, it makes the collider unfitted.</summary>
        [DoNotClone]
        public Vector3F Angles
        {
            get => angles.Read(Rotation);
            set => Rotation = angles.Take(value);
        }

        /// <summary>How the collider is stretched along its own axes, about its center; a fit leaves it as it is.
        /// </summary>
        [Bindable]
        public partial Vector3F Scale { get; set; }

        /// <summary>The box round the collider in its entity's own space, built from its sizes and placement.</summary>
        [Bindable]
        public partial Bounds Bounds { get; private set; }

        [Bindable]
        public partial bool DisplayCollider { get; set; }

        /// <summary>Whether the collider only tells what enters and leaves it, pushing and weighing nothing.</summary>
        [Bindable]
        public partial bool IsTrigger { get; set; }

        /// <summary>The name of the project's layer the collider is on; null or empty for its body's.</summary>
        [Bindable]
        public partial string Layer { get; set; }

        /// <summary>The physics material file (<c>.aphysmat</c>) the collider rubs and bounces by; null or empty for its
        /// body's.</summary>
        [Bindable]
        public partial string MaterialPath { get; set; }

        // The CPU mesh used to visualize the collider (debug draw), each collider's own. Its GPU buffers are built and
        // owned by the render-layer geometry cache, keyed by this mesh.
        [Bindable, DoNotClone]
        public partial Mesh Geometry { get; protected set; }

        /// <summary>How the collider's own space is placed in its entity's: scaled, turned, then moved.</summary>
        public Matrix4x4F Placement =>
            Matrix4x4.CreateScale<float>(Scale) * Matrix4x4.CreateFromQuaternion<float>(Turn) *
            Matrix4x4.CreateTranslation<float>(Position);

        /// <summary><see cref="Rotation"/> made whole; no turn for one that holds none.</summary>
        public QuaternionF Turn =>
            Rotation.LengthSquared() > 0 ? QuaternionF.Normalize(Rotation) : QuaternionF.Identity;

        partial void OnIsFittedChanged(bool value)
        {
            if (value && Owner?.GetComponent<MeshData>() is { Mesh: { } mesh })
            {
                FitTo(mesh);
            }
        }

        partial void OnPositionChanged(Vector3F value) => Resize();

        partial void OnRotationChanged(QuaternionF value) => Resize();

        partial void OnScaleChanged(Vector3F value) => Reshape();

        partial void OnIsTriggerChanged(bool value) => RigidBody.ReweighAbove(Owner);

        public override void Initialize()
        {
            var meshData = Owner?.GetComponent<MeshData>();
            if (meshData == null) return;
            FollowMesh(meshData.Mesh);
            base.Initialize();
        }

        // The copy's values are a fit's, so they do not make a fitted copy one sized by hand; its shape is built once.
        public override void CloneValues(IComponent clone)
        {
            var copy = (Collider)clone;
            copy.BeginFit();
            try
            {
                base.CloneValues(clone);
                copy.isShaped = isShaped;
                copy.angles = angles;
            }
            finally
            {
                copy.EndFit();
            }
        }

        public abstract void ClearData();

        public abstract bool ContainsDataFor(CameraBase camera);

        /// <summary>The shown collider: built the first time it is asked for, and filled anew in the same mesh when the
        /// shape changed since.</summary>
        public Mesh GetVisualRepresentation()
        {
            // Cleared before building, so a change made meanwhile is built next time.
            var stale = outlineIsStale;
            outlineIsStale = false;
            if (Geometry == null)
            {
                Geometry = BuildOutline();
            }
            else if (stale)
            {
                Geometry.ReplaceWith(BuildOutline());
            }

            return Geometry;
        }

        public abstract void UpdateForCamera(CameraBase camera);

        public abstract ContainmentType IsInsideCameraFrustum(Camera camera);

        /// <summary>Fits a fitted collider to <paramref name="mesh"/>; one sized by hand keeps its sizes.</summary>
        public void FollowMesh(Mesh mesh)
        {
            ArgumentNullException.ThrowIfNull(mesh);
            if (IsFitted)
            {
                FitTo(mesh);
                return;
            }

            BeginFit();
            try
            {
                TakeMeshShape(mesh);
            }
            finally
            {
                EndFit();
            }
        }

        /// <summary>Takes the collider's sizes and placement from <paramref name="mesh"/>, fitted or not.</summary>
        public void FitTo(Mesh mesh)
        {
            ArgumentNullException.ThrowIfNull(mesh);
            BeginFit();
            try
            {
                FitShape(mesh);
                isShaped = true;
            }
            finally
            {
                EndFit();
            }
        }

        /// <summary>
        /// Grows the collider to the box round it and <paramref name="collider"/> of this entity or of one under it, as it
        /// stands here; unscaled, as that box is.
        /// </summary>
        public virtual void Merge(Collider collider)
        {
            ArgumentNullException.ThrowIfNull(collider);
            Span<Vector3F> corners = stackalloc Vector3F[2 * OrientedBoundingBox<float>.CornerCount];
            var count = 0;
            if (isShaped)
            {
                Bounds.ToOrientedBoundingBox().As<float>().GetCorners(corners[..OrientedBoundingBox<float>.CornerCount]);
                count = OrientedBoundingBox<float>.CornerCount;
            }

            collider.Bounds.ToOrientedBoundingBox().As<float>().Transform(PlacementOf(collider.Owner))
                .GetCorners(corners.Slice(count, OrientedBoundingBox<float>.CornerCount));
            var merged = OrientedBoundingBox.CreateFromPoints<float>(corners[..(count + OrientedBoundingBox<float>.CornerCount)]);
            FitToBox(Bounds.FromBoundingBox(merged.As<double>()));
        }

        /// <summary>The collider's sizes and placement; null for one fitted to its mesh, unscaled. A fitted one keeps
        /// only what a fit leaves as it is.</summary>
        public ColliderShapeEdits GetShape()
        {
            var shape = new ColliderShapeEdits { IsFitted = IsFitted, Scale = Scale };
            WriteKept(shape);
            if (!IsFitted)
            {
                shape.Position = Position;
                shape.Rotation = Rotation;
                WriteSizes(shape);
            }

            return shape.HoldsDefaults() ? null : shape;
        }

        /// <summary>Gives the collider <paramref name="shape"/>'s sizes and placement, all at once - fitted to its mesh for
        /// null.</summary>
        public void SetShape(ColliderShapeEdits shape)
        {
            shape ??= new ColliderShapeEdits();
            BeginFit();
            try
            {
                // Made one sized by hand first, so what it is given is not fitted to the mesh on the way.
                if (!shape.IsFitted)
                {
                    IsFitted = false;
                }

                Scale = shape.Scale;
                TakeKept(shape);
                if (!shape.IsFitted)
                {
                    Position = shape.Position;
                    Rotation = shape.Rotation;
                    TakeSizes(shape);
                    isShaped = true;
                }

                // Made fitted, it fits at once; a fitted one fits again only for a kept value changed, as it does then.
                IsFitted = shape.IsFitted;
            }
            finally
            {
                EndFit();
            }
        }

        public abstract bool Intersects(ref Ray<float> ray, out Vector3F point);

        public abstract bool IntersectsForCamera(Camera camera, ref Ray<float> ray, out Vector3F point);

        /// <summary>
        /// The collider where its entity stands in the world, against <paramref name="origin"/> - a point near it - as a
        /// shape <see cref="Gjk"/> can test; null while it holds nothing.
        /// </summary>
        public abstract IConvexShape<float> GetWorldShape(WorldPosition origin);

        /// <summary>The space the collider holds, in cubic meters, its entity scaled by <paramref name="scale"/>, as the
        /// physics measures its shape; none for a mesh.</summary>
        public abstract double MeasureVolume(Vector3F scale);

        /// <summary>The space the collider holds on an entity skewed in its body, whose matrix stretches space by
        /// <paramref name="stretch"/>, as the physics carries it there: nothing for a round collider, which it leaves
        /// out.</summary>
        public virtual double MeasureCarriedVolume(double stretch) => 0;

        // Turned on or off, the body it belongs to weighs anew; one added or taken off its entity tells it so.
        protected override void OnEnabledChanged() => RigidBody.ReweighAbove(Owner);

        /// <summary>Whether this collider and <paramref name="other"/> share a point where their entities stand.</summary>
        public bool Intersects(Collider other)
        {
            // Both against this one's entity, so far from the origin the test is as near it.
            var origin = Owner.Transform.WorldPosition;
            var first = GetWorldShape(origin);
            var second = other.GetWorldShape(origin);
            return first != null && second != null && Gjk.Intersects(first, second);
        }

        /// <summary>Takes the sizes, <see cref="Position"/> and <see cref="Rotation"/> from <paramref name="mesh"/>; a box
        /// round it, unless the collider fits a mesh closer.</summary>
        protected virtual void FitShape(Mesh mesh) => FitBox(mesh.Bounds);

        /// <summary>Takes the sizes, <see cref="Position"/> and <see cref="Rotation"/> to hold <paramref name="box"/>.
        /// </summary>
        protected abstract void FitBox(Bounds box);

        /// <summary>Fits the collider, unscaled, to hold <paramref name="box"/>.</summary>
        protected void FitToBox(Bounds box)
        {
            BeginFit();
            try
            {
                Scale = Vector3F.One;
                FitBox(box);
                isShaped = true;
            }
            finally
            {
                EndFit();
            }
        }

        /// <summary>Takes what a collider sized by hand still takes from a new mesh: nothing, unless its shape is the
        /// mesh's own.</summary>
        protected virtual void TakeMeshShape(Mesh mesh)
        {
        }

        /// <summary>Writes the collider's own sizes to <paramref name="shape"/>.</summary>
        protected abstract void WriteSizes(ColliderShapeEdits shape);

        /// <summary>Takes the collider's own sizes from <paramref name="shape"/>.</summary>
        protected abstract void TakeSizes(ColliderShapeEdits shape);

        /// <summary>Writes what a fit leaves as it is, besides the scale: nothing, unless the collider keeps more.</summary>
        protected virtual void WriteKept(ColliderShapeEdits shape)
        {
        }

        /// <summary>Takes what a fit leaves as it is, besides the scale.</summary>
        protected virtual void TakeKept(ColliderShapeEdits shape)
        {
        }

        /// <summary>Places the collider's shape in its entity's space by its placement, keeps it for picking and the view,
        /// and gives the box round it.</summary>
        protected abstract Bounds PlaceShape();

        /// <summary>Tells that the shape changed though the box round it did not: the outline is built anew.</summary>
        protected void MarkOutlineStale() => outlineIsStale = true;

        /// <summary>The placement without the scale: turned, then moved - for a shape that takes the scale itself.
        /// </summary>
        protected Matrix4x4F Standing =>
            Matrix4x4.CreateFromQuaternion<float>(Turn) * Matrix4x4.CreateTranslation<float>(Position);

        /// <summary>A new outline mesh of what the collider holds now, in its entity's space.</summary>
        protected abstract Mesh BuildOutline();

        /// <summary>Called when a size, <see cref="Position"/> or <see cref="Rotation"/> changes: set by hand, the
        /// collider stops fitting its mesh. Its shape is built anew.</summary>
        protected void Resize()
        {
            if (fitDepth > 0)
            {
                return;
            }

            isShaped = true;
            IsFitted = false;
            Reshape();
        }

        /// <summary>Builds the collider's shape anew from its sizes and placement; while it is being fitted, once the fit
        /// is done.</summary>
        protected void Reshape()
        {
            if (fitDepth > 0)
            {
                return;
            }

            // A shape that changed marks its outline - the box round it here, any other shape in PlaceShape.
            var staleBefore = outlineIsStale;
            outlineIsStale = false;
            var placed = PlaceShape();
            if (placed != Bounds)
            {
                Bounds = placed;
                outlineIsStale = true;
            }

            var changed = outlineIsStale;
            outlineIsStale |= staleBefore;

            // Where each camera sees a changed shape is placed anew with the entity's world, and its body weighs anew.
            if (changed && Owner is { } owner)
            {
                owner.Transform.IsWorldDirty = true;
                RigidBody.ReweighAbove(owner);
            }
        }

        protected Matrix4x4F PlacementOf(Entity part)
        {
            var placement = Matrix4x4F.Identity;
            for (var at = part; at != Owner; at = at.Owner)
            {
                if (at == null)
                {
                    throw new ArgumentException("Only a collider of this entity or of one under it can be merged in.");
                }

                placement *= at.Transform.GetLocalMatrixF();
            }

            return placement;
        }

        /// <summary><paramref name="box"/>, along its entity's axes, as <see cref="Bounds"/>.</summary>
        protected static Bounds BoundsOf(BoundingBox<float> box) =>
            new(((box.Minimum + box.Maximum) * 0.5f).As<double>(), ((box.Maximum - box.Minimum) * 0.5f).As<double>(),
                Quaternion<double>.Identity);

        /// <summary>The turn that holds <paramref name="box"/>'s axes; none for a box with no turn.</summary>
        protected static QuaternionF TurnOf(Bounds box)
        {
            var turn = box.Orientation.As<float>();
            return turn.LengthSquared() > 0 ? QuaternionF.Normalize(turn) : QuaternionF.Identity;
        }

        /// <summary>Starts setting values that do not make the collider one sized by hand - a fit, or its defaults. Pairs
        /// with <see cref="EndFit"/>, and may be nested.</summary>
        protected void BeginFit() => fitDepth++;

        /// <summary>Ends what <see cref="BeginFit"/> started; the outermost builds the shape once.</summary>
        protected void EndFit()
        {
            fitDepth--;
            Reshape();
        }
    }
}

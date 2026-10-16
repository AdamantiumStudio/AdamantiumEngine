using System;
using Adamantium.ECS.Components;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.MVVM;

namespace Adamantium.ECS.Components
{
    [ViewModel]
    public partial class Light: ActivatableComponent
    {
        [Bindable] private Vector3F _color;
        [Bindable] private float _intensity;
        [Bindable] private LightType _type;
        [Bindable] private float _range;
        [Bindable] private bool _isWithShadows;
        [Bindable] private float _outerSpotAngle;
        [Bindable] private float _innerSpotAngle;
        [Bindable] private float _innerAngleDelta;
        [Bindable] private float _outerAngleDelta;

        public Light() : this(LightType.Point)
        {
        }

        public Light(LightType lightType)
        {
            Type = lightType;
            Range = 1f;
            OuterSpotAngle = (float)Math.Atan(Range);
            Intensity = 1.0f;
            Color = Colors.White.ToVector3();
        }

        partial void OnTypeChanged(LightType value) => ShowKindOnOwner();

        protected override void OnOwnerChanged(Entity oldOwner, Entity newOwner)
        {
            base.OnOwnerChanged(oldOwner, newOwner);
            ShowKindOnOwner();
        }

        // The entity a light is on is shown as that kind of light, whatever made it and whenever the kind changes.
        private void ShowKindOnOwner()
        {
            if (Owner == null) return;

            Owner.IconKey = Type switch
            {
                LightType.Point => EntityIconKeys.PointLight,
                LightType.Spot => EntityIconKeys.SpotLight,
                _ => EntityIconKeys.DirectionalLight
            };
        }

        public Int32 ShadowMapResolution { get; set; }

        public Single DepthBias => 1.0f / (20 * Range);

        /// <summary>Where the light shines: its entity's own -Y axis, turned as the entity stands in the world.</summary>
        public Vector3F Direction
        {
            get
            {
                if (Owner == null)
                {
                    return Vector3F.Down;
                }

                return Vector3F.Normalize(Vector3F.TransformNormal(Vector3F.Down, Owner.Transform.GetWorldMatrixF()));
            }
        }

        public float SpotRadius => Range * (float)Math.Tan(OuterSpotAngle);
    }
}

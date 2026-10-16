using System;
using Adamantium.ECS.Components;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.ECS.Components
{
    public class Light: ActivatableComponent
    {
        private Vector3F _color;
        private float _intensity;
        private LightType _type;
        private float _range;
        private bool _isWithShadows;
        private float _outerSpotAngle;
        private float _innerSpotAngle;
        private float _innerAngleDelta;
        private float _outerAngleDelta;

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

        public Vector3F Color
        {
            get => _color;
            set => SetProperty(ref _color, value);
        }

        public Single Intensity
        {
            get => _intensity;
            set => SetProperty(ref _intensity, value);
        }

        public LightType Type
        {
            get => _type;
            set
            {
                if (SetProperty(ref _type, value)) ShowKindOnOwner();
            }
        }

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

        public Single Range
        {
            get => _range;
            set => SetProperty(ref _range, value);
        }

        public Boolean IsWithShadows
        {
            get => _isWithShadows;
            set => SetProperty(ref _isWithShadows, value);
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

        public float OuterSpotAngle
        {
            get => _outerSpotAngle;
            set => SetProperty(ref _outerSpotAngle, value);
        }

        public float SpotRadius => Range * (float)Math.Tan(OuterSpotAngle);

        public float InnerAngleDelta
        {
            get => _innerAngleDelta;
            set => SetProperty(ref _innerAngleDelta, value);
        }

        public float OuterAngleDelta
        {
            get => _outerAngleDelta;
            set => SetProperty(ref _outerAngleDelta, value);
        }

        public float InnerSpotAngle
        {
            get => _innerSpotAngle;
            set => SetProperty(ref _innerSpotAngle, value);
        }
    }
}

using Adamantium.MVVM;

namespace Adamantium.ECS.Components
{
    [ViewModel]
    public abstract partial class ActivatableComponent : Component
    {
        protected ActivatableComponent()
        {
            IsEnabled = true;
        }

        [Bindable]
        public partial bool IsEnabled { get; set; }

        partial void OnIsEnabledChanged(bool value) => OnEnabledChanged();

        /// <summary>Called when the component is turned on or off.</summary>
        protected virtual void OnEnabledChanged()
        {
        }
    }
}

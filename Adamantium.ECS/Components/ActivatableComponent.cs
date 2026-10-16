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
    }
}

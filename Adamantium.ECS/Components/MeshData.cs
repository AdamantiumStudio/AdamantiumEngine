using System;
using Adamantium.ECS.Components;
using Adamantium.Graphics.Core.Models;
using Adamantium.MVVM;
using Adamantium.Vulkan.Core;

namespace Adamantium.ECS.Components
{
    // [ViewModel]: this class already has a base (Component) that supplies SetProperty, so the MVVM generator turns the
    // [Bindable] fields below into INPC properties without a base-class change - no hand-written backing-field plumbing.
    [ViewModel]
    public sealed partial class MeshData : ActivatableComponent
    {
        [Bindable] private Mesh mesh;
        [Bindable] private MeshRenderMode renderMode;
        [Bindable] private bool isWireFrame;
        [Bindable] private CullModeFlagBits cullMode = CullModeFlagBits.None;
        [Bindable] private bool depthTestEnabled = true;
        [Bindable] private bool depthWriteEnabled = true;
        [Bindable] private PrimitiveTopology? topologyOverride;

        // Generator hooks: keep the existing side-effect events (fired after the property actually changed).
        partial void OnMeshChanged(Mesh value) => MeshDataChanged?.Invoke(this, EventArgs.Empty);

        public event EventHandler MeshDataChanged;

        public override IComponent Clone()
        {
            var meshComponent = new MeshData
            {
                Mesh = Mesh.Clone(),
                RenderMode = RenderMode,
                IsWireFrame = IsWireFrame,
                CullMode = CullMode,
                DepthTestEnabled = DepthTestEnabled,
                DepthWriteEnabled = DepthWriteEnabled,
                TopologyOverride = TopologyOverride
            };
            return meshComponent;
        }
    }
}

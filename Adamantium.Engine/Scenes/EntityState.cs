using Adamantium.ECS;
using Adamantium.ECS.Components;

namespace Adamantium.Engine.Scenes;

internal readonly record struct EntityState(
    string Name, TransformState Transform, bool IsEnabled, bool Visible, Entity Owner, int Index)
{
    public static EntityState Of(Entity entity, Entity owner, int index) =>
        new(entity.Name, entity.Transform.State, entity.IsEnabled, entity.Visible, owner, index);

    public bool ApplyTo(Entity entity, EntityManager manager, bool hangBack)
    {
        var moved = false;
        if (hangBack)
        {
            moved = entity.Owner != Owner || manager.GetSiblingIndex(entity) != Index;
            if (entity.Owner != Owner)
                manager.SetOwner(entity, Owner);
        }

        if (entity.Transform.State != Transform)
            entity.Transform.State = Transform;

        entity.Name = Name;
        entity.IsEnabled = IsEnabled;
        entity.Visible = Visible;
        return moved;
    }
}

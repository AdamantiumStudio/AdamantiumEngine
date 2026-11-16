using System;

namespace Adamantium.ECS
{
    public class EntityServiceEventArgs : EventArgs
    {
        public IEntityService Service { get; }

        public EntityServiceEventArgs(IEntityService system)
        {
            Service = system;
        }
    }
}
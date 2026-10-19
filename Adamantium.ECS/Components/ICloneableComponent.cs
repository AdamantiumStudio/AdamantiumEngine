using System.Collections.Generic;

namespace Adamantium.ECS.Components
{
    public interface ICloneableComponent
    {
        IComponent Clone();

        void CloneValues(IComponent clone);

        /// <summary>
        /// Gives <paramref name="clone"/> the entities this component refers to, once the whole tree it was copied with
        /// is copied: each one copied with it as its copy in <paramref name="copies"/>, any other as it is.
        /// </summary>
        void CloneReferences(IComponent clone, IReadOnlyDictionary<Entity, Entity> copies);
    }
}
using System;
using System.Collections.Generic;

namespace Adamantium.ECS;

public interface IEntityService : IUpdateService, IRenderService
{
    UInt128 Uid { get; }

    /// <summary>
    /// Place of this service in the frame, lower first. States a dependency on other services, which is why it is
    /// one value for the whole cycle rather than one per phase.
    /// </summary>
    int Priority { get; set; }
    
    EntityWorld EntityWorld { get; }
    
    bool IsUpdateService { get; }
    
    bool IsRenderingService { get; }

    /// <summary>Whether the service takes part in the frame: an inactive one is not updated, drawn or presented - a
    /// sleeping one costs nothing - and is only told the frame ended.</summary>
    bool IsActive { get; set; }

    EntityServiceType ServiceType { get; }

    void Initialize();
    
    IReadOnlyList<IEntityProcessor> Processors { get; }

    void AttachProcessor(IEntityProcessor processor);

    void DetachProcessor(IEntityProcessor processor);
}
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

    /// <summary>Whether the service is put to sleep when its <see cref="IUpdateService.Update"/> throws, the others
    /// updated on. One that is not fails the frame, which the universe runs again.</summary>
    bool SleepsWhenUpdateFails { get; }

    EntityServiceType ServiceType { get; }

    void Initialize();

    /// <summary>Called on every service, active or not, once the frame is presented.</summary>
    void FrameEnded();

    /// <summary>At a breakpoint or a step, before the world is drawn there: brings what the drawing reads up to date with
    /// what the game changed so far - no time goes by, nothing steps. Called on another thread, the game's own held; of
    /// the engine's services only, never a game's.</summary>
    void UpdateAtBreakpoint();

    IReadOnlyList<IEntityProcessor> Processors { get; }

    void AttachProcessor(IEntityProcessor processor);

    void DetachProcessor(IEntityProcessor processor);
}
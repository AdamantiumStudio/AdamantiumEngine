using System.Collections.Generic;
using Adamantium.Core;
using Adamantium.ECS;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>Keeps the time of every frame it is updated in.</summary>
internal sealed class TimeRecordingService : EntityService
{
    public TimeRecordingService(EntityWorld world) : base(world)
    {
    }

    public List<AppTime> Frames { get; } = [];

    public override bool IsUpdateService => true;

    public override bool IsRenderingService => false;

    public override EntityServiceType ServiceType => EntityServiceType.Update;

    public override void Update(AppTime appTime) => Frames.Add(appTime);
}

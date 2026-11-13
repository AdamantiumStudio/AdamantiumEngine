using System.Threading;
using System.Threading.Tasks;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Image = Adamantium.Imaging.Image;

namespace Adamantium.Engine.Screenshots;

/// <summary>Takes screenshots, one a frame, in the universe it is a service of: of entities that belong to no world,
/// and of whole scenes of its world.</summary>
public interface IScreenshotRenderingService : IEntityService
{
    /// <summary>The light screenshots are taken in; set from any thread.</summary>
    ScreenshotLighting Lighting { get; set; }

    /// <summary>Puts <paramref name="entity"/> - one no world holds - in the queue; completes with its picture, or with
    /// null when it has nothing to draw. A screenshot cancelled through <paramref name="cancellation"/> before it is
    /// taken is left out, its entity let go. From any thread.</summary>
    Task<Image> Enqueue(Entity entity, CancellationToken cancellation = default);

    /// <summary>Puts the picture of all of <paramref name="scene"/> - a scene of the service's world - in the queue, as
    /// <paramref name="viewpoint"/> sees it, in the light screenshots are taken in; completes with it. Cancelled as the
    /// screenshot of an entity is. From any thread.</summary>
    Task<Image> Enqueue(Scene scene, Camera viewpoint, CancellationToken cancellation = default);

    /// <summary>Takes the service out of its world at the end of a frame, the screenshots still asked for cancelled, its
    /// output and what it drew with let go.</summary>
    void Remove();
}

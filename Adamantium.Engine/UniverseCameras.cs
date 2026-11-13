using System.Collections.Generic;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Multiverse;

namespace Adamantium.Engine;

public static class UniverseCameras
{
    /// <summary>
    /// Fills <paramref name="cameras"/> with the camera each visible output looks through now - once, however many
    /// outputs share it. A viewpoint nobody sees is not in it.
    /// </summary>
    public static void CollectCurrentCameras(this IUniverse universe, List<Camera> cameras)
    {
        cameras.Clear();
        var outputs = universe.Outputs;
        for (int i = 0; i < outputs.Count; i++)
        {
            if (!outputs[i].IsVisible)
            {
                continue;
            }

            var camera = outputs[i].Camera;
            if (camera != null && !cameras.Contains(camera))
            {
                cameras.Add(camera);
            }
        }
    }

    /// <summary>
    /// Fills <paramref name="scenes"/> with the scene each visible output looking through a camera shows now - once,
    /// however many outputs show it. A scene nobody sees is not in it: nothing runs it.
    /// </summary>
    public static void CollectShownScenes(this IUniverse universe, List<Scene> scenes)
    {
        scenes.Clear();
        var outputs = universe.Outputs;
        for (int i = 0; i < outputs.Count; i++)
        {
            var output = outputs[i];
            if (output.IsVisible && output.Camera != null && output.Scene is { IsRemoved: false } scene
                && !scenes.Contains(scene))
            {
                scenes.Add(scene);
            }
        }
    }
}

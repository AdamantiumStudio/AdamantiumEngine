using System.Collections.Generic;
using System.Linq;
using Adamantium.Graphics.Core.Models;

namespace Adamantium.Engine.CompilerTests;

/// <summary>Walks the model tree of an imported scene.</summary>
internal static class TestScenes
{
    /// <summary>Every model of the scene's tree, each before its children.</summary>
    public static IEnumerable<SceneData.Model> GetAllModels(SceneData scene)
    {
        var pending = new Stack<SceneData.Model>();
        pending.Push(scene.Models);
        while (pending.Count > 0)
        {
            var model = pending.Pop();
            yield return model;
            foreach (var child in model.Dependencies)
            {
                pending.Push(child);
            }
        }
    }

    /// <summary>Every mesh of the scene's tree.</summary>
    public static IEnumerable<Mesh> GetAllMeshes(SceneData scene) => GetAllModels(scene).SelectMany(model => model.Meshes);
}

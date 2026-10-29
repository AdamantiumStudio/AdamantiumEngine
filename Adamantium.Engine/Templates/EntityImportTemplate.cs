using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.ECS.Templates;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Content;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Serilog;

namespace Adamantium.Engine.Templates;

public class EntityImportTemplate : IEntityTemplate
{
    private SceneData sceneData;
    private IContentManager contentManager;

    private Vector3<double> initialPosition;
    //private EntityFramework.Components.Camera camera;

    public EntityImportTemplate(SceneData sceneData, IContentManager manager, Camera camera)
    {
        this.sceneData = sceneData;
        contentManager = manager;
        //this.camera = camera;
    }
        
    /// <param name="initialPosition">Where the model's own origin is placed, as the file has it: the root's position is
    /// its position in the file plus this, the model is not moved to put its center there, and it scales and turns about
    /// that origin. Each part below it turns about its own center.</param>
    public EntityImportTemplate(SceneData sceneData, IContentManager manager, Vector3<double> initialPosition)
    {
        this.sceneData = sceneData;
        contentManager = manager;
        this.initialPosition = initialPosition;
        //this.camera = camera;
    }

    public Task<Entity> BuildEntity(Entity owner)
    {
        return ImportEntity(owner);
    }

    private Task<Entity> ImportEntity(Entity owner)
    {
        try
        {
            if (sceneData == null)
            {
                return null;
            }

            var entityOf = new Dictionary<SceneData.Model, Entity>(ReferenceEqualityComparer.Instance);
            var nodeEntities = new Dictionary<string, Entity>();
            var restPoses = new Dictionary<string, SceneData.KeyFrame>();
            var drawnOf = new Dictionary<Entity, List<Entity>>();
            var skinned = new List<(Entity Node, SceneData.Controller Skin)>();
            owner.Name = sceneData.Name;
            owner.Source = sceneData.Source;
            owner.IconKey = EntityIconKeys.Model;
            entityOf.Add(sceneData.Models, owner);
            var stack = new Stack<SceneData.Model>();
            stack.Push(sceneData.Models);
            while (stack.Count > 0)
            {
                Entity entity = null;
                var currentMesh = stack.Pop();
                if (currentMesh.Parent == null)
                {
                    entity = owner;
                }
                else
                {
                    if (entityOf.TryGetValue(currentMesh.Parent, out var parentEntity))
                    {
                        entity = new Entity(parentEntity, currentMesh.Name);
                        entityOf.Add(currentMesh, entity);
                    }
                }

                if (entity != null && !string.IsNullOrEmpty(currentMesh.ID))
                {
                    nodeEntities[currentMesh.ID] = entity;
                }

                var drawn = new List<Entity>();
                if (currentMesh.Meshes.Count == 1)
                {
                    var mesh = currentMesh.Meshes[0];
                    WriteMeshData(entity, mesh);
                    drawn.Add(entity);
                }
                //Если меш разбит на части, тогда создаём дополнительные энтити, которые будут дочерними по отношению к текущему
                else
                {
                    var count = 1;
                    foreach (var mesh in currentMesh.Meshes)
                    {
                        var entityPart = new Entity(entity, $"{currentMesh.Name} ({count})");
                        count++;
                        WriteMeshData(entityPart, mesh);
                        drawn.Add(entityPart);
                    }
                }

                if (entity != null)
                {
                    drawnOf[entity] = drawn;
                }

                var scale = currentMesh.Parent == null
                    ? currentMesh.Scale * new Vector3F(sceneData.Units.Value)
                    : currentMesh.Scale;
                SetTransformation(entity, currentMesh.Position.As<double>(), scale, currentMesh.Rotation);
                if (!string.IsNullOrEmpty(currentMesh.ID))
                {
                    restPoses[currentMesh.ID] = new SceneData.KeyFrame(scale, currentMesh.Position, currentMesh.Rotation);
                }

                if (sceneData.Controllers.TryGetValue(currentMesh.ID, out var nodeSkin))
                {
                    skinned.Add((entity, nodeSkin));
                }

                foreach (var newEntity in currentMesh.Dependencies)
                {
                    stack.Push(newEntity);
                }
            }

            foreach (var (node, skinData) in skinned)
            {
                if (node == null || !drawnOf.TryGetValue(node, out var drawnEntities)) continue;

                var skeleton = new Skeleton();
                foreach (var joint in skinData.JointNames)
                {
                    skeleton.Joints.Add(nodeEntities.GetValueOrDefault(joint));
                }

                skeleton.InverseBindMatrices.AddRange(skinData.JointMatrices);
                foreach (var drawnEntity in drawnEntities)
                {
                    drawnEntity.AddComponent(new Skin { Skeleton = skeleton });
                }
            }

            if (sceneData.Clips is { Count: > 0 })
            {
                var animation = new AnimationComponent();
                foreach (var (name, clip) in sceneData.Clips)
                {
                    animation.Clips[name] = clip;
                    foreach (var key in clip.Keys)
                    {
                        if (nodeEntities.TryGetValue(key, out var target))
                        {
                            animation.Targets[key] = target;
                            animation.RestPoses[key] = restPoses[key];
                        }
                    }
                }

                animation.CurrentClip = sceneData.Clips.Keys.First();
                owner.AddComponent(animation);
            }

            CalculateBoundBoxes(owner);

            owner.TraverseInDepth(entity =>
            {
                if (ReferenceEquals(entity, owner))
                {
                    return;
                }

                var transform = entity.Transform;
                transform.Pivot = Vector3F.TransformCoordinate(entity.GetLocalCenter(), transform.GetLocalMatrixF()).As<double>();
            });
            owner.Transform.Position += initialPosition;
            return Task.FromResult(owner);
        }
        catch (Exception exception)
        {
            Log.Logger.Error(exception, "Building an entity from imported model data failed");
        }
        return null;
    }


    private void CalculateBoundBoxes(Entity root)
    {
        var roots = new List<Entity>();
        var queue = new Queue<Entity>();
        queue.Enqueue(root);
        if (root.Dependencies.Count > 0)
        {
            roots.Add(root);
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var entity in current.Dependencies)
            {
                if (entity.Dependencies.Count > 0)
                {
                    queue.Enqueue(entity);
                    roots.Add(entity);
                }
            }
        }

        for (var i = roots.Count - 1; i >= 0; --i)
        {
            BoxCollider rootCollider = null;
            foreach (var entity in roots[i].Dependencies)
            {
                var collider = entity.GetComponent<Collider>();
                if (collider == null)
                {
                    continue;
                }

                rootCollider ??= roots[i].GetOrCreateComponent<BoxCollider>();
                rootCollider.Merge(collider);
            }
        }
    }

    private static void SetTransformation(Entity entity, Vector3<double> pos, Vector3F scale, QuaternionF rot)
    {
        entity.Transform.Position = pos;
        entity.Transform.Rotation = rot;
        entity.Transform.BaseScale = scale;
    }

    private void WriteMeshData(Entity entity, Mesh mesh)
    {
        // The model's root keeps its own icon when it carries a mesh as well.
        if (entity.IconKey == EntityIconKeys.Entity) entity.IconKey = EntityIconKeys.Mesh;

        var meshData = new MeshData();
        meshData.Mesh = mesh;
        // Import default: a mesh that carries joint data gets the animated path, others the plain path. This is now an
        // EXPLICIT, overridable choice on MeshData (not re-derived from the semantic at draw time), so the same mesh can
        // later be rendered either statically or skinned.
        meshData.RenderMode = mesh.Semantic.HasFlag(VertexSemantic.JointIndices)
            ? MeshRenderMode.Skinned
            : MeshRenderMode.Static;
        entity.AddComponent(meshData);

        Collider collider = new BoxCollider();
        entity.AddComponent(collider);
        collider.Initialize();

        SceneData.Material materialData;
        if (!String.IsNullOrEmpty(mesh.MaterialID))
        {
            sceneData.Materials.TryGetValue(mesh.MaterialID, out materialData);
            if (materialData != null)
            {
                var material = new Material();
                material.AmbientColor = materialData.AmbientColor;
                if (materialData.DiffuseColor != default)
                {
                    material.DiffuseColor = materialData.DiffuseColor;
                }
                material.SpecularColor = materialData.SpecularColor;
                material.Emission = materialData.Emission;
                material.Reflective = materialData.Reflective;
                material.Reflectivity = materialData.Reflectivity;
                material.RefractionIndex = materialData.RefractionIndex;
                material.Shininess = materialData.Shininess;
                material.Transparent = materialData.Transparent;
                material.Transparency = materialData.Transparency;
                material.MetallicFactor = materialData.MetallicFactor;
                material.RoughnessFactor = materialData.RoughnessFactor;
                material.MetallicRoughnessPath = PathOf(materialData.MetallicRoughnessMap);
                material.NormalMapPath = PathOf(materialData.NormalMap);
                material.NormalScale = materialData.NormalScale;
                material.OcclusionMapPath = PathOf(materialData.OcclusionMap);
                material.OcclusionStrength = materialData.OcclusionStrength;
                material.EmissiveMapPath = PathOf(materialData.EmissiveMap);
                material.AlphaMode = materialData.AlphaMode;
                material.AlphaCutoff = materialData.AlphaCutoff;
                material.DoubleSided = materialData.DoubleSided;

                if (!String.IsNullOrEmpty(materialData.DiffuseMap)
                    && sceneData.Images.TryGetValue(materialData.DiffuseMap, out var image))
                {
                    material.TexturePath = PathOf(materialData.DiffuseMap);
                    material.Texture = LoadTexture(image, material.TexturePath);
                }
                entity.AddComponent(material);
            }
            else
            {
                var material = new Material();
                entity.AddComponent(material);
            }
        }
        else
        {
            var material = new Material();
            entity.AddComponent(material);
        }
    }

    private string PathOf(string imageKey)
    {
        if (string.IsNullOrEmpty(imageKey) || !sceneData.Images.TryGetValue(imageKey, out var image)) return null;

        if (image.FilePath != null || image.ImageName != null) return image.FilePath ?? image.ImageName;

        // Named by what it holds too, so a model saved again with another picture does not get the old one's texture.
        var content = image.Data is { Length: > 0 } data ? ":" + Convert.ToHexString(SHA256.HashData(data), 0, 6) : "";
        return $"{sceneData.Source ?? sceneData.Name}#{image.ID ?? imageKey}{content}";
    }

    // An image carried inside the model is embedded under its path, so the content manager owns its texture as it owns
    // one read from a file, and loads it again for a new device. A picture that cannot be read leaves the part untextured.
    private Texture LoadTexture(SceneData.Image image, string path)
    {
        if (contentManager == null) return null;

        try
        {
            var options = new ContentLoadOptions { AllowDuplication = false, IgnoreRootDirectory = true };
            if (File.Exists(image.FilePath))
            {
                return contentManager.Load<Texture>(image.FilePath, options);
            }

            if (image.Data is not { Length: > 0 } data) return null;

            contentManager.Embed(path, data);
            return contentManager.Load<Texture>(path, options);
        }
        catch (Exception exception)
        {
            Log.Logger.Warning(exception, "The picture {Path} of an imported model could not be loaded", path);
            return null;
        }
    }
}
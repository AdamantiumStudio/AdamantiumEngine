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
using Adamantium.Graphics.Core.Assets;
using Adamantium.Graphics.Core.Models;
using Adamantium.Engine.Physics;
using Adamantium.Mathematics;
using Serilog;

namespace Adamantium.Engine.Templates;

public class EntityImportTemplate : IEntityTemplate
{
    private SceneData sceneData;
    private IAssetService assets;

    private Vector3<double> initialPosition;
    //private EntityFramework.Components.Camera camera;

    public EntityImportTemplate(SceneData sceneData, IAssetService assetService, Camera camera)
    {
        this.sceneData = sceneData;
        assets = assetService;
        //this.camera = camera;
    }
        
    /// <param name="initialPosition">Where the model's own origin is placed, as the file has it: the root's position is
    /// its position in the file plus this, the model is not moved to put its center there, and it scales and turns about
    /// that origin. Each part below it turns about its own center.</param>
    public EntityImportTemplate(SceneData sceneData, IAssetService assetService, Vector3<double> initialPosition)
    {
        this.sceneData = sceneData;
        assets = assetService;
        this.initialPosition = initialPosition;
        //this.camera = camera;
    }

    public Task<Entity> BuildEntity(Entity owner) => Task.FromResult(Build(owner));

    /// <summary>Makes <paramref name="owner"/> the model, its parts under it, at once: nothing of it waits. Null when
    /// there is no model data, or it could not be made - logged.</summary>
    public Entity Build(Entity owner)
    {
        try
        {
            if (sceneData == null)
            {
                return null;
            }

            var entityOf = new Dictionary<SceneData.Model, Entity>(ReferenceEqualityComparer.Instance);
            // Each part's path in the model: its id is made from it and the root's, the same on every load.
            var parts = new PartPaths(owner);
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
                        entity = parts.Make(parentEntity, currentMesh.Name);
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
                        var entityPart = parts.Make(entity, $"{currentMesh.Name} ({count})");
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

                // Last pushed first: the parts are made in the file's order, so one added after its namesakes by a new
                // export takes the next count, and theirs - and their ids - stay.
                for (var i = currentMesh.Dependencies.Count - 1; i >= 0; i--)
                {
                    stack.Push(currentMesh.Dependencies[i]);
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

            ApplyEdits(owner);
            var bounds = new Dictionary<Entity, Bounds>();
            owner.MeasureBoundsBelow(bounds);
            owner.TraverseInDepth(entity =>
            {
                if (ReferenceEquals(entity, owner))
                {
                    return;
                }

                var center = bounds.TryGetValue(entity, out var measured) ? measured.Center.As<float>() : Vector3F.Zero;
                var transform = entity.Transform;
                transform.Pivot = Vector3F.TransformCoordinate(center, transform.GetLocalMatrixF()).As<double>();
            });
            owner.Transform.Position += initialPosition;
            return owner;
        }
        catch (Exception exception)
        {
            Log.Logger.Error(exception, "Building an entity from imported model data failed");
        }
        return null;
    }

    // The parts of a model as they are made, each with its path - its id made from the root's and that path - and how many
    // of each name each part holds already.
    private sealed class PartPaths(Entity model)
    {
        private readonly Dictionary<Entity, string> pathOf = new() { [model] = string.Empty };
        private readonly Dictionary<Entity, Dictionary<string, int>> namesUnder = new();

        public Entity Make(Entity under, string name)
        {
            // A node whose parent was not made stands alone, as before.
            if (under == null || !pathOf.TryGetValue(under, out var underPath))
            {
                return new Entity(under, name);
            }

            if (!namesUnder.TryGetValue(under, out var names))
            {
                names = new Dictionary<string, int>(StringComparer.Ordinal);
                namesUnder[under] = names;
            }

            var key = name ?? string.Empty;
            var count = names.GetValueOrDefault(key);
            names[key] = count + 1;
            var path = ModelPartPath.Child(underPath, key, count);
            var part = new Entity(under, name, ModelPartPath.UidOf(model.Uid, path));
            pathOf[part] = path;
            return part;
        }
    }

    // A part's joint, held to the part of the same model its path names, or to the world; one whose other part a new
    // import took away waits in the file.
    private static void ApplyJoint(Entity model, Entity part, string path, JointEdits joint)
    {
        Entity connected = null;
        if (joint.ConnectedPart != null && (connected = ModelPartPath.Find(model, joint.ConnectedPart)) == null)
        {
            Log.Logger.Warning("{Model} has no part {Other} any more; the joint of {Part} waits in the file", model.Name,
                joint.ConnectedPart, path);
            return;
        }

        if (!PhysicsParts.SetJoint(part, joint, connected))
        {
            Log.Logger.Warning("The joint {Model} keeps for {Part} is left out", model.Name, path);
        }
    }

    // The edits made in the editor, each on the part its path names; an edit whose part a new import took away waits in
    // the file.
    private void ApplyEdits(Entity model)
    {
        if (sceneData.Edits?.Parts is not { Count: > 0 } parts)
        {
            return;
        }

        foreach (var (path, edits) in parts)
        {
            if (edits == null)
            {
                continue;
            }

            var part = ModelPartPath.Find(model, path);
            if (part == null)
            {
                Log.Logger.Warning("{Model} has no part {Part} any more; its edits wait in the file", model.Name, path);
                continue;
            }

            if (edits.Collider is { } kind)
            {
                PhysicsParts.SetCollider(part, kind);
            }

            // Only on a collider of the kind the edits keep - the import's box for none: one kept for a mesh it could
            // not be fitted to has sizes of its own.
            if (edits.ColliderShape is { } shape && part.GetComponent<Collider>() is { } collider
                && PhysicsParts.KindOf(collider) == (edits.Collider ?? ColliderKind.Box))
            {
                PhysicsParts.SetShape(collider, shape);
            }

            if (edits.Body is { } body && !PhysicsParts.SetBody(part, body))
            {
                Log.Logger.Warning("The body {Model} keeps for {Part} is left out", model.Name, path);
            }

            foreach (var joint in edits.Joints ?? [])
            {
                if (joint != null)
                {
                    ApplyJoint(model, part, path, joint);
                }
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

        entity.AddComponent(new BoxCollider());

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
                material.Faces = materialData.Faces;

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

    // An image carried inside the model is embedded under its path, so the asset service owns its texture as it owns
    // one read from a file, and loads it again for a new device. A picture that cannot be read leaves the part untextured.
    private Texture LoadTexture(SceneData.Image image, string path)
    {
        if (assets == null)
        {
            return null;
        }

        try
        {
            var options = new AssetLoadOptions { AllowDuplication = false, IgnoreRootDirectory = true };
            if (File.Exists(image.FilePath))
            {
                return assets.Load<Texture>(image.FilePath, options);
            }

            if (image.Data is not { Length: > 0 } data) return null;

            assets.Embed(path, data);
            return assets.Load<Texture>(path, options);
        }
        catch (Exception exception)
        {
            Log.Logger.Warning(exception, "The picture {Path} of an imported model could not be loaded", path);
            return null;
        }
    }
}
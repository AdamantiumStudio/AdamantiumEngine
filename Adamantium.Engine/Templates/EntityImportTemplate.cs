using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.ECS.Templates;
using Adamantium.Graphics;
using Adamantium.Graphics.Core.Content;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Serilog;

namespace Adamantium.Engine.Templates;

public class EntityImportTemplate : IEntityTemplate
{
    private SceneData sceneData;
    private IContentManager contentManager;

    private Vector3 initialPosition;
    //private EntityFramework.Components.Camera camera;

    public EntityImportTemplate(SceneData sceneData, IContentManager manager, Camera camera)
    {
        this.sceneData = sceneData;
        contentManager = manager;
        //this.camera = camera;
    }
        
    /// <param name="initialPosition">Where the model's center is placed; the model turns and scales about it.</param>
    public EntityImportTemplate(SceneData sceneData, IContentManager manager, Vector3 initialPosition)
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

            var nameToEntityId = new Dictionary<string, Entity>();
            owner.Name = sceneData.Name;
            owner.IconKey = EntityIconKeys.Model;
            nameToEntityId.Add(sceneData.Models.ToString(), owner);
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
                    if (nameToEntityId.TryGetValue(currentMesh.Parent.ToString(), out var parentEntity))
                    {
                        entity = new Entity(parentEntity, currentMesh.ToString());
                        nameToEntityId.Add(currentMesh.ToString(), entity);
                    }
                }

                if (currentMesh.Meshes.Count == 1)
                {
                    var mesh = currentMesh.Meshes[0];
                    WriteMeshData(entity, mesh);
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
                    }
                }

                var scale = currentMesh.Parent == null
                    ? currentMesh.Scale * new Vector3F(sceneData.Units.Value)
                    : currentMesh.Scale;
                SetTransformation(entity, (Vector3)currentMesh.Position, scale, currentMesh.Rotation);

                if (sceneData.Controllers.ContainsKey(currentMesh.ID))
                {
                    var controllerData = sceneData.Controllers[currentMesh.ID];
                    var controller = new AnimationController();
                    controller.BindShapeMatrix = controllerData.BindShapeMatrix;
                    controller.ControllerId = controllerData.Name;
                    controller.JointDictionary = controllerData.JointDictionary;

                    entity.AddComponent(controller);
                }

                foreach (var newEntity in currentMesh.Dependencies)
                {
                    stack.Push(newEntity);
                }
            }

            if (sceneData.Animation.Count > 0)
            {
                var animation = new AnimationComponent();
                animation.Animations = sceneData.Animation;
                animation.Skeletons = sceneData.Skeletons;

                owner.AddComponent(animation);
            }

            CalculateBoundBoxes(owner);

            owner.TraverseInDepth(entity =>
            {
                var transform = entity.Transform;
                transform.Pivot = (Vector3)Vector3F.TransformCoordinate(entity.GetLocalCenter(), transform.GetLocalMatrixF());
            });
            owner.Transform.Position += initialPosition - owner.Transform.Pivot;
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

    private static void SetTransformation(Entity entity, Vector3 pos, Vector3F scale, QuaternionF rot)
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
                material.DiffuseColor = materialData.DiffuseColor;
                material.SpecularColor = materialData.SpecularColor;
                material.Emission = materialData.Emission;
                material.Reflective = materialData.Reflective;
                material.Reflectivity = materialData.Reflectivity;
                material.RefractionIndex = materialData.RefractionIndex;
                material.Shininess = materialData.Shininess;
                material.Transparent = materialData.Transparent;
                material.Transparency = materialData.Transparency;

                if (!String.IsNullOrEmpty(materialData.DiffuseMap))
                {
                    material.TexturePath = sceneData.Images[materialData.DiffuseMap].FilePath;
                    if (File.Exists(material.TexturePath))
                    {
                        var options = new ContentLoadOptions()
                        {
                            AllowDuplication = false,
                            IgnoreRootDirectory = true
                        };
                        material.Texture = contentManager.Load<Texture>(material.TexturePath, options);
                    }
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
}
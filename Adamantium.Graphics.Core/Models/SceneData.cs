using System;
using System.Collections.Generic;

namespace Adamantium.Graphics.Core.Models;

public partial class SceneData
{
   public SceneData()
   {
      Models = new Model();
      meshesDictionary = new Dictionary<String, Model>();
      meshToId = new Dictionary<string, Model>();

      Images = new ImageCollection();
      CameraData = new CameraCollection();
      LightData = new LightCollection();
      Clips = new AnimationSetCollection();
      Materials = new MaterialCollection();
      Controllers = new ControllerCollection();
      Units = new Unit();
      Scenes = [];
      AnimationSources = [];
      Samplers = new SamplerCollection();
      Textures = new TextureCollection();
      MaterialVariants = [];
      XmpPackets = [];
      ExtensionsUsed = [];
      ExtensionsRequired = [];
   }

   public Model CreateMesh(Model parent, String id = "", String name = "")
   {
      lock (this)
      {
         Model meshdata = null;
         if (!meshesDictionary.ContainsKey(name + id))
         {
            meshdata = new Model(parent, id, name);
            meshesDictionary.Add(meshdata.ToString(), meshdata);
            if (!meshToId.ContainsKey(id))
            {
               meshToId.Add(id, meshdata);
            }

            parent?.AddDependency(meshdata);
         }
         else
         {
            meshdata = meshesDictionary[name + id];
         }

         return meshdata;
      }
   }

   /// <summary>
   /// Removes a <see cref="Model"/> and all its sub meshes
   /// </summary>
   /// <param name="mesh"></param>
   public void RemoveMesh(Model mesh)
   {
      lock (this)
      {
         Stack<Model> stack = new Stack<Model>();
         stack.Push(mesh);
         while (stack.Count > 0)
         {
            var current = stack.Pop();

            if (meshesDictionary.ContainsKey(mesh.ToString()))
            {
               meshesDictionary.Remove(mesh.ToString());
            }

            if (meshToId.ContainsKey(mesh.ID))
            {
               meshToId.Remove(mesh.ID);
            }

            foreach (var mesh1 in current.Dependencies)
            {
               stack.Push(mesh1);
            }
         }

         if (mesh.Parent != null)
         {
            mesh.Parent.Dependencies.Remove(mesh);
         }
         else
         {
            Models = null;
         }
      }
   }

   public Model GetModelByName(String name)
   {
      lock (this)
      {
         if (meshesDictionary.ContainsKey(name))
         {
            return meshesDictionary[name];
         }
         return null;
      }
   }

   public Model GetModelByID(String id)
   {
      lock (this)
      {
         if (meshToId.ContainsKey(id))
         {
            return meshToId[id];
         }
         return null;
      }
   }

   /// <summary>
   /// Rebuilds state that is skipped during serialization: re-links each <see cref="Model.Parent"/> and
   /// <see cref="Joint.ParentJoint"/> back-reference by walking the hierarchies, and repopulates the mesh
   /// lookup tables. Call after deserializing a <see cref="SceneData"/> (see <see cref="SceneDataSerializer"/>).
   /// </summary>
   public void RebuildHierarchy()
   {
      meshesDictionary.Clear();
      meshToId.Clear();

      foreach (var root in new[] { Models, OtherNodes })
      {
         if (root == null)
         {
            continue;
         }

         var stack = new Stack<Model>();
         stack.Push(root);
         while (stack.Count > 0)
         {
            var node = stack.Pop();
            meshesDictionary[node.ToString()] = node;
            if (!String.IsNullOrEmpty(node.ID) && !meshToId.ContainsKey(node.ID))
            {
               meshToId[node.ID] = node;
            }

            foreach (var child in node.Dependencies)
            {
               child.Parent = node;
               stack.Push(child);
            }
         }
      }
   }

   public String Name { get; set; }

   /// <summary>Where the scene was loaded from, as it was asked for; null for one made in memory.</summary>
   public String Source { get; set; }

   public Unit Units { get; set; }

   private readonly Dictionary<String, Model> meshesDictionary;
   private readonly Dictionary<String, Model> meshToId; 

   public Model Models { get; set; }

   /// <summary>The nodes of the source file outside the scene in <see cref="Models"/> - those of its other scenes and
   /// those of none - under a root of their own; null when there are none. Kept, not shown.</summary>
   public Model OtherNodes { get; set; }

   /// <summary>The scenes of the source file; <see cref="DefaultScene"/> is the one in <see cref="Models"/>.</summary>
   public List<Scene> Scenes { get; set; }

   /// <summary>The index in <see cref="Scenes"/> of the scene in <see cref="Models"/>; -1 when the file has none.</summary>
   public Int32 DefaultScene { get; set; } = -1;

   public AssetInfo Asset { get; set; }

   /// <summary>What the source file says of itself beyond the rest: its own data and its extensions.</summary>
   public ExtraData Extra { get; set; }

   /// <summary>The extensions the source file says it uses.</summary>
   public List<String> ExtensionsUsed { get; set; }

   /// <summary>The extensions the source file says a reader must know to read it.</summary>
   public List<String> ExtensionsRequired { get; set; }

   /// <summary>The source file's metadata packets (KHR_xmp_json_ld), each as JSON-LD text; an object points at one
   /// by its index from its own <see cref="ExtraData"/>.</summary>
   public List<String> XmpPackets { get; set; }

   /// <summary>The animations of the source file as it has them; <see cref="Clips"/> are made of them.</summary>
   public List<AnimationSource> AnimationSources { get; set; }

   public ImageCollection Images { get; set; }

   /// <summary>The source file's samplers, by key.</summary>
   public SamplerCollection Samplers { get; set; }

   /// <summary>The source file's textures, by key: an image with its sampler.</summary>
   public TextureCollection Textures { get; set; }

   /// <summary>The names of the source file's material variants (KHR_materials_variants): the looks a model can be
   /// switched between; each mesh says its material in each.</summary>
   public List<String> MaterialVariants { get; set; }
   public ControllerCollection Controllers { get; set; }
   public MaterialCollection Materials { get; set; }
   public CameraCollection CameraData { get; set; }
   public LightCollection LightData { get; set; }
   /// <summary>Every clip of the model by name - walking, running - each its frames by the node of the model they
   /// move (its <see cref="Model.ID"/>), a skeleton's joint among them. A format with one clip has it here, under the
   /// model's name.</summary>
   public AnimationSetCollection Clips { get; set; }
}
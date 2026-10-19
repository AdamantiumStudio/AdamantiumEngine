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
      Animation = new AnimationCollection();
      Clips = new AnimationSetCollection();
      Materials = new MaterialCollection();
      Controllers = new ControllerCollection();
      Skeletons = new SkeletonCollection();
      Units = new Unit();
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

      if (Models != null)
      {
         var stack = new Stack<Model>();
         stack.Push(Models);
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

      if (Skeletons != null)
      {
         foreach (var joints in Skeletons.Values)
         {
            foreach (var joint in joints)
            {
               RelinkJoints(joint);
            }
         }
      }
   }

   private static void RelinkJoints(Joint joint)
   {
      if (joint.Children == null) return;

      foreach (var child in joint.Children)
      {
         child.ParentJoint = joint;
         RelinkJoints(child);
      }
   }

   public String Name { get; set; }

   /// <summary>Where the scene was loaded from, as it was asked for; null for one made in memory.</summary>
   public String Source { get; set; }

   public Unit Units { get; set; }

   private readonly Dictionary<String, Model> meshesDictionary;
   private readonly Dictionary<String, Model> meshToId; 

   public Model Models { get; set; }
   public ImageCollection Images { get; set; }
   public ControllerCollection Controllers { get; set; }
   public MaterialCollection Materials { get; set; }
   public CameraCollection CameraData { get; set; }
   public LightCollection LightData { get; set; }
   public AnimationCollection Animation { get; set; }

   /// <summary>Every clip of the model by name - walking, running - each its frames by what they move: a joint of a
   /// skeleton (<see cref="FrameCollection.FullName"/>) or a node of the model (its <see cref="Model.ID"/>). A format
   /// with one clip has it here too, under the model's name.</summary>
   public AnimationSetCollection Clips { get; set; }
   /// <summary>
   /// List of Hierarchical joints. One root for one skeleton
   /// </summary>
   public SkeletonCollection Skeletons { get; set; }
}
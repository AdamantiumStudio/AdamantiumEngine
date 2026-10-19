using System;
using System.Collections.Generic;
using Adamantium.Mathematics;
using MessagePack;

namespace Adamantium.Graphics.Core.Models;

public partial class SceneData
{
   //Класс для хранения меша, собранного из текстурных координат, вершин и нормалей.
   //Содержит в себе всю информацию о геометрии, положении меша в пространстве и его видимости, а так же анимацию, если она присутствует.
   public class Model
   {
      [SerializationConstructor]
      public Model()
      {
         Dependencies = new List<Model>();
         Meshes = new List<Mesh>();
         Rotation = QuaternionF.Identity;
         Scale = Vector3F.One;
      }

      public Model(Model parent = null, String id = "", String name = "") : this()
      {
         ID = id;
         Name = name;
         Parent = parent;
      }

      internal void AddDependency(Model data)
      {
         if (!Dependencies.Contains(data))
         {
            Dependencies.Add(data);
         }
      }

      public override string ToString()
      {
         return Name + ID;
      }

      [IgnoreMember]
      public Model Parent { get; set; }

      public List<Model> Dependencies { get; set; }

      public String Name { get; set; }

      public String ID { get; set; }

      public Vector3F Scale { get; set; }

      public Vector3F Position { get; set; }

      public QuaternionF Rotation { get; set; }

      public List<Mesh> Meshes { get; set; }

      /// <summary>What the source file says of the node beyond the rest.</summary>
      public ExtraData Extra { get; set; }

      /// <summary>What the source file says of the node's mesh as a whole, beyond its parts in <see cref="Meshes"/>.</summary>
      public ExtraData MeshExtra { get; set; }

      /// <summary>How much of each morph target of its meshes the node shows, over theirs; null to show theirs.</summary>
      public Single[] MorphWeights { get; set; }

      /// <summary>Where each copy of the node's meshes stands, relative to the node, when the source draws them many
      /// times over (EXT_mesh_gpu_instancing); null when it draws them once.</summary>
      public List<InstanceTransform> Instances { get; set; }

      /// <summary>Per-copy data beside <see cref="Instances"/> by the source's name for it, as the source has it.</summary>
      public Dictionary<String, VertexAttribute> InstanceAttributes { get; set; }

   }
}
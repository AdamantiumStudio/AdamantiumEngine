using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Adamantium.Engine.Compiler.Converter.Configs;
using Adamantium.Engine.Compiler.Converter.Parsers;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Converter.Converters
{
   public abstract class ConverterBase
   {
      protected ConversionConfig Config { get; }
      protected UpAxis UpAxis { get; set; }
      protected string FileName { get; }
      protected string FilePath { get; }

      protected SceneData SceneDataContainer;

      /// <summary>What the file has that the import cannot do. Filled in by the parse, read by whoever started the
      /// import: half a model lost in silence looks whole right up until someone notices it is not.</summary>
      public List<String> UnsupportedFeatures { get; } = new List<String>();

      protected Boolean IsCanceled { get; set; }

      protected ModelFileParser Parser { get; set; }

      protected ConverterBase(string filePath, ConversionConfig config, UpAxis upAxis = UpAxis.Y_UP_RH)
      {
         Config = config;
         UpAxis = upAxis;
         FilePath = filePath;
         FileName = Path.GetFileName(filePath);
         SceneDataContainer = new SceneData {Name = FileName};
      }

      /// <summary>Returns null only when the file could not be parsed at all. A parse error goes up: there used to
      /// be a MessageBox with a stack trace here - on a build machine it hung the process forever, and the caller
      /// got null plus an invented "the reader failed" instead of the real cause.</summary>
      public virtual SceneData StartConversion(CancellationToken cancellationToken = default)
      {
         CancellationToken = cancellationToken;
         Convert();
         if (IsCanceled)
         {
            return null;
         }

         if (SceneDataContainer.Clips.Count == 0 && SceneDataContainer.Animation.Count > 0)
         {
            SceneDataContainer.Clips[Path.GetFileNameWithoutExtension(FileName)] = SceneDataContainer.Animation;
         }

         AssembleModel();
         return SceneDataContainer;
      }

      //Checked between libraries and on every geometry - where the parse can be dropped without half measures
      protected CancellationToken CancellationToken { get; private set; }

      protected abstract void Convert();

      /// <summary>
      /// Assemble all parts of converted data. Here coordinate system can be changed/optimized, normales/tangents/bitangents calculated
      /// Also textures for mesh (if present) will be copied to new directory from where they will be loaded.
      /// </summary>
      protected virtual void AssembleModel()
      {
         //An empty root is legal: RemoveMesh nulls it out when it takes away the last model
         if (SceneDataContainer.Models == null)
         {
            return;
         }

         Stack<SceneData.Model> stack = new Stack<SceneData.Model>();
         stack.Push(SceneDataContainer.Models);
         while (stack.Count > 0)
         {
            var currentModel = stack.Pop();
            if (Config.OptimizeMeshes)
            {
               for (int i = 0; i < currentModel.Meshes.Count; i++)
               {
                   if (!currentModel.Meshes[i].HasIndices)
                   {
                       currentModel.Meshes[i].GenerateBasicIndices();
                   }
                  // Normals: recompute only when the file gave none, or welding vertices would silently replace
                  // the ones the artist authored with averaged ones. Tangents: not here at all - this runs BEFORE
                  // the axis change, which does not carry tangents over, so anything computed now is stale by the
                  // time the mesh is done. They are worked out once, below, in the engine's own axes.
                  var mesh = currentModel.Meshes[i];
                  mesh.Optimize(recalculateNormals: false, recalculateTangents: false);
               }
            }
            for (int i = 0; i < currentModel.Meshes.Count; i++)
            {
               var mesh = currentModel.Meshes[i];
               mesh.UpAxis = UpAxis;
               //Move vertices, normals and texture coordinates into the engine's coordinate system
               UpAxis destinationUpAxis = UpAxis.Y_DOWN_RH;
               if (Config.ConvertToRHDirectX || Config.ConvertToOGL)
               {
                  destinationUpAxis = UpAxis.Y_UP_RH;
               }
               mesh.ChangeCoordinateSystem(destinationUpAxis);
               if (destinationUpAxis == UpAxis.Y_DOWN_RH)
               {
                  mesh.ReverseWinding();
               }

               // Only when the file gave none: averaging over triangles erases the hard edges the artist authored
               // those normals for.
               if (mesh.Normals.Length != mesh.Points.Length)
               {
                  mesh.CalculateNormals();
               }

               if (Config.CalculateTangentsBitangentsIfNotPresent)
               {
                  mesh.CalculateTangentsAndBinormals();
               }

               mesh.CalculateBoundingVolumes();
            }

            foreach (var mesh in currentModel.Dependencies)
            {
               stack.Push(mesh);
            }
         }

         ConvertToEngineAxes(DestinationUpAxis);
      }

      private UpAxis DestinationUpAxis =>
         Config.ConvertToRHDirectX || Config.ConvertToOGL ? UpAxis.Y_UP_RH : UpAxis.Y_DOWN_RH;

      /// <summary>Moves EVERYTHING but the vertices themselves into the coordinate system the meshes were just put
      /// in: node placement, lights, cameras, bones and animation key frames. This used to happen for Z_UP only and
      /// scattered across the parsing code, so in a Y_UP file the bones came out flipped relative to their own
      /// model, and a component-wise node pose was never converted at all.</summary>
      private void ConvertToEngineAxes(UpAxis destinationUpAxis)
      {
         var conversion = AxisConversion.Between(UpAxis, destinationUpAxis);
         if (conversion == Matrix4x4F.Identity)
         {
            return;
         }

         // Through composing and decomposing a matrix rather than per coordinate: a basis change can be a
         // reflection, which would send the scale negative. Decompose folds that sign into the rotation, where it
         // belongs.
         foreach (var model in Models())
         {
            var moved = AxisConversion.Convert(Compose(model.Scale, model.Rotation, model.Position), conversion);
            moved.Decompose(out var scale, out var rotation, out var position);
            model.Scale = scale;
            model.Rotation = rotation;
            model.Position = position;
         }

         foreach (var light in SceneDataContainer.LightData.Values)
         {
            var moved = AxisConversion.Convert(Compose(light.Scale, light.Rotation, light.Position), conversion);
            moved.Decompose(out var scale, out var rotation, out var position);
            light.Scale = scale;
            light.Rotation = rotation;
            light.Position = position;
         }

         foreach (var camera in SceneDataContainer.CameraData.Values)
         {
            var moved = AxisConversion.Convert(
               Compose(Vector3F.One, camera.Rotation, camera.Translation), conversion);
            moved.Decompose(out _, out var rotation, out var translation);
            camera.Rotation = rotation;
            camera.Translation = translation;
         }

         foreach (var controller in SceneDataContainer.Controllers.Values)
         {
            controller.BindShapeMatrix = AxisConversion.Convert(controller.BindShapeMatrix, conversion);

            for (int i = 0; i < controller.JointMatrices.Count; i++)
            {
               controller.JointMatrices[i] = AxisConversion.Convert(controller.JointMatrices[i], conversion);
            }

            foreach (var name in controller.JointDictionary.Keys.ToArray())
            {
               controller.JointDictionary[name] = AxisConversion.Convert(controller.JointDictionary[name], conversion);
            }
         }

         foreach (var joints in SceneDataContainer.Skeletons.Values)
         {
            foreach (var joint in Flatten(joints))
            {
               joint.LocalMatrix = AxisConversion.Convert(joint.LocalMatrix, conversion);
            }
         }

         foreach (var frames in AllFrames())
         {
            foreach (var frame in frames)
            {
               var pose = AxisConversion.Convert(frame.ComposeMatrix(), conversion);
               pose.Decompose(out var scale, out var rotation, out var position);
               frame.Position = position;
               frame.Scale = scale;
               frame.Rotation = rotation;
            }
         }
      }

      // Each list of frames once: the one clip of a format with one is both the animation and a clip.
      private IEnumerable<SceneData.FrameCollection> AllFrames()
      {
         var seen = new HashSet<SceneData.FrameCollection>(ReferenceEqualityComparer.Instance);
         foreach (var frames in SceneDataContainer.Animation.Values)
         {
            if (seen.Add(frames)) yield return frames;
         }

         foreach (var clip in SceneDataContainer.Clips.Values)
         {
            foreach (var frames in clip.Values)
            {
               if (seen.Add(frames)) yield return frames;
            }
         }
      }

      private static IEnumerable<SceneData.Joint> Flatten(List<SceneData.Joint> roots)
      {
         var stack = new Stack<SceneData.Joint>(roots);
         while (stack.Count > 0)
         {
            var joint = stack.Pop();
            yield return joint;
            foreach (var child in joint.Children) stack.Push(child);
         }
      }

      private static Matrix4x4F Compose(Vector3F scale, QuaternionF rotation, Vector3F translation) =>
         Matrix4x4F.Scaling(scale) * Matrix4x4F.RotationQuaternion(rotation) * Matrix4x4F.Translation(translation);

      private IEnumerable<SceneData.Model> Models()
      {
         if (SceneDataContainer.Models == null) yield break;

         var stack = new Stack<SceneData.Model>();
         stack.Push(SceneDataContainer.Models);
         while (stack.Count > 0)
         {
            var model = stack.Pop();
            yield return model;
            foreach (var child in model.Dependencies) stack.Push(child);
         }
      }
   }
}

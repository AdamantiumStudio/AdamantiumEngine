using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Adamantium.Engine.Compiler.Converter.Configs;
using Adamantium.Engine.Compiler.Converter.Parsers;
using Adamantium.Engine.Compiler.Models;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Converter.Converters
{
   public abstract class ConverterBase
   {
      private readonly byte[] _fileBytes;
      private readonly Dictionary<string, int> _idUses = new(StringComparer.Ordinal);
      private readonly HashSet<string> _usedIds = new(StringComparer.Ordinal);

      protected ConversionConfig Config { get; }
      protected UpAxis UpAxis { get; set; }
      protected string FileName { get; }
      protected string FilePath { get; }

      protected SceneData SceneDataContainer;

      /// <summary>What the file has that the import cannot do. Filled in by the parse, read by whoever started the
      /// import: half a model lost in silence looks whole right up until someone notices it is not.</summary>
      public List<String> UnsupportedFeatures => Notes.All;

      /// <summary>The notes behind <see cref="UnsupportedFeatures"/>, each told once; a file reader is handed them.</summary>
      private protected ImportNotes Notes { get; } = new();

      protected Boolean IsCanceled { get; set; }

      protected ModelFileParser Parser { get; set; }

      protected ConverterBase(string filePath, ConversionConfig config, UpAxis upAxis = UpAxis.Y_UP_RH)
         : this(filePath, null, config, upAxis)
      {
      }

      /// <summary>A converter of <paramref name="fileBytes"/> in place of the file at <paramref name="filePath"/>: a
      /// model carried inside another file - a PDF's 3D artwork - has bytes and no file of its own. The path still
      /// names the model and says where whatever it refers to lies. Null bytes read the file.</summary>
      protected ConverterBase(string filePath, byte[] fileBytes, ConversionConfig config, UpAxis upAxis)
      {
         _fileBytes = fileBytes;
         Config = config;
         UpAxis = upAxis;
         FilePath = filePath;
         FileName = Path.GetFileName(filePath);
         SceneDataContainer = new SceneData {Name = FileName};
      }

      /// <summary>The model's bytes: those the converter was given, or else the file's.</summary>
      protected byte[] ReadFileBytes() => _fileBytes ?? File.ReadAllBytes(FilePath);

      /// <summary>Adds <paramref name="feature"/> to <see cref="UnsupportedFeatures"/> unless it is there already.</summary>
      protected void Tell(string feature) => Notes.Tell(feature);

      /// <summary><paramref name="id"/> the first time, then with the count of its uses appended - skipping any ID
      /// already given - so a node shown in several places gets a model ID of its own in each.</summary>
      protected string CreateUniqueId(string id)
      {
         var count = _idUses.GetValueOrDefault(id);
         string candidate;
         do
         {
            count++;
            candidate = count == 1 ? id : $"{id} ({count})";
         }
         while (!_usedIds.Add(candidate));

         _idUses[id] = count;
         return candidate;
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

         if (Config.SourceUpAxis is { } upAxis)
         {
            UpAxis = upAxis;
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
         if (SceneDataContainer.OtherNodes != null)
         {
            stack.Push(SceneDataContainer.OtherNodes);
         }

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

               // The file gave none: flat, as glTF asks and as OBJ shades a face in no smoothing group - averaged over
               // the corners welded above, a box was shaded like a ball. Recalculated only when asked otherwise:
               // averaging erases the hard edges the artist authored the normals for.
               if (mesh.Normals.Length != mesh.Points.Length)
               {
                  mesh.CalculateFlatNormals();
               }
               else if (Config.RecalculateNormals)
               {
                  mesh.CalculateNormals();
               }

               if (Config.CalculateTangentsBitangentsIfNotPresent &&
                   (Config.RecalculateNormals || mesh.Tangents.Length != mesh.Points.Length))
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

            foreach (var copy in model.Instances ?? [])
            {
               var placed = AxisConversion.Convert(Compose(copy.Scale, copy.Rotation, copy.Position), conversion);
               placed.Decompose(out var copyScale, out var copyRotation, out var copyPosition);
               copy.Scale = copyScale;
               copy.Rotation = copyRotation;
               copy.Position = copyPosition;
            }
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
            for (int i = 0; i < controller.JointMatrices.Count; i++)
            {
               controller.JointMatrices[i] = AxisConversion.Convert(controller.JointMatrices[i], conversion);
            }
         }

         foreach (var clip in SceneDataContainer.Clips.Values)
         {
            foreach (var frames in clip.Values)
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
      }

      private static Matrix4x4F Compose(Vector3F scale, QuaternionF rotation, Vector3F translation) =>
         Matrix4x4.CreateScale<float>(scale) * Matrix4x4.CreateFromQuaternion<float>(rotation) * Matrix4x4.CreateTranslation<float>(translation);

      private IEnumerable<SceneData.Model> Models()
      {
         if (SceneDataContainer.Models == null) yield break;

         var stack = new Stack<SceneData.Model>();
         stack.Push(SceneDataContainer.Models);
         if (SceneDataContainer.OtherNodes != null)
         {
            stack.Push(SceneDataContainer.OtherNodes);
         }

         while (stack.Count > 0)
         {
            var model = stack.Pop();
            yield return model;
            foreach (var child in model.Dependencies) stack.Push(child);
         }
      }
   }
}

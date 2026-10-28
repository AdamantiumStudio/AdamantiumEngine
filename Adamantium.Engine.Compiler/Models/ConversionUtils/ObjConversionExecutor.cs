using System;
using System.Collections.Generic;
using System.Globalization;
using Adamantium.Engine.Compiler.Converter.Configs;
using Adamantium.Engine.Compiler.Converter.Containers;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.ConversionUtils
{
   internal class ObjConversionExecutor:ConversionExecutorBase
   {
      /*
          Ka - ambient
          Kd - diffuse
          Ks - specular
          Ns - specular exponent
          Ni - refraction index
          d/Tr - transparency

          Illunimation models enum:
          0. Color on and Ambient off
          1. Color on and Ambient on
          2. Highlight on
          3. Reflection on and Ray trace on
          4. Transparency: Glass on, Reflection: Ray trace on
          5. Reflection: Fresnel on and Ray trace on
          6. Transparency: Refraction on, Reflection: Fresnel off and Ray trace on
          7. Transparency: Refraction on, Reflection: Fresnel on and Ray trace on
          8. Reflection on and Ray trace off
          9. Transparency: Glass on, Reflection: Ray trace off
          10. Casts shadows onto invisible surfaces

          #Texture maps
          map_Ka lenna.tga           # the ambient texture map
          map_Kd lenna.tga           # the diffuse texture map (most of the time, it will
                                     # be the same as the ambient texture map)
          map_Ks lenna.tga           # specular color texture map
          map_Ns lenna_spec.tga      # specular highlight component
          map_d lenna_alpha.tga      # the alpha texture map
          map_bump lenna_bump.tga    # some implementations use 'map_bump' instead of 'bump' below

          bump lenna_bump.tga        # bump map (which by default uses luminance channel of the image)
          disp lenna_disp.tga        # displacement map
          decal lenna_stencil.tga    # stencil decal texture (defaults to 'matte' channel of the image)

          Texture map statements may also have option parameters (see full spec).

          map_Ka -o 1 1 1 ambient.tga            # texture origin (1,1,1) 
          refl -type sphere clouds.tga           # spherical reflection map

      */

      private const String materialId = "newmtl";
      private const String ambient = "Ka ";
      private const String diffuse = "Kd ";
      private const String specular = "Ks ";
      private const String specularExponent = "Ns ";
      private const String refractionIndex = "Ni ";
      private const String diffuseMap = "map_Kd ";
      private const String ambientMap = "map_Ka ";
      private const String alphaMap = "map_d ";
      private const String specularMap = "map_Ks ";
      private const String specularHighlightMap = "map_Ns ";
      private const String displacementMap = "disp ";
      private const String bumpMap = "bump ";
      private const String bumpMap2 = "map_bump ";
      private const String transparency1 = "d ";
      private const String transparency2 = "Tr ";

      public ObjConversionExecutor(ConversionConfig config, UpAxis upAxis) : base(config, upAxis)
      {
      }

      /* Obj conversion */

      internal SceneData.Model ConstructMeshGeometry(SceneData model, List<IndicesContainer> indicesContainers, ObjGeometryData geometryData, ObjMeshData meshData)
      {
         //The real mesh takes the temporary one's semantic
         SceneData.Model constructedMesh = null;
         var meshName = meshData.Name;
         if (meshData.Type == ObjectType.Group)
         {
            var names = meshName.Split(' ');
            if (names.Length > 1)
            {
               SceneData.Model parent = model.Models;
               for (int i = 0; i < names.Length; i++)
               {
                  parent = model.CreateMesh(parent, "", names[i]);
               }
               meshName = names[^1];
               constructedMesh = model.GetModelByName(meshName);
            }
            else
            {
               constructedMesh = model.CreateMesh(model.Models, "", meshName);
            }
         }
         else
         {
            constructedMesh = model.CreateMesh(model.Models, "", meshName);
         }

         foreach (var indicesContainer in indicesContainers)
         {
            Mesh mesh = new Mesh();
            mesh.MeshTopology = indicesContainer.MeshTopology;
            mesh.MaterialID = indicesContainer.MaterialId;

            var semantic = indicesContainer.Semantic;
            //Vertices are reordered the way the faces name them, not the way the file stores them - so a
            //coordinate set may well repeat
            var positions = new List<Vector3<double>>();
            var normals = new List<Vector3F>();
            var uv0 = new List<Vector2F>();
            for (int i = 0; i < indicesContainer.Positions.Count; i++)
            {
               var position = geometryData.Positions[indicesContainer.Positions[i]];
               positions.Add(position);

               // vn was parsed and then dropped right here, so the mesh re-derived normals by averaging and the
               // hard edges the file described were lost.
               if (semantic.HasFlag(VertexSemantic.Normal) && i < indicesContainer.Normals.Count)
               {
                  normals.Add(geometryData.Normals[indicesContainer.Normals[i]]);
               }

               if (semantic.HasFlag(VertexSemantic.UV0))
               {
                  uv0.Add(geometryData.UV[indicesContainer.UV0[i]]);
               }
            }
            mesh.SetPoints(positions);
            if (normals.Count == positions.Count) mesh.SetNormals(normals);
            if (uv0.Count == positions.Count) mesh.SetUVs(0, uv0);
            mesh.GenerateBasicIndices();
            constructedMesh.Meshes.Add(mesh);
         }
         return constructedMesh;
      }


      private Vector3F ParseFloatString(string str)
      {
         Vector3F color = new Vector3F();
         String[] line = str.Split(' ');
         for (int j = 0; j < line.Length; j++)
         {
            float result;
            if (Single.TryParse(line[j], NumberStyles.Float, CultureInfo.InvariantCulture, out result))
            {
               if (j == 1)
               {
                  color.X = result;
               }
               else if (j == 2)
               {
                  color.Y = result;
               }
               else if (j == 3)
               {
                  color.Z = result;
               }
            }
         }
         return color;
      }

      public SceneData.Material GetMaterial(ObjMaterialData material)
      {
         SceneData.Material materialData = new SceneData.Material();
         materialData.ID = material.Name;
         for (int i = 0; i < material.Data.Count; i++)
         {
            String line = material.Data[i];
            if (line.StartsWith(materialId))
            {
               materialData.ID = line.Split(' ')[1];
            }
            else if (line.StartsWith(ambient))
            {
               materialData.AmbientColor = new Vector4F(ParseFloatString(line), 1);
            }
            else if (line.StartsWith(diffuse))
            {
               materialData.DiffuseColor = new Vector4F(ParseFloatString(line), 1);
            }
            else if (line.StartsWith(specular))
            {
               materialData.SpecularColor = new Vector4F(ParseFloatString(line), 1);
            }
            else if (line.StartsWith(refractionIndex))
            {
               float result;
               if (Single.TryParse(line.Substring(refractionIndex.Length), NumberStyles.Float, CultureInfo.InvariantCulture, out result))
               {
                  materialData.RefractionIndex = result;
               }
            }
            else if (line.StartsWith(transparency1))
            {
               float result;
               if (Single.TryParse(line.Substring(transparency1.Length), NumberStyles.Float, CultureInfo.InvariantCulture, out result))
               {
                  materialData.Transparency = result;
               }
            }
            else if (line.StartsWith(transparency2))
            {
               float result;
               if (Single.TryParse(line.Substring(transparency2.Length), NumberStyles.Float, CultureInfo.InvariantCulture, out result))
               {
                  materialData.Transparency = result;
               }
            }
            else if (line.StartsWith(diffuseMap))
            {
               materialData.DiffuseMap = line.Substring(diffuseMap.Length).Trim(' ');
            }
            else if (line.StartsWith(specularMap))
            {
               materialData.SpecularColorMap = line.Substring(specularMap.Length).Trim(' ');
            }
            else if (line.StartsWith(specularHighlightMap))
            {
               materialData.SpecularHighlightMap = line.Substring(specularHighlightMap.Length).Trim(' ');
            }
            else if (line.StartsWith(ambientMap))
            {
               materialData.AmbientMap = line.Substring(ambientMap.Length).Trim(' ');
            }
            else if (line.StartsWith(alphaMap))
            {
               materialData.AlphaTextureMap = line.Substring(alphaMap.Length).Trim(' ');
            }
            else if (line.StartsWith(bumpMap))
            {
               materialData.BumpMap = line.Substring(bumpMap.Length).Trim(' ');
            }
            else if (line.StartsWith(bumpMap2))
            {
               materialData.BumpMap = line.Substring(bumpMap2.Length).Trim(' ');
            }
            else if (line.StartsWith(displacementMap))
            {
               materialData.DisplacementMap = line.Substring(displacementMap.Length).Trim(' ');
            }
         }
         return materialData;
      }
   }
}

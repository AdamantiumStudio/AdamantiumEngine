using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Engine.Compiler.Models.Prc;

namespace Adamantium.Engine.CompilerTests;

/// <summary>Writes the entities of PRC test files as the specification's serialization procedures lay them out:
/// base content, graphics, user data, the globals, the tree, the tessellation section and the model file.</summary>
internal static class PrcTestEntities
{
    /// <summary>The PRC unique identifier the writers give an entity that names none of its own.</summary>
    public const uint AnyId = 7;

    public static void WriteContentPrcBase(PrcTestBitWriter writer, string name, bool referenceable, uint uniqueId = AnyId)
    {
        writer.WriteUnsignedInteger(0);
        writer.WriteBoolean(false);
        writer.WriteString(name);
        if (referenceable)
        {
            writer.WriteUnsignedInteger(0);
            writer.WriteUnsignedInteger(0);
            writer.WriteUnsignedInteger(uniqueId);
        }
    }

    public static void WriteGraphics(PrcTestBitWriter writer, int style, ushort behavior = 0x0001, int layer = -1)
    {
        writer.WriteBoolean(false);
        writer.WriteUnsignedInteger((uint)(layer + 1));
        writer.WriteUnsignedInteger((uint)(style + 1));
        writer.WriteCharacter((byte)behavior);
        writer.WriteCharacter((byte)(behavior >> 8));
    }

    public static void WriteBaseWithGraphics(PrcTestBitWriter writer, string name, int style, ushort behavior = 0x0001, uint uniqueId = AnyId,
        int layer = -1)
    {
        WriteContentPrcBase(writer, name, true, uniqueId);
        WriteGraphics(writer, style, behavior, layer);
    }

    public static void WriteUserData(PrcTestBitWriter writer) => writer.WriteUnsignedInteger(0);

    /// <summary>A schema: for each entry its type and its tokens.</summary>
    public static void WriteSchema(PrcTestBitWriter writer, IReadOnlyList<PrcTestSchema> schemas)
    {
        writer.WriteUnsignedInteger((uint)schemas.Count);
        foreach (var schema in schemas)
        {
            writer.WriteUnsignedInteger(schema.Type);
            writer.WriteUnsignedInteger((uint)schema.Tokens.Length);
            foreach (var token in schema.Tokens)
            {
                writer.WriteUnsignedInteger(token);
            }
        }
    }

    /// <summary>Globals with the colors given and one plain-color style per color, then
    /// <paramref name="materialStyles"/>; the optional parts are written only when given.</summary>
    public static byte[] CreateGlobals(IReadOnlyList<double[]> colors, IReadOnlyList<PrcTestSchema> schemas = null,
        Action<PrcTestBitWriter> coordinateSystems = null, int coordinateSystemCount = 0, IReadOnlyList<string> fonts = null,
        Action<PrcTestBitWriter> pictures = null, Action<PrcTestBitWriter> textures = null, Action<PrcTestBitWriter> materials = null,
        IReadOnlyList<Action<PrcTestBitWriter>> materialStyles = null)
    {
        var writer = new PrcTestBitWriter();
        WriteSchema(writer, schemas ?? []);
        writer.WriteUnsignedInteger(PrcTypes.FileStructureGlobals);
        WriteContentPrcBase(writer, null, false);
        writer.WriteUnsignedInteger(0);
        writer.WriteDouble(0);
        writer.WriteDouble(0);
        writer.WriteString(null);
        writer.WriteUnsignedInteger((uint)(fonts?.Count ?? 0));
        foreach (var font in fonts ?? [])
        {
            writer.WriteString(font);
            writer.WriteUnsignedInteger(0);
            writer.WriteUnsignedInteger(1);
            writer.WriteUnsignedInteger(11);
            writer.WriteCharacter(2);
        }

        writer.WriteUnsignedInteger((uint)colors.Count);
        foreach (var color in colors)
        {
            writer.WriteDouble(color[0]);
            writer.WriteDouble(color[1]);
            writer.WriteDouble(color[2]);
        }

        Action<PrcTestBitWriter>[] arrays = [pictures, textures, materials];
        foreach (var array in arrays)
        {
            if (array == null)
            {
                writer.WriteUnsignedInteger(0);
            }
            else
            {
                array(writer);
            }
        }

        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger((uint)(colors.Count + (materialStyles?.Count ?? 0)));
        for (var color = 0; color < colors.Count; color++)
        {
            writer.WriteUnsignedInteger(PrcTypes.Style);
            WriteContentPrcBase(writer, $"Style {color}", true);
            writer.WriteDouble(0);
            writer.WriteBoolean(false);
            writer.WriteUnsignedInteger(0);
            writer.WriteBoolean(false);
            writer.WriteUnsignedInteger((uint)(color * 3 + 1));
            writer.WriteBoolean(colors[color].Length > 3);
            if (colors[color].Length > 3)
            {
                writer.WriteCharacter((byte)colors[color][3]);
            }

            writer.WriteBoolean(false);
            writer.WriteBoolean(false);
            writer.WriteBoolean(false);
        }

        foreach (var style in materialStyles ?? [])
        {
            style(writer);
        }

        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger((uint)coordinateSystemCount);
        coordinateSystems?.Invoke(writer);
        WriteUserData(writer);
        return writer.ToArray();
    }

    /// <summary>A tree of the parts and occurrences the writers give, and the file structure's internal data naming
    /// its last occurrence the root.</summary>
    public static byte[] CreateTree(IReadOnlyList<Action<PrcTestBitWriter>> parts, IReadOnlyList<Action<PrcTestBitWriter>> occurrences,
        PrcTestBitWriter writer = null)
    {
        writer ??= new PrcTestBitWriter();
        writer.WriteUnsignedInteger(PrcTypes.FileStructureTree);
        WriteContentPrcBase(writer, null, false);
        writer.WriteUnsignedInteger((uint)parts.Count);
        foreach (var part in parts)
        {
            part(writer);
        }

        writer.WriteUnsignedInteger((uint)occurrences.Count);
        foreach (var occurrence in occurrences)
        {
            occurrence(writer);
        }

        writer.WriteUnsignedInteger(PrcTypes.FileStructure);
        WriteContentPrcBase(writer, null, false);
        writer.WriteUnsignedInteger(100);
        writer.WriteUnsignedInteger((uint)occurrences.Count);
        WriteUserData(writer);
        return writer.ToArray();
    }

    public static void WriteEmptyMarkups(PrcTestBitWriter writer)
    {
        for (var array = 0; array < 4; array++)
        {
            writer.WriteUnsignedInteger(0);
        }
    }

    /// <summary>A part definition of the representation items the writer gives.</summary>
    public static Action<PrcTestBitWriter> CreatePart(string name, int itemCount, Action<PrcTestBitWriter> items,
        Action<PrcTestBitWriter> markups = null, Action<PrcTestBitWriter> beforeUserData = null) => writer =>
    {
        writer.WriteUnsignedInteger(PrcTypes.PartDefinition);
        WriteBaseWithGraphics(writer, name, -1);
        for (var corner = 0; corner < 6; corner++)
        {
            writer.WriteDouble(0);
        }

        writer.WriteUnsignedInteger((uint)itemCount);
        items(writer);
        (markups ?? WriteEmptyMarkups)(writer);
        writer.WriteUnsignedInteger(0);
        beforeUserData?.Invoke(writer);
        WriteUserData(writer);
    };

    /// <summary>A product occurrence of a part (-1 for none) and sons, at a location the writer gives (null for none),
    /// in millimeters of a CAD system unless <paramref name="unit"/> and <paramref name="unitFromCad"/> say otherwise,
    /// with the entity references, views, display filters and scene display parameters the writers give.</summary>
    public static Action<PrcTestBitWriter> CreateOccurrence(string name, int part, IReadOnlyList<int> sons,
        Action<PrcTestBitWriter> location = null, int prototype = -1, ushort behavior = 0x0001, uint uniqueId = AnyId,
        double unit = 1, bool unitFromCad = true, IReadOnlyList<Action<PrcTestBitWriter>> references = null,
        IReadOnlyList<Action<PrcTestBitWriter>> views = null, IReadOnlyList<Action<PrcTestBitWriter>> displayFilters = null,
        IReadOnlyList<Action<PrcTestBitWriter>> scenes = null) => writer =>
    {
        writer.WriteUnsignedInteger(PrcTypes.ProductOccurrence);
        WriteBaseWithGraphics(writer, name, -1, behavior, uniqueId);
        writer.WriteUnsignedInteger((uint)(part + 1));
        writer.WriteUnsignedInteger((uint)(prototype + 1));
        if (prototype >= 0)
        {
            writer.WriteBoolean(true);
        }

        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger((uint)sons.Count);
        foreach (var son in sons)
        {
            writer.WriteUnsignedInteger((uint)son);
        }

        writer.WriteCharacter(0);
        writer.WriteBoolean(unitFromCad);
        writer.WriteDouble(unit);
        writer.WriteCharacter(0);
        writer.WriteInteger(3);
        writer.WriteBoolean(location != null);
        location?.Invoke(writer);
        WriteEntities(writer, references);
        WriteEmptyMarkups(writer);
        WriteEntities(writer, views);
        writer.WriteBoolean(false);
        WriteEntities(writer, displayFilters);
        WriteEntities(writer, scenes);
        WriteUserData(writer);
    };

    /// <summary>A count, then the entities the writers give.</summary>
    public static void WriteEntities(PrcTestBitWriter writer, IReadOnlyList<Action<PrcTestBitWriter>> entities)
    {
        writer.WriteUnsignedInteger((uint)(entities?.Count ?? 0));
        foreach (var entity in entities ?? [])
        {
            entity(writer);
        }
    }

    /// <summary>A PRC_TYPE_MISC_EntityReference: its own graphics - the style and behavior it gives its target - a
    /// reference coordinate system of the globals (-1 for none), and a reference on a PRC base to the entity of type
    /// <paramref name="targetType"/> and unique identifier <paramref name="targetId"/> in the same file structure.</summary>
    public static Action<PrcTestBitWriter> CreateEntityReference(uint targetType, uint targetId, int style = -1, ushort behavior = 0xFFFF,
        int coordinateSystem = -1) => writer =>
    {
        writer.WriteUnsignedInteger(PrcTypes.EntityReference);
        WriteBaseWithGraphics(writer, null, style, behavior);
        writer.WriteUnsignedInteger((uint)(coordinateSystem + 1));
        writer.WriteBoolean(true);
        writer.WriteUnsignedInteger(PrcTypes.ReferenceOnPrcBase);
        writer.WriteUnsignedInteger(targetType);
        writer.WriteBoolean(true);
        writer.WriteUnsignedInteger(targetId);
        WriteUserData(writer);
    };

    /// <summary>A PRC_TYPE_ASM_Filter: whether it is active, the layers it keeps (or takes away), the entities it keeps
    /// (or takes away) - representation items of this file structure by their unique identifiers.</summary>
    public static Action<PrcTestBitWriter> CreateFilter(string name, bool active, bool layersInclusive, IReadOnlyList<uint> layers,
        bool entitiesInclusive = false, IReadOnlyList<uint> entities = null) => writer =>
    {
        writer.WriteUnsignedInteger(PrcTypes.Filter);
        WriteContentPrcBase(writer, name, true);
        writer.WriteBoolean(active);
        writer.WriteBoolean(layersInclusive);
        WriteUnsignedIntegers(writer, layers);
        writer.WriteBoolean(entitiesInclusive);
        writer.WriteUnsignedInteger((uint)(entities?.Count ?? 0));
        foreach (var entity in entities ?? [])
        {
            CreateEntityReference(PrcTypes.PolyBrepModel, entity)(writer);
        }

        WriteUserData(writer);
    };

    /// <summary>A PRC_TYPE_GRAPH_Camera: perspective with its fields of view in radians, or orthographic with its scale.</summary>
    public static Action<PrcTestBitWriter> CreateCamera(string name, bool orthographic, double[] position, double[] lookAt, double[] up,
        double fieldOfView, double near = 1, double far = 1000) => writer =>
    {
        writer.WriteUnsignedInteger(PrcTypes.Camera);
        WriteContentPrcBase(writer, name, true);
        writer.WriteBoolean(orthographic);
        foreach (var value in position.Concat(lookAt).Concat(up))
        {
            writer.WriteDouble(value);
        }

        writer.WriteDouble(fieldOfView);
        writer.WriteDouble(fieldOfView);
        writer.WriteDouble(1.5);
        writer.WriteDouble(near);
        writer.WriteDouble(far);
        writer.WriteDouble(1);
    };

    /// <summary>A directional light of three color indices - ambient, diffuse, specular, as the errata have them -
    /// shining along <paramref name="direction"/>, with the intensity of authoring version 8030 on.</summary>
    public static Action<PrcTestBitWriter> CreateDirectionalLight(string name, int diffuseColor, double[] direction, double intensity) => writer =>
    {
        writer.WriteUnsignedInteger(PrcTypes.DirectionalLight);
        WriteContentPrcBase(writer, name, true);
        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger((uint)(diffuseColor * 3 + 1));
        writer.WriteUnsignedInteger(0);
        foreach (var value in direction)
        {
            writer.WriteDouble(value);
        }

        writer.WriteDouble(intensity);
    };

    /// <summary>A point light at <paramref name="location"/> of a diffuse color of the globals, fading as given.</summary>
    public static Action<PrcTestBitWriter> CreatePointLight(string name, int diffuseColor, double[] location, double linear) => writer =>
    {
        writer.WriteUnsignedInteger(PrcTypes.PointLight);
        WriteContentPrcBase(writer, name, true);
        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger((uint)(diffuseColor * 3 + 1));
        writer.WriteUnsignedInteger(0);
        foreach (var value in location)
        {
            writer.WriteDouble(value);
        }

        writer.WriteDouble(1);
        writer.WriteDouble(linear);
        writer.WriteDouble(0);
    };

    /// <summary>PRC_TYPE_GRAPH_SceneDisplayParameters of the lights and camera the writers give, and a clipping plane
    /// when asked for.</summary>
    public static Action<PrcTestBitWriter> CreateScene(string name, IReadOnlyList<Action<PrcTestBitWriter>> lights,
        Action<PrcTestBitWriter> camera, bool clippingPlane = false) => writer =>
    {
        writer.WriteUnsignedInteger(PrcTypes.SceneDisplayParameters);
        WriteContentPrcBase(writer, name, true);
        writer.WriteBoolean(true);
        WriteEntities(writer, lights);
        writer.WriteBoolean(camera != null);
        camera?.Invoke(writer);
        writer.WriteBoolean(false);
        writer.WriteUnsignedInteger(clippingPlane ? 1u : 0u);
        if (clippingPlane)
        {
            WritePlaneSurface(writer);
        }

        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger(0);
        writer.WriteBoolean(false);
    };

    /// <summary>A PRC_TYPE_SURF_Plane as views and clipping planes have it: no base content, a translation, its domain
    /// and parameterization.</summary>
    public static void WritePlaneSurface(PrcTestBitWriter writer)
    {
        writer.WriteUnsignedInteger(PrcTypes.SurfacePlane);
        writer.WriteBoolean(false);
        writer.WriteUnsignedInteger(0);
        writer.WriteCharacter(0x01);
        double[] values = [0, 0, 2, -1, -1, 1, 1, 1, 0, 1, 0];
        foreach (var value in values)
        {
            writer.WriteDouble(value);
        }
    }

    /// <summary>A PRC_TYPE_MKP_View of the scene the writer gives (null for none), default or not.</summary>
    public static Action<PrcTestBitWriter> CreateView(string name, uint uniqueId, Action<PrcTestBitWriter> scene, bool isDefault) => writer =>
    {
        writer.WriteUnsignedInteger(PrcTypes.View);
        WriteBaseWithGraphics(writer, name, -1, 0x0001, uniqueId);
        writer.WriteUnsignedInteger(0);
        WritePlaneSurface(writer);
        writer.WriteBoolean(scene != null);
        scene?.Invoke(writer);
        writer.WriteBoolean(false);
        writer.WriteBoolean(isDefault);
        writer.WriteBoolean(false);
        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger(0);
        WriteUserData(writer);
    };

    /// <summary>The pictures of the globals: each a PNG in the uncompressed file of its own index.</summary>
    public static Action<PrcTestBitWriter> CreatePictures(int count) => writer =>
    {
        writer.WriteUnsignedInteger((uint)count);
        for (var picture = 0; picture < count; picture++)
        {
            writer.WriteUnsignedInteger(PrcTypes.Picture);
            WriteContentPrcBase(writer, $"Picture {picture}", false);
            writer.WriteInteger(0);
            writer.WriteUnsignedInteger((uint)(picture + 1));
            writer.WriteUnsignedInteger(0);
            writer.WriteUnsignedInteger(0);
        }
    };

    /// <summary>A texture definition of the globals: a 2D picture on stored coordinates, laid as the given mapping
    /// attributes say by the given function, wrapping as given, its coordinates transformed by a translation, a
    /// rotation by its x axis alone and a uniform scale when <paramref name="scale"/> is not 1.</summary>
    public static void WriteTextureDefinition(PrcTestBitWriter writer, int picture, uint mappingAttributes, int function, int wrapS, int wrapT,
        double scale = 1, double[] xAxis = null, double[] translation = null)
    {
        writer.WriteUnsignedInteger(PrcTypes.TextureDefinition);
        WriteContentPrcBase(writer, $"Texture of {picture}", true);
        writer.WriteUnsignedInteger((uint)(picture + 1));
        writer.WriteCharacter(2);
        writer.WriteInteger(1);
        writer.WriteUnsignedInteger(mappingAttributes);
        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger(1);
        writer.WriteCharacter(0x0F);
        writer.WriteInteger(function);
        writer.WriteInteger(0);
        writer.WriteInteger(0);
        writer.WriteCharacter(0);
        writer.WriteInteger(wrapS);
        writer.WriteInteger(wrapT);
        var transformed = scale != 1 || xAxis != null || translation != null;
        writer.WriteBoolean(transformed);
        if (!transformed)
        {
            return;
        }

        writer.WriteUnsignedInteger(PrcTypes.TextureTransformation);
        writer.WriteBoolean(false);
        writer.WriteBoolean(false);
        writer.WriteBoolean(true);
        writer.WriteCharacter((byte)((translation != null ? 0x01 : 0) | (xAxis != null ? 0x02 : 0) | (scale != 1 ? 0x08 : 0)));
        foreach (var value in (translation ?? []).Concat(xAxis ?? []))
        {
            writer.WriteDouble(value);
        }

        if (scale != 1)
        {
            writer.WriteDouble(scale);
        }
    }

    /// <summary>A generic material of the globals of the given colors' indices.</summary>
    public static void WriteMaterial(PrcTestBitWriter writer, string name, int diffuseColor)
    {
        writer.WriteUnsignedInteger(PrcTypes.Material);
        WriteContentPrcBase(writer, name, true);
        writer.WriteUnsignedInteger((uint)(diffuseColor * 3 + 1));
        writer.WriteUnsignedInteger((uint)(diffuseColor * 3 + 1));
        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger(0);
        writer.WriteDouble(0.5);
        writer.WriteDouble(1);
        writer.WriteDouble(1);
        writer.WriteDouble(1);
        writer.WriteDouble(1);
    }

    /// <summary>A texture application of the globals: a generic material, a texture definition, the next level (-1 for
    /// none) and the first set of texture coordinates.</summary>
    public static void WriteTextureApplication(PrcTestBitWriter writer, string name, int material, int definition, int next = -1)
    {
        writer.WriteUnsignedInteger(PrcTypes.TextureApplication);
        WriteContentPrcBase(writer, name, true);
        writer.WriteUnsignedInteger((uint)(material + 1));
        writer.WriteUnsignedInteger((uint)(definition + 1));
        writer.WriteUnsignedInteger((uint)(next + 1));
        writer.WriteUnsignedInteger(1);
    }

    /// <summary>A style of the globals drawn by material <paramref name="material"/>.</summary>
    public static Action<PrcTestBitWriter> CreateMaterialStyle(string name, int material) => writer =>
    {
        writer.WriteUnsignedInteger(PrcTypes.Style);
        WriteContentPrcBase(writer, name, true);
        writer.WriteDouble(0);
        writer.WriteBoolean(false);
        writer.WriteUnsignedInteger(0);
        writer.WriteBoolean(true);
        writer.WriteUnsignedInteger((uint)(material + 1));
        writer.WriteBoolean(false);
        writer.WriteBoolean(false);
        writer.WriteBoolean(false);
        writer.WriteBoolean(false);
    };

    /// <summary>A cartesian transformation of a translation and, when given, a rotation's x and y axes.</summary>
    public static Action<PrcTestBitWriter> CreateCartesian(double[] translation, double[] xAxis = null, double[] yAxis = null) => writer =>
    {
        writer.WriteUnsignedInteger(PrcTypes.CartesianTransformation);
        writer.WriteCharacter((byte)(0x01 | (xAxis != null ? 0x02 : 0)));
        foreach (var value in translation)
        {
            writer.WriteDouble(value);
        }

        if (xAxis != null)
        {
            foreach (var value in xAxis)
            {
                writer.WriteDouble(value);
            }

            foreach (var value in yAxis)
            {
                writer.WriteDouble(value);
            }
        }
    };

    /// <summary>A general transformation: sixteen coefficients, column by column.</summary>
    public static Action<PrcTestBitWriter> CreateGeneral(double[] columns) => writer =>
    {
        writer.WriteUnsignedInteger(PrcTypes.GeneralTransformation);
        foreach (var value in columns)
        {
            writer.WriteDouble(value);
        }
    };

    /// <summary>The content every representation item starts with.</summary>
    public static void WriteItemContent(PrcTestBitWriter writer, string name, int style, int tessellation, int coordinateSystem = -1,
        ushort behavior = 0x0001, uint uniqueId = AnyId, int layer = -1)
    {
        WriteBaseWithGraphics(writer, name, style, behavior, uniqueId, layer);
        writer.WriteUnsignedInteger((uint)(coordinateSystem + 1));
        writer.WriteUnsignedInteger((uint)(tessellation + 1));
    }

    public static void WritePolyBrep(PrcTestBitWriter writer, string name, int style, int tessellation, int coordinateSystem = -1,
        ushort behavior = 0x0001, uint uniqueId = AnyId, int layer = -1)
    {
        writer.WriteUnsignedInteger(PrcTypes.PolyBrepModel);
        WriteItemContent(writer, name, style, tessellation, coordinateSystem, behavior, uniqueId, layer);
        writer.WriteBoolean(true);
        WriteUserData(writer);
    }

    public static void WritePolyWire(PrcTestBitWriter writer, string name, int style, int tessellation)
    {
        writer.WriteUnsignedInteger(PrcTypes.PolyWire);
        WriteItemContent(writer, name, style, tessellation);
        WriteUserData(writer);
    }

    public static void WriteBrepModel(PrcTestBitWriter writer, string name, int context, int body)
    {
        writer.WriteUnsignedInteger(PrcTypes.BrepModel);
        WriteItemContent(writer, name, -1, -1);
        writer.WriteBoolean(true);
        writer.WriteUnsignedInteger((uint)context);
        writer.WriteUnsignedInteger((uint)body);
        writer.WriteBoolean(true);
        WriteUserData(writer);
    }

    public static void WritePointSet(PrcTestBitWriter writer, string name, IReadOnlyList<double[]> points)
    {
        writer.WriteUnsignedInteger(PrcTypes.PointSet);
        WriteItemContent(writer, name, -1, -1);
        writer.WriteUnsignedInteger((uint)points.Count);
        foreach (var point in points)
        {
            foreach (var value in point)
            {
                writer.WriteDouble(value);
            }
        }

        WriteUserData(writer);
    }

    public static void WriteSetStart(PrcTestBitWriter writer, string name, int count)
    {
        writer.WriteUnsignedInteger(PrcTypes.Set);
        WriteItemContent(writer, name, -1, -1);
        writer.WriteUnsignedInteger((uint)count);
    }

    public static void WriteSetEnd(PrcTestBitWriter writer) => WriteUserData(writer);

    public static void WriteCoordinateSystem(PrcTestBitWriter writer, Action<PrcTestBitWriter> transformation)
    {
        writer.WriteUnsignedInteger(PrcTypes.CoordinateSystem);
        WriteItemContent(writer, "System", -1, -1);
        transformation(writer);
        WriteUserData(writer);
    }

    /// <summary>A tessellation section of the entries the writers give.</summary>
    public static byte[] CreateTessellations(IReadOnlyList<Action<PrcTestBitWriter>> entries, PrcTestBitWriter writer = null)
    {
        writer ??= new PrcTestBitWriter();
        writer.WriteUnsignedInteger(PrcTypes.FileStructureTessellation);
        WriteContentPrcBase(writer, null, false);
        writer.WriteUnsignedInteger((uint)entries.Count);
        foreach (var entry in entries)
        {
            entry(writer);
        }

        WriteUserData(writer);
        return writer.ToArray();
    }

    public static void WriteDoubles(PrcTestBitWriter writer, IReadOnlyList<double> values)
    {
        writer.WriteUnsignedInteger((uint)values.Count);
        foreach (var value in values)
        {
            writer.WriteDouble(value);
        }
    }

    public static void WriteUnsignedIntegers(PrcTestBitWriter writer, IReadOnlyList<uint> values)
    {
        writer.WriteUnsignedInteger((uint)values.Count);
        foreach (var value in values)
        {
            writer.WriteUnsignedInteger(value);
        }
    }

    /// <summary>A regular 3D tessellation of one face per entry of <paramref name="faces"/>: with normals, or, when they
    /// are null, normals to be recalculated by <paramref name="creaseAngle"/> in degrees.</summary>
    public static Action<PrcTestBitWriter> CreateTessellation3D(IReadOnlyList<double> coordinates, IReadOnlyList<double> normals,
        IReadOnlyList<uint> triangulated, IReadOnlyList<PrcTestFace> faces, IReadOnlyList<double> textures = null,
        double creaseAngle = 0.5) => writer =>
    {
        writer.WriteUnsignedInteger(PrcTypes.Tessellation3D);
        writer.WriteBoolean(false);
        WriteDoubles(writer, coordinates);
        writer.WriteBoolean(true);
        writer.WriteBoolean(false);
        writer.WriteBoolean(normals == null);
        if (normals == null)
        {
            writer.WriteCharacter(0);
            writer.WriteDouble(creaseAngle);
        }

        WriteDoubles(writer, normals ?? []);
        WriteUnsignedIntegers(writer, []);
        WriteUnsignedIntegers(writer, triangulated);
        writer.WriteUnsignedInteger((uint)faces.Count);
        foreach (var face in faces)
        {
            writer.WriteUnsignedInteger(PrcTypes.TessellationFace);
            WriteUnsignedIntegers(writer, face.Style >= 0 ? [(uint)(face.Style + 1)] : []);
            writer.WriteUnsignedInteger(0);
            WriteUnsignedIntegers(writer, []);
            writer.WriteUnsignedInteger(face.UsedEntities);
            writer.WriteUnsignedInteger(face.Start);
            WriteUnsignedIntegers(writer, face.Sizes);
            writer.WriteUnsignedInteger(face.TextureIndexes);
            writer.WriteBoolean(face.Colors != null);
            if (face.Colors != null)
            {
                writer.WriteBoolean(false);
                writer.WriteBoolean(false);
                writer.WriteBoolean(false);
                for (var color = 0; color < face.Colors.Count; color++)
                {
                    if (color > 0)
                    {
                        writer.WriteBoolean(false);
                    }

                    foreach (var component in face.Colors[color])
                    {
                        writer.WriteCharacter(component);
                    }
                }
            }

            if (face.Style >= 0)
            {
                writer.WriteUnsignedInteger(1);
            }
        }

        WriteDoubles(writer, textures ?? []);
    };

    public static Action<PrcTestBitWriter> CreateWire(IReadOnlyList<double> coordinates, IReadOnlyList<uint> wires) => writer =>
    {
        writer.WriteUnsignedInteger(PrcTypes.Tessellation3DWire);
        writer.WriteBoolean(false);
        WriteDoubles(writer, coordinates);
        WriteUnsignedIntegers(writer, wires);
        writer.WriteBoolean(false);
    };

    public static Action<PrcTestBitWriter> CreateMarkup(IReadOnlyList<double> coordinates, IReadOnlyList<uint> codes, string label) => writer =>
    {
        writer.WriteUnsignedInteger(PrcTypes.TessellationMarkup);
        writer.WriteBoolean(false);
        WriteDoubles(writer, coordinates);
        WriteUnsignedIntegers(writer, codes);
        writer.WriteUnsignedInteger(1);
        writer.WriteString("Ø 12");
        writer.WriteString(label);
        writer.WriteCharacter(0);
    };

    /// <summary>A markup of the part or product, drawn by markup tessellation <paramref name="tessellation"/>.</summary>
    public static Action<PrcTestBitWriter> CreateMarkups(string name, int tessellation) => writer =>
    {
        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger(1);
        writer.WriteUnsignedInteger(PrcTypes.MarkupItem);
        WriteBaseWithGraphics(writer, name, 0);
        writer.WriteUnsignedInteger(2);
        writer.WriteUnsignedInteger(1);
        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger(0);
        writer.WriteUnsignedInteger((uint)(tessellation + 1));
        WriteUserData(writer);
        writer.WriteUnsignedInteger(0);
    };

    public static byte[] CreateEmptyGeometry(uint type)
    {
        var writer = new PrcTestBitWriter();
        writer.WriteUnsignedInteger(type);
        WriteContentPrcBase(writer, null, false);
        writer.WriteUnsignedInteger(0);
        WriteUserData(writer);
        return writer.ToArray();
    }

    public static byte[] CreateModelFile(uint[] structure, int root, double unit, IReadOnlyList<PrcTestSchema> schemas = null,
        bool unitFromCad = true)
    {
        var writer = new PrcTestBitWriter();
        WriteSchema(writer, schemas ?? []);
        writer.WriteUnsignedInteger(PrcTypes.ModelFile);
        WriteContentPrcBase(writer, "Model", false);
        writer.WriteBoolean(unitFromCad);
        writer.WriteDouble(unit);
        writer.WriteUnsignedInteger(1);
        foreach (var part in structure)
        {
            writer.WriteUnsignedInteger(part);
        }

        writer.WriteUnsignedInteger((uint)(root + 1));
        writer.WriteBoolean(true);
        writer.WriteUnsignedInteger(0);
        WriteUserData(writer);
        return writer.ToArray();
    }
}

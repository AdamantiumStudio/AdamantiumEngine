using System;
using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>Reads the parts many PRC entities share, in one section: the base content and its current name, the
/// graphics and its current graphics, attributes, user data, references, transformations, cameras, lights, views and
/// filters - and runs the schema at the end of each type to step over what newer versions added. The current name and
/// graphics start afresh with each section, as the specification resets them when a section is flushed.</summary>
internal sealed class PrcEntityReader
{
    private const int BytesOfUniqueId = 4;

    private readonly PrcNotes notes;
    private string currentName;
    private PrcGraphics currentGraphics = PrcGraphics.Default;

    internal PrcEntityReader(PrcBitReader bits, PrcSchema schema, PrcNotes notes)
    {
        Bits = bits;
        Schema = schema;
        this.notes = notes;
    }

    internal PrcBitReader Bits { get; }

    internal PrcSchema Schema { get; }

    /// <summary>The authoring version of what is read, which decides fields later versions added.</summary>
    internal uint AuthoringVersion { get; init; } = PrcSchema.ReaderVersion;

    internal void Tell(string note) => notes.Tell(note);

    /// <summary>Steps over what versions newer than this reader added to <paramref name="type"/>.</summary>
    internal void EndOf(uint type) => Schema.SkipNewData(Bits, type);

    /// <summary>Reads the type code of the next entity and checks it is <paramref name="expected"/>.</summary>
    internal void Expect(uint expected, string what)
    {
        var type = Bits.ReadUnsignedInteger();
        if (type != expected)
        {
            throw Bits.Corrupt($"{what} has type {type} where type {expected} belongs");
        }
    }

    /// <summary>ContentPRCBase: the attributes, the name, and for a type that can be referenced the CAD and PRC
    /// identifiers.</summary>
    internal string ReadContentPrcBase(uint type, Dictionary<string, string> attributes = null) =>
        ReadContentPrcBase(PrcTypes.IsReferenceable(type), attributes, null);

    /// <summary>ContentPRCBase into <paramref name="entity"/>: its attributes, its name, and for a type that can be
    /// referenced its PRC unique identifier.</summary>
    internal void ReadContentPrcBase(PrcTreeEntity entity) =>
        entity.Name = ReadContentPrcBase(PrcTypes.IsReferenceable(entity.Type), entity.Attributes, entity);

    private string ReadContentPrcBase(bool referenceable, Dictionary<string, string> attributes, PrcTreeEntity into)
    {
        ReadAttributes(attributes);
        var name = ReadName();
        if (referenceable)
        {
            Bits.ReadUnsignedInteger();
            Bits.ReadUnsignedInteger();
            var uniqueId = Bits.ReadUnsignedInteger();
            if (into != null)
            {
                into.UniqueId = uniqueId;
            }
        }

        EndOf(PrcTypes.PrcBase);
        return name;
    }

    /// <summary>PRC_TYPE_ROOT_PRCBaseWithGraphics: the base content of an entity that can be referenced - every entity
    /// with graphics can - then the graphics or the current ones.</summary>
    internal void ReadPrcBaseWithGraphics(PrcTreeEntity entity)
    {
        entity.Name = ReadContentPrcBase(true, entity.Attributes, entity);
        entity.Graphics = ReadGraphics();
    }

    /// <summary>The base content and graphics of an entity whose content is not kept.</summary>
    internal void SkipPrcBaseWithGraphics()
    {
        ReadContentPrcBase(true, null, null);
        ReadGraphics();
    }

    /// <summary>A Name: a flag saying it is the current name, else the name, which becomes the current one.</summary>
    internal string ReadName()
    {
        if (Bits.ReadBoolean())
        {
            return currentName;
        }

        currentName = Bits.ReadString();
        return currentName;
    }

    /// <summary>The graphics: a flag saying they are the current ones, else a layer, a style and the behaviour bits.</summary>
    internal PrcGraphics ReadGraphics()
    {
        if (!Bits.ReadBoolean())
        {
            var layer = (int)Bits.ReadUnsignedInteger() - 1;
            var style = (int)Bits.ReadUnsignedInteger() - 1;
            var low = Bits.ReadCharacter();
            var high = Bits.ReadCharacter();
            currentGraphics = new PrcGraphics { LayerIndex = layer, StyleIndex = style, Behavior = (ushort)(low | (high << 8)) };
        }

        EndOf(PrcTypes.PrcBaseWithGraphics);
        return currentGraphics;
    }

    /// <summary>AttributeData: a count of PRC_TYPE_MISC_Attribute, each a title and key and value pairs. The values
    /// are kept as text, by their keys, prefixed with the title.</summary>
    internal void ReadAttributes(Dictionary<string, string> into)
    {
        var count = Bits.ReadCount(3, "attributes");
        for (var attribute = 0; attribute < count; attribute++)
        {
            Expect(PrcTypes.Attribute, "an attribute");
            var title = ReadAttributeTitle();
            var pairs = Bits.ReadCount(3, "attribute keys");
            for (var pair = 0; pair < pairs; pair++)
            {
                var key = ReadAttributeTitle();
                var kind = Bits.ReadUnsignedInteger();
                var value = kind switch
                {
                    1 => Bits.ReadInteger().ToString(System.Globalization.CultureInfo.InvariantCulture),
                    2 => Bits.ReadDouble().ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    3 => Bits.ReadUnsignedInteger().ToString(System.Globalization.CultureInfo.InvariantCulture),
                    4 => Bits.ReadString() ?? string.Empty,
                    _ => null
                };
                if (into != null && value != null)
                {
                    into[string.IsNullOrEmpty(title) ? key : $"{title}/{key}"] = value;
                }
            }

            EndOf(PrcTypes.Attribute);
        }
    }

    private string ReadAttributeTitle()
    {
        if (Bits.ReadBoolean())
        {
            return "#" + Bits.ReadUnsignedInteger().ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return Bits.ReadString() ?? string.Empty;
    }

    /// <summary>UserData: a count of bits and the bits, which only their own application reads.</summary>
    internal void SkipUserData()
    {
        var bits = Bits.ReadUnsignedInteger();
        Bits.CheckCount(bits, 1, "bits of user data");
        Bits.Skip(bits);
    }

    /// <summary>Tells when data is left after the end of the section, which a misread would leave.</summary>
    internal void CheckEnd()
    {
        if (!Bits.IsAtPadding)
        {
            Tell($"{Bits.RemainingBits} bits are left after the end of the PRC {Bits.Section}; they are not read");
        }
    }

    /// <summary>A CompressedUniqueId: four UnsignedIntegers.</summary>
    internal PrcUniqueId ReadCompressedUniqueId()
    {
        Bits.CheckCount(BytesOfUniqueId, 1, "parts of a unique identifier");
        return new PrcUniqueId(Bits.ReadUnsignedInteger(), Bits.ReadUnsignedInteger(), Bits.ReadUnsignedInteger(),
            Bits.ReadUnsignedInteger());
    }

    internal Vector3<double> ReadVector3d() => new(Bits.ReadDouble(), Bits.ReadDouble(), Bits.ReadDouble());

    internal void SkipDoubles(int count)
    {
        for (var index = 0; index < count; index++)
        {
            Bits.ReadDouble();
        }
    }

    /// <summary>A transformation entity: PRC_TYPE_MISC_CartesianTransformation or
    /// PRC_TYPE_MISC_GeneralTransformation with its type code, as a row-vector matrix.</summary>
    internal Matrix4x4F ReadTransformationEntity()
    {
        var type = Bits.ReadUnsignedInteger();
        switch (type)
        {
            case PrcTypes.CartesianTransformation:
                var cartesian = ReadTransformation3d();
                EndOf(type);
                return cartesian;
            case PrcTypes.GeneralTransformation:
                var values = new float[16];
                for (var index = 0; index < values.Length; index++)
                {
                    values[index] = (float)Bits.ReadDouble();
                }

                EndOf(type);
                return new Matrix4x4F(values);
            default:
                throw Bits.Corrupt($"a transformation has type {type}");
        }
    }

    /// <summary>A 3D Transformation: its behaviour bits, then the parts they name - translation, rotation or non
    /// orthogonal axes, scale, homogeneous part - as a row-vector matrix (each PRC column a row).</summary>
    internal Matrix4x4F ReadTransformation3d()
    {
        const int translate = 0x01;
        const int rotate = 0x02;
        const int mirror = 0x04;
        const int scale = 0x08;
        const int nonUniformScale = 0x10;
        const int nonOrthogonal = 0x20;
        const int homogeneous = 0x40;

        var behavior = Bits.ReadCharacter();
        var origin = Vector3<double>.Zero;
        var xAxis = new Vector3<double>(1, 0, 0);
        var yAxis = new Vector3<double>(0, 1, 0);
        var zAxis = new Vector3<double>(0, 0, 1);
        if ((behavior & translate) != 0)
        {
            origin = ReadVector3d();
        }

        if ((behavior & nonOrthogonal) != 0)
        {
            xAxis = ReadVector3d();
            yAxis = ReadVector3d();
            zAxis = ReadVector3d();
        }
        else if ((behavior & rotate) != 0)
        {
            xAxis = ReadVector3d();
            yAxis = ReadVector3d();
            zAxis = (behavior & mirror) != 0 ? Vector3<double>.Cross(yAxis, xAxis) : Vector3<double>.Cross(xAxis, yAxis);
        }

        var scales = Vector3<double>.One;
        if ((behavior & nonUniformScale) != 0)
        {
            scales = ReadVector3d();
        }
        else if ((behavior & scale) != 0)
        {
            var uniform = Bits.ReadDouble();
            scales = new Vector3<double>(uniform, uniform, uniform);
        }

        var last = new Vector4<double>(0, 0, 0, 1);
        if ((behavior & homogeneous) != 0)
        {
            last = new Vector4<double>(Bits.ReadDouble(), Bits.ReadDouble(), Bits.ReadDouble(), Bits.ReadDouble());
        }

        if ((behavior & ~0x7F) != 0)
        {
            Tell($"a transformation has behaviour bits 0x{behavior:X2} past those of the specification; they are left out");
        }

        return new Matrix4x4F(
            (float)(xAxis.X * scales.X), (float)(xAxis.Y * scales.X), (float)(xAxis.Z * scales.X), (float)last.X,
            (float)(yAxis.X * scales.Y), (float)(yAxis.Y * scales.Y), (float)(yAxis.Z * scales.Y), (float)last.Y,
            (float)(zAxis.X * scales.Z), (float)(zAxis.Y * scales.Z), (float)(zAxis.Z * scales.Z), (float)last.Z,
            (float)origin.X, (float)origin.Y, (float)origin.Z, (float)last.W);
    }

    /// <summary>A reference to an entity by its unique identifier (ReferenceUniqueIdentifier, the content of
    /// PRC_TYPE_MISC_ReferenceOnPRCBase): the target's type, whether it is in this file structure, the file structure
    /// it is in when not, its identifier there.</summary>
    internal PrcReferenceTarget ReadReferenceOnPrcBaseContent()
    {
        var type = Bits.ReadUnsignedInteger();
        var fileStructure = Bits.ReadBoolean() ? null : ReadCompressedUniqueId();
        var target = new PrcReferenceTarget { Type = type, FileStructure = fileStructure, UniqueId = Bits.ReadUnsignedInteger() };
        EndOf(PrcTypes.ReferenceOnPrcBase);
        return target;
    }

    /// <summary>A ReferenceUniqueIdentifier with its type code.</summary>
    internal PrcReferenceTarget ReadReferenceUniqueIdentifier()
    {
        Expect(PrcTypes.ReferenceOnPrcBase, "a reference");
        return ReadReferenceOnPrcBaseContent();
    }

    /// <summary>The content of PRC_TYPE_MISC_ReferenceOnTopology: the topological type, and when the target has a body
    /// the file structure, context, body and indices that locate it.</summary>
    internal PrcReferenceTarget ReadReferenceOnTopologyContent()
    {
        var type = Bits.ReadUnsignedInteger();
        PrcUniqueId fileStructure = null;
        if (Bits.ReadBoolean())
        {
            if (!Bits.ReadBoolean())
            {
                fileStructure = ReadCompressedUniqueId();
            }

            Bits.ReadUnsignedInteger();
            Bits.ReadUnsignedInteger();
            var indices = Bits.ReadCount(1, "indices of a topological reference");
            for (var index = 0; index < indices; index++)
            {
                Bits.ReadUnsignedInteger();
            }
        }

        EndOf(PrcTypes.ReferenceOnTopology);
        return new PrcReferenceTarget { Type = type, FileStructure = fileStructure, IsTopology = true };
    }

    /// <summary>ReferenceData: a reference on a PRC base or on topology, with its type code; null for one of a type this
    /// reader does not know.</summary>
    internal PrcReferenceTarget ReadReferenceData()
    {
        var type = Bits.ReadUnsignedInteger();
        switch (type)
        {
            case PrcTypes.ReferenceOnPrcBase:
                return ReadReferenceOnPrcBaseContent();
            case PrcTypes.ReferenceOnTopology:
                return ReadReferenceOnTopologyContent();
            default:
                SkipUnknown(type, "a reference");
                return null;
        }
    }

    /// <summary>ContentEntityReference: base content and graphics, a local coordinate system - stored plus one, as the
    /// file's other indices are - and the reference.</summary>
    internal PrcEntityReference ReadContentEntityReference(uint type)
    {
        var holder = new PrcTreeEntity { Type = type };
        ReadPrcBaseWithGraphics(holder);
        var coordinateSystem = (int)Bits.ReadUnsignedInteger() - 1;
        var target = Bits.ReadBoolean() ? ReadReferenceData() : null;
        var reference = new PrcEntityReference
        {
            Type = type,
            Name = holder.Name,
            Graphics = holder.Graphics,
            UniqueId = holder.UniqueId,
            CoordinateSystemIndex = coordinateSystem,
            Target = target
        };
        foreach (var attribute in holder.Attributes)
        {
            reference.Attributes[attribute.Key] = attribute.Value;
        }

        return reference;
    }

    /// <summary>A PRC_TYPE_MISC_EntityReference with its type code.</summary>
    internal PrcEntityReference ReadEntityReference()
    {
        Expect(PrcTypes.EntityReference, "an entity reference");
        var reference = ReadContentEntityReference(PrcTypes.EntityReference);
        EndOf(PrcTypes.EntityReference);
        SkipUserData();
        return reference;
    }

    /// <summary>An entity of a type this reader does not read here, stepped over by the file's schema.</summary>
    internal void SkipUnknown(uint type, string what)
    {
        Tell($"{what} of type {type}, which this reader does not know, is stepped over by the file's schema");
        Schema.SkipEntity(Bits, type);
    }

    /// <summary>A PRC_TYPE_ASM_Filter with its type code: its base content, whether it is active, its layer filter
    /// and its entity filter.</summary>
    internal PrcFilter ReadFilter()
    {
        Expect(PrcTypes.Filter, "a filter");
        var holder = new PrcTreeEntity { Type = PrcTypes.Filter };
        ReadContentPrcBase(holder);
        var isActive = Bits.ReadBoolean();
        var layersInclusive = Bits.ReadBoolean();
        var layers = new List<uint>(Bits.ReadCount(1, "layers of a filter"));
        for (var layer = 0; layer < layers.Capacity; layer++)
        {
            layers.Add(Bits.ReadUnsignedInteger());
        }

        var entitiesInclusive = Bits.ReadBoolean();
        var references = new List<PrcEntityReference>(Bits.ReadCount(8, "entities of a filter"));
        for (var entity = 0; entity < references.Capacity; entity++)
        {
            references.Add(ReadEntityReference());
        }

        EndOf(PrcTypes.Filter);
        SkipUserData();
        var filter = new PrcFilter
        {
            Type = PrcTypes.Filter,
            Name = holder.Name,
            UniqueId = holder.UniqueId,
            IsActive = isActive,
            LayersInclusive = layersInclusive,
            EntitiesInclusive = entitiesInclusive
        };
        filter.Layers.AddRange(layers);
        filter.Entities.AddRange(references);
        return filter;
    }

    /// <summary>A light of a scene with its type code: its base content, its ambient, diffuse and specular colours -
    /// three, not the four of the specification's tables, as the errata have it - and what its kind adds; a directional
    /// light's intensity from authoring version 8030. Null for a type this reader does not know.</summary>
    internal PrcLight ReadLight()
    {
        const uint directionalIntensityVersion = 8030;

        var type = Bits.ReadUnsignedInteger();
        if (type is < PrcTypes.AmbientLight or > PrcTypes.SpotLight)
        {
            SkipUnknown(type, "a light");
            return null;
        }

        var name = ReadContentPrcBase(type);
        var ambient = (int)Bits.ReadUnsignedInteger() - 1;
        var diffuse = (int)Bits.ReadUnsignedInteger() - 1;
        var specular = (int)Bits.ReadUnsignedInteger() - 1;
        var location = Vector3<double>.Zero;
        var direction = new Vector3<double>(0, 0, -1);
        var constant = 1d;
        var linear = 0d;
        var quadratic = 0d;
        var intensity = 1d;
        var angle = 0d;
        var exponent = 0d;
        switch (type)
        {
            case PrcTypes.PointLight:
                location = ReadVector3d();
                constant = Bits.ReadDouble();
                linear = Bits.ReadDouble();
                quadratic = Bits.ReadDouble();
                break;
            case PrcTypes.DirectionalLight:
                direction = ReadVector3d();
                if (AuthoringVersion >= directionalIntensityVersion)
                {
                    intensity = Bits.ReadDouble();
                }

                break;
            case PrcTypes.SpotLight:
                location = ReadVector3d();
                constant = Bits.ReadDouble();
                linear = Bits.ReadDouble();
                quadratic = Bits.ReadDouble();
                direction = ReadVector3d();
                angle = Bits.ReadDouble();
                exponent = Bits.ReadDouble();
                break;
        }

        EndOf(type);
        return new PrcLight
        {
            Type = type,
            Name = name,
            AmbientIndex = ambient,
            DiffuseIndex = diffuse,
            SpecularIndex = specular,
            Location = location,
            Direction = direction,
            ConstantAttenuation = constant,
            LinearAttenuation = linear,
            QuadraticAttenuation = quadratic,
            Intensity = intensity,
            FallOffAngle = angle,
            FallOffExponent = exponent
        };
    }

    /// <summary>A PRC_TYPE_GRAPH_Camera with its type code.</summary>
    internal PrcCamera ReadCamera()
    {
        Expect(PrcTypes.Camera, "a camera");
        var camera = new PrcCamera
        {
            Name = ReadContentPrcBase(PrcTypes.Camera),
            IsOrthographic = Bits.ReadBoolean(),
            Position = ReadVector3d(),
            LookAt = ReadVector3d(),
            Up = ReadVector3d(),
            FieldOfViewX = Bits.ReadDouble(),
            FieldOfViewY = Bits.ReadDouble(),
            Aspect = Bits.ReadDouble(),
            Near = Bits.ReadDouble(),
            Far = Bits.ReadDouble(),
            Zoom = Bits.ReadDouble()
        };
        EndOf(PrcTypes.Camera);
        return camera;
    }

    /// <summary>A PRC_TYPE_SURF_Plane with its type code, as views and clipping planes have it: its content, its
    /// transformation, its domain and parameterization.</summary>
    internal void SkipPlaneSurface()
    {
        var type = Bits.ReadUnsignedInteger();
        if (type != PrcTypes.SurfacePlane)
        {
            SkipUnknown(type, "a surface");
            return;
        }

        if (Bits.ReadBoolean())
        {
            ReadAttributes(null);
            ReadName();
            Bits.ReadUnsignedInteger();
        }

        Bits.ReadUnsignedInteger();
        ReadTransformation3d();
        SkipDoubles(4 + 4);
        EndOf(PrcTypes.SurfacePlane);
    }

    /// <summary>A PRC_TYPE_GRAPH_SceneDisplayParameters with its type code.</summary>
    internal PrcSceneDisplayParameters ReadSceneDisplayParameters()
    {
        Expect(PrcTypes.SceneDisplayParameters, "scene display parameters");
        var scene = new PrcSceneDisplayParameters
        {
            Name = ReadContentPrcBase(PrcTypes.SceneDisplayParameters),
            IsActive = Bits.ReadBoolean()
        };
        var lights = Bits.ReadCount(4, "lights");
        for (var index = 0; index < lights; index++)
        {
            if (ReadLight() is { } light)
            {
                scene.Lights.Add(light);
            }
        }

        if (Bits.ReadBoolean())
        {
            scene.Camera = ReadCamera();
        }

        if (Bits.ReadBoolean())
        {
            scene.RotationCenter = ReadVector3d();
        }

        scene.ClippingPlaneCount = Bits.ReadCount(8, "clipping planes");
        for (var plane = 0; plane < scene.ClippingPlaneCount; plane++)
        {
            SkipPlaneSurface();
        }

        scene.BackgroundStyleIndex = (int)Bits.ReadUnsignedInteger() - 1;
        Bits.ReadUnsignedInteger();
        var defaults = Bits.ReadCount(2, "default styles");
        for (var style = 0; style < defaults; style++)
        {
            Bits.ReadUnsignedInteger();
            Bits.ReadUnsignedInteger();
        }

        scene.IsAbsolute = Bits.ReadBoolean();
        EndOf(PrcTypes.SceneDisplayParameters);
        return scene;
    }

    /// <summary>A PRC_TYPE_MKP_View with its type code: the annotations it groups, its plane, its scene, and the
    /// linked items and filters version 8016 added.</summary>
    internal PrcView ReadView()
    {
        Expect(PrcTypes.View, "a view");
        var view = new PrcView { Type = PrcTypes.View };
        ReadPrcBaseWithGraphics(view);
        var annotations = Bits.ReadCount(4, "annotations of a view");
        for (var annotation = 0; annotation < annotations; annotation++)
        {
            view.Annotations.Add(ReadReferenceUniqueIdentifier());
        }

        SkipPlaneSurface();
        if (Bits.ReadBoolean())
        {
            view.Scene = ReadSceneDisplayParameters();
        }

        view.IsAnnotationView = Bits.ReadBoolean();
        view.IsDefault = Bits.ReadBoolean();
        Bits.ReadBoolean();
        var linked = Bits.ReadCount(4, "linked items of a view");
        for (var item = 0; item < linked; item++)
        {
            view.LinkedItems.Add(ReadReferenceUniqueIdentifier());
        }

        var filters = Bits.ReadCount(4, "filters of a view");
        for (var filter = 0; filter < filters; filter++)
        {
            view.Filters.Add(ReadFilter());
        }

        EndOf(PrcTypes.View);
        SkipUserData();
        return view;
    }
}

/// <summary>What a read found that the import leaves out, each said once.</summary>
internal sealed class PrcNotes
{
    private readonly HashSet<string> told = new(StringComparer.Ordinal);

    internal List<string> All { get; } = [];

    internal void Tell(string note)
    {
        if (told.Add(note))
        {
            All.Add(note);
        }
    }
}

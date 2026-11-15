using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Adamantium.Engine.Compiler.Converter.Configs;
using Adamantium.Engine.Compiler.Converter.Converters;
using Adamantium.Engine.Compiler.Models.Prc;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.Converters;

/// <summary>Imports a PRC file (PRC Format Specification 8137) - a <c>.prc</c> file, or the 3D artwork of a PDF read from
/// its bytes, as CAD systems write it. The model file's root product occurrences become the model tree, each occurrence
/// a node placed by its location, its sons, prototype and external data under it, its part's representation items -
/// sets nested, items placed by their coordinate systems - as nodes with the meshes of their tessellations: a regular 3D
/// tessellation's triangles, fans and stripes, a wire tessellation's polylines, a point set's points. Markups become line
/// and triangle meshes from their markup tessellations, under a node of their own. A mesh is drawn by the material of its
/// style - a plain colour, or a material of the globals - inherited down the tree when an entity has none; a colour index
/// of a style or a material names the colour's first component in the globals' colour array, so it is three times the
/// colour's place in it. PRC is Z up
/// and right-handed; its unit is the model file's, in millimetres. What is not read - highly compressed tessellation,
/// the exact geometry of B-rep bodies, hidden or removed entities - is told in <see cref="ConverterBase.UnsupportedFeatures"/>.</summary>
public class PrcConverter : ConverterBase
{
    private const int MaximumNodeAppearances = 1_000_000;
    private const int MaximumPrototypeChain = 64;
    private const float DefaultGrey = 0.75f;
    private const string DefaultMaterialId = "default";
    private const int NamesShown = 5;

    private readonly HashSet<string> told = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> appearancesOf = new(StringComparer.Ordinal);
    private readonly PrcNotes meshNotes = new();
    private readonly Dictionary<string, List<string>> grouped = new(StringComparer.Ordinal);
    private PrcFile file;
    private int appearances;

    public PrcConverter(string filePath, ConversionConfig config) : base(filePath, config, UpAxis.Z_UP)
    {
    }

    /// <summary>A converter of a PRC model held in memory, known by <paramref name="fileName"/>.</summary>
    public PrcConverter(string fileName, byte[] bytes, ConversionConfig config) : base(fileName, bytes, config, UpAxis.Z_UP)
    {
        ArgumentNullException.ThrowIfNull(bytes);
    }

    protected override void Convert()
    {
        file = PrcFile.Read(ReadFileBytes(), CancellationToken);
        foreach (var note in file.Notes)
        {
            Tell(note);
        }

        if (file.Unit > 0)
        {
            SceneDataContainer.Units = new SceneData.Unit(UnitType.Meter, (float)(file.Unit / 1000));
        }

        if (!file.UnitFromCad)
        {
            Tell("the file's unit does not come from a CAD system; it is not to be measured by");
        }

        SceneDataContainer.Models = SceneDataContainer.CreateMesh(null, "", Path.GetFileNameWithoutExtension(FileName));
        BuildTree();
        TellGrouped();
        foreach (var note in meshNotes.All)
        {
            Tell(note);
        }
    }

    private void Tell(string feature)
    {
        if (told.Add(feature))
        {
            UnsupportedFeatures.Add(feature);
        }
    }

    private void TellOf(string feature, string name)
    {
        if (!grouped.TryGetValue(feature, out var names))
        {
            names = [];
            grouped.Add(feature, names);
        }

        names.Add(name);
    }

    private void TellGrouped()
    {
        foreach (var group in grouped)
        {
            var shown = string.Join(", ", group.Value.Take(NamesShown));
            var more = group.Value.Count > NamesShown ? $" and {group.Value.Count - NamesShown} more" : string.Empty;
            Tell($"{group.Value.Count} {group.Key}: {shown}{more}");
        }
    }

    private void BuildTree()
    {
        var pending = new Stack<PrcPendingNode>();
        for (var index = file.Roots.Count - 1; index >= 0; index--)
        {
            var root = file.Roots[index];
            var structure = file.FileStructures.FirstOrDefault(candidate => candidate.Id.Equals(root.FileStructure));
            if (structure == null)
            {
                Tell($"a root product occurrence lies in file structure {root.FileStructure}, which the file does not hold");
                continue;
            }

            pending.Push(PrcPendingNode.ForOccurrence(structure, root.Occurrence, SceneDataContainer.Models, null, null));
        }

        while (pending.Count > 0)
        {
            CancellationToken.ThrowIfCancellationRequested();
            var node = pending.Pop();
            if (++appearances > MaximumNodeAppearances)
            {
                Tell($"the tree shows nodes more than {MaximumNodeAppearances} times over; the rest are left out");
                return;
            }

            if (node.Item == null)
            {
                ExpandOccurrence(node, pending);
            }
            else
            {
                ExpandItem(node, pending);
            }
        }
    }

    private void ExpandOccurrence(PrcPendingNode node, Stack<PrcPendingNode> pending)
    {
        var structure = node.Structure;
        if (node.Occurrence < 0 || node.Occurrence >= structure.Occurrences.Count)
        {
            Tell($"file structure {structure.Index} names product occurrence {node.Occurrence} of its {structure.Occurrences.Count}");
            return;
        }

        var occurrence = structure.Occurrences[node.Occurrence];
        var name = string.IsNullOrEmpty(occurrence.Name) ? $"Product {node.Occurrence}" : occurrence.Name;
        if (node.HasAncestor(structure, node.Occurrence))
        {
            Tell($"product occurrence {name} is its own ancestor - the assembly has a cycle - and is left out there");
            return;
        }

        if (!Shown(occurrence, name) || (occurrence.Behavior & PrcProductOccurrence.Suppressed) != 0)
        {
            if ((occurrence.Behavior & PrcProductOccurrence.Suppressed) != 0)
            {
                Tell($"product occurrence {name} is suppressed; it is left out");
            }

            return;
        }

        var model = SceneDataContainer.CreateMesh(node.Parent, AppearanceId($"{structure.Index}/{node.Occurrence}"), name);
        model.Extra = ExtraOf(occurrence.Attributes);
        Place(model, occurrence.Location ?? Matrix4x4F.Identity);
        var style = StyleFrom(structure, occurrence.Graphics, node.Style);
        ReportOccurrenceContent(occurrence, name);

        var children = new List<PrcPendingNode>();
        var source = occurrence;
        var sourceStructure = structure;
        var sons = occurrence.Sons;
        var sonsStructure = structure;
        var part = occurrence.PartIndex;
        var partStructure = structure;
        for (var link = 0; link < MaximumPrototypeChain && source.PrototypeIndex >= 0; link++)
        {
            var prototypeStructure = Resolve(sourceStructure, source.PrototypeFileStructure, name, "prototype");
            if (prototypeStructure == null || source.PrototypeIndex >= prototypeStructure.Occurrences.Count)
            {
                Tell($"the prototype of product occurrence {name} is not in the file");
                break;
            }

            var prototype = prototypeStructure.Occurrences[source.PrototypeIndex];
            if (sons.Count == 0 && prototype.Sons.Count > 0)
            {
                sons = prototype.Sons;
                sonsStructure = prototypeStructure;
            }

            if (part < 0 && prototype.PartIndex >= 0)
            {
                part = prototype.PartIndex;
                partStructure = prototypeStructure;
            }

            source = prototype;
            sourceStructure = prototypeStructure;
        }

        if (part >= 0)
        {
            if (part < partStructure.Parts.Count)
            {
                AddPart(model, partStructure, partStructure.Parts[part], style, children, node);
            }
            else
            {
                Tell($"product occurrence {name} shows part {part} of the {partStructure.Parts.Count} of its file structure");
            }
        }

        AddMarkups(model, structure, occurrence.Markups, style, model.ID);
        foreach (var son in sons)
        {
            children.Add(PrcPendingNode.ForOccurrence(sonsStructure, son, model, style, node));
        }

        if (occurrence.ExternalIndex >= 0)
        {
            var external = Resolve(structure, occurrence.ExternalFileStructure, name, "external data");
            if (external != null)
            {
                children.Add(PrcPendingNode.ForOccurrence(external, occurrence.ExternalIndex, model, style, node));
            }
        }

        for (var index = children.Count - 1; index >= 0; index--)
        {
            pending.Push(children[index]);
        }
    }

    private void ReportOccurrenceContent(PrcProductOccurrence occurrence, string name)
    {
        if (occurrence.ViewCount > 0)
        {
            Tell("the views of the file - markup views and product views - are left out");
        }

        if (occurrence.SceneDisplayParameterCount > 0)
        {
            Tell("scene display parameters - lights, cameras, clipping planes - are left out");
        }

        if (occurrence.FilterCount > 0)
        {
            Tell("the display filters of product occurrences are left out; everything is shown");
        }

        if (occurrence.EntityReferenceCount > 0)
        {
            TellOf("product occurrences redefine entities of their sons by reference - their position, colour or visibility; " +
                   "the redefinitions are left out", name);
        }

        if (file.Unit > 0 && occurrence.Unit > 0 && Math.Abs(occurrence.Unit - file.Unit) > 1e-9 * file.Unit)
        {
            Tell($"product occurrences in a unit other than the model file's ({file.Unit} mm) are read in the model file's");
        }
    }

    private PrcFileStructure Resolve(PrcFileStructure from, PrcUniqueId id, string name, string what)
    {
        if (id == null)
        {
            return from;
        }

        var found = file.FileStructures.FirstOrDefault(candidate => candidate.Id.Equals(id));
        if (found == null)
        {
            Tell($"the {what} of product occurrence {name} lies in file structure {id}, which the file does not hold");
        }

        return found;
    }

    private void AddPart(SceneData.Model model, PrcFileStructure structure, PrcPartDefinition part, PrcStyleChoice style,
        List<PrcPendingNode> children, PrcPendingNode owner)
    {
        var name = string.IsNullOrEmpty(part.Name) ? model.Name : part.Name;
        if (!Shown(part, name))
        {
            return;
        }

        var partStyle = StyleFrom(structure, part.Graphics, style);
        foreach (var item in part.Items)
        {
            children.Add(PrcPendingNode.ForItem(structure, item, model, partStyle, owner));
        }

        if (part.ViewCount > 0)
        {
            Tell("the views of the file - markup views and product views - are left out");
        }

        AddMarkups(model, structure, part.Markups, partStyle, model.ID + "/part");
    }

    private void ExpandItem(PrcPendingNode node, Stack<PrcPendingNode> pending)
    {
        var item = node.Item;
        var structure = node.Structure;
        var name = string.IsNullOrEmpty(item.Name) ? KindOf(item.Type) : item.Name;
        if (!Shown(item, name))
        {
            return;
        }

        var model = SceneDataContainer.CreateMesh(node.Parent, AppearanceId($"{node.Parent.ID}/{name}"), name);
        model.Extra = ExtraOf(item.Attributes);
        if (item.Type == PrcTypes.CoordinateSystem)
        {
            Place(model, item.Transform);
        }
        else if (item.CoordinateSystemIndex >= 0)
        {
            if (item.CoordinateSystemIndex < structure.CoordinateSystems.Count)
            {
                Place(model, structure.CoordinateSystems[item.CoordinateSystemIndex].Transform);
            }
            else
            {
                Tell($"representation item {name} is placed by coordinate system {item.CoordinateSystemIndex}, which the globals do not hold");
            }
        }

        var style = StyleFrom(structure, item.Graphics, node.Style);
        AddItemMeshes(model, structure, item, name, style);
        for (var index = item.Items.Count - 1; index >= 0; index--)
        {
            pending.Push(PrcPendingNode.ForItem(structure, item.Items[index], model, style, node.Owner));
        }
    }

    private void AddItemMeshes(SceneData.Model model, PrcFileStructure structure, PrcRepresentationItem item, string name,
        PrcStyleChoice style)
    {
        if (item.Type == PrcTypes.PointSet && item.Points.Count > 0)
        {
            var mesh = new Mesh(PrimitiveType.PointList) { Name = name };
            mesh.SetPoints(item.Points).SetIndices(Enumerable.Range(0, item.Points.Count));
            mesh.MaterialID = MaterialOf(style);
            model.Meshes.Add(mesh);
            return;
        }

        if (item.Type is PrcTypes.Direction or PrcTypes.Plane && item.TessellationIndex < 0)
        {
            Tell("construction directions and planes are kept as nodes; they are not drawn");
        }

        if (item.TessellationIndex < 0)
        {
            if (item.ContextIndex >= 0)
            {
                Tell($"representation item {name} has exact geometry alone (a B-rep body, topological context {item.ContextIndex} " +
                     $"body {item.BodyIndex}) and no tessellation; it is not drawn");
            }

            return;
        }

        if (item.TessellationIndex >= structure.Tessellations.Count)
        {
            if (item.TessellationIndex < structure.DeclaredTessellationCount)
            {
                TellOf("representation items show a tessellation that is not read - highly compressed, or past one that is " +
                       "in its section - and are not drawn", name);
            }
            else
            {
                Tell($"representation item {name} shows tessellation {item.TessellationIndex}, which file structure {structure.Index} does not hold");
            }

            return;
        }

        switch (structure.Tessellations[item.TessellationIndex])
        {
            case Prc3DTessellation tessellation:
                if (tessellation.MustRecalculateNormals)
                {
                    Tell("normals a tessellation asks to be worked out by its crease angle are made flat");
                }

                var groups = PrcMeshBuilder.Build3D(tessellation, face => FaceMaterial(structure, face, style), meshNotes);
                foreach (var group in groups.OrderBy(entry => entry.Key, StringComparer.Ordinal))
                {
                    if (group.Value.Indices.Count == 0)
                    {
                        continue;
                    }

                    var mesh = ToMesh(group.Value, $"{name} {model.Meshes.Count}");
                    mesh.MaterialID = group.Key;
                    model.Meshes.Add(mesh);
                }

                if (tessellation.Faces.Count == 0 || groups.Values.All(group => group.Indices.Count == 0))
                {
                    Tell($"the tessellation of representation item {name} has no triangles");
                }

                break;
            case PrcWireTessellation wire:
                var lines = PrcMeshBuilder.BuildWire(wire);
                if (lines.Indices.Count > 0)
                {
                    var mesh = ToMesh(lines, name);
                    mesh.MaterialID = MaterialOf(style);
                    model.Meshes.Add(mesh);
                }

                break;
            case PrcMarkupTessellation markup:
                AddMarkupMeshes(model, structure, markup, style, name);
                break;
            default:
                Tell($"representation item {name} shows a tessellation of type {structure.Tessellations[item.TessellationIndex].Type}, which is not drawn");
                break;
        }
    }

    private void AddMarkups(SceneData.Model model, PrcFileStructure structure, PrcMarkupData data, PrcStyleChoice style, string id)
    {
        if (data == null || data.Markups.Count + data.Leaders.Count == 0)
        {
            return;
        }

        SceneData.Model holder = null;
        foreach (var markup in data.Markups.Concat(data.Leaders))
        {
            var name = string.IsNullOrEmpty(markup.Name) ? (markup.Type == PrcTypes.Leader ? "Leader" : "Markup") : markup.Name;
            if (!Shown(markup, name) || markup.TessellationIndex < 0)
            {
                continue;
            }

            if (markup.TessellationIndex >= structure.Tessellations.Count ||
                structure.Tessellations[markup.TessellationIndex] is not PrcMarkupTessellation tessellation)
            {
                Tell($"markup {name} shows tessellation {markup.TessellationIndex}, which is not a markup tessellation read from file structure {structure.Index}");
                continue;
            }

            holder ??= SceneDataContainer.CreateMesh(model, AppearanceId(id + "/markups"), "Markups");
            var node = SceneDataContainer.CreateMesh(holder, AppearanceId($"{holder.ID}/{name}"), name);
            AddMarkupMeshes(node, structure, tessellation, StyleFrom(structure, markup.Graphics, style), name);
        }
    }

    private void AddMarkupMeshes(SceneData.Model model, PrcFileStructure structure, PrcMarkupTessellation markup,
        PrcStyleChoice style, string name)
    {
        if ((markup.Behavior & 0x01) != 0)
        {
            Tell($"the tessellation of markup {name} is set to be hidden; it is left out");
            return;
        }

        var start = ColorOfStyle(style);
        var meshes = PrcMeshBuilder.BuildMarkup(markup, index => ColorAt(structure, index), start, meshNotes);
        var material = MarkupMaterial();
        foreach (var data in new[] { meshes.Lines, meshes.Triangles, meshes.Points })
        {
            if (data.Indices.Count == 0)
            {
                continue;
            }

            var mesh = ToMesh(data, $"{name} {model.Meshes.Count}");
            mesh.MaterialID = material;
            model.Meshes.Add(mesh);
        }
    }

    private static Mesh ToMesh(PrcMeshData data, string name)
    {
        var mesh = new Mesh(data.Topology) { Name = name };
        mesh.SetPoints(data.Points).SetIndices(data.Indices);
        if (data.HasNormals && data.Topology == PrimitiveType.TriangleList)
        {
            mesh.SetNormals(data.Normals);
        }

        if (data.HasColors)
        {
            mesh.SetColors(data.Colors);
        }

        if (data.HasTextureCoordinates)
        {
            mesh.SetUVs(0, data.TextureCoordinates);
        }

        return mesh;
    }

    private bool Shown(PrcTreeEntity entity, string name)
    {
        if (entity.Graphics.IsRemoved)
        {
            TellOf("entities removed from the tree in the file are left out", name);
            return false;
        }

        if (entity.Graphics.Behavior != 0xFFFF && !entity.Graphics.IsShown)
        {
            TellOf("entities set not to be shown in the file are left out", name);
            return false;
        }

        return true;
    }

    private void Place(SceneData.Model model, Matrix4x4F transform)
    {
        if (!transform.Decompose(out var scale, out var rotation, out var translation))
        {
            Tell($"the placement of {model.Name} is not a rotation, scale and translation; it is left out");
            return;
        }

        model.Scale = scale;
        model.Rotation = rotation;
        model.Position = translation;
    }

    private string AppearanceId(string id)
    {
        appearancesOf.TryGetValue(id, out var count);
        appearancesOf[id] = ++count;
        return count == 1 ? id : $"{id} ({count})";
    }

    private static PrcStyleChoice StyleFrom(PrcFileStructure structure, PrcGraphics graphics, PrcStyleChoice inherited) =>
        graphics.StyleIndex >= 0 && graphics.StyleIndex < structure.Styles.Count
            ? new PrcStyleChoice(structure, graphics.StyleIndex)
            : inherited;

    private string FaceMaterial(PrcFileStructure structure, PrcFaceTessellation face, PrcStyleChoice style)
    {
        if (face.LineAttributes.Length > 1)
        {
            Tell("faces drawn by a style for each of their triangulations are drawn by their first one");
        }

        if (face.LineAttributes.Length > 0)
        {
            var index = (int)face.LineAttributes[0] - 1;
            if (index >= 0 && index < structure.Styles.Count)
            {
                return MaterialOf(new PrcStyleChoice(structure, index));
            }
        }

        return MaterialOf(style);
    }

    private string MaterialOf(PrcStyleChoice style)
    {
        if (style == null)
        {
            return DefaultMaterial();
        }

        var id = style.MaterialId;
        if (SceneDataContainer.Materials.ContainsKey(id))
        {
            return id;
        }

        var structure = style.Structure;
        var definition = style.Style;
        var material = new SceneData.Material
        {
            ID = id,
            Name = string.IsNullOrEmpty(definition.Name) ? id : definition.Name,
            AmbientColor = new Vector4F(0, 0, 0, 1),
            SpecularColor = new Vector4F(0, 0, 0, 1),
            Emission = new Vector4F(0, 0, 0, 1),
            Transparency = 1,
            AlphaMode = AlphaMode.Opaque
        };
        var opacity = definition.Transparency is { } transparency ? transparency / 255f : 1f;
        if (definition.IsMaterial)
        {
            ApplyMaterial(structure, material, definition.ColorOrMaterialIndex, opacity, id);
        }
        else
        {
            var colour = ColorVectorAt(structure, definition.ColorOrMaterialIndex);
            material.DiffuseColor = new Vector4F(colour, opacity);
            material.AmbientColor = new Vector4F(colour, 1);
        }

        material.Transparency = material.DiffuseColor.W;
        material.AlphaMode = material.DiffuseColor.W < 1 ? AlphaMode.Blend : AlphaMode.Opaque;
        SceneDataContainer.Materials[id] = material;
        return id;
    }

    private void ApplyMaterial(PrcFileStructure structure, SceneData.Material material, int index, float opacity, string id)
    {
        if (index < 0 || index >= structure.Materials.Count)
        {
            Tell($"{id} names material {index}, which the globals do not hold; it is drawn grey");
            material.DiffuseColor = new Vector4F(DefaultGrey, DefaultGrey, DefaultGrey, opacity);
            return;
        }

        var definition = structure.Materials[index];
        if (definition.IsTextureApplication)
        {
            Tell("a textured material (a texture application) is drawn by its generic material, without its texture");
            definition = definition.MaterialGenericIndex >= 0 && definition.MaterialGenericIndex < structure.Materials.Count
                ? structure.Materials[definition.MaterialGenericIndex]
                : new PrcMaterial { DiffuseIndex = -1 };
        }

        material.Name = string.IsNullOrEmpty(definition.Name) ? material.Name : definition.Name;
        material.AmbientColor = new Vector4F(ColorVectorAt(structure, definition.AmbientIndex), (float)definition.AmbientAlpha);
        material.DiffuseColor = new Vector4F(ColorVectorAt(structure, definition.DiffuseIndex), (float)definition.DiffuseAlpha * opacity);
        material.SpecularColor = new Vector4F(ColorVectorAt(structure, definition.SpecularIndex, 0), (float)definition.SpecularAlpha);
        var emissive = ColorVectorAt(structure, definition.EmissiveIndex, 0);
        material.Emission = new Vector4F(emissive, (float)definition.EmissiveAlpha);
        material.EmissiveFactor = emissive;
        material.Shininess = (float)definition.Shininess;
    }

    private string DefaultMaterial()
    {
        if (!SceneDataContainer.Materials.ContainsKey(DefaultMaterialId))
        {
            SceneDataContainer.Materials[DefaultMaterialId] = new SceneData.Material
            {
                ID = DefaultMaterialId,
                Name = "Default",
                AmbientColor = new Vector4F(DefaultGrey, DefaultGrey, DefaultGrey, 1),
                DiffuseColor = new Vector4F(DefaultGrey, DefaultGrey, DefaultGrey, 1),
                SpecularColor = new Vector4F(0, 0, 0, 1),
                Emission = new Vector4F(0, 0, 0, 1),
                Transparency = 1,
                AlphaMode = AlphaMode.Opaque
            };
        }

        return DefaultMaterialId;
    }

    private string MarkupMaterial()
    {
        const string id = "markup";
        if (!SceneDataContainer.Materials.ContainsKey(id))
        {
            SceneDataContainer.Materials[id] = new SceneData.Material
            {
                ID = id,
                Name = "Markup",
                DiffuseColor = new Vector4F(1, 1, 1, 1),
                AmbientColor = new Vector4F(0, 0, 0, 1),
                SpecularColor = new Vector4F(0, 0, 0, 1),
                Emission = new Vector4F(0, 0, 0, 1),
                Transparency = 1,
                AlphaMode = AlphaMode.Opaque,
                Unlit = true
            };
        }

        return id;
    }

    private Color ColorOfStyle(PrcStyleChoice style)
    {
        if (style == null)
        {
            return new Color(0, 0, 0, 255);
        }

        var structure = style.Structure;
        var definition = style.Style;
        if (definition.IsMaterial)
        {
            var index = definition.ColorOrMaterialIndex;
            return index >= 0 && index < structure.Materials.Count
                ? new Color(ColorVectorAt(structure, structure.Materials[index].DiffuseIndex))
                : new Color(0, 0, 0, 255);
        }

        return new Color(ColorVectorAt(structure, definition.ColorOrMaterialIndex));
    }

    private Color ColorAt(PrcFileStructure structure, uint index) => new(ColorVectorAt(structure, (int)index));

    private Vector3F ColorVectorAt(PrcFileStructure structure, int index, float missing = DefaultGrey)
    {
        if (index < 0)
        {
            return new Vector3F(missing, missing, missing);
        }

        if (index % 3 != 0 || index / 3 >= structure.Colors.Count)
        {
            Tell($"file structure {structure.Index} names colour component {index} of its {structure.Colors.Count} colours; it is drawn grey");
            return new Vector3F(DefaultGrey, DefaultGrey, DefaultGrey);
        }

        var colour = structure.Colors[index / 3];
        return new Vector3F((float)colour.X, (float)colour.Y, (float)colour.Z);
    }

    private static string KindOf(uint type) => type switch
    {
        PrcTypes.BrepModel => "Brep model",
        PrcTypes.RepresentationCurve => "Curve",
        PrcTypes.Direction => "Direction",
        PrcTypes.Plane => "Plane",
        PrcTypes.PointSet => "Point set",
        PrcTypes.PolyBrepModel => "Poly brep model",
        PrcTypes.PolyWire => "Poly wire",
        PrcTypes.Set => "Set",
        PrcTypes.CoordinateSystem => "Coordinate system",
        _ => "Item"
    };

    private static ExtraData ExtraOf(Dictionary<string, string> attributes)
    {
        if (attributes == null || attributes.Count == 0)
        {
            return null;
        }

        return new ExtraData { Data = JsonSerializer.Serialize(attributes) };
    }
}

/// <summary>A node waiting to be placed: a product occurrence or a representation item, the file structure it lies
/// in, the model it goes under, the style it inherits and the occurrence it hangs under.</summary>
internal sealed class PrcPendingNode
{
    private PrcPendingNode(PrcFileStructure structure, int occurrence, PrcRepresentationItem item, SceneData.Model parent,
        PrcStyleChoice style, PrcPendingNode owner)
    {
        Structure = structure;
        Occurrence = occurrence;
        Item = item;
        Parent = parent;
        Style = style;
        Owner = owner;
    }

    internal PrcFileStructure Structure { get; }

    /// <summary>The occurrence's index in its file structure, as the file gives it; -1 for a representation item.</summary>
    internal int Occurrence { get; }

    internal PrcRepresentationItem Item { get; }

    internal SceneData.Model Parent { get; }

    /// <summary>The style the node inherits; null for none.</summary>
    internal PrcStyleChoice Style { get; }

    /// <summary>The occurrence node this one was found under; null at the root.</summary>
    internal PrcPendingNode Owner { get; }

    internal static PrcPendingNode ForOccurrence(PrcFileStructure structure, int occurrence, SceneData.Model parent,
        PrcStyleChoice style, PrcPendingNode owner) =>
        new(structure, occurrence, null, parent, style, owner);

    internal static PrcPendingNode ForItem(PrcFileStructure structure, PrcRepresentationItem item, SceneData.Model parent,
        PrcStyleChoice style, PrcPendingNode owner) =>
        new(structure, -1, item, parent, style, owner);

    /// <summary>Whether an occurrence above this node is <paramref name="occurrence"/> of <paramref name="structure"/>:
    /// the tree would then be its own ancestor.</summary>
    internal bool HasAncestor(PrcFileStructure structure, int occurrence)
    {
        for (var node = Owner; node != null; node = node.Owner)
        {
            if (node.Structure == structure && node.Occurrence == occurrence)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>A style of a file structure's globals, which a mesh is drawn by.</summary>
internal sealed class PrcStyleChoice
{
    internal PrcStyleChoice(PrcFileStructure structure, int index)
    {
        Structure = structure;
        Index = index;
    }

    internal PrcFileStructure Structure { get; }

    internal int Index { get; }

    internal PrcStyle Style => Structure.Styles[Index];

    internal string MaterialId => $"style {Structure.Index}/{Index}";
}

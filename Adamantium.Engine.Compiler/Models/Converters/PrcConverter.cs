using System;
using System.Collections.Generic;
using System.Globalization;
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

/// <summary>Imports a PRC file - a <c>.prc</c> file, or a PDF's 3D artwork from its bytes: the product tree, the meshes of
/// its tessellations, materials, textures, cameras and lights.</summary>
public class PrcConverter : ConverterBase
{
    private const string ViewsKeptAsCameras = "the views of the file are kept as their cameras, named after them; which " +
                                              "annotations, linked items and filters a view shows is left out - the scene " +
                                              "data has no views";

    private const int MaximumNodeAppearances = 1_000_000;
    private const int MaximumPrototypeChain = 64;
    private const float DefaultGray = 0.75f;
    private const string DefaultMaterialId = "default";
    private const int NamesShown = 5;
    private const uint NoLayer = 0xFFFF;
    private const uint DiffuseMapping = 0x0001;

    private readonly ImportNotes _meshNotes = new();
    private readonly Dictionary<string, List<string>> _grouped = new(StringComparer.Ordinal);
    private readonly HashSet<PrcEntityReference> _applied = [];
    private readonly HashSet<PrcEntityReference> _redefinitions = [];
    private readonly Dictionary<string, PrcTextureTransformation> _textureTransformations = new(StringComparer.Ordinal);
    private PrcFile _file;
    private int _appearances;

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
        _file = PrcFile.Read(ReadFileBytes(), CancellationToken);
        foreach (var note in _file.Notes)
        {
            Tell(note);
        }

        var unit = FindValidUnit();
        if (unit is { } valid)
        {
            SceneDataContainer.Units = new SceneData.Unit(UnitType.Meter, (float)(valid / 1000));
        }
        else
        {
            if (_file.Unit > 0)
            {
                SceneDataContainer.Units = new SceneData.Unit(UnitType.Meter, (float)(_file.Unit / 1000));
            }

            Tell("the file's unit does not come from a CAD system; it is not to be measured by");
        }

        SceneDataContainer.Models = SceneDataContainer.CreateMesh(null, "", Path.GetFileNameWithoutExtension(FileName));
        BuildTree();
        TellUnapplied();
        TellGrouped();
        foreach (var note in _meshNotes.All)
        {
            Tell(note);
        }
    }

    private void TellWithName(string feature, string name)
    {
        if (!_grouped.TryGetValue(feature, out var names))
        {
            names = [];
            _grouped.Add(feature, names);
        }

        names.Add(name);
    }

    private void TellGrouped()
    {
        foreach (var group in _grouped)
        {
            var shown = string.Join(", ", group.Value.Take(NamesShown));
            var more = group.Value.Count > NamesShown ? $" and {group.Value.Count - NamesShown} more" : string.Empty;
            Tell($"{group.Value.Count} {group.Key}: {shown}{more}");
        }
    }

    private void TellUnapplied()
    {
        foreach (var kind in _redefinitions.Where(reference => !_applied.Contains(reference) && reference.Target.Type != PrcTypes.View)
                     .GroupBy(reference => reference.Target.Type).OrderBy(group => group.Key))
        {
            Tell($"{kind.Count()} redefinitions by reference of entities of type {kind.Key} meet no entity the tree shows; they are left out");
        }
    }

    private double? FindValidUnit()
    {
        if (_file.UnitFromCad && _file.Unit > 0)
        {
            return _file.Unit;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<PrcOccurrenceSlot>();
        for (var index = _file.Roots.Count - 1; index >= 0; index--)
        {
            var structure = _file.FileStructures.FirstOrDefault(candidate => candidate.Id.Equals(_file.Roots[index].FileStructure));
            if (structure != null)
            {
                pending.Push(new PrcOccurrenceSlot(structure, _file.Roots[index].Occurrence));
            }
        }

        while (pending.Count > 0)
        {
            var slot = pending.Pop();
            if (slot.Index < 0 || slot.Index >= slot.Structure.Occurrences.Count || !seen.Add($"{slot.Structure.Index}/{slot.Index}"))
            {
                continue;
            }

            var occurrence = slot.Structure.Occurrences[slot.Index];
            if (occurrence.UnitFromCad && occurrence.Unit > 0)
            {
                return occurrence.Unit;
            }

            PushLinked(pending, slot.Structure, occurrence.ExternalIndex, occurrence.ExternalFileStructure);
            PushLinked(pending, slot.Structure, occurrence.PrototypeIndex, occurrence.PrototypeFileStructure);
            for (var son = occurrence.Sons.Count - 1; son >= 0; son--)
            {
                pending.Push(new PrcOccurrenceSlot(slot.Structure, occurrence.Sons[son]));
            }
        }

        return null;
    }

    private void PushLinked(Stack<PrcOccurrenceSlot> pending, PrcFileStructure from, int index, PrcUniqueId id)
    {
        if (index < 0)
        {
            return;
        }

        var structure = id == null ? from : _file.FileStructures.FirstOrDefault(candidate => candidate.Id.Equals(id));
        if (structure != null)
        {
            pending.Push(new PrcOccurrenceSlot(structure, index));
        }
    }

    private void BuildTree()
    {
        var pending = new Stack<PrcPendingNode>();
        for (var index = _file.Roots.Count - 1; index >= 0; index--)
        {
            var root = _file.Roots[index];
            var structure = _file.FileStructures.FirstOrDefault(candidate => candidate.Id.Equals(root.FileStructure));
            if (structure == null)
            {
                Tell($"a root product occurrence lies in file structure {root.FileStructure}, which the file does not hold");
                continue;
            }

            pending.Push(PrcPendingNode.CreateForOccurrence(structure, root.Occurrence, SceneDataContainer.Models, null, null,
                Matrix4x4F.Identity, null));
        }

        while (pending.Count > 0)
        {
            CancellationToken.ThrowIfCancellationRequested();
            var node = pending.Pop();
            if (++_appearances > MaximumNodeAppearances)
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

        var redefinition = FindRedefinition(node.Owner, structure, occurrence.UniqueId);
        if (!IsShown(occurrence, name, redefinition) || (occurrence.Behavior & PrcProductOccurrence.Suppressed) != 0)
        {
            if ((occurrence.Behavior & PrcProductOccurrence.Suppressed) != 0)
            {
                Tell($"product occurrence {name} is suppressed; it is left out");
            }

            return;
        }

        if (IsFilteredOut(node.Owner, occurrence, structure, true, node.Admitted, name, out var admitted))
        {
            return;
        }

        var model = SceneDataContainer.CreateMesh(node.Parent, CreateUniqueId($"{structure.Index}/{node.Occurrence}"), name);
        model.Extra = CreateExtra(occurrence.Attributes);
        var location = GetPlacement(redefinition, name) ?? occurrence.Location ?? Matrix4x4F.Identity;
        Place(model, location);
        var world = location * node.ParentWorld;
        var style = ChooseStyle(structure, occurrence.Graphics, node.Style, redefinition);
        ReportOccurrenceContent(occurrence);

        List<PrcPendingNode> children = [];
        var context = new PrcOccurrenceContext();
        AddRedefinitions(context, structure, occurrence);
        AddFilters(context, structure, occurrence);
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
            AddRedefinitions(context, prototypeStructure, prototype);
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

        node.Context = context;
        AddScenes(occurrence, structure, model, world);
        AddViews(occurrence.Views, structure, node, model, world);
        if (part >= 0)
        {
            if (part < partStructure.Parts.Count)
            {
                AddPart(model, partStructure, partStructure.Parts[part], style, children, node, world, admitted);
            }
            else
            {
                Tell($"product occurrence {name} shows part {part} of the {partStructure.Parts.Count} of its file structure");
            }
        }

        AddMarkups(model, structure, occurrence.Markups, style, model.ID, node, admitted);
        foreach (var son in sons)
        {
            children.Add(PrcPendingNode.CreateForOccurrence(sonsStructure, son, model, style, node, world, admitted));
        }

        if (occurrence.ExternalIndex >= 0)
        {
            var external = Resolve(structure, occurrence.ExternalFileStructure, name, "external data");
            if (external != null)
            {
                children.Add(PrcPendingNode.CreateForOccurrence(external, occurrence.ExternalIndex, model, style, node, world, admitted));
            }
        }

        for (var index = children.Count - 1; index >= 0; index--)
        {
            pending.Push(children[index]);
        }
    }

    private void ReportOccurrenceContent(PrcProductOccurrence occurrence)
    {
        if (occurrence.Views.Count > 0)
        {
            Tell(ViewsKeptAsCameras);
        }

        if (occurrence.DisplayFilters.Count(filter => filter.IsActive) > 1)
        {
            Tell("a product occurrence has several active display filters; the first is applied");
        }
    }

    private void AddRedefinitions(PrcOccurrenceContext context, PrcFileStructure structure, PrcProductOccurrence occurrence)
    {
        foreach (var reference in occurrence.EntityReferences)
        {
            if (reference.Target == null)
            {
                continue;
            }

            if (reference.Target.IsTopology)
            {
                Tell("redefinitions by reference of the faces of B-rep bodies (references on topology) are left out");
                continue;
            }

            _redefinitions.Add(reference);
            context.Redefinitions.TryAdd(new PrcEntityKey(reference.Target.FileStructure ?? structure.Id, reference.Target.UniqueId),
                new PrcRedefinition(reference, structure));
        }
    }

    private void AddFilters(PrcOccurrenceContext context, PrcFileStructure structure, PrcProductOccurrence occurrence)
    {
        if (occurrence.EntityFilter != null)
        {
            context.Filters.Add(new PrcActiveFilter(occurrence.EntityFilter, structure));
        }

        var active = occurrence.DisplayFilters.FirstOrDefault(filter => filter.IsActive);
        if (active != null)
        {
            context.Filters.Add(new PrcActiveFilter(active, structure));
        }

        foreach (var filter in context.Filters)
        {
            if (filter.Filter.Entities.Any(reference => reference.Target is { IsTopology: true }))
            {
                Tell("filters that keep or take away faces of B-rep bodies (references on topology) do not filter them");
            }
        }
    }

    private PrcRedefinition FindRedefinition(PrcPendingNode owner, PrcFileStructure structure, uint uniqueId)
    {
        if (uniqueId == 0)
        {
            return null;
        }

        PrcRedefinition found = null;
        var key = new PrcEntityKey(structure.Id, uniqueId);
        for (var node = owner; node != null; node = node.Owner)
        {
            if (node.Context != null && node.Context.Redefinitions.TryGetValue(key, out var redefinition))
            {
                found = redefinition;
            }
        }

        if (found != null)
        {
            _applied.Add(found.Reference);
        }

        return found;
    }

    private bool IsFilteredOut(PrcPendingNode owner, PrcTreeEntity entity, PrcFileStructure structure, bool isContainer,
        PrcFilterPass passed, string name, out PrcFilterPass admitted)
    {
        admitted = passed;
        var layer = entity.Graphics.LayerIndex < 0 ? NoLayer : (uint)entity.Graphics.LayerIndex;
        var key = new PrcEntityKey(structure.Id, entity.UniqueId);
        for (var node = owner; node != null; node = node.Owner)
        {
            if (node.Context == null)
            {
                continue;
            }

            foreach (var active in node.Context.Filters)
            {
                if (active.Layers.Count > 0 && active.Filter.LayersInclusive != active.Layers.Contains(layer))
                {
                    TellWithName("entities the display filter of a product occurrence takes away by their layer are left out", name);
                    return true;
                }

                if (active.Entities.Count == 0)
                {
                    continue;
                }

                var named = entity.UniqueId != 0 && active.Entities.Contains(key);
                if (!active.Filter.EntitiesInclusive)
                {
                    if (named)
                    {
                        TellWithName("entities the display filter of a product occurrence takes away are left out", name);
                        return true;
                    }

                    continue;
                }

                if (named)
                {
                    admitted = new PrcFilterPass(active.Filter, admitted);
                }
                else if (!PrcFilterPass.HasFilter(admitted, active.Filter) && !isContainer)
                {
                    TellWithName("entities the display filter of a product occurrence does not keep are left out", name);
                    return true;
                }
            }
        }

        return false;
    }

    private Matrix4x4F? GetPlacement(PrcRedefinition redefinition, string name)
    {
        if (redefinition == null || redefinition.Reference.CoordinateSystemIndex < 0)
        {
            return null;
        }

        var systems = redefinition.Structure.CoordinateSystems;
        if (redefinition.Reference.CoordinateSystemIndex >= systems.Count)
        {
            Tell($"{name} is placed by reference by coordinate system {redefinition.Reference.CoordinateSystemIndex}, which the globals " +
                 "do not hold; it keeps its own place");
            return null;
        }

        return systems[redefinition.Reference.CoordinateSystemIndex].Transform;
    }

    private PrcFileStructure Resolve(PrcFileStructure from, PrcUniqueId id, string name, string what)
    {
        if (id == null)
        {
            return from;
        }

        var found = _file.FileStructures.FirstOrDefault(candidate => candidate.Id.Equals(id));
        if (found == null)
        {
            Tell($"the {what} of product occurrence {name} lies in file structure {id}, which the file does not hold");
        }

        return found;
    }

    private void AddPart(SceneData.Model model, PrcFileStructure structure, PrcPartDefinition part, PrcStyleChoice style,
        List<PrcPendingNode> children, PrcPendingNode owner, Matrix4x4F world, PrcFilterPass passed)
    {
        var name = string.IsNullOrEmpty(part.Name) ? model.Name : part.Name;
        var redefinition = FindRedefinition(owner, structure, part.UniqueId);
        if (!IsShown(part, name, redefinition) || IsFilteredOut(owner, part, structure, true, passed, name, out var admitted))
        {
            return;
        }

        var partStyle = ChooseStyle(structure, part.Graphics, style, redefinition);
        foreach (var item in part.Items)
        {
            children.Add(PrcPendingNode.CreateForItem(structure, item, model, partStyle, owner, world, admitted));
        }

        if (part.Views.Count > 0)
        {
            Tell(ViewsKeptAsCameras);
        }

        AddViews(part.Views, structure, owner, model, world);
        AddMarkups(model, structure, part.Markups, partStyle, model.ID + "/part", owner, admitted);
    }

    private void ExpandItem(PrcPendingNode node, Stack<PrcPendingNode> pending)
    {
        var item = node.Item;
        var structure = node.Structure;
        var name = string.IsNullOrEmpty(item.Name) ? GetKindName(item.Type) : item.Name;
        var redefinition = FindRedefinition(node.Owner, structure, item.UniqueId);
        if (!IsShown(item, name, redefinition) ||
            IsFilteredOut(node.Owner, item, structure, item.Type == PrcTypes.Set, node.Admitted, name, out var admitted))
        {
            return;
        }

        var model = SceneDataContainer.CreateMesh(node.Parent, CreateUniqueId($"{node.Parent.ID}/{name}"), name);
        model.Extra = CreateExtra(item.Attributes);
        if (GetPlacement(redefinition, name) is { } placement)
        {
            Place(model, placement);
        }
        else if (item.Type == PrcTypes.CoordinateSystem)
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

        var style = ChooseStyle(structure, item.Graphics, node.Style, redefinition);
        AddItemMeshes(model, structure, item, name, style);
        for (var index = item.Items.Count - 1; index >= 0; index--)
        {
            pending.Push(PrcPendingNode.CreateForItem(structure, item.Items[index], model, style, node.Owner, node.ParentWorld, admitted));
        }
    }

    private void AddItemMeshes(SceneData.Model model, PrcFileStructure structure, PrcRepresentationItem item, string name,
        PrcStyleChoice style)
    {
        if (item.Type == PrcTypes.PointSet && item.Points.Count > 0)
        {
            var mesh = new Mesh(PrimitiveType.PointList) { Name = name };
            mesh.SetPoints(item.Points).SetIndices(Enumerable.Range(0, item.Points.Count));
            mesh.MaterialID = GetMaterial(style);
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
            Tell($"representation item {name} shows tessellation {item.TessellationIndex}, which file structure {structure.Index} does not hold");
            return;
        }

        switch (structure.Tessellations[item.TessellationIndex])
        {
            case Prc3DTessellation tessellation:
                var groups = PrcMeshBuilder.Build3D(tessellation, face => GetFaceMaterial(structure, face, style), _meshNotes);
                foreach (var group in groups.OrderBy(entry => entry.Key, StringComparer.Ordinal))
                {
                    if (group.Value.Indices.Count == 0)
                    {
                        continue;
                    }

                    if (group.Value.HasTextureCoordinates && _textureTransformations.TryGetValue(group.Key, out var transformation))
                    {
                        group.Value.TransformTextureCoordinates(transformation);
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
                    mesh.MaterialID = GetMaterial(style);
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

    private void AddMarkups(SceneData.Model model, PrcFileStructure structure, PrcMarkupData data, PrcStyleChoice style, string id,
        PrcPendingNode owner, PrcFilterPass passed)
    {
        if (data == null || data.Markups.Count + data.Leaders.Count == 0)
        {
            return;
        }

        SceneData.Model holder = null;
        foreach (var markup in data.Markups.Concat(data.Leaders))
        {
            var name = string.IsNullOrEmpty(markup.Name) ? (markup.Type == PrcTypes.Leader ? "Leader" : "Markup") : markup.Name;
            var redefinition = FindRedefinition(owner, structure, markup.UniqueId);
            if (!IsShown(markup, name, redefinition) || markup.TessellationIndex < 0 ||
                IsFilteredOut(owner, markup, structure, false, passed, name, out _))
            {
                continue;
            }

            if (markup.TessellationIndex >= structure.Tessellations.Count ||
                structure.Tessellations[markup.TessellationIndex] is not PrcMarkupTessellation tessellation)
            {
                Tell($"markup {name} shows tessellation {markup.TessellationIndex}, which is not a markup tessellation read from file structure {structure.Index}");
                continue;
            }

            holder ??= SceneDataContainer.CreateMesh(model, CreateUniqueId(id + "/markups"), "Markups");
            var node = SceneDataContainer.CreateMesh(holder, CreateUniqueId($"{holder.ID}/{name}"), name);
            AddMarkupMeshes(node, structure, tessellation, ChooseStyle(structure, markup.Graphics, style, redefinition), name);
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

        var start = GetStyleColor(style);
        var meshes = PrcMeshBuilder.BuildMarkup(markup, index => GetColor(structure, index), start, _meshNotes);
        var material = GetMarkupMaterial();
        PrcMeshData[] parts = [meshes.Lines, meshes.Triangles, meshes.Points];
        foreach (var data in parts)
        {
            if (data.Indices.Count == 0)
            {
                continue;
            }

            var mesh = ToMesh(data, $"{name} {model.Meshes.Count}");
            mesh.MaterialID = material;
            model.Meshes.Add(mesh);
        }

        if (meshes.Texts.Count > 0)
        {
            model.MeshExtra = CreateTextsExtra(structure, meshes.Texts);
            Tell("the texts of markups are kept on their nodes' mesh data - the string, its font, size and place - not drawn: " +
                 "the file names its fonts and does not hold them");
        }
    }

    private static ExtraData CreateTextsExtra(PrcFileStructure structure, List<PrcMarkupText> texts)
    {
        List<Dictionary<string, object>> written = [];
        foreach (var text in texts)
        {
            double[] position = [text.Position.X, text.Position.Y, text.Position.Z];
            var entry = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["text"] = text.Text ?? string.Empty,
                ["width"] = text.Width,
                ["height"] = text.Height,
                ["position"] = position
            };
            if (text.FontIndex >= 0 && text.FontIndex < structure.Fonts.Count)
            {
                var font = structure.Fonts[text.FontIndex];
                entry["font"] = font.Name ?? string.Empty;
                entry["fontSize"] = font.Size;
                entry["fontAttributes"] = font.Attributes;
            }

            written.Add(entry);
        }

        return new ExtraData { Data = JsonSerializer.Serialize(new Dictionary<string, object> { ["markupTexts"] = written }) };
    }

    private void AddScenes(PrcProductOccurrence occurrence, PrcFileStructure structure, SceneData.Model model, Matrix4x4F world)
    {
        for (var index = 0; index < occurrence.SceneDisplayParameters.Count; index++)
        {
            var scene = occurrence.SceneDisplayParameters[index];
            var name = !string.IsNullOrEmpty(scene.Name) ? scene.Name : !string.IsNullOrEmpty(scene.Camera?.Name) ? scene.Camera.Name : model.Name;
            AddScene(scene, structure, $"{model.ID} scene {index}", name, world, true);
        }
    }

    private void AddViews(List<PrcView> views, PrcFileStructure structure, PrcPendingNode owner, SceneData.Model model, Matrix4x4F world)
    {
        for (var index = 0; index < views.Count; index++)
        {
            var view = views[index];
            var redefinition = FindRedefinition(owner, structure, view.UniqueId);
            var graphics = redefinition != null && redefinition.Reference.Graphics.HasBehavior ? redefinition.Reference.Graphics : view.Graphics;
            if (graphics.IsRemoved || (graphics.HasBehavior && !graphics.IsShown) || view.Scene == null)
            {
                continue;
            }

            var name = string.IsNullOrEmpty(view.Name) ? $"{model.Name} view {index}" : view.Name;
            AddScene(view.Scene, structure, $"{model.ID} view {index}", name, world, view.IsDefault);
        }
    }

    private void AddScene(PrcSceneDisplayParameters scene, PrcFileStructure structure, string id, string name, Matrix4x4F world,
        bool withLights)
    {
        var placement = scene.IsAbsolute ? Matrix4x4F.Identity : world;
        if (scene.Camera != null)
        {
            AddCamera(scene.Camera, CreateUniqueId(id), name, placement);
        }

        if (withLights)
        {
            for (var index = 0; index < scene.Lights.Count; index++)
            {
                AddLight(scene.Lights[index], structure, CreateUniqueId($"{id} light {index}"), name, placement);
            }
        }

        if (scene.ClippingPlaneCount > 0 || scene.BackgroundStyleIndex >= 0 || scene.RotationCenter != null)
        {
            Tell("what scenes set beyond their lights and cameras - clipping planes, backgrounds, centers of rotation - is left out: " +
                 "the scene data has no place for it");
        }
    }

    private void AddCamera(PrcCamera source, string id, string name, Matrix4x4F world)
    {
        var position = TransformPoint(source.Position, world);
        var target = TransformPoint(source.LookAt, world);
        var up = Vector3F.TransformNormal(ToSingle(source.Up), world);
        if (CreateLookAlongMatrix(position, target - position, up) is not { } placement || !placement.Decompose(out _, out var rotation, out var translation))
        {
            Tell($"camera {name} looks at the point it stands on; it is left out");
            return;
        }

        var camera = new SceneData.Camera
        {
            ID = id,
            Name = name,
            Rotation = rotation,
            Translation = translation,
            ZNear = (float)source.Near,
            ZFar = (float)source.Far,
            AspectRatio = (float)source.Aspect,
            FovType = FovType.YFov
        };
        if (source.IsOrthographic)
        {
            camera.ProjectionType = CameraProjectionType.Orthogonal;
            camera.XMag = (float)source.FieldOfViewX;
            camera.YMag = (float)source.FieldOfViewY;
        }
        else
        {
            camera.ProjectionType = CameraProjectionType.Perspective;
            camera.Fov = (float)(source.FieldOfViewY * 180 / Math.PI);
        }

        if (source.Zoom != 1 && source.Zoom != 0)
        {
            camera.Extra = new ExtraData { Data = JsonSerializer.Serialize(new Dictionary<string, double> { ["zoom"] = source.Zoom }) };
        }

        SceneDataContainer.CameraData[camera.ID] = camera;
    }

    private void AddLight(PrcLight source, PrcFileStructure structure, string id, string name, Matrix4x4F world)
    {
        var position = TransformPoint(source.Location, world);
        var direction = Vector3F.TransformNormal(ToSingle(source.Direction), world);
        var rotation = QuaternionF.Identity;
        if (source.Type is PrcTypes.DirectionalLight or PrcTypes.SpotLight &&
            CreateLookAlongMatrix(position, direction, Vector3F.UnitZ) is { } placement && placement.Decompose(out _, out var turned, out _))
        {
            rotation = turned;
        }

        var color = source.Type == PrcTypes.AmbientLight && source.AmbientIndex >= 0
            ? GetColorVector(structure, source.AmbientIndex, 1)
            : GetColorVector(structure, source.DiffuseIndex, 1);
        SceneDataContainer.LightData[id] = new SceneData.Light
        {
            ID = id,
            Name = string.IsNullOrEmpty(source.Name) ? name : source.Name,
            LightType = source.Type switch
            {
                PrcTypes.AmbientLight => LightType.Ambient,
                PrcTypes.DirectionalLight => LightType.Directional,
                PrcTypes.SpotLight => LightType.Spot,
                _ => LightType.Point
            },
            Color = color,
            Intensity = (float)source.Intensity,
            Position = position,
            Rotation = rotation,
            Scale = Vector3F.One,
            ConstantAttenuation = (float)source.ConstantAttenuation,
            LinearAttenuation = (float)source.LinearAttenuation,
            QuadraticAttenuation = (float)source.QuadraticAttenuation,
            FalloffAngle = (float)source.FallOffAngle,
            FalloffExponent = (float)source.FallOffExponent
        };
    }

    private static Matrix4x4F? CreateLookAlongMatrix(Vector3F position, Vector3F forward, Vector3F up)
    {
        if (forward.LengthSquared() < 1e-20f)
        {
            return null;
        }

        var ahead = Vector3F.Normalize(forward);
        var right = Vector3F.Cross(ahead, up);
        if (right.LengthSquared() < 1e-12f)
        {
            right = Vector3F.Cross(ahead, MathF.Abs(ahead.Z) < 0.9f ? Vector3F.UnitZ : Vector3F.UnitX);
        }

        right = Vector3F.Normalize(right);
        var above = Vector3F.Cross(right, ahead);
        return new Matrix4x4F(
            right.X, right.Y, right.Z, 0,
            ahead.X, ahead.Y, ahead.Z, 0,
            above.X, above.Y, above.Z, 0,
            position.X, position.Y, position.Z, 1);
    }

    private static Vector3F TransformPoint(Vector3<double> point, Matrix4x4F world) => Vector3F.TransformCoordinate(ToSingle(point), world);

    private static Vector3F ToSingle(Vector3<double> vector) => new((float)vector.X, (float)vector.Y, (float)vector.Z);

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

    private bool IsShown(PrcTreeEntity entity, string name, PrcRedefinition redefinition)
    {
        if (redefinition != null && redefinition.Reference.Graphics.HasBehavior)
        {
            var graphics = redefinition.Reference.Graphics;
            if (graphics.IsRemoved || !graphics.IsShown)
            {
                TellWithName("entities a product occurrence takes away or hides by reference are left out", name);
                return false;
            }

            return true;
        }

        if (entity.Graphics.IsRemoved)
        {
            TellWithName("entities removed from the tree in the file are left out", name);
            return false;
        }

        if (entity.Graphics.HasBehavior && !entity.Graphics.IsShown)
        {
            TellWithName("entities set not to be shown in the file are left out", name);
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

    private PrcStyleChoice ChooseStyle(PrcFileStructure structure, PrcGraphics graphics, PrcStyleChoice inherited,
        PrcRedefinition redefinition)
    {
        if (redefinition != null && redefinition.Reference.Graphics.StyleIndex >= 0)
        {
            var index = redefinition.Reference.Graphics.StyleIndex;
            if (index < redefinition.Structure.Styles.Count)
            {
                return new PrcStyleChoice(redefinition.Structure, index);
            }

            Tell($"a redefinition by reference gives style {index}, which the globals of file structure {redefinition.Structure.Index} " +
                 "do not hold; the entity keeps its own");
        }

        return graphics.StyleIndex >= 0 && graphics.StyleIndex < structure.Styles.Count
            ? new PrcStyleChoice(structure, graphics.StyleIndex)
            : inherited;
    }

    private string GetFaceMaterial(PrcFileStructure structure, PrcFaceTessellation face, PrcStyleChoice style)
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
                return GetMaterial(new PrcStyleChoice(structure, index));
            }
        }

        return GetMaterial(style);
    }

    private string GetMaterial(PrcStyleChoice style)
    {
        if (style == null)
        {
            return GetDefaultMaterial();
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
            var color = GetColorVector(structure, definition.ColorOrMaterialIndex);
            material.DiffuseColor = new Vector4F(color, opacity);
            material.AmbientColor = new Vector4F(color, 1);
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
            Tell($"{id} names material {index}, which the globals do not hold; it is drawn gray");
            material.DiffuseColor = new Vector4F(DefaultGray, DefaultGray, DefaultGray, opacity);
            return;
        }

        var definition = structure.Materials[index];
        var application = definition.IsTextureApplication ? definition : null;
        if (application != null)
        {
            definition = application.MaterialGenericIndex >= 0 && application.MaterialGenericIndex < structure.Materials.Count &&
                         !structure.Materials[application.MaterialGenericIndex].IsTextureApplication
                ? structure.Materials[application.MaterialGenericIndex]
                : new PrcMaterial { DiffuseIndex = -1 };
        }

        material.Name = string.IsNullOrEmpty(definition.Name) ? material.Name : definition.Name;
        material.AmbientColor = new Vector4F(GetColorVector(structure, definition.AmbientIndex), (float)definition.AmbientAlpha);
        material.DiffuseColor = new Vector4F(GetColorVector(structure, definition.DiffuseIndex), (float)definition.DiffuseAlpha * opacity);
        material.SpecularColor = new Vector4F(GetColorVector(structure, definition.SpecularIndex, 0), (float)definition.SpecularAlpha);
        var emissive = GetColorVector(structure, definition.EmissiveIndex, 0);
        material.Emission = new Vector4F(emissive, (float)definition.EmissiveAlpha);
        material.EmissiveFactor = emissive;
        material.Shininess = (float)definition.Shininess;
        if (application != null)
        {
            LayTexture(structure, material, application, id);
        }
    }

    private void LayTexture(PrcFileStructure structure, SceneData.Material material, PrcMaterial application, string id)
    {
        var level = application;
        var seen = new HashSet<PrcMaterial>();
        PrcMaterial chosen = null;
        while (level != null && seen.Add(level))
        {
            var definitionIndex = level.TextureDefinitionIndex;
            if (definitionIndex < 0 || definitionIndex >= structure.TextureDefinitions.Count)
            {
                Tell($"a texture application of file structure {structure.Index} names texture definition {definitionIndex}, " +
                     "which the globals do not hold; it is left out");
            }
            else if (chosen == null && ((structure.TextureDefinitions[definitionIndex].MappingAttributes & DiffuseMapping) != 0 ||
                                        structure.TextureDefinitions[definitionIndex].MappingAttributes == 0))
            {
                chosen = level;
            }
            else
            {
                Tell($"textures laid other than as the diffuse color (mapping attributes " +
                     $"0x{structure.TextureDefinitions[definitionIndex].MappingAttributes:X}: bump, opacity, reflection) are left out");
            }

            level = level.NextTextureIndex >= 0 && level.NextTextureIndex < structure.Materials.Count &&
                    structure.Materials[level.NextTextureIndex].IsTextureApplication
                ? structure.Materials[level.NextTextureIndex]
                : null;
        }

        if (chosen == null)
        {
            return;
        }

        var texture = structure.TextureDefinitions[chosen.TextureDefinitionIndex];
        var image = GetImage(structure, texture.PictureIndex);
        if (image == null)
        {
            return;
        }

        if (texture.MappingType is not (PrcTextureDefinition.MappingStored or 0))
        {
            Tell($"textures whose coordinates are to be worked out (mapping type {texture.MappingType}) are laid on the " +
                 "tessellation's stored coordinates");
        }

        if (chosen.TextureCoordinateSet != 0)
        {
            Tell("textures read with a vertex's set of texture coordinates other than the first are laid with the first");
        }

        if (texture.Function is PrcTextureDefinition.FunctionBlend or PrcTextureDefinition.FunctionDecal)
        {
            Tell("textures blended with a color or laid as decals are laid multiplying their material's color");
        }

        var textureId = $"texture {structure.Index}/{chosen.TextureDefinitionIndex}";
        if (!SceneDataContainer.Textures.ContainsKey(textureId))
        {
            SceneDataContainer.Textures[textureId] = new SceneData.Texture
            {
                ID = textureId,
                Name = string.IsNullOrEmpty(texture.Name) ? textureId : texture.Name,
                Image = image,
                Sampler = GetSampler(texture)
            };
        }

        material.DiffuseMap = image;
        material.BaseColorTexture = new TextureReference { Texture = textureId, TexCoord = 0 };
        if (texture.Function == PrcTextureDefinition.FunctionReplace)
        {
            material.DiffuseColor = new Vector4F(1, 1, 1, material.DiffuseColor.W);
        }

        if (texture.Transformation != null && texture.Transformation.Matrix != Matrix4x4<double>.Identity ||
            texture.Transformation is { FlipS: true } or { FlipT: true })
        {
            _textureTransformations[id] = texture.Transformation;
        }
    }

    private string GetImage(PrcFileStructure structure, int pictureIndex)
    {
        var id = $"picture {structure.Index}/{pictureIndex}";
        if (SceneDataContainer.Images.ContainsKey(id))
        {
            return id;
        }

        if (pictureIndex < 0 || pictureIndex >= structure.Pictures.Count)
        {
            Tell($"a texture definition of file structure {structure.Index} names picture {pictureIndex}, which the globals do not hold");
            return null;
        }

        var picture = structure.Pictures[pictureIndex];
        if (picture.UncompressedFileIndex < 0 || picture.UncompressedFileIndex >= structure.UncompressedFiles.Count)
        {
            Tell($"picture {pictureIndex} of file structure {structure.Index} lies in uncompressed file {picture.UncompressedFileIndex}, " +
                 "which the file structure does not hold");
            return null;
        }

        var mime = picture.Format switch
        {
            PrcPicture.Png => "image/png",
            PrcPicture.Jpeg => "image/jpeg",
            _ => null
        };
        if (mime == null)
        {
            Tell($"pictures of raw pixels (format {picture.Format}) are left out; their textures are not laid");
            return null;
        }

        SceneDataContainer.Images[id] = new SceneData.Image
        {
            ID = id,
            Name = string.IsNullOrEmpty(picture.Name) ? id : picture.Name,
            Data = structure.UncompressedFiles[picture.UncompressedFileIndex],
            MimeType = mime
        };
        return id;
    }

    private string GetSampler(PrcTextureDefinition texture)
    {
        var id = $"sampler {texture.WrapS.ToString(CultureInfo.InvariantCulture)}/{texture.WrapT.ToString(CultureInfo.InvariantCulture)}";
        if (!SceneDataContainer.Samplers.ContainsKey(id))
        {
            SceneDataContainer.Samplers[id] = new SceneData.Sampler { ID = id, Name = id, WrapS = ConvertWrap(texture.WrapS), WrapT = ConvertWrap(texture.WrapT) };
        }

        return id;
    }

    private TextureWrap ConvertWrap(int mode)
    {
        switch (mode)
        {
            case PrcTextureDefinition.WrapMirroredRepeat:
                return TextureWrap.MirroredRepeat;
            case PrcTextureDefinition.WrapClamp or PrcTextureDefinition.WrapClampToEdge:
                return TextureWrap.ClampToEdge;
            case PrcTextureDefinition.WrapClampToBorder:
                Tell("textures clamped to their border color are clamped to their edge");
                return TextureWrap.ClampToEdge;
            default:
                return TextureWrap.Repeat;
        }
    }

    private string GetDefaultMaterial()
    {
        if (!SceneDataContainer.Materials.ContainsKey(DefaultMaterialId))
        {
            SceneDataContainer.Materials[DefaultMaterialId] = new SceneData.Material
            {
                ID = DefaultMaterialId,
                Name = "Default",
                AmbientColor = new Vector4F(DefaultGray, DefaultGray, DefaultGray, 1),
                DiffuseColor = new Vector4F(DefaultGray, DefaultGray, DefaultGray, 1),
                SpecularColor = new Vector4F(0, 0, 0, 1),
                Emission = new Vector4F(0, 0, 0, 1),
                Transparency = 1,
                AlphaMode = AlphaMode.Opaque
            };
        }

        return DefaultMaterialId;
    }

    private string GetMarkupMaterial()
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

    private Color GetStyleColor(PrcStyleChoice style)
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
                ? new Color(GetColorVector(structure, structure.Materials[index].DiffuseIndex))
                : new Color(0, 0, 0, 255);
        }

        return new Color(GetColorVector(structure, definition.ColorOrMaterialIndex));
    }

    private Color GetColor(PrcFileStructure structure, uint index) => new(GetColorVector(structure, (int)index));

    private Vector3F GetColorVector(PrcFileStructure structure, int index, float missing = DefaultGray)
    {
        if (index < 0)
        {
            return new Vector3F(missing, missing, missing);
        }

        if (index % 3 != 0 || index / 3 >= structure.Colors.Count)
        {
            Tell($"file structure {structure.Index} names color component {index} of its {structure.Colors.Count} colors; it is drawn gray");
            return new Vector3F(DefaultGray, DefaultGray, DefaultGray);
        }

        var color = structure.Colors[index / 3];
        return new Vector3F((float)color.X, (float)color.Y, (float)color.Z);
    }

    private static string GetKindName(uint type) => type switch
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

    private static ExtraData CreateExtra(Dictionary<string, string> attributes)
    {
        if (attributes == null || attributes.Count == 0)
        {
            return null;
        }

        return new ExtraData { Data = JsonSerializer.Serialize(attributes) };
    }
}

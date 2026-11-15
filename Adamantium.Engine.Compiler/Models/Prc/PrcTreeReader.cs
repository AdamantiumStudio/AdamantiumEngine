using System.Collections.Generic;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>Reads a file structure's tree section (PRC_TYPE_ASM_FileStructureTree): part definitions and product
/// occurrences with their markups, views, filters and scenes.</summary>
internal static class PrcTreeReader
{
    /// <summary>Reads the tree in <paramref name="bits"/> into <paramref name="structure"/>.</summary>
    internal static void Read(PrcBitReader bits, PrcFileStructure structure, ImportNotes notes)
    {
        var reader = new PrcEntityReader(bits, structure.Schema, notes) { AuthoringVersion = structure.AuthoringVersion };
        reader.Expect(PrcTypes.FileStructureTree, "the tree");
        reader.ReadContentPrcBase(PrcTypes.FileStructureTree);
        var parts = bits.ReadCount(8, "part definitions");
        for (var index = 0; index < parts; index++)
        {
            var type = bits.ReadUnsignedInteger();
            if (type != PrcTypes.PartDefinition)
            {
                reader.SkipUnknown(type, "a part definition");
                structure.Parts.Add(new PrcPartDefinition { Type = type });
                continue;
            }

            structure.Parts.Add(ReadPartDefinition(reader));
        }

        var occurrences = bits.ReadCount(8, "product occurrences");
        for (var index = 0; index < occurrences; index++)
        {
            var type = bits.ReadUnsignedInteger();
            if (type != PrcTypes.ProductOccurrence)
            {
                reader.SkipUnknown(type, "a product occurrence");
                structure.Occurrences.Add(new PrcProductOccurrence { Type = type });
                continue;
            }

            structure.Occurrences.Add(ReadProductOccurrence(reader));
        }

        reader.Expect(PrcTypes.FileStructure, "the file structure's internal data");
        reader.ReadContentPrcBase(PrcTypes.FileStructure);
        bits.ReadUnsignedInteger();
        structure.RootIndex = (int)bits.ReadUnsignedInteger() - 1;
        reader.SkipNewData(PrcTypes.FileStructure);
        reader.SkipNewData(PrcTypes.FileStructureTree);
        reader.SkipUserData();
        reader.CheckEnd();
    }

    private static PrcPartDefinition ReadPartDefinition(PrcEntityReader reader)
    {
        var bits = reader.Bits;
        var part = new PrcPartDefinition { Type = PrcTypes.PartDefinition };
        reader.ReadPrcBaseWithGraphics(part);
        reader.SkipDoubles(6);
        var items = bits.ReadCount(8, "representation items");
        ReadRepresentationItems(reader, part.Items, items);
        part.Markups = ReadMarkupData(reader);
        var views = bits.ReadCount(8, "views");
        for (var view = 0; view < views; view++)
        {
            part.Views.Add(reader.ReadView());
        }

        reader.SkipNewData(PrcTypes.PartDefinition);
        reader.SkipUserData();
        return part;
    }

    private static PrcProductOccurrence ReadProductOccurrence(PrcEntityReader reader)
    {
        var bits = reader.Bits;
        var start = new PrcTreeEntity { Type = PrcTypes.ProductOccurrence };
        reader.ReadPrcBaseWithGraphics(start);
        var partIndex = (int)bits.ReadUnsignedInteger() - 1;
        var prototypeIndex = (int)bits.ReadUnsignedInteger() - 1;
        PrcUniqueId prototypeStructure = null;
        if (prototypeIndex >= 0 && !bits.ReadBoolean())
        {
            prototypeStructure = reader.ReadCompressedUniqueId();
        }

        var externalIndex = (int)bits.ReadUnsignedInteger() - 1;
        PrcUniqueId externalStructure = null;
        if (externalIndex >= 0 && !bits.ReadBoolean())
        {
            externalStructure = reader.ReadCompressedUniqueId();
        }

        var sons = new List<int>(bits.ReadCount(1, "son occurrences"));
        for (var son = 0; son < sons.Capacity; son++)
        {
            sons.Add((int)bits.ReadUnsignedInteger());
        }

        var occurrence = new PrcProductOccurrence
        {
            Type = PrcTypes.ProductOccurrence,
            Name = start.Name,
            Graphics = start.Graphics,
            PartIndex = partIndex,
            PrototypeIndex = prototypeIndex,
            PrototypeFileStructure = prototypeStructure,
            ExternalIndex = externalIndex,
            ExternalFileStructure = externalStructure,
            Behavior = bits.ReadCharacter(),
            UniqueId = start.UniqueId
        };
        occurrence.Sons.AddRange(sons);
        foreach (var attribute in start.Attributes)
        {
            occurrence.Attributes[attribute.Key] = attribute.Value;
        }

        occurrence.UnitFromCad = bits.ReadBoolean();
        occurrence.Unit = bits.ReadDouble();
        bits.ReadCharacter();
        bits.ReadInteger();
        if (bits.ReadBoolean())
        {
            occurrence.Location = reader.ReadTransformationEntity();
        }

        var references = bits.ReadCount(8, "entity references");
        for (var reference = 0; reference < references; reference++)
        {
            occurrence.EntityReferences.Add(reader.ReadEntityReference());
        }

        occurrence.Markups = ReadMarkupData(reader);
        var views = bits.ReadCount(8, "views");
        for (var view = 0; view < views; view++)
        {
            occurrence.Views.Add(reader.ReadView());
        }

        if (bits.ReadBoolean())
        {
            occurrence.EntityFilter = reader.ReadFilter();
        }

        var filters = bits.ReadCount(8, "display filters");
        for (var filter = 0; filter < filters; filter++)
        {
            occurrence.DisplayFilters.Add(reader.ReadFilter());
        }

        var scenes = bits.ReadCount(8, "scene display parameters");
        for (var scene = 0; scene < scenes; scene++)
        {
            occurrence.SceneDisplayParameters.Add(reader.ReadSceneDisplayParameters());
        }

        reader.SkipNewData(PrcTypes.ProductOccurrence);
        reader.SkipUserData();
        return occurrence;
    }

    /// <summary>Reads <paramref name="count"/> representation items into <paramref name="into"/>, each with its type
    /// code; the items of a set go into the set, read with a stack rather than by recursion.</summary>
    internal static void ReadRepresentationItems(PrcEntityReader reader, List<PrcRepresentationItem> into, int count)
    {
        var bits = reader.Bits;
        var pending = new Stack<PrcPendingItems>();
        pending.Push(new PrcPendingItems(null, into, count));
        while (pending.Count > 0)
        {
            var level = pending.Peek();
            if (level.Remaining == 0)
            {
                pending.Pop();
                if (level.Set != null)
                {
                    reader.SkipNewData(PrcTypes.Set);
                    reader.SkipUserData();
                }

                continue;
            }

            level.Remaining--;
            var type = bits.ReadUnsignedInteger();
            if (type is < PrcTypes.BrepModel or > PrcTypes.CoordinateSystem)
            {
                reader.SkipUnknown(type, "a representation item");
                continue;
            }

            var item = new PrcRepresentationItem { Type = type };
            level.Into.Add(item);
            ReadRepresentationItemContent(reader, item);
            if (type == PrcTypes.Set)
            {
                var children = bits.ReadCount(8, "items of a set");
                pending.Push(new PrcPendingItems(item, item.Items, children));
                continue;
            }

            ReadRepresentationItemRest(reader, item);
        }
    }

    /// <summary>A PRC_TYPE_RI_CoordinateSystem, its type code read.</summary>
    internal static PrcRepresentationItem ReadCoordinateSystem(PrcEntityReader reader)
    {
        var item = new PrcRepresentationItem { Type = PrcTypes.CoordinateSystem };
        ReadRepresentationItemContent(reader, item);
        ReadRepresentationItemRest(reader, item);
        return item;
    }

    private static void ReadRepresentationItemContent(PrcEntityReader reader, PrcRepresentationItem item)
    {
        reader.ReadPrcBaseWithGraphics(item);
        item.CoordinateSystemIndex = (int)reader.Bits.ReadUnsignedInteger() - 1;
        item.TessellationIndex = (int)reader.Bits.ReadUnsignedInteger() - 1;
        reader.SkipNewData(PrcTypes.RepresentationItemContent);
    }

    private static void ReadRepresentationItemRest(PrcEntityReader reader, PrcRepresentationItem item)
    {
        var bits = reader.Bits;
        switch (item.Type)
        {
            case PrcTypes.BrepModel:
                ReadBody(reader, item);
                item.IsClosed = bits.ReadBoolean();
                break;
            case PrcTypes.RepresentationCurve or PrcTypes.Plane:
                ReadBody(reader, item);
                break;
            case PrcTypes.Direction:
                if (bits.ReadBoolean())
                {
                    item.Points.Add(reader.ReadVector3d());
                }

                item.Points.Add(reader.ReadVector3d());
                break;
            case PrcTypes.PointSet:
                var points = bits.ReadCount(3 * PrcBitReader.ShortestDouble, "points");
                for (var point = 0; point < points; point++)
                {
                    item.Points.Add(reader.ReadVector3d());
                }

                break;
            case PrcTypes.PolyBrepModel:
                item.IsClosed = bits.ReadBoolean();
                break;
            case PrcTypes.CoordinateSystem:
                item.Transform = reader.ReadTransformationEntity();
                break;
        }

        reader.SkipNewData(item.Type);
        reader.SkipUserData();
    }

    private static void ReadBody(PrcEntityReader reader, PrcRepresentationItem item)
    {
        if (reader.Bits.ReadBoolean())
        {
            item.ContextIndex = (int)reader.Bits.ReadUnsignedInteger();
            item.BodyIndex = (int)reader.Bits.ReadUnsignedInteger();
        }
    }

    private static PrcMarkupData ReadMarkupData(PrcEntityReader reader)
    {
        var bits = reader.Bits;
        var data = new PrcMarkupData { LinkedItemCount = bits.ReadCount(8, "markup linked items") };
        for (var item = 0; item < data.LinkedItemCount; item++)
        {
            reader.Expect(PrcTypes.MarkupLinkedItem, "a markup linked item");
            reader.ReadContentEntityReference(PrcTypes.MarkupLinkedItem);
            if (bits.ReadBoolean())
            {
                reader.ReadReferenceData();
            }

            for (var flag = 0; flag < 4; flag++)
            {
                bits.ReadBoolean();
            }

            reader.SkipNewData(PrcTypes.MarkupLinkedItem);
            reader.SkipUserData();
        }

        var leaders = bits.ReadCount(8, "leaders");
        for (var leader = 0; leader < leaders; leader++)
        {
            reader.Expect(PrcTypes.Leader, "a leader");
            data.Leaders.Add(ReadMarkup(reader, PrcTypes.Leader));
        }

        var markups = bits.ReadCount(8, "markups");
        for (var markup = 0; markup < markups; markup++)
        {
            reader.Expect(PrcTypes.MarkupItem, "a markup");
            data.Markups.Add(ReadMarkup(reader, PrcTypes.MarkupItem));
        }

        data.AnnotationCount = bits.ReadCount(8, "annotation entities");
        SkipAnnotations(reader, data.AnnotationCount);
        return data;
    }

    private static PrcMarkup ReadMarkup(PrcEntityReader reader, uint type)
    {
        var bits = reader.Bits;
        var holder = new PrcTreeEntity { Type = type };
        reader.ReadPrcBaseWithGraphics(holder);
        uint markupType = 0;
        uint subType = 0;
        if (type == PrcTypes.MarkupItem)
        {
            markupType = bits.ReadUnsignedInteger();
            subType = bits.ReadUnsignedInteger();
            var linked = bits.ReadCount(4, "linked items of a markup");
            for (var item = 0; item < linked; item++)
            {
                reader.ReadReferenceUniqueIdentifier();
            }

            var leaders = bits.ReadCount(4, "leaders of a markup");
            for (var leader = 0; leader < leaders; leader++)
            {
                reader.ReadReferenceUniqueIdentifier();
            }
        }
        else
        {
            if (bits.ReadBoolean())
            {
                reader.ReadReferenceData();
            }

            if (bits.ReadBoolean())
            {
                reader.ReadReferenceData();
            }
        }

        var markup = new PrcMarkup
        {
            Type = type,
            Name = holder.Name,
            Graphics = holder.Graphics,
            UniqueId = holder.UniqueId,
            MarkupType = markupType,
            SubType = subType,
            TessellationIndex = (int)bits.ReadUnsignedInteger() - 1
        };
        reader.SkipNewData(type);
        reader.SkipUserData();
        return markup;
    }

    private static void SkipAnnotations(PrcEntityReader reader, int count)
    {
        var bits = reader.Bits;
        var pending = new Stack<PrcPendingAnnotations>();
        pending.Push(new PrcPendingAnnotations(count, false));
        while (pending.Count > 0)
        {
            var level = pending.Peek();
            if (level.Remaining == 0)
            {
                pending.Pop();
                if (level.InSet)
                {
                    reader.SkipNewData(PrcTypes.AnnotationSet);
                    reader.SkipUserData();
                }

                continue;
            }

            level.Remaining--;
            var type = bits.ReadUnsignedInteger();
            switch (type)
            {
                case PrcTypes.AnnotationItem:
                    reader.SkipPrcBaseWithGraphics();
                    reader.ReadReferenceUniqueIdentifier();
                    reader.SkipNewData(type);
                    reader.SkipUserData();
                    break;
                case PrcTypes.AnnotationSet:
                    reader.SkipPrcBaseWithGraphics();
                    pending.Push(new PrcPendingAnnotations(bits.ReadCount(8, "entities of an annotation set"), true));
                    break;
                case PrcTypes.AnnotationReference:
                    reader.SkipPrcBaseWithGraphics();
                    var linked = bits.ReadCount(4, "items of an annotation reference");
                    for (var item = 0; item < linked; item++)
                    {
                        reader.ReadReferenceUniqueIdentifier();
                    }

                    reader.SkipNewData(type);
                    reader.SkipUserData();
                    break;
                default:
                    reader.SkipUnknown(type, "an annotation entity");
                    break;
            }
        }
    }
}

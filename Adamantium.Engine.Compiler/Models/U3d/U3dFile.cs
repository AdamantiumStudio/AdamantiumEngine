using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading;

namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>A U3D file (ECMA-363) read into its palettes: the nodes, the model resources' geometry and shading, the
/// shaders, materials, textures, lights and views, each by its name - a later object of a name replacing the earlier,
/// as a palette has it. What the file holds and the reader leaves out is listed in <see cref="Notes"/>.</summary>
internal sealed class U3dFile
{
    /// <summary>The profile bit of a file whose compressed fields are written plain.</summary>
    internal const uint NoCompressionProfile = 0x4;

    /// <summary>The profile bit of a file that gives its units.</summary>
    internal const uint DefinedUnitsProfile = 0x8;

    private const uint KnownProfileBits = 0x2 | NoCompressionProfile | DefinedUnitsProfile;
    private const uint Utf8 = 106;
    private const string LocatedKey = "U3dBlock";

    private readonly HashSet<string> told = new(StringComparer.Ordinal);
    private readonly Dictionary<uint, U3dExtension> extensionBlocks = [];
    private readonly Dictionary<string, int> nodeIndex = new(StringComparer.Ordinal);

    internal short MajorVersion { get; private set; }

    internal short MinorVersion { get; private set; }

    internal uint Profile { get; private set; }

    /// <summary>Metres a unit, when the file defines its units.</summary>
    internal double? UnitsScalingFactor { get; private set; }

    internal bool NoCompression => (Profile & NoCompressionProfile) != 0;

    /// <summary>The nodes in the order the file declares them.</summary>
    internal List<U3dNode> Nodes { get; } = [];

    internal Dictionary<string, U3dGeometry> Geometries { get; } = new(StringComparer.Ordinal);

    /// <summary>The shading modifiers of node chains, by node.</summary>
    internal Dictionary<string, U3dShadingModifier> NodeShading { get; } = new(StringComparer.Ordinal);

    /// <summary>The shading modifiers of model resource chains, by resource.</summary>
    internal Dictionary<string, U3dShadingModifier> ResourceShading { get; } = new(StringComparer.Ordinal);

    internal Dictionary<string, U3dShader> Shaders { get; } = new(StringComparer.Ordinal);

    internal Dictionary<string, U3dMaterial> Materials { get; } = new(StringComparer.Ordinal);

    internal Dictionary<string, U3dTexture> Textures { get; } = new(StringComparer.Ordinal);

    internal Dictionary<string, U3dLightResource> Lights { get; } = new(StringComparer.Ordinal);

    internal Dictionary<string, U3dViewResource> Views { get; } = new(StringComparer.Ordinal);

    /// <summary>What the file has that the reader leaves out, each said once.</summary>
    internal List<string> Notes { get; } = [];

    /// <summary>Reads <paramref name="bytes"/>. Throws <see cref="InvalidDataException"/> for bytes that are not U3D,
    /// are cut short or are broken, naming the block and where it starts.</summary>
    internal static U3dFile Read(byte[] bytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length < 4 || BitConverter.ToUInt32(bytes, 0) != U3dBlockTypes.FileHeader)
        {
            throw new InvalidDataException("This is not a U3D file: it does not start with a U3D file header block");
        }

        var file = new U3dFile();
        var header = U3dBlockReader.ReadBlock(bytes, 0, bytes.Length);
        var end = file.ReadHeader(header, bytes.Length);
        var pending = new Stack<U3dPendingBlock>();
        PushAll(pending, U3dBlockReader.ReadBlocks(bytes, header.End, end), null);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = pending.Pop();
            file.Dispatch(next.Block, next.Chain, pending);
        }

        return file;
    }

    private static void PushAll(Stack<U3dPendingBlock> pending, List<U3dBlock> blocks, U3dChain chain)
    {
        for (var index = blocks.Count - 1; index >= 0; index--)
        {
            pending.Push(new U3dPendingBlock(blocks[index], chain));
        }
    }

    private void Tell(string note)
    {
        if (told.Add(note))
        {
            Notes.Add(note);
        }
    }

    private int ReadHeader(U3dBlock header, int length)
    {
        var reader = header.OpenData(true);
        ulong fileSize;
        uint encoding;
        try
        {
            MajorVersion = reader.ReadI16();
            MinorVersion = reader.ReadI16();
            Profile = reader.ReadU32();
            reader.ReadU32();
            fileSize = reader.ReadU64();
            encoding = reader.ReadU32();
            if ((Profile & DefinedUnitsProfile) != 0)
            {
                UnitsScalingFactor = reader.ReadF64();
            }

            reader.EnsureWithinData();
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException($"The U3D file header is broken: {exception.Message}", exception);
        }

        if ((Profile & ~KnownProfileBits) != 0)
        {
            Tell($"profile bits 0x{Profile & ~KnownProfileBits:X} of the file header are not known");
        }

        if (encoding != Utf8)
        {
            Tell($"strings are said to be in character set {encoding}; they are read as UTF-8, the only one U3D has");
        }

        if (fileSize > (ulong)length)
        {
            throw new InvalidDataException(
                $"The U3D file is cut short: its header gives {fileSize} bytes, and there are {length}");
        }

        if (fileSize == 0)
        {
            return length;
        }

        if (fileSize < (ulong)header.End)
        {
            throw new InvalidDataException($"The U3D file header gives a file size of {fileSize}, less than the header itself");
        }

        if (fileSize < (ulong)length)
        {
            Tell($"{(ulong)length - fileSize} bytes past the file size the header gives are left out");
        }

        return (int)fileSize;
    }

    private void Dispatch(U3dBlock block, U3dChain chain, Stack<U3dPendingBlock> pending)
    {
        try
        {
            if (block.Type == U3dBlockTypes.ModifierChain)
            {
                if (chain != null)
                {
                    Tell($"a modifier chain inside modifier chain {chain.Name} is skipped");
                    return;
                }

                ReadChain(block, pending);
                return;
            }

            var reader = block.OpenData(NoCompression);
            if (Parse(block, chain, reader))
            {
                reader.EnsureWithinData();
            }
        }
        catch (InvalidDataException exception) when (!exception.Data.Contains(LocatedKey))
        {
            var located = new InvalidDataException(
                $"U3D block 0x{block.Type:X8} at byte {block.Offset}: {exception.Message}", exception);
            located.Data[LocatedKey] = block.Offset;
            throw located;
        }
    }

    private bool Parse(U3dBlock block, U3dChain chain, U3dBitReader reader)
    {
        switch (block.Type)
        {
            case U3dBlockTypes.PriorityUpdate:
                reader.ReadU32();
                return true;
            case U3dBlockTypes.NewObjectType:
                ReadNewObjectType(reader);
                return true;
            case U3dBlockTypes.FileReference:
                Tell($"the reference to another U3D file ({reader.ReadString()}) is not followed: what it holds is left out");
                return false;
            case U3dBlockTypes.GroupNode:
                AddNode(ReadNode(reader, U3dNodeKind.Group, block));
                return true;
            case U3dBlockTypes.ModelNode:
                AddNode(ReadNode(reader, U3dNodeKind.Model, block));
                return true;
            case U3dBlockTypes.LightNode:
                AddNode(ReadNode(reader, U3dNodeKind.Light, block));
                return true;
            case U3dBlockTypes.ViewNode:
                AddNode(ReadNode(reader, U3dNodeKind.View, block));
                return true;
            case U3dBlockTypes.MeshDeclaration:
                AddGeometry(U3dGeometryReader.ReadDeclaration(reader, U3dGeometryKind.Mesh));
                return true;
            case U3dBlockTypes.PointSetDeclaration:
                AddGeometry(U3dGeometryReader.ReadDeclaration(reader, U3dGeometryKind.PointSet));
                return true;
            case U3dBlockTypes.LineSetDeclaration:
                AddGeometry(U3dGeometryReader.ReadDeclaration(reader, U3dGeometryKind.LineSet));
                return true;
            case U3dBlockTypes.MeshBaseContinuation:
                return Continue(reader, U3dGeometryKind.Mesh, "base mesh", U3dGeometryReader.ReadBaseMesh);
            case U3dBlockTypes.MeshProgressiveContinuation:
                return Continue(reader, U3dGeometryKind.Mesh, "resolution updates", U3dProgressiveMeshReader.Read);
            case U3dBlockTypes.PointSetContinuation:
                return Continue(reader, U3dGeometryKind.PointSet, "points", U3dGeometryReader.ReadPointContinuation);
            case U3dBlockTypes.LineSetContinuation:
                return Continue(reader, U3dGeometryKind.LineSet, "lines", U3dGeometryReader.ReadLineContinuation);
            case U3dBlockTypes.ShadingModifier:
                ReadShadingModifier(reader, chain);
                return true;
            case U3dBlockTypes.GlyphModifier:
                Tell("2D glyph modifiers (text drawn as outlines) are read past: their glyphs are left out");
                return false;
            case U3dBlockTypes.SubdivisionModifier:
                Tell("subdivision modifiers are read past: meshes are kept as the file gives them, not subdivided");
                return false;
            case U3dBlockTypes.AnimationModifier:
                Tell("animation modifiers are read past: the scene is kept at rest");
                return false;
            case U3dBlockTypes.BoneWeightModifier:
                Tell("bone weight modifiers are read past: meshes are not bound to their skeletons");
                return false;
            case U3dBlockTypes.ClodModifier:
                Tell("CLOD modifiers (the level of detail to show) are read past: meshes are kept at the full resolution read");
                return false;
            case U3dBlockTypes.MotionResource:
                Tell("motion resources (key-frame animation) are read past");
                return false;
            case U3dBlockTypes.LightResource:
                AddLight(reader);
                return true;
            case U3dBlockTypes.ViewResource:
                AddView(reader);
                return true;
            case U3dBlockTypes.LitTextureShader:
                AddShader(reader, block);
                return true;
            case U3dBlockTypes.MaterialResource:
                AddMaterial(reader);
                return true;
            case U3dBlockTypes.TextureDeclaration:
                AddTexture(reader, block);
                return true;
            case U3dBlockTypes.TextureContinuation:
                ReadTextureContinuation(reader, block);
                return false;
            default:
                if (extensionBlocks.TryGetValue(block.Type, out var extension))
                {
                    Tell($"the objects of extension {extension.Name} ({extension.Vendor}, {extension.Id}) are read past");
                }
                else
                {
                    Tell($"block type 0x{block.Type:X8} is not one this reader knows, and is skipped");
                }

                return false;
        }
    }

    private bool Continue(U3dBitReader reader, U3dGeometryKind kind, string what, Action<U3dBitReader, U3dGeometry> read)
    {
        var name = reader.ReadString();
        if (!Geometries.TryGetValue(name, out var geometry) || geometry.Kind != kind)
        {
            Tell($"the {what} of {name}, which the file does not declare, are skipped");
            return false;
        }

        read(reader, geometry);
        return true;
    }

    private void ReadChain(U3dBlock block, Stack<U3dPendingBlock> pending)
    {
        var reader = block.OpenData(true);
        var name = reader.ReadString();
        var type = reader.ReadU32();
        var attributes = reader.ReadU32();
        var bounds = ((attributes & 0x1) != 0 ? 4 : 0) + ((attributes & 0x2) != 0 ? 6 : 0);
        for (var value = 0; value < bounds; value++)
        {
            reader.ReadF32();
        }

        reader.EnsureWithinData();
        var countOffset = block.DataOffset + (int)((reader.PlainBytesRead + 3) & ~3L);
        var dataEnd = block.DataOffset + block.DataSize;
        if (countOffset + 4 > dataEnd)
        {
            throw new InvalidDataException($"Modifier chain {name} has no room for its modifier count");
        }

        var count = BitConverter.ToUInt32(block.Source, countOffset);
        if (type > U3dChain.TextureChain)
        {
            Tell($"modifier chain {name} is of type {type}, which is not known; its modifiers are read as they come");
        }

        var modifiers = U3dBlockReader.ReadBlocks(block.Source, countOffset + 4, dataEnd);
        if (modifiers.Count != count)
        {
            Tell($"modifier chain {name} says it holds {count} modifiers and holds {modifiers.Count}");
        }

        PushAll(pending, modifiers, new U3dChain(name, type));
    }

    private void ReadNewObjectType(U3dBitReader reader)
    {
        var name = reader.ReadString();
        reader.ReadU32();
        var first = reader.ReadU32();
        var second = reader.ReadU16();
        var third = reader.ReadU16();
        var rest = reader.ReadBytes(8);
        var declarationType = reader.ReadU32();
        var continuationCount = reader.ReadU32();
        reader.CheckCount(continuationCount, 32, "continuation block types");
        var types = new List<uint> { declarationType };
        for (var index = 0u; index < continuationCount; index++)
        {
            types.Add(reader.ReadU32());
        }

        var vendor = reader.ReadString();
        var urlCount = reader.ReadU32();
        reader.CheckCount(urlCount, 16, "extension URLs");
        for (var index = 0u; index < urlCount; index++)
        {
            reader.ReadString();
        }

        reader.ReadString();
        var extension = new U3dExtension
        {
            Name = name, Vendor = vendor, Id = new Guid(first, second, third, rest[0], rest[1], rest[2], rest[3], rest[4],
                rest[5], rest[6], rest[7])
        };
        foreach (var type in types)
        {
            extensionBlocks[type] = extension;
        }
    }

    private U3dNode ReadNode(U3dBitReader reader, U3dNodeKind kind, U3dBlock block)
    {
        var node = new U3dNode { Kind = kind, Name = reader.ReadString(), MetaData = block.MetaData };
        var parentCount = reader.ReadU32();
        reader.CheckCount(parentCount, 16 + 16 * 32, "parents");
        for (var index = 0u; index < parentCount; index++)
        {
            var parent = reader.ReadString();
            var elements = new float[16];
            for (var element = 0; element < elements.Length; element++)
            {
                elements[element] = reader.ReadF32();
            }

            node.Parents.Add(new U3dParent
            {
                Name = parent,
                Transform = new Matrix4x4F(elements[0], elements[1], elements[2], elements[3], elements[4], elements[5],
                    elements[6], elements[7], elements[8], elements[9], elements[10], elements[11], elements[12],
                    elements[13], elements[14], elements[15])
            });
        }

        switch (kind)
        {
            case U3dNodeKind.Model:
                node.ResourceName = reader.ReadString();
                node.Visibility = reader.ReadU32();
                break;
            case U3dNodeKind.Light:
                node.ResourceName = reader.ReadString();
                break;
            case U3dNodeKind.View:
                node.ResourceName = reader.ReadString();
                node.View = ReadViewNode(reader);
                break;
        }

        return node;
    }

    private static U3dViewNodeParameters ReadViewNode(U3dBitReader reader)
    {
        var view = new U3dViewNodeParameters { Attributes = reader.ReadU32() };
        view.NearClip = reader.ReadF32();
        view.FarClip = reader.ReadF32();
        switch (view.ProjectionMode)
        {
            case U3dViewNodeParameters.ThreePointPerspective:
                view.FieldOfView = reader.ReadF32();
                break;
            case U3dViewNodeParameters.Orthographic:
                view.OrthographicHeight = reader.ReadF32();
                break;
            default:
                reader.ReadF32();
                reader.ReadF32();
                reader.ReadF32();
                break;
        }

        view.PortWidth = reader.ReadF32();
        view.PortHeight = reader.ReadF32();
        reader.ReadF32();
        reader.ReadF32();
        view.BackdropCount = SkipPictures(reader, "backdrops");
        view.OverlayCount = SkipPictures(reader, "overlays");
        return view;
    }

    private static uint SkipPictures(U3dBitReader reader, string what)
    {
        var count = reader.ReadU32();
        reader.CheckCount(count, 16 + 8 * 32, what);
        for (var index = 0u; index < count; index++)
        {
            reader.ReadString();
            for (var value = 0; value < 8; value++)
            {
                reader.ReadU32();
            }
        }

        return count;
    }

    private void AddNode(U3dNode node)
    {
        if (node.Name.Length == 0)
        {
            Tell("a node with no name - the name of the world - is skipped");
            return;
        }

        if (nodeIndex.TryGetValue(node.Name, out var index))
        {
            Nodes[index] = node;
            return;
        }

        nodeIndex.Add(node.Name, Nodes.Count);
        Nodes.Add(node);
    }

    private void AddGeometry(U3dGeometry geometry)
    {
        Geometries[geometry.Name] = geometry;
        if (geometry.BoneCount > 0)
        {
            Tell($"the skeleton of {geometry.Name} ({geometry.BoneCount} bones) is read past");
        }
    }

    private void ReadShadingModifier(U3dBitReader reader, U3dChain chain)
    {
        var name = reader.ReadString();
        reader.ReadU32();
        var modifier = new U3dShadingModifier { Attributes = reader.ReadU32() };
        var listCount = reader.ReadU32();
        reader.CheckCount(listCount, 32, "shader lists");
        for (var list = 0u; list < listCount; list++)
        {
            var shaderCount = reader.ReadU32();
            reader.CheckCount(shaderCount, 16, "shaders");
            var shaders = new string[shaderCount];
            for (var shader = 0; shader < shaders.Length; shader++)
            {
                shaders[shader] = reader.ReadString();
            }

            modifier.ShaderLists.Add(shaders);
        }

        if (chain == null)
        {
            Tell("a shading modifier outside a modifier chain is taken for the model resource of its name");
            ResourceShading[name] = modifier;
        }
        else if (chain.Type == U3dChain.NodeChain)
        {
            NodeShading[chain.Name] = modifier;
        }
        else
        {
            ResourceShading[chain.Name] = modifier;
        }
    }

    private void AddLight(U3dBitReader reader)
    {
        var name = reader.ReadString();
        var attributes = reader.ReadU32();
        var type = reader.ReadU8();
        var color = new Vector3F(reader.ReadF32(), reader.ReadF32(), reader.ReadF32());
        reader.ReadF32();
        Lights[name] = new U3dLightResource
        {
            Name = name,
            Attributes = attributes,
            Type = type,
            Color = color,
            ConstantAttenuation = reader.ReadF32(),
            LinearAttenuation = reader.ReadF32(),
            QuadraticAttenuation = reader.ReadF32(),
            SpotAngle = reader.ReadF32(),
            Intensity = reader.ReadF32()
        };
    }

    private void AddView(U3dBitReader reader)
    {
        var name = reader.ReadString();
        var passCount = reader.ReadU32();
        reader.CheckCount(passCount, 16 + 8 * 32, "render passes");
        var root = string.Empty;
        var fog = false;
        for (var pass = 0u; pass < passCount; pass++)
        {
            var passRoot = reader.ReadString();
            var renderAttributes = reader.ReadU32();
            for (var value = 0; value < 7; value++)
            {
                reader.ReadU32();
            }

            if (pass == 0)
            {
                root = passRoot;
            }

            fog |= (renderAttributes & 0x1) != 0;
        }

        Views[name] = new U3dViewResource { Name = name, PassCount = passCount, RootNodeName = root, FogEnabled = fog };
    }

    private void AddShader(U3dBitReader reader, U3dBlock block)
    {
        var name = reader.ReadString();
        var attributes = reader.ReadU32();
        var alphaReference = reader.ReadF32();
        var alphaFunction = reader.ReadU32();
        var blendFunction = reader.ReadU32();
        reader.ReadU32();
        var channels = reader.ReadU32();
        reader.ReadU32();
        var shader = new U3dShader
        {
            Name = name,
            Attributes = attributes,
            AlphaTestReference = alphaReference,
            AlphaTestFunction = alphaFunction,
            ColorBlendFunction = blendFunction,
            ShaderChannels = channels,
            MaterialName = reader.ReadString(),
            MetaData = block.MetaData
        };

        var layerCount = BitOperations.PopCount(channels & 0xFF);
        for (var layer = 0; layer < layerCount; layer++)
        {
            var textureName = reader.ReadString();
            var intensity = reader.ReadF32();
            var layerBlend = reader.ReadU8();
            reader.ReadU8();
            reader.ReadF32();
            var mode = reader.ReadU8();
            var transform = ReadMatrix(reader);
            ReadMatrix(reader);
            shader.Layers.Add(new U3dTextureLayer
            {
                TextureName = textureName,
                Intensity = intensity,
                BlendFunction = layerBlend,
                TextureMode = mode,
                TextureTransform = transform,
                Repeat = reader.ReadU8()
            });
        }

        Shaders[name] = shader;
    }

    private void AddMaterial(U3dBitReader reader)
    {
        var name = reader.ReadString();
        Materials[name] = new U3dMaterial
        {
            Name = name,
            Attributes = reader.ReadU32(),
            AmbientColor = ReadColor(reader),
            DiffuseColor = ReadColor(reader),
            SpecularColor = ReadColor(reader),
            EmissiveColor = ReadColor(reader),
            Reflectivity = reader.ReadF32(),
            Opacity = reader.ReadF32()
        };
    }

    private void AddTexture(U3dBitReader reader, U3dBlock block)
    {
        var name = reader.ReadString();
        var height = reader.ReadU32();
        var width = reader.ReadU32();
        reader.ReadU8();
        var texture = new U3dTexture { Name = name, Height = height, Width = width, MetaData = block.MetaData };
        var imageCount = reader.ReadU32();
        reader.CheckCount(imageCount, 64, "continuation images");
        for (var index = 0u; index < imageCount; index++)
        {
            var compression = reader.ReadU8();
            var channels = reader.ReadU8();
            var attributes = reader.ReadU16();
            var external = (attributes & 0x1) != 0;
            var image = new U3dTextureImage
            {
                CompressionType = compression,
                Channels = channels,
                IsExternal = external,
                ByteCount = external ? 0 : reader.ReadU32()
            };
            if (external)
            {
                var urlCount = reader.ReadU32();
                reader.CheckCount(urlCount, 16, "image URLs");
                for (var url = 0u; url < urlCount; url++)
                {
                    image.Urls.Add(reader.ReadString());
                }
            }

            texture.Images.Add(image);
        }

        Textures[name] = texture;
    }

    private void ReadTextureContinuation(U3dBitReader reader, U3dBlock block)
    {
        var name = reader.ReadString();
        var index = reader.ReadU32();
        reader.EnsureWithinData();
        if (!Textures.TryGetValue(name, out var texture))
        {
            Tell($"image data of texture {name}, which the file does not declare, is skipped");
            return;
        }

        if (index >= texture.Images.Count)
        {
            Tell($"image data of texture {name} for its continuation image {index}, which it does not declare, is skipped");
            return;
        }

        var start = (int)reader.PlainBytesRead;
        texture.Images[(int)index].Chunks.Add(block.Data.Slice(start).ToArray());
    }

    private static Vector3F ReadColor(U3dBitReader reader) => new(reader.ReadF32(), reader.ReadF32(), reader.ReadF32());

    private static Matrix4x4F ReadMatrix(U3dBitReader reader)
    {
        var elements = new float[16];
        for (var element = 0; element < elements.Length; element++)
        {
            elements[element] = reader.ReadF32();
        }

        return new Matrix4x4F(elements[0], elements[1], elements[2], elements[3], elements[4], elements[5], elements[6],
            elements[7], elements[8], elements[9], elements[10], elements[11], elements[12], elements[13], elements[14],
            elements[15]);
    }
}

/// <summary>A block waiting its turn, with the modifier chain it came in; null for one at the top of the file.</summary>
internal sealed class U3dPendingBlock
{
    internal U3dPendingBlock(U3dBlock block, U3dChain chain)
    {
        Block = block;
        Chain = chain;
    }

    internal U3dBlock Block { get; }

    internal U3dChain Chain { get; }
}

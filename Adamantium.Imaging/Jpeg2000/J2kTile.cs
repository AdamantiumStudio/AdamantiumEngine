using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Adamantium.Imaging.Jpeg2000;

/// <summary>Decodes one tile (ITU-T T.800, annex B, D, E, F and G): its packets in their order into code-blocks, the
/// code-blocks into coefficients, the inverse wavelet, the inverse component transform. A packet the data cuts short
/// ends the tile's packets: what came before it is kept.</summary>
internal sealed class J2kTile
{
    private const int StartOfPacket = 0xFF91;
    private const int EndOfHeader = 0xFF92;
    private const int ParallelBlocks = 8;

    private readonly J2kTileComponent[] _components;
    private readonly J2kCodingStyle _coding;
    private readonly (int X0, int Y0, int X1, int Y1) _bounds;
    private readonly J2kLimits _limits;
    private readonly int _layers;

    private J2kTile(J2kTileComponent[] components, J2kCodingStyle coding, (int, int, int, int) bounds,
        J2kLimits limits, int layers)
    {
        _components = components;
        _coding = coding;
        _bounds = bounds;
        _limits = limits;
        _layers = layers;
    }

    public (int X0, int Y0, int X1, int Y1) Bounds => _bounds;

    public J2kTileComponent[] Components => _components;

    /// <summary>The bytes of code-block data the tile's packets hold.</summary>
    public long CodedBytes { get; private set; }

    /// <summary>The bytes of code-block data the decoded resolutions and layers took.</summary>
    public long CodedBytesUsed { get; private set; }

    /// <summary>Decodes a tile without its <paramref name="discard"/> highest resolutions and with its first
    /// <paramref name="layers"/> quality layers, 0 for all.</summary>
    public static J2kTile Decode(J2kCodestream codestream, J2kTileData data, J2kLimits limits, int discard,
        int layers)
    {
        var size = codestream.SizeInfo;
        var main = codestream.Main;
        var bounds = size.Tile(data.Index);
        var coding = data.Markers.CodingStyle(main);
        var components = new J2kTileComponent[size.Components.Length];
        limits.Spend(components.Length);
        for (var c = 0; c < components.Length; c++)
        {
            components[c] = new J2kTileComponent(c, size.Components[c], data.Markers.ComponentStyle(c, main),
                data.Markers.Quantization(c, main), data.Markers.RoiShift(c, main), bounds, limits);
            components[c].Top = components[c].Style.Levels - Math.Min(discard, components[c].Style.Levels);
        }

        var tile = new J2kTile(components, coding, bounds, limits, layers > 0 ? layers : int.MaxValue);
        tile.ReadPackets(data, data.Markers.ProgressionChanges(main));
        tile.Reconstruct();
        return tile;
    }

    private void ReadPackets(J2kTileData data, IReadOnlyList<J2kProgressionChange> changes)
    {
        var body = data.Body();
        var packed = data.PackedHeaders();
        var headers = packed == null ? null : new J2kHeaderBits(packed, 0, packed.Length);
        var bodyBits = new J2kHeaderBits(body, 0, body.Length);
        var order = J2kPacketOrder.Of(_components, _bounds, _coding.Layers, _coding.Progression, changes, _limits);
        try
        {
            foreach (var packet in order)
            {
                if (bodyBits.Position >= body.Length && headers == null)
                {
                    break;
                }

                ReadPacket(packet, headers ?? bodyBits, bodyBits, body);
            }
        }
        catch (InvalidDataException)
        {
        }
    }

    private void ReadPacket(J2kPacket packet, J2kHeaderBits header, J2kHeaderBits bodyBits, byte[] body)
    {
        var component = _components[packet.Component];
        var resolution = component.Resolutions[packet.Resolution];
        var precinct = resolution.Precinct(packet.Precinct, _limits);
        if (_coding.StartOfPacketMarkers)
        {
            bodyBits.SkipMarker(StartOfPacket, 6);
        }

        var keep = packet.Layer < _layers && packet.Resolution <= component.Top;
        var contributions = new List<(J2kSegment Segment, int Length)>();
        var bit = header.ReadBit();
        if (bit == 1)
        {
            foreach (var part in precinct)
            {
                for (var i = 0; i < part.Blocks.Length; i++)
                {
                    ReadBlockHeader(part, i, packet.Layer, header, component.Style, keep, contributions);
                }
            }
        }

        header.Align();
        if (_coding.EndOfHeaderMarkers)
        {
            header.SkipMarker(EndOfHeader, 2);
        }

        var at = header == bodyBits ? header.Position : bodyBits.Position;
        foreach (var (segment, length) in contributions)
        {
            var take = Math.Min(length, body.Length - at);
            CodedBytes += Math.Max(0, take);
            if (take > 0 && keep)
            {
                segment.Add(body, at, take);
                CodedBytesUsed += take;
            }

            at += Math.Max(0, take);
            if (take < length)
            {
                throw new InvalidDataException("A JPEG 2000 packet is longer than its tile.");
            }
        }

        bodyBits.MoveTo(at);
    }

    private static void ReadBlockHeader(J2kPrecinctBand part, int index, int layer, J2kHeaderBits header,
        J2kComponentStyle style, bool keep, List<(J2kSegment, int)> contributions)
    {
        var block = part.Blocks[index];
        bool included;
        var first = !block.Included;
        if (first)
        {
            included = part.Inclusion.Decode(header, index, layer + 1);
        }
        else
        {
            included = header.ReadBit() == 1;
        }

        if (!included)
        {
            return;
        }

        if (first)
        {
            block.ZeroPlanes = part.ZeroPlanes.Value(header, index);
            block.Included = true;
        }

        var passes = PassCount(header);
        while (header.ReadBit() == 1)
        {
            block.LengthBits++;
            if (block.LengthBits > 32)
            {
                throw new InvalidDataException("A JPEG 2000 code-block length is longer than any can be.");
            }
        }

        var pass = block.Passes;
        var left = passes;
        while (left > 0)
        {
            var count = 1;
            while (count < left && !J2kCodeBlock.StartsSegment(pass + count, style))
            {
                count++;
            }

            J2kSegment segment;
            if (J2kCodeBlock.StartsSegment(pass, style) || block.Segments.Count == 0)
            {
                segment = new J2kSegment(pass);
                block.Segments.Add(segment);
            }
            else
            {
                segment = block.Segments[^1];
            }

            var bits = block.LengthBits + Log2(count);
            if (bits > 30)
            {
                throw new InvalidDataException("A JPEG 2000 code-block length is longer than any can be.");
            }

            var length = header.ReadBits(bits);
            segment.Passes += count;
            if (keep)
            {
                segment.KeptPasses += count;
                block.KeptPasses += count;
            }

            contributions.Add((segment, length));
            pass += count;
            left -= count;
        }

        block.Passes = pass;
    }

    private static int PassCount(J2kHeaderBits header)
    {
        if (header.ReadBit() == 0)
        {
            return 1;
        }

        if (header.ReadBit() == 0)
        {
            return 2;
        }

        var two = header.ReadBits(2);
        if (two < 3)
        {
            return 3 + two;
        }

        var five = header.ReadBits(5);
        return five < 31 ? 6 + five : 37 + header.ReadBits(7);
    }

    private static int Log2(int value)
    {
        var log = 0;
        while (value > 1)
        {
            value >>= 1;
            log++;
        }

        return log;
    }

    private void Reconstruct()
    {
        var blocks = new List<(J2kCodeBlock Block, J2kBand Band, J2kTileComponent Component)>();
        foreach (var component in _components)
        {
            for (var r = 0; r <= component.Top; r++)
            {
                var resolution = component.Resolutions[r];
                foreach (var band in resolution.Bands)
                {
                    var count = (long)band.Width * band.Height;
                    _limits.Spend(count);
                    if (component.Style.Reversible)
                    {
                        band.Integers = new int[Math.Max(0, count)];
                    }
                    else
                    {
                        band.Values = new float[Math.Max(0, count)];
                    }
                }

                foreach (var precinct in resolution.Precincts)
                {
                    if (precinct == null)
                    {
                        continue;
                    }

                    foreach (var part in precinct)
                    {
                        foreach (var block in part.Blocks)
                        {
                            if (block.KeptPasses > 0)
                            {
                                blocks.Add((block, part.Band, component));
                            }
                        }
                    }
                }
            }
        }

        if (blocks.Count < ParallelBlocks)
        {
            var decoder = new J2kBlockDecoder();
            foreach (var (block, band, component) in blocks)
            {
                decoder.Decode(block, band, component.Style, component.RoiShift);
            }
        }
        else
        {
            Parallel.ForEach(blocks, () => new J2kBlockDecoder(), (item, _, decoder) =>
            {
                decoder.Decode(item.Block, item.Band, item.Component.Style, item.Component.RoiShift);
                return decoder;
            }, _ => { });
        }

        foreach (var component in _components)
        {
            component.Samples = component.OutputWidth > 0 && component.OutputHeight > 0
                ? J2kWavelet.Inverse(component)
                : [];
        }

        if (_coding.ComponentTransform && _components.Length >= 3)
        {
            InverseComponentTransform();
        }
    }

    private void InverseComponentTransform()
    {
        var y0 = _components[0].Samples;
        var y1 = _components[1].Samples;
        var y2 = _components[2].Samples;
        if (y1.Length != y0.Length || y2.Length != y0.Length)
        {
            return;
        }

        if (_components[0].Style.Reversible)
        {
            for (var i = 0; i < y0.Length; i++)
            {
                var g = (int)y0[i] - (int)Math.Floor((y2[i] + y1[i]) / 4);
                var r = (int)y2[i] + g;
                var b = (int)y1[i] + g;
                y0[i] = r;
                y1[i] = g;
                y2[i] = b;
            }

            return;
        }

        for (var i = 0; i < y0.Length; i++)
        {
            var y = y0[i];
            var cb = y1[i];
            var cr = y2[i];
            y0[i] = y + 1.402f * cr;
            y1[i] = y - 0.34413f * cb - 0.71414f * cr;
            y2[i] = y + 1.772f * cb;
        }
    }
}

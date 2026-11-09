using System.Collections.Generic;
using System.IO;

namespace Adamantium.Imaging.Jpeg2000;

/// <summary>The coding markers of a main header or of a tile's first tile-part header (ITU-T T.800, A.6): COD, COC,
/// QCD, QCC, RGN and POC. A tile's set falls back on the main header's as A.6 orders it.</summary>
internal sealed class J2kMarkerSet
{
    public const int Cod = 0xFF52;
    public const int Coc = 0xFF53;
    public const int Rgn = 0xFF5E;
    public const int Qcd = 0xFF5C;
    public const int Qcc = 0xFF5D;
    public const int Poc = 0xFF5F;

    private readonly Dictionary<int, J2kComponentStyle> _componentStyles = [];
    private readonly Dictionary<int, J2kQuantization> _componentQuantizations = [];
    private readonly Dictionary<int, int> _roiShifts = [];
    private readonly List<J2kProgressionChange> _progressionChanges = [];
    private J2kCodingStyle _codingStyle;
    private J2kQuantization _quantization;

    /// <summary>Reads one marker segment of the set; false for a marker that is not one of them.</summary>
    public bool Read(int marker, BigEndianReader segment, int components)
    {
        switch (marker)
        {
            case Cod:
                _codingStyle = J2kCodingStyle.Read(segment);
                return true;
            case Coc:
            {
                var component = Component(segment, components);
                var flags = segment.ReadByte();
                _componentStyles[component] = J2kComponentStyle.Read(segment, (flags & 1) != 0);
                return true;
            }
            case Qcd:
                _quantization = J2kQuantization.Read(segment);
                return true;
            case Qcc:
                _componentQuantizations[Component(segment, components)] = J2kQuantization.Read(segment);
                return true;
            case Rgn:
            {
                var component = Component(segment, components);
                segment.ReadByte();
                _roiShifts[component] = segment.ReadByte();
                return true;
            }
            case Poc:
                while (segment.Remaining > 0)
                {
                    var firstResolution = segment.ReadByte();
                    var firstComponent = components < 257 ? segment.ReadByte() : segment.ReadUInt16();
                    var endLayer = segment.ReadUInt16();
                    var endResolution = segment.ReadByte();
                    int endComponent = components < 257 ? segment.ReadByte() : segment.ReadUInt16();
                    if (endComponent == 0)
                    {
                        endComponent = components < 257 ? 256 : 16384;
                    }

                    var order = segment.ReadByte();
                    if (order > 4)
                    {
                        throw new InvalidDataException("A JPEG 2000 progression change has no order " + order + ".");
                    }

                    _progressionChanges.Add(new J2kProgressionChange(firstResolution, firstComponent, endLayer,
                        endResolution, endComponent, (J2kProgression)order));
                }

                return true;
            default:
                return false;
        }
    }

    public J2kCodingStyle CodingStyle(J2kMarkerSet main) =>
        _codingStyle ?? main?._codingStyle ?? throw new InvalidDataException("A JPEG 2000 codestream has no COD.");

    public J2kComponentStyle ComponentStyle(int component, J2kMarkerSet main)
    {
        if (_componentStyles.TryGetValue(component, out var own))
        {
            return own;
        }

        if (_codingStyle != null)
        {
            return _codingStyle.Component;
        }

        return main != null ? main.ComponentStyle(component, null) : CodingStyle(null).Component;
    }

    public J2kQuantization Quantization(int component, J2kMarkerSet main)
    {
        if (_componentQuantizations.TryGetValue(component, out var own))
        {
            return own;
        }

        if (_quantization != null)
        {
            return _quantization;
        }

        return main?.Quantization(component, null) ??
               throw new InvalidDataException("A JPEG 2000 codestream has no QCD.");
    }

    public int RoiShift(int component, J2kMarkerSet main) =>
        _roiShifts.TryGetValue(component, out var shift) ? shift : main?.RoiShift(component, null) ?? 0;

    public IReadOnlyList<J2kProgressionChange> ProgressionChanges(J2kMarkerSet main) =>
        _progressionChanges.Count > 0 || main == null ? _progressionChanges : main._progressionChanges;

    private static int Component(BigEndianReader segment, int components) =>
        components < 257 ? segment.ReadByte() : segment.ReadUInt16();
}

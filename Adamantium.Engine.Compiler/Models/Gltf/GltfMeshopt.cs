using System;
using System.IO;

namespace Adamantium.Engine.Compiler.Models.Gltf;

/// <summary>Unpacks buffer views compressed by <c>KHR_meshopt_compression</c> or <c>EXT_meshopt_compression</c>, as the
/// Khronos extension specifies them: attributes of versions 0 and 1 (<see cref="GltfMeshoptAttributes"/>), triangles
/// and indices (<see cref="GltfMeshoptIndexCodecs"/>), then the octahedral, quaternion, exponential or color filter
/// (<see cref="GltfMeshoptFilters"/>).</summary>
public static class GltfMeshopt
{
    /// <summary>The <paramref name="compression"/>'s <c>count</c> elements unpacked from <paramref name="data"/>,
    /// the bytes it points at.</summary>
    /// <exception cref="InvalidDataException">The stream is broken or does not fit its description - checked before
    /// anything its size is made.</exception>
    public static byte[] Decode(GltfMeshoptCompression compression, ReadOnlySpan<byte> data)
    {
        var (mode, filter, count, stride) = (compression.Mode, compression.Filter ?? "NONE", compression.Count,
            compression.ByteStride);
        Check(mode, filter, count, stride);
        var minimum = mode switch
        {
            "ATTRIBUTES" => GltfMeshoptAttributes.MinimumSize(data, count, stride),
            "TRIANGLES" => GltfMeshoptIndexCodecs.MinimumTriangleSize(count),
            _ => GltfMeshoptIndexCodecs.MinimumIndexSize(count)
        };
        if (data.Length < minimum || (long)count * stride > Array.MaxLength)
        {
            throw new InvalidDataException($"meshopt data of {data.Length} bytes cannot hold {count} {mode.ToLowerInvariant()} "
                                           + $"of {stride} bytes.");
        }

        var output = new byte[count * stride];
        switch (mode)
        {
            case "ATTRIBUTES":
                GltfMeshoptAttributes.Decode(data, output, count, stride);
                GltfMeshoptFilters.Apply(filter, output, stride);
                break;
            case "TRIANGLES":
                GltfMeshoptIndexCodecs.DecodeTriangles(data, output, count, stride);
                break;
            default:
                GltfMeshoptIndexCodecs.DecodeIndices(data, output, count, stride);
                break;
        }

        return output;
    }

    private static void Check(string mode, string filter, int count, int stride)
    {
        if (count < 0)
        {
            throw new InvalidDataException($"meshopt data of {count} elements.");
        }

        var valid = mode switch
        {
            "ATTRIBUTES" => stride is >= 4 and <= 256 && stride % 4 == 0 && GltfMeshoptFilters.Fits(filter, stride),
            "TRIANGLES" => stride is 2 or 4 && count % 3 == 0 && filter == "NONE",
            "INDICES" => stride is 2 or 4 && filter == "NONE",
            _ => throw new InvalidDataException($"meshopt data of mode {mode}.")
        };
        if (!valid)
        {
            throw new InvalidDataException($"meshopt {mode.ToLowerInvariant()} of {count} elements of {stride} bytes "
                                           + $"cannot take the {filter.ToLowerInvariant()} filter.");
        }
    }
}

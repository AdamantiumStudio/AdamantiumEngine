using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Adamantium.Engine.CompilerTests;

/// <summary>The mesh of an IDTF text file - what MeshLab hands the U3D library to write - as far as the tests compare
/// it: its positions (MODEL_POSITION_LIST) and its faces as positions (MESH_FACE_POSITION_LIST).</summary>
internal sealed class U3dTestIdtf
{
    private U3dTestIdtf(List<Vector3F> positions, List<int[]> faces)
    {
        Positions = positions;
        Faces = faces;
    }

    public List<Vector3F> Positions { get; }

    public List<int[]> Faces { get; }

    public static U3dTestIdtf Read(string path)
    {
        var lines = File.ReadAllLines(path).Select(line => line.Trim()).ToList();
        var positions = ReadSection(lines, "MODEL_POSITION_LIST")
            .Select(values => new Vector3F(ParseNumber(values[0]), ParseNumber(values[1]), ParseNumber(values[2]))).ToList();
        var faces = ReadSection(lines, "MESH_FACE_POSITION_LIST")
            .Select(values => values.Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray()).ToList();
        return new U3dTestIdtf(positions, faces);
    }

    private static float ParseNumber(string text) => float.Parse(text, CultureInfo.InvariantCulture);

    private static List<string[]> ReadSection(List<string> lines, string name)
    {
        var start = lines.IndexOf(name + " {");
        if (start < 0)
        {
            throw new InvalidDataException($"The IDTF file has no {name}");
        }

        List<string[]> rows = [];
        for (var index = start + 1; index < lines.Count && lines[index] != "}"; index++)
        {
            rows.Add(lines[index].Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        return rows;
    }
}

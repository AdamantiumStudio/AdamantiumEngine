// Ported from Draco (src/draco/compression/attributes/mesh_attribute_indices_encoding_data.h).
// Copyright 2016 The Draco Authors. Under the Apache License 2.0; translated to C# and changed, details:
// THIRD-PARTY-NOTICES.md.

using System.Collections.Generic;

namespace Adamantium.Engine.Compiler.Models.Gltf.Draco;

internal sealed class DracoEncodingData
{
    public int[] VertexToValue { get; private set; } = [];

    public List<int> ValueToCorner { get; } = [];

    public void Init(int vertices) => VertexToValue = new int[vertices];
}

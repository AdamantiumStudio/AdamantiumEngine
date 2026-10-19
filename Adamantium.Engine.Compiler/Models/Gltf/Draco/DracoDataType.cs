// Ported from Draco (src/draco/core/draco_types.h).
// Copyright 2016 The Draco Authors. Under the Apache License 2.0; translated to C# and changed, details:
// THIRD-PARTY-NOTICES.md.

namespace Adamantium.Engine.Compiler.Models.Gltf.Draco;

/// <summary>The type of a Draco attribute's components, by the numbers the bitstream gives them.</summary>
public enum DracoDataType : byte
{
    Int8 = 1,
    UInt8,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Float32,
    Float64,
    Bool
}

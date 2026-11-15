using System;
using Adamantium.Engine.Compiler.Models.Prc;

namespace Adamantium.Engine.Compiler.Models.Converters;

/// <summary>An entity as references name it: the file structure it lies in and its unique identifier there.</summary>
internal readonly struct PrcEntityKey : IEquatable<PrcEntityKey>
{
    internal PrcEntityKey(PrcUniqueId fileStructure, uint uniqueId)
    {
        FileStructure = fileStructure;
        UniqueId = uniqueId;
    }

    internal PrcUniqueId FileStructure { get; }

    internal uint UniqueId { get; }

    public bool Equals(PrcEntityKey other) => UniqueId == other.UniqueId && Equals(FileStructure, other.FileStructure);

    public override bool Equals(object other) => other is PrcEntityKey key && Equals(key);

    public override int GetHashCode() => HashCode.Combine(FileStructure, UniqueId);
}

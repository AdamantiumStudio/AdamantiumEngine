using System;

namespace Adamantium.ECS;

/// <summary>A full name a type had before: what a scene written then calls it. A world's code finds the type by it when
/// no type has that name now, so a component renamed or moved to another namespace keeps the values its scenes gave it.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class MovedFromAttribute : Attribute
{
    public MovedFromAttribute(string fullName)
    {
        FullName = fullName;
    }

    /// <summary>The full name the type had: namespace and name.</summary>
    public string FullName { get; }
}

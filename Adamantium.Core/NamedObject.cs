using System;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace Adamantium.Core;

/// <summary>An object with a name - fixed once given, unless made changeable - and a tag of the owner's.</summary>
[DataContract]
public abstract class NamedObject : INamedObject
{
    private string _name = string.Empty;

    /// <summary>An object of no name, which may be named later.</summary>
    protected NamedObject()
    {
    }

    /// <summary>An object named <paramref name="name"/>, fixed unless <paramref name="isImmutable"/> is false.</summary>
    protected NamedObject(string name, bool isImmutable = true)
    {
        _name = name;
        IsNameImmutable = isImmutable;
    }

    /// <summary>Whether <see cref="Name"/> refuses to change.</summary>
    public bool IsNameImmutable { get; set; }

    /// <summary>The object's name; setting it on a fixed name throws <see cref="ArgumentException"/>.</summary>
    [DefaultValue("")]
    [DataMember(Order = 1)]
    public string Name
    {
        get => _name;
        set => _name = IsNameImmutable
            ? throw new ArgumentException("The name of this object cannot change.", nameof(value))
            : value;
    }

    /// <summary>Whether the object has a name that is not empty.</summary>
    public bool HasName => !string.IsNullOrEmpty(Name);

    /// <summary>Whatever the owner keeps with the object.</summary>
    [DefaultValue(null)]
    [DataMember(Order = 2, EmitDefaultValue = false)]
    public object Tag { get; set; }
}

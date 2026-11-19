namespace Adamantium.MVVM.Generators;

/// <summary>The modifiers a partial property was declared with, which its implementation repeats: the property's
/// (<c>public virtual</c>), each accessor's own (<c>private </c>, or empty) and whether its setter is <c>init</c>.</summary>
internal sealed record PropertyAccess(string Modifiers, string Getter, string Setter, string SetterKeyword, bool IsPublic,
    bool GetterIsPublic, bool SetterIsPublic)
{
    /// <summary>A field-backed property: all public.</summary>
    public static PropertyAccess Public { get; } = new("public", "", "", "set", true, true, true);
}

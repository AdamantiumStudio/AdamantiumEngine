using System;

namespace Adamantium.MVVM;

/// <summary>With <see cref="BindableAttribute"/>, also raises change notification for the named members; naming a generated
/// command re-raises its <c>CanExecuteChanged</c>.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
public sealed class AffectsAttribute : Attribute
{
    public AffectsAttribute(params string[] memberNames) => MemberNames = memberNames;

    public string[] MemberNames { get; }
}

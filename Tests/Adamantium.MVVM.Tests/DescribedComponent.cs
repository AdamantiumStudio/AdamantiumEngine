namespace Adamantium.MVVM.Tests;

/// <summary>A base that asks for its properties and its descendants' to be described, without notifying.</summary>
[DescribeProperties]
public abstract class DescribedComponent
{
    public double Width { get; set; }
}

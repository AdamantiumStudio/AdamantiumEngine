namespace Adamantium.Engine.Tools;

/// <summary>
/// Scales the selected entity about its pivot, along its own axes or evenly - or several together about their common
/// center, along the world's axes.
/// </summary>
public class ScaleTool : PointerTool
{
    public ScaleTool()
        : base(new ScaleHandles())
    {
    }

    protected override bool WorksGroups => true;
}

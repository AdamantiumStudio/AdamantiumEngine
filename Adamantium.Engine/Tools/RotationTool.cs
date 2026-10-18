namespace Adamantium.Engine.Tools;

/// <summary>
/// Turns the selected entity about its pivot - or several together about their common center: about an axis, about
/// the view, or freely.
/// </summary>
public class RotationTool : ToolProcessor
{
    public RotationTool()
        : base(new RotationHandles())
    {
    }

    protected override bool WorksGroups => true;
}

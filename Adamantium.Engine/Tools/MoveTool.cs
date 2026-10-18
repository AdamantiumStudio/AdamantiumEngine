namespace Adamantium.Engine.Tools;

/// <summary>
/// Moves the selected entity - or several together, from their common center - along an axis, across a plane, or
/// across the view.
/// </summary>
public class MoveTool : ToolProcessor
{
    public MoveTool()
        : base(new MoveHandles())
    {
    }

    protected override bool WorksGroups => true;
}

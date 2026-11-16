namespace Adamantium.Engine.Tools;

/// <summary>
/// Moves and turns the point the selected entity turns and scales about, leaving the entity where it is.
/// </summary>
public class PivotTool : PointerTool
{
    public PivotTool()
        : base(new PivotHandles())
    {
    }
}

namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>The contexts a moved face's attribute change is read in.</summary>
internal sealed class U3dChangeContexts
{
    internal U3dChangeContexts(string keepChange, string changeType, string indexNew, string indexLocal, string indexGlobal)
    {
        KeepChange = U3dContext.CreateDynamic(keepChange);
        ChangeType = U3dContext.CreateDynamic(changeType);
        IndexNew = U3dContext.CreateDynamic(indexNew);
        IndexLocal = U3dContext.CreateDynamic(indexLocal);
        IndexGlobal = U3dContext.CreateDynamic(indexGlobal);
    }

    internal U3dContext KeepChange { get; }

    internal U3dContext ChangeType { get; }

    internal U3dContext IndexNew { get; }

    internal U3dContext IndexLocal { get; }

    internal U3dContext IndexGlobal { get; }
}

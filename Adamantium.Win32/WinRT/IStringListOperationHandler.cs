using System.Runtime.InteropServices;

namespace Adamantium.Win32.WinRT;

/// <summary>Told when an <see cref="IStringListOperation"/> ends
/// (Windows.Foundation.AsyncOperationCompletedHandler&lt;IVectorView&lt;String&gt;&gt;): a delegate, so IUnknown only.
/// </summary>
[ComImport]
[Guid("7c7899be-5f2e-5bf3-ade5-ad98b772c7cd")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IStringListOperationHandler
{
    [PreserveSig] int Invoke(IStringListOperation operation, AsyncStatus status);
}

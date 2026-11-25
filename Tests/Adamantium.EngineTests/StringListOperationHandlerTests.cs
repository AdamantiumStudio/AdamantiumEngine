using System;
using System.Runtime.InteropServices;
using Adamantium.Win32.WinRT;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>How a Windows Runtime operation ended is told apart: canceled it has no strings, failed it says the error.
/// </summary>
[TestFixture]
public class StringListOperationHandlerTests
{
    private const int InputServiceFailure = unchecked((int)0x87B20803);

    [Test]
    public void AFailedOperation_SaysItsError()
    {
        var handler = new StringListOperationHandler();

        handler.Invoke(new EndedOperation(InputServiceFailure), AsyncStatus.Error);

        var failure = Assert.ThrowsAsync<COMException>(() => handler.Task);
        Assert.That(failure.ErrorCode, Is.EqualTo(InputServiceFailure));
    }

    [Test]
    public void AFailedOperationWithNoCode_FailsAllTheSame()
    {
        var handler = new StringListOperationHandler();

        handler.Invoke(new EndedOperation(0), AsyncStatus.Error);

        Assert.ThrowsAsync<COMException>(() => handler.Task);
    }

    [Test]
    public void ResultsThatCannotBeRead_AreAFailure_NotNoStrings()
    {
        var handler = new StringListOperationHandler();

        handler.Invoke(new EndedOperation(InputServiceFailure), AsyncStatus.Completed);

        var failure = Assert.ThrowsAsync<COMException>(() => handler.Task);
        Assert.That(failure.ErrorCode, Is.EqualTo(InputServiceFailure));
    }

    [Test]
    public void ACanceledOperation_HasNoStrings()
    {
        var handler = new StringListOperationHandler();

        handler.Invoke(new EndedOperation(InputServiceFailure), AsyncStatus.Canceled);

        Assert.That(handler.Task.Result, Is.Empty);
    }

    private sealed class EndedOperation(int code) : IStringListOperation, IAsyncInfo
    {
        public int GetIids(out uint count, out IntPtr iids) => throw new NotSupportedException();

        public int GetRuntimeClassName(out IntPtr className) => throw new NotSupportedException();

        public int GetTrustLevel(out int trustLevel) => throw new NotSupportedException();

        public int SetCompleted(IStringListOperationHandler handler) => throw new NotSupportedException();

        public int GetCompleted(out IStringListOperationHandler handler) => throw new NotSupportedException();

        public int GetResults(out IStringVectorView results)
        {
            results = null;
            return code;
        }

        public int GetId(out uint id) => throw new NotSupportedException();

        public int GetStatus(out AsyncStatus status) => throw new NotSupportedException();

        public int GetErrorCode(out int errorCode)
        {
            errorCode = code;
            return 0;
        }

        public int Cancel() => 0;

        public int Close() => 0;
    }
}

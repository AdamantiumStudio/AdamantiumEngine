using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.DBus;
using NUnit.Framework;

namespace Adamantium.EngineTests;

public class DBusTests
{
    private const string BusName = "org.freedesktop.DBus";
    private const string BusPath = "/org/freedesktop/DBus";

    private const string GioMessage =
        "6c0100014c000000090000007300000001016f000c0000002f6f72672f6578616d706c650000000002017300110000006f72672e65" +
        "78616d706c652e5468696e6700000000000000060173000b0000006f72672e6578616d706c650000000000080167000d7375617b73" +
        "767d6128696929760000000000000301730002000000446f00000000000001000000780000002a0000002c000000040000006e616d" +
        "650001730005000000746f75636800000005000000636f756e740001750000000003000000000000000176000169000000fcffffff";

    [Test]
    public void ABody_IsWrittenByteForByteAsGioWritesIt()
    {
        var gio = Convert.FromHexString(GioMessage);
        var writer = new DBusWriter();
        writer.WriteString("x");
        writer.WriteUInt32(42);
        writer.WriteDictionary(new Dictionary<string, DBusVariant>
        {
            ["name"] = DBusVariant.String("touch"),
            ["count"] = DBusVariant.UInt32(3),
        });
        writer.WriteValue("a(ii)", new List<object>());
        writer.WriteVariant(new DBusVariant("v", DBusVariant.Int32(-4)));

        Assert.That(Convert.ToHexString(writer.ToArray()), Is.EqualTo(Convert.ToHexString(gio[^76..])));
    }

    [Test]
    public void GiosMessage_IsReadWhole()
    {
        var message = DBusMessage.Parse(Convert.FromHexString(GioMessage));
        var body = message.Reader();

        Assert.Multiple(() =>
        {
            Assert.That((message.Type, message.Serial), Is.EqualTo((DBusMessageType.MethodCall, 9u)));
            Assert.That((message.Destination, message.Path, message.Interface, message.Member, message.Signature),
                Is.EqualTo(("org.example", "/org/example", "org.example.Thing", "Do", "sua{sv}a(ii)v")));
            Assert.That(body.ReadString(), Is.EqualTo("x"));
            Assert.That(body.ReadUInt32(), Is.EqualTo(42u));
            var options = (Dictionary<object, object>)body.ReadValue("a{sv}");
            Assert.That(((DBusVariant)options["name"]).Value, Is.EqualTo("touch"));
            Assert.That(((DBusVariant)options["count"]).Value, Is.EqualTo(3u));
            Assert.That((List<object>)body.ReadValue("a(ii)"), Is.Empty);
            var outer = body.ReadVariant();
            Assert.That(((DBusVariant)outer.Value).Value, Is.EqualTo(-4));
            Assert.That(body.AtEnd);
        });
    }

    [Test]
    public void WhatIsRead_IsWrittenBackTheSame()
    {
        var gio = Convert.FromHexString(GioMessage);
        var reader = new DBusReader(gio[^76..]);
        var values = new[] { "s", "u", "a{sv}", "a(ii)", "v" }.Select(signature => (signature, reader.ReadValue(signature))).ToList();
        var writer = new DBusWriter();
        foreach (var (signature, value) in values)
        {
            writer.WriteValue(signature, value);
        }

        Assert.That(Convert.ToHexString(writer.ToArray()), Is.EqualTo(Convert.ToHexString(gio[^76..])));
    }

    [Test]
    public void AMessageIsReadAsItWasWritten()
    {
        var call = DBusMessage.MethodCall("org.example", "/org/example", "org.example.Thing", "Do", "su", writer =>
        {
            writer.WriteString("x");
            writer.WriteUInt32(42);
        });

        var bytes = call.Serialize(9);
        var read = DBusMessage.Parse(bytes);

        Assert.Multiple(() =>
        {
            Assert.That((bytes.Length - read.Body.Length) % 8, Is.Zero, "the body starts on an 8-byte boundary");
            Assert.That(DBusMessage.LengthOf(bytes), Is.EqualTo(bytes.Length));
            Assert.That((read.Destination, read.Path, read.Interface, read.Member, read.Signature),
                Is.EqualTo(("org.example", "/org/example", "org.example.Thing", "Do", "su")));
            Assert.That(read.Serial, Is.EqualTo(9u));
        });
    }

    [Test]
    public void ABigEndianMessage_IsRefused()
    {
        var bytes = Convert.FromHexString(GioMessage);
        bytes[0] = (byte)'B';

        Assert.Throws<DBusException>(() => DBusMessage.Parse(bytes));
    }

    [Test]
    public void TheSessionBus_NamesItsPeers()
    {
        using var connection = Session();
        var names = (List<object>)connection
            .Call(DBusMessage.MethodCall(BusName, BusPath, BusName, "ListNames")).Reader().ReadValue("as");

        Assert.That(connection.UniqueName, Does.StartWith(":"));
        Assert.That(names, Does.Contain(BusName).And.Contain(connection.UniqueName));
    }

    [Test]
    public void ACallTheBusRefuses_ThrowsItsError()
    {
        using var connection = Session();

        var error = Assert.Throws<DBusException>(() =>
            connection.Call(DBusMessage.MethodCall(BusName, BusPath, BusName, "NoSuchMethod")));
        Assert.That(error.Name, Is.EqualTo("org.freedesktop.DBus.Error.UnknownMethod"));
    }

    [Test]
    public void CallsFromManyThreads_EachGetTheirOwnReply()
    {
        using var connection = Session();

        var ids = Enumerable.Range(0, 32).AsParallel().Select(_ =>
            connection.Call(DBusMessage.MethodCall(BusName, BusPath, BusName, "GetNameOwner", "s",
                writer => writer.WriteString(connection.UniqueName))).Reader().ReadString()).ToList();

        Assert.That(ids, Is.All.EqualTo(connection.UniqueName));
    }

    [Test]
    public void ASignal_ReachesItsSubscriber_AndAThrowingHandlerStopsNothing()
    {
        using var connection = Session();
        var joined = new ConcurrentQueue<string>();
        using var arrived = new ManualResetEventSlim();
        using var thrower = connection.Subscribe(BusName, BusPath, BusName, "NameOwnerChanged",
            _ => throw new InvalidOperationException("a handler that fails"));
        using var subscription = connection.Subscribe(BusName, BusPath, BusName, "NameOwnerChanged", signal =>
        {
            var body = signal.Reader();
            var name = body.ReadString();
            body.ReadString();
            if (name == body.ReadString())
            {
                joined.Enqueue(name);
                arrived.Set();
            }
        });

        using var second = Session();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!joined.Contains(second.UniqueName) && DateTime.UtcNow < deadline)
        {
            arrived.Wait(TimeSpan.FromMilliseconds(100));
            arrived.Reset();
        }

        Assert.That(joined, Does.Contain(second.UniqueName));
        Assert.That(connection.Call(DBusMessage.MethodCall(BusName, BusPath, BusName, "GetId")).Reader().ReadString(),
            Is.Not.Empty, "the connection still answers");
    }

    [Test]
    public void AHandlerWaitingForAReply_IsRefused_ButMayAwaitOne()
    {
        using var connection = Session();
        Exception waited = null;
        string awaited = null;
        using var done = new ManualResetEventSlim();
        using var subscription = connection.Subscribe(BusName, BusPath, BusName, "NameOwnerChanged", _ =>
        {
            if (done.IsSet)
            {
                return;
            }

            waited = Assert.Catch(() => connection.Call(DBusMessage.MethodCall(BusName, BusPath, BusName, "GetId")));
            connection.CallAsync(DBusMessage.MethodCall(BusName, BusPath, BusName, "GetId")).ContinueWith(reply =>
            {
                awaited = reply.Result.Reader().ReadString();
                done.Set();
            });
        });

        using var second = Session();

        Assert.That(done.Wait(TimeSpan.FromSeconds(5)), Is.True);
        Assert.That(waited, Is.InstanceOf<InvalidOperationException>());
        Assert.That(awaited, Is.Not.Empty);
    }

    [Test]
    public void AClosedConnection_FailsWhatWaitsAndWhatComes()
    {
        var connection = Session();
        var waiting = connection.CallAsync(DBusMessage.MethodCall(BusName, BusPath, "org.freedesktop.DBus.Peer", "Ping"));
        connection.Dispose();

        Exception outcome = null;
        try
        {
            waiting.GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            outcome = e;
        }

        Assert.That(outcome, Is.Null.Or.InstanceOf<DBusException>(), "answered before the close, or failed by it");
        Assert.Throws<DBusException>(() => connection.Call(DBusMessage.MethodCall(BusName, BusPath, BusName, "GetId")));
    }

    [Test]
    public void AnObject_AnswersACallFromAnotherConnection_AndNotWhatItDoesNotServe()
    {
        using var server = Session();
        using var client = Session();
        using var served = server.Serve(call => Task.FromResult(call.Member == "Echo"
            ? DBusMessage.MethodReturn(call, "s", writer => writer.WriteString(call.Reader().ReadString() + "!"))
            : null));

        var echoed = client.Call(Echo(server, "Echo", "hi")).Reader().ReadString();
        var missing = Assert.Throws<DBusException>(() => client.Call(Echo(server, "Other", "hi")));

        Assert.That(echoed, Is.EqualTo("hi!"));
        Assert.That(missing.Name, Is.EqualTo("org.freedesktop.DBus.Error.UnknownMethod"));
    }

    [Test]
    public void AHandlerThatThrows_IsAnsweredWithAnError_AndTheObjectServesOn()
    {
        using var server = Session();
        using var client = Session();
        using var served = server.Serve(call => call.Member == "Fail"
            ? throw new InvalidOperationException("broken")
            : Task.FromResult(DBusMessage.MethodReturn(call)));

        var failure = Assert.Throws<DBusException>(() => client.Call(Echo(server, "Fail", "x")));

        Assert.That(failure.Name, Is.EqualTo("org.freedesktop.DBus.Error.Failed"));
        Assert.That(failure.Message, Does.Contain("adamantium.Test.Fail").And.Not.Contain("broken"),
            "what went wrong inside stays inside: any process on the bus may call");
        Assert.That(client.Call(Echo(server, "Fine", "x")).Type, Is.EqualTo(DBusMessageType.MethodReturn));
    }

    [Test]
    public void Calls_AreHandledOneAtATime_InTheOrderTheyCame()
    {
        using var server = Session();
        using var client = Session();
        var handled = new List<string>();
        var running = 0;
        var overlapped = false;
        using var served = server.Serve(async call =>
        {
            overlapped |= Interlocked.Increment(ref running) > 1;
            if (call.Member == "Slow")
            {
                await Task.Delay(200);
            }

            lock (handled)
            {
                handled.Add(call.Member);
            }

            Interlocked.Decrement(ref running);
            return DBusMessage.MethodReturn(call);
        });

        var slow = client.CallAsync(Echo(server, "Slow", "x"));
        var fast = client.CallAsync(Echo(server, "Fast", "x"));
        Task.WaitAll(slow, fast);

        Assert.That(handled, Is.EqualTo(new[] { "Slow", "Fast" }));
        Assert.That(overlapped, Is.False);
    }

    [Test]
    public void ANulInAString_IsSentAsAReplacement_AndTheConnectionLives()
    {
        using var server = Session();
        using var client = Session();
        using var served = server.Serve(call => Task.FromResult(DBusMessage.MethodReturn(call, "s",
            writer => writer.WriteString("a\0b"))));

        Assert.That(client.Call(Echo(server, "Nul", "x")).Reader().ReadString(), Is.EqualTo("a�b"));
        Assert.That(client.Call(Echo(server, "Again", "x")).Type, Is.EqualTo(DBusMessageType.MethodReturn));
    }

    [Test]
    public void CallsQueuedBeforeServingStopped_AreNotServed()
    {
        using var server = Session();
        using var client = Session();
        var release = new TaskCompletionSource();
        var served = server.Serve(async call =>
        {
            if (call.Member == "Hold")
            {
                await release.Task;
            }

            return DBusMessage.MethodReturn(call);
        });

        var held = client.CallAsync(Echo(server, "Hold", "x"));
        var queued = client.CallAsync(Echo(server, "Queued", "x"));
        Thread.Sleep(200);
        served.Dispose();
        release.SetResult();

        Assert.That(held.Result.Type, Is.EqualTo(DBusMessageType.MethodReturn));
        var error = Assert.Throws<AggregateException>(() => queued.Wait()).InnerException as DBusException;
        Assert.That(error?.Name, Is.EqualTo("org.freedesktop.DBus.Error.UnknownMethod"));
    }

    [Test]
    public void AConnectionServes_OnlyOneHandlerAtATime()
    {
        using var server = Session();
        using var served = server.Serve(call => Task.FromResult(DBusMessage.MethodReturn(call)));

        Assert.Throws<InvalidOperationException>(() => server.Serve(call => Task.FromResult(DBusMessage.MethodReturn(call))));
    }

    [Test]
    public void ServingStoppedOnce_IsNotStoppedAgain_ByTheSameHandlersOldServing()
    {
        using var server = Session();
        using var client = Session();
        Func<DBusMessage, Task<DBusMessage>> handler = call => Task.FromResult(DBusMessage.MethodReturn(call));
        var first = server.Serve(handler);
        first.Dispose();
        using var second = server.Serve(handler);

        first.Dispose();

        Assert.That(client.Call(Echo(server, "Still", "x")).Type, Is.EqualTo(DBusMessageType.MethodReturn));
    }

    [Test]
    public void AHandler_MayWaitForACallOverItsOwnConnection()
    {
        using var server = Session();
        using var client = Session();
        using var served = server.Serve(call =>
        {
            var id = server.Call(DBusMessage.MethodCall(BusName, BusPath, BusName, "GetId")).Reader().ReadString();
            return Task.FromResult(DBusMessage.MethodReturn(call, "s", writer => writer.WriteString(id)));
        });

        Assert.That(client.Call(Echo(server, "Id", "x")).Reader().ReadString(), Is.Not.Empty);
    }

    [Test]
    public void ASignalSent_ReachesWhoeverSubscribesToIt()
    {
        using var server = Session();
        using var client = Session();
        string heard = null;
        using var arrived = new ManualResetEventSlim();
        using var subscription = client.Subscribe(server.UniqueName, "/adamantium/test", "adamantium.Test", "Said", signal =>
        {
            heard = signal.Reader().ReadString();
            arrived.Set();
        });

        server.Send(DBusMessage.Signal("/adamantium/test", "adamantium.Test", "Said", "s", writer => writer.WriteString("hello")));

        Assert.That(arrived.Wait(TimeSpan.FromSeconds(5)), Is.True);
        Assert.That(heard, Is.EqualTo("hello"));
    }

    [Test]
    public void OnceServingStops_CallsAreNotServed_ButAPingIsAnswered()
    {
        using var server = Session();
        using var client = Session();
        server.Serve(call => Task.FromResult(DBusMessage.MethodReturn(call))).Dispose();

        Assert.Throws<DBusException>(() => client.Call(Echo(server, "Echo", "x")));
        Assert.That(client.Call(DBusMessage.MethodCall(server.UniqueName, "/", "org.freedesktop.DBus.Peer", "Ping")).Type,
            Is.EqualTo(DBusMessageType.MethodReturn));
    }

    private static DBusMessage Echo(DBusConnection server, string member, string text) =>
        DBusMessage.MethodCall(server.UniqueName, "/adamantium/test", "adamantium.Test", member, "s",
            writer => writer.WriteString(text));

    private static DBusConnection Session()
    {
        Assume.That(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") ?? Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"),
            Is.Not.Null.And.Not.Empty, "no session bus here");
        return DBusConnection.ConnectSession();
    }
}

using System;
using System.Collections.Generic;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics;

public partial class GraphicsDevice
{
    private readonly List<(CommandBuffer Buffer, ulong Done)> _asideBuffers = new();
    private Semaphore _asideTimeline;
    private ulong _asideSubmitted;

    public void SubmitAside(Action<CommandBuffer> record, Semaphore wait, PipelineStageFlagBits2 waitStage, ulong waitValue,
        IReadOnlyList<(Semaphore Semaphore, ulong Value)> signals)
    {
        RefuseWhileIssuingAside(nameof(SubmitAside));

        var buffer = TakeAsideBuffer();
        buffer.ResetCommandBuffer(0);
        buffer.BeginCommandBuffer(new CommandBufferBeginInfo { Flags = CommandBufferUsageFlagBits.OneTimeSubmitBit });
        record(buffer);
        buffer.EndCommandBuffer();

        _asideSubmitted++;
        var waits = wait == null
            ? Array.Empty<SemaphoreSubmitInfo>()
            : new[] { new SemaphoreSubmitInfo { Semaphore = wait, Value = waitValue, StageMask = (ulong)waitStage } };
        var signalInfos = new SemaphoreSubmitInfo[1 + (signals?.Count ?? 0)];
        signalInfos[0] = new SemaphoreSubmitInfo
        {
            Semaphore = _asideTimeline, Value = _asideSubmitted, StageMask = (ulong)PipelineStageFlagBits2.AllCommandsBit
        };
        for (var i = 1; i < signalInfos.Length; i++)
        {
            var (semaphore, value) = signals[i - 1];
            signalInfos[i] = new SemaphoreSubmitInfo
            {
                Semaphore = semaphore, Value = value, StageMask = (ulong)PipelineStageFlagBits2.AllCommandsBit
            };
        }

        var submit = new SubmitInfo2
        {
            WaitSemaphoreInfoCount = (uint)waits.Length,
            PWaitSemaphoreInfos = waits,
            CommandBufferInfoCount = 1,
            PCommandBufferInfos = new[] { new CommandBufferSubmitInfo { CommandBuffer = buffer } },
            SignalSemaphoreInfoCount = (uint)signalInfos.Length,
            PSignalSemaphoreInfos = signalInfos
        };

        Result result;
        lock (GraphicsQueueLock)
        {
            result = GraphicsQueue.QueueSubmit2(1, submit, null);
        }

        _asideBuffers.Add((buffer, _asideSubmitted));
        MainDevice.Observe(result);
    }

    private CommandBuffer TakeAsideBuffer()
    {
        if (_asideTimeline == null)
        {
            _asideTimeline = LogicalDevice.CreateSemaphore(new SemaphoreCreateInfo
            {
                PNext = new SemaphoreTypeCreateInfo { SemaphoreType = SemaphoreType.Timeline, InitialValue = 0 }
            });
        }

        LogicalDevice.GetSemaphoreCounterValue(_asideTimeline, out var done);
        for (var i = 0; i < _asideBuffers.Count; i++)
        {
            if (_asideBuffers[i].Done <= done)
            {
                var free = _asideBuffers[i].Buffer;
                _asideBuffers.RemoveAt(i);
                return free;
            }
        }

        var allocation = new CommandBufferAllocateInfo
        {
            CommandPool = GraphicsCommandPool, Level = CommandBufferLevel.Primary, CommandBufferCount = 1
        };
        return LogicalDevice.AllocateCommandBuffers(allocation)[0];
    }

    private void FreeAsideBuffers()
    {
        foreach (var (buffer, _) in _asideBuffers)
        {
            LogicalDevice.FreeCommandBuffers(GraphicsCommandPool, 1, buffer);
        }

        _asideBuffers.Clear();
        if (_asideTimeline != null)
        {
            LogicalDevice.DestroySemaphore(_asideTimeline);
            _asideTimeline = null;
        }
    }
}

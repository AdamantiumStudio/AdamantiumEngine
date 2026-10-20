using System;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics;

public partial class GraphicsDevice
{
    private QueryPool _gpuTimestamps;
    private bool[] _gpuTimed;
    private double _gpuTickMs;

    /// <summary>GPU time of the last finished frame, from its first command to its last, in milliseconds; work other
    /// queues ran in between counts too. 0 when the device cannot time.</summary>
    public double LastGpuFrameMs { get; private set; }

    private void StartGpuTimer(CommandBuffer commandBuffer)
    {
        if (_gpuTimestamps == null && !CreateGpuTimestamps()) return;

        var first = CurrentFrame * 2;
        if (_gpuTimed[CurrentFrame])
        {
            // The slot's fence is already waited, so its two stamps are in.
            var result = LogicalDevice.GetQueryPoolResults(_gpuTimestamps, first, 2, 16, out var data, 8,
                QueryResultFlagBits._64Bit);
            if (result == Result.Success)
            {
                var start = BitConverter.ToUInt64(data, 0);
                var end = BitConverter.ToUInt64(data, 8);
                LastGpuFrameMs = end > start ? (end - start) * _gpuTickMs : 0;
            }
        }

        commandBuffer.ResetQueryPool(_gpuTimestamps, first, 2);
        commandBuffer.WriteTimestamp2((ulong)PipelineStageFlagBits2.TopOfPipeBit, _gpuTimestamps, first);
        _gpuTimed[CurrentFrame] = false;
    }

    private void StopGpuTimer(CommandBuffer commandBuffer)
    {
        if (_gpuTimestamps == null) return;

        commandBuffer.WriteTimestamp2((ulong)PipelineStageFlagBits2.BottomOfPipeBit, _gpuTimestamps, CurrentFrame * 2 + 1);
        _gpuTimed[CurrentFrame] = true;
    }

    private bool CreateGpuTimestamps()
    {
        var limits = Adapter.AdapterProperties.Limits;
        if (!limits.TimestampComputeAndGraphics) return false;

        _gpuTickMs = limits.TimestampPeriod / 1e6;
        _gpuTimed = new bool[MaxFramesInFlight];
        var info = new QueryPoolCreateInfo { QueryType = QueryType.Timestamp, QueryCount = MaxFramesInFlight * 2 };
        return LogicalDevice.CreateQueryPool(info, null, out _gpuTimestamps) == Result.Success;
    }

    private void DestroyGpuTimestamps()
    {
        if (_gpuTimestamps == null) return;

        LogicalDevice?.DestroyQueryPool(_gpuTimestamps);
        _gpuTimestamps = null;
    }
}

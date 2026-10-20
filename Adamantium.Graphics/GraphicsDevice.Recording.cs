using System;
using System.Collections.Concurrent;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Effects;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics;

public partial class GraphicsDevice
{
    private readonly ConcurrentQueue<RecordedCommands> _retiredRecordings = new();
    private RecordedCommands _recording;
    private long _recordingEpoch;
    private long _reloadsSeen;
    private bool _hashing;
    private bool _swallowing;
    private ulong _issueHash;

    public bool SupportsRecordedCommands => EnableDynamicRendering && MainDevice.SupportsRecordedCommands;

    public void BeginFingerprint()
    {
        if (_hashing) throw new InvalidOperationException("A fingerprint or a recording is already in progress.");

        ForgetCommandState();
        _swallowing = true;
        StartHash();
    }

    public ulong EndFingerprint()
    {
        if (!_swallowing) throw new InvalidOperationException("No fingerprint is in progress.");

        _swallowing = false;
        _hashing = false;
        ForgetCommandState();
        return _issueHash;
    }

    internal bool IsSwallowing => _swallowing;

    internal bool IsHashing => _hashing;

    private void StartHash()
    {
        _hashing = true;
        _issueHash = 0xCBF29CE484222325UL;
        HashViewportsAndScissors(viewportsArray, scissorsArray);
    }

    internal void Hash(ulong value)
    {
        var h = (_issueHash ^ value) * 0x9E3779B97F4A7C15UL;
        _issueHash = h ^ (h >> 31);
    }

    internal void HashBytes(ReadOnlySpan<byte> bytes)
    {
        var words = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ulong>(bytes);
        foreach (var word in words)
        {
            Hash(word);
        }

        ulong tail = 0;
        for (var i = words.Length * 8; i < bytes.Length; i++)
        {
            tail = (tail << 8) | bytes[i];
        }

        Hash(tail);
        Hash((ulong)bytes.Length);
    }

    private unsafe void HashHandle(void* handle) => Hash((ulong)handle);

    private void HashViewportsAndScissors(ReadOnlySpan<Viewport> viewports, ReadOnlySpan<Rect2D> scissors)
    {
        foreach (var v in viewports)
        {
            HashFloats(v.X, v.Y, v.Width, v.Height);
            HashFloats(v.MinDepth, v.MaxDepth, 0, 0);
        }

        foreach (var s in scissors)
        {
            HashScissor(s);
        }
    }

    private void HashScissor(Rect2D s)
    {
        Hash(((ulong)(uint)s.Offset.X << 32) | (uint)s.Offset.Y);
        Hash(((ulong)s.Extent.Width << 32) | s.Extent.Height);
    }

    private void HashFloats(float a, float b, float c, float d)
    {
        Hash(((ulong)BitConverter.SingleToUInt32Bits(a) << 32) | BitConverter.SingleToUInt32Bits(b));
        Hash(((ulong)BitConverter.SingleToUInt32Bits(c) << 32) | BitConverter.SingleToUInt32Bits(d));
    }

    private void HashDrawState()
    {
        Hash((ulong)(VertexType?.GetHashCode() ?? 0));
        Hash(((ulong)PrimitiveTopology << 32) | (uint)MSAALevel);
        Hash(((ulong)(uint)PolygonMode << 32) | (uint)CullMode);
        Hash(((ulong)(uint)FrontFace << 32) | (uint)DepthCompareFunction);
        Hash((DepthWriteEnable ? 1UL : 0) | (DepthTestEnabled ? 2UL : 0) | (DepthBoundsTestEnabled ? 4UL : 0)
             | (DepthBiasEnabled ? 8UL : 0) | (DepthClampEnable ? 16UL : 0) | (StencilTestEnabled ? 32UL : 0)
             | (LogicOperationsEnabled ? 64UL : 0) | (RasterizerDiscardEnabled ? 128UL : 0)
             | (AlphaToCoverageEnable ? 256UL : 0) | (PrimitiveRestartEnable ? 512UL : 0)
             | ((bool)ColorBlendEnabled ? 1024UL : 0));
        HashFloats(LineWidth, DepthBiasConstantFactor, DepthBiasSlopeFactor, 0);
        Hash(((ulong)(uint)StencilPassOp << 32) | (uint)StencilCompareOp);
        Hash(((ulong)StencilWriteMask << 32) | StencilReference);
        Hash((ulong)(SampleMask?.GetHashCode() ?? 0));
        Hash((ulong)ColorComponentFlags);
        var blend = ColorBlendEquation;
        Hash(((ulong)(uint)blend.SrcColorBlendFactor << 32) | (uint)blend.DstColorBlendFactor);
        Hash(((ulong)(uint)blend.ColorBlendOp << 32) | (uint)blend.SrcAlphaBlendFactor);
        Hash(((ulong)(uint)blend.DstAlphaBlendFactor << 32) | (uint)blend.AlphaBlendOp);
    }

    private void RefuseWhileIssuingAside(string what)
    {
        if (_hashing) throw new InvalidOperationException($"{what} cannot be recorded or fingerprinted.");
    }

    public IRecordedCommands BeginRecording(IRecordedCommands into)
    {
        if (_hashing) throw new InvalidOperationException("A fingerprint or a recording is already in progress.");

        var recording = into as RecordedCommands;
        if (recording == null || recording.Released || !ReferenceEquals(recording.Device, this) || recording.Frame != CurrentFrame)
        {
            var allocation = new CommandBufferAllocateInfo
            {
                CommandPool = GraphicsCommandPool, Level = CommandBufferLevel.Secondary, CommandBufferCount = 1
            };
            recording = new RecordedCommands(this, LogicalDevice.AllocateCommandBuffers(allocation)[0],
                new EffectPass.DynamicBufferPool(this, 128 * 1024), CurrentFrame);
        }
        else
        {
            recording.Buffer.ResetCommandBuffer(0);
            recording.Constants.Reset();
        }

        var colorFormats = new Format[renderTargets.Length];
        for (var i = 0; i < renderTargets.Length; i++)
        {
            colorFormats[i] = renderTargets[i].SurfaceFormat;
        }

        var (resourceHeap, samplerHeap) = ((DescriptorHeapManager)DescriptorHeapManager).HeapBindInfos(this);
        var rendering = new CommandBufferInheritanceRenderingInfo
        {
            Flags = RenderingFlagBits.ContentsInlineBitKhr,
            ColorAttachmentCount = (uint)colorFormats.Length,
            PColorAttachmentFormats = colorFormats,
            DepthAttachmentFormat = depthBuffer?.SurfaceFormat ?? Format.UNDEFINED,
            StencilAttachmentFormat = depthBuffer?.SurfaceFormat ?? Format.UNDEFINED,
            RasterizationSamples = (SampleCountFlagBits)renderTargets[0].MSAALevel,
            PNext = new CommandBufferInheritanceDescriptorHeapInfoEXT
            {
                PResourceHeapBindInfo = resourceHeap, PSamplerHeapBindInfo = samplerHeap
            }
        };
        var begin = new CommandBufferBeginInfo
        {
            Flags = CommandBufferUsageFlagBits.RenderPassContinueBit,
            PInheritanceInfo = new CommandBufferInheritanceInfo { PNext = rendering }
        };

        var result = recording.Buffer.BeginCommandBuffer(begin);
        if (result != Result.Success)
        {
            recording.Dispose();
            throw new InvalidOperationException($"Could not begin a recording: {result}");
        }

        recording.Restart(_recordingEpoch, TargetSignature());
        _recording = recording;
        ForgetCommandState();
        StartHash();
        return recording;
    }

    public ulong EndRecording()
    {
        var recording = _recording ?? throw new InvalidOperationException("No recording is in progress.");
        _recording = null;
        _hashing = false;
        recording.Buffer.EndCommandBuffer();
        recording.Ended = true;
        ForgetCommandState();
        return _issueHash;
    }

    public void ExecuteRecorded(IRecordedCommands commands)
    {
        var recording = (RecordedCommands)commands;
        if (!recording.Ended || recording.Released || !ReferenceEquals(recording.Device, this) || recording.Frame != CurrentFrame)
        {
            throw new ArgumentException("Not a finished recording of this device for this frame slot.", nameof(commands));
        }

        CurrentCommandBuffer.ExecuteCommands(1, recording.Buffer);
        ForgetCommandState();
        DescriptorHeapManager.BindDescriptorHeaps(this);
    }

    private int TargetSignature()
    {
        var hash = new HashCode();
        foreach (var target in renderTargets)
        {
            hash.Add((Format)target.SurfaceFormat);
            hash.Add(target.MSAALevel);
        }

        hash.Add(depthBuffer == null ? Format.UNDEFINED : (Format)depthBuffer.SurfaceFormat);
        return hash.ToHashCode();
    }

    private void ObserveShaderReloads()
    {
        var reloads = MainDevice.ShaderHotReload.ReloadCount;
        if (reloads == _reloadsSeen) return;

        _reloadsSeen = reloads;
        _recordingEpoch++;
    }

    private void ForgetCommandState()
    {
        _stateInitialized = false;
        _depthBiasKnown = false;
        _boundShaderPass = null;
    }

    private void FreeRetiredRecordings()
    {
        while (_retiredRecordings.TryDequeue(out var recording))
        {
            LogicalDevice.FreeCommandBuffers(GraphicsCommandPool, 1, recording.Buffer);
            recording.Constants.Dispose();
        }
    }

    private sealed class RecordedCommands(GraphicsDevice device, CommandBuffer buffer, EffectPass.DynamicBufferPool constants,
        uint frame) : IRecordedCommands
    {
        private long epoch;
        private int targets;

        public GraphicsDevice Device => device;

        public CommandBuffer Buffer => buffer;

        public EffectPass.DynamicBufferPool Constants => constants;

        public uint Frame => frame;

        public bool Ended { get; set; }

        public bool Released { get; private set; }

        public bool IsReusable =>
            Ended && !Released && epoch == device._recordingEpoch && targets == device.TargetSignature();

        public void Restart(long recordingEpoch, int targetSignature)
        {
            epoch = recordingEpoch;
            targets = targetSignature;
            Ended = false;
        }

        public void Dispose()
        {
            if (Released) return;

            Released = true;
            device.AddToDeferDisposeQueue(new Release(this));
        }

        private sealed class Release(RecordedCommands commands) : IDisposable
        {
            public void Dispose() => commands.Device._retiredRecordings.Enqueue(commands);
        }
    }
}
